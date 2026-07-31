using UnityEngine;

/// <summary>
/// 能见度：默认半径 10；不可移动到自身能见度外；迷雾按当前视野角色刷新（热座=行动者；AI 对战=玩家角色）。
/// </summary>
public static class VisibilityService
{
    public const int DefaultVisibility = 10;

    public static UnitActor GetFogViewer()
    {
        // 管理员：全屏视野（无迷雾中心限制）
        if (MatchConfig.IsAdminMode)
            return null;

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
        // 管理员：全图可见（迷雾关闭）
        if (MatchConfig.IsAdminMode)
            return true;
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

    /// <summary>视野角色是否能看见该单位（隐匿另计；管理员无视隐匿与迷雾）。</summary>
    public static bool CanSeeUnit(UnitActor viewer, UnitActor target)
    {
        if (target == null || target.IsDead)
            return false;
        if (MatchConfig.IsAdminMode)
            return true;
        if (viewer == null)
            return false;
        if (viewer == target)
            return true;
        if (!CanSeeCell(viewer, target.Cell))
            return false;
        if (target.HasStatus(StatusType.Hidden)
            && GridManager.Instance.GetManhattanDistance(viewer.Cell, target.Cell) > 1)
            return false;
        return true;
    }

    public static void RefreshWorld()
    {
        var viewer = GetFogViewer();
        MapVisual.Instance?.ApplyFog(viewer);
        RefreshUnitVisibility(viewer);
        GroundItemManager.Instance?.RefreshAllVisibility(viewer);
        HazardManager.Instance?.RefreshHazardVisibility();
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
