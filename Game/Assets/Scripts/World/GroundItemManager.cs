using System.Collections.Generic;
using UnityEngine;

public class GroundItemManager : MonoBehaviour
{
    public static GroundItemManager Instance;

    private readonly Dictionary<Vector2Int, List<InventoryItem>> piles = new Dictionary<Vector2Int, List<InventoryItem>>();
    private readonly Dictionary<Vector2Int, GameObject> visuals = new Dictionary<Vector2Int, GameObject>();

    private void Awake()
    {
        Instance = this;
    }

    public void DropItems(Vector2Int cell, List<InventoryItem> items)
    {
        if (items == null || items.Count == 0)
            return;

        if (!piles.TryGetValue(cell, out var list))
        {
            list = new List<InventoryItem>();
            piles[cell] = list;
        }

        list.AddRange(items);
        RefreshVisual(cell);
    }

    public void DropItem(Vector2Int cell, ItemKind kind)
    {
        DropItem(cell, InventoryItem.CreateFresh(kind));
    }

    public void DropItem(Vector2Int cell, InventoryItem item)
    {
        DropItems(cell, new List<InventoryItem> { item });
    }

    public bool HasItems(Vector2Int cell)
    {
        return piles.TryGetValue(cell, out var list) && list.Count > 0;
    }

    /// <summary>曼哈顿半径内是否有掉落物（无分配；供动作掩码/寻路预判用）。</summary>
    public bool HasItemsAround(Vector2Int center, int radius)
    {
        for (int dx = -radius; dx <= radius; dx++)
        {
            for (int dy = -radius; dy <= radius; dy++)
            {
                if (Mathf.Abs(dx) + Mathf.Abs(dy) > radius)
                    continue;
                if (HasItems(center + new Vector2Int(dx, dy)))
                    return true;
            }
        }
        return false;
    }

