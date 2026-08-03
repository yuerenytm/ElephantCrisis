using UnityEngine;

/// <summary>程序化地形顶面 / 高地崖壁贴图（点滤波色块风格，各地形可区分）。</summary>
public static class TerrainSpriteFactory
{
    public static Sprite CreateTop(TileType type, bool checkerAlt, int size = 32)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        var fill = TerrainInfo.GetFillColor(type, checkerAlt);
        var border = TerrainInfo.GetBorderColor(type);
        var accent = Accent(type, fill);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                bool edge = x == 0 || y == 0 || x == size - 1 || y == size - 1;
                if (edge)
                {
                    tex.SetPixel(x, y, border);
                    continue;
                }

                Color c = fill;
                float n = Mathf.PerlinNoise((x + TypeSeed(type)) * 0.17f, (y + TypeSeed(type) * 3) * 0.17f);
                switch (type)
                {
                    case TileType.Sand:
                        // 沙纹：斜向细带
                        if (((x + y / 2) % 5) == 0)
                            c = Color.Lerp(fill, accent, 0.35f);
                        c = Color.Lerp(c, accent, n * 0.2f);
                        break;
                    case TileType.Swamp:
                        // 浊斑 / 水洼
                        if (n > 0.62f)
                            c = Color.Lerp(fill, accent, 0.55f);
                        else if (n < 0.28f)
                            c = Color.Lerp(fill, border, 0.35f);
                        break;
                    case TileType.Ice:
                        // 裂纹
                        if (x == size / 2 || y == size / 3 || (x + y) % 11 == 0)
                            c = Color.Lerp(fill, accent, 0.65f);
                        c = Color.Lerp(c, Color.white, n * 0.18f);
                        break;
                    case TileType.Jungle:
                        // 叶簇斑点
                        if (n > 0.55f)
                            c = Color.Lerp(fill, accent, 0.5f);
                        if (((x * 3 + y * 5) % 7) == 0)
                            c = Color.Lerp(c, border, 0.4f);
                        break;
                    case TileType.Highland:
                        // 岩块
                        int bx = x / 6;
                        int by = y / 6;
                        float bn = (bx * 13 + by * 7) % 5 / 5f;
                        c = Color.Lerp(fill, accent, bn * 0.45f);
                        if (x % 6 == 0 || y % 6 == 0)
                            c = Color.Lerp(c, border, 0.35f);
                        break;
                    case TileType.Lava:
                        float heat = Mathf.PerlinNoise(x * 0.28f, y * 0.28f);
                        c = Color.Lerp(border, fill, heat);
                        if (heat > 0.7f)
                            c = Color.Lerp(c, new Color(1f, 0.85f, 0.25f), 0.55f);
                        break;
                    default:
                        // 草地：稀疏草点
                        if (((x * 17 + y * 31) % 13) == 0)
                            c = Color.Lerp(fill, accent, 0.55f);
                        c = Color.Lerp(c, accent, n * 0.12f);
                        break;
                }

                tex.SetPixel(x, y, c);
            }
        }

        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }

    /// <summary>高地侧面：岩层条纹，枢轴在顶边中心便于贴顶面下沿。</summary>
    public static Sprite CreateHighlandCliff(int width = 32, int height = 16)
    {
        var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        var top = new Color(0.5f, 0.44f, 0.36f);
        var mid = new Color(0.36f, 0.3f, 0.24f);
        var dark = new Color(0.22f, 0.17f, 0.13f);
        var seam = new Color(0.16f, 0.12f, 0.09f);

        for (int y = 0; y < height; y++)
        {
            float t = y / (float)(height - 1);
            var row = Color.Lerp(dark, top, t);
            for (int x = 0; x < width; x++)
            {
                var c = row;
                if (y == height - 1)
                    c = top;
                else if (y % 4 == 0)
                    c = Color.Lerp(c, seam, 0.55f);
                else if ((x + y) % 9 == 0)
                    c = Color.Lerp(c, mid, 0.4f);
                if (x == 0 || x == width - 1)
                    c = seam;
                tex.SetPixel(x, y, c);
            }
        }

        tex.Apply();
        // 枢轴在上边中心：贴在抬升顶面下沿
        return Sprite.Create(tex, new Rect(0, 0, width, height), new Vector2(0.5f, 1f), width);
    }

    private static int TypeSeed(TileType type) => (int)type * 37 + 11;

    private static Color Accent(TileType type, Color fill)
    {
        switch (type)
        {
            case TileType.Sand: return new Color(0.92f, 0.82f, 0.55f);
            case TileType.Swamp: return new Color(0.22f, 0.28f, 0.18f);
            case TileType.Ice: return new Color(0.78f, 0.92f, 1f);
            case TileType.Jungle: return new Color(0.28f, 0.62f, 0.28f);
            case TileType.Highland: return new Color(0.62f, 0.55f, 0.45f);
            case TileType.Lava: return new Color(1f, 0.45f, 0.1f);
            default: return new Color(fill.r + 0.08f, fill.g + 0.12f, fill.b + 0.05f);
        }
    }
}
