using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 与主菜单一致的设置浮层：显示模式、窗口分辨率、桌面、游戏规则。
/// 主菜单与对局 UI 共用。
/// </summary>
public class GameSettingsOverlay
{
    public const string PrefFullscreen = "ElephantCrisis.Fullscreen";
    public const string PrefResW = "ElephantCrisis.ResW";
    public const string PrefResH = "ElephantCrisis.ResH";

    private static readonly Vector2Int[] WindowResolutions =
    {
        new Vector2Int(1280, 720),
        new Vector2Int(1600, 900),
        new Vector2Int(1920, 1080),
        new Vector2Int(2560, 1440)
    };

    public GameObject SettingsPanel { get; private set; }
    public GameObject RulesPanel { get; private set; }

    private Text displayModeLabel;
    private Text resolutionValueLabel;
    private Text tableStyleLabel;
    private readonly List<Button> resolutionPresetButtons = new List<Button>();
    private readonly List<Button> tableStyleButtons = new List<Button>();
    private int resolutionIndex = 2;

    public static GameSettingsOverlay Build(Transform parent)
    {
        var overlay = new GameSettingsOverlay();
        overlay.BuildSettingsPanel(parent);
        overlay.BuildRulesPanel(parent);
        return overlay;
    }

    public void Show()
    {
        if (RulesPanel != null)
            RulesPanel.SetActive(false);
        if (SettingsPanel != null)
        {
            SettingsPanel.SetActive(true);
            SettingsPanel.transform.SetAsLastSibling();
            RefreshDisplayModeLabel();
            RefreshResolutionUI();
            RefreshTableStyleUI();
        }
    }

    public void Hide()
    {
        if (SettingsPanel != null)
            SettingsPanel.SetActive(false);
        if (RulesPanel != null)
            RulesPanel.SetActive(false);
    }

    public static void ApplySavedDisplaySettings()
    {
#if UNITY_EDITOR
        return;
#else
        int w = PlayerPrefs.GetInt(PrefResW, 1920);
        int h = PlayerPrefs.GetInt(PrefResH, 1080);
        bool full = PlayerPrefs.HasKey(PrefFullscreen)
            ? PlayerPrefs.GetInt(PrefFullscreen, 0) == 1
            : false;
        Screen.SetResolution(w, h, full ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed);
#endif
    }

    private void ToggleDisplayMode()
    {
        bool nextFull = !IsFullscreen();
        var res = WindowResolutions[ClampResIndex(resolutionIndex)];
        ApplyResolution(res.x, res.y, nextFull);
        RefreshDisplayModeLabel();
        RefreshResolutionUI();
    }

    private void SelectResolution(int index)
    {
        if (IsFullscreen())
            return;
        resolutionIndex = ClampResIndex(index);
        var res = WindowResolutions[resolutionIndex];
        ApplyResolution(res.x, res.y, false);
        RefreshResolutionUI();
    }

    private static void ApplyResolution(int w, int h, bool fullscreen)
    {
        Screen.SetResolution(w, h, fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed);
        PlayerPrefs.SetInt(PrefResW, w);
        PlayerPrefs.SetInt(PrefResH, h);
        PlayerPrefs.SetInt(PrefFullscreen, fullscreen ? 1 : 0);
        PlayerPrefs.Save();
    }

    private static bool IsFullscreen()
    {
        return Screen.fullScreenMode == FullScreenMode.FullScreenWindow
            || Screen.fullScreenMode == FullScreenMode.ExclusiveFullScreen
            || Screen.fullScreen;
    }

    private static int ClampResIndex(int index)
    {
        if (index < 0) return 0;
        if (index >= WindowResolutions.Length) return WindowResolutions.Length - 1;
        return index;
    }

    private void SyncResolutionIndexFromPrefs()
    {
        int w = PlayerPrefs.GetInt(PrefResW, Screen.width);
        int h = PlayerPrefs.GetInt(PrefResH, Screen.height);
        resolutionIndex = 2;
        for (int i = 0; i < WindowResolutions.Length; i++)
        {
            if (WindowResolutions[i].x == w && WindowResolutions[i].y == h)
            {
                resolutionIndex = i;
                return;
            }
        }
    }

    private void RefreshDisplayModeLabel()
    {
        if (displayModeLabel == null)
            return;
        displayModeLabel.text = IsFullscreen()
            ? "显示模式：全屏（点击切窗口）"
            : "显示模式：窗口（点击切全屏）";
    }

