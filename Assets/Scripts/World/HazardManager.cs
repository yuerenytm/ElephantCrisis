using System.Collections.Generic;
using UnityEngine;

/// <summary>场上危险物：投掷香蕉皮 / 定时炸弹（仅放置者可见）、地雷（全员可见）。</summary>
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
        public int RoundsLeft;
        public RoleType Owner;
        public GameObject Visual;
    }

    public class MineHazard
    {
        public Vector2Int Cell;
        public GameObject Visual;
    }

    private readonly List<BananaTrap> bananas = new List<BananaTrap>();
    private readonly List<TimedBombHazard> timedBombs = new List<TimedBombHazard>();
    private readonly List<MineHazard> mines = new List<MineHazard>();

    private static readonly Color HazardRed = new Color(0.92f, 0.22f, 0.18f, 1f);
    private static readonly Color HazardRedBorder = new Color(0.45f, 0.08f, 0.06f, 1f);

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
        bananas.Clear();
        timedBombs.Clear();
        mines.Clear();
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
        var bomb = new TimedBombHazard { Cell = cell, RoundsLeft = rounds, Owner = owner };
        bomb.Visual = CreateRedMarker(cell, "TimedBomb");
        var labelGo = new GameObject("Label");
        labelGo.transform.SetParent(bomb.Visual.transform, false);
        labelGo.transform.localPosition = new Vector3(0f, 0.55f, 0f);
        labelGo.transform.localScale = Vector3.one * 2.2f;
        var tm = labelGo.AddComponent<TextMesh>();
        tm.text = rounds.ToString();
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

    private GameObject CreateRedMarker(Vector2Int cell, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.transform.position = GridManager.Instance.CellToWorld(cell) + new Vector3(-0.32f, 0.32f, 0f);
        go.transform.localScale = Vector3.one * 0.28f;
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = SpriteFactory.CreateBorderedSprite(HazardRed, HazardRedBorder, 16, 2);
        sr.sortingOrder = 5;
        return go;
    }

    /// <summary>香蕉皮与定时炸弹仅对放置者可见。</summary>
    public void RefreshHazardVisibility()
    {
        var viewer = TurnManager.Instance?.CurrentUnit;
        foreach (var b in bananas)
        {
            if (b.Visual == null) continue;
            bool show = viewer != null && viewer.Role == b.Thrower;
            b.Visual.SetActive(show);
        }
        foreach (var bomb in timedBombs)
        {
            if (bomb.Visual == null) continue;
            bool show = viewer != null && viewer.Role == bomb.Owner;
            bomb.Visual.SetActive(show);
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
            lines.Add($"定时炸弹（你放置）：{bomb.RoundsLeft} 完整回合后爆炸（半径4）");
        }

        foreach (var mine in mines)
        {
            if (mine.Cell != cell) continue;
            lines.Add("地雷：踩上受到 15 点真伤");
        }

        foreach (var b in bananas)
        {
            if (b.Cell != cell) continue;
            if (viewer == null || b.Thrower != viewer.Role)
                continue;
            lines.Add($"香蕉皮陷阱（你放置）：别人踩上会跌倒");
        }

        if (lines.Count == 0)
            return null;
        return string.Join("\n", lines);
    }

    public bool HasVisibleMarkerAt(Vector2Int cell, UnitActor viewer)
    {
        foreach (var mine in mines)
            if (mine.Cell == cell) return true;
        return CanSeeBananaAt(cell, viewer) || CanSeeTimedBombAt(cell, viewer);
    }

    public void ResolveBananaAtTurnStart(UnitActor unit)
    {
        if (unit == null || unit.IsDead || unit.IsDying)
            return;

        for (int i = bananas.Count - 1; i >= 0; i--)
        {
            if (bananas[i].Cell != unit.Cell)
                continue;

            TurnManager.Instance?.Log(
                $"⚠ {RoleInfo.GetDisplayName(unit.Role)} 踩到了香蕉皮！（回合开始）");
            unit.ApplyStatus(StatusType.Trip, 3);
            DeckManager.Instance?.AddToDiscard(ItemKind.BananaPeel);
            if (bananas[i].Visual != null)
                Destroy(bananas[i].Visual);
            bananas.RemoveAt(i);
        }
    }

    public void ResolveMineAt(UnitActor unit)
    {
        if (unit == null || unit.IsDead)
            return;

        for (int i = mines.Count - 1; i >= 0; i--)
        {
            if (mines[i].Cell != unit.Cell)
                continue;

            int dealt = unit.TakeDamage(15, trueDamage: true);
            TurnManager.Instance?.Log(
                $"⚠ {RoleInfo.GetDisplayName(unit.Role)} 踩到地雷，受到 {dealt} 点真伤");
            DeckManager.Instance?.AddToDiscard(ItemKind.Mine);
            if (mines[i].Visual != null)
                Destroy(mines[i].Visual);
            mines.RemoveAt(i);
            GameManager.Instance?.CheckWinConditions();
            break;
        }
    }

    /// <summary>
    /// 完整一轮结束时结算（猫行动结束、下一轮对象开始前）：倒计时−1，到 0 则爆炸。
    /// </summary>
    public void TickTimedBombsOnFullRound()
    {
        for (int i = timedBombs.Count - 1; i >= 0; i--)
        {
            var bomb = timedBombs[i];
            bomb.RoundsLeft--;
            if (bomb.Visual != null)
            {
                var tm = bomb.Visual.GetComponentInChildren<TextMesh>();
                if (tm != null)
                    tm.text = Mathf.Max(0, bomb.RoundsLeft).ToString();
            }

            if (bomb.RoundsLeft > 0)
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
                if (other.TakeDamage(15, trueDamage: false) > 0)
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
    }
}
