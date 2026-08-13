using System.Collections.Generic;
using UnityEngine;

public class GroundItemManager : MonoBehaviour
{
    public static GroundItemManager Instance;

    /// <summary>地面堆叠条目：开局散落 FaceDown=true（仅知有牌）；弃置/濒死掉落 FaceDown=false（正面可见）。</summary>
    public struct GroundStackEntry
    {
        public InventoryItem Item;
        public bool FaceDown;
    }

    private readonly Dictionary<Vector2Int, List<GroundStackEntry>> piles =
        new Dictionary<Vector2Int, List<GroundStackEntry>>();
    private readonly Dictionary<Vector2Int, GameObject> visuals = new Dictionary<Vector2Int, GameObject>();

    /// <summary>开局散落背面色（与弃置掉落蓝区分；玩偶散落同色）。</summary>
    private static readonly Color ScatterFill = new Color(0.42f, 0.4f, 0.36f, 1f);
    private static readonly Color ScatterBorder = new Color(0.22f, 0.2f, 0.18f, 1f);
    /// <summary>弃置/掉落正面普通牌。</summary>
    private static readonly Color DropFill = new Color(0.25f, 0.55f, 0.95f, 1f);
    private static readonly Color DropBorder = new Color(0.1f, 0.25f, 0.5f, 1f);

    private void Awake()
    {
        Instance = this;
    }

    public void DropItems(Vector2Int cell, List<InventoryItem> items)
    {
        if (items == null || items.Count == 0)
            return;
        EnsurePile(cell);
        foreach (var item in items)
            piles[cell].Add(new GroundStackEntry { Item = item, FaceDown = false });
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

    /// <summary>开局散落：背面朝上，身份不可见。</summary>
    public void ScatterFaceDown(Vector2Int cell, InventoryItem item)
    {
        EnsurePile(cell);
        piles[cell].Add(new GroundStackEntry { Item = item, FaceDown = true });
        RefreshVisual(cell);
    }

    private void EnsurePile(Vector2Int cell)
    {
        if (!piles.ContainsKey(cell))
            piles[cell] = new List<GroundStackEntry>();
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
        var result = new List<InventoryItem>();
        if (!piles.TryGetValue(cell, out var list))
            return result;
        foreach (var e in list)
            result.Add(e.Item);
        return result;
    }

    public List<GroundStackEntry> PeekEntries(Vector2Int cell)
    {
        if (piles.TryGetValue(cell, out var list))
            return new List<GroundStackEntry>(list);
        return new List<GroundStackEntry>();
    }

    public bool IsFaceDown(Vector2Int cell, int index)
    {
        if (!piles.TryGetValue(cell, out var list) || index < 0 || index >= list.Count)
            return false;
        return list[index].FaceDown;
    }

    public struct GroundLootEntry
    {
        public Vector2Int Cell;
        public int Index;
        public ItemKind Kind;
        public int Charges;
        public bool FaceDown;
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
                        Kind = list[i].Item.Kind,
                        Charges = list[i].Item.Charges,
                        FaceDown = list[i].FaceDown
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
        item = list[index].Item;
        return true;
    }

    public bool TryTakeAt(Vector2Int cell, int index, out InventoryItem item)
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

    /// <summary>拾取指定格上指定下标的一件物品（允许暂时超重）。</summary>
    public bool TryPickupOne(Vector2Int cell, int index, Inventory inventory)
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

    /// <summary>脚下是否有可直接使用的血瓶（濒死自救）。仅计正面朝上的牌，不暴露开局散落身份。</summary>
    public bool HasPotionAt(Vector2Int cell)
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

    /// <summary>取走该格第一瓶正面血瓶（不进入背包）。</summary>
    public bool TryTakeFirstPotion(Vector2Int cell, out InventoryItem item)
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

    public int TryPickupAll(Vector2Int cell, Inventory inventory)
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

    public int TryPickupAround(Vector2Int center, Inventory inventory)
    {
        int total = 0;
        int radius = GameRulesConfig.PickupRange;
        for (int dx = -radius; dx <= radius; dx++)
        {
            for (int dy = -radius; dy <= radius; dy++)
            {
                if (Mathf.Abs(dx) + Mathf.Abs(dy) > radius)
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

        Color fill;
        Color border;
        bool emphasize;
        if (!anyFaceUp)
        {
            // 全为开局散落背面
            fill = ScatterFill;
            border = ScatterBorder;
            emphasize = false;
        }
        else if (faceUpDoll)
        {
            fill = ItemInfo.GetDollGoldFill();
            border = ItemInfo.GetDollGoldBorder();
            emphasize = true;
        }
        else
        {
            fill = DropFill;
            border = DropBorder;
            emphasize = false;
        }

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
        sr.sortingOrder = grid.GetSortOrder(cell, emphasize ? 35 : 30);

        go.transform.position = grid.CellToWorld(cell) + new Vector3(0.32f, 0.04f, -0.32f);
        go.transform.localScale = Vector3.one * (emphasize ? 0.34f : 0.28f);
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

    /// <summary>悬停用：该格掉落物摘要，无则 null。背面牌不暴露种类。</summary>
    public string DescribeLoot(Vector2Int cell)
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
