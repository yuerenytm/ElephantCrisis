using System.Collections.Generic;
using UnityEngine;

public enum StatBoost
{
    Attack,
    Defense,
    Move
}

/// <summary>统一卡牌/物品使用入口，UI 只调用这里，不写死每种按钮。</summary>
public static class ItemUseService
{
    public static bool CanAttemptUse(UnitActor unit, int itemIndex, out string reason)
    {
        reason = null;
        if (unit == null || unit.IsDead)
        {
            reason = "单位无效";
            return false;
        }
        if (TurnManager.Instance.CurrentUnit != unit || TurnManager.Instance.Phase == TurnPhase.GameOver)
        {
            reason = "不是你的回合";
            return false;
        }
        if (itemIndex < 0 || itemIndex >= unit.Inventory.Count)
        {
            reason = "物品不存在";
            return false;
        }

        var kind = unit.Inventory.Items[itemIndex].Kind;
        var use = ItemInfo.GetUseKind(kind);

        if (use == ItemUseKind.None)
        {
            if (ItemInfo.IsStackableAmmo(kind))
                reason = "弹药请通过弓或弩射击消耗";
            else if (ItemInfo.IsDoll(kind))
                reason = "玩偶不可直接使用";
            else
                reason = "该物品无法使用";
            return false;
        }

        if (unit.IsDying)
        {
            reason = "濒死时不可使用背包卡牌（仅可使用脚下血瓶）";
            return false;
        }

        if (ItemInfo.IsRangedWeapon(kind))
        {
            // 装备操作用不着检查弹药；开火时再查
        }
        else if (kind == ItemKind.Flamethrower)
        {
            // 装备中开火时再查汽油；此处仅拦「已装备却无油」的误触瞄准
            if (unit.Inventory.Items[itemIndex].Equipped
                && unit.Inventory.CountOf(ItemKind.GasolineBottle) <= 0)
            {
                reason = "火焰喷射器需要【汽油瓶】作为弹药";
                return false;
            }
        }

        if (kind == ItemKind.Adrenaline && unit.Hp * 100 >= unit.MaxHp * GameRulesConfig.AdrenalineHpThresholdPct)
        {
            reason = $"仅当 HP < {GameRulesConfig.AdrenalineHpThresholdPct}% 时可使用肾上腺素";
            return false;
        }

        if (kind == ItemKind.SkillUpgrade)
        {
            if (unit.Inventory.CountOf(ItemKind.SkillUpgrade) < GameRulesConfig.SkillUpgradeCards)
            {
                reason = $"需集齐 {GameRulesConfig.SkillUpgradeCards} 张技能升级卡才能使用";
                return false;
            }
            if (unit.SkillLevel >= GameRulesConfig.SkillLevelMax)
            {
                reason = "技能等级已达上限";
                return false;
            }
        }

        if (kind == ItemKind.Milk
            && !unit.HasStatus(StatusType.Poison)
            && !unit.HasStatus(StatusType.Burning)
            && !unit.HasStatus(StatusType.Trip))
        {
            reason = "没有可清除的中毒 / 着火 / 跌倒";
            return false;
        }

        if (ItemInfo.TryGetWeatherBulletTarget(kind, out var targetWeather)
            && WeatherService.Current == targetWeather)
        {
            reason = $"当前已是{WeatherService.GetDisplayName(targetWeather)}";
            return false;
        }

        if (ItemInfo.IsMolotovKit(kind) && !ItemInfo.CanThrowMolotov(unit))
        {
            reason = "需同时持有【打火机】与【汽油瓶】才能点燃投掷";
            return false;
        }

        return true;
    }

    public static bool TryBeginUse(UnitActor unit, int itemIndex)
    {
        if (!CanAttemptUse(unit, itemIndex, out string reason))
        {
            TurnManager.Instance?.LogFor(unit, reason);
            return false;
        }

        var kind = unit.Inventory.Items[itemIndex].Kind;
        switch (ItemInfo.GetUseKind(kind))
        {
            case ItemUseKind.Instant:
                if (kind == ItemKind.SmallPotion || kind == ItemKind.LargePotion)
                    return ActionService.TryUsePotion(unit, kind);
                if (kind == ItemKind.Adrenaline)
                    return ActionService.TryUseAdrenaline(unit);
                if (kind == ItemKind.SkillUpgrade)
                    return ActionService.TryUseSkillUpgrade(unit);
                if (kind == ItemKind.Milk)
                    return ActionService.TryUseMilk(unit);
                if (ItemInfo.IsWeatherBullet(kind))
                    return ActionService.TryUseWeatherBullet(unit, kind);
                if (kind == ItemKind.RedBull)
                    return ActionService.TryUseRedBull(unit);
                return false;

            case ItemUseKind.ChooseStat:
                TurnManager.Instance.EnterReinforceChoice(itemIndex);
                TurnManager.Instance.LogFor(unit, "选择强化：攻击 / 防御 / 移动");
                return true;

            case ItemUseKind.TargetCell:
                if (kind == ItemKind.BananaPeel)
                    PlayerInputController.Instance?.StartBananaMode(itemIndex);
                else if (kind == ItemKind.Mine)
                    PlayerInputController.Instance?.StartMineMode(itemIndex);
                else if (kind == ItemKind.Flashbang)
                    PlayerInputController.Instance?.StartFlashbangMode(itemIndex);
                else if (ItemInfo.IsMolotovKit(kind))
                    PlayerInputController.Instance?.StartMolotovMode(itemIndex);
                else
                    PlayerInputController.Instance?.StartBombMode(itemIndex);
                return true;

            case ItemUseKind.ChooseDelay:
                TurnManager.Instance.EnterTimedBombDelay(itemIndex);
                TurnManager.Instance.LogFor(unit, $"选择定时炸弹延时：{GameRulesConfig.TimedBombMinRounds}～{GameRulesConfig.TimedBombMaxRounds} 回合（×{GameRulesConfig.ActionsPerRound} 行动，含本次）后爆炸");
                return true;

            case ItemUseKind.ChooseDirection:
                PlayerInputController.Instance?.StartFlameMode(itemIndex);
                return true;

            case ItemUseKind.TargetUnit:
                if (kind == ItemKind.Boomerang)
                    PlayerInputController.Instance?.StartBoomerangMode(itemIndex);
                else
                    PlayerInputController.Instance?.StartShootMode(itemIndex);
                return true;

            case ItemUseKind.EquipToggle:
                return ActionService.TryUseEquipable(unit, itemIndex);

            default:
                return false;
        }
    }

    public static bool TryConfirmReinforce(UnitActor unit, StatBoost boost)
    {
        if (TurnManager.Instance.Phase != TurnPhase.SelectingReinforce)
            return false;
        return ActionService.TryUseReinforce(unit, boost);
    }

    public static bool TryConfirmTimedBombDelay(UnitActor unit, int rounds)
    {
        if (TurnManager.Instance.Phase != TurnPhase.SelectingTimedBombDelay)
            return false;
        return ActionService.TryPlaceTimedBomb(unit, rounds);
    }

    public static bool TryConfirmAmmo(UnitActor unit, ItemKind ammo)
    {
        if (TurnManager.Instance.Phase != TurnPhase.SelectingAmmo)
            return false;
        if (unit == null)
            return false;
        int weaponIndex = TurnManager.Instance.PendingItemIndex;
        if (weaponIndex < 0 || weaponIndex >= unit.Inventory.Count)
            return false;
        var weapon = unit.Inventory.Items[weaponIndex].Kind;
        int need = ItemInfo.GetArrowCost(weapon);
        if (unit.Inventory.CountOf(ammo) < need)
        {
            TurnManager.Instance.LogFor(unit, $"【{ItemInfo.GetDisplayName(ammo)}】不足（需要{need}）");
            return false;
        }
        PlayerInputController.Instance?.StartShootModeWithAmmo(weaponIndex, ammo);
        return true;
    }
}

