using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;

/// <summary>
/// 现代独立策略风 UI：少边框、半透明面板、柔和阴影、单一强调绿。
/// 间距刻度：4 / 8 / 16 / 24 / 32。
/// </summary>
public static class UiTheme
{
    // —— 色板（刻意收敛）——
    public static readonly Color BgDeep = Hex(0x101514);
    public static readonly Color Panel = new Color(0x18 / 255f, 0x23 / 255f, 0x1E / 255f, 0.88f);
    public static readonly Color PanelSolid = Hex(0x18231E);
    public static readonly Color Button = Hex(0x2C7045);
    public static readonly Color ButtonHover = Hex(0x3A8A56);
    public static readonly Color ButtonPressed = Hex(0x215534);
    public static readonly Color Highlight = Hex(0x84C95A);
    public static readonly Color TextBody = Hex(0xEAE7D5);
    public static readonly Color TextMuted = new Color(0xEA / 255f, 0xE7 / 255f, 0xD5 / 255f, 0.55f);
    public static readonly Color TextTitle = Hex(0xEAE7D5);
    public static readonly Color Danger = Hex(0xC45A4A);
    public static readonly Color AccentGold = Hex(0xC9A84A); // 仅强调用，禁止铺满边框
    public static readonly Color Shadow = new Color(0f, 0f, 0f, 0.45f);
    public static readonly Color Hairline = new Color(1f, 1f, 1f, 0.08f);
    public static readonly Color Row = new Color(1f, 1f, 1f, 0.04f);
    public static readonly Color RowEquipped = new Color(0x84 / 255f, 0xC9 / 255f, 0x5A / 255f, 0.14f);
    public static readonly Color Divider = new Color(1f, 1f, 1f, 0.1f);

    // 兼容旧命名
    public static readonly Color BgWash = Panel;
    public static readonly Color PanelFill = Panel;
    public static readonly Color PanelFillSolid = PanelSolid;
    public static readonly Color HeaderFill = new Color(1f, 1f, 1f, 0.04f);
    public static readonly Color Border = Hairline;
    public static readonly Color BorderLight = Hairline;
    public static readonly Color AccentMoss = Button;
    public static readonly Color AccentMossBright = Highlight;
    public static readonly Color AccentClay = AccentGold;
    public static readonly Color AccentDanger = Danger;
    public static readonly Color AccentMuted = new Color(0.25f, 0.28f, 0.26f, 0.9f);
    public static readonly Color TextIvory = TextTitle;
    public static readonly Color TextOnButton = TextBody;

    public const int FontTitle = 24;
    public const int FontBody = 18;
    public const int FontSmall = 15;
    public const int FontTiny = 13;

    public const float Pad = 16f;
    public const float Gap = 8f;
    public const float GapLg = 16f;

    // 按钮色 tint（乘在绿色底上）
    public static Color ButtonTintMoss => Color.white;
    public static Color ButtonTintClay => new Color(1f, 0.92f, 0.75f, 1f);
    public static Color ButtonTintDanger => new Color(1f, 0.7f, 0.65f, 1f);
    public static Color ButtonTintMuted => new Color(0.65f, 0.68f, 0.65f, 1f);
    public static Color ButtonTintAdmin => new Color(0.9f, 0.8f, 1f, 1f);
    public static Color ButtonTintSky => new Color(0.75f, 0.9f, 1f, 1f);

    private static Sprite cachedRoundSprite;
    private static Sprite cachedSoftBgSprite;
    private static Font cachedFont;

    public static Font Font()
    {
        if (cachedFont != null)
            return cachedFont;
        cachedFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (cachedFont == null)
            cachedFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
        return cachedFont;
    }

    public static Sprite RoundSprite()
    {
        if (cachedRoundSprite == null)
            cachedRoundSprite = CreateRoundedSprite(64, 10);
        return cachedRoundSprite;
    }

    public static Sprite PanelSprite() => RoundSprite();
    public static Sprite ButtonSprite() => RoundSprite();
    public static Sprite HeaderSprite() => RoundSprite();

    public static Sprite SoftBgSprite()
    {
        if (cachedSoftBgSprite == null)
            cachedSoftBgSprite = CreateVignetteSprite(128);
        return cachedSoftBgSprite;
    }

    /// <summary>半透明圆角面板 + 柔和阴影，无金框。</summary>
    public static RectTransform CreateFramedPanel(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax,
        bool solid = false)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        // 阴影（略偏移）
        var shadowGo = new GameObject("Shadow");
        shadowGo.transform.SetParent(go.transform, false);
        var srt = shadowGo.AddComponent<RectTransform>();
        srt.anchorMin = Vector2.zero;
        srt.anchorMax = Vector2.one;
        srt.offsetMin = new Vector2(2f, -4f);
        srt.offsetMax = new Vector2(4f, -2f);
        var simg = shadowGo.AddComponent<Image>();
        simg.sprite = RoundSprite();
        simg.type = Image.Type.Sliced;
        simg.color = Shadow;
        simg.raycastTarget = false;

