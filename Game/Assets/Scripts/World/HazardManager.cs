using System.Collections.Generic;
using UnityEngine;

/// <summary>场上危险物：香蕉皮 / 定时炸弹 / 地雷 / 汽油火焰地块。</summary>
public class HazardManager : MonoBehaviour
{
    public static HazardManager Instance;

    public class BananaTrap
    {
        public Vector2Int Cell;
        public RoleType Thrower;
        public GameObject Visual;
    }

    public class TimedBombHazard
    {
        public Vector2Int Cell;
        /// <summary>剩余行动数（安放时 = 所选回合×4；每次行动结束 −1，含安放当次）。</summary>
        public int ActionsLeft;
        public RoleType Owner;
        public GameObject Visual;
    }

    public class MineHazard
    {
        public Vector2Int Cell;
        public GameObject Visual;
    }

    public class FlamePatch
    {
        public Vector2Int Cell;
        /// <summary>剩余行动数（铺设时 = 规则回合×4；每次行动结束 −1）。</summary>
        public int ActionsLeft;
        public GameObject Visual;
    }

    private readonly List<BananaTrap> bananas = new List<BananaTrap>();
    private readonly List<TimedBombHazard> timedBombs = new List<TimedBombHazard>();
    private readonly List<MineHazard> mines = new List<MineHazard>();
    private readonly List<FlamePatch> flames = new List<FlamePatch>();

    private static readonly Color HazardRed = new Color(0.92f, 0.22f, 0.18f, 1f);
    private static readonly Color HazardRedBorder = new Color(0.45f, 0.08f, 0.06f, 1f);
    private static readonly Color FlameCore = new Color(1f, 0.45f, 0.08f, 0.88f);
    private static readonly Color FlameEdge = new Color(0.95f, 0.15f, 0.05f, 0.55f);
    private static readonly Color FlameGlow = new Color(1f, 0.75f, 0.2f, 0.35f);

    private void Awake() => Instance = this;

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public void ClearAll()
    {
        foreach (var b in bananas)
            if (b.Visual != null) Destroy(b.Visual);
        foreach (var t in timedBombs)
            if (t.Visual != null) Destroy(t.Visual);
        foreach (var m in mines)
            if (m.Visual != null) Destroy(m.Visual);
        foreach (var f in flames)
            if (f.Visual != null) Destroy(f.Visual);
        bananas.Clear();
        timedBombs.Clear();
        mines.Clear();
        flames.Clear();
    }

    public void ClearAllFlames(string reason = null)
    {
        var cells = new List<Vector2Int>(flames.Count);
        for (int i = flames.Count - 1; i >= 0; i--)
        {
            cells.Add(flames[i].Cell);
            if (flames[i].Visual != null)
                Destroy(flames[i].Visual);
            flames.RemoveAt(i);
        }
        for (int i = 0; i < cells.Count; i++)
            NotifyJungleStealthAt(cells[i]);
        if (!string.IsNullOrEmpty(reason))
            TurnManager.Instance?.Log(reason);
    }

    public bool HasFlameAt(Vector2Int cell)
    {
        for (int i = 0; i < flames.Count; i++)
        {
            if (flames[i].Cell == cell)
                return true;
        }
        return false;
    }

    public void PlaceBananaTrap(Vector2Int cell, RoleType thrower)
    {
        var trap = new BananaTrap { Cell = cell, Thrower = thrower };
        trap.Visual = CreateRedMarker(cell, "BananaTrap");
        bananas.Add(trap);
        RefreshHazardVisibility();
    }

    public void PlaceTimedBomb(Vector2Int cell, int rounds, RoleType owner)
    {
        int actions = Mathf.Clamp(rounds, 1, 5) * 4;
        var bomb = new TimedBombHazard { Cell = cell, ActionsLeft = actions, Owner = owner };
        bomb.Visual = CreateRedMarker(cell, "TimedBomb");
        var labelGo = new GameObject("Label");
        labelGo.transform.SetParent(bomb.Visual.transform, false);
        labelGo.transform.localPosition = new Vector3(0f, 0.55f, 0f);
        labelGo.transform.localScale = Vector3.one * 2.2f;
        var tm = labelGo.AddComponent<TextMesh>();
        tm.text = actions.ToString();
        tm.characterSize = 0.12f;
        tm.fontSize = 48;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.color = Color.white;
        var mr = labelGo.GetComponent<MeshRenderer>();
        if (mr != null) mr.sortingOrder = 6;
        timedBombs.Add(bomb);
        RefreshHazardVisibility();
    }