public static class ActionService
{
    public static bool TryAttack(UnitActor attacker, UnitActor defender)
    {
        if (attacker == null || defender == null)
            return false;
        if (attacker.IsDead || defender.IsDead)
            return false;
        if (!StealthService.CanTargetDespiteHidden(attacker, defender))
        {
            TurnManager.Instance.LogFor(attacker, "目标处于隐匿，无法攻击（需邻接）");
            return false;
        }
        if (!VisibilityService.CanSeeCell(attacker, defender.Cell))
        {
            TurnManager.Instance.LogFor(attacker, "目标在能见度之外");
            return false;
        }
        if (TurnManager.Instance.HasMeleeAttacked)
        {
            TurnManager.Instance.LogFor(attacker, "本回合普通攻击已使用");
            return false;
        }
        int dist = GridManager.Instance.GetManhattanDistance(attacker.Cell, defender.Cell);
        if (dist > attacker.AttackRange)
            return false;
        // 隐匿者仅邻接可打（长剑距 2 也打不到）——已由 CanTargetDespiteHidden 保证

        if (ItemInfo.HasEquippedCursedBlade(attacker))
            return TryCursedBladeAttack(attacker, defender);

        BreakStealthIfAttacking(attacker, defender);
        int raw = attacker.MeleeAtk;
        // 猫的普攻为法伤（法系输出定位：穿高物防，但受目标法抗减免、不耗甲耐久）
        bool magicMelee = attacker.Role == RoleType.Cat;
        bool thorns = ItemInfo.HasEquippedThornsArmor(defender);
        bool pierce = ItemInfo.MeleeIgnoresArmor(attacker);
        int dealt = defender.TakeDamage(raw, magicDamage: magicMelee, ignoreArmorDefense: pierce && !magicMelee);
        LogicMatchLogger.Active?.EmitDamage(
            LogicSimNaming.Role(attacker.Role),
            LogicSimNaming.Role(defender.Role),
            magicMelee ? "magic" : "physical", raw, dealt,
            magicMelee ? "melee_magic" : pierce ? "melee_pierce" : "melee");
        TurnManager.Instance.MarkMeleeAttacked();
        TurnManager.Instance.Log(
            $"{RoleInfo.GetDisplayName(attacker.Role)} 近战攻击 {RoleInfo.GetDisplayName(defender.Role)}" +
            (magicMelee ? "（法伤）" : pierce ? "（破甲）" : "") +
            $"，造成 {dealt} 伤害" +
            (defender.IsDead ? "（击杀）" : defender.IsDying ? "（濒死）" : dealt <= 0 ? (magicMelee ? "（法抗抵消）" : "（被挡下）") : $"（剩余HP {defender.Hp}）"));

        if (thorns)
            TryThornsReflect(attacker, defender, dealt);
        GameManager.Instance?.CheckWinConditions();
        return true;
    }

    /// <summary>
    /// 诅咒之刃：第 x 次使用对目标 2x 真伤、自身 x 真伤（无来源）；本行动已移动则不可用。
    /// </summary>
    private static bool TryCursedBladeAttack(UnitActor attacker, UnitActor defender)
    {
        if (TurnManager.Instance.HasMoved)
        {
            TurnManager.Instance.LogFor(attacker, "本行动已移动，不可使用【诅咒之刃】");
            return false;
        }

        int idx = -1;
        for (int i = 0; i < attacker.Inventory.Count; i++)
        {
            var it = attacker.Inventory.Items[i];
            if (it.Equipped && it.Kind == ItemKind.CursedBlade)
            {
                idx = i;
                break;
            }
        }
        if (idx < 0)
            return false;

        int x = Mathf.Max(0, attacker.Inventory.Items[idx].Charges) + 1;
        int targetRaw = GameRulesConfig.CursedBladeTargetMult * x;
        int selfRaw = GameRulesConfig.CursedBladeSelfMult * x;

        BreakStealthIfAttacking(attacker, defender);

        int dealt = defender.TakeDamage(targetRaw, trueDamage: true);
        LogicMatchLogger.Active?.EmitDamage(
            "none",
            LogicSimNaming.Role(defender.Role),
            "true", targetRaw, dealt, "cursed_blade");

        var blade = attacker.Inventory.Items[idx];
        blade.Charges = x;
        attacker.Inventory.Items[idx] = blade;

        int selfDealt = 0;
        if (!attacker.IsDead)
        {
            selfDealt = attacker.TakeDamage(selfRaw, trueDamage: true);
            LogicMatchLogger.Active?.EmitDamage(
                "none",
                LogicSimNaming.Role(attacker.Role),
                "true", selfRaw, selfDealt, "cursed_blade_recoil");
        }

        TurnManager.Instance.MarkMeleeAttacked();
        TurnManager.Instance.Log(
            $"{RoleInfo.GetDisplayName(attacker.Role)} 使用【诅咒之刃】第{x}次：" +
            $"{RoleInfo.GetDisplayName(defender.Role)} 受 {dealt} 真伤，自身受 {selfDealt} 真伤" +
            (defender.IsDead ? "（目标死亡）" : defender.IsDying ? "（目标濒死）" : "") +
            (attacker.IsDead ? "（自身死亡）" : attacker.IsDying ? "（自身濒死）" : ""));
        GameManager.Instance?.CheckWinConditions();
        return true;
    }

    /// <summary>荆棘护甲：被近战打中后，对攻击者反弹实际伤害 50%（向下取整）真伤。</summary>
    private static void TryThornsReflect(UnitActor attacker, UnitActor defender, int dealt)
    {
        if (attacker == null || defender == null || attacker.IsDead || dealt <= 0)
            return;

        int reflect = dealt * GameRulesConfig.ThornsReflectPct / 100;
        if (reflect <= 0)
            return;

        int rDealt = attacker.TakeDamage(reflect, trueDamage: true);
        LogicMatchLogger.Active?.EmitDamage(
            "none",
            LogicSimNaming.Role(attacker.Role),
            "true", reflect, rDealt, "thorns");
        TurnManager.Instance?.Log(
            $"{RoleInfo.GetDisplayName(defender.Role)} 的【荆棘护甲】反弹 {rDealt} 真伤给 {RoleInfo.GetDisplayName(attacker.Role)}" +
            (attacker.IsDead ? "（击杀）" : attacker.IsDying ? "（濒死）" : $"（剩余HP {attacker.Hp}）"));
    }

    private static void BreakStealthIfAttacking(UnitActor attacker, UnitActor defender = null)
    {
        if (attacker != null && attacker.HasStatus(StatusType.Hidden))
        {
            attacker.ClearStatus(StatusType.Hidden, "break_attack");
            TurnManager.Instance?.Log(
                $"{RoleInfo.GetDisplayName(attacker.Role)} 进行攻击，【隐匿】解除");
            // 猫技能满级：攻击破隐时，被攻击者获得中毒（回合数见 game_rules.yaml）
            if (attacker.Role == RoleType.Cat && attacker.SkillLevel >= GameRulesConfig.SkillLevelMax && defender != null && !defender.IsDead)
            {
                defender.ApplyStatus(StatusType.Poison, GameRulesConfig.PoisonRounds, attacker);
                TurnManager.Instance?.Log(
                    $"{RoleInfo.GetDisplayName(defender.Role)} 因破隐攻击获得【中毒】");
            }
        }

        BreakStealthIfAttacked(defender);
    }

    /// <summary>被攻击（含 AOE 覆盖）后解除隐匿。</summary>
    private static void BreakStealthIfAttacked(UnitActor defender)
    {
        if (defender == null || defender.IsDead || !defender.HasStatus(StatusType.Hidden))
            return;
        defender.ClearStatus(StatusType.Hidden, "break_attacked");
        TurnManager.Instance?.Log(
            $"{RoleInfo.GetDisplayName(defender.Role)} 被攻击，【隐匿】解除");
    }

