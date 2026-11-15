using System.Collections.Generic;

public class GroundItemManager
{
    public static GroundItemManager Instance;

    /// <summary>
    /// 地面堆叠条目：开局散落 FaceDown=true（仅知有牌）；弃置/濒死掉落 FaceDown=false（正面可见）。
    /// </summary>
    public struct GroundStackEntry
    {
        public InventoryItem Item;
        public bool FaceDown;
    }

    private readonly Dictionary<Int2, List<GroundStackEntry>> piles =
        new Dictionary<Int2, List<GroundStackEntry>>();
    private struct LootVisual
    {
        public int Handle;
        public LootStyle Style;
    }

    private readonly Dictionary<Int2, LootVisual> visuals = new Dictionary<Int2, LootVisual>();


    /// <summary>
    /// 在格子上掉落一组物品（正面可见）。
    /// </summary>
    public void DropItems(Int2 cell, List<InventoryItem> items)
    {
        if (items == null || items.Count == 0)
            return;
        EnsurePile(cell);
        foreach (var item in items)
            piles[cell].Add(new GroundStackEntry { Item = item, FaceDown = false });
        RefreshVisual(cell);
    }

    /// <summary>
    /// 按种类掉落单件物品。
    /// </summary>
    public void DropItem(Int2 cell, ItemKind kind)
    {
        DropItem(cell, InventoryItem.CreateFresh(kind));
    }

    /// <summary>
    /// 掉落指定实例的物品。
    /// </summary>
    public void DropItem(Int2 cell, InventoryItem item)
    {
        DropItems(cell, new List<InventoryItem> { item });
    }

    /// <summary>
    /// 开局散落：背面朝上，身份不可见。
    /// </summary>
    public void ScatterFaceDown(Int2 cell, InventoryItem item)
    {
        EnsurePile(cell);
        piles[cell].Add(new GroundStackEntry { Item = item, FaceDown = true });
        RefreshVisual(cell);
    }

    private void EnsurePile(Int2 cell)
    {
        if (!piles.ContainsKey(cell))
            piles[cell] = new List<GroundStackEntry>();
    }

    /// <summary>
    /// 该格是否有物品。
    /// </summary>
    public bool HasItems(Int2 cell)
    {
        return piles.TryGetValue(cell, out var list) && list.Count > 0;
    }