    public void PlaceMine(Vector2Int cell)
    {
        for (int i = 0; i < mines.Count; i++)
        {
            if (mines[i].Cell == cell)
                return;
        }
        var mine = new MineHazard { Cell = cell };
        mine.Visual = CreateRedMarker(cell, "Mine");
        mines.Add(mine);
    }

    /// <summary>在爆点半径内铺火焰，持续 rounds 个完整回合（实际 = rounds×4 个行动）；熔岩/冰地不铺；同格刷新为较长剩余。</summary>
    public void PlaceFlameArea(Vector2Int center, int radius, int rounds)
    {
        if (WeatherService.BlocksBurning)
            return;
        var grid = GridManager.Instance;
        if (grid == null || rounds <= 0)
            return;

        int actions = rounds * 4;
        for (int x = 0; x < grid.gridWidth; x++)
        {
            for (int y = 0; y < grid.gridHeight; y++)
            {
                var cell = new Vector2Int(x, y);
                if (grid.GetManhattanDistance(center, cell) > radius)
                    continue;
                if (grid.GetTileType(cell) == TileType.Lava)
                    continue;
                if (grid.GetTileType(cell) == TileType.Ice)
                    continue;
                UpsertFlame(cell, actions);
            }
        }
        RefreshHazardVisibility();
    }

    /// <summary>
    /// 沿四向直线铺火焰（不含起点）：最多 maxSteps 格，持续 rounds 个完整回合（实际 = rounds×4 个行动）；熔岩/冰地跳过。
    /// </summary>
    public void PlaceFlameLine(Vector2Int origin, Vector2Int stepDir, int maxSteps, int rounds)
    {
        if (WeatherService.BlocksBurning)
            return;
        var grid = GridManager.Instance;
        if (grid == null || rounds <= 0 || maxSteps <= 0)
            return;
        if (stepDir == Vector2Int.zero)
            return;

        int actions = rounds * 4;
        for (int step = 1; step <= maxSteps; step++)
        {
            var cell = origin + stepDir * step;
            if (!grid.IsValidCell(cell))
                break;
            if (grid.GetTileType(cell) == TileType.Lava)
                continue;
            if (grid.GetTileType(cell) == TileType.Ice)
                continue;
            UpsertFlame(cell, actions);
        }
        RefreshHazardVisibility();
    }

    private void UpsertFlame(Vector2Int cell, int actions)
    {
        var grid = GridManager.Instance;
        if (grid != null)
        {
            var tile = grid.GetTileType(cell);
            if (tile == TileType.Lava || tile == TileType.Ice)
                return;
        }

        bool existed = false;
        for (int i = 0; i < flames.Count; i++)
        {
            if (flames[i].Cell != cell)
                continue;
            var f = flames[i];
            f.ActionsLeft = Mathf.Max(f.ActionsLeft, actions);
            flames[i] = f;
            existed = true;
            break;
        }

        if (!existed)
        {
            flames.Add(new FlamePatch
            {
                Cell = cell,
                ActionsLeft = actions,
                Visual = CreateFlameVisual(cell)
            });
        }

        // 有火的丛林不再提供隐匿
        NotifyJungleStealthAt(cell);
    }

    private static void NotifyJungleStealthAt(Vector2Int cell)
    {
        var occ = StealthService.GetOccupantUnit(cell);
        occ?.SyncJungleStealthFromTerrain();
    }

