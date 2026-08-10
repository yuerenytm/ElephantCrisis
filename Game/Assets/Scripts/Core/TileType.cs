using UnityEngine;

public enum TileType
{
    Normal,
    Sand,
    Swamp,
    Ice,
    Jungle,
    Highland,
    Lava
}

/// <summary>地形显示名、色块与数值修正。</summary>
public static class TerrainInfo
{
    public static string GetDisplayName(TileType type)
    {
        switch (type)
        {
            case TileType.Normal: return "普通";
            case TileType.Sand: return "沙地";
            case TileType.Swamp: return "沼泽";
            case TileType.Ice: return "冰地";
            case TileType.Jungle: return "丛林";
            case TileType.Highland: return "高地";
            case TileType.Lava: return "熔岩";
            default: return type.ToString();
        }
    }

    public static string GetShortEffect(TileType type)
    {
        switch (type)
        {
            case TileType.Sand: return "移动力-1；法抗-10";
            case TileType.Swamp: return "中毒；每行动始叠层；离开清地形层；法抗-20";
            case TileType.Ice: return "移动力+1；行动开始20%跌倒";
            case TileType.Jungle: return "进入获得隐匿；法抗+20";
            case TileType.Highland: return "攻击+3 防御+3 射程+1";
            case TileType.Lava: return "行动开始20真伤";
            default: return "无修正";
        }
    }

    public static string GetDescription(TileType type)
        => $"{GetDisplayName(type)}：{GetShortEffect(type)}";

    public static Color GetFillColor(TileType type, bool checkerAlt)
    {
        switch (type)
        {
            case TileType.Sand:
                return checkerAlt
                    ? new Color(0.78f, 0.68f, 0.42f)
                    : new Color(0.85f, 0.75f, 0.48f);
            case TileType.Swamp:
                // 浊橄榄褐，与普通草地拉开色相
                return checkerAlt
                    ? new Color(0.32f, 0.34f, 0.16f)
                    : new Color(0.38f, 0.40f, 0.20f);
            case TileType.Ice:
                // 淡蓝色
                return checkerAlt
                    ? new Color(0.55f, 0.78f, 0.92f)
                    : new Color(0.62f, 0.84f, 0.96f);
            case TileType.Jungle:
                return checkerAlt
                    ? new Color(0.12f, 0.38f, 0.18f)
                    : new Color(0.16f, 0.45f, 0.22f);
            case TileType.Highland:
                return checkerAlt
                    ? new Color(0.48f, 0.42f, 0.36f)
                    : new Color(0.55f, 0.48f, 0.4f);
            case TileType.Lava:
                return new Color(0.85f, 0.28f, 0.1f);
            default: // Normal：偏亮草绿
                return checkerAlt
                    ? new Color(0.26f, 0.42f, 0.30f)
                    : new Color(0.32f, 0.48f, 0.34f);
        }
    }

    public static Color GetBorderColor(TileType type)
    {
        switch (type)
        {
            case TileType.Sand: return new Color(0.45f, 0.35f, 0.18f);
            case TileType.Swamp: return new Color(0.18f, 0.16f, 0.08f);
            case TileType.Ice: return new Color(0.3f, 0.5f, 0.65f);
            case TileType.Jungle: return new Color(0.05f, 0.18f, 0.08f);
            case TileType.Highland: return new Color(0.28f, 0.22f, 0.18f);
            case TileType.Lava: return new Color(0.4f, 0.1f, 0.05f);
            default: return new Color(0.12f, 0.16f, 0.12f);
        }
    }

    public static int GetMoveMod(TileType type)
    {
        switch (type)
        {
            case TileType.Sand: return -1;
            case TileType.Ice: return 1;
            default: return 0; // 沼泽不再直接改移速（经中毒状态）
        }
    }

    public static int GetAtkMod(TileType type)
        => type == TileType.Highland ? 3 : 0;

    public static int GetDefMod(TileType type)
    {
        switch (type)
        {
            case TileType.Highland: return 3;
            default: return 0; // 沼泽不再直接改防御（经中毒状态）
        }
    }

    /// <summary>地形法抗修正（百分比，平值相加，下限 0 由 UnitActor.MagicResist 统一夹取）。丛林 +20、沙地 −10、沼泽 −20。</summary>
    public static int GetMagicResistMod(TileType type)
    {
        switch (type)
        {
            case TileType.Jungle: return 20;
            case TileType.Sand: return -10;
            case TileType.Swamp: return -20;
            default: return 0;
        }
    }

    public static int GetRangeBonus(TileType type)
        => type == TileType.Highland ? 1 : 0;

    /// <summary>世界 Y 抬升（仅高地；单位/掉落跟随 CellToWorld）。</summary>
    public static float GetElevation(TileType type)
        => type == TileType.Highland ? GridManager.HighlandElevation : 0f;
}
