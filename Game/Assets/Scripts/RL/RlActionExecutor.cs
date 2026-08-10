using System.Collections.Generic;
using UnityEngine;

/// <summary>将 RL 离散动作译为 Game API 调用。</summary>
public static class RlActionExecutor
{
    public struct Result
    {
        public bool Success;
        public bool EndedTurn;
        public float ShapingReward;
        public int DamageDealt;
        public bool GotKill;
        public bool LeaderDeclared;
    }

    public static Result Execute(UnitActor unit, List<UnitActor> all, int op, int dxAxis, int dyAxis, int targetIdx)
    {
        var result = new Result();
        if (unit == null || unit.IsDead)
            return result;

        var opEnum = (RlActionSpace.Op)Mathf.Clamp(op, 0, RlActionSpace.OpCount - 1);
        if (!RlActionMask.IsOpLegal(unit, all, opEnum) && opEnum != RlActionSpace.Op.EndTurn)
        {
            result.ShapingReward = RlReward.IllegalAction;
            if (TryEndTurn(unit))
                result.EndedTurn = true;
            return result;
        }

        int dx = RlActionSpace.AxisToOffset(Mathf.Clamp(dxAxis, 0, RlActionSpace.AxisCount - 1));
        int dy = RlActionSpace.AxisToOffset(Mathf.Clamp(dyAxis, 0, RlActionSpace.AxisCount - 1));
        var cell = new Vector2Int(unit.Cell.x + dx, unit.Cell.y + dy);
        var target = RlActionMask.ResolveTarget(unit, all, targetIdx);

        switch (opEnum)
        {
            case RlActionSpace.Op.EndTurn:
                result.Success = TryEndTurn(unit);
                result.EndedTurn = result.Success;
                break;
            case RlActionSpace.Op.Move:
                result.Success = ActionService.TryMove(unit, cell);
                break;
            case RlActionSpace.Op.Melee:
                if (target == null)
                    target = RlActionMask.FindMeleeTarget(unit, all);
                if (target != null)
                    result.Success = TryAttackWithReward(unit, target, ref result);
                break;
            case RlActionSpace.Op.Shoot:
                result.Success = TryShoot(unit, target, all, ref result);
                break;
            case RlActionSpace.Op.Bomb:
            {
                // 炸弹范围伤：执行前后扫 HP
                var hpSnap = SnapshotHp(all);
                result.Success = ActionService.TryThrowBomb(unit, cell);
                if (result.Success)
                    AccrueAoEDamage(unit, all, hpSnap, ref result);
                break;
            }
            case RlActionSpace.Op.Pickup:
                // 已满/无空位时不捡，避免超重卡死
                if (unit.Inventory == null || !unit.Inventory.HasSpace)
                    result.Success = false;
                else
                    result.Success = ActionService.TryPickup(unit);
                break;
            case RlActionSpace.Op.PotionSmall:
                result.Success = ActionService.TryUsePotion(unit, ItemKind.SmallPotion)
                    || ActionService.TryUseGroundPotion(unit);
                break;
            case RlActionSpace.Op.PotionLarge:
                result.Success = ActionService.TryUsePotion(unit, ItemKind.LargePotion);
                break;
            case RlActionSpace.Op.Equip:
                result.Success = TryEquipFirst(unit);
                break;
            case RlActionSpace.Op.Skill:
                result.Success = TrySkill(unit, target, all, ref result);
                break;
            case RlActionSpace.Op.Leader:
                result.Success = LeaderDeclarationService.TryDeclare(unit);
                result.LeaderDeclared = result.Success;
                break;
            case RlActionSpace.Op.Upgrade:
                result.Success = ActionService.TryUseSkillUpgrade(unit);
                break;
        }

        if (result.DamageDealt > 0 || result.GotKill)
            result.ShapingReward += RlEpisodeProbe.RewardForAttacker(result.DamageDealt, result.GotKill);

        if (!result.Success && opEnum != RlActionSpace.Op.EndTurn)
        {
            result.ShapingReward += RlReward.IllegalAction;
            if (TryEndTurn(unit))
                result.EndedTurn = true;
        }

        return result;
    }

