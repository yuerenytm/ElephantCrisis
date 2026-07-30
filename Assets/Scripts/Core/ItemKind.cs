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
    Mine
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
            default: return kind.ToString();
        }
    }

    public static string GetShortDesc(ItemKind kind)
    {
        switch (kind)
        {
            case ItemKind.SmallPotion: return "+9血";
            case ItemKind.Bomb: return "掷地，15物伤，半径2";
            case ItemKind.MegaBomb: return "掷地，24物伤，半径2";
            case ItemKind.TimedBomb: return "安放，1–5回合后爆半径4，仅自己可见";
            case ItemKind.Reinforce: return "攻/防+3 或 移+1";
            case ItemKind.Bow: return "攻+2 射距3，耗1弹药";
            case ItemKind.Crossbow: return "攻+10 射距5，耗3弹药";
            case ItemKind.Arrow: return "弹药，3支/张，可合并";
            case ItemKind.PoisonArrow: return "3支/张，命中中毒1回合";
            case ItemKind.FireRocket: return "3支/张，命中着火1回合";
            case ItemKind.BananaPeel: return "投掷隐身陷阱，踩踏跌倒";
            case ItemKind.Flamethrower: return "直线5格15伤+着火，CD3";
            case ItemKind.WoodArmor: return "使用穿戴，防+4";
            case ItemKind.IronArmor: return "使用穿戴，防+8";
            case ItemKind.EnergyShield: return "使用穿戴，挡物伤";
            case ItemKind.Adrenaline: return "HP低于30%：移+2攻+3，3回合";
            case ItemKind.Mine: return "弃置到格上，踩中15真伤";
            default: return IsDoll(kind) ? "集齐获胜" : "";
        }
    }

    public static bool IsStackableAmmo(ItemKind kind)
    {
        return kind == ItemKind.Arrow || kind == ItemKind.PoisonArrow || kind == ItemKind.FireRocket;
    }

    public static int GetAmmoPerCard(ItemKind kind)
    {
        if (IsStackableAmmo(kind))
            return 3;
        return 0;
    }

    public static float GetWeight(ItemKind kind)
    {
        if (IsStackableAmmo(kind))
            return 1f / 3f;
        return 1f;
    }

    public static float GetWeight(InventoryItem item)
    {
        if (IsStackableAmmo(item.Kind))
            return item.Charges * (1f / 3f);
        return GetWeight(item.Kind);
    }

    /// <summary>地上/弃牌时，按支数折合多少张弹药牌（向上取整，3 支一张）。</summary>
    public static int AmmoChargesToCards(int charges)
    {
        if (charges <= 0)
            return 0;
        return (charges + 2) / 3;
    }

    [System.Obsolete("用 AmmoChargesToCards")]
    public static int ArrowChargesToCards(int charges) => AmmoChargesToCards(charges);

    public static bool IsDoll(ItemKind kind)
    {
        return kind == ItemKind.DollElephant
            || kind == ItemKind.DollHuman
            || kind == ItemKind.DollMonkey
            || kind == ItemKind.DollCat;
    }

    public static bool IsWeapon(ItemKind kind)
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
            case ItemKind.Crossbow: return 3;
            default: return 0;
        }
    }

    public static ItemUseKind GetUseKind(ItemKind kind)
    {
        switch (kind)
        {
            case ItemKind.SmallPotion:
            case ItemKind.Adrenaline:
                return ItemUseKind.Instant;
            case ItemKind.Reinforce:
                return ItemUseKind.ChooseStat;
            case ItemKind.Bomb:
            case ItemKind.MegaBomb:
            case ItemKind.BananaPeel:
                return ItemUseKind.TargetCell;
            case ItemKind.TimedBomb:
                return ItemUseKind.ChooseDelay;
            case ItemKind.Flamethrower:
                return ItemUseKind.ChooseDirection;
            case ItemKind.Bow:
            case ItemKind.Crossbow:
                return ItemUseKind.TargetUnit;
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
