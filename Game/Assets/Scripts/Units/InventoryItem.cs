/// <summary>背包/地面掉落物条目。
/// Charges：甲/护盾=剩余次数；弹药=支数；其它为 0。
/// Equipped：甲/护盾装备中才提供减伤/免伤。
/// InstanceId：用于猴技能标记（合并弹药堆时保留原堆 Id）。</summary>
public struct InventoryItem
{
    public ItemKind Kind;
    public int Charges;
    public bool Equipped;
    public int InstanceId;

    private static int nextInstanceId = 1;

    public InventoryItem(ItemKind kind, int charges = 0, bool equipped = false, int instanceId = 0)
    {
        Kind = kind;
        Charges = charges;
        Equipped = equipped;
        InstanceId = instanceId > 0 ? instanceId : AllocId();
    }

    public static int AllocId() => nextInstanceId++;

    public static void ResetIdCounter() => nextInstanceId = 1;

    public static InventoryItem CreateFresh(ItemKind kind)
    {
        if (ItemInfo.IsStackableAmmo(kind))
            return new InventoryItem(kind, ItemInfo.GetAmmoPerCard(kind));
        return new InventoryItem(kind, ItemInfo.GetMaxCharges(kind));
    }
}
