using UnityEngine;

public enum TableStyle
{
    Wood = 0,
    Parchment = 1
}

/// <summary>桌游桌面材质：优先加载 Resources/Table 贴图，设置中切换。</summary>
public static class TableSurface
{
    public const string PrefKey = "ElephantCrisis.TableStyle";

    private static Sprite cachedWood;
    private static Sprite cachedParchment;
    private static Sprite cachedFelt;
    private static Sprite cachedRim;

    public static TableStyle Current
    {
        get
        {
            int v = PlayerPrefs.GetInt(PrefKey, (int)TableStyle.Wood);
            if (v != (int)TableStyle.Wood && v != (int)TableStyle.Parchment)
                return TableStyle.Wood;
            return (TableStyle)v;
        }
        set
        {
            PlayerPrefs.SetInt(PrefKey, (int)value);
            PlayerPrefs.Save();
            // 对局已改为户外氛围天空，桌面样式仅保留设置项偏好
            MapVisual.Instance?.ApplyTableStyle(value);
            AtmosphereVisual.Instance?.Refresh();
            BoardCameraController.Instance?.ApplyOutdoorClearColor();
        }
    }

    public static string GetDisplayName(TableStyle style)
        => style == TableStyle.Parchment ? "羊皮纸桌面" : "木纹桌面";

    public static Color GetClearColor(TableStyle style)
    {
        return style == TableStyle.Parchment
            ? new Color(0.84f, 0.76f, 0.58f)
            : new Color(0.32f, 0.2f, 0.11f);
    }

    public static Sprite CreateTableSprite(TableStyle style, int size = 256)
    {
        if (style == TableStyle.Parchment)
        {
            if (cachedParchment == null)
                cachedParchment = LoadOrBake("Table/parchment", CreateParchmentSprite, size);
            return cachedParchment;
        }

        if (cachedWood == null)
            cachedWood = LoadOrBake("Table/wood", CreateWoodSprite, size);
        return cachedWood;
    }

    public static Sprite CreateBoardFeltSprite(int size = 128)
    {
        if (cachedFelt == null)
            cachedFelt = CreateFeltSprite(size);
        return cachedFelt;
    }

    public static Sprite CreateBoardRimSprite(int size = 128)
    {
        if (cachedRim == null)
            cachedRim = CreateRimSprite(size);
        return cachedRim;
    }

    /// <summary>一张贴图约覆盖的世界单位；越小越清晰、重复越密。</summary>
    public const float TileWorldSize = 10f;

    public static Texture2D GetTableTexture(TableStyle style)
    {
        var sprite = CreateTableSprite(style);
        var tex = sprite != null ? sprite.texture : null;
        if (tex != null)
        {
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.filterMode = FilterMode.Trilinear;
            tex.anisoLevel = 8;
        }
        return tex;
    }

    public static Material CreateTableMaterial(TableStyle style, float worldSize)
    {
        var mat = new Material(FindTableShader()) { name = "TableSurface" };
        ApplyTableMaterial(mat, style, worldSize);
        return mat;
    }

    public static void ApplyTableMaterial(Material mat, TableStyle style, float worldSize)
    {
        if (mat == null)
            return;
        var tex = GetTableTexture(style);
        mat.mainTexture = tex;
        float tiles = Mathf.Max(1f, worldSize / TileWorldSize);
        mat.mainTextureScale = new Vector2(tiles, tiles);
        if (mat.HasProperty("_BaseMap"))
        {
            mat.SetTexture("_BaseMap", tex);
            mat.SetTextureScale("_BaseMap", new Vector2(tiles, tiles));
        }
        if (mat.HasProperty("_Color"))
            mat.SetColor("_Color", Color.white);
        if (mat.HasProperty("_BaseColor"))
            mat.SetColor("_BaseColor", Color.white);
    }

    private static Shader FindTableShader()
    {
        return Shader.Find("Unlit/Texture")
            ?? Shader.Find("Universal Render Pipeline/Unlit")
            ?? Shader.Find("Sprites/Default")
            ?? Shader.Find("Standard");
    }

