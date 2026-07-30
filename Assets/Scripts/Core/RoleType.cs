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

    public static void GetBaseStats(RoleType role, out int move, out int hp, out int atk, out int def, out int bag)
    {
        switch (role)
        {
            // 血/攻/防/背包相对初版已放大；象偏肉盾：高血高防、攻击不突出
            case RoleType.Elephant:
                move = 3; hp = 120; atk = 9; def = 10; bag = 15;
                break;
            case RoleType.Human:
                move = 5; hp = 90; atk = 9; def = 8; bag = 24;
                break;
            case RoleType.Monkey:
                move = 6; hp = 90; atk = 6; def = 6; bag = 18;
                break;
            case RoleType.Cat:
                move = 8; hp = 60; atk = 9; def = 4; bag = 12;
                break;
            default:
                move = 4; hp = 90; atk = 9; def = 8; bag = 15;
                break;
        }
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
