using System;
using System.Collections.Generic;

/// <summary>管理员模式：从共用牌库任选取出（可洗入弃牌）；本行动次数不限。</summary>
public static class AdminGrantService
{
    public static bool CanOpenPanel(UnitActor unit, out string reason)
    {
        reason = null;
        if (!MatchConfig.IsAdminMode)
        {
            reason = "仅管理员模式可用";
            return false;
        }
        if (unit == null || unit.IsDead || unit.IsDying)
        {
            reason = "无法领取";
            return false;
        }
        if (!MatchConfig.IsHumanControlled(unit))
        {
            reason = "仅玩家行动可领取";
            return false;
        }
        if (TurnManager.Instance == null || TurnManager.Instance.CurrentUnit != unit)
        {
            reason = "非自己的行动";
            return false;
        }
        if (TurnManager.Instance.Phase != TurnPhase.WaitingAction)
        {
            reason = "当前无法领取";
            return false;
        }
        return true;
    }

    public static List<(ItemKind Kind, int Count)> ListAvailableFromDeck()
    {
        var result = new List<(ItemKind, int)>();
        var deck = DeckManager.Instance;
        if (deck == null)
            return result;

        foreach (ItemKind kind in Enum.GetValues(typeof(ItemKind)))
        {
            int n = deck.CountAvailable(kind);
            if (n > 0)
                result.Add((kind, n));
        }
        return result;
    }

    public static bool TryGrant(UnitActor unit, ItemKind kind)
    {
        if (!CanOpenPanel(unit, out string reason))
        {
            TurnManager.Instance?.LogFor(unit, reason);
            return false;
        }

        var deck = DeckManager.Instance;
        if (deck == null)
        {
            TurnManager.Instance?.LogFor(unit, "牌库不可用");
            return false;
        }

        if (!deck.TryTakeSpecific(kind))
        {
            TurnManager.Instance?.LogFor(unit,
                $"牌库与弃牌堆中已无【{ItemInfo.GetDisplayName(kind)}】");
            return false;
        }

        unit.Inventory.Add(kind);
        unit.RefreshBagCapacity();
        string name = ItemInfo.GetDisplayName(kind);
        if (ItemInfo.IsDoll(kind))
            name = $"★{name}★";
        TurnManager.Instance.LogFor(unit,
            $"[管理员] {RoleInfo.GetDisplayName(unit.Role)} 从牌库取出【{name}】" +
            $"（库{deck.DrawCount}/弃{deck.DiscardCount}）");
        if (unit.Inventory.IsOverCapacity)
        {
            TurnManager.Instance.LogFor(unit,
                $"背包已超重（{unit.Inventory.UsedWeight:0.##}/{unit.Inventory.Capacity:0.##}），超重时无法结束行动");
        }
        TurnManager.Instance.NotifyActionDone();
        return true;
    }
}
