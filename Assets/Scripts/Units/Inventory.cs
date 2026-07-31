using System.Collections.Generic;
using UnityEngine;

public class Inventory
{
    public float Capacity { get; private set; }
    public List<InventoryItem> Items { get; private set; } = new List<InventoryItem>();

    /// <summary>已消耗弹药的债务字段已废弃（一张一支，消耗即弃牌）。</summary>
    private readonly Dictionary<ItemKind, int> ammoSpendDebt = new Dictionary<ItemKind, int>();

    private void ClearAmmoDebt(ItemKind ammo) => ammoSpendDebt.Remove(ammo);

    public Inventory(float capacity)
    {
        Capacity = capacity;
    }

    public void SetCapacity(float capacity)
    {
        Capacity = capacity;
    }

    public int Count => Items.Count;

    public float UsedWeight
    {
        get
        {
            float w = 0f;
            foreach (var item in Items)
                w += ItemInfo.GetWeight(item);
            return w;
        }
    }

    public bool HasSpace => UsedWeight < Capacity - 0.001f;

    /// <summary>当前负重是否超过容量（行动中允许超重，结束行动前必须清掉）。</summary>
    public bool IsOverCapacity => UsedWeight > Capacity + 0.001f;

    public bool CanAdd(ItemKind kind) => CanAdd(InventoryItem.CreateFresh(kind));

    /// <summary>是否能在不超重的前提下放入（仅作提示；行动中实际添加不受此限制）。</summary>
    public bool CanAdd(InventoryItem item)
    {
        float addWeight = ItemInfo.GetWeight(item);
        return UsedWeight + addWeight <= Capacity + 0.001f;
    }

    public bool Add(ItemKind kind) => Add(InventoryItem.CreateFresh(kind));

    /// <summary>加入背包；行动中允许超过容量。</summary>
    public bool Add(InventoryItem item)
    {
        if (ItemInfo.GetMaxCharges(item.Kind) > 0 && item.Charges <= 0)
            item.Charges = ItemInfo.GetMaxCharges(item.Kind);

        if (ItemInfo.IsStackableAmmo(item.Kind))
        {
            int add = item.Charges > 0 ? item.Charges : ItemInfo.GetAmmoPerCard(item.Kind);
            for (int i = 0; i < Items.Count; i++)
            {
                if (Items[i].Kind != item.Kind)
                    continue;
                var stack = Items[i];
                stack.Charges += add;
                Items[i] = stack;
                return true;
            }
            Items.Add(new InventoryItem(item.Kind, add));
            return true;
        }

        item.Equipped = false;
        Items.Add(item);
        return true;
    }

    public bool Remove(ItemKind kind)
    {
        for (int i = 0; i < Items.Count; i++)
        {
            if (Items[i].Kind != kind)
                continue;
            if (ItemInfo.IsStackableAmmo(kind))
                ClearAmmoDebt(kind);
            Items.RemoveAt(i);
            return true;
        }
        return false;
    }

    public bool RemoveAt(int index)
    {
        if (index < 0 || index >= Items.Count)
            return false;
        if (ItemInfo.IsStackableAmmo(Items[index].Kind))
            ClearAmmoDebt(Items[index].Kind);
        Items.RemoveAt(index);
        return true;
    }

    public bool TryGet(int index, out InventoryItem item)
    {
        if (index < 0 || index >= Items.Count)
        {
            item = default;
            return false;
        }
        item = Items[index];
        return true;
    }

    public void SetAt(int index, InventoryItem item)
    {
        if (index < 0 || index >= Items.Count)
            return;
        Items[index] = item;
    }

    /// <summary>弹药返回支数；其它返回件数。</summary>
    public int CountOf(ItemKind kind)
    {
        if (ItemInfo.IsStackableAmmo(kind))
        {
            foreach (var item in Items)
            {
                if (item.Kind == kind)
                    return item.Charges;
            }
            return 0;
        }

        int n = 0;
        foreach (var item in Items)
            if (item.Kind == kind) n++;
        return n;
    }

    public bool Contains(ItemKind kind)
    {
        if (ItemInfo.IsStackableAmmo(kind))
            return CountOf(kind) > 0;
        foreach (var item in Items)
            if (item.Kind == kind) return true;
        return false;
    }

    public int TotalAmmoCharges()
    {
        return CountOf(ItemKind.Arrow) + CountOf(ItemKind.PoisonArrow) + CountOf(ItemKind.FireRocket);
    }

    public List<ItemKind> GetAvailableAmmoTypes(int need)
    {
        var list = new List<ItemKind>();
        if (CountOf(ItemKind.Arrow) >= need) list.Add(ItemKind.Arrow);
        if (CountOf(ItemKind.PoisonArrow) >= need) list.Add(ItemKind.PoisonArrow);
        if (CountOf(ItemKind.FireRocket) >= need) list.Add(ItemKind.FireRocket);
        return list;
    }

    public int CountDolls()
    {
        int n = 0;
        foreach (var item in Items)
            if (ItemInfo.IsDoll(item.Kind)) n++;
        return n;
    }

