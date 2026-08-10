public enum RoleType
{
    Elephant,
    Human,
    Monkey,
    Cat
}

public static class RoleInfo
{
    public static string GetDisplayName(RoleType role)
    {
        switch (role)
        {
            case RoleType.Elephant: return "象";
            case RoleType.Human: return "人";
            case RoleType.Monkey: return "猴";
            case RoleType.Cat: return "猫";
            default: return role.ToString();
        }
    }

    public static UnityEngine.Color GetColor(RoleType role)
    {
        switch (role)
        {
            case RoleType.Elephant: return new UnityEngine.Color(0.55f, 0.55f, 0.6f);
            case RoleType.Human: return new UnityEngine.Color(0.35f, 0.55f, 0.95f);
            case RoleType.Monkey: return new UnityEngine.Color(0.85f, 0.55f, 0.25f);
            case RoleType.Cat: return new UnityEngine.Color(0.95f, 0.75f, 0.2f);
            default: return UnityEngine.Color.white;
        }
    }

    /// <summary>角色基础五维，来自 StreamingAssets/Config/game_rules.yaml（与 sim 共用）。</summary>
    public static void GetBaseStats(RoleType role, out int move, out int hp, out int atk, out int def, out int bag)
    {
        var s = GameRulesConfig.GetRoleStats(role);
        move = s.Move;
        hp = s.Hp;
        atk = s.Atk;
        def = s.Def;
        bag = s.Bag;
    }

    public static ItemKind GetOwnDoll(RoleType role)
    {
        switch (role)
        {
            case RoleType.Elephant: return ItemKind.DollElephant;
            case RoleType.Human: return ItemKind.DollHuman;
            case RoleType.Monkey: return ItemKind.DollMonkey;
            case RoleType.Cat: return ItemKind.DollCat;
            default: return ItemKind.DollHuman;
        }
    }
}
