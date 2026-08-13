using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 共用限量牌库：开局构建后背面散落到地图格；结算进弃牌；场上弃置/散落物被熔岩吞噬后进弃牌。
/// 行动开始不再摸牌；管理员虚空印牌不走牌库。
/// </summary>
public class DeckManager : MonoBehaviour
{
    public static DeckManager Instance;

    private readonly List<ItemKind> drawPile = new List<ItemKind>();
    private readonly List<ItemKind> discardPile = new List<ItemKind>();

    public int DrawCount => drawPile.Count;
    public int DiscardCount => discardPile.Count;

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public void BuildDemoDeck()
    {
        drawPile.Clear();
        discardPile.Clear();

        // 印数来自 game_rules.yaml → GameRulesConfig.deck
        var counts = new List<KeyValuePair<ItemKind, int>>(48);
        GameRulesConfig.CollectPositiveDeckCounts(counts);
        for (int i = 0; i < counts.Count; i++)
            AddCopies(counts[i].Key, counts[i].Value);

        Shuffle(drawPile);
        TurnManager.Instance?.Log($"牌库已构建：{drawPile.Count} 张（读表）");
    }

    /// <summary>
    /// 将当前抽牌堆随机散落到地图每一格至多 1 张（18×18=324 格与印数对齐时恰好铺满）。
    /// 散落后抽牌堆清空（多余牌留在库中；格数不足时余牌留库）。
    /// </summary>
    public void ScatterDrawPileOntoMap()
    {
        var grid = GridManager.Instance;
        var ground = GroundItemManager.Instance;
        if (grid == null || ground == null)
        {
            TurnManager.Instance?.Log("散落失败：地图或地面掉落系统未就绪");
            return;
        }

        var cells = new List<Vector2Int>(grid.gridWidth * grid.gridHeight);
        for (int x = 0; x < grid.gridWidth; x++)
        {
            for (int y = 0; y < grid.gridHeight; y++)
                cells.Add(new Vector2Int(x, y));
        }

        Shuffle(cells);
        Shuffle(drawPile);

        int n = Mathf.Min(drawPile.Count, cells.Count);
        for (int i = 0; i < n; i++)
            ground.ScatterFaceDown(cells[i], InventoryItem.CreateFresh(drawPile[i]));

        if (n > 0)
            drawPile.RemoveRange(0, n);

        TurnManager.Instance?.Log(
            $"开局散落：{n} 张卡牌分布于地图" +
            (drawPile.Count > 0 ? $"（牌库余 {drawPile.Count}）" : "（牌库已空）") +
            (cells.Count > n ? $"（{cells.Count - n} 格无牌）" : ""));
    }

    private void AddCopies(ItemKind kind, int count)
    {
        for (int i = 0; i < count; i++)
            drawPile.Add(kind);
    }

    public void AddToDiscard(ItemKind kind)
    {
        discardPile.Add(kind);
    }

    public void AddRangeToDiscard(IEnumerable<ItemKind> items)
    {
        if (items == null)
            return;
        discardPile.AddRange(items);
    }

    public bool TryDraw(out ItemKind card)
    {
        card = default;
        if (drawPile.Count == 0)
            ReshuffleDiscardIntoDraw();

        if (drawPile.Count == 0)
            return false;

        int last = drawPile.Count - 1;
        card = drawPile[last];
        drawPile.RemoveAt(last);
        return true;
    }

    public int CountInDraw(ItemKind kind)
    {
        int n = 0;
        for (int i = 0; i < drawPile.Count; i++)
        {
            if (drawPile[i] == kind)
                n++;
        }
        return n;
    }

    public int CountInDiscard(ItemKind kind)
    {
        int n = 0;
        for (int i = 0; i < discardPile.Count; i++)
        {
            if (discardPile[i] == kind)
                n++;
        }
        return n;
    }

    /// <summary>牌库+弃牌堆中该牌剩余张数（管理员选牌用）。</summary>
    public int CountAvailable(ItemKind kind) => CountInDraw(kind) + CountInDiscard(kind);

    /// <summary>从牌库取出指定牌（牌库无则先洗入弃牌堆）；成功则移出牌库。</summary>
    public bool TryTakeSpecific(ItemKind kind)
    {
        if (TryRemoveFromDraw(kind))
            return true;
        ReshuffleDiscardIntoDraw();
        return TryRemoveFromDraw(kind);
    }

    private bool TryRemoveFromDraw(ItemKind kind)
    {
        for (int i = drawPile.Count - 1; i >= 0; i--)
        {
            if (drawPile[i] != kind)
                continue;
            drawPile.RemoveAt(i);
            return true;
        }
        return false;
    }

    public void DrawFor(UnitActor unit)
    {
        if (unit == null || unit.IsDead || unit.IsDying)
            return;

        if (!TryDraw(out var card))
        {
            TurnManager.Instance?.LogFor(unit, $"{RoleInfo.GetDisplayName(unit.Role)} 牌库与弃牌堆皆空，跳过抽牌");
            return;
        }

        unit.Inventory.Add(card);
        string drawName = ItemInfo.GetDisplayName(card);
        if (ItemInfo.IsDoll(card))
            drawName = $"★{drawName}★";
        TurnManager.Instance?.LogFor(unit,
            $"{RoleInfo.GetDisplayName(unit.Role)} 抽到【{drawName}】（库{drawPile.Count}/弃{discardPile.Count}）");
        LogicMatchLogger.Active?.EmitDraw(unit, card);
        if (unit.Inventory.IsOverCapacity)
        {
            TurnManager.Instance?.LogFor(unit,
                $"背包已超重（{unit.Inventory.UsedWeight:0.##}/{unit.Inventory.Capacity:0.##}），超重时无法结束行动");
        }
    }

    private void ReshuffleDiscardIntoDraw()
    {
        if (discardPile.Count == 0)
            return;

        drawPile.AddRange(discardPile);
        discardPile.Clear();
        Shuffle(drawPile);
        TurnManager.Instance?.Log($"弃牌堆洗入牌库，现有 {drawPile.Count} 张");
    }

    private static void Shuffle<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
