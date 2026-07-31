using UnityEngine;

public enum ItemKind
{
    DollElephant,
    DollHuman,
    DollMonkey,
    DollCat,
    SmallPotion,
    Bomb,
    MegaBomb,
    TimedBomb,
    Reinforce,
    Bow,
    Crossbow,
    Arrow,
    PoisonArrow,
    FireRocket,
    BananaPeel,
    Flamethrower,
    WoodArmor,
    IronArmor,
    EnergyShield,
    Adrenaline,
    Mine,
    LargePotion,
    SkillUpgrade
}

public enum ItemUseKind
{
    None,
    Instant,
    ChooseStat,
    TargetCell,
    TargetUnit,
    EquipToggle,
    ChooseDelay,    // 定时炸弹：选 1–5 回合
    ChooseDirection // 火焰喷射器：选方向
}

public static class ItemInfo
{
    public static string GetDisplayName(ItemKind kind)
    {
        switch (kind)
        {
            case ItemKind.DollElephant: return "象玩偶";
            case ItemKind.DollHuman: return "人玩偶";
            case ItemKind.DollMonkey: return "猴玩偶";
            case ItemKind.DollCat: return "猫玩偶";
            case ItemKind.SmallPotion: return "小血瓶";
            case ItemKind.LargePotion: return "大血瓶";
            case ItemKind.Bomb: return "炸弹";
            case ItemKind.MegaBomb: return "高爆炸弹";
            case ItemKind.TimedBomb: return "定时炸弹";
            case ItemKind.Reinforce: return "强化剂";
            case ItemKind.Bow: return "弓";
            case ItemKind.Crossbow: return "弩";
            case ItemKind.Arrow: return "弓箭";
            case ItemKind.PoisonArrow: return "毒箭";
            case ItemKind.FireRocket: return "火箭";
            case ItemKind.BananaPeel: return "香蕉皮";
            case ItemKind.Flamethrower: return "火焰喷射器";
            case ItemKind.WoodArmor: return "木甲";
            case ItemKind.IronArmor: return "铁甲";
            case ItemKind.EnergyShield: return "能量护盾";
            case ItemKind.Adrenaline: return "肾上腺素";
            case ItemKind.Mine: return "地雷";
            case ItemKind.SkillUpgrade: return "技能升级卡";
            default: return kind.ToString();
        }
    }

    public static string GetShortDesc(ItemKind kind)
    {
        switch (kind)
        {
            case ItemKind.SmallPotion: return "+9血";
            case ItemKind.LargePotion: return "+15血";
            case ItemKind.Bomb: return "掷地，15物伤，半径2";
            case ItemKind.MegaBomb: return "掷地，24物伤，半径2";
            case ItemKind.TimedBomb: return "安放，1–5回合后爆半径4，仅自己可见";
            case ItemKind.Reinforce: return "攻/防+3 或 移+1";
            case ItemKind.Bow: return "攻+2 射距3，耗1弹药（须装备）";
            case ItemKind.Crossbow: return "攻+10 射距5，耗1；须蓄力，同行动不可射";
            case ItemKind.Arrow: return "弹药，1支/张，重1，可合并";
            case ItemKind.PoisonArrow: return "1支/张，命中中毒1回合";
            case ItemKind.FireRocket: return "1支/张，命中着火1回合";
            case ItemKind.BananaPeel: return "投掷隐身陷阱，踩踏跌倒";
            case ItemKind.Flamethrower: return "直线5格10法伤+着火1，CD3（须装备）";
            case ItemKind.WoodArmor: return "装备防+4；物伤即耗耐久";
            case ItemKind.IronArmor: return "装备防+8；物伤即耗耐久";
            case ItemKind.EnergyShield: return "装备，吸收物伤/法伤（不挡真伤）";
            case ItemKind.Adrenaline: return "HP低于30%：移+2攻+3，3回合";
            case ItemKind.Mine: return "放置陷阱，踩中15法伤；弃置可捡";
            case ItemKind.SkillUpgrade: return "集齐3张：技能等级+1";
            default: return IsDoll(kind) ? "金色·持有增益/集齐获胜" : "";
        }
    }

    /// <summary>玩偶卡 UI / 掉落角标用金色。</summary>
    public static Color GetDollGoldFill() => new Color(0.92f, 0.72f, 0.18f, 1f);
    public static Color GetDollGoldBorder() => new Color(0.55f, 0.38f, 0.08f, 1f);
    public static Color GetDollGoldUiBg() => new Color(0.42f, 0.32f, 0.1f, 0.95f);
    public static Color GetDollGoldText() => new Color(1f, 0.9f, 0.4f, 1f);

    public static bool IsStackableAmmo(ItemKind kind)
    {
        return kind == ItemKind.Arrow || kind == ItemKind.PoisonArrow || kind == ItemKind.FireRocket;
    }

    public static int GetAmmoPerCard(ItemKind kind)
    {
        if (IsStackableAmmo(kind))
            return 1;
        return 0;
    }

