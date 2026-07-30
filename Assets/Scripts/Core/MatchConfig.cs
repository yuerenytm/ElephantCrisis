/// <summary>当前对局模式与人类操控角色（AI 对战时用）。</summary>
public enum GameMode
{
    Hotseat,
    AiBattle
}

public static class MatchConfig
{
    public static GameMode Mode { get; private set; } = GameMode.Hotseat;
    public static RoleType HumanRole { get; private set; } = RoleType.Human;

    public static bool IsAiBattle => Mode == GameMode.AiBattle;

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