        var img = go.AddComponent<Image>();
        img.sprite = RoundSprite();
        img.type = Image.Type.Sliced;
        img.color = solid ? PanelSolid : Panel;
        img.raycastTarget = true;

        return rt;
    }

    /// <summary>标题区：无框，仅文字 + 底部分隔线。</summary>
    public static RectTransform CreateHeaderBar(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var line = new GameObject("Rule");
        line.transform.SetParent(go.transform, false);
        var lrt = line.AddComponent<RectTransform>();
        lrt.anchorMin = new Vector2(0f, 0f);
        lrt.anchorMax = new Vector2(1f, 0f);
        lrt.pivot = new Vector2(0.5f, 0f);
        lrt.sizeDelta = new Vector2(0f, 1f);
        lrt.anchoredPosition = Vector2.zero;
        var limg = line.AddComponent<Image>();
        limg.color = Divider;
        limg.raycastTarget = false;
        return rt;
    }

    public static Text CreateText(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax,
        int fontSize, TextAnchor align, Color? color = null, bool bold = false)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        var text = go.AddComponent<Text>();
        text.font = Font();
        text.fontSize = fontSize;
        text.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
        text.color = color ?? TextBody;
        text.alignment = align;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        text.supportRichText = true;
        return text;
    }

    public static Button CreateButton(Transform parent, string label, Vector2 anchorMin, Vector2 anchorMax,
        UnityAction onClick, Color? tint = null, int fontSize = FontBody)
    {
        var go = new GameObject(label);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var img = go.AddComponent<Image>();
        img.sprite = RoundSprite();
        img.type = Image.Type.Sliced;
        var baseTint = tint ?? Color.white;
        img.color = Multiply(Button, baseTint);

        // ColorTint：Image 用白色，状态色写在 ColorBlock 里
        img.color = Color.white;
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        var colors = btn.colors;
        colors.normalColor = Multiply(Button, baseTint);
        colors.highlightedColor = Multiply(ButtonHover, baseTint);
        colors.pressedColor = Multiply(ButtonPressed, baseTint);
        colors.selectedColor = Multiply(ButtonHover, baseTint);
        colors.disabledColor = new Color(0.28f, 0.3f, 0.28f, 0.55f);
        colors.fadeDuration = 0.08f;
        btn.colors = colors;
        btn.onClick.AddListener(onClick);

        var text = CreateText(go.transform, "Text", Vector2.zero, Vector2.one, fontSize, TextAnchor.MiddleCenter,
            TextOnButton, bold: true);
        // 左右内边距感：字略缩
        var trt = text.GetComponent<RectTransform>();
        trt.offsetMin = new Vector2(8f, 2f);
        trt.offsetMax = new Vector2(-8f, -2f);
        text.text = label;
        return btn;
    }

    public static Button CreateStackedButton(Transform parent, string label, ref float topY, float step,
        UnityAction onClick, Color? tint = null, int fontSize = FontBody)
    {
        // 统一按钮间隙：step 含按钮高 + Gap
        float gap = 0.02f;
        float bottom = topY - step + gap;
        var btn = CreateButton(parent, label,
            new Vector2(0.1f, bottom), new Vector2(0.9f, topY),
            onClick, tint, fontSize);
        topY -= step;
        return btn;
    }

    public static void StyleExistingImageAsPanel(Image img)
    {
        if (img == null) return;
        img.sprite = RoundSprite();
        img.type = Image.Type.Sliced;
        img.color = Panel;
    }

    private static Color Hex(int rgb)
    {
        return new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);
    }

    private static Color Multiply(Color a, Color b)
        => new Color(a.r * b.r, a.g * b.g, a.b * b.b, a.a * b.a);

    private static Sprite CreateRoundedSprite(int size, int radius)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        float r = radius;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float ax = Mathf.Min(x, size - 1 - x);
                float ay = Mathf.Min(y, size - 1 - y);
                float alpha = 1f;
                if (ax < r && ay < r)
                {
                    float dx = r - ax;
                    float dy = r - ay;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    alpha = Mathf.Clamp01(r - d + 0.5f);
                }
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }
        tex.Apply();
        int border = radius + 1;
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size,
            0, SpriteMeshType.FullRect, new Vector4(border, border, border, border));
    }

    private static Sprite CreateVignetteSprite(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        float cx = (size - 1) * 0.5f;
        float cy = (size - 1) * 0.5f;
        var deep = Hex(0x101514);
        var mid = Hex(0x18231E);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float nx = (x - cx) / cx;
                float ny = (y - cy) / cy;
                float d = Mathf.Sqrt(nx * nx * 0.9f + ny * ny);
                tex.SetPixel(x, y, Color.Lerp(mid, deep, Mathf.Clamp01(d * 0.85f)));
            }
        }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }
}
