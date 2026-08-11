using System;
using System.Collections.Generic;

/// <summary>管理员模式：虚空印牌（凭空生成卡牌入背包，不消耗牌库/弃牌）；本行动次数不限。</summary>
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
            reason = "无法印牌";
            return false;
        }
        if (!MatchConfig.IsHumanControlled(unit))
        {
            reason = "仅玩家行动可印牌";
            return false;
        }
        if (TurnManager.Instance == null || TurnManager.Instance.CurrentUnit != unit)
        {
            reason = "非自己的行动";
            return false;
        }
        if (TurnManager.Instance.Phase != TurnPhase.WaitingAction)
        {
            reason = "当前无法印牌";
            return false;
        }
        return true;
    }

    /// <summary>可印的全部卡种（不依赖牌库存量）。</summary>
    public static List<ItemKind> ListPrintableKinds()
    {
        var result = new List<ItemKind>();
        foreach (ItemKind kind in Enum.GetValues(typeof(ItemKind)))
            result.Add(kind);
        return result;
    }

    public static bool TryGrant(UnitActor unit, ItemKind kind)
    {
        if (!CanOpenPanel(unit, out string reason))
        {
            TurnManager.Instance?.LogFor(unit, reason);
            return false;
        }

        unit.Inventory.Add(kind);
        unit.RefreshBagCapacity();
        string name = ItemInfo.GetDisplayName(kind);
        if (ItemInfo.IsDoll(kind))
            name = $"★{name}★";
        TurnManager.Instance.LogFor(unit,
            $"[管理员] {RoleInfo.GetDisplayName(unit.Role)} 虚空印出【{name}】");
        if (unit.Inventory.IsOverCapacity)
        {
            TurnManager.Instance.LogFor(unit,
                $"背包已超重（{unit.Inventory.UsedWeight:0.##}/{unit.Inventory.Capacity:0.##}），超重时无法结束行动");
        }
        TurnManager.Instance.NotifyActionDone();
        return true;
    }
}
