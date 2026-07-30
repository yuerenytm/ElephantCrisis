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
    private Text displayModeLabel;
    private Text resolutionValueLabel;
    private readonly List<Button> resolutionPresetButtons = new List<Button>();
    private int resolutionIndex = 2;
    private bool built;

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
        cam.backgroundColor = new Color(0.07f, 0.09f, 0.08f);
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

        CreateImage(root.transform, "Bg", Vector2.zero, Vector2.one, new Color(0.06f, 0.1f, 0.08f, 0.92f));

        var title = CreateText(root.transform, "Title", new Vector2(0.1f, 0.72f), new Vector2(0.9f, 0.9f), 56, TextAnchor.MiddleCenter);
        title.text = "象群危机";
        title.color = new Color(0.92f, 0.88f, 0.7f);

        var subtitle = CreateText(root.transform, "Sub", new Vector2(0.15f, 0.64f), new Vector2(0.85f, 0.74f), 22, TextAnchor.MiddleCenter);
        subtitle.text = "四人各自为战 · 回合制格子对决";
        subtitle.color = new Color(0.7f, 0.75f, 0.68f);

        // 右上角设置
        float settingsY = 0.97f;
        CreateMenuButton(root.transform, "设置", ref settingsY, 0.07f, new Color(0.28f, 0.36f, 0.4f, 0.95f), () =>
        {
            if (settingsPanel != null)
            {
                settingsPanel.SetActive(true);
                RefreshDisplayModeLabel();
                RefreshResolutionUI();
            }
        }, new Vector2(0.86f, 0.9f), new Vector2(0.98f, 0.97f));

        mainButtonsRoot = new GameObject("MainButtons");
        mainButtonsRoot.transform.SetParent(root.transform, false);
        var mrt = mainButtonsRoot.AddComponent<RectTransform>();
        mrt.anchorMin = Vector2.zero;
        mrt.anchorMax = Vector2.one;
        mrt.offsetMin = Vector2.zero;
        mrt.offsetMax = Vector2.zero;

        float y = 0.56f;
        float step = 0.1f;
        CreateMenuButton(mainButtonsRoot.transform, "热座模式", ref y, step, new Color(0.22f, 0.48f, 0.36f, 0.95f), () =>
        {
            GameBootstrap.Instance?.StartHotseat();
        });

        CreateMenuButton(mainButtonsRoot.transform, "AI 对战", ref y, step, new Color(0.4f, 0.32f, 0.18f, 0.95f), OnAiBattleClicked);

        var onlineBtn = CreateMenuButton(mainButtonsRoot.transform, "联机模式（即将推出）", ref y, step, new Color(0.28f, 0.28f, 0.28f, 0.7f), OnOnlineClicked);
        onlineBtn.interactable = false;

        CreateMenuButton(mainButtonsRoot.transform, "结束游戏", ref y, step, new Color(0.45f, 0.22f, 0.2f, 0.95f), QuitGame);

        BuildRoleSelectPanel(root.transform);
        BuildSettingsPanel(root.transform);
        BuildRulesPanel(root.transform);
    }

    public void OnAiBattleClicked()
    {
        if (mainButtonsRoot != null)
            mainButtonsRoot.SetActive(false);
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
                    ? new Color(0.35f, 0.55f, 0.4f, 0.95f)
                    : new Color(0.22f, 0.28f, 0.32f, windowed ? 0.95f : 0.45f);
            }
        }
    }

    private void BuildSettingsPanel(Transform parent)
    {
        settingsPanel = new GameObject("SettingsPanel");
        settingsPanel.transform.SetParent(parent, false);
        var rt = settingsPanel.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.22f, 0.12f);
        rt.anchorMax = new Vector2(0.78f, 0.88f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        var img = settingsPanel.AddComponent<Image>();
        img.color = new Color(0.06f, 0.09f, 0.08f, 0.97f);
        img.raycastTarget = true;

        var title = CreateText(settingsPanel.transform, "SettingsTitle", new Vector2(0.08f, 0.88f), new Vector2(0.92f, 0.97f), 30, TextAnchor.MiddleCenter);
        title.text = "设置";
        title.color = new Color(0.92f, 0.88f, 0.7f);

        float y = 0.82f;
        float step = 0.11f;

        var displayBtn = CreateMenuButton(settingsPanel.transform, "显示模式", ref y, step,
            new Color(0.28f, 0.4f, 0.38f, 0.95f), ToggleDisplayMode,
            new Vector2(0.1f, y - step + 0.015f), new Vector2(0.9f, y));
        y -= step;
        displayModeLabel = displayBtn.GetComponentInChildren<Text>();
        if (displayModeLabel != null)
            displayModeLabel.fontSize = 22;
        RefreshDisplayModeLabel();

        resolutionValueLabel = CreateText(settingsPanel.transform, "ResLabel",
            new Vector2(0.1f, y - 0.06f), new Vector2(0.9f, y), 18, TextAnchor.MiddleCenter);
        resolutionValueLabel.color = new Color(0.85f, 0.88f, 0.82f);
        y -= 0.08f;

        // 四个分辨率预设横排
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
                new Color(0.22f, 0.28f, 0.32f, 0.95f), () => SelectResolution(index),
                new Vector2(xMin, y - btnH), new Vector2(xMax, y));
            var t = btn.GetComponentInChildren<Text>();
            if (t != null)
                t.fontSize = 16;
            resolutionPresetButtons.Add(btn);
        }
        y -= btnH + 0.04f;

        var tip = CreateText(settingsPanel.transform, "ResTip",
            new Vector2(0.1f, y - 0.05f), new Vector2(0.9f, y), 14, TextAnchor.MiddleCenter);
        tip.text = "打包后的窗口也可拖边框缩放；编辑器 Play 下分辨率切换可能无效";
        tip.color = new Color(0.65f, 0.7f, 0.66f);
        y -= 0.08f;

        SyncResolutionIndexFromPrefs();
        RefreshResolutionUI();

        CreateMenuButton(settingsPanel.transform, "游戏规则", ref y, step,
            new Color(0.28f, 0.4f, 0.48f, 0.95f), () =>
            {
                settingsPanel.SetActive(false);
                if (rulesPanel != null)
                    rulesPanel.SetActive(true);
            },
            new Vector2(0.1f, y - step + 0.015f), new Vector2(0.9f, y));
        y -= step;

        CreateMenuButton(settingsPanel.transform, "关闭", ref y, step,
            new Color(0.35f, 0.28f, 0.25f, 0.95f), () =>
            {
                settingsPanel.SetActive(false);
            },
            new Vector2(0.1f, y - step + 0.015f), new Vector2(0.9f, y));

        settingsPanel.SetActive(false);
    }

    private void BuildRoleSelectPanel(Transform parent)
    {
        roleSelectPanel = new GameObject("RoleSelectPanel");
        roleSelectPanel.transform.SetParent(parent, false);
        var rt = roleSelectPanel.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        roleSelectPanel.AddComponent<Image>().color = new Color(0.05f, 0.08f, 0.07f, 0.96f);

        var title = CreateText(roleSelectPanel.transform, "RoleTitle", new Vector2(0.1f, 0.78f), new Vector2(0.9f, 0.92f), 36, TextAnchor.MiddleCenter);
        title.text = "选择你的角色";
        title.color = new Color(0.92f, 0.88f, 0.7f);

        var tip = CreateText(roleSelectPanel.transform, "RoleTip", new Vector2(0.15f, 0.7f), new Vector2(0.85f, 0.78f), 18, TextAnchor.MiddleCenter);
        tip.text = "四人混战 · 其余三人由 AI 操控";
        tip.color = new Color(0.7f, 0.75f, 0.68f);

        var roles = new[]
        {
            RoleType.Elephant,
            RoleType.Human,
            RoleType.Monkey,
            RoleType.Cat
        };
        var colors = new[]
        {
            new Color(0.45f, 0.45f, 0.48f, 0.95f),
            new Color(0.28f, 0.42f, 0.7f, 0.95f),
            new Color(0.65f, 0.4f, 0.2f, 0.95f),
            new Color(0.7f, 0.55f, 0.18f, 0.95f)
        };

        float y = 0.62f;
        float step = 0.1f;
        for (int i = 0; i < roles.Length; i++)
        {
            var role = roles[i];
            string label = RoleInfo.GetDisplayName(role);
            RoleInfo.GetBaseStats(role, out int move, out int hp, out int atk, out int def, out int bag);
            string text = $"{label}    移{move} 血{hp} 攻{atk} 防{def} 包{bag}";
            float bottom = y - step + 0.015f;
            CreateMenuButton(roleSelectPanel.transform, text, ref y, step, colors[i], () =>
            {
                GameBootstrap.Instance?.StartAiBattle(role);
            }, new Vector2(0.18f, bottom), new Vector2(0.82f, y));
            y -= step;
        }

        CreateMenuButton(roleSelectPanel.transform, "返回", ref y, step, new Color(0.3f, 0.3f, 0.3f, 0.9f), () =>
        {
            roleSelectPanel.SetActive(false);
            if (mainButtonsRoot != null)
                mainButtonsRoot.SetActive(true);
        });

        roleSelectPanel.SetActive(false);
    }

    private void BuildRulesPanel(Transform parent)
    {
        rulesPanel = new GameObject("RulesPanel");
        rulesPanel.transform.SetParent(parent, false);
        var rt = rulesPanel.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.08f, 0.06f);
        rt.anchorMax = new Vector2(0.92f, 0.94f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        var panelImg = rulesPanel.AddComponent<Image>();
        panelImg.color = new Color(0.05f, 0.07f, 0.06f, 0.97f);
        panelImg.raycastTarget = true;

        var title = CreateText(rulesPanel.transform, "RulesTitle", new Vector2(0.05f, 0.9f), new Vector2(0.8f, 0.98f), 28, TextAnchor.MiddleLeft);
        title.text = "游戏介绍与规则";
        title.color = new Color(0.92f, 0.88f, 0.7f);

        var closeY = 0.92f;
        CreateMenuButton(rulesPanel.transform, "关闭", ref closeY, 0.08f, new Color(0.35f, 0.25f, 0.22f, 0.95f), () =>
        {
            rulesPanel.SetActive(false);
            // 从设置进入规则时，关闭后仍回到设置面板
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
        body.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (body.font == null)
            body.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        body.fontSize = 20;
        body.color = new Color(0.85f, 0.88f, 0.82f);
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
        himg.color = new Color(0.45f, 0.55f, 0.48f, 0.95f);
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
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = min;
        rt.anchorMax = max;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        var text = go.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (text.font == null)
            text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        text.fontSize = size;
        text.color = Color.white;
        text.alignment = align;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        text.supportRichText = true;
        return text;
    }

    private static Button CreateMenuButton(Transform parent, string label, ref float topY, float step, Color color,
        UnityEngine.Events.UnityAction onClick, Vector2? anchorMin = null, Vector2? anchorMax = null)
    {
        float bottom = topY - step + 0.015f;
        var go = new GameObject(label);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        if (anchorMin.HasValue && anchorMax.HasValue)
        {
            rt.anchorMin = anchorMin.Value;
            rt.anchorMax = anchorMax.Value;
        }
        else
        {
            rt.anchorMin = new Vector2(0.28f, bottom);
            rt.anchorMax = new Vector2(0.72f, topY);
            topY -= step;
        }
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var img = go.AddComponent<Image>();
        img.color = color;
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(onClick);

        var textGo = new GameObject("Text");
        textGo.transform.SetParent(go.transform, false);
        var trt = textGo.AddComponent<RectTransform>();
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = Vector2.zero;
        trt.offsetMax = Vector2.zero;
        var text = textGo.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (text.font == null)
            text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        text.text = label;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.fontSize = 26;
        text.raycastTarget = false;
        return btn;
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
四人各自为战（象、人、猴、猫）。开局每人带着自己的本命玩偶。
满足任一条件即获胜：
· 集齐四种玩偶（象/人/猴/猫各一）
· 成为场上唯一存活的人
若多人同时阵亡且无人集齐玩偶，则为平局。

【对局模式】
· 热座：四人同机轮流操作
· AI 对战：自选一个角色，其余三人由 AI 按相同规则行动

【怎么玩（热座）】
四位玩家轮流用同一台电脑操作。轮到谁，谁才能行动。
每名玩家的行动回合开始时会抽 1 张牌。
本回合你可以：
· 移动一次（蓝格为可走范围）
· 普通近战攻击一次（邻格左键）；弓/弩/炸弹等可多次使用（受弹药与手牌限制）
· 不限次数地弃置物品；用血瓶、强化剂等非攻击牌
· 拾取：点「拾取」，在半径 0～1 的掉落物里挑选（背包满了也能捡，但结束回合前要弃到容量以内）
右键可取消瞄准/拾取/强化选择。
鼠标悬停在角色上，底部会显示属性；悬停在蓝/红角标格子上，可查看掉落物或陷阱信息。
蓝角标 = 掉落物；红角标 = 地雷 / 你放置的定时炸弹或香蕉皮。

【伤害】
普通攻击与炸弹、弓箭等多为「物伤」：最终扣血 = max(0, 伤害 − 防御)。
若攻不破防（扣 0 血），不算「打中」对方（不耗甲）。
熔岩造成「真伤」，无视防御。

【濒死】
血量掉到 0 以下进入濒死：几乎无法作战，物品掉一地，移动力只剩 1。
若再受到会扣血的伤害，或连续几个完整回合没人用血瓶救你，就会死亡。
血瓶可以在濒死当回合自救。

【角色基础（当前版本，血攻防与背包已放大）】
· 象：移3 血120 攻9 防10 包15
· 人：移5 血90 攻9 防8 包24
· 猴：移6 血90 攻6 防6 包18
· 猫：移8 血60 攻9 防4 包12

【卡牌与装备（当前版本）】
共用一副限量牌库，用完的牌进弃牌堆，牌库空了会洗回去。
常见效果：
· 小血瓶：回 9 血，可救濒死
· 炸弹 / 高爆炸弹：投掷，15 / 24 点物伤（悬停可预览爆炸范围）
· 定时炸弹：安在脚下，选 1～5 回合后爆，半径 4，仅自己可见；猫回合结束后结算倒计时
· 香蕉皮：投掷到攻击距离内（仅你可见）；别人回合开始踩到会跌倒；弃置则可见且不触发
· 地雷：弃置到格上（红角标）；踩上受 15 真伤后销毁
· 毒箭 / 火箭：每张牌 3 支（同弓箭合并规则）；命中附加中毒 / 着火
· 弓箭：每张牌 3 支，同种合并为「×N」；每用尽 3 支进弃牌堆
· 弓 / 弩 + 弹药：远程射击（弓攻+2 耗1 / 弩攻+10 耗3）
· 火焰喷射器：直线 5 格 15 物伤+着火，冷却 3 回合
· 强化剂：攻/防永久 +3，或移动 +1
· 木甲 / 铁甲：点「穿戴」后防 +4 / +8；己方回合可「卸下」；未穿戴不减伤；物伤扣血后耗耐久
· 能量护盾：点「穿戴」后挡物伤；己方回合可卸下；未穿戴不生效；共 3 次
· 肾上腺素：仅 HP 低于 30% 可用；移+2 攻+3，持续 3 完整回合（不可叠加）
背包有容量；3 支弓箭约占 1 点容量。行动中可以超重（抽牌/拾取仍可进行）；点「结束回合」时若仍超重，必须先弃置到容量以内才会换手。弃置的东西留在地上，别人可以捡；格子被熔岩吞掉后，地上的牌会进弃牌堆。濒死时物品（含穿戴中的甲/盾）掉落并解除穿戴。
左侧「拾取」下方显示当前行动角色的状态图标（着火/跌倒/中毒等）。

【地图压力】
地图 18×18。每隔 5 个完整回合，最外圈会变成熔岩并不断向内收缩。
站在熔岩上，你的回合开始时会受到 9 点真伤——别站太久。

【操作速查】
· 左键：移动 / 近战攻击 / 确认落点
· 右键：取消当前选择
· 右下角圆形「结束回合」或 Enter：结束当前行动回合（超重时会先要求弃置）
· 右侧背包：使用 / 弃置
· 左上角「重新开始 / 主菜单」：再开一局或回标题";
}
