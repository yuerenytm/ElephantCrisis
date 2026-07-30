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
        AddCopies(ItemKind.Bomb, 5);
        AddCopies(ItemKind.MegaBomb, 2);
        AddCopies(ItemKind.TimedBomb, 3);
        AddCopies(ItemKind.Reinforce, 3);
        AddCopies(ItemKind.Bow, 3);
        AddCopies(ItemKind.Crossbow, 1);
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
        TurnManager.Instance?.LogFor(unit,
            $"{RoleInfo.GetDisplayName(unit.Role)} 抽到【{ItemInfo.GetDisplayName(card)}】（库{drawPile.Count}/弃{discardPile.Count}）");
        if (unit.Inventory.IsOverCapacity)
        {
            TurnManager.Instance?.LogFor(unit,
                $"背包已超重（{unit.Inventory.UsedWeight:0.##}/{unit.Inventory.Capacity:0.##}），结束回合前需弃置");
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
