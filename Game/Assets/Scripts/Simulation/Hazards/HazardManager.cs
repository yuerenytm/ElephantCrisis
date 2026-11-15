using System.Collections.Generic;

/// <summary>
/// 场上危险物：香蕉皮 / 定时炸弹 / 地雷 / 汽油火焰地块。
/// </summary>
public class HazardManager
{
    public static HazardManager Instance;

    public class BananaTrap
    {
        public Int2 Cell;
        public RoleType Thrower;
    }

    public class TimedBombHazard
    {
        public Int2 Cell;
        /// <summary>
        /// 剩余行动数（安放时 = 所选回合×4；每次行动结束 −1，含安放当次）。
        /// </summary>
        public int ActionsLeft;
        public RoleType Owner;
    }

    public class MineHazard
    {
        public Int2 Cell;
    }

    public class FlamePatch
    {
        public Int2 Cell;
        /// <summary>
        /// 剩余行动数（铺设时 = 规则回合×4；每次行动结束 −1）。
        /// </summary>
        public int ActionsLeft;
    }

    private readonly List<BananaTrap> bananas = new List<BananaTrap>();
    private readonly List<TimedBombHazard> timedBombs = new List<TimedBombHazard>();
    private readonly List<MineHazard> mines = new List<MineHazard>();
    private readonly List<FlamePatch> flames = new List<FlamePatch>();



    /// <summary>视觉对象注册表：不挂在逻辑数据结构上，避免"画面当数据"。</summary>
    private readonly Dictionary<object, int> visuals = new Dictionary<object, int>();

    /// <summary>登记某条危险物对应的视觉对象。</summary>
    private T TrackVisual<T>(T hazard, int visual) where T : class
    {
        if (visual != 0)
            visuals[hazard] = visual;
        return hazard;
    }

    /// <summary>取视觉对象（无头模式下为 null）。</summary>
    private int VisualOf(object hazard)
        => hazard != null && visuals.TryGetValue(hazard, out int handle) ? handle : 0;

    /// <summary>销毁并移除视觉对象。</summary>
    private void UntrackVisual(object hazard)
    {
        if (hazard == null || !visuals.TryGetValue(hazard, out int handle))
            return;
        Presentation.Current.DestroyVisual(handle);
        visuals.Remove(hazard);
    }



    /// <summary>
    /// 清空全部危险物（视觉 + 数据）。
    /// </summary>
    public void ClearAll()
    {
        foreach (var b in bananas)
            UntrackVisual(b);
        foreach (var t in timedBombs)
            UntrackVisual(t);
        foreach (var m in mines)
            UntrackVisual(m);
        foreach (var f in flames)
            UntrackVisual(f);
        visuals.Clear();
        bananas.Clear();
        timedBombs.Clear();
        mines.Clear();
        flames.Clear();
    }

    /// <summary>
    /// 扑灭全部火焰（下雨时调用）。
    /// </summary>
    public void ClearAllFlames(string reason = null)
    {
        var cells = new List<Int2>(flames.Count);
        for (int i = flames.Count - 1; i >= 0; i--)
        {
            cells.Add(flames[i].Cell);
            UntrackVisual(flames[i]);
            flames.RemoveAt(i);
        }
        for (int i = 0; i < cells.Count; i++)
            NotifyJungleStealthAt(cells[i]);
        if (!string.IsNullOrEmpty(reason))
            TurnManager.Instance?.Log(reason);
    }

    /// <summary>
    /// 该格是否有火焰。
    /// </summary>
    public bool HasFlameAt(Int2 cell)
    {
        for (int i = 0; i < flames.Count; i++)
        {
            if (flames[i].Cell == cell)
                return true;
        }
        return false;
    }

    /// <summary>
    /// 放置香蕉皮陷阱。
    /// </summary>
    public void PlaceBananaTrap(Int2 cell, RoleType thrower)
    {
        var trap = new BananaTrap { Cell = cell, Thrower = thrower };
        TrackVisual(trap, Presentation.Current.CreateMarkerVisual(cell, "BananaTrap"));
        bananas.Add(trap);
        RefreshHazardVisibility();
    }

    /// <summary>
    /// 放置定时炸弹（回合数会换算成行动数）。
    /// </summary>
    public void PlaceTimedBomb(Int2 cell, int rounds, RoleType owner)
    {
        int actions = System.Math.Clamp(rounds, GameRulesConfig.TimedBombMinRounds, GameRulesConfig.TimedBombMaxRounds) * GameRulesConfig.ActionsPerRound;
        var bomb = new TimedBombHazard { Cell = cell, ActionsLeft = actions, Owner = owner };
        TrackVisual(bomb, Presentation.Current.CreateMarkerVisual(cell, "TimedBomb"));
        // 倒计时标签纯属表现：无头模式没有 Visual，跳过即可（不影响回合数）
        Presentation.Current.UpdateMarkerLabel(VisualOf(bomb), actions);
        timedBombs.Add(bomb);
        RefreshHazardVisibility();
    }

