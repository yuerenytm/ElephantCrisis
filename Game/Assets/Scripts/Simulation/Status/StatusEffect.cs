using System.Collections.Generic;
using UnityEngine;

public enum StatusType
{
    Trip,     // 跌倒：防/移减益
    Poison,   // 中毒：可叠层，每层攻防移减益；视缩减；行动结束每层法伤
    Burning,  // 着火：行动开始火焰法伤，持续按自身行动开始倒数
    Hidden,   // 隐匿：攻击/被攻击/着火解除；丛林来源另计离林/着火格
    Leader,   // 领袖：攻防/移增益；行动开始真伤
    Stun,     // 晕眩：行动开始跳过本次行动
    Blind     // 致盲：能见度 0
}

/// <summary>
/// 状态何时递减。以前这件事散在四个 Tick 函数里、靠注释约定；
/// 现在变成字段：加新状态时必须显式选一个，选错会被对拍抓出来。
/// </summary>
public enum TickPolicy
{
    /// <summary>施加者每次行动开始时倒数。</summary>
    ApplierTurn,
    /// <summary>持有者自己行动开始时倒数；着火 / 领袖是"先结算伤害再递减"。</summary>
    OwnTurnStart,
    /// <summary>完整回合结束时倒数（无施加者的普通状态）。</summary>
    FullRound,
    /// <summary>不按回合递减，只靠显式清除（地形毒、隐匿）。</summary>
    Manual
}

public struct StatusEffect
{
    /// <summary>状态类型。</summary>
    public StatusType Type;
    /// <summary>剩余层数 / 回合数；归零时移除。</summary>
    public int RoundsLeft;
    /// <summary>递减时机；决定它由哪个 Tick 入口处理。</summary>
    public TickPolicy Policy;
    /// <summary>是否记录了施加者：为 true 时由 SourceRole 决定「按施加者行动开始倒数」。</summary>
    public bool HasSource;
    /// <summary>施加者角色；仅在 HasSource 为 true 时有意义。</summary>
    public RoleType SourceRole;

    public StatusEffect(StatusType type, int rounds, TickPolicy policy)
    {
        Type = type;
        RoundsLeft = rounds;
        Policy = policy;
        HasSource = false;
        SourceRole = default;
    }

    public StatusEffect(StatusType type, int rounds, TickPolicy policy, RoleType source)
    {
        Type = type;
        RoundsLeft = rounds;
        Policy = policy;
        HasSource = true;
        SourceRole = source;
    }
}

public static class StatusInfo
{
    public static string GetDisplayName(StatusType type)
        => GameRulesConfig.GetString("display",
            "status_" + GameRulesConfig.ToSnakeCase(type.ToString()) + "_name",
            FallbackDisplayName(type));

    /// <summary>代码内置名称；配置缺失时回退。</summary>
    private static string FallbackDisplayName(StatusType type)
    {
        switch (type)
        {
            case StatusType.Trip: return "跌倒";
            case StatusType.Poison: return "中毒";
            case StatusType.Burning: return "着火";
            case StatusType.Hidden: return "隐匿";
            case StatusType.Leader: return "领袖";
            case StatusType.Stun: return "晕眩";
            case StatusType.Blind: return "致盲";
            default: return type.ToString();
        }
    }

    public static string GetShortTip(StatusType type)
        => GameRulesConfig.GetString("display",
            "status_" + GameRulesConfig.ToSnakeCase(type.ToString()) + "_tip",
            FallbackShortTip(type));

    /// <summary>代码内置提示；配置缺失时回退。</summary>
    private static string FallbackShortTip(StatusType type)
    {
        switch (type)
        {
            case StatusType.Trip: return $"防{GameRulesConfig.TripDef} 移{GameRulesConfig.TripMove}";
            case StatusType.Poison: return $"每层攻防移{GameRulesConfig.PoisonPerStackAtk} 视{GameRulesConfig.PoisonVisibility}；终每层{GameRulesConfig.PoisonEndDamagePerStack}毒伤；最多{GameRulesConfig.PoisonMaxStacks}层";
            case StatusType.Burning: return $"行动开始{GameRulesConfig.BurningMagicDamage}火焰法伤";
            case StatusType.Hidden: return "非邻接不可被攻";
            case StatusType.Leader: return $"攻防+{GameRulesConfig.LeaderAtk} 移+{GameRulesConfig.LeaderMove}；行动开始{GameRulesConfig.LeaderStartDamage}真伤";
            case StatusType.Stun: return "跳过本次行动";
            case StatusType.Blind: return $"能见度{GameRulesConfig.BlindVisibility}";
            default: return "";
        }
    }
}