    public bool HasAllDolls()
    {
        return Contains(ItemKind.DollElephant)
            && Contains(ItemKind.DollHuman)
            && Contains(ItemKind.DollMonkey)
            && Contains(ItemKind.DollCat);
    }

    public int GetArmorDefenseBonus()
    {
        int sum = 0;
        foreach (var item in Items)
        {
            if (!item.Equipped)
                continue;
            sum += ItemInfo.GetArmorDefense(item.Kind);
        }
        return sum;
    }

    public bool IsEquipable(ItemKind kind) => ItemInfo.IsEquipable(kind);

    /// <summary>装备；同槽位已有装备则先卸下。</summary>
    public bool TryEquip(int index, out string log)
    {
        log = null;
        if (index < 0 || index >= Items.Count)
            return false;
        var item = Items[index];
        var slot = ItemInfo.GetEquipSlot(item.Kind);
        if (slot == ItemInfo.EquipSlot.None)
            return false;
        if (item.Equipped)
        {
            log = "已在装备中";
            return false;
        }

        for (int i = 0; i < Items.Count; i++)
        {
            if (i == index || !Items[i].Equipped)
                continue;
            if (ItemInfo.GetEquipSlot(Items[i].Kind) != slot)
                continue;
            var prev = Items[i];
            prev.Equipped = false;
            Items[i] = prev;
            if (prev.Kind == ItemKind.Crossbow)
                TurnManager.Instance?.CurrentUnit?.ConsumeCrossbowCharge();
            log = $"卸下【{ItemInfo.GetDisplayName(prev.Kind)}】并装备【{ItemInfo.GetDisplayName(item.Kind)}】";
        }

        item.Equipped = true;
        Items[index] = item;
        if (log == null)
            log = $"装备【{ItemInfo.GetDisplayName(item.Kind)}】";
        return true;
    }

    public bool TryUnequip(int index, out string log)
    {
        log = null;
        if (index < 0 || index >= Items.Count)
            return false;
        var item = Items[index];
        if (!item.Equipped)
        {
            log = "未在装备";
            return false;
        }
        item.Equipped = false;
        Items[index] = item;
        log = $"卸下【{ItemInfo.GetDisplayName(item.Kind)}】";
        return true;
    }

    /// <summary>消耗指定弹药；每消耗 1 支弃 1 张对应弹药牌。</summary>
    public bool TryConsumeAmmo(ItemKind ammo, int count, List<ItemKind> discardedCards)
    {
        if (!ItemInfo.IsStackableAmmo(ammo) || count <= 0)
            return false;

        for (int i = 0; i < Items.Count; i++)
        {
            if (Items[i].Kind != ammo)
                continue;
            if (Items[i].Charges < count)
                return false;

            var stack = Items[i];
            stack.Charges -= count;
            for (int c = 0; c < count; c++)
                discardedCards?.Add(ammo);

            if (stack.Charges <= 0)
                Items.RemoveAt(i);
            else
                Items[i] = stack;
            ClearAmmoDebt(ammo);
            return true;
        }
        return false;
    }

    public bool TryConsumeArrows(int count, List<ItemKind> discardedCards)
        => TryConsumeAmmo(ItemKind.Arrow, count, discardedCards);

    public bool TryConsumeShieldCharge(out bool destroyed)
    {
        destroyed = false;
        for (int i = 0; i < Items.Count; i++)
        {
            if (Items[i].Kind != ItemKind.EnergyShield || !Items[i].Equipped || Items[i].Charges <= 0)
                continue;

            var entry = Items[i];
            entry.Charges--;
            if (entry.Charges <= 0)
            {
                destroyed = true;
                Items.RemoveAt(i);
            }
            else
                Items[i] = entry;
            return true;
        }
        return false;
    }

    public bool TryWearArmor(out bool broken, out ItemKind brokenKind)
    {
        broken = false;
        brokenKind = default;
        for (int i = 0; i < Items.Count; i++)
        {
            if (!ItemInfo.IsArmor(Items[i].Kind) || !Items[i].Equipped || Items[i].Charges <= 0)
                continue;

            var entry = Items[i];
            entry.Charges--;
            if (entry.Charges <= 0)
            {
                broken = true;
                brokenKind = entry.Kind;
                Items.RemoveAt(i);
            }
            else
                Items[i] = entry;
            return true;
        }
        return false;
    }

    public List<InventoryItem> TakeAll()
    {
        ammoSpendDebt.Clear();
        var copy = new List<InventoryItem>(Items.Count);
        foreach (var item in Items)
        {
            var dropped = item;
            dropped.Equipped = false; // 掉落物不再处于穿戴
            copy.Add(dropped);
        }
        Items.Clear();
        return copy;
    }

    /// <summary>旧接口：非弓箭按件移除；弓箭请用 TryConsumeArrows。</summary>
    public int RemoveUpTo(ItemKind kind, int count, List<ItemKind> removed)
    {
        if (kind == ItemKind.Arrow)
            return TryConsumeArrows(count, removed) ? count : 0;

        int got = 0;
        for (int i = Items.Count - 1; i >= 0 && got < count; i--)
        {
            if (Items[i].Kind != kind)
                continue;
            removed?.Add(Items[i].Kind);
            Items.RemoveAt(i);
            got++;
        }
        return got;
    }
}