    private GameObject CreateFlameVisual(Vector2Int cell)
    {
        var grid = GridManager.Instance;
        var go = new GameObject("Flame");
        go.transform.SetParent(transform, false);
        Vector3 pos = grid != null ? grid.CellToWorld(cell) : Vector3.zero;
        go.transform.position = pos + new Vector3(0f, 0.05f, 0f);

        var glow = new GameObject("Glow");
        glow.transform.SetParent(go.transform, false);
        glow.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        glow.transform.localScale = Vector3.one * 0.95f;
        var gsr = glow.AddComponent<SpriteRenderer>();
        gsr.sprite = SpriteFactory.CreateCircleSprite(FlameGlow, FlameEdge, 32, 0.48f);
        gsr.sortingOrder = grid != null ? grid.GetSortOrder(cell, 20) : 20;

        var body = new GameObject("Body");
        body.transform.SetParent(go.transform, false);
        body.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        body.transform.localPosition = new Vector3(0f, 0.02f, 0f);
        body.transform.localScale = Vector3.one * 0.72f;
        var bsr = body.AddComponent<SpriteRenderer>();
        bsr.sprite = SpriteFactory.CreateBorderedSprite(FlameCore, FlameEdge, 24, 3);
        bsr.sortingOrder = grid != null ? grid.GetSortOrder(cell, 21) : 21;

        var tongue = new GameObject("Tongue");
        tongue.transform.SetParent(go.transform, false);
        tongue.transform.localPosition = new Vector3(0f, 0.28f, 0f);
        tongue.transform.localScale = new Vector3(0.35f, 0.55f, 0.35f);
        var tsr = tongue.AddComponent<SpriteRenderer>();
        tsr.sprite = SpriteFactory.CreateBorderedSprite(
            new Color(1f, 0.85f, 0.25f, 0.95f),
            new Color(1f, 0.35f, 0.05f, 0.9f),
            16, 2);
        tsr.sortingOrder = grid != null ? grid.GetSortOrder(cell, 36) : 36;
        tongue.AddComponent<CameraBillboard>();

        go.AddComponent<FlameFlicker>();
        return go;
    }

    private GameObject CreateRedMarker(Vector2Int cell, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var grid = GridManager.Instance;
        // 西北角偏移（XZ）
        go.transform.position = grid.CellToWorld(cell) + new Vector3(-0.32f, 0.04f, 0.32f);
        go.transform.localScale = Vector3.one * 0.28f;
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = SpriteFactory.CreateBorderedSprite(HazardRed, HazardRedBorder, 16, 2);
        sr.sortingOrder = grid.GetSortOrder(cell, 38);
        go.AddComponent<CameraBillboard>();
        return go;
    }

    /// <summary>香蕉皮与定时炸弹仅对放置者可见，且须在迷雾视野内；地雷全员可见但受迷雾限制。</summary>
    public void RefreshHazardVisibility()
    {
        var fogViewer = VisibilityService.GetFogViewer();
        // 热座：当前行动者；AI 对战/管理员：始终按玩家角色视野（荣誉：只看见自己的陷阱）
        var ownerViewer = MatchConfig.IsAiBattle ? fogViewer : TurnManager.Instance?.CurrentUnit;
        foreach (var b in bananas)
        {
            if (b.Visual == null) continue;
            bool ownerSee = ownerViewer != null && ownerViewer.Role == b.Thrower;
            bool inFog = fogViewer == null || VisibilityService.CanSeeCell(fogViewer, b.Cell);
            b.Visual.SetActive(ownerSee && inFog);
        }
        foreach (var bomb in timedBombs)
        {
            if (bomb.Visual == null) continue;
            bool ownerSee = ownerViewer != null && ownerViewer.Role == bomb.Owner;
            bool inFog = fogViewer == null || VisibilityService.CanSeeCell(fogViewer, bomb.Cell);
            bomb.Visual.SetActive(ownerSee && inFog);
        }
        foreach (var m in mines)
        {
            if (m.Visual == null) continue;
            bool inFog = fogViewer == null || VisibilityService.CanSeeCell(fogViewer, m.Cell);
            m.Visual.SetActive(inFog);
        }
        foreach (var f in flames)
        {
            if (f.Visual == null) continue;
            bool inFog = fogViewer == null || VisibilityService.CanSeeCell(fogViewer, f.Cell);
            f.Visual.SetActive(inFog);
        }
    }

    /// <summary>兼容旧调用名。</summary>
    public void RefreshBananaVisibility() => RefreshHazardVisibility();

    public bool CanSeeBananaAt(Vector2Int cell, UnitActor viewer)
    {
        if (viewer == null) return false;
        foreach (var b in bananas)
        {
            if (b.Cell == cell && b.Thrower == viewer.Role)
                return true;
        }
        return false;
    }

    public bool CanSeeTimedBombAt(Vector2Int cell, UnitActor viewer)
    {
        if (viewer == null) return false;
        foreach (var bomb in timedBombs)
        {
            if (bomb.Cell == cell && bomb.Owner == viewer.Role)
                return true;
        }
        return false;
    }

