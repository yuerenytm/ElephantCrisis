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
                        // 浓绿苔地 + 叶斑 + 藤蔓暗纹
                        if (n > 0.48f)
                            c = Color.Lerp(fill, accent, 0.55f);
                        else if (n < 0.3f)
                            c = Color.Lerp(fill, border, 0.35f);
                        if (((x * 3 + y * 5) % 6) == 0)
                            c = Color.Lerp(c, accent, 0.45f);
                        // 斜向藤蔓
                        if (((x + y * 2) % 9) == 0 || ((x * 2 - y + size) % 11) == 0)
                            c = Color.Lerp(c, border, 0.55f);
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

    /// <summary>高地侧面：岩层条纹；枢轴在底边，从地面长到台面。</summary>
    public static Sprite CreateHighlandCliff(int width = 32, int height = 24)
    {
        var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        var top = new Color(0.5f, 0.44f, 0.36f);
        var mid = new Color(0.36f, 0.3f, 0.24f);
        var dark = new Color(0.22f, 0.17f, 0.13f);
        var seam = new Color(0.16f, 0.12f, 0.09f);

        for (int y = 0; y < height; y++)
        {
            // y=0 贴地（暗），y=max 接台面（亮）
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
        return Sprite.Create(tex, new Rect(0, 0, width, height), new Vector2(0.5f, 0f), width);
    }

    /// <summary>棋盘外装饰林地坪（不可交互）。</summary>
    public static Sprite CreateForestFloor(bool alt, int size = 32)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        var fill = alt ? new Color(0.14f, 0.28f, 0.14f) : new Color(0.12f, 0.24f, 0.12f);
        var border = new Color(0.08f, 0.16f, 0.08f);
        var accent = new Color(0.2f, 0.36f, 0.16f);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                if (x == 0 || y == 0 || x == size - 1 || y == size - 1)
                {
                    tex.SetPixel(x, y, border);
                    continue;
                }
                float n = Mathf.PerlinNoise(x * 0.2f + (alt ? 3f : 0f), y * 0.2f);
                var c = Color.Lerp(fill, accent, n * 0.45f);
                if (((x * 11 + y * 19) % 9) == 0)
                    c = Color.Lerp(c, border, 0.4f);
                tex.SetPixel(x, y, c);
            }
        }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }

    /// <summary>丛林格上的灌木 / 蕨 / 阔叶（广告牌，枢轴在脚下；画幅加厚）。</summary>
    public static Sprite CreateJungleProp(int variant, int size = 48)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                tex.SetPixel(x, y, Color.clear);

        var stem = new Color(0.28f, 0.2f, 0.1f);
        var leafA = new Color(0.14f, 0.48f, 0.18f);
        var leafB = new Color(0.08f, 0.36f, 0.14f);
        var leafC = new Color(0.22f, 0.58f, 0.24f);
        var leafD = new Color(0.35f, 0.55f, 0.2f);
        int v = Mathf.Abs(variant) % 3;

        if (v == 0)
        {
            // 圆灌木：粗干 + 多层重叠叶球
            for (int y = 0; y < size / 4; y++)
                for (int x = size / 2 - 2; x <= size / 2 + 2; x++)
                    tex.SetPixel(x, y, stem);
            FillTreeDisk(tex, size * 0.5f, size * 0.48f, size * 0.4f, leafA, leafB);
            FillTreeDisk(tex, size * 0.32f, size * 0.55f, size * 0.26f, leafC, leafA);
            FillTreeDisk(tex, size * 0.68f, size * 0.52f, size * 0.26f, leafB, leafD);
            FillTreeDisk(tex, size * 0.5f, size * 0.68f, size * 0.22f, leafD, leafC);
            FillTreeDisk(tex, size * 0.42f, size * 0.38f, size * 0.18f, leafB, leafA);
        }
        else if (v == 1)
        {
            // 蕨：粗中轴 + 密羽叶
            int cx = size / 2;
            for (int y = 1; y < size * 7 / 8; y++)
            {
                for (int dx = -1; dx <= 1; dx++)
                    tex.SetPixel(cx + dx, y, stem);
                float t = y / (float)size;
                int spread = Mathf.Max(2, Mathf.RoundToInt((0.22f + t * 0.42f) * size * 0.5f));
                for (int s = 1; s <= spread; s++)
                {
                    var c = (s + y) % 2 == 0 ? leafA : leafC;
                    int yy = Mathf.Min(size - 1, y + (s & 1));
                    if (cx - s >= 0) tex.SetPixel(cx - s, yy, c);
                    if (cx + s < size) tex.SetPixel(cx + s, yy, c);
                    if (s + 1 <= spread)
                    {
                        if (cx - s >= 0 && yy + 1 < size) tex.SetPixel(cx - s, yy + 1, leafB);
                        if (cx + s < size && yy + 1 < size) tex.SetPixel(cx + s, yy + 1, leafB);
                    }
                }
            }
        }
        else
        {
            // 阔叶：短粗干 + 四五片大叶叠层
            for (int y = 0; y < size / 3; y++)
                for (int x = size / 2 - 2; x <= size / 2 + 1; x++)
                    tex.SetPixel(x, y, stem);
            FillLeafBlade(tex, size * 0.3f, size * 0.52f, size * 0.28f, size * 0.4f, -32f, leafA, leafB);
            FillLeafBlade(tex, size * 0.7f, size * 0.5f, size * 0.28f, size * 0.4f, 34f, leafC, leafA);
            FillLeafBlade(tex, size * 0.48f, size * 0.68f, size * 0.26f, size * 0.38f, 4f, leafD, leafB);
            FillLeafBlade(tex, size * 0.38f, size * 0.62f, size * 0.22f, size * 0.32f, -12f, leafB, leafC);
            FillLeafBlade(tex, size * 0.62f, size * 0.64f, size * 0.22f, size * 0.32f, 16f, leafA, leafD);
        }

        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0f), size);
    }

    private static void FillLeafBlade(Texture2D tex, float cx, float cy, float rx, float ry, float angleDeg, Color a, Color b)
    {
        float rad = angleDeg * Mathf.Deg2Rad;
        float cos = Mathf.Cos(rad);
        float sin = Mathf.Sin(rad);
        int size = tex.width;
        int minX = Mathf.Max(0, Mathf.FloorToInt(cx - rx - ry));
        int maxX = Mathf.Min(size - 1, Mathf.CeilToInt(cx + rx + ry));
        int minY = Mathf.Max(0, Mathf.FloorToInt(cy - rx - ry));
        int maxY = Mathf.Min(size - 1, Mathf.CeilToInt(cy + rx + ry));
        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                float dx = x - cx;
                float dy = y - cy;
                float lx = dx * cos + dy * sin;
                float ly = -dx * sin + dy * cos;
                float nx = lx / Mathf.Max(0.001f, rx);
                float ny = ly / Mathf.Max(0.001f, ry);
                if (nx * nx + ny * ny > 1f)
                    continue;
                // 叶尖收窄
                if (Mathf.Abs(nx) > 0.2f && ny > 0.35f)
                    continue;
                tex.SetPixel(x, y, ((x + y) & 1) == 0 ? a : b);
            }
        }
    }

    /// <summary>装饰树冠广告牌（variant 换造型；枢轴在脚下）。</summary>
    public static Sprite CreateForestTree(int variant, int size = 32)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                tex.SetPixel(x, y, Color.clear);

        var trunk = new Color(0.32f, 0.22f, 0.12f);
        var leafA = new Color(0.16f, 0.42f, 0.18f);
        var leafB = new Color(0.1f, 0.32f, 0.14f);
        var leafC = new Color(0.22f, 0.5f, 0.22f);
        int v = Mathf.Abs(variant) % 3;

        // 树干
        int trunkW = v == 1 ? 3 : 2;
        int trunkTop = size / 3;
        for (int y = 0; y < trunkTop; y++)
            for (int x = size / 2 - trunkW / 2; x <= size / 2 + trunkW / 2; x++)
                tex.SetPixel(x, y, trunk);

        if (v == 0)
        {
            // 尖杉
            for (int y = trunkTop - 2; y < size - 2; y++)
            {
                float t = (y - (trunkTop - 2)) / (float)(size - trunkTop);
                int half = Mathf.Max(1, Mathf.RoundToInt((1f - t) * size * 0.28f));
                for (int x = size / 2 - half; x <= size / 2 + half; x++)
                {
                    var c = ((x + y) & 1) == 0 ? leafA : leafB;
                    if (y > size - 6)
                        c = leafC;
                    tex.SetPixel(x, y, c);
                }
            }
        }
        else if (v == 1)
        {
            // 圆冠
            FillTreeDisk(tex, size * 0.5f, size * 0.62f, size * 0.28f, leafA, leafB);
            FillTreeDisk(tex, size * 0.38f, size * 0.52f, size * 0.16f, leafB, leafC);
            FillTreeDisk(tex, size * 0.62f, size * 0.55f, size * 0.15f, leafC, leafA);
        }
        else
        {
            // 双层杉
            for (int layer = 0; layer < 2; layer++)
            {
                int y0 = trunkTop - 1 + layer * (size / 5);
                int y1 = y0 + size / 4;
                for (int y = y0; y < y1 && y < size; y++)
                {
                    float t = (y - y0) / (float)Mathf.Max(1, y1 - y0);
                    int half = Mathf.Max(1, Mathf.RoundToInt((1f - t) * size * 0.26f));
                    for (int x = size / 2 - half; x <= size / 2 + half; x++)
                        tex.SetPixel(x, y, (layer + x + y) % 2 == 0 ? leafA : leafB);
                }
            }
        }

        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0f), size);
    }

    private static void FillTreeDisk(Texture2D tex, float cx, float cy, float r, Color a, Color b)
    {
        int size = tex.width;
        float r2 = r * r;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x + 0.5f - cx;
                float dy = y + 0.5f - cy;
                float d2 = dx * dx + dy * dy;
                if (d2 > r2)
                    continue;
                tex.SetPixel(x, y, ((x * 3 + y * 5) & 1) == 0 ? a : b);
            }
        }
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