    public static bool TryShoot(UnitActor attacker, int weaponIndex, UnitActor defender)
    {
        if (attacker == null || defender == null)
            return false;
        if (attacker.IsDead || attacker.IsDying || defender.IsDead)
            return false;
        if (!StealthService.CanTargetDespiteHidden(attacker, defender))
        {
            TurnManager.Instance.LogFor(attacker, "目标处于隐匿，无法射击（需邻接）");
            return false;
        }
        if (!VisibilityService.CanSeeCell(attacker, defender.Cell))
        {
            TurnManager.Instance.LogFor(attacker, "目标在能见度之外");
            return false;
        }
        if (weaponIndex < 0 || weaponIndex >= attacker.Inventory.Count)
            return false;

        var weapon = attacker.Inventory.Items[weaponIndex].Kind;
        if (!ItemInfo.IsRangedWeapon(weapon))
            return false;
        if (!attacker.Inventory.Items[weaponIndex].Equipped)
        {
            TurnManager.Instance.LogFor(attacker, "请先装备该武器");
            return false;
        }
        if (weapon == ItemKind.Crossbow && !attacker.CanFireCrossbow())
        {
            TurnManager.Instance.LogFor(attacker, "弩需先蓄力，且不可在同一行动内射击");
            return false;
        }

        int cost = ItemInfo.GetArrowCost(weapon);
        var ammo = TurnManager.Instance.PendingAmmoKind;
        if (!ItemInfo.IsStackableAmmo(ammo))
            ammo = ItemKind.Arrow;
        if (attacker.Inventory.CountOf(ammo) < cost)
            return false;

        int range = attacker.GetShootRange(weapon);
        if (GridManager.Instance.GetManhattanDistance(attacker.Cell, defender.Cell) > range)
            return false;

        var spent = new List<ItemKind>();
        if (!attacker.Inventory.TryConsumeAmmo(ammo, cost, spent))
            return false;
        foreach (var a in spent)
            DeckManager.Instance?.AddToDiscard(a);

        if (weapon == ItemKind.Crossbow)
            attacker.ConsumeCrossbowCharge();

        BreakStealthIfAttacking(attacker, defender);
        int power = attacker.CurrentAtk + ItemInfo.GetWeaponAtkBonus(weapon);
        int dealt = defender.TakeDamage(power, magicDamage: false);
        LogicMatchLogger.Active?.EmitDamage(
            LogicSimNaming.Role(attacker.Role),
            LogicSimNaming.Role(defender.Role),
            "physical", power, dealt, "shoot");

        if (ammo == ItemKind.PoisonArrow)
            defender.ApplyStatus(StatusType.Poison, GameRulesConfig.PoisonRounds, attacker);
        else if (ammo == ItemKind.FireRocket)
            defender.ApplyStatus(StatusType.Burning, GameRulesConfig.BurningRounds, attacker);

        TurnManager.Instance.CancelTargeting();
        if (MatchConfig.IsAiBattle && !MatchConfig.IsHumanControlled(attacker))
        {
            TurnManager.Instance.Log(
                $"{RoleInfo.GetDisplayName(attacker.Role)} 射击 {RoleInfo.GetDisplayName(defender.Role)}，伤害{dealt}" +
                (defender.IsDead ? "（击杀）" : defender.IsDying ? "（濒死）" : dealt <= 0 ? "（被挡）" : $"（HP{defender.Hp}）"));
        }
        else
        {
            TurnManager.Instance.Log(
                $"{RoleInfo.GetDisplayName(attacker.Role)} 用【{ItemInfo.GetDisplayName(weapon)}】+【{ItemInfo.GetDisplayName(ammo)}】射击 " +
                $"{RoleInfo.GetDisplayName(defender.Role)}，攻{power}，伤害{dealt}" +
                (defender.IsDead ? "（击杀）" : defender.IsDying ? "（濒死）" : dealt <= 0 ? "（被挡）" : $"（HP{defender.Hp}）"));
        }

        GameManager.Instance?.CheckWinConditions();
        TurnManager.Instance.NotifyActionDone();
        return true;
    }

    public static bool TryMove(UnitActor unit, Vector2Int target)
    {
        if (unit == null || unit.IsDead)
            return false;
        if (TurnManager.Instance.HasMoved)
            return false;
        var from = unit.Cell;
        if (!unit.TryMoveTo(target))
            return false;

        HazardManager.Instance?.ResolveTrapsAfterMove(unit);
        TurnManager.Instance.MarkMoved();
        LogicMatchLogger.Active?.EmitMove(unit, from, unit.Cell);
        if (unit.LastMoveBumped && unit.LastMoveBumpedUnit != null)
        {
            var bumped = unit.LastMoveBumpedUnit;
            TurnManager.Instance.LogFor(unit,
                $"{RoleInfo.GetDisplayName(unit.Role)} 撞上隐匿的{RoleInfo.GetDisplayName(bumped.Role)}（{unit.LastMoveIntendedCell.x},{unit.LastMoveIntendedCell.y}），弹至 ({unit.Cell.x},{unit.Cell.y})（对方仍隐匿，仅邻接可见）");
        }
        else
        {
            TurnManager.Instance.LogFor(unit,
                $"{RoleInfo.GetDisplayName(unit.Role)} 移动到 ({unit.Cell.x},{unit.Cell.y})");
        }
        StealthService.CheckAll();
        if (!MatchConfig.IsLogicSim)
            VisibilityService.RefreshWorld();
        return true;
    }

    public static bool TryPickup(UnitActor unit)
    {
        if (unit == null || unit.IsDead || unit.IsDying)
            return false;

        int n = GroundItemManager.Instance.TryPickupAround(unit.Cell, unit.Inventory);
        if (n <= 0)
        {
            TurnManager.Instance.LogFor(unit, "附近没有可拾取物品");
            return false;
        }

        TurnManager.Instance.LogFor(unit, $"{RoleInfo.GetDisplayName(unit.Role)} 拾取了 {n} 件物品");
        LogicMatchLogger.Active?.EmitPickup(unit, n);
        if (unit.Inventory.IsOverCapacity)
            TurnManager.Instance.LogFor(unit,
                $"背包已超重（{unit.Inventory.UsedWeight:0.##}/{unit.Inventory.Capacity:0.##}），超重时无法结束行动");
        unit.RefreshBagCapacity();
        GameManager.Instance?.CheckWinConditions();
        TurnManager.Instance.NotifyActionDone();
        return true;
    }

    public static bool TryPickupOne(UnitActor unit, Vector2Int cell, int groundIndex)
    {
        if (unit == null || unit.IsDead)
        {
            TurnManager.Instance?.LogFor(unit, "拾取失败：单位无效");
            return false;
        }
        if (unit.IsDying)
        {
            TurnManager.Instance.LogFor(unit, "濒死中无法拾取");
            return false;
        }
        if (TurnManager.Instance.CurrentUnit != unit)
        {
            TurnManager.Instance.LogFor(unit, "拾取失败：不是你的回合");
            return false;
        }
        if (TurnManager.Instance.Phase != TurnPhase.SelectingPickup)
        {
            TurnManager.Instance.LogFor(unit, "拾取失败：请先点击「拾取」进入拾取模式");
            return false;
        }

        var grid = GridManager.Instance;
        if (grid.GetManhattanDistance(unit.Cell, cell) > GameRulesConfig.PickupRange)
        {
            TurnManager.Instance.LogFor(unit, $"只能拾取半径 0 或 {GameRulesConfig.PickupRange} 的物品");
            return false;
        }

        var peek = GroundItemManager.Instance.Peek(cell);
        if (groundIndex < 0 || groundIndex >= peek.Count)
        {
            TurnManager.Instance.LogFor(unit, "拾取失败：掉落物已不存在，请重试");
            return false;
        }
        var kind = peek[groundIndex].Kind;

        if (!GroundItemManager.Instance.TryPickupOne(cell, groundIndex, unit.Inventory))
        {
            TurnManager.Instance.LogFor(unit, "拾取失败（掉落物已不存在）");
            return false;
        }

        // 地面物品消失是公开信息；具体谁捡了什么仅对本方详细记录
        if (MatchConfig.IsAiBattle && !MatchConfig.IsHumanControlled(unit))
            TurnManager.Instance.Log($"({cell.x},{cell.y}) 的掉落物被拾取");
        else
            TurnManager.Instance.Log(
                $"{RoleInfo.GetDisplayName(unit.Role)} 拾取了【{ItemInfo.GetDisplayName(kind)}】于 ({cell.x},{cell.y})");
        if (unit.Inventory.IsOverCapacity)
            TurnManager.Instance.LogFor(unit,
                $"背包已超重（{unit.Inventory.UsedWeight:0.##}/{unit.Inventory.Capacity:0.##}），超重时无法结束行动");
        unit.RefreshBagCapacity();
        GameManager.Instance?.CheckWinConditions();

        var remaining = GroundItemManager.Instance.GetLootInRange(unit.Cell, GameRulesConfig.PickupRange);
        if (remaining.Count == 0)
            TurnManager.Instance.CancelTargeting();
        else
            TurnManager.Instance.NotifyActionDone();
        return true;
    }

    public static bool TryDiscard(UnitActor unit, int itemIndex, Vector2Int cell)
    {
        if (unit == null || unit.IsDead)
            return false;
        if (TurnManager.Instance.CurrentUnit != unit)
            return false;
        if (TurnManager.Instance.Phase == TurnPhase.GameOver)
            return false;
        if (itemIndex < 0 || itemIndex >= unit.Inventory.Count)
            return false;

        var grid = GridManager.Instance;
        if (!grid.IsValidCell(cell))
            return false;
        if (grid.GetManhattanDistance(unit.Cell, cell) > GameRulesConfig.PickupRange)
            return false;

        var item = unit.Inventory.Items[itemIndex];
        item.Equipped = false;
        unit.Inventory.RemoveAt(itemIndex);

        GroundItemManager.Instance.DropItem(cell, item);

        TurnManager.Instance.CancelTargeting();
        if (MatchConfig.IsAiBattle && !MatchConfig.IsHumanControlled(unit))
            TurnManager.Instance.Log($"【{ItemInfo.GetDisplayName(item.Kind)}】出现于 ({cell.x},{cell.y})");
        else
            TurnManager.Instance.Log(
                $"{RoleInfo.GetDisplayName(unit.Role)} 弃置【{ItemInfo.GetDisplayName(item.Kind)}】到 ({cell.x},{cell.y})");

        FinishDiscardSideEffects(unit);
        return true;
    }

    private static void FinishDiscardSideEffects(UnitActor unit)
    {
        unit.RefreshBagCapacity();
        TurnManager.Instance.NotifyActionDone();
    }