    private void RefreshResolutionUI()
    {
        SyncResolutionIndexFromPrefs();
        bool windowed = !IsFullscreen();

        if (resolutionValueLabel != null)
        {
            if (!windowed)
                resolutionValueLabel.text = "窗口分辨率（请先切换到窗口模式）";
            else
            {
                var res = WindowResolutions[resolutionIndex];
                resolutionValueLabel.text = $"当前窗口：{res.x} × {res.y}";
            }
        }

        for (int i = 0; i < resolutionPresetButtons.Count; i++)
        {
            var btn = resolutionPresetButtons[i];
            if (btn == null) continue;
            btn.interactable = windowed;
            var img = btn.targetGraphic as Image;
            if (img != null)
            {
                bool selected = windowed && i == resolutionIndex;
                img.color = selected
                    ? UiTheme.ButtonTintMoss
                    : (windowed ? UiTheme.ButtonTintMuted : new Color(0.5f, 0.5f, 0.5f, 0.45f));
            }
        }
    }

    private void SelectTableStyle(TableStyle style)
    {
        TableSurface.Current = style;
        RefreshTableStyleUI();
    }

    private void RefreshTableStyleUI()
    {
        var cur = TableSurface.Current;
        if (tableStyleLabel != null)
            tableStyleLabel.text = $"桌面：{TableSurface.GetDisplayName(cur)}";

        var styles = new[] { TableStyle.Wood, TableStyle.Parchment };
        for (int i = 0; i < tableStyleButtons.Count && i < styles.Length; i++)
        {
            var btn = tableStyleButtons[i];
            if (btn == null) continue;
            var img = btn.targetGraphic as Image;
            if (img != null)
                img.color = styles[i] == cur ? UiTheme.ButtonTintMoss : UiTheme.ButtonTintMuted;
        }
    }

    private void BuildSettingsPanel(Transform parent)
    {
        var plate = UiTheme.CreateFramedPanel(parent, "SettingsPanel",
            new Vector2(0.22f, 0.12f), new Vector2(0.78f, 0.88f), solid: true);
        SettingsPanel = plate.gameObject;

        var head = UiTheme.CreateHeaderBar(SettingsPanel.transform, "Head",
            new Vector2(0.06f, 0.88f), new Vector2(0.94f, 0.97f));
        var title = UiTheme.CreateText(head, "SettingsTitle", Vector2.zero, Vector2.one, 28, TextAnchor.MiddleCenter);
        title.text = "设置";
        title.color = UiTheme.TextIvory;
        title.fontStyle = FontStyle.Bold;

        float y = 0.84f;
        float step = 0.11f;

        var displayBtn = CreateMenuButton(SettingsPanel.transform, "显示模式", ref y, step,
            UiTheme.ButtonTintMoss, ToggleDisplayMode,
            new Vector2(0.1f, y - step + 0.015f), new Vector2(0.9f, y));
        y -= step;
        displayModeLabel = displayBtn.GetComponentInChildren<Text>();
        if (displayModeLabel != null)
            displayModeLabel.fontSize = 20;
        RefreshDisplayModeLabel();

        resolutionValueLabel = UiTheme.CreateText(SettingsPanel.transform, "ResLabel",
            new Vector2(0.1f, y - 0.06f), new Vector2(0.9f, y), 17, TextAnchor.MiddleCenter);
        resolutionValueLabel.color = UiTheme.TextBody;
        y -= 0.08f;

        resolutionPresetButtons.Clear();
        float btnH = 0.09f;
        float gap = 0.02f;
        float totalW = 0.8f;
        float btnW = (totalW - gap * (WindowResolutions.Length - 1)) / WindowResolutions.Length;
        float x0 = 0.1f;
        for (int i = 0; i < WindowResolutions.Length; i++)
        {
            int index = i;
            var res = WindowResolutions[i];
            string label = $"{res.x}×{res.y}";
            float xMin = x0 + i * (btnW + gap);
            float xMax = xMin + btnW;
            float dummyY = y;
            var btn = CreateMenuButton(SettingsPanel.transform, label, ref dummyY, btnH,
                UiTheme.ButtonTintMuted, () => SelectResolution(index),
                new Vector2(xMin, y - btnH), new Vector2(xMax, y));
            var t = btn.GetComponentInChildren<Text>();
            if (t != null)
                t.fontSize = 15;
            resolutionPresetButtons.Add(btn);
        }
        y -= btnH + 0.04f;

        var tip = UiTheme.CreateText(SettingsPanel.transform, "ResTip",
            new Vector2(0.1f, y - 0.04f), new Vector2(0.9f, y), 13, TextAnchor.MiddleCenter);
        tip.text = "打包后的窗口也可拖边框缩放；编辑器 Play 下分辨率切换可能无效";
        tip.color = UiTheme.TextMuted;
        y -= 0.06f;

        SyncResolutionIndexFromPrefs();
        RefreshResolutionUI();

        tableStyleLabel = UiTheme.CreateText(SettingsPanel.transform, "TableLabel",
            new Vector2(0.1f, y - 0.04f), new Vector2(0.9f, y), 16, TextAnchor.MiddleCenter);
        tableStyleLabel.color = UiTheme.TextBody;
        y -= 0.05f;

        tableStyleButtons.Clear();
        float tableBtnH = 0.08f;
        float tableGap = 0.03f;
        float tableBtnW = (0.8f - tableGap) * 0.5f;
        var styles = new[] { TableStyle.Wood, TableStyle.Parchment };
        for (int i = 0; i < styles.Length; i++)
        {
            var style = styles[i];
            float xMin = 0.1f + i * (tableBtnW + tableGap);
            float xMax = xMin + tableBtnW;
            float dummyY = y;
            var btn = CreateMenuButton(SettingsPanel.transform, TableSurface.GetDisplayName(style), ref dummyY, tableBtnH,
                UiTheme.ButtonTintMuted, () => SelectTableStyle(style),
                new Vector2(xMin, y - tableBtnH), new Vector2(xMax, y));
            var t = btn.GetComponentInChildren<Text>();
            if (t != null)
                t.fontSize = 16;
            tableStyleButtons.Add(btn);
        }
        y -= tableBtnH + 0.03f;
        RefreshTableStyleUI();

        var tableTip = UiTheme.CreateText(SettingsPanel.transform, "TableTip",
            new Vector2(0.1f, y - 0.04f), new Vector2(0.9f, y), 13, TextAnchor.MiddleCenter);
        tableTip.text = "对局中：右键拖移 · R 逆时针 / Shift+R 顺时针 · 滚轮推近 · Shift+滚轮俯仰 · 开局玩家后方视角";
        tableTip.color = UiTheme.TextMuted;
        y -= 0.06f;

        CreateMenuButton(SettingsPanel.transform, "游戏规则", ref y, step,
            UiTheme.ButtonTintSky, () =>
            {
                SettingsPanel.SetActive(false);
                if (RulesPanel != null)
                {
                    RulesPanel.SetActive(true);
                    RulesPanel.transform.SetAsLastSibling();
                }
            },
            new Vector2(0.1f, y - step + 0.015f), new Vector2(0.9f, y));
        y -= step;

        CreateMenuButton(SettingsPanel.transform, "关闭", ref y, step,
            UiTheme.ButtonTintClay, Hide,
            new Vector2(0.1f, y - step + 0.015f), new Vector2(0.9f, y));

        SettingsPanel.SetActive(false);
    }

