/// <summary>当前对局模式与人类操控角色（AI / 管理员对战时用）。</summary>
public enum GameMode
{
    Hotseat,
    AiBattle,
    /// <summary>基于 AI 对战：全屏视野、可查 AI 背包/技能、每行动可领一张任意卡。</summary>
    AdminMode
}

public static class MatchConfig
{
    public static GameMode Mode { get; private set; } = GameMode.Hotseat;
    public static RoleType HumanRole { get; private set; } = RoleType.Human;

    /// <summary>有 AI 队友/对手的对战（含管理员模式）。</summary>
    public static bool IsAiBattle => Mode == GameMode.AiBattle || Mode == GameMode.AdminMode;

    public static bool IsAdminMode => Mode == GameMode.AdminMode;

    public static void SetHotseat()
    {
        Mode = GameMode.Hotseat;
        HumanRole = RoleType.Human;
    }

    public static void SetAiBattle(RoleType humanRole)
    {
        Mode = GameMode.AiBattle;
        HumanRole = humanRole;
    }

    public static void SetAdminMode(RoleType humanRole)
    {
        Mode = GameMode.AdminMode;
        HumanRole = humanRole;
    }

    public static void Clear()
    {
        Mode = GameMode.Hotseat;
        HumanRole = RoleType.Human;
    }

    public static bool IsHumanControlled(RoleType role)
    {
        if (!IsAiBattle)
            return true;
        return role == HumanRole;
    }

    public static bool IsHumanControlled(UnitActor unit)
    {
        return unit != null && IsHumanControlled(unit.Role);
    }

    /// <summary>当前回合是否由本地玩家操作。</summary>
    public static bool IsHumanTurn()
    {
        var unit = TurnManager.Instance?.CurrentUnit;
        if (unit == null || unit.IsDead)
            return false;
        return IsHumanControlled(unit);
    }
}