    /// <summary>AI/系统：自动弃置到脚下，直到不超重（优先弃非玩偶、未穿戴）。</summary>
    public static void AutoDiscardToCapacity(UnitActor unit)
    {
        if (unit == null || unit.IsDead || unit.Inventory == null)
            return;

        int guard = 48;
        while (unit.Inventory.IsOverCapacity && guard-- > 0)
        {
            int idx = FindAutoDiscardIndex(unit);
            if (idx < 0)
                break;
            if (!TryDiscard(unit, idx, unit.Cell))
                break;
        }
    }

    private static int FindAutoDiscardIndex(UnitActor unit)
    {
        int best = -1;
        float bestScore = float.MinValue;
        var items = unit.Inventory.Items;
        for (int i = 0; i < items.Count; i++)
        {
            var it = items[i];
            if (ItemInfo.IsDoll(it.Kind))
                continue;
            float score = ItemInfo.GetWeight(it);
            if (it.Equipped)
                score -= 20f;
            if (ItemInfo.IsStackableAmmo(it.Kind))
                score += 5f;
            if (ItemInfo.IsArmor(it.Kind) || it.Kind == ItemKind.EnergyShield)
                score -= 8f;
            if (score > bestScore)
            {
                bestScore = score;
                best = i;
            }
        }

        // 实在只剩玩偶仍超重时，被迫弃玩偶以外已无物——不再弃玩偶
        return best;
    }

    /// <summary>装备栏：未装备则装备；甲/盾已装备则卸下；武器已装备则发动（射击/蓄力/喷射）。</summary>
    public static bool TryUseEquipable(UnitActor unit, int itemIndex)
    {
        if (unit == null || unit.IsDead || unit.IsDying)
            return false;
        if (TurnManager.Instance.CurrentUnit != unit || TurnManager.Instance.Phase != TurnPhase.WaitingAction)
            return false;
        if (itemIndex < 0 || itemIndex >= unit.Inventory.Count)
            return false;

        var entry = unit.Inventory.Items[itemIndex];
        if (!ItemInfo.IsEquipable(entry.Kind))
            return false;

        if (!entry.Equipped)
            return TryToggleEquip(unit, itemIndex);

        // 已装备：武器发动，其余卸下
        if (entry.Kind == ItemKind.Bow)
        {
            if (unit.Inventory.TotalAmmoCharges() < 1)
            {
                TurnManager.Instance.LogFor(unit, "弹药不足");
                return false;
            }
            PlayerInputController.Instance?.StartShootMode(itemIndex);
            return true;
        }
        if (entry.Kind == ItemKind.Crossbow)
        {
            if (!unit.CrossbowCharged)
            {
                if (!unit.TryChargeCrossbow())
                {
                    TurnManager.Instance.LogFor(unit, "无法蓄力");
                    return false;
                }
                TurnManager.Instance.LogFor(unit, $"{RoleInfo.GetDisplayName(unit.Role)} 为弩蓄力（本行动不可射击）");
                TurnManager.Instance.NotifyActionDone();
                return true;
            }
            if (!unit.CanFireCrossbow())
            {
                TurnManager.Instance.LogFor(unit, "弩刚蓄力，须等到下一次行动才能射击");
                return false;
            }
            if (unit.Inventory.TotalAmmoCharges() < 1)
            {
                TurnManager.Instance.LogFor(unit, "弹药不足");
                return false;
            }
            PlayerInputController.Instance?.StartShootMode(itemIndex);
            return true;
        }
        if (entry.Kind == ItemKind.Flamethrower)
        {
            if (unit.Inventory.CountOf(ItemKind.GasolineBottle) <= 0)
            {
                TurnManager.Instance.LogFor(unit, "火焰喷射器需要【汽油瓶】作为弹药，无法单独喷射");
                return false;
            }
            PlayerInputController.Instance?.StartFlameMode(itemIndex);
            return true;
        }
        if (entry.Kind == ItemKind.Motorcycle)
        {
            if (!entry.Equipped)
                return TryToggleEquip(unit, itemIndex);
            // 已装备：未发动 → 发动；已发动且未移动 → 冲击；否则卸下
            if (unit.MotorcycleActiveRounds <= 0)
                return TryStartMotorcycle(unit, itemIndex);
            if (TurnManager.Instance.HasMoved)
                return TryToggleEquip(unit, itemIndex);
            PlayerInputController.Instance?.StartMotorcycleRamMode(itemIndex);
            return true;
        }
        if (entry.Kind == ItemKind.GrappleHook)
        {
            PlayerInputController.Instance?.StartHookMode(itemIndex);
            return true;
        }

        return TryToggleEquip(unit, itemIndex);
    }

    public static bool TryToggleEquip(UnitActor unit, int itemIndex)
    {
        if (unit == null || unit.IsDead || unit.IsDying)
            return false;
        if (TurnManager.Instance.CurrentUnit != unit)
            return false;
        if (TurnManager.Instance.Phase != TurnPhase.WaitingAction)
            return false;
        if (itemIndex < 0 || itemIndex >= unit.Inventory.Count)
            return false;

        var entry = unit.Inventory.Items[itemIndex];
        if (!unit.Inventory.IsEquipable(entry.Kind))
            return false;

        bool wasEquipped = entry.Equipped;
        bool ok = wasEquipped
            ? unit.Inventory.TryUnequip(itemIndex, out string log)
            : unit.Inventory.TryEquip(itemIndex, out log);
        if (!ok)
        {
            TurnManager.Instance.LogFor(unit, log ?? "无法操作装备");
            return false;
        }

        // 卸下弩时清除蓄力；卸下摩托熄火
        if (ok && wasEquipped)
        {
            if (entry.Kind == ItemKind.Crossbow)
                unit.ConsumeCrossbowCharge();
            if (entry.Kind == ItemKind.Motorcycle)
                unit.MotorcycleActiveRounds = 0;
        }

        if (!wasEquipped)
            LogicMatchLogger.Active?.EmitEquip(unit, entry.Kind);

        TurnManager.Instance.LogFor(unit, $"{RoleInfo.GetDisplayName(unit.Role)} {log}");
        unit.RefreshBagCapacity();
        if (!MatchConfig.IsLogicSim)
            VisibilityService.RefreshWorld();
        TurnManager.Instance.NotifyActionDone();
        return true;
    }

    public static bool TryUsePotion(UnitActor unit, ItemKind kind)
    {
        if (!ItemInfo.IsPotion(kind) || !CanUseOwned(unit, kind))
            return false;

        int heal = ItemInfo.GetPotionHeal(kind);
        int hpBefore = unit.Hp;
        unit.Inventory.Remove(kind);
        DeckManager.Instance?.AddToDiscard(kind);
        unit.Heal(heal);
        LogicMatchLogger.Active?.EmitHeal(unit, kind, unit.Hp - hpBefore, unit.Hp);
        TurnManager.Instance.CancelTargeting();
        TurnManager.Instance.LogFor(unit,
            $"{RoleInfo.GetDisplayName(unit.Role)} 使用{ItemInfo.GetDisplayName(kind)}，HP={unit.Hp}");
        TurnManager.Instance.NotifyActionDone();
        return true;
    }

    public static bool TryUseSmallPotion(UnitActor unit)
        => TryUsePotion(unit, ItemKind.SmallPotion);

    /// <summary>牛奶：清除自身中毒（全部层）、着火、跌倒。</summary>
    public static bool TryUseMilk(UnitActor unit)
    {
        if (!CanUseOwned(unit, ItemKind.Milk))
            return false;
        if (unit.IsDying)
            return false;

        bool hasBad = unit.HasStatus(StatusType.Poison)
            || unit.HasStatus(StatusType.Burning)
            || unit.HasStatus(StatusType.Trip);
        if (!hasBad)
        {
            TurnManager.Instance.LogFor(unit, "没有可清除的中毒 / 着火 / 跌倒");
            return false;
        }

        var cleared = new List<string>(3);
        if (unit.HasStatus(StatusType.Poison))
        {
            unit.ClearStatus(StatusType.Poison, "clear_milk");
            cleared.Add("中毒");
        }
        if (unit.HasStatus(StatusType.Burning))
        {
            unit.ClearStatus(StatusType.Burning, "clear_milk");
            cleared.Add("着火");
        }
        if (unit.HasStatus(StatusType.Trip))
        {
            unit.ClearStatus(StatusType.Trip, "clear_milk");
            cleared.Add("跌倒");
        }

        unit.Inventory.Remove(ItemKind.Milk);
        DeckManager.Instance?.AddToDiscard(ItemKind.Milk);
        TurnManager.Instance.CancelTargeting();
        TurnManager.Instance.LogFor(unit,
            $"{RoleInfo.GetDisplayName(unit.Role)} 喝下牛奶，清除了{string.Join("、", cleared)}");
        TurnManager.Instance.NotifyActionDone();
        return true;
    }

