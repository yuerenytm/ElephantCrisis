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
    RubberRaincoat,
    EnergyShield,
    ThornsArmor,
    TacticalVest,
    Adrenaline,
    Mine,
    LargePotion,
    SkillUpgrade,
    NightVision,
    Telescope,
    Skateboard,
    Motorcycle,
    IceSkates,
    GrappleHook,
    Amulet,
    Flashbang,
    Dagger,
    Longsword,
    ArmorPiercingBlade,
    CursedBlade,
    GasolineBottle,
    Lighter,
    Boomerang,
    Milk,
    WeatherClear,
    WeatherRain,
    WeatherFog,
    RedBull
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
            case ItemKind.RubberRaincoat: return "橡胶雨衣";
            case ItemKind.EnergyShield: return "能量护盾";
            case ItemKind.ThornsArmor: return "荆棘护甲";
            case ItemKind.TacticalVest: return "战术背心";
            case ItemKind.Adrenaline: return "肾上腺素";
            case ItemKind.Mine: return "地雷";
            case ItemKind.SkillUpgrade: return "技能升级卡";
            case ItemKind.NightVision: return "夜视镜";
            case ItemKind.Telescope: return "望远镜";
            case ItemKind.Skateboard: return "滑板";
            case ItemKind.Motorcycle: return "摩托车";
            case ItemKind.IceSkates: return "滑行靴";
            case ItemKind.GrappleHook: return "抢夺勾爪";
            case ItemKind.Amulet: return "护身符";
            case ItemKind.Flashbang: return "闪光弹";
            case ItemKind.Dagger: return "匕首";
            case ItemKind.Longsword: return "长剑";
            case ItemKind.ArmorPiercingBlade: return "破甲刃";
            case ItemKind.CursedBlade: return "诅咒之刃";
            case ItemKind.GasolineBottle: return "汽油瓶";
            case ItemKind.Lighter: return "打火机";
            case ItemKind.Boomerang: return "回旋镖";
            case ItemKind.Milk: return "牛奶";
            case ItemKind.WeatherClear: return "晴天弹";
            case ItemKind.WeatherRain: return "雨天弹";
            case ItemKind.WeatherFog: return "雾天弹";
            case ItemKind.RedBull: return "红牛";
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
            case ItemKind.Reinforce: return "攻/防/移 永久+1（三选一）";
            case ItemKind.Bow: return "攻+2 射距3，耗1弹药（须装备）";
            case ItemKind.Crossbow: return "攻+10 射距5，耗1；须蓄力，同行动不可射";
            case ItemKind.Arrow: return "弹药，1支/张，重1，可合并";
            case ItemKind.PoisonArrow: return "1支/张，命中中毒1回合";
            case ItemKind.FireRocket: return "1支/张，命中着火1回合";
            case ItemKind.BananaPeel: return "投掷隐身陷阱，踩踏跌倒";
            case ItemKind.Flamethrower: return "须装备+耗1汽油；直线5格10法伤+着火；路径火焰2回合";
            case ItemKind.WoodArmor: return "装备防+4；物伤即耗耐久";
            case ItemKind.IronArmor: return "装备防+8；物伤即耗耐久";
            case ItemKind.RubberRaincoat: return "装备防+3；免雨天减益；免着火耗耐久；耐久3";
            case ItemKind.EnergyShield: return "装备，吸收物伤/法伤（不挡真伤）";
            case ItemKind.ThornsArmor: return "装备防+2；近战反伤实际伤害50%真伤";
            case ItemKind.TacticalVest: return "装备防+2 包+5；炸/雷额外-2；耐久6";
            case ItemKind.Adrenaline: return "HP低于30%：移+2攻+3，3回合";
            case ItemKind.Mine: return "放置陷阱，踩中15法伤；弃置可捡";
            case ItemKind.SkillUpgrade: return "集齐3张：技能等级+1";
            case ItemKind.NightVision: return "装备；黑夜基础能见度按8";
            case ItemKind.Telescope: return "装备；昼间晴/雨：全图视野（不含隐匿）";
            case ItemKind.Skateboard: return "装备；移动力+2";
            case ItemKind.Motorcycle: return "装备；耗1油发动3回合：移+3/冲击6–10宽3";
            case ItemKind.IceSkates: return "装备；冰地移+2且不跌倒";
            case ItemKind.GrappleHook: return "装备；半径3抢一件，用后毁";
            case ItemKind.Amulet: return "装备；致命伤免伤一次+护盾+隐匿";
            case ItemKind.Flashbang: return "投掷；爆点半径2致盲1回合";
            case ItemKind.Dagger: return "装备；近战距1 伤害+6";
            case ItemKind.Longsword: return "装备；近战距2 伤害+8";
            case ItemKind.ArmorPiercingBlade: return "装备；近战距1 伤害+6；无视防具";
            case ItemKind.CursedBlade: return "装备；第x次：己x/敌2x真伤；移动后不可用";
            case ItemKind.GasolineBottle: return "弹药：喷火耗1；或+打火机投掷燃瓶";
            case ItemKind.Lighter: return "点燃汽油瓶投掷（保留打火机）";
            case ItemKind.Boomerang: return "射程4，15物伤；击杀回手否则弃牌";
            case ItemKind.Milk: return "清除自身中毒/着火/跌倒";
            case ItemKind.WeatherClear: return "立刻将天气转为晴天";
            case ItemKind.WeatherRain: return "立刻将天气转为雨天";
            case ItemKind.WeatherFog: return "立刻将天气转为雾天";
            case ItemKind.RedBull: return "行动结束后额外行动一次";
            default: return IsDoll(kind) ? "金色·持有增益/集齐获胜" : "";
        }
    }

    public const int BoomerangRange = 4;
    public const int BoomerangDamage = 15;

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
            case ItemKind.Dagger:
            case ItemKind.Longsword:
            case ItemKind.ArmorPiercingBlade:
            case ItemKind.CursedBlade:
                return EquipSlot.Weapon;
            case ItemKind.WoodArmor:
            case ItemKind.IronArmor:
            case ItemKind.RubberRaincoat:
            case ItemKind.ThornsArmor:
            case ItemKind.TacticalVest:
            case ItemKind.EnergyShield:
                return EquipSlot.Armor;
            case ItemKind.NightVision:
            case ItemKind.Telescope:
            case ItemKind.GrappleHook:
            case ItemKind.Amulet:
                return EquipSlot.Accessory;
            case ItemKind.Skateboard:
            case ItemKind.Motorcycle:
            case ItemKind.IceSkates:
                return EquipSlot.Vehicle;
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
        => kind == ItemKind.Bow || kind == ItemKind.Crossbow || kind == ItemKind.Flamethrower
            || IsMeleeWeapon(kind);

    public static bool IsRangedWeapon(ItemKind kind)
        => kind == ItemKind.Bow || kind == ItemKind.Crossbow;

    public static bool IsMeleeWeapon(ItemKind kind)
        => kind == ItemKind.Dagger || kind == ItemKind.Longsword
           || kind == ItemKind.ArmorPiercingBlade || kind == ItemKind.CursedBlade;

    /// <summary>近战武器基础攻击距离（不含高地等外部增益）。无近战武器时为 1。</summary>
    public static int GetMeleeWeaponRange(ItemKind kind)
    {
        switch (kind)
        {
            case ItemKind.Dagger: return 1;
            case ItemKind.Longsword: return 2;
            case ItemKind.ArmorPiercingBlade: return 1;
            case ItemKind.CursedBlade: return 1;
            default: return 1;
        }
    }

    public static int GetMeleeWeaponAtkBonus(ItemKind kind)
    {
        switch (kind)
        {
            case ItemKind.Dagger: return 6;
            case ItemKind.Longsword: return 8;
            case ItemKind.ArmorPiercingBlade: return 6;
            case ItemKind.CursedBlade: return 0; // 伤害为按次数真伤，不走物伤加成
            default: return 0;
        }
    }

    public static bool HasEquippedCursedBlade(UnitActor unit)
    {
        if (unit?.Inventory == null)
            return false;
        foreach (var it in unit.Inventory.Items)
        {
            if (it.Equipped && it.Kind == ItemKind.CursedBlade)
                return true;
        }
        return false;
    }

    /// <summary>诅咒之刃已使用次数（下次为 +1）。</summary>
    public static int GetCursedBladeTimesUsed(UnitActor unit)
    {
        if (unit?.Inventory == null)
            return 0;
        foreach (var it in unit.Inventory.Items)
        {
            if (it.Equipped && it.Kind == ItemKind.CursedBlade)
                return Mathf.Max(0, it.Charges);
        }
        return 0;
    }

    /// <summary>近战攻击是否无视目标已装备防具提供的防御（不含能量护盾吸收）。</summary>
    public static bool MeleeIgnoresArmor(UnitActor attacker)
    {
        if (attacker?.Inventory == null)
            return false;
        foreach (var it in attacker.Inventory.Items)
        {
            if (it.Equipped && it.Kind == ItemKind.ArmorPiercingBlade)
                return true;
        }
        return false;
    }

    public static bool IsThrowableBomb(ItemKind kind)
        => kind == ItemKind.Bomb || kind == ItemKind.MegaBomb;

    public static bool IsFlashbang(ItemKind kind) => kind == ItemKind.Flashbang;

    /// <summary>点燃汽油瓶投掷：须同时持有打火机与汽油瓶。</summary>
    public static bool IsMolotovKit(ItemKind kind)
        => kind == ItemKind.GasolineBottle || kind == ItemKind.Lighter;

    public static bool CanThrowMolotov(UnitActor unit)
        => unit != null
           && unit.Inventory != null
           && unit.Inventory.CountOf(ItemKind.GasolineBottle) > 0
           && unit.Inventory.CountOf(ItemKind.Lighter) > 0;

    public static bool HasEquippedFlamethrower(UnitActor unit)
    {
        if (unit?.Inventory == null)
            return false;
        foreach (var it in unit.Inventory.Items)
        {
            if (it.Kind == ItemKind.Flamethrower && it.Equipped)
                return true;
        }
        return false;
    }

    public static bool CanFireFlamethrower(UnitActor unit)
        => unit != null
           && unit.Inventory != null
           && unit.Inventory.CountOf(ItemKind.GasolineBottle) > 0
           && HasEquippedFlamethrower(unit);

    public static int GetVehicleMoveBonus(ItemKind kind)
    {
        switch (kind)
        {
            case ItemKind.Skateboard: return 2;
            case ItemKind.Motorcycle: return 3;
            case ItemKind.IceSkates: return 0; // 仅在冰地由 UnitActor 额外 +2
            default: return 0;
        }
    }

    public static bool HasEquippedIceSkates(UnitActor unit)
    {
        if (unit?.Inventory == null)
            return false;
        foreach (var it in unit.Inventory.Items)
        {
            if (it.Kind == ItemKind.IceSkates && it.Equipped)
                return true;
        }
        return false;
    }

    public static bool IsArmor(ItemKind kind)
        => kind == ItemKind.WoodArmor || kind == ItemKind.IronArmor || kind == ItemKind.RubberRaincoat
            || kind == ItemKind.ThornsArmor || kind == ItemKind.TacticalVest;

    public static bool HasEquippedTacticalVest(UnitActor unit)
    {
        if (unit?.Inventory == null)
            return false;
        foreach (var it in unit.Inventory.Items)
        {
            if (it.Kind == ItemKind.TacticalVest && it.Equipped)
                return true;
        }
        return false;
    }

    public static int GetTacticalVestBagBonus(UnitActor unit)
        => HasEquippedTacticalVest(unit) ? 5 : 0;

    public static bool HasEquippedThornsArmor(UnitActor unit)
    {
        if (unit?.Inventory == null)
            return false;
        foreach (var it in unit.Inventory.Items)
        {
            if (it.Kind == ItemKind.ThornsArmor && it.Equipped)
                return true;
        }
        return false;
    }

    /// <summary>装备橡胶雨衣：免疫雨天防/视减益（不耗耐久）；可挡着火施加（每次耗 1 耐久）。</summary>
    public static bool HasEquippedRubberRaincoat(UnitActor unit)
    {
        if (unit?.Inventory == null)
            return false;
        foreach (var it in unit.Inventory.Items)
        {
            if (it.Kind == ItemKind.RubberRaincoat && it.Equipped)
                return true;
        }
        return false;
    }

    /// <summary>全图视野用曼哈顿半径（覆盖 18×18）。</summary>
    public const int FullMapVisibilityRadius = 40;

    public static bool HasEquippedTelescope(UnitActor unit)
    {
        if (unit?.Inventory == null)
            return false;
        foreach (var it in unit.Inventory.Items)
        {
            if (it.Kind == ItemKind.Telescope && it.Equipped)
                return true;
        }
        return false;
    }

    /// <summary>望远镜生效：已装备，且非黑夜 +（晴天或雨天）（清晨/白天/黄昏均可）。</summary>
    public static bool IsTelescopeVisionActive(UnitActor unit)
    {
        if (!HasEquippedTelescope(unit) || unit.IsDead)
            return false;
        int round = TurnManager.Instance != null ? TurnManager.Instance.RoundNumber : 1;
        if (GameClock.GetPeriod(round) == GameClock.Period.Night)
            return false;
        var w = WeatherService.Current;
        return w == WeatherType.Clear || w == WeatherType.Rain;
    }

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
            case ItemKind.RubberRaincoat: return 3;
            case ItemKind.ThornsArmor: return 2;
            case ItemKind.TacticalVest: return 2;
            default: return 0;
        }
    }

    public static int GetMaxCharges(ItemKind kind)
    {
        switch (kind)
        {
            case ItemKind.WoodArmor: return 5;
            case ItemKind.IronArmor: return 8;
            case ItemKind.RubberRaincoat: return 3;
            case ItemKind.ThornsArmor: return 4;
            case ItemKind.TacticalVest: return 6;
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
            case ItemKind.Milk:
            case ItemKind.WeatherClear:
            case ItemKind.WeatherRain:
            case ItemKind.WeatherFog:
            case ItemKind.RedBull:
                return ItemUseKind.Instant;
            case ItemKind.Reinforce:
                return ItemUseKind.ChooseStat;
            case ItemKind.Bomb:
            case ItemKind.MegaBomb:
            case ItemKind.Flashbang:
            case ItemKind.BananaPeel:
            case ItemKind.Mine:
            case ItemKind.GasolineBottle:
            case ItemKind.Lighter:
                return ItemUseKind.TargetCell;
            case ItemKind.Boomerang:
                return ItemUseKind.TargetUnit;
            case ItemKind.TimedBomb:
                return ItemUseKind.ChooseDelay;
            case ItemKind.Bow:
            case ItemKind.Crossbow:
            case ItemKind.Flamethrower:
            case ItemKind.Dagger:
            case ItemKind.Longsword:
            case ItemKind.ArmorPiercingBlade:
            case ItemKind.CursedBlade:
            case ItemKind.WoodArmor:
            case ItemKind.IronArmor:
            case ItemKind.RubberRaincoat:
            case ItemKind.ThornsArmor:
            case ItemKind.TacticalVest:
            case ItemKind.EnergyShield:
            case ItemKind.NightVision:
            case ItemKind.Telescope:
            case ItemKind.Skateboard:
            case ItemKind.Motorcycle:
            case ItemKind.IceSkates:
            case ItemKind.GrappleHook:
            case ItemKind.Amulet:
                return ItemUseKind.EquipToggle;
            default:
                return ItemUseKind.None;
        }
    }

    public static bool CanDiscard(ItemKind kind) => true;

    public static bool IsWeatherBullet(ItemKind kind)
        => kind == ItemKind.WeatherClear
           || kind == ItemKind.WeatherRain
           || kind == ItemKind.WeatherFog;

    public static bool TryGetWeatherBulletTarget(ItemKind kind, out WeatherType weather)
    {
        switch (kind)
        {
            case ItemKind.WeatherClear:
                weather = WeatherType.Clear;
                return true;
            case ItemKind.WeatherRain:
                weather = WeatherType.Rain;
                return true;
            case ItemKind.WeatherFog:
                weather = WeatherType.Fog;
                return true;
            default:
                weather = WeatherType.Clear;
                return false;
        }
    }
}