    /// <summary>
    /// 曼哈顿半径内是否有掉落物（无分配；供动作掩码/寻路预判用）。
    /// </summary>
    public bool HasItemsAround(Int2 center, int radius)
    {
        for (int dx = -radius; dx <= radius; dx++)
        {
            for (int dy = -radius; dy <= radius; dy++)
            {
                if (System.Math.Abs(dx) + System.Math.Abs(dy) > radius)
                    continue;
                if (HasItems(center + new Int2(dx, dy)))
                    return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 全图随机收集至多 count 张牌进背包（领袖宣言等效果用）；不消耗行动、不限距离、允许超重。返回实际收集张数。
    /// </summary>
    public int CollectRandom(Inventory inventory, int count)
    {
        if (inventory == null || count <= 0)
            return 0;

        var cells = new List<Int2>(piles.Keys);
        for (int i = cells.Count - 1; i > 0; i--)
        {
            int j = Rng.Range(0, i + 1);
            (cells[i], cells[j]) = (cells[j], cells[i]);
        }

        int picked = 0;
        foreach (var cell in cells)
        {
            if (picked >= count)
                break;
            int remaining = count - picked;
            while (remaining-- > 0
                && piles.TryGetValue(cell, out var list)
                && list.Count > 0)
            {
                int idx = Rng.Range(0, list.Count);
                if (!TryPickupOne(cell, idx, inventory))
                    break;
                picked++;
            }
        }
        return picked;
    }

    /// <summary>
    /// 查看该格全部物品。
    /// </summary>
    public List<InventoryItem> Peek(Int2 cell)
    {
        var result = new List<InventoryItem>();
        if (!piles.TryGetValue(cell, out var list))
            return result;
        foreach (var e in list)
            result.Add(e.Item);
        return result;
    }

    /// <summary>
    /// 查看该格的堆叠条目（含正反面标记）。
    /// </summary>
    public List<GroundStackEntry> PeekEntries(Int2 cell)
    {
        if (piles.TryGetValue(cell, out var list))
            return new List<GroundStackEntry>(list);
        return new List<GroundStackEntry>();
    }

    /// <summary>
    /// 该格第 index 张是否背面朝下。
    /// </summary>
    public bool IsFaceDown(Int2 cell, int index)
    {
        if (!piles.TryGetValue(cell, out var list) || index < 0 || index >= list.Count)
            return false;
        return list[index].FaceDown;
    }

    public struct GroundLootEntry
    {
        public Int2 Cell;
        public int Index;
        public ItemKind Kind;
        public int Charges;
        public bool FaceDown;
    }

    /// <summary>
    /// 取范围内可拾取的物品列表。
    /// </summary>
    public List<GroundLootEntry> GetLootInRange(Int2 center, int maxDist)
    {
        var result = new List<GroundLootEntry>();
        for (int dx = -maxDist; dx <= maxDist; dx++)
        {
            for (int dy = -maxDist; dy <= maxDist; dy++)
            {
                if (System.Math.Abs(dx) + System.Math.Abs(dy) > maxDist)
                    continue;
                var cell = center + new Int2(dx, dy);
                if (!piles.TryGetValue(cell, out var list) || list.Count == 0)
                    continue;
                for (int i = 0; i < list.Count; i++)
                {
                    result.Add(new GroundLootEntry
                    {
                        Cell = cell,
                        Index = i,
                        Kind = list[i].Item.Kind,
                        Charges = list[i].Item.Charges,
                        FaceDown = list[i].FaceDown
                    });
                }
            }
        }
        return result;
    }

    /// <summary>
    /// 偷看该格第 index 张牌。
    /// </summary>
    public bool TryPeek(Int2 cell, int index, out InventoryItem item)
    {
        item = default;
        if (!piles.TryGetValue(cell, out var list) || index < 0 || index >= list.Count)
            return false;
        item = list[index].Item;
        return true;
    }

    /// <summary>
    /// 取走该格第 index 张牌。
    /// </summary>
    public bool TryTakeAt(Int2 cell, int index, out InventoryItem item)
    {
        item = default;
        if (!piles.TryGetValue(cell, out var list) || index < 0 || index >= list.Count)
            return false;
        item = list[index].Item;
        list.RemoveAt(index);
        if (list.Count == 0)
            piles.Remove(cell);
        RefreshVisual(cell);
        return true;
    }

    /// <summary>
    /// 拾取指定格上指定下标的一件物品（允许暂时超重）。
    /// </summary>
    public bool TryPickupOne(Int2 cell, int index, Inventory inventory)
    {
        if (inventory == null || !piles.TryGetValue(cell, out var list))
            return false;
        if (index < 0 || index >= list.Count)
            return false;
        var item = list[index].Item;

        list.RemoveAt(index);
        inventory.Add(item);
        if (list.Count == 0)
            piles.Remove(cell);
        RefreshVisual(cell);
        return true;
    }

    /// <summary>
    /// 脚下是否有可直接使用的血瓶（濒死自救）。仅计正面朝上的牌，不暴露开局散落身份。
    /// </summary>
    public bool HasPotionAt(Int2 cell)
    {
        if (!piles.TryGetValue(cell, out var list))
            return false;
        foreach (var e in list)
        {
            if (!e.FaceDown && ItemInfo.IsPotion(e.Item.Kind))
                return true;
        }
        return false;
    }

    /// <summary>
    /// 取走该格第一瓶正面血瓶（不进入背包）。
    /// </summary>
    public bool TryTakeFirstPotion(Int2 cell, out InventoryItem item)
    {
        item = default;
        if (!piles.TryGetValue(cell, out var list))
            return false;
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].FaceDown || !ItemInfo.IsPotion(list[i].Item.Kind))
                continue;
            item = list[i].Item;
            list.RemoveAt(i);
            if (list.Count == 0)
                piles.Remove(cell);
            RefreshVisual(cell);
            return true;
        }
        return false;
    }

    /// <summary>
    /// 拾取该格全部物品。
    /// </summary>
    public int TryPickupAll(Int2 cell, Inventory inventory)
    {
        if (!piles.TryGetValue(cell, out var list) || list.Count == 0)
            return 0;

        int picked = 0;
        for (int i = list.Count - 1; i >= 0; i--)
        {
            if (inventory.Add(list[i].Item))
            {
                list.RemoveAt(i);
                picked++;
            }
        }

        if (list.Count == 0)
            piles.Remove(cell);
        RefreshVisual(cell);
        return picked;
    }

