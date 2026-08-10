using System.Collections.Generic;

/// <summary>四角色现行技能的等级数值。</summary>
public static class SkillInfo
{
    public static string GetSkillName(RoleType role) => role switch
    {
        RoleType.Elephant => "威慑",
        RoleType.Human => "强化",
        RoleType.Monkey => "抢夺",
        RoleType.Cat => "隐匿",
        _ => "技能"
    };

    public static bool IsPassive(RoleType role) => role == RoleType.Elephant;

    public static int GetHumanCooldownRounds(int skillLevel) => skillLevel switch
    {
        3 => 5,
        2 => 6,
        _ => 8
    };

    public static int GetMonkeyCooldownRounds(int skillLevel) => skillLevel switch
    {
        3 => 3,
        2 => 4,
        _ => 5
    };

    public static int GetCatCooldownRounds(int skillLevel) => skillLevel switch
    {
        3 => 3,
        2 => 4,
        _ => 5
    };

    public static int GetMonkeyRadius(int skillLevel) => skillLevel switch
    {
        3 => 4,
        2 => 3,
        _ => 2
    };

    public static int GetDeterrenceOuterRadius(int skillLevel) => skillLevel switch
    {
        3 => 7,
        2 => 5,
        _ => 3
    };

    public static int GetDeterrenceInnerRadius(int skillLevel)
        => skillLevel >= 3 ? 2 : 0;
}

/// <summary>旧版猴标记已废弃；保留空实现以免残留引用编译失败。</summary>
public static class StealMarks
{
    public static void Reset() { }
    public static void Mark(int instanceId) { }
    public static bool IsMarked(int instanceId) => false;
    public static void Unmark(int instanceId) { }
}