    /// <summary>全图随机收集至多 count 张牌进背包（领袖宣言等效果用）；不消耗行动、不限距离、允许超重。返回实际收集张数。</summary>
    public int CollectRandom(Inventory inventory, int count)
    {
        if (inventory == null || count <= 0)
            return 0;

        var cells = new List<Vector2Int>(piles.Keys);
        for (int i = cells.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
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
                int idx = Random.Range(0, list.Count);
                if (!TryPickupOne(cell, idx, inventory))
                    break;
                picked++;
            }
        }
        return picked;
    }

    public List<InventoryItem> Peek(Vector2Int cell)
    {
        if (piles.TryGetValue(cell, out var list))
            return new List<InventoryItem>(list);
        return new List<InventoryItem>();
    }

    public struct GroundLootEntry
    {
        public Vector2Int Cell;
        public int Index;
        public ItemKind Kind;
        public int Charges;
    }

    public List<GroundLootEntry> GetLootInRange(Vector2Int center, int maxDist)
    {
        var result = new List<GroundLootEntry>();
        for (int dx = -maxDist; dx <= maxDist; dx++)
        {
            for (int dy = -maxDist; dy <= maxDist; dy++)
            {
                if (Mathf.Abs(dx) + Mathf.Abs(dy) > maxDist)
                    continue;
                var cell = center + new Vector2Int(dx, dy);
                if (!piles.TryGetValue(cell, out var list) || list.Count == 0)
                    continue;
                for (int i = 0; i < list.Count; i++)
                {
                    result.Add(new GroundLootEntry
                    {
                        Cell = cell,
                        Index = i,
                        Kind = list[i].Kind,
                        Charges = list[i].Charges
                    });
                }
            }
        }
        return result;
    }

    public bool TryPeek(Vector2Int cell, int index, out InventoryItem item)
    {
        item = default;
        if (!piles.TryGetValue(cell, out var list) || index < 0 || index >= list.Count)
            return false;
        item = list[index];
        return true;
    }

    public bool TryTakeAt(Vector2Int cell, int index, out InventoryItem item)
    {
        item = default;
        if (!piles.TryGetValue(cell, out var list) || index < 0 || index >= list.Count)
            return false;
        item = list[index];
        list.RemoveAt(index);
        if (list.Count == 0)
            piles.Remove(cell);
        RefreshVisual(cell);
        return true;
    }

    /// <summary>拾取指定格上指定下标的一件物品（允许暂时超重）。</summary>
    public bool TryPickupOne(Vector2Int cell, int index, Inventory inventory)
    {
        if (inventory == null || !piles.TryGetValue(cell, out var list))
            return false;
        if (index < 0 || index >= list.Count)
            return false;
        var item = list[index];

        list.RemoveAt(index);
        inventory.Add(item);
        if (list.Count == 0)
            piles.Remove(cell);
        RefreshVisual(cell);
        return true;
    }

    /// <summary>脚下是否有可直接使用的血瓶（濒死自救）。</summary>
    public bool HasPotionAt(Vector2Int cell)
    {
        if (!piles.TryGetValue(cell, out var list))
            return false;
        foreach (var item in list)
        {
            if (ItemInfo.IsPotion(item.Kind))
                return true;
        }
        return false;
    }

    /// <summary>取走该格第一瓶血瓶（不进入背包）。</summary>
    public bool TryTakeFirstPotion(Vector2Int cell, out InventoryItem item)
    {
        item = default;
        if (!piles.TryGetValue(cell, out var list))
            return false;
        for (int i = 0; i < list.Count; i++)
        {
            if (!ItemInfo.IsPotion(list[i].Kind))
                continue;
            item = list[i];
            list.RemoveAt(i);
            if (list.Count == 0)
                piles.Remove(cell);
            RefreshVisual(cell);
            return true;
        }
        return false;
    }

    public int TryPickupAll(Vector2Int cell, Inventory inventory)
    {
        if (!piles.TryGetValue(cell, out var list) || list.Count == 0)
            return 0;

        int picked = 0;
        for (int i = list.Count - 1; i >= 0; i--)
        {
            if (inventory.Add(list[i]))
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

    public int TryPickupAround(Vector2Int center, Inventory inventory)
    {
        int total = 0;
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                if (Mathf.Abs(dx) + Mathf.Abs(dy) > 1)
                    continue;
                total += TryPickupAll(center + new Vector2Int(dx, dy), inventory);
            }
        }
        return total;
    }

    /// <summary>熔岩吞噬格子上的掉落物，送入弃牌堆。返回件数。</summary>
    public int SwallowCellsToDiscard(List<Vector2Int> cells)
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
            foreach (var item in list)
            {
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

    public void ClearAll()
    {
        foreach (var kv in visuals)
        {
            if (kv.Value != null)
                Destroy(kv.Value);
        }
        visuals.Clear();
        piles.Clear();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void RefreshVisual(Vector2Int cell)
    {
        bool has = piles.TryGetValue(cell, out var list) && list.Count > 0;
        if (!has)
        {
            if (visuals.TryGetValue(cell, out var old) && old != null)
                Destroy(old);
            visuals.Remove(cell);
            return;
        }

        bool hasDoll = false;
        for (int i = 0; i < list.Count; i++)
        {
            if (ItemInfo.IsDoll(list[i].Kind))
            {
                hasDoll = true;
                break;
            }
        }

        Color fill = hasDoll
            ? ItemInfo.GetDollGoldFill()
            : new Color(0.25f, 0.55f, 0.95f, 1f);
        Color border = hasDoll
            ? ItemInfo.GetDollGoldBorder()
            : new Color(0.1f, 0.25f, 0.5f, 1f);

        if (!visuals.TryGetValue(cell, out var go) || go == null)
        {
            go = new GameObject($"Loot_{cell.x}_{cell.y}");
            go.transform.SetParent(transform, false);
            go.AddComponent<SpriteRenderer>();
            visuals[cell] = go;
        }

        var sr = go.GetComponent<SpriteRenderer>();
        sr.sprite = SpriteFactory.CreateBorderedSprite(fill, border, 16, 2);
        var grid = GridManager.Instance;
        sr.sortingOrder = grid.GetSortOrder(cell, hasDoll ? 35 : 30);

        // 东南角偏移（XZ），贴在格面高度上
        go.transform.position = grid.CellToWorld(cell) + new Vector3(0.32f, 0.04f, -0.32f);
        go.transform.localScale = Vector3.one * (hasDoll ? 0.34f : 0.28f);
        if (go.GetComponent<CameraBillboard>() == null)
            go.AddComponent<CameraBillboard>();

        var viewer = VisibilityService.GetFogViewer();
        go.SetActive(viewer == null || VisibilityService.CanSeeCell(viewer, cell));
    }

    public void RefreshAllVisibility(UnitActor viewer)
    {
        foreach (var kv in visuals)
        {
            if (kv.Value == null)
                continue;
            bool show = viewer == null || VisibilityService.CanSeeCell(viewer, kv.Key);
            kv.Value.SetActive(show);
        }
    }

    /// <summary>悬停用：该格掉落物摘要，无则 null。</summary>
    public string DescribeLoot(Vector2Int cell)
    {
        if (!piles.TryGetValue(cell, out var list) || list.Count == 0)
            return null;

        var parts = new List<string>();
        foreach (var item in list)
        {
            if (ItemInfo.IsStackableAmmo(item.Kind))
                parts.Add($"{ItemInfo.GetDisplayName(item.Kind)}×{item.Charges}");
            else
                parts.Add(ItemInfo.GetDisplayName(item.Kind));
        }
        return "掉落：" + string.Join("、", parts);
    }
}
