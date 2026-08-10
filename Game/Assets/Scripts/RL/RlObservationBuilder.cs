using System.Collections.Generic;
using UnityEngine;

/// <summary>构建固定长度向量观测（参数共享 + 角色 one-hot）。</summary>
public static class RlObservationBuilder
{
    public const int Size = 96;

    private static readonly StatusType[] StatusOrder =
    {
        StatusType.Poison,
        StatusType.Burning,
        StatusType.Stun,
        StatusType.Trip,
        StatusType.Hidden,
        StatusType.Blind,
        StatusType.Leader
    };

    public static void Write(UnitActor self, List<UnitActor> allUnits, float[] buffer)
    {
        if (buffer == null || buffer.Length < Size)
            return;
        for (int i = 0; i < Size; i++)
            buffer[i] = 0f;

        int o = 0;
        // Role one-hot (4)
        for (int r = 0; r < 4; r++)
            buffer[o++] = (int)self.Role == r ? 1f : 0f;

        // Self scalars
        buffer[o++] = self.MaxHp > 0 ? self.Hp / (float)self.MaxHp : 0f;
        buffer[o++] = Mathf.Clamp01(self.CurrentDef / 40f);
        buffer[o++] = Mathf.Clamp01(self.CurrentMove / 12f);
        buffer[o++] = Mathf.Clamp01(self.Inventory != null ? self.Inventory.UsedWeight / Mathf.Max(1f, self.CurrentBagCapacity) : 0f);
        buffer[o++] = Mathf.Clamp01(self.SkillLevel / 3f);
        buffer[o++] = Mathf.Clamp01(self.SkillCooldownLeft / 6f);
        buffer[o++] = TurnManager.Instance != null && TurnManager.Instance.HasMoved ? 1f : 0f;
        buffer[o++] = self.IsDying ? 1f : 0f;
        buffer[o++] = self.IsDead ? 1f : 0f;
        buffer[o++] = NormalizeCoord(self.Cell.x);
        buffer[o++] = NormalizeCoord(self.Cell.y);

        foreach (var st in StatusOrder)
        {
            if (st == StatusType.Poison)
                buffer[o++] = Mathf.Clamp01(self.PoisonStacks / (float)UnitActor.MaxPoisonStacks);
            else
                buffer[o++] = self.HasStatus(st) ? 1f : 0f;
        }

        // Inventory counts (clipped)
        o = WriteItemCount(buffer, o, self, ItemKind.SmallPotion, 3);
        o = WriteItemCount(buffer, o, self, ItemKind.LargePotion, 3);
        o = WriteItemCount(buffer, o, self, ItemKind.Bomb, 3);
        o = WriteItemCount(buffer, o, self, ItemKind.MegaBomb, 2);
        o = WriteItemCount(buffer, o, self, ItemKind.Bow, 1);
        o = WriteItemCount(buffer, o, self, ItemKind.Crossbow, 1);
        o = WriteAmmo(buffer, o, self, 6);
        o = WriteItemCount(buffer, o, self, ItemKind.WoodArmor, 1);
        o = WriteItemCount(buffer, o, self, ItemKind.IronArmor, 1);
        o = WriteItemCount(buffer, o, self, ItemKind.EnergyShield, 1);
        o = WriteItemCount(buffer, o, self, ItemKind.SkillUpgrade, 5);
        o = WriteDolls(buffer, o, self);

        // Opponents (up to 3), ordered by Role enum excluding self
        var others = ListOthers(self, allUnits);
        for (int i = 0; i < 3; i++)
        {
            if (i >= others.Count || others[i] == null)
            {
                o += 8;
                continue;
            }
            var u = others[i];
            bool visible = IsVisibleTo(self, u);
            buffer[o++] = visible ? 1f : 0f;
            buffer[o++] = visible && u.MaxHp > 0 ? u.Hp / (float)u.MaxHp : 0f;
            buffer[o++] = visible ? NormalizeCoord(u.Cell.x - self.Cell.x + 18) : 0f;
            buffer[o++] = visible ? NormalizeCoord(u.Cell.y - self.Cell.y + 18) : 0f;
            buffer[o++] = visible && u.IsDying ? 1f : 0f;
            buffer[o++] = visible && u.HasStatus(StatusType.Hidden) ? 1f : 0f;
            buffer[o++] = visible && u.HasStatus(StatusType.Stun) ? 1f : 0f;
            buffer[o++] = (int)u.Role / 3f;
        }

        // Local terrain: self + 4-neighbors as normalized tile id
        o = WriteTile(buffer, o, self.Cell);
        o = WriteTile(buffer, o, self.Cell + Vector2Int.up);
        o = WriteTile(buffer, o, self.Cell + Vector2Int.down);
        o = WriteTile(buffer, o, self.Cell + Vector2Int.left);
        o = WriteTile(buffer, o, self.Cell + Vector2Int.right);

        // Global
        buffer[o++] = WeatherOneHot(WeatherService.Current, 0);
        buffer[o++] = WeatherOneHot(WeatherService.Current, 1);
        buffer[o++] = WeatherOneHot(WeatherService.Current, 2);
        int lava = GridManager.Instance != null ? GridManager.Instance.LavaInset : 0;
        buffer[o++] = Mathf.Clamp01(lava / 8f);
        int round = TurnManager.Instance != null ? TurnManager.Instance.RoundNumber : 1;
        buffer[o++] = Mathf.Clamp01(round / 80f);

        // Pad remaining with zeros (already cleared)
        if (o > Size)
            Debug.LogError($"RlObservation overflow o={o} Size={Size}");
    }