    /// <summary>天气弹：立刻将天气转为对应天气（已是该天气不可用）。</summary>
    public static bool TryUseWeatherBullet(UnitActor unit, ItemKind kind)
    {
        if (!ItemInfo.TryGetWeatherBulletTarget(kind, out var target))
            return false;
        if (!CanUseOwned(unit, kind))
            return false;
        if (unit.IsDying)
            return false;
        if (WeatherService.Current == target)
        {
            TurnManager.Instance.LogFor(unit, $"当前已是{WeatherService.GetDisplayName(target)}");
            return false;
        }

        unit.Inventory.Remove(kind);
        DeckManager.Instance?.AddToDiscard(kind);
        TurnManager.Instance.CancelTargeting();
        TurnManager.Instance.LogFor(unit,
            $"{RoleInfo.GetDisplayName(unit.Role)} 发射【{ItemInfo.GetDisplayName(kind)}】");
        // ApplyWeather 内会 NotifyActionDone，并 RefreshAfterVisionRuleChange（迷雾半径/移动提示）
        if (!WeatherService.TryApplyFromCard(target))
            TurnManager.Instance.NotifyActionDone();
        return true;
    }

    /// <summary>红牛：本行动结束后额外行动一次（可叠加次数）。</summary>
    public static bool TryUseRedBull(UnitActor unit)
    {
        if (!CanUseOwned(unit, ItemKind.RedBull))
            return false;
        if (unit.IsDying)
            return false;

        unit.Inventory.Remove(ItemKind.RedBull);
        DeckManager.Instance?.AddToDiscard(ItemKind.RedBull);
        unit.PendingExtraActions = Mathf.Max(0, unit.PendingExtraActions) + 1;
        TurnManager.Instance.CancelTargeting();
        TurnManager.Instance.LogFor(unit,
            $"{RoleInfo.GetDisplayName(unit.Role)} 喝下【红牛】（行动结束后额外行动×{unit.PendingExtraActions}）");
        TurnManager.Instance.NotifyActionDone();
        return true;
    }

    /// <summary>濒死自救：直接使用自己脚下格子上的血瓶（不经背包）。</summary>
    public static bool TryUseGroundPotion(UnitActor unit)
    {
        if (unit == null || unit.IsDead)
            return false;
        if (TurnManager.Instance.CurrentUnit != unit || TurnManager.Instance.Phase == TurnPhase.GameOver)
            return false;
        if (!unit.IsDying)
        {
            TurnManager.Instance.LogFor(unit, "仅濒死时可直接使用脚下血瓶");
            return false;
        }

        var ground = GroundItemManager.Instance;
        if (ground == null || !ground.TryTakeFirstPotion(unit.Cell, out var potion))
        {
            TurnManager.Instance.LogFor(unit, "脚下没有血瓶");
            return false;
        }

        int heal = ItemInfo.GetPotionHeal(potion.Kind);
        int hpBefore = unit.Hp;
        DeckManager.Instance?.AddToDiscard(potion.Kind);
        unit.Heal(heal);
        LogicMatchLogger.Active?.EmitHeal(unit, potion.Kind, unit.Hp - hpBefore, unit.Hp);
        TurnManager.Instance.CancelTargeting();
        TurnManager.Instance.LogFor(unit,
            $"{RoleInfo.GetDisplayName(unit.Role)} 使用脚下【{ItemInfo.GetDisplayName(potion.Kind)}】，HP={unit.Hp}");
        GameManager.Instance?.CheckWinConditions();
        TurnManager.Instance.NotifyActionDone();
        return true;
    }

    public static bool HasGroundPotionAt(UnitActor unit)
    {
        if (unit == null || GroundItemManager.Instance == null)
            return false;
        return GroundItemManager.Instance.HasPotionAt(unit.Cell);
    }

    public static bool TryUseAdrenaline(UnitActor unit)
    {
        if (!CanUseOwned(unit, ItemKind.Adrenaline))
            return false;
        if (unit.IsDying)
            return false;
        if (unit.Hp * 100 >= unit.MaxHp * GameRulesConfig.AdrenalineHpThresholdPct)
        {
            TurnManager.Instance.LogFor(unit, $"仅当 HP < {GameRulesConfig.AdrenalineHpThresholdPct}% 时可使用肾上腺素");
            return false;
        }

        unit.Inventory.Remove(ItemKind.Adrenaline);
        DeckManager.Instance?.AddToDiscard(ItemKind.Adrenaline);
        unit.TryApplyAdrenaline();
        TurnManager.Instance.CancelTargeting();
        TurnManager.Instance.LogFor(unit,
            $"{RoleInfo.GetDisplayName(unit.Role)} 使用肾上腺素：移+{GameRulesConfig.AdrenalineMove} 攻+{GameRulesConfig.AdrenalineAtk}，持续 {GameRulesConfig.AdrenalineDuration} 回合");
        TurnManager.Instance.NotifyActionDone();
        return true;
    }

    public static bool TryUseSkillUpgrade(UnitActor unit)
    {
        if (unit == null || unit.IsDead || unit.IsDying)
            return false;
        if (TurnManager.Instance == null || TurnManager.Instance.CurrentUnit != unit)
            return false;
        if (TurnManager.Instance.Phase != TurnPhase.WaitingAction)
            return false;
        if (!unit.TryConsumeSkillUpgradeCards(out string reason))
        {
            TurnManager.Instance.LogFor(unit, reason);
            return false;
        }

        TurnManager.Instance.CancelTargeting();
        TurnManager.Instance.LogFor(unit,
            $"{RoleInfo.GetDisplayName(unit.Role)} 使用 {GameRulesConfig.SkillUpgradeCards} 张技能升级卡，技能升至 Lv{unit.SkillLevel}");
        TurnManager.Instance.NotifyActionDone();
        return true;
    }

    public static bool TryUseReinforce(UnitActor unit, StatBoost boost)
    {
        if (boost != StatBoost.Attack && boost != StatBoost.Defense && boost != StatBoost.Move)
            return false;
        if (!CanUseOwned(unit, ItemKind.Reinforce))
            return false;
        if (unit.IsDying)
            return false;

        unit.Inventory.Remove(ItemKind.Reinforce);
        DeckManager.Instance?.AddToDiscard(ItemKind.Reinforce);
        int amount = GameRulesConfig.ReinforceAmount; // 攻/防/移永久加成，见 game_rules.yaml
        unit.ApplyPermanentBoost(boost, amount);

        string name = boost == StatBoost.Attack ? "攻击" : boost == StatBoost.Defense ? "防御" : "移动";
        TurnManager.Instance.CancelTargeting();
        TurnManager.Instance.LogFor(unit, $"{RoleInfo.GetDisplayName(unit.Role)} 使用强化剂，{name}+{amount}");
        TurnManager.Instance.NotifyActionDone();
        return true;
    }

    /// <summary>回旋镖：射程 4、15 物伤；击杀（死亡）回手，否则进弃牌。</summary>
    public static bool TryThrowBoomerang(UnitActor unit, UnitActor defender)
    {
        if (unit == null || defender == null || unit.IsDead || unit.IsDying || defender.IsDead)
            return false;
        if (TurnManager.Instance.CurrentUnit != unit)
            return false;
        if (TurnManager.Instance.Phase != TurnPhase.SelectingShootTarget)
            return false;

        int idx = TurnManager.Instance.PendingItemIndex;
        if (idx < 0 || idx >= unit.Inventory.Count || unit.Inventory.Items[idx].Kind != ItemKind.Boomerang)
            return false;

        if (!StealthService.CanTargetDespiteHidden(unit, defender))
        {
            TurnManager.Instance.LogFor(unit, "目标处于隐匿，无法投掷（需邻接）");
            return false;
        }
        if (!VisibilityService.CanSeeCell(unit, defender.Cell))
        {
            TurnManager.Instance.LogFor(unit, "目标在能见度之外");
            return false;
        }

        var grid = GridManager.Instance;
        if (grid.GetManhattanDistance(unit.Cell, defender.Cell) > ItemInfo.BoomerangRange)
            return false;

        unit.Inventory.RemoveAt(idx);
        BreakStealthIfAttacking(unit, defender);

        int damage = ItemInfo.BoomerangDamage;
        int dealt = defender.TakeDamage(damage, magicDamage: false);
        LogicMatchLogger.Active?.EmitDamage(
            LogicSimNaming.Role(unit.Role),
            LogicSimNaming.Role(defender.Role),
            "physical", damage, dealt, "boomerang");

        bool killed = defender.IsDead;
        if (killed)
        {
            unit.Inventory.Add(ItemKind.Boomerang);
            unit.RefreshBagCapacity();
        }
        else
            DeckManager.Instance?.AddToDiscard(ItemKind.Boomerang);

        TurnManager.Instance.CancelTargeting();
        TurnManager.Instance.Log(
            $"{RoleInfo.GetDisplayName(unit.Role)} 投掷【回旋镖】击中 {RoleInfo.GetDisplayName(defender.Role)}，" +
            $"物伤结算 {dealt}" +
            (killed ? "（击杀，回旋镖回手）" : defender.IsDying ? "（濒死，回旋镖弃牌）" : dealt <= 0 ? "（被挡，回旋镖弃牌）" : $"（HP{defender.Hp}，回旋镖弃牌）"));
        GameManager.Instance?.CheckWinConditions();
        TurnManager.Instance.NotifyActionDone();
        return true;
    }

