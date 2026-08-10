using UnityEngine;

public static class SpriteFactory
{
    public static Sprite CreateColorSprite(Color color, int size = 32)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        var pixels = new Color[size * size];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = color;
        tex.SetPixels(pixels);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }

    public static Sprite CreateBorderedSprite(Color fill, Color border, int size = 32, int borderWidth = 2)
    {
        var tex = NewClearTex(size);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                bool isBorder = x < borderWidth || y < borderWidth
                    || x >= size - borderWidth || y >= size - borderWidth;
                tex.SetPixel(x, y, isBorder ? border : fill);
            }
        }
        tex.Apply();
        return ToSprite(tex, size);
    }

    public static Sprite CreateCircleSprite(Color fill, Color border, int size, float radiusNorm)
    {
        var tex = NewClearTex(size);
        float cx = (size - 1) * 0.5f;
        float cy = (size - 1) * 0.5f;
        float r = size * radiusNorm;
        float rOuter = r;
        float rInner = r - 2.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                if (d <= rInner)
                    tex.SetPixel(x, y, fill);
                else if (d <= rOuter)
                    tex.SetPixel(x, y, border);
            }
        }
        tex.Apply();
        return ToSprite(tex, size);
    }

    public static Sprite CreateEllipseSprite(Color fill, Color border, int size, float rxNorm, float ryNorm)
    {
        var tex = NewClearTex(size);
        float cx = (size - 1) * 0.5f;
        float cy = (size - 1) * 0.5f;
        float rx = size * rxNorm;
        float ry = size * ryNorm;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float nx = (x - cx) / rx;
                float ny = (y - cy) / ry;
                float d = nx * nx + ny * ny;
                if (d <= 0.78f)
                    tex.SetPixel(x, y, fill);
                else if (d <= 1f)
                    tex.SetPixel(x, y, border);
            }
        }
        tex.Apply();
        return ToSprite(tex, size);
    }

    public static Sprite CreateHumanoidSprite(Color fill, Color border, int size)
    {
        var tex = NewClearTex(size);
        // 头
        FillCircle(tex, size * 0.5f, size * 0.72f, size * 0.14f, fill, border);
        // 身
        FillRect(tex, size * 0.32f, size * 0.22f, size * 0.36f, size * 0.38f, fill, border);
        // 腿
        FillRect(tex, size * 0.34f, size * 0.06f, size * 0.12f, size * 0.2f, fill, border);
        FillRect(tex, size * 0.54f, size * 0.06f, size * 0.12f, size * 0.2f, fill, border);
        tex.Apply();
        return ToSprite(tex, size);
    }

    public static Sprite CreateCatSprite(Color fill, Color border, int size)
    {
        var tex = NewClearTex(size);
        float cx = size * 0.5f;
        float cy = size * 0.42f;
        FillCircle(tex, cx, cy, size * 0.28f, fill, border);
        // 耳朵
        FillTriangle(tex, cx - size * 0.22f, cy + size * 0.18f, size * 0.14f, fill, border);
        FillTriangle(tex, cx + size * 0.22f, cy + size * 0.18f, size * 0.14f, fill, border);
        tex.Apply();
        return ToSprite(tex, size);
    }

    /// <summary>结束回合圆形按钮：泥色象纹印章，贴合象群危机氛围。</summary>
    public static Sprite CreateEndTurnButton(int size = 256)
    {
        var tex = NewClearTex(size);
        tex.filterMode = FilterMode.Bilinear;
        float cx = (size - 1) * 0.5f;
        float cy = (size - 1) * 0.5f;
        float r = size * 0.48f;

        // 泥灰 / 象牙 / 密林，避免文明青绿金边套路
        var clayDark = new Color(0.28f, 0.18f, 0.1f, 1f);
        var clay = new Color(0.55f, 0.38f, 0.2f, 1f);
        var ivory = new Color(0.9f, 0.82f, 0.62f, 1f);
        var mossDeep = new Color(0.14f, 0.22f, 0.14f, 1f);
        var moss = new Color(0.22f, 0.34f, 0.2f, 1f);
        var mossLight = new Color(0.32f, 0.46f, 0.28f, 1f);
        var dust = new Color(0.42f, 0.32f, 0.2f, 1f);
        var stamp = new Color(0.78f, 0.68f, 0.48f, 0.92f);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - cx;
                float dy = y - cy;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                if (d > r)
                    continue;

                float t = d / r;
                Color c;
                if (t > 0.93f)
                    c = clayDark;
                else if (t > 0.84f)
                    c = Color.Lerp(ivory, clay, (t - 0.84f) / 0.09f);
                else if (t > 0.76f)
                    c = clayDark;
                else
                {
                    float face = Mathf.Clamp01(1f - t / 0.76f);
                    c = Color.Lerp(mossDeep, mossLight, face * 0.55f + 0.2f);
                    // 轻微尘土斑驳
                    float n = Mathf.PerlinNoise(x * 0.045f, y * 0.045f);
                    c = Color.Lerp(c, dust, n * 0.18f);
                    c = Color.Lerp(c, moss, (1f - Mathf.Abs(dy) / r) * 0.12f);
                }
                tex.SetPixel(x, y, c);
            }
        }

        // 内环细线（印章边）
        DrawRing(tex, cx, cy, r * 0.68f, ivory, 2);
        DrawRing(tex, cx, cy, r * 0.62f, clay, 1);

        // 象头侧影（中上偏左，给下方文字留空）
        float hx = cx - size * 0.02f;
        float hy = cy + size * 0.08f;
        var elephant = stamp;
        var elephantEdge = clayDark;
        // 耳
        FillEllipse(tex, hx - size * 0.12f, hy + size * 0.02f, size * 0.14f, size * 0.16f, elephant, elephantEdge);
        // 头
        FillEllipse(tex, hx + size * 0.02f, hy, size * 0.13f, size * 0.12f, elephant, elephantEdge);
        // 鼻梁/额
        FillEllipse(tex, hx + size * 0.08f, hy + size * 0.02f, size * 0.07f, size * 0.08f, elephant, elephantEdge);
        // 象牙
        FillEllipse(tex, hx + size * 0.1f, hy - size * 0.06f, size * 0.055f, size * 0.025f, ivory, clay);
        // 象鼻（下弯）
        DrawThickCurveTrunk(tex, hx + size * 0.12f, hy - size * 0.02f, size, elephant, elephantEdge);
        // 眼
        FillCircle(tex, hx + size * 0.04f, hy + size * 0.03f, size * 0.018f, clayDark, clayDark);

        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }

    private static void DrawRing(Texture2D tex, float cx, float cy, float radius, Color color, int thickness)
    {
        float r0 = radius - thickness * 0.5f;
        float r1 = radius + thickness * 0.5f;
        int min = Mathf.Max(0, Mathf.FloorToInt(cx - r1 - 1));
        int max = Mathf.Min(tex.width - 1, Mathf.CeilToInt(cx + r1 + 1));
        for (int y = min; y <= max; y++)
        {
            for (int x = min; x <= max; x++)
            {
                float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                if (d >= r0 && d <= r1)
                    tex.SetPixel(x, y, color);
            }
        }
    }

    private static void FillEllipse(Texture2D tex, float cx, float cy, float rx, float ry, Color fill, Color border)
    {
        int minX = Mathf.Max(0, Mathf.FloorToInt(cx - rx - 1));
        int maxX = Mathf.Min(tex.width - 1, Mathf.CeilToInt(cx + rx + 1));
        int minY = Mathf.Max(0, Mathf.FloorToInt(cy - ry - 1));
        int maxY = Mathf.Min(tex.height - 1, Mathf.CeilToInt(cy + ry + 1));
        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                float nx = (x - cx) / rx;
                float ny = (y - cy) / ry;
                float d = nx * nx + ny * ny;
                if (d <= 0.82f)
                    tex.SetPixel(x, y, fill);
                else if (d <= 1f)
                    tex.SetPixel(x, y, border);
            }
        }
    }

    private static void DrawThickCurveTrunk(Texture2D tex, float startX, float startY, int size, Color fill, Color border)
    {
        // 象鼻：先略右下，再内弯
        int steps = 28;
        for (int i = 0; i <= steps; i++)
        {
            float t = i / (float)steps;
            float x = startX + t * size * 0.02f + Mathf.Sin(t * Mathf.PI) * size * 0.04f;
            float y = startY - t * size * 0.22f;
            float rad = Mathf.Lerp(size * 0.035f, size * 0.022f, t);
            FillCircle(tex, x, y, rad, fill, border);
        }
        // 鼻尖微卷
        FillCircle(tex, startX + size * 0.01f, startY - size * 0.24f, size * 0.028f, fill, border);
        FillCircle(tex, startX - size * 0.02f, startY - size * 0.23f, size * 0.022f, fill, border);
    }

    private static Texture2D NewClearTex(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        var clear = new Color(0, 0, 0, 0);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                tex.SetPixel(x, y, clear);
        return tex;
    }

    private static Sprite ToSprite(Texture2D tex, int size)
    {
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }

    private static void FillCircle(Texture2D tex, float cx, float cy, float r, Color fill, Color border)
    {
        int size = tex.width;
        float rIn = r - 1.8f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                if (d <= rIn)
                    tex.SetPixel(x, y, fill);
                else if (d <= r)
                    tex.SetPixel(x, y, border);
            }
        }
    }

    private static void FillRect(Texture2D tex, float x0, float y0, float w, float h, Color fill, Color border)
    {
        int x1 = Mathf.FloorToInt(x0);
        int y1 = Mathf.FloorToInt(y0);
        int x2 = Mathf.CeilToInt(x0 + w);
        int y2 = Mathf.CeilToInt(y0 + h);
        for (int y = y1; y <= y2; y++)
        {
            for (int x = x1; x <= x2; x++)
            {
                if (x < 0 || y < 0 || x >= tex.width || y >= tex.height)
                    continue;
                bool edge = x == x1 || y == y1 || x == x2 || y == y2;
                tex.SetPixel(x, y, edge ? border : fill);
            }
        }
    }

    private static void FillTriangle(Texture2D tex, float cx, float tipY, float halfW, Color fill, Color border)
    {
        float baseY = tipY - halfW * 1.4f;
        int minX = Mathf.FloorToInt(cx - halfW);
        int maxX = Mathf.CeilToInt(cx + halfW);
        int minY = Mathf.FloorToInt(baseY);
        int maxY = Mathf.CeilToInt(tipY);
        for (int y = minY; y <= maxY; y++)
        {
            float t = Mathf.InverseLerp(baseY, tipY, y);
            float hw = halfW * (1f - t);
            for (int x = minX; x <= maxX; x++)
            {
                if (x < 0 || y < 0 || x >= tex.width || y >= tex.height)
                    continue;
                if (Mathf.Abs(x - cx) <= hw)
                {
                    bool edge = Mathf.Abs(x - cx) > hw - 1.2f || y == minY || y == maxY;
                    tex.SetPixel(x, y, edge ? border : fill);
                }
            }
        }
    }
}