    private static Sprite LoadOrBake(string resourcePath, System.Func<int, Sprite> bake, int bakeSize)
    {
        var sprite = ClientPerfResourceProbe.Load<Sprite>(resourcePath);
        if (sprite != null)
            return sprite;

        var tex = ClientPerfResourceProbe.Load<Texture2D>(resourcePath);
        if (tex != null)
        {
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.filterMode = FilterMode.Trilinear;
            // 约 8 世界单位铺一张 1024 贴图，平铺时更清晰
            const float pixelsPerUnit = 128f;
            return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), pixelsPerUnit);
        }

        return bake(bakeSize);
    }

    private static Sprite CreateFeltSprite(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float n = Mathf.PerlinNoise(x * 0.08f, y * 0.08f);
                float n2 = Mathf.PerlinNoise(x * 0.22f + 5f, y * 0.22f);
                var deep = new Color(0.08f, 0.18f, 0.12f);
                var mid = new Color(0.14f, 0.28f, 0.18f);
                var nap = new Color(0.18f, 0.34f, 0.22f);
                Color c = Color.Lerp(deep, mid, n);
                c = Color.Lerp(c, nap, n2 * 0.25f);
                tex.SetPixel(x, y, c);
            }
        }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }

    private static Sprite CreateRimSprite(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        int border = Mathf.Max(6, size / 9);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int dx = Mathf.Min(x, size - 1 - x);
                int dy = Mathf.Min(y, size - 1 - y);
                int d = Mathf.Min(dx, dy);
                if (d >= border)
                {
                    tex.SetPixel(x, y, new Color(0f, 0f, 0f, 0f));
                    continue;
                }
                float t = d / (float)border;
                float grain = Mathf.PerlinNoise(x * 0.15f, y * 0.04f);
                var dark = new Color(0.22f, 0.12f, 0.06f);
                var light = new Color(0.48f, 0.3f, 0.14f);
                var c = Color.Lerp(dark, light, t * 0.55f + grain * 0.35f);
                c.a = 1f;
                tex.SetPixel(x, y, c);
            }
        }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }

    private static Sprite CreateWoodSprite(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float u = x / (float)size;
                float v = y / (float)size;
                float wave = Mathf.Sin(v * 18f + Mathf.PerlinNoise(u * 2f, v * 0.5f) * 4f) * 0.015f;
                float grainU = u + wave;
                float grain = Mathf.PerlinNoise(grainU * 28f, v * 3.5f);
                float fine = Mathf.PerlinNoise(grainU * 70f, v * 9f);
                float pore = Mathf.PerlinNoise(grainU * 40f + 9f, v * 12f);
                var dark = new Color(0.22f, 0.11f, 0.05f);
                var mid = new Color(0.42f, 0.25f, 0.11f);
                var light = new Color(0.58f, 0.38f, 0.18f);
                Color c = Color.Lerp(dark, mid, grain);
                c = Color.Lerp(c, light, fine * 0.35f);
                if (pore > 0.78f)
                    c = Color.Lerp(c, dark, 0.45f);
                tex.SetPixel(x, y, c);
            }
        }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }

    private static Sprite CreateParchmentSprite(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float n = Mathf.PerlinNoise(x * 0.05f, y * 0.05f);
                float fiberH = Mathf.PerlinNoise(x * 0.35f, y * 0.08f);
                float fiberV = Mathf.PerlinNoise(x * 0.08f + 3f, y * 0.32f);
                float weave = Mathf.PerlinNoise(x * 0.18f + 1f, y * 0.18f);
                var baseCol = new Color(0.91f, 0.85f, 0.71f);
                var fiberCol = new Color(0.78f, 0.68f, 0.48f);
                float grain = n * 0.35f + fiberH * 0.35f + fiberV * 0.2f + weave * 0.15f;
                Color c = Color.Lerp(baseCol, fiberCol, grain);
                tex.SetPixel(x, y, c);
            }
        }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }
}
