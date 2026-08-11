using UnityEngine;

/// <summary>
/// 能见度：时段×天气矩阵 + 挂件/状态；不可移动到自身能见度外；迷雾按当前视野角色刷新（热座=行动者；AI 对战=玩家角色）。
/// </summary>
public static class VisibilityService
{
    /// <summary>白天基础能见度（兼容旧引用）。</summary>
    public const int DefaultVisibility = 10;

    public static UnitActor GetFogViewer()
    {
        // AI 对战 / 管理员：始终按玩家角色视野（荣誉模式，无全图透视）
        if (MatchConfig.IsAiBattle)
        {
            var human = FindUnit(MatchConfig.HumanRole);
            if (human != null && !human.IsDead)
                return human;
        }
        return TurnManager.Instance?.CurrentUnit;
    }

    public static UnitActor FindUnit(RoleType role)
    {
        var units = GameManager.Instance?.Units ?? TurnManager.Instance?.Units;
        if (units == null)
            return null;
        foreach (var u in units)
        {
            if (u != null && u.Role == role && !u.IsDead)
                return u;
        }
        return null;
    }

    public static bool CanSeeCell(UnitActor viewer, Vector2Int cell)
    {
        var grid = GridManager.Instance;
        if (grid == null || !grid.IsValidCell(cell))
            return false;
        if (viewer == null || viewer.IsDead)
            return false;
        return grid.GetManhattanDistance(viewer.Cell, cell) <= viewer.CurrentVisibility;
    }

    /// <summary>行动者是否可将目标格作为移动落点（须在自身能见度内）。</summary>
    public static bool CanMoveTo(UnitActor mover, Vector2Int target)
    {
        if (mover == null || mover.IsDead)
            return false;
        var grid = GridManager.Instance;
        if (grid == null || !grid.IsValidCell(target))
            return false;
        return grid.GetManhattanDistance(mover.Cell, target) <= mover.CurrentVisibility;
    }

    /// <summary>视野角色是否能看见该单位（隐匿另计）。</summary>
    public static bool CanSeeUnit(UnitActor viewer, UnitActor target)
    {
        if (target == null || target.IsDead)
            return false;
        if (viewer == null)
            return false;
        if (viewer == target)
            return true;
        if (!CanSeeCell(viewer, target.Cell))
            return false;
        if (target.HasStatus(StatusType.Hidden)
            && GridManager.Instance.GetManhattanDistance(viewer.Cell, target.Cell) > 1
            && !ItemInfo.CanRevealHidden(viewer))
            return false;
        return true;
    }

    /// <summary>望远镜窥视：viewer 能否查看 target 背包（须望远镜生效且能看见该单位）。</summary>
    public static bool CanPeekInventory(UnitActor viewer, UnitActor target)
    {
        if (!ItemInfo.CanPeekInventories(viewer) || target == null || target.IsDead)
            return false;
        if (viewer == target)
            return true;
        return CanSeeUnit(viewer, target);
    }

    public static void RefreshWorld()
    {
        var viewer = GetFogViewer();
        MapVisual.Instance?.ApplyFog(viewer);
        RefreshUnitVisibility(viewer);
        GroundItemManager.Instance?.RefreshAllVisibility(viewer);
        HazardManager.Instance?.RefreshHazardVisibility();
        AtmosphereVisual.Instance?.Refresh();
    }

    /// <summary>天气/时段变更后：立刻重算迷雾半径、单位显隐与移动提示。</summary>
    public static void RefreshAfterVisionRuleChange()
    {
        RefreshWorld();
        PlayerInputController.Instance?.RefreshHints();
        // 状态栏「视」等：延后刷新，避免在用牌按钮回调里拆毁 UI
        GameUI.Instance?.RequestRefresh();
    }

    private static void RefreshUnitVisibility(UnitActor viewer)
    {
        var units = GameManager.Instance?.Units ?? TurnManager.Instance?.Units;
        if (units == null)
            return;
        foreach (var u in units)
        {
            if (u == null || u.IsDead)
                continue;
            bool show = viewer == null || CanSeeUnit(viewer, u);
            u.SetWorldVisible(show);
        }
    }
}
