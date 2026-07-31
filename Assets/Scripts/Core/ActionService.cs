using System.Collections.Generic;
using UnityEngine;

public enum StatBoost
{
    Attack,
    Defense,
    Move,
    Draw
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
        else if (kind == ItemKind.Flamethrower && unit.FlamethrowerCooldown > 0)
        {
            reason = $"火焰喷射器冷却中（剩{unit.FlamethrowerCooldown}回合）";
            return false;
        }

        if (kind == ItemKind.Adrenaline && unit.Hp * 10 >= unit.MaxHp * 3)
        {
            reason = "仅当 HP < 30% 时可使用肾上腺素";
            return false;
        }

        if (kind == ItemKind.SkillUpgrade)
        {
            if (unit.Inventory.CountOf(ItemKind.SkillUpgrade) < 3)
            {
                reason = "需集齐 3 张技能升级卡才能使用";
                return false;
            }
            if (unit.SkillLevel >= 3)
            {
                reason = "技能等级已达上限";
                return false;
            }
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
                else
                    PlayerInputController.Instance?.StartBombMode(itemIndex);
                return true;

            case ItemUseKind.ChooseDelay:
                TurnManager.Instance.EnterTimedBombDelay(itemIndex);
                TurnManager.Instance.LogFor(unit, "选择定时炸弹延时：1～5 回合后爆炸");
                return true;

            case ItemUseKind.ChooseDirection:
                PlayerInputController.Instance?.StartFlameMode(itemIndex);
                return true;

            case ItemUseKind.TargetUnit:
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
        if (GridManager.Instance.GetManhattanDistance(attacker.Cell, defender.Cell) > attacker.AttackRange)
            return false;

        BreakStealthIfAttacking(attacker, defender);
        int dealt = defender.TakeDamage(attacker.CurrentAtk, magicDamage: false);
        TurnManager.Instance.MarkMeleeAttacked();
        TurnManager.Instance.Log(
            $"{RoleInfo.GetDisplayName(attacker.Role)} 攻击 {RoleInfo.GetDisplayName(defender.Role)}，造成 {dealt} 伤害" +
            (defender.IsDead ? "（击杀）" : defender.IsDying ? "（濒死）" : dealt <= 0 ? "（被挡下）" : $"（剩余HP {defender.Hp}）"));

        GameManager.Instance?.CheckWinConditions();
        return true;
    }

    private static void BreakStealthIfAttacking(UnitActor attacker, UnitActor defender = null)
    {
        if (attacker == null || !attacker.HasStatus(StatusType.Hidden))
            return;
        attacker.ClearStatus(StatusType.Hidden);
        TurnManager.Instance?.Log(
            $"{RoleInfo.GetDisplayName(attacker.Role)} 进行攻击，【隐匿】解除");
        // 猫 Lv3：攻击破隐时，被攻击者获得中毒 1 回合
        if (attacker.Role == RoleType.Cat && attacker.SkillLevel >= 3 && defender != null && !defender.IsDead)
        {
            defender.ApplyStatus(StatusType.Poison, 1, attacker);
            TurnManager.Instance?.Log(
                $"{RoleInfo.GetDisplayName(defender.Role)} 因破隐攻击获得【中毒】");
        }
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

        if (ammo == ItemKind.PoisonArrow)
            defender.ApplyStatus(StatusType.Poison, 1, attacker);
        else if (ammo == ItemKind.FireRocket)
            defender.ApplyStatus(StatusType.Burning, 1, attacker);

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
        if (!unit.TryMoveTo(target))
            return false;

        HazardManager.Instance?.ResolveTrapsAfterMove(unit);
        TurnManager.Instance.MarkMoved();
        TurnManager.Instance.LogFor(unit, $"{RoleInfo.GetDisplayName(unit.Role)} 移动到 ({target.x},{target.y})");
        StealthService.CheckAll();
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
        if (unit.Inventory.IsOverCapacity)
            TurnManager.Instance.LogFor(unit,
                $"背包已超重（{unit.Inventory.UsedWeight:0.##}/{unit.Inventory.Capacity:0.##}），结束行动前需弃置");
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
        if (grid.GetManhattanDistance(unit.Cell, cell) > 1)
        {
            TurnManager.Instance.LogFor(unit, "只能拾取半径 0 或 1 的物品");
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
                $"背包已超重（{unit.Inventory.UsedWeight:0.##}/{unit.Inventory.Capacity:0.##}），结束行动前需弃置");
        unit.RefreshBagCapacity();
        GameManager.Instance?.CheckWinConditions();

        var remaining = GroundItemManager.Instance.GetLootInRange(unit.Cell, 1);
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
        if (grid.GetManhattanDistance(unit.Cell, cell) > 1)
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
        if (TurnManager.Instance.AwaitingCapacityTrim)
        {
            if (unit.Inventory.IsOverCapacity)
            {
                TurnManager.Instance.LogFor(unit,
                    $"仍超重（{unit.Inventory.UsedWeight:0.##}/{unit.Inventory.Capacity:0.##}），请继续弃置");
                TurnManager.Instance.NotifyActionDone();
            }
            else
                TurnManager.Instance.ContinueEndTurnAfterDiscard();
            return;
        }

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
            if (unit.FlamethrowerCooldown > 0)
            {
                TurnManager.Instance.LogFor(unit, $"火焰喷射器冷却中（剩{unit.FlamethrowerCooldown}回合）");
                return false;
            }
            PlayerInputController.Instance?.StartFlameMode(itemIndex);
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

        bool ok = entry.Equipped
            ? unit.Inventory.TryUnequip(itemIndex, out string log)
            : unit.Inventory.TryEquip(itemIndex, out log);
        if (!ok)
        {
            TurnManager.Instance.LogFor(unit, log ?? "无法操作装备");
            return false;
        }

        // 卸下弩时清除蓄力
        if (ok && entry.Kind == ItemKind.Crossbow && entry.Equipped)
            unit.ConsumeCrossbowCharge();

        TurnManager.Instance.LogFor(unit, $"{RoleInfo.GetDisplayName(unit.Role)} {log}");
        TurnManager.Instance.NotifyActionDone();
        return true;
    }