    private static bool TryAttackWithReward(UnitActor unit, UnitActor target, ref Result result)
    {
        bool alive = target != null && !target.IsDead;
        int hpBefore = target != null ? target.Hp : 0;
        bool ok = ActionService.TryAttack(unit, target);
        if (ok && target != null)
        {
            result.DamageDealt += Mathf.Max(0, hpBefore - target.Hp);
            if (alive && target.IsDead)
                result.GotKill = true;
        }
        return ok;
    }

    private static Dictionary<RoleType, int> SnapshotHp(List<UnitActor> all)
    {
        var d = new Dictionary<RoleType, int>();
        if (all == null)
            return d;
        foreach (var u in all)
        {
            if (u != null)
                d[u.Role] = u.Hp;
        }
        return d;
    }

    private static void AccrueAoEDamage(
        UnitActor attacker,
        List<UnitActor> all,
        Dictionary<RoleType, int> before,
        ref Result result)
    {
        if (all == null || before == null)
            return;
        foreach (var u in all)
        {
            if (u == null || u == attacker)
                continue;
            int prev = before.TryGetValue(u.Role, out var h) ? h : u.Hp;
            int dealt = Mathf.Max(0, prev - u.Hp);
            if (dealt <= 0)
                continue;
            result.DamageDealt += dealt;
            if (prev > 0 && u.IsDead)
                result.GotKill = true;
        }
    }

    private static bool TryEndTurn(UnitActor unit)
    {
        var turn = TurnManager.Instance;
        if (turn == null || turn.Phase == TurnPhase.GameOver)
            return false;
        if (turn.CurrentUnit != unit)
            return false;
        turn.CancelTargeting();
        return turn.RequestEndTurn();
    }

    private static bool TryShoot(UnitActor unit, UnitActor target, List<UnitActor> all, ref Result result)
    {
        if (unit.Inventory == null)
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
        if (target == null)
        {
            foreach (var o in all)
            {
                if (o == null || o.IsDead || o == unit)
                    continue;
                target = o;
                break;
            }
        }
        if (target == null)
            return false;
        bool alive = !target.IsDead;
        int hpBefore = target.Hp;
        bool ok = ActionService.TryShoot(unit, weapon, target);
        if (ok)
        {
            result.DamageDealt += Mathf.Max(0, hpBefore - target.Hp);
            if (alive && target.IsDead)
                result.GotKill = true;
        }
        return ok;
    }

    private static bool TryEquipFirst(UnitActor unit)
    {
        if (unit.Inventory == null)
            return false;
        for (int i = 0; i < unit.Inventory.Count; i++)
        {
            var it = unit.Inventory.Items[i];
            if (!it.Equipped && ItemInfo.IsEquipable(it.Kind))
                return ActionService.TryToggleEquip(unit, i);
        }
        return false;
    }

    private static bool TrySkill(UnitActor unit, UnitActor target, List<UnitActor> all, ref Result result)
    {
        switch (unit.Role)
        {
            case RoleType.Human:
                if (!SkillService.CanUseActiveSkill(unit, out _))
                    return false;
                TurnManager.Instance.EnterSkillReinforce();
                return SkillService.TryConfirmHumanReinforce(unit, StatBoost.Attack);
            case RoleType.Cat:
                return SkillService.TryCatStealth(unit);
            case RoleType.Monkey:
                if (!SkillService.CanUseActiveSkill(unit, out _))
                    return false;
                if (target == null)
                    target = RlActionMask.FindMeleeTarget(unit, all);
                if (target == null)
                    return false;
                int itemIdx = -1;
                for (int i = 0; i < target.Inventory.Count; i++)
                {
                    if (!ItemInfo.IsDoll(target.Inventory.Items[i].Kind))
                    {
                        itemIdx = i;
                        break;
                    }
                }
                if (itemIdx < 0)
                    return false;
                if (!SkillService.TryBeginMonkeySteal(unit))
                    return false;
                if (!SkillService.TryMonkeySelectTarget(unit, target))
                {
                    TurnManager.Instance.CancelTargeting();
                    return false;
                }
                return SkillService.TryMonkeyTakeItem(unit, itemIdx);
            default:
                return false;
        }
    }
}
