using System.Collections.Generic;
using Unity.MLAgents.Actuators;
using UnityEngine;

/// <summary>按当前局面写离散动作 mask。</summary>
public static class RlActionMask
{
    public static void Write(UnitActor unit, List<UnitActor> allUnits, IDiscreteActionMask mask)
    {
        if (unit == null || mask == null)
            return;

        bool canAct = unit != null
            && !unit.IsDead
            && TurnManager.Instance != null
            && TurnManager.Instance.CurrentUnit == unit
            && TurnManager.Instance.Phase == TurnPhase.WaitingAction
            && !(GameManager.Instance?.IsGameOver ?? false);

        // Branch 0: Op
        for (int op = 0; op < RlActionSpace.OpCount; op++)
        {
            bool ok = canAct && IsOpLegal(unit, allUnits, (RlActionSpace.Op)op);
            if (!ok)
                mask.SetActionEnabled(0, op, false);
        }

        // Axis branches always enable all; illegal cells rejected at execute.
        // Target: enable only living others for combat ops — keep all enabled; executor validates.
    }

    public static bool IsOpLegal(UnitActor unit, List<UnitActor> allUnits, RlActionSpace.Op op)
    {
        if (unit.IsDying)
        {
            return op == RlActionSpace.Op.EndTurn
                || op == RlActionSpace.Op.PotionSmall
                || op == RlActionSpace.Op.PotionLarge
                || op == RlActionSpace.Op.Pickup;
        }

        switch (op)
        {
            case RlActionSpace.Op.EndTurn:
                return true;
            case RlActionSpace.Op.Move:
                return TurnManager.Instance != null && !TurnManager.Instance.HasMoved && !unit.HasStatus(StatusType.Trip);
            case RlActionSpace.Op.Melee:
                return FindMeleeTarget(unit, allUnits) != null;
            case RlActionSpace.Op.Shoot:
                return CanShootAnyone(unit, allUnits);
            case RlActionSpace.Op.Bomb:
                return unit.Inventory != null
                    && (unit.Inventory.CountOf(ItemKind.Bomb) > 0 || unit.Inventory.CountOf(ItemKind.MegaBomb) > 0);
            case RlActionSpace.Op.Pickup:
                // 与 TryPickupAround 的实际拾取范围一致（半径 1 曼哈顿）；背包无空位时不发起
                return unit.Inventory != null
                    && unit.Inventory.HasSpace
                    && GroundItemManager.Instance != null
                    && GroundItemManager.Instance.HasItemsAround(unit.Cell, 1);
            case RlActionSpace.Op.PotionSmall:
                return unit.Inventory != null && unit.Inventory.CountOf(ItemKind.SmallPotion) > 0;
            case RlActionSpace.Op.PotionLarge:
                return unit.Inventory != null && unit.Inventory.CountOf(ItemKind.LargePotion) > 0;
            case RlActionSpace.Op.Equip:
                return HasUnequippedGear(unit);
            case RlActionSpace.Op.Skill:
                return CanSkill(unit, allUnits);
            case RlActionSpace.Op.Leader:
                return LeaderDeclarationService.CanDeclare(unit, out _, out _, out _);
            case RlActionSpace.Op.Upgrade:
                return unit.Inventory != null
                    && unit.Inventory.CountOf(ItemKind.SkillUpgrade) >= 3
                    && unit.SkillLevel < 3;
            default:
                return false;
        }
    }

    public static UnitActor ResolveTarget(UnitActor self, List<UnitActor> all, int targetIndex)
    {
        if (targetIndex <= 0 || all == null)
            return null;
        var others = new List<UnitActor>(3);
        for (int r = 0; r < 4; r++)
        {
            if (r == (int)self.Role)
                continue;
            foreach (var u in all)
            {
                if (u != null && (int)u.Role == r)
                {
                    others.Add(u);
                    break;
                }
            }
        }
        int idx = targetIndex - 1;
        if (idx < 0 || idx >= others.Count)
            return null;
        var t = others[idx];
        if (t == null || t.IsDead)
            return null;
        return t;
    }

    public static UnitActor FindMeleeTarget(UnitActor unit, List<UnitActor> all)
    {
        if (all == null || GridManager.Instance == null)
            return null;
        foreach (var o in all)
        {
            if (o == null || o.IsDead || o == unit)
                continue;
            int d = GridManager.Instance.GetManhattanDistance(unit.Cell, o.Cell);
            if (d <= unit.AttackRange)
                return o;
        }
        return null;
    }

    private static bool CanShootAnyone(UnitActor unit, List<UnitActor> all)
    {
        if (unit.Inventory == null || all == null || GridManager.Instance == null)
            return false;
        int weapon = -1;
        for (int i = 0; i < unit.Inventory.Count; i++)
        {
            var it = unit.Inventory.Items[i];
            if (it.Equipped && ItemInfo.IsRangedWeapon(it.Kind))
            {
                weapon = i;
                break;
            }
        }
        if (weapon < 0)
            return false;
        if (unit.Inventory.TotalAmmoCharges() <= 0)
            return false;
        foreach (var o in all)
        {
            if (o == null || o.IsDead || o == unit)
                continue;
            // Rough range check via weapon bonus
            int range = ItemInfo.GetWeaponRangeBonus(unit.Inventory.Items[weapon].Kind) + unit.GetRangeBonus();
            int d = GridManager.Instance.GetManhattanDistance(unit.Cell, o.Cell);
            if (d <= range && d > 0)
                return true;
        }
        return false;
    }

    private static bool HasUnequippedGear(UnitActor unit)
    {
        if (unit.Inventory == null)
            return false;
        for (int i = 0; i < unit.Inventory.Count; i++)
        {
            var it = unit.Inventory.Items[i];
            if (!it.Equipped && ItemInfo.IsEquipable(it.Kind))
                return true;
        }
        return false;
    }

    private static bool CanSkill(UnitActor unit, List<UnitActor> all)
    {
        if (SkillInfo.IsPassive(unit.Role) || unit.SkillCooldownLeft > 0)
            return false;
        if (!SkillService.CanUseActiveSkill(unit, out _))
            return false;
        if (unit.Role == RoleType.Cat)
            return !unit.HasStatus(StatusType.Hidden);
        if (unit.Role == RoleType.Monkey)
            return FindMeleeTarget(unit, all) != null || true; // radius check deferred
        return true;
    }
}