    public static bool TryThrowBomb(UnitActor unit, Vector2Int target)
    {
        if (unit == null || unit.IsDead || unit.IsDying)
            return false;
        if (TurnManager.Instance.CurrentUnit != unit)
            return false;
        if (TurnManager.Instance.Phase != TurnPhase.SelectingBombTarget)
            return false;

        int idx = TurnManager.Instance.PendingItemIndex;
        if (idx < 0 || idx >= unit.Inventory.Count)
            return false;

        var kind = unit.Inventory.Items[idx].Kind;
        if (!ItemInfo.IsThrowableBomb(kind))
            return false;

        var grid = GridManager.Instance;
        if (grid.GetManhattanDistance(unit.Cell, target) > GameRulesConfig.BombRange)
            return false;

        int damage = ItemInfo.GetBombDamage(kind);
        unit.Inventory.RemoveAt(idx);
        DeckManager.Instance?.AddToDiscard(kind);
        bool wasHidden = unit.HasStatus(StatusType.Hidden);
        BreakStealthIfAttacking(unit);

        int hits = 0;
        int damaged = 0;
        foreach (var other in GameManager.Instance.Units)
        {
            if (other == null || other.IsDead)
                continue;
            if (grid.GetManhattanDistance(target, other.Cell) <= GameRulesConfig.BombBlastRadius)
            {
                hits++;
                BreakStealthIfAttacked(other);
                int dealt = other.TakeDamage(damage, magicDamage: false, fromBombOrMine: true);
                LogicMatchLogger.Active?.EmitDamage(
                    LogicSimNaming.Role(unit.Role),
                    LogicSimNaming.Role(other.Role),
                    "physical", damage, dealt, "bomb");
                if (dealt > 0)
                {
                    damaged++;
                    if (wasHidden && unit.Role == RoleType.Cat && unit.SkillLevel >= GameRulesConfig.SkillLevelMax)
                        other.ApplyStatus(StatusType.Poison, GameRulesConfig.PoisonRounds, unit);
                }
            }
        }

        TurnManager.Instance.CancelTargeting();
        LogicMatchLogger.Active?.EmitBomb(unit, target);
        // 爆炸效果公开；投弹本身若想隐藏弹药种类可简化——投掷炸弹是可见攻击行为
        TurnManager.Instance.Log(
            $"{RoleInfo.GetDisplayName(unit.Role)} 投掷【{ItemInfo.GetDisplayName(kind)}】于 ({target.x},{target.y})，" +
            $"覆盖 {hits} 人，{damaged} 人扣血");
        GameManager.Instance?.CheckWinConditions();
        TurnManager.Instance.NotifyActionDone();
        return true;
    }

    /// <summary>
    /// 打火机点燃汽油瓶投掷：耗汽油瓶，保留打火机；爆点范围内法伤（火焰），铺火焰并立刻着火（数值见 game_rules.yaml）。
    /// </summary>
    public static bool TryThrowMolotov(UnitActor unit, Vector2Int target)
    {
        if (unit == null || unit.IsDead || unit.IsDying)
            return false;
        if (TurnManager.Instance.CurrentUnit != unit)
            return false;
        if (TurnManager.Instance.Phase != TurnPhase.SelectingBombTarget)
            return false;
        if (!ItemInfo.CanThrowMolotov(unit))
            return false;

        int gasIdx = unit.Inventory.Items.FindIndex(i => i.Kind == ItemKind.GasolineBottle);
        if (gasIdx < 0)
            return false;

        var grid = GridManager.Instance;
        if (grid.GetManhattanDistance(unit.Cell, target) > GameRulesConfig.MolotovRange)
            return false;

        unit.Inventory.RemoveAt(gasIdx);
        DeckManager.Instance?.AddToDiscard(ItemKind.GasolineBottle);
        bool wasHidden = unit.HasStatus(StatusType.Hidden);
        BreakStealthIfAttacking(unit);

        int blast = GameRulesConfig.MolotovRadius;
        int damage = GameRulesConfig.MolotovDamage;
        int hits = 0;
        int damaged = 0;
        foreach (var other in GameManager.Instance.Units)
        {
            if (other == null || other.IsDead)
                continue;
            if (grid.GetManhattanDistance(target, other.Cell) > blast)
                continue;
            hits++;
            BreakStealthIfAttacked(other);
            int dealt = other.TakeDamage(damage, magicDamage: true);
            LogicMatchLogger.Active?.EmitDamage(
                LogicSimNaming.Role(unit.Role),
                LogicSimNaming.Role(other.Role),
                "magic", damage, dealt, "molotov");
            if (dealt > 0)
            {
                damaged++;
                if (wasHidden && unit.Role == RoleType.Cat && unit.SkillLevel >= GameRulesConfig.SkillLevelMax)
                    other.ApplyStatus(StatusType.Poison, GameRulesConfig.PoisonRounds, unit);
            }
            other.ApplyStatus(StatusType.Burning, GameRulesConfig.BurningRounds, unit);
        }

        HazardManager.Instance?.PlaceFlameArea(target, blast, 2);
        // 铺火后再扫一次：爆点内角色（含未扣血）立刻着火
        foreach (var other in GameManager.Instance.Units)
        {
            if (other == null || other.IsDead)
                continue;
            if (grid.GetManhattanDistance(target, other.Cell) <= blast)
                HazardManager.Instance?.ResolveFlameOnCell(other);
        }

        TurnManager.Instance.CancelTargeting();
        TurnManager.Instance.Log(
            $"{RoleInfo.GetDisplayName(unit.Role)} 点燃【汽油瓶】投于 ({target.x},{target.y})，" +
            $"覆盖 {hits} 人，{damaged} 人扣血；爆点半径 {blast} 留下火焰 {GameRulesConfig.MolotovFlameRounds} 回合");
        GameManager.Instance?.CheckWinConditions();
        TurnManager.Instance.NotifyActionDone();
        return true;
    }

    public static bool TryThrowBanana(UnitActor unit, Vector2Int target)
    {
        if (unit == null || unit.IsDead || unit.IsDying)
            return false;
        if (TurnManager.Instance.CurrentUnit != unit)
            return false;
        if (TurnManager.Instance.Phase != TurnPhase.SelectingBananaTarget)
            return false;

        int idx = TurnManager.Instance.PendingItemIndex;
        if (idx < 0 || idx >= unit.Inventory.Count)
            return false;
        if (unit.Inventory.Items[idx].Kind != ItemKind.BananaPeel)
            return false;

        var grid = GridManager.Instance;
        if (grid.GetManhattanDistance(unit.Cell, target) > unit.AttackRange)
            return false;

        unit.Inventory.RemoveAt(idx);
        HazardManager.Instance?.PlaceBananaTrap(target, unit.Role);
        TurnManager.Instance.CancelTargeting();
        TurnManager.Instance.LogFor(unit,
            $"{RoleInfo.GetDisplayName(unit.Role)} 投掷香蕉皮于 ({target.x},{target.y})（仅你可见）");
        TurnManager.Instance.NotifyActionDone();
        return true;
    }

    public static bool TryPlaceMine(UnitActor unit, Vector2Int target)
    {
        if (unit == null || unit.IsDead || unit.IsDying)
            return false;
        if (TurnManager.Instance.CurrentUnit != unit)
            return false;
        if (TurnManager.Instance.Phase != TurnPhase.SelectingMineTarget)
            return false;

        int idx = TurnManager.Instance.PendingItemIndex;
        if (idx < 0 || idx >= unit.Inventory.Count)
            return false;
        if (unit.Inventory.Items[idx].Kind != ItemKind.Mine)
            return false;

        var grid = GridManager.Instance;
        if (grid.GetManhattanDistance(unit.Cell, target) > unit.AttackRange)
            return false;

        unit.Inventory.RemoveAt(idx);
        HazardManager.Instance?.PlaceMine(target);
        TurnManager.Instance.CancelTargeting();
        TurnManager.Instance.Log(
            $"{RoleInfo.GetDisplayName(unit.Role)} 在 ({target.x},{target.y}) 安置了地雷");
        TurnManager.Instance.NotifyActionDone();
        return true;
    }