    /// <summary>悬停用：该格可见危险物摘要，无则 null。</summary>
    public string DescribeHazards(Vector2Int cell, UnitActor viewer)
    {
        var lines = new List<string>();

        foreach (var bomb in timedBombs)
        {
            if (bomb.Cell != cell) continue;
            if (viewer == null || bomb.Owner != viewer.Role)
                continue;
            lines.Add($"定时炸弹（你放置）：{bomb.ActionsLeft} 个行动后爆炸（半径4）");
        }

        foreach (var mine in mines)
        {
            if (mine.Cell != cell) continue;
            lines.Add("武装地雷：踩上受到 15 点法伤");
        }

        foreach (var b in bananas)
        {
            if (b.Cell != cell) continue;
            if (viewer == null || b.Thrower != viewer.Role)
                continue;
            lines.Add($"香蕉皮陷阱（你放置）：别人踩上会跌倒");
        }

        foreach (var f in flames)
        {
            if (f.Cell != cell) continue;
            lines.Add($"火焰：剩余 {f.ActionsLeft} 个行动；身处立刻着火");
        }

        if (lines.Count == 0)
            return null;
        return string.Join("\n", lines);
    }

    public bool HasVisibleMarkerAt(Vector2Int cell, UnitActor viewer)
    {
        foreach (var mine in mines)
            if (mine.Cell == cell) return true;
        if (HasFlameAt(cell)) return true;
        return CanSeeBananaAt(cell, viewer) || CanSeeTimedBombAt(cell, viewer);
    }

    /// <summary>
    /// 移动踩踏语义：仅在移动落地后结算。行动开始时已站在陷阱上不触发。
    /// </summary>
    public void ResolveTrapsAfterMove(UnitActor unit)
    {
        if (unit == null || unit.IsDead)
            return;
        ResolveMineOnStep(unit);
        if (unit.IsDead)
            return;
        ResolveBananaOnStep(unit);
        if (unit.IsDead)
            return;
        ResolveFlameOnCell(unit);
    }

    /// <summary>身处火焰格立刻获得着火（雨天无法着火）。</summary>
    public void ResolveFlameOnCell(UnitActor unit)
    {
        if (unit == null || unit.IsDead || unit.IsDying)
            return;
        if (!HasFlameAt(unit.Cell))
            return;
        if (WeatherService.BlocksBurning)
            return;
        if (unit.HasStatus(StatusType.Burning))
            return;
        unit.ApplyStatus(StatusType.Burning, 1, unit);
        TurnManager.Instance?.Log(
            $"🔥 {RoleInfo.GetDisplayName(unit.Role)} 身处火焰，获得「着火」");
    }

    /// <summary>行动开始时：站在火焰上则补着火。</summary>
    public void ApplyFlameBurningAtTurnStart(UnitActor unit) => ResolveFlameOnCell(unit);

    /// <summary>踩踏香蕉皮：投掷者自己踩不触发；濒死无法获得跌倒则不消耗。</summary>
    public void ResolveBananaOnStep(UnitActor unit)
    {
        if (unit == null || unit.IsDead || unit.IsDying)
            return;

        for (int i = bananas.Count - 1; i >= 0; i--)
        {
            if (bananas[i].Cell != unit.Cell)
                continue;
            // 投掷者踩自己的皮不触发
            if (bananas[i].Thrower == unit.Role)
                continue;

            TurnManager.Instance?.Log(
                $"⚠ {RoleInfo.GetDisplayName(unit.Role)} 踩到了香蕉皮！");
            // 施加者=踩到者自己，持续按本人行动开始倒数
            unit.ApplyStatus(StatusType.Trip, 3, unit);
            DeckManager.Instance?.AddToDiscard(ItemKind.BananaPeel);
            if (bananas[i].Visual != null)
                Destroy(bananas[i].Visual);
            bananas.RemoveAt(i);
        }
    }