    /// <summary>
    /// 安放地雷。
    /// </summary>
    public void PlaceMine(Int2 cell)
    {
        for (int i = 0; i < mines.Count; i++)
        {
            if (mines[i].Cell == cell)
                return;
        }
        var mine = new MineHazard { Cell = cell };
        TrackVisual(mine, Presentation.Current.CreateMarkerVisual(cell, "Mine"));
        mines.Add(mine);
    }

    /// <summary>
    /// 在爆点半径内铺火焰，持续 rounds 个完整回合（实际 = rounds×4 个行动）；熔岩/冰地不铺；同格刷新为较长剩余。
    /// </summary>
    public void PlaceFlameArea(Int2 center, int radius, int rounds)
    {
        if (WeatherService.BlocksBurning)
            return;
        var grid = GridManager.Instance;
        if (grid == null || rounds <= 0)
            return;

        int actions = rounds * GameRulesConfig.ActionsPerRound;
        for (int x = 0; x < grid.gridWidth; x++)
        {
            for (int y = 0; y < grid.gridHeight; y++)
            {
                var cell = new Int2(x, y);
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
    public void PlaceFlameLine(Int2 origin, Int2 stepDir, int maxSteps, int rounds)
    {
        if (WeatherService.BlocksBurning)
            return;
        var grid = GridManager.Instance;
        if (grid == null || rounds <= 0 || maxSteps <= 0)
            return;
        if (stepDir == Int2.zero)
            return;

        int actions = rounds * GameRulesConfig.ActionsPerRound;
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

    private void UpsertFlame(Int2 cell, int actions)
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
            f.ActionsLeft = System.Math.Max(f.ActionsLeft, actions);
            flames[i] = f;
            existed = true;
            break;
        }

        if (!existed)
        {
            flames.Add(TrackVisual(new FlamePatch { Cell = cell, ActionsLeft = actions }, Presentation.Current.CreateFlameVisual(cell)));
        }

        // 有火的丛林不再提供隐匿
        NotifyJungleStealthAt(cell);
    }

    private static void NotifyJungleStealthAt(Int2 cell)
    {
        var occ = StealthService.GetOccupantUnit(cell);
        occ?.SyncJungleStealthFromTerrain();
    }





    /// <summary>
    /// 香蕉皮与定时炸弹仅对放置者可见，且须在迷雾视野内；地雷全员可见但受迷雾限制。
    /// </summary>
    public void RefreshHazardVisibility()
    {
        var fogViewer = VisibilityService.GetFogViewer();
        // 热座：当前行动者；AI 对战/管理员：始终按玩家角色视野（荣誉：只看见自己的陷阱）
        var ownerViewer = MatchConfig.IsAiBattle ? fogViewer : TurnManager.Instance?.CurrentUnit;
        foreach (var b in bananas)
        {
            if (VisualOf(b) == 0) continue;
            bool ownerSee = ownerViewer != null && ownerViewer.Role == b.Thrower;
            bool inFog = fogViewer == null || VisibilityService.CanSeeCell(fogViewer, b.Cell);
            Presentation.Current.SetVisualActive(VisualOf(b), ownerSee && inFog);
        }
        foreach (var bomb in timedBombs)
        {
            if (VisualOf(bomb) == 0) continue;
            bool ownerSee = ownerViewer != null && ownerViewer.Role == bomb.Owner;
            bool inFog = fogViewer == null || VisibilityService.CanSeeCell(fogViewer, bomb.Cell);
            Presentation.Current.SetVisualActive(VisualOf(bomb), ownerSee && inFog);
        }
        foreach (var m in mines)
        {
            if (VisualOf(m) == 0) continue;
            bool inFog = fogViewer == null || VisibilityService.CanSeeCell(fogViewer, m.Cell);
            Presentation.Current.SetVisualActive(VisualOf(m), inFog);
        }
        foreach (var f in flames)
        {
            if (VisualOf(f) == 0) continue;
            bool inFog = fogViewer == null || VisibilityService.CanSeeCell(fogViewer, f.Cell);
            Presentation.Current.SetVisualActive(VisualOf(f), inFog);
        }
    }

    /// <summary>
    /// 兼容旧调用名。
    /// </summary>
    public void RefreshBananaVisibility() => RefreshHazardVisibility();

    /// <summary>
    /// 视野角色能否看见该格的香蕉皮。
    /// </summary>
    public bool CanSeeBananaAt(Int2 cell, UnitActor viewer)
    {
        if (viewer == null) return false;
        foreach (var b in bananas)
        {
            if (b.Cell == cell && b.Thrower == viewer.Role)
                return true;
        }
        return false;
    }

    /// <summary>
    /// 视野角色能否看见该格的定时炸弹。
    /// </summary>
    public bool CanSeeTimedBombAt(Int2 cell, UnitActor viewer)
    {
        if (viewer == null) return false;
        foreach (var bomb in timedBombs)
        {
            if (bomb.Cell == cell && bomb.Owner == viewer.Role)
                return true;
        }
        return false;
    }

    /// <summary>
    /// 悬停用：该格可见危险物摘要，无则 null。
    /// </summary>
    public string DescribeHazards(Int2 cell, UnitActor viewer)
    {
        var lines = new List<string>();

        foreach (var bomb in timedBombs)
        {
            if (bomb.Cell != cell) continue;
            if (viewer == null || bomb.Owner != viewer.Role)
                continue;
            lines.Add($"定时炸弹（你放置）：{bomb.ActionsLeft} 个行动后爆炸（半径{GameRulesConfig.TimedBombRadius}）");
        }

        foreach (var mine in mines)
        {
            if (mine.Cell != cell) continue;
            lines.Add($"武装地雷：踩上受到 {GameRulesConfig.MineDamage} 点法伤");
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

    /// <summary>
    /// 该格是否有可见的陷阱标记。
    /// </summary>
    public bool HasVisibleMarkerAt(Int2 cell, UnitActor viewer)
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

    /// <summary>
    /// 身处火焰格立刻获得着火（雨天无法着火）。
    /// </summary>
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
        unit.ApplyStatus(StatusType.Burning, GameRulesConfig.BurningRounds, unit);
        TurnManager.Instance?.Log(
            $"🔥 {RoleInfo.GetDisplayName(unit.Role)} 身处火焰，获得「着火」");
    }

    /// <summary>
    /// 行动开始时：站在火焰上则补着火。
    /// </summary>
    public void ApplyFlameBurningAtTurnStart(UnitActor unit) => ResolveFlameOnCell(unit);

    /// <summary>
    /// 踩踏香蕉皮：投掷者自己踩不触发；濒死无法获得跌倒则不消耗。
    /// </summary>
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
            unit.ApplyStatus(StatusType.Trip, GameRulesConfig.TripRounds, unit);
            DeckManager.Instance?.AddToDiscard(ItemKind.BananaPeel);
            UntrackVisual(bananas[i]);
            bananas.RemoveAt(i);
        }
    }

    /// <summary>
    /// 踩到地雷：法伤并消耗地雷。
    /// </summary>
    public void ResolveMineOnStep(UnitActor unit)
    {
        if (unit == null || unit.IsDead)
            return;

        for (int i = mines.Count - 1; i >= 0; i--)
        {
            if (mines[i].Cell != unit.Cell)
                continue;

            int dealt = unit.TakeDamage(GameRulesConfig.MineDamage, magicDamage: true, fromBombOrMine: true);
            TurnManager.Instance?.Log(
                $"⚠ {RoleInfo.GetDisplayName(unit.Role)} 踩到地雷，受到 {dealt} 点法伤");
            DeckManager.Instance?.AddToDiscard(ItemKind.Mine);
            UntrackVisual(mines[i]);
            mines.RemoveAt(i);
            GameManager.Instance?.CheckWinConditions();
            break;
        }
    }

    /// <summary>
    /// 行动结束：火焰剩余行动 −1，耗尽移除。
    /// </summary>
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
            UntrackVisual(f);
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
            Presentation.Current.UpdateMarkerLabel(VisualOf(bomb), System.Math.Max(0, bomb.ActionsLeft));

            if (bomb.ActionsLeft > 0)
            {
                timedBombs[i] = bomb;
                continue;
            }

            ExplodeTimedBomb(bomb);
            UntrackVisual(bomb);
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
            if (grid.GetManhattanDistance(bomb.Cell, other.Cell) <= GameRulesConfig.TimedBombRadius)
            {
                hits++;
                if (other.TakeDamage(GameRulesConfig.TimedBombDamage, magicDamage: false) > 0)
                    damaged++;
            }
        }
        TurnManager.Instance?.Log(
            $"定时炸弹在 ({bomb.Cell.x},{bomb.Cell.y}) 爆炸！覆盖 {hits} 人，{damaged} 人扣血");
        GameManager.Instance?.CheckWinConditions();
    }

    /// <summary>
    /// 熔岩吞掉指定格的危险物。
    /// </summary>
    public void SwallowCells(List<Int2> cells)
    {
        if (cells == null) return;
        var set = new HashSet<Int2>(cells);
        for (int i = bananas.Count - 1; i >= 0; i--)
        {
            if (!set.Contains(bananas[i].Cell)) continue;
            DeckManager.Instance?.AddToDiscard(ItemKind.BananaPeel);
            UntrackVisual(bananas[i]);
            bananas.RemoveAt(i);
        }
        for (int i = timedBombs.Count - 1; i >= 0; i--)
        {
            if (!set.Contains(timedBombs[i].Cell)) continue;
            DeckManager.Instance?.AddToDiscard(ItemKind.TimedBomb);
            UntrackVisual(timedBombs[i]);
            timedBombs.RemoveAt(i);
        }
        for (int i = mines.Count - 1; i >= 0; i--)
        {
            if (!set.Contains(mines[i].Cell)) continue;
            DeckManager.Instance?.AddToDiscard(ItemKind.Mine);
            UntrackVisual(mines[i]);
            mines.RemoveAt(i);
        }
        for (int i = flames.Count - 1; i >= 0; i--)
        {
            if (!set.Contains(flames[i].Cell)) continue;
            var cell = flames[i].Cell;
            UntrackVisual(flames[i]);
            flames.RemoveAt(i);
            NotifyJungleStealthAt(cell);
        }
    }
}