    public static bool TryPlaceTimedBomb(UnitActor unit, int rounds)
    {
        if (unit == null || unit.IsDead || unit.IsDying)
            return false;
        if (rounds < GameRulesConfig.TimedBombMinRounds || rounds > GameRulesConfig.TimedBombMaxRounds)
            return false;
        if (!CanUseOwned(unit, ItemKind.TimedBomb))
            return false;

        unit.Inventory.Remove(ItemKind.TimedBomb);
        HazardManager.Instance?.PlaceTimedBomb(unit.Cell, rounds, unit.Role);
        TurnManager.Instance.CancelTargeting();
        TurnManager.Instance.LogFor(unit,
            $"{RoleInfo.GetDisplayName(unit.Role)} 在脚下安置定时炸弹，{rounds} 回合（{rounds * GameRulesConfig.ActionsPerRound} 个行动，含本次）后爆炸（仅你可见）");
        TurnManager.Instance.NotifyActionDone();
        return true;
    }

    public static bool TryFlamethrower(UnitActor unit, Vector2Int dirCell)
    {
        if (unit == null || unit.IsDead || unit.IsDying)
            return false;
        if (TurnManager.Instance.Phase != TurnPhase.SelectingFlameDirection)
            return false;

        int idx = TurnManager.Instance.PendingItemIndex;
        if (idx < 0 || idx >= unit.Inventory.Count || unit.Inventory.Items[idx].Kind != ItemKind.Flamethrower)
            return false;
        if (!unit.Inventory.Items[idx].Equipped)
        {
            TurnManager.Instance.LogFor(unit, "请先装备火焰喷射器");
            return false;
        }

        int gasIdx = unit.Inventory.Items.FindIndex(i => i.Kind == ItemKind.GasolineBottle);
        if (gasIdx < 0)
        {
            TurnManager.Instance.LogFor(unit, "火焰喷射器需要【汽油瓶】作为弹药");
            return false;
        }

        var grid = GridManager.Instance;
        int dx = dirCell.x - unit.Cell.x;
        int dy = dirCell.y - unit.Cell.y;
        // 四向之一
        if (!((dx == 0 && Mathf.Abs(dy) == 1) || (dy == 0 && Mathf.Abs(dx) == 1)))
            return false;

        // 开火瞬间耗油（与弓耗箭一致：先扣弹药再结算）
        unit.Inventory.RemoveAt(gasIdx);
        DeckManager.Instance?.AddToDiscard(ItemKind.GasolineBottle);

        var stepDir = new Vector2Int(dx, dy);
        int flameLen = GameRulesConfig.FlamethrowerRange;
        int flameRounds = GameRulesConfig.FlamethrowerFlameRounds;

        int hits = 0, damaged = 0;
        bool wasHidden = unit.HasStatus(StatusType.Hidden);
        for (int step = 1; step <= flameLen; step++)
        {
            var cell = unit.Cell + stepDir * step;
            if (!grid.IsValidCell(cell))
                break;
            var occ = grid.GetOccupant(cell);
            if (occ == null) continue;
            var other = occ.GetComponent<UnitActor>();
            if (other == null || other.IsDead || other == unit) continue;
            hits++;
            BreakStealthIfAttacked(other);
            int dealt = other.TakeDamage(GameRulesConfig.FlamethrowerDamage, magicDamage: true);
            if (dealt > 0 && !other.IsDead)
                other.ApplyStatus(StatusType.Burning, GameRulesConfig.BurningRounds, unit);
            if (dealt > 0)
            {
                damaged++;
                if (wasHidden && unit.Role == RoleType.Cat && unit.SkillLevel >= GameRulesConfig.SkillLevelMax)
                    other.ApplyStatus(StatusType.Poison, GameRulesConfig.PoisonRounds, unit);
            }
        }

        // 喷射路径铺火焰 2 完整回合（与燃瓶一致）；雨天不铺
        HazardManager.Instance?.PlaceFlameLine(unit.Cell, stepDir, flameLen, flameRounds);
        for (int step = 1; step <= flameLen; step++)
        {
            var cell = unit.Cell + stepDir * step;
            if (!grid.IsValidCell(cell))
                break;
            var occ = grid.GetOccupant(cell);
            if (occ == null) continue;
            var other = occ.GetComponent<UnitActor>();
            if (other == null || other.IsDead)
                continue;
            HazardManager.Instance?.ResolveFlameOnCell(other);
        }

        BreakStealthIfAttacking(unit);
        TurnManager.Instance.CancelTargeting();
        TurnManager.Instance.Log(
            $"{RoleInfo.GetDisplayName(unit.Role)} 使用火焰喷射器（耗【汽油瓶】×{GameRulesConfig.FuelCost}），命中 {hits} 人，{damaged} 人扣血；路径火焰 {flameRounds} 回合（{flameRounds * GameRulesConfig.ActionsPerRound} 行动）");
        GameManager.Instance?.CheckWinConditions();
        TurnManager.Instance.NotifyActionDone();
        return true;
    }

    public static bool TryThrowFlashbang(UnitActor unit, Vector2Int target)
    {
        if (unit == null || unit.IsDead || unit.IsDying)
            return false;
        if (TurnManager.Instance.CurrentUnit != unit)
            return false;
        if (TurnManager.Instance.Phase != TurnPhase.SelectingBombTarget)
            return false;

        int idx = TurnManager.Instance.PendingItemIndex;
        if (idx < 0 || idx >= unit.Inventory.Count || unit.Inventory.Items[idx].Kind != ItemKind.Flashbang)
            return false;

        var grid = GridManager.Instance;
        if (grid.GetManhattanDistance(unit.Cell, target) > GameRulesConfig.FlashbangRange)
            return false;

        unit.Inventory.RemoveAt(idx);
        DeckManager.Instance?.AddToDiscard(ItemKind.Flashbang);
        BreakStealthIfAttacking(unit);

        int hits = 0;
        foreach (var other in GameManager.Instance.Units)
        {
            if (other == null || other.IsDead)
                continue;
            if (grid.GetManhattanDistance(target, other.Cell) > GameRulesConfig.FlashbangRadius)
                continue;
            BreakStealthIfAttacked(other);
            other.ApplyStatus(StatusType.Blind, GameRulesConfig.BlindRounds, unit);
            hits++;
        }

        TurnManager.Instance.CancelTargeting();
        TurnManager.Instance.Log(
            $"{RoleInfo.GetDisplayName(unit.Role)} 投掷闪光弹于 ({target.x},{target.y})，{hits} 人获得【致盲】{GameRulesConfig.FlashbangBlindRounds}回合");
        VisibilityService.RefreshWorld();
        TurnManager.Instance.NotifyActionDone();
        return true;
    }

    /// <summary>发动摩托车：耗汽油瓶，持续若干完整回合（期间移加速且可冲击，数值见 game_rules.yaml）。</summary>
    public static bool TryStartMotorcycle(UnitActor unit, int itemIndex)
    {
        if (unit == null || unit.IsDead || unit.IsDying)
            return false;
        if (TurnManager.Instance.CurrentUnit != unit)
            return false;
        if (TurnManager.Instance.Phase != TurnPhase.WaitingAction)
            return false;
        if (itemIndex < 0 || itemIndex >= unit.Inventory.Count
            || unit.Inventory.Items[itemIndex].Kind != ItemKind.Motorcycle)
            return false;
        if (!unit.Inventory.Items[itemIndex].Equipped)
        {
            TurnManager.Instance.LogFor(unit, "请先装备摩托车");
            return false;
        }
        if (unit.MotorcycleActiveRounds > 0)
        {
            TurnManager.Instance.LogFor(unit, $"摩托车已发动（剩 {unit.MotorcycleActiveRounds} 回合）");
            return false;
        }

        int gasIdx = unit.Inventory.Items.FindIndex(i => i.Kind == ItemKind.GasolineBottle);
        if (gasIdx < 0)
        {
            TurnManager.Instance.LogFor(unit, $"发动摩托车需要【汽油瓶】×{GameRulesConfig.FuelCost}");
            return false;
        }

        unit.Inventory.RemoveAt(gasIdx);
        DeckManager.Instance?.AddToDiscard(ItemKind.GasolineBottle);
        unit.MotorcycleActiveRounds = GameRulesConfig.MotorcycleDuration;
        TurnManager.Instance.Log(
            $"{RoleInfo.GetDisplayName(unit.Role)} 发动摩托车（耗【汽油瓶】×{GameRulesConfig.FuelCost}），持续 {GameRulesConfig.MotorcycleDuration} 回合：移+{GameRulesConfig.MotorcycleMove} / 可冲击");
        TurnManager.Instance.NotifyActionDone();
        return true;
    }

