using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// 主菜单：热座 / AI 对战 / 联机（预留）/ 结束游戏；右上角设置（规则、全屏）。
/// </summary>
public class MainMenuUI : MonoBehaviour
{
    public static MainMenuUI Instance;

    private const string PrefFullscreen = "ElephantCrisis.Fullscreen";
    private const string PrefResW = "ElephantCrisis.ResW";
    private const string PrefResH = "ElephantCrisis.ResH";

    private static readonly Vector2Int[] WindowResolutions =
    {
        new Vector2Int(1280, 720),
        new Vector2Int(1600, 900),
        new Vector2Int(1920, 1080),
        new Vector2Int(2560, 1440)
    };

    private GameObject root;
    private GameObject rulesPanel;
    private GameObject settingsPanel;
    private GameObject roleSelectPanel;
    private GameObject mainButtonsRoot;
    private bool roleSelectForAdmin;
    private Text roleSelectTitle;
    private Text roleSelectTip;
    private Text displayModeLabel;
    private Text resolutionValueLabel;
    private Text tableStyleLabel;
    private readonly List<Button> resolutionPresetButtons = new List<Button>();
    private readonly List<Button> tableStyleButtons = new List<Button>();
    private int resolutionIndex = 2;
    private bool built;

    public bool IsVisible => root != null && root.activeSelf;