    public static float GetWeight(ItemKind kind) => 1f;

    public static float GetWeight(InventoryItem item)
    {
        if (IsStackableAmmo(item.Kind))
            return Mathf.Max(1, item.Charges);
        return 1f;
    }

    /// <summary>支数 = 牌数（一张一支）。</summary>
    public static int AmmoChargesToCards(int charges)
    {
        return Mathf.Max(0, charges);
    }

    [System.Obsolete("用 AmmoChargesToCards")]
    public static int ArrowChargesToCards(int charges) => AmmoChargesToCards(charges);

    public enum EquipSlot
    {
        None,
        Weapon,
        Armor,
        Vehicle,
        Accessory
    }

    public static EquipSlot GetEquipSlot(ItemKind kind)
    {
        switch (kind)
        {
            case ItemKind.Bow:
            case ItemKind.Crossbow:
            case ItemKind.Flamethrower:
                return EquipSlot.Weapon;
            case ItemKind.WoodArmor:
            case ItemKind.IronArmor:
            case ItemKind.EnergyShield:
                return EquipSlot.Armor;
            default:
                return EquipSlot.None;
        }
    }

    public static bool IsEquipable(ItemKind kind) => GetEquipSlot(kind) != EquipSlot.None;

    public static bool IsPotion(ItemKind kind)
        => kind == ItemKind.SmallPotion || kind == ItemKind.LargePotion;

    public static int GetPotionHeal(ItemKind kind)
    {
        switch (kind)
        {
            case ItemKind.SmallPotion: return 9;
            case ItemKind.LargePotion: return 15;
            default: return 0;
        }
    }

    public static bool IsDoll(ItemKind kind)
    {
        return kind == ItemKind.DollElephant
            || kind == ItemKind.DollHuman
            || kind == ItemKind.DollMonkey
            || kind == ItemKind.DollCat;
    }

    public static bool IsWeapon(ItemKind kind)
        => kind == ItemKind.Bow || kind == ItemKind.Crossbow || kind == ItemKind.Flamethrower;

    public static bool IsRangedWeapon(ItemKind kind)
        => kind == ItemKind.Bow || kind == ItemKind.Crossbow;

    public static bool IsThrowableBomb(ItemKind kind)
        => kind == ItemKind.Bomb || kind == ItemKind.MegaBomb;

    public static bool IsArmor(ItemKind kind)
        => kind == ItemKind.WoodArmor || kind == ItemKind.IronArmor;

    public static int GetBombDamage(ItemKind kind)
    {
        switch (kind)
        {
            case ItemKind.Bomb: return 15;
            case ItemKind.MegaBomb: return 24;
            default: return 0;
        }
    }

    public static int GetBombBlastRadius(ItemKind kind) => 2;

    public static int GetArmorDefense(ItemKind kind)
    {
        switch (kind)
        {
            case ItemKind.WoodArmor: return 4;
            case ItemKind.IronArmor: return 8;
            default: return 0;
        }
    }

    public static int GetMaxCharges(ItemKind kind)
    {
        switch (kind)
        {
            case ItemKind.WoodArmor: return 5;
            case ItemKind.IronArmor: return 8;
            case ItemKind.EnergyShield: return 3;
            default: return 0;
        }
    }

    public static int GetWeaponAtkBonus(ItemKind weapon)
    {
        switch (weapon)
        {
            case ItemKind.Bow: return 2;
            case ItemKind.Crossbow: return 10;
            default: return 0;
        }
    }

    public static int GetWeaponRangeBonus(ItemKind weapon)
    {
        switch (weapon)
        {
            case ItemKind.Bow: return 3;
            case ItemKind.Crossbow: return 5;
            default: return 0;
        }
    }

    public static int GetArrowCost(ItemKind weapon)
    {
        switch (weapon)
        {
            case ItemKind.Bow: return 1;
            case ItemKind.Crossbow: return 1;
            default: return 0;
        }
    }

    public static ItemUseKind GetUseKind(ItemKind kind)
    {
        switch (kind)
        {
            case ItemKind.SmallPotion:
            case ItemKind.LargePotion:
            case ItemKind.Adrenaline:
            case ItemKind.SkillUpgrade:
                return ItemUseKind.Instant;
            case ItemKind.Reinforce:
                return ItemUseKind.ChooseStat;
            case ItemKind.Bomb:
            case ItemKind.MegaBomb:
            case ItemKind.BananaPeel:
            case ItemKind.Mine:
                return ItemUseKind.TargetCell;
            case ItemKind.TimedBomb:
                return ItemUseKind.ChooseDelay;
            case ItemKind.Bow:
            case ItemKind.Crossbow:
            case ItemKind.Flamethrower:
            case ItemKind.WoodArmor:
            case ItemKind.IronArmor:
            case ItemKind.EnergyShield:
                return ItemUseKind.EquipToggle;
            default:
                return ItemUseKind.None;
        }
    }

    public static bool CanDiscard(ItemKind kind) => true;
}