    private void BuildRulesPanel(Transform parent)
    {
        var plate = UiTheme.CreateFramedPanel(parent, "RulesPanel",
            new Vector2(0.08f, 0.06f), new Vector2(0.92f, 0.94f), solid: true);
        RulesPanel = plate.gameObject;

        var head = UiTheme.CreateHeaderBar(RulesPanel.transform, "Head",
            new Vector2(0.03f, 0.9f), new Vector2(0.78f, 0.98f));
        var title = UiTheme.CreateText(head, "RulesTitle", new Vector2(0.04f, 0f), new Vector2(0.96f, 1f), 26,
            TextAnchor.MiddleLeft);
        title.text = "游戏介绍与规则";
        title.color = UiTheme.TextIvory;
        title.fontStyle = FontStyle.Bold;

        var closeY = 0.92f;
        CreateMenuButton(RulesPanel.transform, "关闭", ref closeY, 0.08f, UiTheme.ButtonTintClay, () =>
        {
            RulesPanel.SetActive(false);
            if (SettingsPanel != null)
            {
                SettingsPanel.SetActive(true);
                SettingsPanel.transform.SetAsLastSibling();
            }
        }, new Vector2(0.82f, 0.9f), new Vector2(0.97f, 0.97f));

        var scrollGo = new GameObject("Scroll");
        scrollGo.transform.SetParent(RulesPanel.transform, false);
        var srt = scrollGo.AddComponent<RectTransform>();
        srt.anchorMin = new Vector2(0.04f, 0.04f);
        srt.anchorMax = new Vector2(0.96f, 0.88f);
        srt.offsetMin = Vector2.zero;
        srt.offsetMax = Vector2.zero;
        var scroll = scrollGo.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.inertia = true;
        scroll.decelerationRate = 0.135f;
        scroll.scrollSensitivity = 45f;

        var viewport = new GameObject("Viewport");
        viewport.transform.SetParent(scrollGo.transform, false);
        var vrt = viewport.AddComponent<RectTransform>();
        vrt.anchorMin = Vector2.zero;
        vrt.anchorMax = Vector2.one;
        vrt.pivot = new Vector2(0.5f, 0.5f);
        vrt.offsetMin = Vector2.zero;
        vrt.offsetMax = Vector2.zero;
        var vpImg = viewport.AddComponent<Image>();
        vpImg.color = new Color(1f, 1f, 1f, 0.01f);
        vpImg.raycastTarget = true;
        viewport.AddComponent<RectMask2D>();
        scroll.viewport = vrt;

        var content = new GameObject("Content");
        content.transform.SetParent(viewport.transform, false);
        var crt = content.AddComponent<RectTransform>();
        crt.anchorMin = new Vector2(0f, 1f);
        crt.anchorMax = new Vector2(1f, 1f);
        crt.pivot = new Vector2(0.5f, 1f);
        crt.anchoredPosition = Vector2.zero;
        crt.sizeDelta = new Vector2(0f, 800f);
        scroll.content = crt;

        var bodyGo = new GameObject("Body");
        bodyGo.transform.SetParent(content.transform, false);
        var brt = bodyGo.AddComponent<RectTransform>();
        brt.anchorMin = new Vector2(0f, 1f);
        brt.anchorMax = new Vector2(1f, 1f);
        brt.pivot = new Vector2(0.5f, 1f);
        brt.anchoredPosition = new Vector2(0f, -8f);
        brt.sizeDelta = new Vector2(-24f, 0f);

        var body = bodyGo.AddComponent<Text>();
        body.font = UiTheme.Font();
        body.fontSize = 20;
        body.color = UiTheme.TextBody;
        body.alignment = TextAnchor.UpperLeft;
        body.horizontalOverflow = HorizontalWrapMode.Wrap;
        body.verticalOverflow = VerticalWrapMode.Overflow;
        body.raycastTarget = false;
        body.supportRichText = true;
        body.text = PlayerRulesText.Content;

        Canvas.ForceUpdateCanvases();
        float contentWidth = Mathf.Max(400f, srt.rect.width > 1f ? srt.rect.width - 40f : 1400f);
        var genSettings = body.GetGenerationSettings(new Vector2(contentWidth, 0f));
        float textHeight = body.cachedTextGeneratorForLayout.GetPreferredHeight(body.text, genSettings) / body.pixelsPerUnit + 40f;
        textHeight = Mathf.Max(textHeight, 900f);
        brt.sizeDelta = new Vector2(-24f, textHeight);
        crt.sizeDelta = new Vector2(0f, textHeight + 24f);

        var barGo = new GameObject("Scrollbar");
        barGo.transform.SetParent(scrollGo.transform, false);
        var bart = barGo.AddComponent<RectTransform>();
        bart.anchorMin = new Vector2(1f, 0f);
        bart.anchorMax = new Vector2(1f, 1f);
        bart.pivot = new Vector2(1f, 1f);
        bart.sizeDelta = new Vector2(14f, 0f);
        bart.anchoredPosition = Vector2.zero;
        var barBg = barGo.AddComponent<Image>();
        barBg.color = new Color(0.15f, 0.15f, 0.15f, 0.6f);
        var scrollbar = barGo.AddComponent<Scrollbar>();
        scrollbar.direction = Scrollbar.Direction.BottomToTop;

        var handleArea = new GameObject("Sliding Area");
        handleArea.transform.SetParent(barGo.transform, false);
        var hart = handleArea.AddComponent<RectTransform>();
        hart.anchorMin = Vector2.zero;
        hart.anchorMax = Vector2.one;
        hart.offsetMin = new Vector2(2f, 2f);
        hart.offsetMax = new Vector2(-2f, -2f);

        var handle = new GameObject("Handle");
        handle.transform.SetParent(handleArea.transform, false);
        var hrt = handle.AddComponent<RectTransform>();
        hrt.anchorMin = Vector2.zero;
        hrt.anchorMax = Vector2.one;
        hrt.offsetMin = Vector2.zero;
        hrt.offsetMax = Vector2.zero;
        var himg = handle.AddComponent<Image>();
        himg.color = UiTheme.AccentMoss;
        scrollbar.targetGraphic = himg;
        scrollbar.handleRect = hrt;
        scroll.verticalScrollbar = scrollbar;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

        var watcher = RulesPanel.AddComponent<RulesScrollBootstrap>();
        watcher.Init(scroll, body, crt, brt);

        RulesPanel.SetActive(false);
    }

    private static Button CreateMenuButton(Transform parent, string label, ref float topY, float step, Color tint,
        UnityEngine.Events.UnityAction onClick, Vector2? anchorMin = null, Vector2? anchorMax = null)
    {
        float bottom = topY - step + 0.012f;
        Vector2 min = anchorMin ?? new Vector2(0.28f, bottom);
        Vector2 max = anchorMax ?? new Vector2(0.72f, topY);
        if (!anchorMin.HasValue)
            topY -= step;
        return UiTheme.CreateButton(parent, label, min, max, onClick, tint, UiTheme.FontBody + 2);
    }
}
