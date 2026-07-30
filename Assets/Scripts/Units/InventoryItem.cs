/// <summary>背包/地面掉落物条目。
/// Charges：甲/护盾=剩余次数；弹药=支数；其它为 0。
/// Equipped：甲/护盾穿戴中才提供减伤/免伤。</summary>
public struct InventoryItem
{
    public ItemKind Kind;
    public int Charges;
    public bool Equipped;

    public InventoryItem(ItemKind kind, int charges = 0, bool equipped = false)
    {
        Kind = kind;
        Charges = charges;
        Equipped = equipped;
    }

    public static InventoryItem CreateFresh(ItemKind kind)
    {
        if (ItemInfo.IsStackableAmmo(kind))
            return new InventoryItem(kind, ItemInfo.GetAmmoPerCard(kind));
        return new InventoryItem(kind, ItemInfo.GetMaxCharges(kind));
    }
}