    /// <summary>
    /// 拾取周围物品（含所在格）。
    /// </summary>
    public int TryPickupAround(Int2 center, Inventory inventory)
    {
        int total = 0;
        int radius = GameRulesConfig.PickupRange;
        for (int dx = -radius; dx <= radius; dx++)
        {
            for (int dy = -radius; dy <= radius; dy++)
            {
                if (System.Math.Abs(dx) + System.Math.Abs(dy) > radius)
                    continue;
                total += TryPickupAll(center + new Int2(dx, dy), inventory);
            }
        }
        return total;
    }

    /// <summary>
    /// 熔岩吞噬格子上的掉落物，送入弃牌堆。返回件数。
    /// </summary>
    public int SwallowCellsToDiscard(List<Int2> cells)
    {
        int total = 0;
        if (cells == null)
            return 0;

        foreach (var cell in cells)
        {
            if (!piles.TryGetValue(cell, out var list) || list.Count == 0)
                continue;

            total += list.Count;
            var kinds = new List<ItemKind>();
            foreach (var e in list)
            {
                var item = e.Item;
                if (ItemInfo.IsStackableAmmo(item.Kind))
                {
                    int cards = ItemInfo.AmmoChargesToCards(item.Charges);
                    for (int c = 0; c < cards; c++)
                        kinds.Add(item.Kind);
                }
                else
                    kinds.Add(item.Kind);
            }
            DeckManager.Instance?.AddRangeToDiscard(kinds);
            piles.Remove(cell);
            RefreshVisual(cell);
        }

        return total;
    }

    /// <summary>
    /// 清空全部地面物品。
    /// </summary>
    public void ClearAll()
    {
        foreach (var kv in visuals)
            Presentation.Current.DestroyVisual(kv.Value.Handle);
        visuals.Clear();
        piles.Clear();
    }


    private void RefreshVisual(Int2 cell)
    {
        bool has = piles.TryGetValue(cell, out var list) && list.Count > 0;
        if (!has)
        {
            if (visuals.TryGetValue(cell, out var old))
                Presentation.Current.DestroyVisual(old.Handle);
            visuals.Remove(cell);
            return;
        }

        bool anyFaceUp = false;
        bool faceUpDoll = false;
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].FaceDown)
                continue;
            anyFaceUp = true;
            if (ItemInfo.IsDoll(list[i].Item.Kind))
                faceUpDoll = true;
        }

        LootStyle style = !anyFaceUp ? LootStyle.Scatter : (faceUpDoll ? LootStyle.Doll : LootStyle.Drop);
        if (!visuals.TryGetValue(cell, out var visual) || visual.Handle == 0 || visual.Style != style)
        {
            if (visual.Handle != 0)
                Presentation.Current.DestroyVisual(visual.Handle);
            visual = new LootVisual { Handle = Presentation.Current.CreateLootVisual(cell, style), Style = style };
            visuals[cell] = visual;
        }

        var viewer = VisibilityService.GetFogViewer();
        Presentation.Current.SetVisualActive(visual.Handle, viewer == null || VisibilityService.CanSeeCell(viewer, cell));
    }

    /// <summary>
    /// 按视野角色刷新地面物品显隐。
    /// </summary>
    public void RefreshAllVisibility(UnitActor viewer)
    {
        foreach (var kv in visuals)
        {
            bool show = viewer == null || VisibilityService.CanSeeCell(viewer, kv.Key);
            Presentation.Current.SetVisualActive(kv.Value.Handle, show);
        }
    }

    /// <summary>
    /// 悬停用：该格掉落物摘要，无则 null。背面牌不暴露种类。
    /// </summary>
    public string DescribeLoot(Int2 cell)
    {
        if (!piles.TryGetValue(cell, out var list) || list.Count == 0)
            return null;

        var parts = new List<string>();
        foreach (var e in list)
        {
            if (e.FaceDown)
            {
                parts.Add("未知卡牌");
                continue;
            }
            if (ItemInfo.IsStackableAmmo(e.Item.Kind))
                parts.Add($"{ItemInfo.GetDisplayName(e.Item.Kind)}×{e.Item.Charges}");
            else
                parts.Add(ItemInfo.GetDisplayName(e.Item.Kind));
        }
        return "掉落：" + string.Join("、", parts);
    }
}