    public static bool TryMotorcycleRam(UnitActor unit, Vector2Int target)
    {
        if (unit == null || unit.IsDead || unit.IsDying)
            return false;
        if (TurnManager.Instance.CurrentUnit != unit)
            return false;
        if (TurnManager.Instance.Phase != TurnPhase.SelectingMotorcycleRam)
            return false;
        if (TurnManager.Instance.HasMoved)
            return false;
        if (unit.MotorcycleActiveRounds <= 0)
        {
            TurnManager.Instance.LogFor(unit, "请先发动摩托车（耗汽油瓶）");
            return false;
        }

        int idx = TurnManager.Instance.PendingItemIndex;
        if (idx < 0 || idx >= unit.Inventory.Count || unit.Inventory.Items[idx].Kind != ItemKind.Motorcycle)
            return false;
        if (!unit.Inventory.Items[idx].Equipped)
        {
            TurnManager.Instance.LogFor(unit, "请先装备摩托车");
            return false;
        }

        var grid = GridManager.Instance;
        int dx = target.x - unit.Cell.x;
        int dy = target.y - unit.Cell.y;
        int dist = Mathf.Abs(dx) + Mathf.Abs(dy);
        if (dist < GameRulesConfig.MotorcycleRamMin || dist > GameRulesConfig.MotorcycleRamMax)
            return false;
        if (!((dx == 0 && dy != 0) || (dy == 0 && dx != 0)))
            return false;

        int sx = dx == 0 ? 0 : (dx > 0 ? 1 : -1);
        int sy = dy == 0 ? 0 : (dy > 0 ? 1 : -1);

        // 终点须可站：无可见占格；路径上的人受伤但不挡落点
        if (StealthService.BlocksMovementFor(unit, target))
        {
            TurnManager.Instance.LogFor(unit, "冲击终点被占据");
            return false;
        }
        for (int step = 1; step < dist; step++)
        {
            var cell = unit.Cell + new Vector2Int(sx * step, sy * step);
            if (!grid.IsValidCell(cell))
                return false;
        }
        if (!grid.IsValidCell(target))
            return false;

        Vector2Int from = unit.Cell;
        int damage = GameRulesConfig.MotorcycleRamDamage;
        int hits = 0;
        bool wasHidden = unit.HasStatus(StatusType.Hidden);
        var hitUnits = new System.Collections.Generic.HashSet<UnitActor>();

        // 3×移动格数 矩形：沿路径长 dist、垂直宽 3（中心线 ±1）
        for (int step = 1; step <= dist; step++)
        {
            var center = from + new Vector2Int(sx * step, sy * step);
            for (int w = -1; w <= 1; w++)
            {
                var cell = sx != 0
                    ? new Vector2Int(center.x, center.y + w)
                    : new Vector2Int(center.x + w, center.y);
                if (!grid.IsValidCell(cell))
                    continue;
                var occ = StealthService.GetOccupantUnit(cell);
                if (occ == null || occ == unit || !hitUnits.Add(occ))
                    continue;
                hits++;
                BreakStealthIfAttacked(occ);
                int dealt = occ.TakeDamage(damage, magicDamage: false);
                if (!occ.IsDead)
                    occ.ApplyStatus(StatusType.Stun, GameRulesConfig.StunRounds, unit);
                if (dealt > 0 && wasHidden && unit.Role == RoleType.Cat && unit.SkillLevel >= GameRulesConfig.SkillLevelMax)
                    occ.ApplyStatus(StatusType.Poison, GameRulesConfig.PoisonRounds, unit);
            }
        }

        if (hits > 0)
            BreakStealthIfAttacking(unit);

        // 落点：若终点有不可见隐匿者则弹回逻辑
        var endOcc = StealthService.GetOccupantUnit(target);
        if (endOcc != null && endOcc != unit && !VisibilityService.CanSeeUnit(unit, endOcc))
        {
            Vector2Int land = StealthService.GetBumpLandCell(from, target);
            unit.PlaceAt(land, true);
            unit.ApplyTerrainEnterEffects();
        }
        else if (endOcc == null)
        {
            unit.PlaceAt(target, true);
            unit.ApplyTerrainEnterEffects();
        }
        else
        {
            var stop = from + new Vector2Int(sx * (dist - 1), sy * (dist - 1));
            if (stop != from && grid.IsValidCell(stop) && !StealthService.BlocksMovementFor(unit, stop))
            {
                unit.PlaceAt(stop, true);
                unit.ApplyTerrainEnterEffects();
            }
        }

        HazardManager.Instance?.ResolveTrapsAfterMove(unit);
        TurnManager.Instance.MarkMoved();
        TurnManager.Instance.CancelTargeting();
        TurnManager.Instance.Log(
            $"{RoleInfo.GetDisplayName(unit.Role)} 摩托车冲击至 ({unit.Cell.x},{unit.Cell.y})，" +
            $"宽{GameRulesConfig.MotorcycleRamWidth}×{dist} 矩形命中 {hits} 人（{GameRulesConfig.MotorcycleRamDamage}物伤+晕眩{GameRulesConfig.StunRounds}；发动剩{unit.MotorcycleActiveRounds}回合）");
        GameManager.Instance?.CheckWinConditions();
        VisibilityService.RefreshWorld();
        TurnManager.Instance.NotifyActionDone();
        return true;
    }

    public static bool TryHookSelectTarget(UnitActor unit, UnitActor target)
    {
        if (unit == null || target == null || unit.IsDead || target.IsDead || target == unit)
            return false;
        if (TurnManager.Instance.Phase != TurnPhase.SelectingHookTarget)
            return false;
        if (GridManager.Instance.GetManhattanDistance(unit.Cell, target.Cell) > GameRulesConfig.GrappleHookRange)
        {
            TurnManager.Instance.LogFor(unit, $"目标超出勾爪半径 {GameRulesConfig.GrappleHookRange}");
            return false;
        }
        if (!VisibilityService.CanSeeUnit(unit, target))
        {
            TurnManager.Instance.LogFor(unit, "看不见该目标");
            return false;
        }
        if (!StealthService.CanTargetDespiteHidden(unit, target))
        {
            TurnManager.Instance.LogFor(unit, "目标处于隐匿（需邻接）");
            return false;
        }

        bool hasLoot = false;
        foreach (var it in target.Inventory.Items)
        {
            if (!ItemInfo.IsDoll(it.Kind))
            {
                hasLoot = true;
                break;
            }
        }
        if (!hasLoot)
        {
            TurnManager.Instance.LogFor(unit, "目标没有可抢的非玩偶物品");
            return false;
        }

        TurnManager.Instance.EnterHookItemPick(target);
        TurnManager.Instance.LogFor(unit,
            $"查看 {RoleInfo.GetDisplayName(target.Role)} 的物品，点选一件夺取（勾爪用后损毁）");
        return true;
    }

    public static bool TryHookTakeItem(UnitActor unit, int itemIndex)
    {
        var turn = TurnManager.Instance;
        if (unit == null || turn == null || !turn.PendingHookSteal)
            return false;
        var target = turn.PendingSkillTargetUnit;
        if (target == null || turn.Phase != TurnPhase.SelectingMonkeyMarkItem)
            return false;

        int hookIdx = turn.PendingItemIndex;
        if (hookIdx < 0 || hookIdx >= unit.Inventory.Count
            || unit.Inventory.Items[hookIdx].Kind != ItemKind.GrappleHook
            || !unit.Inventory.Items[hookIdx].Equipped)
            return false;

        if (itemIndex < 0 || itemIndex >= target.Inventory.Count)
            return false;
        var item = target.Inventory.Items[itemIndex];
        if (ItemInfo.IsDoll(item.Kind))
        {
            turn.LogFor(unit, "不可抢夺玩偶");
            return false;
        }
        if (GridManager.Instance.GetManhattanDistance(unit.Cell, target.Cell) > GameRulesConfig.GrappleHookRange)
            return false;

        target.Inventory.RemoveAt(itemIndex);
        unit.Inventory.Add(item);
        // 勾爪损毁
        unit.Inventory.RemoveAt(hookIdx);
        DeckManager.Instance?.AddToDiscard(ItemKind.GrappleHook);
        unit.RefreshBagCapacity();
        target.RefreshBagCapacity();

        turn.CancelTargeting();
        turn.Log(
            $"{RoleInfo.GetDisplayName(unit.Role)} 用抢夺勾爪获得 {RoleInfo.GetDisplayName(target.Role)} 的【{ItemInfo.GetDisplayName(item.Kind)}】（勾爪损毁）");
        GameManager.Instance?.CheckWinConditions();
        turn.NotifyActionDone();
        return true;
    }

    private static bool CanUseOwned(UnitActor unit, ItemKind kind)
    {
        if (unit == null || unit.IsDead)
            return false;
        if (TurnManager.Instance.CurrentUnit != unit)
            return false;
        if (TurnManager.Instance.Phase == TurnPhase.GameOver)
            return false;
        return unit.Inventory.Contains(kind);
    }
}