    public void ResolveMineOnStep(UnitActor unit)
    {
        if (unit == null || unit.IsDead)
            return;

        for (int i = mines.Count - 1; i >= 0; i--)
        {
            if (mines[i].Cell != unit.Cell)
                continue;

            int dealt = unit.TakeDamage(15, magicDamage: true, fromBombOrMine: true);
            TurnManager.Instance?.Log(
                $"⚠ {RoleInfo.GetDisplayName(unit.Role)} 踩到地雷，受到 {dealt} 点法伤");
            DeckManager.Instance?.AddToDiscard(ItemKind.Mine);
            if (mines[i].Visual != null)
                Destroy(mines[i].Visual);
            mines.RemoveAt(i);
            GameManager.Instance?.CheckWinConditions();
            break;
        }
    }

    /// <summary>行动结束：火焰剩余行动 −1，耗尽移除。</summary>
    public void TickFlamesOnActionEnd()
    {
        for (int i = flames.Count - 1; i >= 0; i--)
        {
            var f = flames[i];
            f.ActionsLeft--;
            if (f.ActionsLeft > 0)
            {
                flames[i] = f;
                continue;
            }
            var cell = f.Cell;
            if (f.Visual != null)
                Destroy(f.Visual);
            flames.RemoveAt(i);
            NotifyJungleStealthAt(cell);
        }
    }

    /// <summary>
    /// 每次行动结束结算：倒计时−1（安放时为所选回合×4，含安放当次行动），到 0 则爆炸。
    /// </summary>
    public void TickTimedBombsOnActionEnd()
    {
        for (int i = timedBombs.Count - 1; i >= 0; i--)
        {
            var bomb = timedBombs[i];
            bomb.ActionsLeft--;
            if (bomb.Visual != null)
            {
                var tm = bomb.Visual.GetComponentInChildren<TextMesh>();
                if (tm != null)
                    tm.text = Mathf.Max(0, bomb.ActionsLeft).ToString();
            }

            if (bomb.ActionsLeft > 0)
            {
                timedBombs[i] = bomb;
                continue;
            }

            ExplodeTimedBomb(bomb);
            if (bomb.Visual != null)
                Destroy(bomb.Visual);
            timedBombs.RemoveAt(i);
            DeckManager.Instance?.AddToDiscard(ItemKind.TimedBomb);
        }
    }

    private void ExplodeTimedBomb(TimedBombHazard bomb)
    {
        var grid = GridManager.Instance;
        int hits = 0, damaged = 0;
        foreach (var other in GameManager.Instance.Units)
        {
            if (other == null || other.IsDead)
                continue;
            if (grid.GetManhattanDistance(bomb.Cell, other.Cell) <= 4)
            {
                hits++;
                if (other.TakeDamage(15, magicDamage: false) > 0)
                    damaged++;
            }
        }
        TurnManager.Instance?.Log(
            $"定时炸弹在 ({bomb.Cell.x},{bomb.Cell.y}) 爆炸！覆盖 {hits} 人，{damaged} 人扣血");
        GameManager.Instance?.CheckWinConditions();
    }

    public void SwallowCells(List<Vector2Int> cells)
    {
        if (cells == null) return;
        var set = new HashSet<Vector2Int>(cells);
        for (int i = bananas.Count - 1; i >= 0; i--)
        {
            if (!set.Contains(bananas[i].Cell)) continue;
            DeckManager.Instance?.AddToDiscard(ItemKind.BananaPeel);
            if (bananas[i].Visual != null) Destroy(bananas[i].Visual);
            bananas.RemoveAt(i);
        }
        for (int i = timedBombs.Count - 1; i >= 0; i--)
        {
            if (!set.Contains(timedBombs[i].Cell)) continue;
            DeckManager.Instance?.AddToDiscard(ItemKind.TimedBomb);
            if (timedBombs[i].Visual != null) Destroy(timedBombs[i].Visual);
            timedBombs.RemoveAt(i);
        }
        for (int i = mines.Count - 1; i >= 0; i--)
        {
            if (!set.Contains(mines[i].Cell)) continue;
            DeckManager.Instance?.AddToDiscard(ItemKind.Mine);
            if (mines[i].Visual != null) Destroy(mines[i].Visual);
            mines.RemoveAt(i);
        }
        for (int i = flames.Count - 1; i >= 0; i--)
        {
            if (!set.Contains(flames[i].Cell)) continue;
            var cell = flames[i].Cell;
            if (flames[i].Visual != null) Destroy(flames[i].Visual);
            flames.RemoveAt(i);
            NotifyJungleStealthAt(cell);
        }
    }
}