    private void Awake()
    {
        Instance = this;
        ApplySavedDisplaySettings();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public void Show()
    {
        // root 可能被误删（== null 对已 Destroy 的对象成立）
        if (!built || root == null)
        {
            built = false;
            Build();
        }
        if (root != null)
            root.SetActive(true);
        if (rulesPanel != null)
            rulesPanel.SetActive(false);
        if (settingsPanel != null)
            settingsPanel.SetActive(false);
        if (roleSelectPanel != null)
            roleSelectPanel.SetActive(false);
        if (mainButtonsRoot != null)
            mainButtonsRoot.SetActive(true);
        RefreshDisplayModeLabel();
        RefreshResolutionUI();
        RefreshTableStyleUI();
        SetupMenuCamera();
    }

    public void Hide()
    {
        if (root != null)
            root.SetActive(false);
    }

    private void SetupMenuCamera()
    {
        var cam = Camera.main;
        if (cam == null)
        {
            var camGo = new GameObject("Main Camera");
            cam = camGo.AddComponent<Camera>();
            cam.tag = "MainCamera";
            camGo.AddComponent<AudioListener>();
        }
        cam.orthographic = true;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = UiTheme.BgDeep;
        cam.transform.position = new Vector3(0f, 0f, -10f);
        cam.orthographicSize = 5f;
    }

    private void Build()
    {
        built = true;
        EnsureEventSystem();

        root = new GameObject("MainMenuCanvas");
        root.transform.SetParent(transform, false);
        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        root.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        root.GetComponent<CanvasScaler>().referenceResolution = new Vector2(1920, 1080);
        root.AddComponent<GraphicRaycaster>();

        var bg = CreateImage(root.transform, "Bg", Vector2.zero, Vector2.one, Color.white);
        bg.sprite = UiTheme.SoftBgSprite();
        bg.type = Image.Type.Simple;
        bg.preserveAspect = false;

        // 品牌：无框，靠留白与字号建立层级
        var title = CreateText(root.transform, "Title", new Vector2(0.1f, 0.74f), new Vector2(0.9f, 0.9f), 56,
            TextAnchor.MiddleCenter);
        title.text = "象群危机";
        title.color = UiTheme.TextTitle;
        title.fontStyle = FontStyle.Bold;
        var subtitle = CreateText(root.transform, "Sub", new Vector2(0.15f, 0.68f), new Vector2(0.85f, 0.76f),
            UiTheme.FontBody, TextAnchor.MiddleCenter);
        subtitle.text = "四人各自为战 · 回合制格子对决";
        subtitle.color = UiTheme.TextMuted;

        float settingsY = 0.97f;
        CreateMenuButton(root.transform, "设置", ref settingsY, 0.07f, UiTheme.ButtonTintMuted, () =>
        {
            if (settingsPanel != null)
            {
                settingsPanel.SetActive(true);
                RefreshDisplayModeLabel();
                RefreshResolutionUI();
                RefreshTableStyleUI();
            }
        }, new Vector2(0.86f, 0.9f), new Vector2(0.98f, 0.97f));

        mainButtonsRoot = new GameObject("MainButtons");
        mainButtonsRoot.transform.SetParent(root.transform, false);
        var mrt = mainButtonsRoot.AddComponent<RectTransform>();
        mrt.anchorMin = Vector2.zero;
        mrt.anchorMax = Vector2.one;
        mrt.offsetMin = Vector2.zero;
        mrt.offsetMax = Vector2.zero;

        // 单一菜单板：半透明 + 阴影，按钮统一主绿
        var menuPlate = UiTheme.CreateFramedPanel(mainButtonsRoot.transform, "MenuPlate",
            new Vector2(0.32f, 0.12f), new Vector2(0.68f, 0.64f));

        float y = 0.9f;
        float step = 0.15f;
        CreateMenuButton(menuPlate, "热座模式", ref y, step, UiTheme.ButtonTintMoss, () =>
        {
            GameBootstrap.Instance?.StartHotseat();
        }, wide: true);

        CreateMenuButton(menuPlate, "AI 对战", ref y, step, UiTheme.ButtonTintMoss, OnAiBattleClicked, wide: true);

        CreateMenuButton(menuPlate, "管理员模式", ref y, step, UiTheme.ButtonTintMuted, OnAdminModeClicked, wide: true);

        var onlineBtn = CreateMenuButton(menuPlate, "联机模式（即将推出）", ref y, step, UiTheme.ButtonTintMuted,
            OnOnlineClicked, wide: true);
        onlineBtn.interactable = false;

        CreateMenuButton(menuPlate, "结束游戏", ref y, step, UiTheme.ButtonTintDanger, QuitGame, wide: true);

        BuildRoleSelectPanel(root.transform);
        BuildSettingsPanel(root.transform);
        BuildRulesPanel(root.transform);
    }

    public void OnAiBattleClicked()
    {
        roleSelectForAdmin = false;
        OpenRoleSelect();
    }

    public void OnAdminModeClicked()
    {
        roleSelectForAdmin = true;
        OpenRoleSelect();
    }

    private void OpenRoleSelect()
    {
        if (mainButtonsRoot != null)
            mainButtonsRoot.SetActive(false);
        if (roleSelectTitle != null)
            roleSelectTitle.text = roleSelectForAdmin ? "管理员模式 · 选择角色" : "选择你的角色";
        if (roleSelectTip != null)
        {
            roleSelectTip.text = roleSelectForAdmin
                ? "基于 AI 对战 · 全屏视野 · 可查 AI 背包/技能 · 行动中可从牌库任选领取（不限次数）"
                : "四人混战 · 其余三人由 AI 操控";
        }
        if (roleSelectPanel != null)
            roleSelectPanel.SetActive(true);
    }

    /// <summary>预留：联机模式入口（当前禁用）。</summary>
    public void OnOnlineClicked()
    {
        Debug.Log("[预留] 联机模式尚未实现");
    }

    private static void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private static void ApplySavedDisplaySettings()
    {
        // 编辑器里强行改分辨率容易把 Game 视图搞乱，仅打包后生效
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

    private void BuildSettingsPanel(Transform parent)
    {
        var plate = UiTheme.CreateFramedPanel(parent, "SettingsPanel",
            new Vector2(0.22f, 0.12f), new Vector2(0.78f, 0.88f), solid: true);
        settingsPanel = plate.gameObject;

        var head = UiTheme.CreateHeaderBar(settingsPanel.transform, "Head",
            new Vector2(0.06f, 0.88f), new Vector2(0.94f, 0.97f));
        var title = CreateText(head, "SettingsTitle", Vector2.zero, Vector2.one, 28, TextAnchor.MiddleCenter);
        title.text = "设置";
        title.color = UiTheme.TextIvory;
        title.fontStyle = FontStyle.Bold;

        float y = 0.84f;
        float step = 0.11f;

        var displayBtn = CreateMenuButton(settingsPanel.transform, "显示模式", ref y, step,
            UiTheme.ButtonTintMoss, ToggleDisplayMode,
            new Vector2(0.1f, y - step + 0.015f), new Vector2(0.9f, y));
        y -= step;
        displayModeLabel = displayBtn.GetComponentInChildren<Text>();
        if (displayModeLabel != null)
            displayModeLabel.fontSize = 20;
        RefreshDisplayModeLabel();

        resolutionValueLabel = CreateText(settingsPanel.transform, "ResLabel",
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
            var btn = CreateMenuButton(settingsPanel.transform, label, ref dummyY, btnH,
                UiTheme.ButtonTintMuted, () => SelectResolution(index),
                new Vector2(xMin, y - btnH), new Vector2(xMax, y));
            var t = btn.GetComponentInChildren<Text>();
            if (t != null)
                t.fontSize = 15;
            resolutionPresetButtons.Add(btn);
        }
        y -= btnH + 0.04f;

        var tip = CreateText(settingsPanel.transform, "ResTip",
            new Vector2(0.1f, y - 0.04f), new Vector2(0.9f, y), 13, TextAnchor.MiddleCenter);
        tip.text = "打包后的窗口也可拖边框缩放；编辑器 Play 下分辨率切换可能无效";
        tip.color = UiTheme.TextMuted;
        y -= 0.06f;

        SyncResolutionIndexFromPrefs();
        RefreshResolutionUI();

        // 桌游桌面
        tableStyleLabel = CreateText(settingsPanel.transform, "TableLabel",
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
            var btn = CreateMenuButton(settingsPanel.transform, TableSurface.GetDisplayName(style), ref dummyY, tableBtnH,
                UiTheme.ButtonTintMuted, () => SelectTableStyle(style),
                new Vector2(xMin, y - tableBtnH), new Vector2(xMax, y));
            var t = btn.GetComponentInChildren<Text>();
            if (t != null)
                t.fontSize = 16;
            tableStyleButtons.Add(btn);
        }
        y -= tableBtnH + 0.03f;
        RefreshTableStyleUI();

        var tableTip = CreateText(settingsPanel.transform, "TableTip",
            new Vector2(0.1f, y - 0.04f), new Vector2(0.9f, y), 13, TextAnchor.MiddleCenter);
        tableTip.text = "对局中：右键拖移视角 · 按住 R 慢速逆时针旋转 · 滚轮推近 · 跟随当前单位";
        tableTip.color = UiTheme.TextMuted;
        y -= 0.06f;

        CreateMenuButton(settingsPanel.transform, "游戏规则", ref y, step,
            UiTheme.ButtonTintSky, () =>
            {
                settingsPanel.SetActive(false);
                if (rulesPanel != null)
                    rulesPanel.SetActive(true);
            },
            new Vector2(0.1f, y - step + 0.015f), new Vector2(0.9f, y));
        y -= step;

        CreateMenuButton(settingsPanel.transform, "关闭", ref y, step,
            UiTheme.ButtonTintClay, () =>
            {
                settingsPanel.SetActive(false);
            },
            new Vector2(0.1f, y - step + 0.015f), new Vector2(0.9f, y));

        settingsPanel.SetActive(false);
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

    private void BuildRoleSelectPanel(Transform parent)
    {
        var veil = new GameObject("RoleSelectPanel");
        veil.transform.SetParent(parent, false);
        var rt = veil.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        var veilImg = veil.AddComponent<Image>();
        veilImg.sprite = UiTheme.SoftBgSprite();
        veilImg.color = new Color(1f, 1f, 1f, 0.97f);
        veilImg.raycastTarget = true;
        roleSelectPanel = veil;

        var plate = UiTheme.CreateFramedPanel(veil.transform, "Plate",
            new Vector2(0.18f, 0.12f), new Vector2(0.82f, 0.88f), solid: true);
        var head = UiTheme.CreateHeaderBar(plate, "Head", new Vector2(0.06f, 0.88f), new Vector2(0.94f, 0.97f));
        var title = CreateText(head, "RoleTitle", Vector2.zero, Vector2.one, 30, TextAnchor.MiddleCenter);
        title.text = "选择你的角色";
        title.color = UiTheme.TextIvory;
        title.fontStyle = FontStyle.Bold;
        roleSelectTitle = title;

        var tip = CreateText(plate, "RoleTip", new Vector2(0.08f, 0.78f), new Vector2(0.92f, 0.87f), 16,
            TextAnchor.MiddleCenter);
        tip.text = "四人混战 · 其余三人由 AI 操控";
        tip.color = UiTheme.TextMuted;
        roleSelectTip = tip;

        var roles = new[]
        {
            RoleType.Elephant,
            RoleType.Human,
            RoleType.Monkey,
            RoleType.Cat
        };
        var colors = new[]
        {
            UiTheme.ButtonTintMuted,
            UiTheme.ButtonTintSky,
            UiTheme.ButtonTintClay,
            UiTheme.ButtonTintMoss
        };

        float y = 0.74f;
        float step = 0.12f;
        for (int i = 0; i < roles.Length; i++)
        {
            var role = roles[i];
            string label = RoleInfo.GetDisplayName(role);
            RoleInfo.GetBaseStats(role, out int move, out int hp, out int atk, out int def, out int bag);
            string text = $"{label}    移{move} 血{hp} 攻{atk} 防{def} 包{bag}";
            float bottom = y - step + 0.015f;
            CreateMenuButton(plate, text, ref y, step, colors[i], () =>
            {
                if (roleSelectForAdmin)
                    GameBootstrap.Instance?.StartAdminMode(role);
                else
                    GameBootstrap.Instance?.StartAiBattle(role);
            }, new Vector2(0.1f, bottom), new Vector2(0.9f, y));
            y -= step;
        }

        CreateMenuButton(plate, "返回", ref y, step, UiTheme.ButtonTintMuted, () =>
        {
            roleSelectPanel.SetActive(false);
            roleSelectForAdmin = false;
            if (mainButtonsRoot != null)
                mainButtonsRoot.SetActive(true);
        }, wide: true);

        roleSelectPanel.SetActive(false);
    }

    private void BuildRulesPanel(Transform parent)
    {
        var plate = UiTheme.CreateFramedPanel(parent, "RulesPanel",
            new Vector2(0.08f, 0.06f), new Vector2(0.92f, 0.94f), solid: true);
        rulesPanel = plate.gameObject;

        var head = UiTheme.CreateHeaderBar(rulesPanel.transform, "Head",
            new Vector2(0.03f, 0.9f), new Vector2(0.78f, 0.98f));
        var title = CreateText(head, "RulesTitle", new Vector2(0.04f, 0f), new Vector2(0.96f, 1f), 26,
            TextAnchor.MiddleLeft);
        title.text = "游戏介绍与规则";
        title.color = UiTheme.TextIvory;
        title.fontStyle = FontStyle.Bold;

        var closeY = 0.92f;
        CreateMenuButton(rulesPanel.transform, "关闭", ref closeY, 0.08f, UiTheme.ButtonTintClay, () =>
        {
            rulesPanel.SetActive(false);
            if (settingsPanel != null)
                settingsPanel.SetActive(true);
        }, new Vector2(0.82f, 0.9f), new Vector2(0.97f, 0.97f));

        // ScrollRect 根节点
        var scrollGo = new GameObject("Scroll");
        scrollGo.transform.SetParent(rulesPanel.transform, false);
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

        // Viewport：必须可接收射线，滚轮才生效
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
        var mask =         viewport.AddComponent<RectMask2D>();
        scroll.viewport = vrt;

        // Content
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

        // 按面板宽度计算文字高度，撑开 Content，否则滚不动
        Canvas.ForceUpdateCanvases();
        float contentWidth = Mathf.Max(400f, srt.rect.width > 1f ? srt.rect.width - 40f : 1400f);
        var settings = body.GetGenerationSettings(new Vector2(contentWidth, 0f));
        float textHeight = body.cachedTextGeneratorForLayout.GetPreferredHeight(body.text, settings) / body.pixelsPerUnit + 40f;
        textHeight = Mathf.Max(textHeight, 900f);
        brt.sizeDelta = new Vector2(-24f, textHeight);
        crt.sizeDelta = new Vector2(0f, textHeight + 24f);

        // 右侧滚动条（可选，滚轮仍可用）
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

        // 打开规则时再按实际宽度重算一次高度
        var watcher = rulesPanel.AddComponent<RulesScrollBootstrap>();
        watcher.Init(scroll, body, crt, brt);

        rulesPanel.SetActive(false);
    }

    private static void EnsureEventSystem()
    {
        if (FindFirstObjectByType<EventSystem>() != null)
            return;
        var es = new GameObject("EventSystem");
        es.AddComponent<EventSystem>();
        var t = System.Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
        if (t != null)
        {
            var mod = es.AddComponent(t);
            t.GetMethod("AssignDefaultActions")?.Invoke(mod, null);
        }
        else
            es.AddComponent<StandaloneInputModule>();
    }

    private static Image CreateImage(Transform parent, string name, Vector2 min, Vector2 max, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = min;
        rt.anchorMax = max;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        var img = go.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    private static Text CreateText(Transform parent, string name, Vector2 min, Vector2 max, int size, TextAnchor align)
        => UiTheme.CreateText(parent, name, min, max, size, align);

    private static Button CreateMenuButton(Transform parent, string label, ref float topY, float step, Color tint,
        UnityEngine.Events.UnityAction onClick, Vector2? anchorMin = null, Vector2? anchorMax = null,
        bool wide = false)
    {
        float bottom = topY - step + 0.012f;
        Vector2 min;
        Vector2 max;
        if (anchorMin.HasValue && anchorMax.HasValue)
        {
            min = anchorMin.Value;
            max = anchorMax.Value;
        }
        else if (wide)
        {
            min = new Vector2(0.08f, bottom);
            max = new Vector2(0.92f, topY);
            topY -= step;
        }
        else
        {
            min = new Vector2(0.28f, bottom);
            max = new Vector2(0.72f, topY);
            topY -= step;
        }

        return UiTheme.CreateButton(parent, label, min, max, onClick, tint, UiTheme.FontBody + 2);
    }
}

/// <summary>规则面板打开时按实际宽度重算内容高度，保证滚轮可滑到底。</summary>
public class RulesScrollBootstrap : MonoBehaviour
{
    private ScrollRect scroll;
    private Text body;
    private RectTransform content;
    private RectTransform bodyRt;
    private bool pending;

    public void Init(ScrollRect scrollRect, Text text, RectTransform contentRt, RectTransform bodyRect)
    {
        scroll = scrollRect;
        body = text;
        content = contentRt;
        bodyRt = bodyRect;
    }

    private void OnEnable()
    {
        pending = true;
    }

    private void LateUpdate()
    {
        if (!pending || body == null || content == null || bodyRt == null)
            return;
        pending = false;
        Recalc();
        if (scroll != null)
            scroll.verticalNormalizedPosition = 1f;
    }

    private void Recalc()
    {
        Canvas.ForceUpdateCanvases();
        float width = bodyRt.rect.width;
        if (width < 10f)
            width = content.rect.width - 24f;
        if (width < 10f)
            width = 1000f;

        var settings = body.GetGenerationSettings(new Vector2(width, 0f));
        float textHeight = body.cachedTextGeneratorForLayout.GetPreferredHeight(body.text, settings) / body.pixelsPerUnit + 48f;
        textHeight = Mathf.Max(textHeight, 600f);
        bodyRt.sizeDelta = new Vector2(bodyRt.sizeDelta.x, textHeight);
        content.sizeDelta = new Vector2(0f, textHeight + 32f);
    }
}

/// <summary>面向玩家的可读规则（与当前 Demo 能力对齐，略去未实装系统）。</summary>
public static class PlayerRulesText
{
    public const string Content =
@"【故事】
有一天我做了个梦：猴子、猫，还有一群大象。大象身上挂着玩偶——我抢走一只，大象就被激怒了，追着我跑。
然后我醒了。

【怎么赢】
四人各自为战（象、人、猴、猫）。四只玩偶各 1 张在共用牌库中，抽到后持有；界面以金色标识。
满足任一条件即获胜：
· 集齐四种玩偶（象/人/猴/猫各一）
· 成为场上唯一存活的人
若多人同时阵亡且无人集齐玩偶，则为平局。

【对局模式】
· 热座：四人同机轮流操作
· AI 对战：自选一个角色，其余三人由 AI 按相同规则行动

【怎么玩（热座）】
四位玩家轮流用同一台电脑操作。轮到谁，谁才能行动。
术语：一名玩家的抽牌/移动/攻击等合计为「行动」；四人各行动一次为「回合」。
每名玩家行动开始时会抽 1 张牌。
本行动你可以：
· 移动一次（蓝格为可走范围）
· 普通近战攻击一次（邻格左键）；弓/弩/炸弹等可多次使用（受弹药与手牌限制）
· 不限次数地弃置物品；用血瓶、强化剂等非攻击牌
· 拾取：点「拾取」，在半径 0～1 的掉落物里挑选（背包满了也能捡，但超重时无法结束行动）
右键可取消瞄准/拾取/强化选择。
鼠标悬停在角色上，底部会显示属性；悬停在蓝/红角标格子上，可查看掉落物或陷阱信息。
蓝角标 = 掉落物（含弃置的地雷）；红角标 = 武装地雷 / 你放置的定时炸弹或香蕉皮。

【伤害】
普通攻击与炸弹、弓箭等多为「物伤」：最终扣血 = max(0, 伤害 − 防御)。
若攻不破防（扣 0 血），不算「打中」对方；但装备中的木甲/铁甲仍会耗 1 次耐久。
熔岩站立造成「真伤」（护盾挡不住），不减防；可附加着火（着火为法伤）。

【濒死】
血量掉到 0 以下进入濒死：移动力变 1，其余属性不变，不清除其他状态；物品（含装备）掉一地。
不可拾取、不可用背包卡、不可放技能；仍可移动与近战。
若脚下有血瓶，行动中可直接使用自救；再受伤或数回合无人救治则会死亡。

【角色基础（当前版本，血攻防与背包已放大）】
· 象：移3 血100 攻9 防10 包15
· 人：移5 血90 攻10 防8 包24
· 猴：移6 血90 攻8 防6 包18
· 猫：移8 血60 攻6 防4 包15

【卡牌与装备（当前版本）】
共用一副限量牌库，用完的牌进弃牌堆，牌库空了会洗回去。
常见效果：
· 小血瓶：回 9 血；大血瓶：回 15 血；可救濒死
· 炸弹 / 高爆炸弹：投掷，15 / 24 点物伤（悬停可预览爆炸范围）
· 定时炸弹：安在脚下，选 1～5 回合后爆，半径 4，仅自己可见；猫行动结束后结算倒计时
· 香蕉皮：投掷到攻击距离内（仅你可见）；别人移动踩到会跌倒；弃置则可见且不触发
· 地雷：放置到攻击距离内（红角标，全员可见）；移动踩上受 15 法伤后销毁；弃置则变为掉落物可捡，踩到不伤人
· 毒箭 / 火箭：每张牌 1 支；命中附加中毒 / 着火
· 弓箭：每张牌 1 支，重 1，同种可合并；射出 1 支弃 1 张
· 匕首×3 / 长剑×2：装备后近战距1/+6 或 距2/+8（距2打不到隐匿）；普攻每行动至多1次；与弓弩同武器槽
· 弓 / 弩：须装备；弓耗1可直接射；弩耗1须先蓄力，蓄力与射击不可同一行动
· 火焰喷射器：装备后直线 5 格 10 法伤+着火1，冷却 3 回合
· 强化剂：攻/防永久 +3，或移动 +1
· 木甲 / 铁甲：装备后防 +4 / +8；同槽只能一件；受到物伤即耗耐久（含破不开防）
· 能量护盾：装备后可吸收物伤或法伤；与甲同属防具槽；共 3 次
· 夜视镜：挂件槽；黑夜时段基础能见度按 8
· 滑板 / 摩托车：载具槽；移+2 / +3；摩托可四向冲击 5–10 格（攻+9 物伤+晕眩，占移动，CD5）
· 抢夺勾爪：挂件；半径3抢一件非玩偶，用后损毁
· 闪光弹：射程5，爆点半径2致盲1回合
· 肾上腺素：仅 HP 低于 30% 可用；移+2 攻+3，持续 3 回合（不可叠加）
· 技能升级卡：牌库 12 张；集齐 3 张可使用，技能等级 +1（上限 3）
背包有容量；弹药一张一支占 1 点。行动中可以超重（抽牌/拾取仍可进行）；超重时无法结束行动，需先弃置到容量以内。弃置的东西留在地上，别人可以捡；格子被熔岩吞掉后，地上的牌会进弃牌堆。濒死时物品（含装备）掉落并解除装备。
· 左侧「技能」：象威慑为被动；人/猴/猫可主动发动（冷却按回合）。本命玩偶与升级卡可提升技能等级。
· 「领袖宣言」：每局限一次；持有任意三只玩偶且缺的一只在其他玩家身上时可发动：领袖状态 10 回合、抽 5 张、得知缺偶位置。
· 虚拟时钟：第1回合6:00，每完整回合+2小时。清晨视8 / 白天10 / 黄昏7 / 黑夜5；迷雾随能见度；不可移出视野；濒死/中毒视1。
· 天气：开局晴天（不改时段视野）；变更后5回合不变，第6–10回合内必再变；晴天必转雨/雾。雨：防-3视-1并灭火；雾：视-2。
· 夜视镜（挂件）：装备后仅在黑夜将基础能见度按8结算。
· 管理员模式（菜单）：基于 AI 对战；全屏视野；可查看 AI 背包/技能/状态；领卡；可自由切换天气（不自动变天）。

【地图压力】
地图 18×18；开局用种子生长铺出沙地/沼泽/冰地（淡蓝）/丛林/高地斑块，四角出生附近为普通地。
沙地移-1；沼泽移-2防-3；冰地移+1且行动开始20%跌倒；进丛林获隐匿；高地攻防+3射程+1。
每隔 5 个回合，最外圈会变成熔岩并不断向内收缩（覆盖原地形）。
站在熔岩上，你的行动开始时会先结算着火等状态，再受到 20 点真伤并可能再次着火——别站太久。

【操作速查】
· 左键：移动 / 近战攻击 / 确认落点
· 右键：取消当前选择
· 面板抽屉：战报/背包默认收起（边缘标签或 A/L/B）；顶栏「收起面板」；拾取等会自动展开背包
· 右下角圆形「结束行动」或 Enter：结束当前行动（超重时会提示背包超载并拒绝）
· 右侧背包：使用 / 弃置
· 左上角「重新开始 / 主菜单」：再开一局或回标题";
}