    private static List<UnitActor> ListOthers(UnitActor self, List<UnitActor> all)
    {
        var list = new List<UnitActor>(3);
        if (all == null)
            return list;
        for (int r = 0; r < 4; r++)
        {
            if (r == (int)self.Role)
                continue;
            UnitActor found = null;
            foreach (var u in all)
            {
                if (u != null && (int)u.Role == r)
                {
                    found = u;
                    break;
                }
            }
            list.Add(found);
        }
        return list;
    }

    private static bool IsVisibleTo(UnitActor viewer, UnitActor other)
    {
        if (viewer == null || other == null || other.IsDead)
            return false;
        if (other.HasStatus(StatusType.Hidden))
            return false;
        if (GridManager.Instance == null)
            return false;
        int vis = viewer.CurrentVisibility;
        int d = GridManager.Instance.GetManhattanDistance(viewer.Cell, other.Cell);
        return d <= vis;
    }

    private static int WriteItemCount(float[] buf, int o, UnitActor u, ItemKind kind, int clip)
    {
        int c = u.Inventory != null ? u.Inventory.CountOf(kind) : 0;
        buf[o++] = Mathf.Clamp01(c / (float)Mathf.Max(1, clip));
        return o;
    }

    private static int WriteAmmo(float[] buf, int o, UnitActor u, int clip)
    {
        int c = 0;
        if (u.Inventory != null)
        {
            c += u.Inventory.CountOf(ItemKind.Arrow);
            c += u.Inventory.CountOf(ItemKind.PoisonArrow);
            c += u.Inventory.CountOf(ItemKind.FireRocket);
        }
        buf[o++] = Mathf.Clamp01(c / (float)clip);
        return o;
    }

    private static int WriteDolls(float[] buf, int o, UnitActor u)
    {
        int n = 0;
        if (u.Inventory != null)
        {
            if (u.Inventory.CountOf(ItemKind.DollElephant) > 0) n++;
            if (u.Inventory.CountOf(ItemKind.DollHuman) > 0) n++;
            if (u.Inventory.CountOf(ItemKind.DollMonkey) > 0) n++;
            if (u.Inventory.CountOf(ItemKind.DollCat) > 0) n++;
        }
        buf[o++] = n / 4f;
        return o;
    }

    private static int WriteTile(float[] buf, int o, Vector2Int cell)
    {
        var grid = GridManager.Instance;
        if (grid == null || !grid.IsValidCell(cell))
        {
            buf[o++] = 0f;
            return o;
        }
        buf[o++] = ((int)grid.GetTileType(cell) + 1) / 8f;
        return o;
    }

    private static float NormalizeCoord(int v) => Mathf.Clamp01(v / 18f);

    private static float WeatherOneHot(WeatherType w, int index)
    {
        int wi = w switch
        {
            WeatherType.Clear => 0,
            WeatherType.Rain => 1,
            WeatherType.Fog => 2,
            _ => 0
        };
        return wi == index ? 1f : 0f;
    }
}
