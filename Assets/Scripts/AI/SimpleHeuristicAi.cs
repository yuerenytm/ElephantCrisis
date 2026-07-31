using System.Collections.Generic;
using UnityEngine;

/// <summary>首期启发式 AI：治疗/穿甲 → 射击/近战/炸弹 → 拾取 → 移动。</summary>
public static class SimpleHeuristicAi
{
    public static bool TryActOnce(UnitActor unit)
    {
        if (unit == null || unit.IsDead)
            return false;
        if (TurnManager.Instance == null || TurnManager.Instance.CurrentUnit != unit)
            return false;
        if (TurnManager.Instance.Phase == TurnPhase.GameOver)
            return false;

        // 确保不卡在瞄准态
        if (TurnManager.Instance.Phase != TurnPhase.WaitingAction)
            TurnManager.Instance.CancelTargeting();

        if (unit.IsDying)
            return ActionService.TryUseGroundPotion(unit);

        if (LeaderDeclarationService.CanDeclare(unit, out _, out _, out _))
            return LeaderDeclarationService.TryDeclare(unit);

        if (unit.Inventory != null
            && unit.Inventory.CountOf(ItemKind.SkillUpgrade) >= 3
            && unit.SkillLevel < 3
            && ActionService.TryUseSkillUpgrade(unit))
            return true;

        if (TryUseSkill(unit))
            return true;

        if (unit.Hp * 100 < unit.MaxHp * 35 && TryHeal(unit))
            return true;

        if (TryEquipGear(unit))
            return true;

        if (TryShootBest(unit))
            return true;

        if (TryMeleeBest(unit))
            return true;

        if (TryBombBest(unit))
            return true;

        if (TryPickup(unit))
            return true;

        if (!TurnManager.Instance.HasMoved && TryMoveBest(unit))
            return true;

        return false;
    }

    private static bool TryUseSkill(UnitActor unit)
    {
        if (unit.SkillCooldownLeft > 0 || SkillInfo.IsPassive(unit.Role))
            return false;

        switch (unit.Role)
        {
            case RoleType.Human:
                if (!SkillService.CanUseActiveSkill(unit, out _))
                    return false;
                TurnManager.Instance.EnterSkillReinforce();
                return SkillService.TryConfirmHumanReinforce(unit, StatBoost.Attack);

            case RoleType.Cat:
                if (unit.Hp * 100 < unit.MaxHp * 55 || unit.HasStatus(StatusType.Hidden))
                    return false;
                return SkillService.TryCatStealth(unit);

            case RoleType.Monkey:
                if (!SkillService.CanUseActiveSkill(unit, out _))
                    return false;
                int radius = SkillInfo.GetMonkeyRadius(unit.SkillLevel);
                UnitActor best = null;
                int bestDist = 99;
                int bestItem = -1;
                foreach (var other in TurnManager.Instance.Units)
                {
                    if (other == null || other.IsDead || other == unit)
                        continue;
                    int d = GridManager.Instance.GetManhattanDistance(unit.Cell, other.Cell);
                    if (d > radius || d >= bestDist)
                        continue;
                    int itemIdx = -1;
                    for (int i = 0; i < other.Inventory.Count; i++)
                    {
                        if (ItemInfo.IsDoll(other.Inventory.Items[i].Kind))
                            continue;
                        itemIdx = i;
                        break;
                    }
                    if (itemIdx < 0)
                        continue;
                    best = other;
                    bestDist = d;
                    bestItem = itemIdx;
                }
                if (best == null || bestItem < 0)
                    return false;
                if (!SkillService.TryBeginMonkeySteal(unit))
                    return false;
                if (!SkillService.TryMonkeySelectTarget(unit, best))
                {
                    TurnManager.Instance.CancelTargeting();
                    return false;
                }
                return SkillService.TryMonkeyTakeItem(unit, bestItem);

            default:
                return false;
        }
    }

    private static bool TryHeal(UnitActor unit)
    {
        if (unit.Inventory.CountOf(ItemKind.LargePotion) > 0)
            return ActionService.TryUsePotion(unit, ItemKind.LargePotion);
        if (unit.Inventory.CountOf(ItemKind.SmallPotion) > 0)
            return ActionService.TryUseSmallPotion(unit);
        return false;
    }

