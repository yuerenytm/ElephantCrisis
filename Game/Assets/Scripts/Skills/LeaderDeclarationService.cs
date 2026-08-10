using UnityEngine;

/// <summary>特殊技能「领袖宣言」：每名玩家限一次。</summary>
public static class LeaderDeclarationService
{
    public static bool CanDeclare(UnitActor unit, out string reason, out UnitActor missingHolder, out ItemKind missingDoll)
    {
        reason = null;
        missingHolder = null;
        missingDoll = default;

        if (unit == null || unit.IsDead)
        {
            reason = "无法发动领袖宣言";
            return false;
        }
        if (unit.IsDying)
        {
            reason = "濒死时不可发动领袖宣言";
            return false;
        }
        if (unit.HasUsedLeaderDeclaration)
        {
            reason = "本局已发动过领袖宣言";
            return false;
        }
        if (TurnManager.Instance == null || TurnManager.Instance.CurrentUnit != unit)
        {
            reason = "非自己的行动";
            return false;
        }
        if (TurnManager.Instance.Phase != TurnPhase.WaitingAction)
        {
            reason = "当前无法发动领袖宣言";
            return false;
        }

        if (!TryFindMissingDollOnOtherPlayer(unit, out missingDoll, out missingHolder, out reason))
            return false;

        return true;
    }

    public static bool TryDeclare(UnitActor unit)
    {
        if (!CanDeclare(unit, out string reason, out var holder, out var missing))
        {
            TurnManager.Instance?.LogFor(unit, reason);
            return false;
        }

        unit.HasUsedLeaderDeclaration = true;
        unit.ApplyStatus(StatusType.Leader, 10, unit);

        int got = GroundItemManager.Instance != null
            ? GroundItemManager.Instance.CollectRandom(unit.Inventory, 5)
            : 0;

        string loc = holder != null
            ? $"{RoleInfo.GetDisplayName(holder.Role)} 位于 ({holder.Cell.x},{holder.Cell.y})"
            : "未知";
        TurnManager.Instance.LogFor(unit,
            $"{RoleInfo.GetDisplayName(unit.Role)} 发动【领袖宣言】：获得领袖状态10回合、从场上随机取{got}张；" +
            $"剩余【{ItemInfo.GetDisplayName(missing)}】在 {loc}");
        TurnManager.Instance.NotifyActionDone();
        GameManager.Instance?.CheckWinConditions();
        return true;
    }

    /// <summary>持有任意三只玩偶，且缺的那一只在其他存活玩家身上（地上不触发）。</summary>
    public static bool TryFindMissingDollOnOtherPlayer(
        UnitActor unit, out ItemKind missing, out UnitActor holder, out string reason)
    {
        missing = default;
        holder = null;
        reason = null;

        var all = new[]
        {
            ItemKind.DollElephant,
            ItemKind.DollHuman,
            ItemKind.DollMonkey,
            ItemKind.DollCat
        };

        int held = 0;
        ItemKind? lack = null;
        for (int i = 0; i < all.Length; i++)
        {
            if (unit.Inventory != null && unit.Inventory.Contains(all[i]))
                held++;
            else
                lack = all[i];
        }

        if (held != 3 || !lack.HasValue)
        {
            reason = "需持有任意三只玩偶，且缺的一只在其他玩家身上";
            return false;
        }

        missing = lack.Value;
        if (TurnManager.Instance?.Units == null)
        {
            reason = "需持有任意三只玩偶，且缺的一只在其他玩家身上";
            return false;
        }

        foreach (var other in TurnManager.Instance.Units)
        {
            if (other == null || other.IsDead || other == unit)
                continue;
            if (other.Inventory != null && other.Inventory.Contains(missing))
            {
                holder = other;
                return true;
            }
        }

        reason = "缺的玩偶不在其他玩家身上（在地上或未入场则不可发动）";
        return false;
    }
}
