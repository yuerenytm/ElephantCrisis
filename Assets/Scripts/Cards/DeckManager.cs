using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 共用限量牌库：抽牌 → 结算进弃牌；场上弃置物被熔岩吞噬后进弃牌；牌库空则洗弃牌堆。
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

        AddCopies(ItemKind.SmallPotion, 5);
        AddCopies(ItemKind.LargePotion, 2);
        AddCopies(ItemKind.Bomb, 5);
        AddCopies(ItemKind.MegaBomb, 2);
        AddCopies(ItemKind.TimedBomb, 3);
        AddCopies(ItemKind.Reinforce, 3);
        AddCopies(ItemKind.Bow, 3);
        AddCopies(ItemKind.Crossbow, 1);
        AddCopies(ItemKind.Dagger, 3);
        AddCopies(ItemKind.Longsword, 2);
        AddCopies(ItemKind.Arrow, 15);
        AddCopies(ItemKind.PoisonArrow, 6);
        AddCopies(ItemKind.FireRocket, 6);
        AddCopies(ItemKind.BananaPeel, 5);
        AddCopies(ItemKind.Mine, 5);
        AddCopies(ItemKind.Flamethrower, 1);
        AddCopies(ItemKind.WoodArmor, 3);
        AddCopies(ItemKind.IronArmor, 1);
        AddCopies(ItemKind.EnergyShield, 3);
        AddCopies(ItemKind.Adrenaline, 5);
        AddCopies(ItemKind.NightVision, 2);
        AddCopies(ItemKind.Skateboard, 3);
        AddCopies(ItemKind.Motorcycle, 1);
        AddCopies(ItemKind.GrappleHook, 3);
        AddCopies(ItemKind.Flashbang, 3);
        AddCopies(ItemKind.SkillUpgrade, 12);
        // 四只玩偶各 1 张，入共用牌库（开局不携带、不散落）
        AddCopies(ItemKind.DollElephant, 1);
        AddCopies(ItemKind.DollHuman, 1);
        AddCopies(ItemKind.DollMonkey, 1);
        AddCopies(ItemKind.DollCat, 1);
        Shuffle(drawPile);

        TurnManager.Instance?.Log($"牌库已构建：{drawPile.Count} 张");
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

    private static void Shuffle(List<ItemKind> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