    public static bool TryUsePotion(UnitActor unit, ItemKind kind)
    {
        if (!ItemInfo.IsPotion(kind) || !CanUseOwned(unit, kind))
            return false;

        int heal = ItemInfo.GetPotionHeal(kind);
        unit.Inventory.Remove(kind);
        DeckManager.Instance?.AddToDiscard(kind);
        unit.Heal(heal);
        TurnManager.Instance.CancelTargeting();
        TurnManager.Instance.LogFor(unit,
            $"{RoleInfo.GetDisplayName(unit.Role)} 使用{ItemInfo.GetDisplayName(kind)}，HP={unit.Hp}");
        TurnManager.Instance.NotifyActionDone();
        return true;
    }

    public static bool TryUseSmallPotion(UnitActor unit)
        => TryUsePotion(unit, ItemKind.SmallPotion);

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
        DeckManager.Instance?.AddToDiscard(potion.Kind);
        unit.Heal(heal);
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
        if (unit.Hp * 10 >= unit.MaxHp * 3)
        {
            TurnManager.Instance.LogFor(unit, "仅当 HP < 30% 时可使用肾上腺素");
            return false;
        }

        unit.Inventory.Remove(ItemKind.Adrenaline);
        DeckManager.Instance?.AddToDiscard(ItemKind.Adrenaline);
        unit.TryApplyAdrenaline();
        TurnManager.Instance.CancelTargeting();
        TurnManager.Instance.LogFor(unit,
            $"{RoleInfo.GetDisplayName(unit.Role)} 使用肾上腺素：移+2 攻+3，持续 3 回合");
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
            $"{RoleInfo.GetDisplayName(unit.Role)} 使用 3 张技能升级卡，技能升至 Lv{unit.SkillLevel}");
        TurnManager.Instance.NotifyActionDone();
        return true;
    }

    public static bool TryUseReinforce(UnitActor unit, StatBoost boost)
    {
        if (boost == StatBoost.Draw)
            return false;
        if (!CanUseOwned(unit, ItemKind.Reinforce))
            return false;
        if (unit.IsDying)
            return false;

        unit.Inventory.Remove(ItemKind.Reinforce);
        DeckManager.Instance?.AddToDiscard(ItemKind.Reinforce);
        int amount = boost == StatBoost.Move ? 1 : 3;
        unit.ApplyPermanentBoost(boost, amount);

        string name = boost == StatBoost.Attack ? "攻击" : boost == StatBoost.Defense ? "防御" : "移动";
        TurnManager.Instance.CancelTargeting();
        TurnManager.Instance.LogFor(unit, $"{RoleInfo.GetDisplayName(unit.Role)} 使用强化剂，{name}+{amount}");
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
        if (grid.GetManhattanDistance(unit.Cell, target) > 5)
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
            if (grid.GetManhattanDistance(target, other.Cell) <= 2)
            {
                hits++;
                int dealt = other.TakeDamage(damage, magicDamage: false);
                if (dealt > 0)
                {
                    damaged++;
                    if (wasHidden && unit.Role == RoleType.Cat && unit.SkillLevel >= 3)
                        other.ApplyStatus(StatusType.Poison, 1, unit);
                }
            }
        }

        TurnManager.Instance.CancelTargeting();
        // 爆炸效果公开；投弹本身若想隐藏弹药种类可简化——投掷炸弹是可见攻击行为
        TurnManager.Instance.Log(
            $"{RoleInfo.GetDisplayName(unit.Role)} 投掷【{ItemInfo.GetDisplayName(kind)}】于 ({target.x},{target.y})，" +
            $"覆盖 {hits} 人，{damaged} 人扣血");
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
        if (rounds < 1 || rounds > 5)
            return false;
        if (!CanUseOwned(unit, ItemKind.TimedBomb))
            return false;

        unit.Inventory.Remove(ItemKind.TimedBomb);
        HazardManager.Instance?.PlaceTimedBomb(unit.Cell, rounds, unit.Role);
        TurnManager.Instance.CancelTargeting();
        TurnManager.Instance.LogFor(unit,
            $"{RoleInfo.GetDisplayName(unit.Role)} 在脚下安置定时炸弹，{rounds} 回合后爆炸（仅你可见；猫行动结束后结算）");
        TurnManager.Instance.NotifyActionDone();
        return true;
    }

    public static bool TryFlamethrower(UnitActor unit, Vector2Int dirCell)
    {
        if (unit == null || unit.IsDead || unit.IsDying)
            return false;
        if (TurnManager.Instance.Phase != TurnPhase.SelectingFlameDirection)
            return false;
        if (unit.FlamethrowerCooldown > 0)
            return false;

        int idx = TurnManager.Instance.PendingItemIndex;
        if (idx < 0 || idx >= unit.Inventory.Count || unit.Inventory.Items[idx].Kind != ItemKind.Flamethrower)
            return false;
        if (!unit.Inventory.Items[idx].Equipped)
        {
            TurnManager.Instance.LogFor(unit, "请先装备火焰喷射器");
            return false;
        }

        var grid = GridManager.Instance;
        int dx = dirCell.x - unit.Cell.x;
        int dy = dirCell.y - unit.Cell.y;
        // 四向之一
        if (!((dx == 0 && Mathf.Abs(dy) == 1) || (dy == 0 && Mathf.Abs(dx) == 1)))
            return false;

        int hits = 0, damaged = 0;
        bool wasHidden = unit.HasStatus(StatusType.Hidden);
        for (int step = 1; step <= 5; step++)
        {
            var cell = unit.Cell + new Vector2Int(dx * step, dy * step);
            if (!grid.IsValidCell(cell))
                break;
            var occ = grid.GetOccupant(cell);
            if (occ == null) continue;
            var other = occ.GetComponent<UnitActor>();
            if (other == null || other.IsDead || other == unit) continue;
            hits++;
            int dealt = other.TakeDamage(10, magicDamage: true);
            if (dealt > 0 && !other.IsDead)
                other.ApplyStatus(StatusType.Burning, 1, unit);
            if (dealt > 0)
            {
                damaged++;
                if (wasHidden && unit.Role == RoleType.Cat && unit.SkillLevel >= 3)
                    other.ApplyStatus(StatusType.Poison, 1, unit);
            }
        }

        BreakStealthIfAttacking(unit);
        unit.FlamethrowerCooldown = 3;
        TurnManager.Instance.CancelTargeting();
        TurnManager.Instance.Log(
            $"{RoleInfo.GetDisplayName(unit.Role)} 使用火焰喷射器，命中 {hits} 人，{damaged} 人扣血并着火（冷却3回合）");
        GameManager.Instance?.CheckWinConditions();
        TurnManager.Instance.NotifyActionDone();
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