    private static bool TryEquipGear(UnitActor unit)
    {
        var items = unit.Inventory.Items;
        bool hasWeapon = false;
        bool wearingArmor = false;
        bool wearingShield = false;
        for (int i = 0; i < items.Count; i++)
        {
            if (!items[i].Equipped)
                continue;
            if (ItemInfo.GetEquipSlot(items[i].Kind) == ItemInfo.EquipSlot.Weapon)
                hasWeapon = true;
            if (ItemInfo.IsArmor(items[i].Kind))
                wearingArmor = true;
            if (items[i].Kind == ItemKind.EnergyShield)
                wearingShield = true;
        }

        if (!hasWeapon && unit.Inventory.TotalAmmoCharges() > 0)
        {
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].Equipped) continue;
                if (items[i].Kind == ItemKind.Crossbow || items[i].Kind == ItemKind.Bow)
                {
                    if (ActionService.TryToggleEquip(unit, i))
                        return true;
                }
            }
        }

        int cb = unit.FindEquippedIndex(ItemKind.Crossbow);
        if (cb >= 0 && !unit.CrossbowCharged && unit.TryChargeCrossbow())
        {
            TurnManager.Instance.LogFor(unit, $"{RoleInfo.GetDisplayName(unit.Role)} 为弩蓄力");
            TurnManager.Instance.NotifyActionDone();
            return true;
        }

        int bestArmor = -1;
        int bestArmorScore = -1;
        int shieldIndex = -1;
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i].Equipped)
                continue;
            if (!wearingArmor && ItemInfo.IsArmor(items[i].Kind))
            {
                int score = items[i].Kind == ItemKind.IronArmor ? 2 : 1;
                if (score > bestArmorScore)
                {
                    bestArmorScore = score;
                    bestArmor = i;
                }
            }
            if (!wearingShield && !wearingArmor && items[i].Kind == ItemKind.EnergyShield)
                shieldIndex = i;
        }

        if (bestArmor >= 0 && ActionService.TryToggleEquip(unit, bestArmor))
            return true;
        if (shieldIndex >= 0 && ActionService.TryToggleEquip(unit, shieldIndex))
            return true;
        return false;
    }

    private static bool TryShootBest(UnitActor unit)
    {
        int bestWeapon = -1;
        ItemKind bestAmmo = ItemKind.Arrow;
        UnitActor bestTarget = null;
        float bestScore = float.MinValue;

        var items = unit.Inventory.Items;
        for (int wi = 0; wi < items.Count; wi++)
        {
            var weapon = items[wi].Kind;
            if (!ItemInfo.IsRangedWeapon(weapon) || !items[wi].Equipped)
                continue;
            if (weapon == ItemKind.Crossbow && !unit.CanFireCrossbow())
                continue;

            int need = ItemInfo.GetArrowCost(weapon);
            var ammos = unit.Inventory.GetAvailableAmmoTypes(need);
            if (ammos.Count == 0)
                continue;

            // 优先普通箭，其次毒/火
            ItemKind ammo = ammos[0];
            for (int a = 0; a < ammos.Count; a++)
            {
                if (ammos[a] == ItemKind.Arrow)
                {
                    ammo = ItemKind.Arrow;
                    break;
                }
            }

            int range = unit.GetShootRange(weapon);
            int power = unit.CurrentAtk + ItemInfo.GetWeaponAtkBonus(weapon);

            foreach (var other in GameManager.Instance.Units)
            {
                if (other == null || other == unit || other.IsDead)
                    continue;
                if (!StealthService.CanTargetDespiteHidden(unit, other))
                    continue;
                int dist = GridManager.Instance.GetManhattanDistance(unit.Cell, other.Cell);
                if (dist > range)
                    continue;

                int expected = Mathf.Max(0, power - other.CurrentDef);
                float score = expected * 10f - dist;
                if (other.IsDying)
                    score += 80f;
                else if (expected >= other.Hp)
                    score += 100f;
                else if (other.Hp <= unit.MaxHp * 0.35f)
                    score += 40f;

                if (score > bestScore)
                {
                    bestScore = score;
                    bestWeapon = wi;
                    bestAmmo = ammo;
                    bestTarget = other;
                }
            }
        }

        if (bestWeapon < 0 || bestTarget == null)
            return false;

        TurnManager.Instance.EnterShootTargeting(bestWeapon, bestAmmo);
        return ActionService.TryShoot(unit, bestWeapon, bestTarget);
    }

    private static bool TryMeleeBest(UnitActor unit)
    {
        if (TurnManager.Instance.HasMeleeAttacked)
            return false;

        UnitActor best = null;
        float bestScore = float.MinValue;
        foreach (var other in GameManager.Instance.Units)
        {
            if (other == null || other == unit || other.IsDead)
                continue;
            int dist = GridManager.Instance.GetManhattanDistance(unit.Cell, other.Cell);
            if (dist > unit.AttackRange)
                continue;

            int expected = Mathf.Max(0, unit.CurrentAtk - other.CurrentDef);
            float score = expected * 10f - dist;
            if (other.IsDying) score += 80f;
            else if (expected >= other.Hp) score += 100f;
            if (score > bestScore)
            {
                bestScore = score;
                best = other;
            }
        }

        if (best == null)
            return false;
        return ActionService.TryAttack(unit, best);
    }

    private static bool TryBombBest(UnitActor unit)
    {
        int bombIndex = -1;
        ItemKind bombKind = ItemKind.Bomb;
        for (int i = 0; i < unit.Inventory.Count; i++)
        {
            var k = unit.Inventory.Items[i].Kind;
            if (!ItemInfo.IsThrowableBomb(k))
                continue;
            bombIndex = i;
            bombKind = k;
            break;
        }
        if (bombIndex < 0)
            return false;

        int blast = ItemInfo.GetBombBlastRadius(bombKind);
        int damage = ItemInfo.GetBombDamage(bombKind);
        var grid = GridManager.Instance;

        Vector2Int bestCell = unit.Cell;
        float bestScore = 0f;
        bool found = false;

        for (int dx = -5; dx <= 5; dx++)
        {
            for (int dy = -5; dy <= 5; dy++)
            {
                if (Mathf.Abs(dx) + Mathf.Abs(dy) > 5)
                    continue;
                var cell = unit.Cell + new Vector2Int(dx, dy);
                if (!grid.IsValidCell(cell))
                    continue;

                float score = 0f;
                bool selfHit = grid.GetManhattanDistance(cell, unit.Cell) <= blast;
                if (selfHit)
                    score -= damage * 8f;

                foreach (var other in GameManager.Instance.Units)
                {
                    if (other == null || other == unit || other.IsDead)
                        continue;
                    if (grid.GetManhattanDistance(cell, other.Cell) > blast)
                        continue;
                    int expected = Mathf.Max(0, damage - other.CurrentDef);
                    score += expected * 12f;
                    if (expected >= other.Hp || other.IsDying)
                        score += 50f;
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    bestCell = cell;
                    found = true;
                }
            }
        }

        if (!found || bestScore < 8f)
            return false;

        TurnManager.Instance.EnterBombTargeting(bombIndex);
        return ActionService.TryThrowBomb(unit, bestCell);
    }

    private static bool TryPickup(UnitActor unit)
    {
        var loot = GroundItemManager.Instance?.GetLootInRange(unit.Cell, 1);
        if (loot == null || loot.Count == 0)
            return false;

        // 优先玩偶
        for (int i = 0; i < loot.Count; i++)
        {
            if (!ItemInfo.IsDoll(loot[i].Kind))
                continue;
            if (ActionService.TryPickupOne(unit, loot[i].Cell, loot[i].Index))
                return true;
        }

        return ActionService.TryPickup(unit);
    }

    private static bool TryMoveBest(UnitActor unit)
    {
        var grid = GridManager.Instance;
        int move = unit.CurrentMove;
        Vector2Int best = unit.Cell;
        float bestScore = ScoreCell(unit, unit.Cell) - 0.5f; // 略偏好移动
        bool found = false;

        for (int dx = -move; dx <= move; dx++)
        {
            for (int dy = -move; dy <= move; dy++)
            {
                int dist = Mathf.Abs(dx) + Mathf.Abs(dy);
                if (dist == 0 || dist > move)
                    continue;
                var cell = unit.Cell + new Vector2Int(dx, dy);
                if (!grid.IsValidCell(cell) || grid.IsCellOccupied(cell))
                    continue;
                if (!VisibilityService.CanMoveTo(unit, cell))
                    continue;
                if (grid.GetTileType(cell) == TileType.Lava)
                    continue;

                float score = ScoreCell(unit, cell);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = cell;
                    found = true;
                }
            }
        }

        if (!found)
            return false;
        return ActionService.TryMove(unit, best);
    }

    private static float ScoreCell(UnitActor unit, Vector2Int cell)
    {
        var grid = GridManager.Instance;
        float score = 0f;

        // 靠近敌人（可打到更好）
        UnitActor nearest = null;
        int nearestDist = int.MaxValue;
        foreach (var other in GameManager.Instance.Units)
        {
            if (other == null || other == unit || other.IsDead)
                continue;
            int d = grid.GetManhattanDistance(cell, other.Cell);
            if (d < nearestDist)
            {
                nearestDist = d;
                nearest = other;
            }

            if (d <= unit.AttackRange)
                score += 35f;
            else
                score += Mathf.Max(0f, 18f - d);
        }

        // 有武器时偏好进入射程
        int weaponRange = 0;
        foreach (var item in unit.Inventory.Items)
        {
            if (!ItemInfo.IsWeapon(item.Kind))
                continue;
            if (unit.Inventory.TotalAmmoCharges() < ItemInfo.GetArrowCost(item.Kind))
                continue;
            weaponRange = Mathf.Max(weaponRange, unit.GetShootRange(item.Kind));
        }
        if (weaponRange > 0 && nearest != null)
        {
            int d = grid.GetManhattanDistance(cell, nearest.Cell);
            if (d <= weaponRange)
                score += 40f;
            else
                score += Mathf.Max(0f, 25f - (d - weaponRange));
        }

        // 地面玩偶
        if (GroundItemManager.Instance != null)
        {
            for (int x = 0; x < grid.gridWidth; x++)
            {
                for (int y = 0; y < grid.gridHeight; y++)
                {
                    var c = new Vector2Int(x, y);
                    if (!GroundItemManager.Instance.HasItems(c))
                        continue;
                    var peek = GroundItemManager.Instance.Peek(c);
                    bool doll = false;
                    for (int i = 0; i < peek.Count; i++)
                    {
                        if (ItemInfo.IsDoll(peek[i].Kind))
                        {
                            doll = true;
                            break;
                        }
                    }
                    int d = grid.GetManhattanDistance(cell, c);
                    if (doll)
                        score += Mathf.Max(0f, 50f - d * 3f);
                    else if (d <= 2)
                        score += 8f;
                }
            }
        }

        // 远离熔岩环（外圈危险）
        int inset = grid.LavaInset;
        int edgeDist = Mathf.Min(
            cell.x - inset,
            cell.y - inset,
            grid.gridWidth - 1 - inset - cell.x,
            grid.gridHeight - 1 - inset - cell.y);
        if (edgeDist <= 1)
            score -= 40f;
        else if (edgeDist <= 2)
            score -= 15f;

        // 地形：避开沼泽，略偏好高地/丛林
        switch (grid.GetTileType(cell))
        {
            case TileType.Swamp:
                score -= 18f;
                break;
            case TileType.Sand:
                score -= 6f;
                break;
            case TileType.Highland:
                score += 12f;
                break;
            case TileType.Jungle:
                score += 8f;
                break;
            case TileType.Ice:
                score += 2f; // 移速好，但有跌倒风险
                break;
        }

        // 略向中心
        var center = new Vector2Int(grid.gridWidth / 2, grid.gridHeight / 2);
        score -= grid.GetManhattanDistance(cell, center) * 0.35f;

        return score;
    }
}
