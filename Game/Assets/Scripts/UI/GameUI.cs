using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class GameUI : MonoBehaviour
{
    public static GameUI Instance;

    private Text titleText;
    private Text statusText;
    private Text logText;
    private Text winnerText;
    private readonly Queue<string> logs = new Queue<string>();
    private const int MaxLogs = 8;

    private Button endTurnBtn;
    private Button restartBtn;
    private Button pickupBtn;
    private Button skillBtn;
    private Button leaderBtn;
    private Button adminGrantBtn;
    private Button adminAtmosphereBtn;
    private Button menuBtn;
    private Button settingsBtn;
    private GameSettingsOverlay settingsOverlay;

    private Text rightTitle;
    private Text hoverText;
    private GameObject hoverPanel;
    private Transform itemListRoot;
    private ScrollRect itemScroll;
    private const float ItemRowHeight = 54f;
    private GameObject reinforcePanel;
    private GameObject ammoPanel;
    private GameObject delayPanel;
    private GameObject adminGrantPanel;
    private Transform adminGrantListRoot;
    private readonly List<GameObject> adminGrantRows = new List<GameObject>();
    private GameObject adminAtmospherePanel;
    private Transform statusIconRoot;
    private readonly List<GameObject> statusIconRows = new List<GameObject>();
    private readonly List<GameObject> itemRows = new List<GameObject>();
    private UnitActor hoveredUnit;
    private string hoveredCellInfo;
    private Vector2Int? hoveredCell;
    private bool built;
    private bool refreshDirty;
    private GameObject canvasRoot;

    // 抽屉：收起后只留边缘标签，把地图让出来
    private RectTransform leftPanelRt;
    private RectTransform logPanelRt;
    private RectTransform bagPanelRt;
    private GameObject tabAction;
    private GameObject tabLog;
    private GameObject tabBag;
    private bool actionDrawerOpen = true;
    private bool logDrawerOpen;
    private bool bagDrawerOpen;

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        var tm = TurnManager.Instance;
        if (tm != null)
        {
            tm.OnTurnChanged -= RequestRefresh;
            tm.OnStateChanged -= RequestRefresh;
            tm.OnLog -= PushLog;
        }
        if (Instance == this)
            Instance = null;
    }

    private void LateUpdate()
    {
        HandleDrawerHotkeys();
        if (!refreshDirty)
            return;
        refreshDirty = false;
        RefreshNow();
    }

    private void HandleDrawerHotkeys()
    {
        if (!built || canvasRoot == null || !canvasRoot.activeInHierarchy)
            return;
        if (MainMenuUI.Instance != null && MainMenuUI.Instance.IsVisible)
            return;
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb == null)
            return;
        if (kb.aKey.wasPressedThisFrame)
            ToggleActionDrawer();
        else if (kb.lKey.wasPressedThisFrame)
            ToggleLogDrawer();
        else if (kb.bKey.wasPressedThisFrame)
            ToggleBagDrawer();
    }

    public void ToggleActionDrawer() => SetActionDrawer(!actionDrawerOpen);
    public void ToggleLogDrawer() => SetLogDrawer(!logDrawerOpen);
    public void ToggleBagDrawer() => SetBagDrawer(!bagDrawerOpen);

    public void SetActionDrawer(bool open)
    {
        actionDrawerOpen = open;
        ApplyDrawerVisibility();
    }

    public void SetLogDrawer(bool open)
    {
        logDrawerOpen = open;
        ApplyDrawerVisibility();
    }

    public void SetBagDrawer(bool open)
    {
        bagDrawerOpen = open;
        ApplyDrawerVisibility();
    }

    public void SetAllDrawers(bool open)
    {
        actionDrawerOpen = open;
        logDrawerOpen = open;
        bagDrawerOpen = open;
        ApplyDrawerVisibility();
    }

    private void ApplyDrawerVisibility()
    {
        if (leftPanelRt != null)
            leftPanelRt.gameObject.SetActive(actionDrawerOpen);
        if (logPanelRt != null)
            logPanelRt.gameObject.SetActive(logDrawerOpen);
        if (bagPanelRt != null)
            bagPanelRt.gameObject.SetActive(bagDrawerOpen);

        if (tabAction != null)
        {
            tabAction.SetActive(!actionDrawerOpen);
            if (!actionDrawerOpen)
                tabAction.transform.SetAsLastSibling();
        }
        if (tabLog != null)
        {
            tabLog.SetActive(!logDrawerOpen);
            if (!logDrawerOpen)
                tabLog.transform.SetAsLastSibling();
        }
        if (tabBag != null)
        {
            tabBag.SetActive(!bagDrawerOpen);
            if (!bagDrawerOpen)
                tabBag.transform.SetAsLastSibling();
        }

        // 悬停条：战报展开时抬高，避免叠在战报上
        if (hoverPanel != null)
        {
            var rt = hoverPanel.GetComponent<RectTransform>();
            if (rt != null)
            {
                if (logDrawerOpen)
                {
                    rt.anchorMin = new Vector2(0.13f, 0.19f);
                    rt.anchorMax = new Vector2(0.7f, 0.27f);
                }
                else
                {
                    rt.anchorMin = new Vector2(0.13f, 0.02f);
                    rt.anchorMax = new Vector2(0.7f, 0.1f);
                }
            }
        }
    }

    private static GameObject CreateDrawerTab(Transform parent, string label, Vector2 anchorMin, Vector2 anchorMax,
        UnityEngine.Events.UnityAction onClick)
    {
        var btn = UiTheme.CreateButton(parent, label, anchorMin, anchorMax, onClick, UiTheme.ButtonTintMoss, UiTheme.FontTiny);
        btn.transform.SetAsLastSibling();
        return btn.gameObject;
    }

    /// <summary>延后到帧末刷新，避免在按钮回调里销毁正在点击的 UI。</summary>
    public void RequestRefresh()
    {
        refreshDirty = true;
    }

    public void Refresh() => RequestRefresh();

    public void ForceRefresh()
    {
        refreshDirty = false;
        RefreshNow();
    }

    public void Teardown()
    {
        refreshDirty = false;
        hoveredUnit = null;
        hoveredCellInfo = null;
        hoveredCell = null;

        var tm = TurnManager.Instance;
        if (tm != null)
        {
            tm.OnTurnChanged -= RequestRefresh;
            tm.OnStateChanged -= RequestRefresh;
            tm.OnLog -= PushLog;
        }

        // 只拆对局 Canvas，不要删 Bootstrap 下的主菜单等其它子物体
        if (canvasRoot != null)
            Destroy(canvasRoot);
        canvasRoot = null;

        itemRows.Clear();
        logs.Clear();
        titleText = null;
        statusText = null;
        logText = null;
        winnerText = null;
        rightTitle = null;
        hoverText = null;
        hoverPanel = null;
        itemListRoot = null;
        itemScroll = null;
        reinforcePanel = null;
        ammoPanel = null;
        delayPanel = null;
        adminGrantPanel = null;
        adminGrantListRoot = null;
        adminGrantRows.Clear();
        adminAtmospherePanel = null;
        statusIconRoot = null;
        statusIconRows.Clear();
        endTurnBtn = null;
        restartBtn = null;
        pickupBtn = null;
        skillBtn = null;
        leaderBtn = null;
        adminGrantBtn = null;
        adminAtmosphereBtn = null;
        menuBtn = null;
        settingsBtn = null;
        settingsOverlay = null;
        leftPanelRt = null;
        logPanelRt = null;
        bagPanelRt = null;
        tabAction = null;
        tabLog = null;
        tabBag = null;
        built = false;
    }

    private void TryClickSkill()
    {
        var turn = TurnManager.Instance;
        var unit = turn?.CurrentUnit;
        if (unit == null || unit.IsDying || unit.IsDead)
            return;
        switch (unit.Role)
        {
            case RoleType.Elephant:
                turn.LogFor(unit, "威慑为被动：降低范围内其他角色移动力");
                break;
            case RoleType.Human:
                SkillService.TryBeginHumanReinforce(unit);
                break;
            case RoleType.Monkey:
                SkillService.TryBeginMonkeySteal(unit);
                break;
            case RoleType.Cat:
                SkillService.TryCatStealth(unit);
                break;
        }
    }

    public void Build()
    {
        if (built)
            return;
        built = true;

        EnsureEventSystem();

        canvasRoot = new GameObject("GameCanvas");
        canvasRoot.transform.SetParent(transform, false);
        var canvas = canvasRoot.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasRoot.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        canvasRoot.AddComponent<GraphicRaycaster>();

        // 地图优先：顶栏细条 + 左窄行动 + 底日志/背包，中间留给战场
        // ┌────────────────────────────┐
        // │ 顶栏                        │
        // ├────┬───────────────────────┤
        // │行动│        地图            │
        // │    │                        │
        // ├────┴──────────┬────────────┤
        // │ 战报           │ 背包       │
        // └───────────────┴────────────┘

        var topBar = UiTheme.CreateFramedPanel(canvasRoot.transform, "TopBar",
            new Vector2(0.01f, 0.935f), new Vector2(0.99f, 0.99f));
        restartBtn = CreateCornerButton(topBar, "Restart", "重新开始",
            new Vector2(0.01f, 0.18f), new Vector2(0.1f, 0.82f),
            () => GameManager.Instance?.Restart());
        menuBtn = CreateCornerButton(topBar, "Menu", "主菜单",
            new Vector2(0.105f, 0.18f), new Vector2(0.19f, 0.82f),
            () => GameManager.Instance?.ReturnToMenu());
        settingsBtn = CreateCornerButton(topBar, "Settings", "设置",
            new Vector2(0.88f, 0.18f), new Vector2(0.985f, 0.82f),
            () => settingsOverlay?.Show());
        titleText = CreateText(topBar, "Title", new Vector2(0.22f, 0.42f), new Vector2(0.86f, 0.92f),
            UiTheme.FontBody, TextAnchor.MiddleLeft);
        titleText.color = UiTheme.TextTitle;
        titleText.fontStyle = FontStyle.Bold;
        statusText = CreateText(topBar, "Status", new Vector2(0.22f, 0.08f), new Vector2(0.86f, 0.48f),
            UiTheme.FontSmall, TextAnchor.MiddleLeft);
        statusText.color = UiTheme.Highlight;

        winnerText = CreateText(canvasRoot.transform, "Winner", new Vector2(0.2f, 0.4f), new Vector2(0.8f, 0.6f),
            36, TextAnchor.MiddleCenter);
        winnerText.color = UiTheme.TextTitle;
        winnerText.fontStyle = FontStyle.Bold;
        winnerText.text = "";

        tabAction = CreateDrawerTab(canvasRoot.transform, "行动 A", new Vector2(0f, 0.55f), new Vector2(0.032f, 0.72f),
            () => ToggleActionDrawer());
        tabLog = CreateDrawerTab(canvasRoot.transform, "战报 L", new Vector2(0.38f, 0f), new Vector2(0.52f, 0.038f),
            () => ToggleLogDrawer());
        tabBag = CreateDrawerTab(canvasRoot.transform, "背包 B", new Vector2(0.968f, 0.38f), new Vector2(1f, 0.55f),
            () => ToggleBagDrawer());

        // 左侧行动（窄，可收起）
        leftPanelRt = CreatePanel(canvasRoot.transform, "LeftPanel",
            new Vector2(0.01f, 0.2f), new Vector2(0.12f, 0.925f));
        var leftHead = UiTheme.CreateHeaderBar(leftPanelRt, "LeftHead",
            new Vector2(0.1f, 0.9f), new Vector2(0.72f, 0.98f));
        var leftTitle = CreateText(leftHead, "LeftTitle", Vector2.zero, Vector2.one,
            UiTheme.FontBody, TextAnchor.MiddleLeft);
        leftTitle.text = "行动";
        leftTitle.color = UiTheme.TextTitle;
        leftTitle.fontStyle = FontStyle.Bold;
        CreateCornerButton(leftPanelRt, "FoldLeft", "‹",
            new Vector2(0.76f, 0.9f), new Vector2(0.94f, 0.98f),
            () => SetActionDrawer(false));

        float y = 0.86f;
        float step = 0.1f;
        pickupBtn = CreateButton(leftPanelRt, "拾取", ref y, step, () =>
        {
            var turn = TurnManager.Instance;
            var unit = turn?.CurrentUnit;
            if (unit == null || unit.IsDying)
                return;
            var loot = GroundItemManager.Instance.GetLootInRange(unit.Cell, 1);
            if (loot.Count == 0)
            {
                turn.LogFor(turn.CurrentUnit, "半径 0–1 内没有可拾取物品");
                return;
            }
            turn.EnterPickupMode();
            turn.LogFor(turn.CurrentUnit, "选择要拾取的物品（右侧列表，右键取消）");
            SetBagDrawer(true);
            RequestRefresh();
        });
        skillBtn = CreateButton(leftPanelRt, "技能", ref y, step, () =>
        {
            TryClickSkill();
            RequestRefresh();
        });
        leaderBtn = CreateButton(leftPanelRt, "领袖宣言", ref y, step, () =>
        {
            LeaderDeclarationService.TryDeclare(TurnManager.Instance?.CurrentUnit);
            RequestRefresh();
        });
        adminGrantBtn = CreateButton(leftPanelRt, "虚空印牌", ref y, step, () =>
        {
            SetBagDrawer(true);
            ToggleAdminGrantPanel();
        });
        adminAtmosphereBtn = CreateButton(leftPanelRt, "氛围", ref y, step, () =>
        {
            ToggleAdminAtmospherePanel();
        });

        var statusTitle = CreateText(leftPanelRt, "StatusTitle", new Vector2(0.1f, 0.04f), new Vector2(0.9f, 0.12f),
            UiTheme.FontSmall, TextAnchor.MiddleLeft);
        statusTitle.text = "状态";
        statusTitle.color = UiTheme.TextMuted;
        var statusGo = new GameObject("StatusIcons");
        statusGo.transform.SetParent(leftPanelRt, false);
        var srt = statusGo.AddComponent<RectTransform>();
        srt.anchorMin = new Vector2(0.1f, 0.14f);
        srt.anchorMax = new Vector2(0.9f, 0.32f);
        srt.offsetMin = Vector2.zero;
        srt.offsetMax = Vector2.zero;
        statusIconRoot = statusGo.transform;

        // 战报（可收起）
        logPanelRt = UiTheme.CreateFramedPanel(canvasRoot.transform, "LogPanel",
            new Vector2(0.13f, 0.02f), new Vector2(0.7f, 0.185f));
        var logHeader = UiTheme.CreateHeaderBar(logPanelRt, "LogHeader",
            new Vector2(0.04f, 0.78f), new Vector2(0.82f, 0.96f));
        var logTitle = CreateText(logHeader, "LogTitle", Vector2.zero, Vector2.one,
            UiTheme.FontSmall, TextAnchor.MiddleLeft);
        logTitle.text = "战报";
        logTitle.color = UiTheme.TextMuted;
        CreateCornerButton(logPanelRt, "FoldLog", "˅",
            new Vector2(0.86f, 0.78f), new Vector2(0.96f, 0.96f),
            () => SetLogDrawer(false));
        logText = CreateText(logPanelRt, "Log", new Vector2(0.04f, 0.06f), new Vector2(0.96f, 0.74f),
            UiTheme.FontSmall, TextAnchor.UpperLeft);
        logText.color = UiTheme.TextBody;

        // 悬停条（贴底，战报收起时不挡地图中央）
        hoverPanel = UiTheme.CreateFramedPanel(canvasRoot.transform, "HoverPanel",
            new Vector2(0.13f, 0.02f), new Vector2(0.7f, 0.1f)).gameObject;
        hoverText = CreateText(hoverPanel.transform, "HoverText", new Vector2(0.04f, 0.1f), new Vector2(0.96f, 0.9f),
            UiTheme.FontSmall, TextAnchor.MiddleLeft);
        hoverText.color = UiTheme.TextBody;
        hoverPanel.SetActive(false);

        // 背包（可收起）
        bagPanelRt = CreatePanel(canvasRoot.transform, "RightPanel",
            new Vector2(0.72f, 0.16f), new Vector2(0.99f, 0.58f));
        var rightHead = UiTheme.CreateHeaderBar(bagPanelRt, "RightHead",
            new Vector2(0.08f, 0.9f), new Vector2(0.72f, 0.98f));
        rightTitle = CreateText(rightHead, "RightTitle", Vector2.zero, Vector2.one,
            UiTheme.FontBody, TextAnchor.MiddleLeft);
        rightTitle.text = "背包";
        rightTitle.color = UiTheme.TextTitle;
        rightTitle.fontStyle = FontStyle.Bold;
        CreateCornerButton(bagPanelRt, "FoldBag", "›",
            new Vector2(0.78f, 0.9f), new Vector2(0.94f, 0.98f),
            () => SetBagDrawer(false));

        CreateItemScrollArea(bagPanelRt);

        reinforcePanel = new GameObject("ReinforcePanel");
        reinforcePanel.transform.SetParent(bagPanelRt, false);
        var rpRt = reinforcePanel.AddComponent<RectTransform>();
        rpRt.anchorMin = new Vector2(0.06f, 0.04f);
        rpRt.anchorMax = new Vector2(0.94f, 0.42f);
        rpRt.offsetMin = Vector2.zero;
        rpRt.offsetMax = Vector2.zero;
        UiTheme.StyleExistingImageAsPanel(reinforcePanel.AddComponent<Image>());

        float ry = 0.98f;
        float rstep = 0.24f;
        CreateButton(reinforcePanel.transform, "强化·攻击", ref ry, rstep, () =>
        {
            var turn = TurnManager.Instance;
            var u = turn?.CurrentUnit;
            if (turn != null && turn.Phase == TurnPhase.SelectingSkillReinforce)
                SkillService.TryConfirmHumanReinforce(u, StatBoost.Attack);
            else
                ItemUseService.TryConfirmReinforce(u, StatBoost.Attack);
            RequestRefresh();
        });
        CreateButton(reinforcePanel.transform, "强化·防御", ref ry, rstep, () =>
        {
            var turn = TurnManager.Instance;
            var u = turn?.CurrentUnit;
            if (turn != null && turn.Phase == TurnPhase.SelectingSkillReinforce)
                SkillService.TryConfirmHumanReinforce(u, StatBoost.Defense);
            else
                ItemUseService.TryConfirmReinforce(u, StatBoost.Defense);
            RequestRefresh();
        });
        CreateButton(reinforcePanel.transform, "强化·移动", ref ry, rstep, () =>
        {
            var turn = TurnManager.Instance;
            var u = turn?.CurrentUnit;
            if (turn != null && turn.Phase == TurnPhase.SelectingSkillReinforce)
                SkillService.TryConfirmHumanReinforce(u, StatBoost.Move);
            else
                ItemUseService.TryConfirmReinforce(u, StatBoost.Move);
            RequestRefresh();
        });
        reinforcePanel.SetActive(false);

        // 弹药选择
        ammoPanel = new GameObject("AmmoPanel");
        ammoPanel.transform.SetParent(bagPanelRt, false);
        var apRt = ammoPanel.AddComponent<RectTransform>();
        apRt.anchorMin = new Vector2(0.06f, 0.04f);
        apRt.anchorMax = new Vector2(0.94f, 0.4f);
        apRt.offsetMin = Vector2.zero;
        apRt.offsetMax = Vector2.zero;
        UiTheme.StyleExistingImageAsPanel(ammoPanel.AddComponent<Image>());
        float ay = 0.95f;
        float astep = 0.3f;
        CreateButton(ammoPanel.transform, "弓箭", ref ay, astep, () =>
            ItemUseService.TryConfirmAmmo(TurnManager.Instance?.CurrentUnit, ItemKind.Arrow));
        CreateButton(ammoPanel.transform, "毒箭", ref ay, astep, () =>
            ItemUseService.TryConfirmAmmo(TurnManager.Instance?.CurrentUnit, ItemKind.PoisonArrow));
        CreateButton(ammoPanel.transform, "火箭", ref ay, astep, () =>
            ItemUseService.TryConfirmAmmo(TurnManager.Instance?.CurrentUnit, ItemKind.FireRocket));
        ammoPanel.SetActive(false);

        // 定时炸弹延时
        delayPanel = new GameObject("DelayPanel");
        delayPanel.transform.SetParent(bagPanelRt, false);
        var dpRt = delayPanel.AddComponent<RectTransform>();
        dpRt.anchorMin = new Vector2(0.06f, 0.04f);
        dpRt.anchorMax = new Vector2(0.94f, 0.4f);
        dpRt.offsetMin = Vector2.zero;
        dpRt.offsetMax = Vector2.zero;
        UiTheme.StyleExistingImageAsPanel(delayPanel.AddComponent<Image>());
        float dy = 0.95f;
        float dstep = 0.18f;
        for (int r = 1; r <= 5; r++)
        {
            int rounds = r;
            CreateButton(delayPanel.transform, $"{rounds}回合后爆", ref dy, dstep, () =>
                ItemUseService.TryConfirmTimedBombDelay(TurnManager.Instance?.CurrentUnit, rounds));
        }
        delayPanel.SetActive(false);

        BuildAdminGrantPanel(bagPanelRt);
        BuildAdminAtmospherePanel(canvasRoot.transform);
        settingsOverlay = GameSettingsOverlay.Build(canvasRoot.transform);

        // 必须在背包面板之后创建，并置顶，否则会被 RightPanel 挡住点击
        endTurnBtn = CreateEndTurnButton(canvasRoot.transform);
        endTurnBtn.transform.SetAsLastSibling();
        ApplyDrawerVisibility();

        if (TurnManager.Instance != null)
        {
            TurnManager.Instance.OnTurnChanged += RequestRefresh;
            TurnManager.Instance.OnStateChanged += RequestRefresh;
            TurnManager.Instance.OnLog += PushLog;
        }
    }

    public void SetHoverUnit(UnitActor unit)
    {
        if (hoveredUnit == unit && hoveredCellInfo == null)
            return;
        hoveredUnit = unit;
        hoveredCellInfo = null;
        hoveredCell = null;
        RefreshHoverPanel();
    }

    public void SetHoverCell(Vector2Int cell, string info)
    {
        if (hoveredUnit == null && hoveredCell == cell && hoveredCellInfo == info)
            return;
        hoveredUnit = null;
        hoveredCell = cell;
        hoveredCellInfo = info;
        RefreshHoverPanel();
    }

    public void ClearHover()
    {
        if (hoveredUnit == null && hoveredCellInfo == null)
            return;
        hoveredUnit = null;
        hoveredCell = null;
        hoveredCellInfo = null;
        RefreshHoverPanel();
    }

    private void RefreshHoverPanel()
    {
        if (hoverPanel == null || hoverText == null)
            return;

        if (hoveredUnit != null && !hoveredUnit.IsDead)
        {
            hoverPanel.SetActive(true);
            string extra = null;
            if (GroundItemManager.Instance != null || HazardManager.Instance != null)
            {
                var loot = GroundItemManager.Instance?.DescribeLoot(hoveredUnit.Cell);
                var hazard = HazardManager.Instance?.DescribeHazards(
                    hoveredUnit.Cell, TurnManager.Instance?.CurrentUnit);
                if (loot != null || hazard != null)
                {
                    extra = "";
                    if (loot != null) extra += "\n" + loot;
                    if (hazard != null) extra += "\n" + hazard;
                }
            }
            var peekViewer = VisibilityService.GetFogViewer();
            if (peekViewer != null
                && hoveredUnit != peekViewer
                && VisibilityService.CanPeekInventory(peekViewer, hoveredUnit)
                && hoveredUnit.Inventory != null)
            {
                extra ??= "";
                extra += "\n望远镜·背包：";
                if (hoveredUnit.Inventory.Count == 0)
                    extra += "空";
                else
                {
                    var names = new System.Collections.Generic.List<string>();
                    foreach (var it in hoveredUnit.Inventory.Items)
                    {
                        string n = ItemInfo.GetDisplayName(it.Kind);
                        if (ItemInfo.IsDoll(it.Kind))
                            n = "★" + n;
                        if (it.Equipped)
                            n += "·装";
                        names.Add(n);
                    }
                    extra += string.Join("、", names);
                }
            }
            hoverText.text = hoveredUnit.GetHoverStatusText() + (extra ?? "");
            return;
        }

        if (!string.IsNullOrEmpty(hoveredCellInfo) && hoveredCell.HasValue)
        {
            hoverPanel.SetActive(true);
            hoverText.text = $"格子 ({hoveredCell.Value.x},{hoveredCell.Value.y})\n{hoveredCellInfo}";
            return;
        }

        hoverPanel.SetActive(false);
    }

    private void PushLog(string msg)
    {
        logs.Enqueue(msg);
        while (logs.Count > MaxLogs)
            logs.Dequeue();
        RequestRefresh();
    }

    private void RefreshNow()
    {
        if (!built || titleText == null)
            return;

        var turn = TurnManager.Instance;
        var game = GameManager.Instance;
        if (turn == null)
            return;

        string deckInfo = DeckManager.Instance != null
            ? $"库{DeckManager.Instance.DrawCount}/弃{DeckManager.Instance.DiscardCount}"
            : "";
        string clock = GameClock.GetStatusLine(turn.RoundNumber);
        string weather = WeatherService.GetDisplayName();
        if (MatchConfig.IsAdminMode)
            titleText.text = $"象群危机 管理员  |  你={RoleInfo.GetDisplayName(MatchConfig.HumanRole)}  |  第{turn.RoundNumber}轮 · {clock} · {weather}  |  {deckInfo}";
        else if (MatchConfig.IsAiBattle)
            titleText.text = $"象群危机 AI  |  你={RoleInfo.GetDisplayName(MatchConfig.HumanRole)}  |  第{turn.RoundNumber}轮 · {clock} · {weather}  |  {deckInfo}";
        else
            titleText.text = $"象群危机 Demo  |  第{turn.RoundNumber}轮 · {clock} · {weather}  |  {deckInfo}";

        if (game != null && game.IsGameOver)
        {
            statusText.text = "游戏结束";
            winnerText.text = game.WinnerText;
        }
        else if (turn.CurrentUnit != null)
        {
            var u = turn.CurrentUnit;
            bool humanTurn = MatchConfig.IsHumanControlled(u);
            string controller = MatchConfig.IsAiBattle
                ? (humanTurn ? (MatchConfig.IsAdminMode ? "（你·管理）" : "（你）") : "（AI）")
                : "";
            string phaseHint = turn.Phase switch
            {
                TurnPhase.SelectingBombTarget => "瞄准投掷（炸弹/闪光弹/汽油瓶，悬停预览，右键取消）",
                TurnPhase.SelectingBananaTarget => "投掷香蕉皮（右键取消）",
                TurnPhase.SelectingMineTarget => "放置地雷（右键取消）",
                TurnPhase.SelectingDiscardTarget => "瞄准弃置（右键取消）",
                TurnPhase.SelectingShootTarget => "瞄准射击/回旋镖（右键取消）",
                TurnPhase.SelectingReinforce => "选择强化（右键取消）",
                TurnPhase.SelectingSkillReinforce => "技能强化（右键取消）",
                TurnPhase.SelectingMonkeyStealTarget => "选择抢夺目标（右键取消）",
                TurnPhase.SelectingMonkeyMarkItem => "选择要夺取的物品",
                TurnPhase.SelectingAmmo => "选择弹药（右键取消）",
                TurnPhase.SelectingTimedBombDelay => "选择延时（右键取消）",
                TurnPhase.SelectingFlameDirection => "选择喷射方向（右键取消）",
                TurnPhase.SelectingMotorcycleRam => "选择冲击终点（四向6–10格·宽3，右键取消）",
                TurnPhase.SelectingHookTarget => "选择勾爪目标（半径3，右键取消）",
                TurnPhase.SelectingPickup => "选择拾取（右键取消）",
                _ => humanTurn ? "左键移动/近战" : "AI 行动中…"
            };
            statusText.text =
                $"当前行动：{RoleInfo.GetDisplayName(u.Role)}{controller}    " +
                $"移{u.CurrentMove} 视{u.CurrentVisibility} 防{u.CurrentDef}    " +
                $"移动{(turn.HasMoved ? "✓" : "○")}  普攻{(turn.HasMeleeAttacked ? "✓" : "○")}    {phaseHint}";
            winnerText.text = "";
        }

        var logSb = new StringBuilder();
        foreach (var line in logs)
            logSb.AppendLine("· " + line);
        if (logs.Count == 0)
            logSb.Append("等待交锋…");
        logText.text = logSb.ToString();

        bool humanTurnActive = MatchConfig.IsHumanTurn();
        bool playing = turn.Phase != TurnPhase.GameOver && turn.CurrentUnit != null && !turn.CurrentUnit.IsDead;
        bool aiming = turn.Phase == TurnPhase.SelectingBombTarget
            || turn.Phase == TurnPhase.SelectingBananaTarget
            || turn.Phase == TurnPhase.SelectingMineTarget
            || turn.Phase == TurnPhase.SelectingDiscardTarget
            || turn.Phase == TurnPhase.SelectingShootTarget
            || turn.Phase == TurnPhase.SelectingReinforce
            || turn.Phase == TurnPhase.SelectingSkillReinforce
            || turn.Phase == TurnPhase.SelectingMonkeyStealTarget
            || turn.Phase == TurnPhase.SelectingMonkeyMarkItem
            || turn.Phase == TurnPhase.SelectingAmmo
            || turn.Phase == TurnPhase.SelectingTimedBombDelay
            || turn.Phase == TurnPhase.SelectingFlameDirection
            || turn.Phase == TurnPhase.SelectingMotorcycleRam
            || turn.Phase == TurnPhase.SelectingHookTarget
            || turn.Phase == TurnPhase.SelectingPickup;

        bool canAct = playing && humanTurnActive;
        endTurnBtn.interactable = canAct && !aiming && turn.Phase == TurnPhase.WaitingAction;
        if (restartBtn != null)
            restartBtn.interactable = true;
        pickupBtn.interactable = canAct && !aiming && turn.CurrentUnit != null && !turn.CurrentUnit.IsDying;
        if (skillBtn != null)
        {
            // 普通 AI 对战：他人回合隐藏；管理员模式可查看 AI 技能信息
            bool showSkillPanel = !MatchConfig.IsAiBattle || humanTurnActive || MatchConfig.IsAdminMode;
            skillBtn.gameObject.SetActive(showSkillPanel);
            if (showSkillPanel)
            {
                var cu = turn.CurrentUnit;
                bool skillOk = canAct && !aiming && cu != null && !cu.IsDying
                    && !SkillInfo.IsPassive(cu.Role) && cu.SkillCooldownLeft <= 0;
                skillBtn.interactable = skillOk || (canAct && !aiming && cu != null && SkillInfo.IsPassive(cu.Role));
                var label = skillBtn.GetComponentInChildren<Text>();
                if (label != null && cu != null)
                {
                    string cd = cu.SkillCooldownLeft > 0 ? $"CD{cu.SkillCooldownLeft}" : $"Lv{cu.SkillLevel}";
                    string who = humanTurnActive ? "" : "·AI ";
                    label.text = $"{who}{SkillInfo.GetSkillName(cu.Role)}\n{cd}";
                }
            }
        }
        if (leaderBtn != null)
        {
            bool showLeader = !MatchConfig.IsAiBattle || humanTurnActive || MatchConfig.IsAdminMode;
            leaderBtn.gameObject.SetActive(showLeader);
            if (showLeader)
            {
                var cu = turn.CurrentUnit;
                bool canLeader = canAct && !aiming && cu != null
                    && LeaderDeclarationService.CanDeclare(cu, out _, out _, out _);
                leaderBtn.interactable = canLeader;
                var label = leaderBtn.GetComponentInChildren<Text>();
                if (label != null && cu != null)
                {
                    label.text = cu.HasUsedLeaderDeclaration
                        ? "领袖宣言\n已用"
                        : "领袖宣言";
                }
            }
        }
        if (adminGrantBtn != null)
        {
            bool showGrant = MatchConfig.IsAdminMode;
            adminGrantBtn.gameObject.SetActive(showGrant);
            if (showGrant)
            {
                bool canGrant = AdminGrantService.CanOpenPanel(turn.CurrentUnit, out _);
                adminGrantBtn.interactable = canGrant && !aiming;
                var label = adminGrantBtn.GetComponentInChildren<Text>();
                if (label != null)
                    label.text = "虚空印牌";
            }
        }
        if (adminAtmosphereBtn != null)
        {
            bool showAtmo = MatchConfig.IsAdminMode;
            adminAtmosphereBtn.gameObject.SetActive(showAtmo);
            if (showAtmo)
            {
                adminAtmosphereBtn.interactable = true;
                var label = adminAtmosphereBtn.GetComponentInChildren<Text>();
                if (label != null)
                {
                    int round = turn.RoundNumber;
                    label.text =
                        $"氛围\n{GameClock.GetPeriodName(round)}/{WeatherService.GetDisplayName()}";
                }
            }
        }
        if (adminAtmospherePanel != null && !MatchConfig.IsAdminMode)
            adminAtmospherePanel.SetActive(false);
        if (menuBtn != null)
            menuBtn.interactable = true;

        reinforcePanel.SetActive(
            (turn.Phase == TurnPhase.SelectingReinforce || turn.Phase == TurnPhase.SelectingSkillReinforce) && canAct);
        if (ammoPanel != null)
            ammoPanel.SetActive(turn.Phase == TurnPhase.SelectingAmmo && canAct);
        if (delayPanel != null)
            delayPanel.SetActive(turn.Phase == TurnPhase.SelectingTimedBombDelay && canAct);

        // 需要背包交互时自动展开
        bool needBag =
            turn.Phase == TurnPhase.SelectingPickup
            || turn.Phase == TurnPhase.SelectingReinforce
            || turn.Phase == TurnPhase.SelectingSkillReinforce
            || turn.Phase == TurnPhase.SelectingAmmo
            || turn.Phase == TurnPhase.SelectingTimedBombDelay
            || turn.Phase == TurnPhase.SelectingMonkeyMarkItem
            || (adminGrantPanel != null && adminGrantPanel.activeSelf);
        if (needBag && canAct)
            SetBagDrawer(true);
        if (adminGrantPanel != null && (!MatchConfig.IsAdminMode || !canAct || aiming))
            adminGrantPanel.SetActive(false);

        // 状态：管理员可看 AI 状态；普通 AI 对战仅自己的回合
        bool showStatus = humanTurnActive || MatchConfig.IsAdminMode || !MatchConfig.IsAiBattle;
        RebuildStatusIcons(showStatus ? turn.CurrentUnit : null);

        if (turn.Phase == TurnPhase.SelectingPickup && canAct)
        {
            rightTitle.text = "附近掉落（半径0–1）";
            RebuildPickupList();
        }
        else if (turn.Phase == TurnPhase.SelectingMonkeyMarkItem && canAct)
        {
            rightTitle.text = turn.PendingHookSteal ? "勾爪夺取" : "抢夺物品";
            RebuildMonkeyMarkList();
        }
        else if (MatchConfig.IsAiBattle && !humanTurnActive && !MatchConfig.IsAdminMode)
        {
            var viewer = VisibilityService.GetFogViewer();
            var aiUnit = turn.CurrentUnit;
            if (viewer != null && aiUnit != null
                && VisibilityService.CanPeekInventory(viewer, aiUnit))
            {
                rightTitle.text = $"窥视·{RoleInfo.GetDisplayName(aiUnit.Role)}";
                RebuildItemList(false);
            }
            else
            {
                rightTitle.text = "背包";
                RebuildHiddenInventory(turn.CurrentUnit);
            }
        }
        else if (MatchConfig.IsAdminMode && !humanTurnActive)
        {
            var aiUnit = turn.CurrentUnit;
            rightTitle.text = aiUnit != null
                ? $"AI背包·{RoleInfo.GetDisplayName(aiUnit.Role)}"
                : "AI背包";
            RebuildItemList(false);
        }
        else
        {
            rightTitle.text = "背包";
            RebuildItemList(canAct && !aiming);
        }

        RefreshHoverPanel();
        PlayerInputController.Instance?.RefreshHints();
        VisibilityService.RefreshWorld();
    }

    private void RebuildStatusIcons(UnitActor unit)
    {
        foreach (var go in statusIconRows)
        {
            if (go != null) Destroy(go);
        }
        statusIconRows.Clear();
        if (statusIconRoot == null)
            return;

        if (unit == null || unit.IsDead || unit.Statuses == null || unit.Statuses.Count == 0)
        {
            var empty = CreateText(statusIconRoot, "None", new Vector2(0f, 0.2f), new Vector2(1f, 0.8f), 12, TextAnchor.MiddleCenter);
            empty.text = MatchConfig.IsAiBattle && !MatchConfig.IsAdminMode && !MatchConfig.IsHumanTurn()
                ? "（不可见）"
                : "（无）";
            empty.color = new Color(0.7f, 0.75f, 0.7f);
            statusIconRows.Add(empty.gameObject);
            return;
        }

        int n = unit.Statuses.Count;
        for (int i = 0; i < n; i++)
        {
            var st = unit.Statuses[i];
            float x0 = (float)i / n;
            float x1 = (float)(i + 1) / n;
            var row = new GameObject(st.Type.ToString());
            row.transform.SetParent(statusIconRoot, false);
            var rt = row.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(x0, 0f);
            rt.anchorMax = new Vector2(x1, 1f);
            rt.offsetMin = new Vector2(2f, 2f);
            rt.offsetMax = new Vector2(-2f, -2f);

            var imgGo = new GameObject("Icon");
            imgGo.transform.SetParent(row.transform, false);
            var irt = imgGo.AddComponent<RectTransform>();
            irt.anchorMin = new Vector2(0.15f, 0.35f);
            irt.anchorMax = new Vector2(0.85f, 0.95f);
            irt.offsetMin = Vector2.zero;
            irt.offsetMax = Vector2.zero;
            var img = imgGo.AddComponent<Image>();
            img.sprite = StatusIconFactory.GetIcon(st.Type);
            img.preserveAspect = true;

            var tip = CreateText(row.transform, "Tip", new Vector2(0f, 0f), new Vector2(1f, 0.32f), 10, TextAnchor.MiddleCenter);
            tip.text = $"{StatusInfo.GetDisplayName(st.Type)}{st.RoundsLeft}";
            tip.color = Color.white;

            statusIconRows.Add(row);
        }
    }

    private void BuildAdminGrantPanel(Transform right)
    {
        adminGrantPanel = new GameObject("AdminGrantPanel");
        adminGrantPanel.transform.SetParent(right, false);
        var rt = adminGrantPanel.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.03f, 0.02f);
        rt.anchorMax = new Vector2(0.97f, 0.91f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        UiTheme.StyleExistingImageAsPanel(adminGrantPanel.AddComponent<Image>());

        var title = CreateText(adminGrantPanel.transform, "GrantTitle", new Vector2(0.05f, 0.92f), new Vector2(0.7f, 0.99f), 16, TextAnchor.MiddleLeft);
        title.text = "虚空印牌（不限次数）";
        title.color = UiTheme.TextIvory;

        float closeY = 0.99f;
        CreateButton(adminGrantPanel.transform, "关闭", ref closeY, 0.08f, () =>
        {
            adminGrantPanel.SetActive(false);
        });

        var scrollGo = new GameObject("GrantScroll");
        scrollGo.transform.SetParent(adminGrantPanel.transform, false);
        var scrollRt = scrollGo.AddComponent<RectTransform>();
        scrollRt.anchorMin = new Vector2(0.04f, 0.04f);
        scrollRt.anchorMax = new Vector2(0.96f, 0.9f);
        scrollRt.offsetMin = Vector2.zero;
        scrollRt.offsetMax = Vector2.zero;
        var scroll = scrollGo.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 28f;

        var viewport = new GameObject("Viewport");
        viewport.transform.SetParent(scrollGo.transform, false);
        var vpRt = viewport.AddComponent<RectTransform>();
        vpRt.anchorMin = Vector2.zero;
        vpRt.anchorMax = Vector2.one;
        vpRt.offsetMin = Vector2.zero;
        vpRt.offsetMax = Vector2.zero;
        viewport.AddComponent<Image>().color = new Color(0, 0, 0, 0.02f);
        viewport.AddComponent<RectMask2D>();

        var content = new GameObject("Content");
        content.transform.SetParent(viewport.transform, false);
        var contentRt = content.AddComponent<RectTransform>();
        contentRt.anchorMin = new Vector2(0f, 1f);
        contentRt.anchorMax = new Vector2(1f, 1f);
        contentRt.pivot = new Vector2(0.5f, 1f);
        contentRt.sizeDelta = Vector2.zero;
        var layout = content.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 3f;
        layout.padding = new RectOffset(2, 2, 2, 2);
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scroll.viewport = vpRt;
        scroll.content = contentRt;
        adminGrantListRoot = content.transform;
        adminGrantPanel.SetActive(false);
    }

    private void ToggleAdminGrantPanel()
    {
        if (!MatchConfig.IsAdminMode || adminGrantPanel == null)
            return;
        if (adminGrantPanel.activeSelf)
        {
            adminGrantPanel.SetActive(false);
            return;
        }
        if (!AdminGrantService.CanOpenPanel(TurnManager.Instance?.CurrentUnit, out string reason))
        {
            TurnManager.Instance?.Log(reason);
            return;
        }
        if (adminAtmospherePanel != null)
            adminAtmospherePanel.SetActive(false);
        RebuildAdminGrantList();
        adminGrantPanel.SetActive(true);
        adminGrantPanel.transform.SetAsLastSibling();
    }

    private void BuildAdminAtmospherePanel(Transform parent)
    {
        adminAtmospherePanel = UiTheme.CreateFramedPanel(parent, "AdminAtmospherePanel",
            new Vector2(0.34f, 0.28f), new Vector2(0.66f, 0.78f)).gameObject;

        var title = CreateText(adminAtmospherePanel.transform, "Title",
            new Vector2(0.06f, 0.88f), new Vector2(0.7f, 0.98f),
            UiTheme.FontBody, TextAnchor.MiddleLeft);
        title.text = "管理员·氛围";
        title.color = UiTheme.TextIvory;

        float closeY = 0.99f;
        CreateButton(adminAtmospherePanel.transform, "关闭", ref closeY, 0.08f, () =>
        {
            adminAtmospherePanel.SetActive(false);
        });

        var periodTitle = CreateText(adminAtmospherePanel.transform, "PeriodTitle",
            new Vector2(0.06f, 0.78f), new Vector2(0.94f, 0.86f),
            UiTheme.FontSmall, TextAnchor.MiddleLeft);
        periodTitle.text = "时段（任选）";
        periodTitle.color = UiTheme.TextMuted;

        float y = 0.76f;
        float step = 0.09f;
        CreateButton(adminAtmospherePanel.transform, "清晨 6:00", ref y, step, () =>
        {
            if (GameClock.AdminSetPeriod(GameClock.Period.Dawn))
                RequestRefresh();
        });
        CreateButton(adminAtmospherePanel.transform, "白天 12:00", ref y, step, () =>
        {
            if (GameClock.AdminSetPeriod(GameClock.Period.Day))
                RequestRefresh();
        });
        CreateButton(adminAtmospherePanel.transform, "黄昏 18:00", ref y, step, () =>
        {
            if (GameClock.AdminSetPeriod(GameClock.Period.Dusk))
                RequestRefresh();
        });
        CreateButton(adminAtmospherePanel.transform, "黑夜 0:00", ref y, step, () =>
        {
            if (GameClock.AdminSetPeriod(GameClock.Period.Night))
                RequestRefresh();
        });

        var weatherTitle = CreateText(adminAtmospherePanel.transform, "WeatherTitle",
            new Vector2(0.06f, y - 0.02f), new Vector2(0.94f, y + 0.05f),
            UiTheme.FontSmall, TextAnchor.MiddleLeft);
        weatherTitle.text = "天气（任选）";
        weatherTitle.color = UiTheme.TextMuted;
        y -= 0.08f;

        CreateButton(adminAtmospherePanel.transform, "晴天", ref y, step, () =>
        {
            if (WeatherService.AdminSetWeather(WeatherType.Clear))
                RequestRefresh();
        });
        CreateButton(adminAtmospherePanel.transform, "雨天", ref y, step, () =>
        {
            if (WeatherService.AdminSetWeather(WeatherType.Rain))
                RequestRefresh();
        });
        CreateButton(adminAtmospherePanel.transform, "雾天", ref y, step, () =>
        {
            if (WeatherService.AdminSetWeather(WeatherType.Fog))
                RequestRefresh();
        });

        adminAtmospherePanel.SetActive(false);
    }

    private void ToggleAdminAtmospherePanel()
    {
        if (!MatchConfig.IsAdminMode || adminAtmospherePanel == null)
            return;
        bool open = !adminAtmospherePanel.activeSelf;
        adminAtmospherePanel.SetActive(open);
        if (open)
        {
            if (adminGrantPanel != null)
                adminGrantPanel.SetActive(false);
            adminAtmospherePanel.transform.SetAsLastSibling();
        }
    }

    private void RebuildAdminGrantList()
    {
        foreach (var row in adminGrantRows)
        {
            if (row != null)
                Destroy(row);
        }
        adminGrantRows.Clear();
        if (adminGrantListRoot == null)
            return;

        var kinds = AdminGrantService.ListPrintableKinds();
        for (int i = 0; i < kinds.Count; i++)
        {
            var kind = kinds[i];
            var row = new GameObject($"Grant_{kind}");
            row.transform.SetParent(adminGrantListRoot, false);
            row.AddComponent<RectTransform>();
            var le = row.AddComponent<LayoutElement>();
            le.minHeight = 40f;
            le.preferredHeight = 40f;

            string label = $"{ItemInfo.GetDisplayName(kind)}  ·  {ItemInfo.GetShortDesc(kind)}";
            var btn = CreateSmallButton(row.transform, label, new Vector2(0.02f, 0.08f), new Vector2(0.98f, 0.92f), () =>
            {
                if (AdminGrantService.TryGrant(TurnManager.Instance?.CurrentUnit, kind))
                    RequestRefresh();
            });
            var img = btn.targetGraphic as Image;
            if (img != null)
            {
                img.color = ItemInfo.IsDoll(kind)
                    ? ItemInfo.GetDollGoldUiBg()
                    : new Color(0.28f, 0.2f, 0.32f, 0.95f);
            }
            adminGrantRows.Add(row);
        }
    }

    private void CreateItemScrollArea(Transform right)
    {
        var scrollGo = new GameObject("ItemScroll");
        scrollGo.transform.SetParent(right, false);
        var scrollRt = scrollGo.AddComponent<RectTransform>();
        scrollRt.anchorMin = new Vector2(0.06f, 0.04f);
        scrollRt.anchorMax = new Vector2(0.94f, 0.86f);
        scrollRt.offsetMin = Vector2.zero;
        scrollRt.offsetMax = Vector2.zero;

        itemScroll = scrollGo.AddComponent<ScrollRect>();
        itemScroll.horizontal = false;
        itemScroll.vertical = true;
        itemScroll.movementType = ScrollRect.MovementType.Clamped;
        itemScroll.scrollSensitivity = 28f;
        itemScroll.inertia = true;
        itemScroll.decelerationRate = 0.135f;

        var viewportGo = new GameObject("Viewport");
        viewportGo.transform.SetParent(scrollGo.transform, false);
        var viewportRt = viewportGo.AddComponent<RectTransform>();
        viewportRt.anchorMin = Vector2.zero;
        viewportRt.anchorMax = Vector2.one;
        viewportRt.offsetMin = Vector2.zero;
        viewportRt.offsetMax = Vector2.zero;
        var vpImg = viewportGo.AddComponent<Image>();
        vpImg.color = new Color(0f, 0f, 0f, 0.02f);
        vpImg.raycastTarget = true;
        viewportGo.AddComponent<RectMask2D>();

        var contentGo = new GameObject("Content");
        contentGo.transform.SetParent(viewportGo.transform, false);
        var contentRt = contentGo.AddComponent<RectTransform>();
        contentRt.anchorMin = new Vector2(0f, 1f);
        contentRt.anchorMax = new Vector2(1f, 1f);
        contentRt.pivot = new Vector2(0.5f, 1f);
        contentRt.anchoredPosition = Vector2.zero;
        contentRt.sizeDelta = new Vector2(0f, 0f);

        var layout = contentGo.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(8, 8, 8, 8);
        layout.spacing = 8f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        var fitter = contentGo.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        itemScroll.viewport = viewportRt;
        itemScroll.content = contentRt;
        itemListRoot = contentGo.transform;
    }

    private void ClearItemRows()
    {
        foreach (var row in itemRows)
        {
            if (row != null)
                Destroy(row);
        }
        itemRows.Clear();
        if (itemScroll != null)
            itemScroll.verticalNormalizedPosition = 1f;
    }

    private GameObject CreateListRow(string name, float height)
    {
        var row = new GameObject(name);
        row.transform.SetParent(itemListRoot, false);
        row.AddComponent<RectTransform>();
        var le = row.AddComponent<LayoutElement>();
        le.minHeight = height;
        le.preferredHeight = height;
        le.flexibleWidth = 1f;
        return row;
    }

    private void RebuildMonkeyMarkList()
    {
        ClearItemRows();

        var turn = TurnManager.Instance;
        var target = turn?.PendingSkillTargetUnit;
        if (target == null || target.Inventory == null)
            return;

        var items = target.Inventory.Items;
        for (int i = 0; i < items.Count; i++)
        {
            int idx = i;
            var it = items[i];
            var row = CreateListRow($"Steal_{i}", ItemRowHeight);
            string label = ItemInfo.IsDoll(it.Kind)
                ? $"{ItemInfo.GetDisplayName(it.Kind)}（不可）"
                : $"夺取 {ItemInfo.GetDisplayName(it.Kind)}";
            var btn = CreateSmallButton(row.transform, label,
                new Vector2(0.02f, 0.08f), new Vector2(0.98f, 0.92f),
                () =>
                {
                    var tm = TurnManager.Instance;
                    if (tm != null && tm.PendingHookSteal)
                        ActionService.TryHookTakeItem(tm.CurrentUnit, idx);
                    else
                        SkillService.TryMonkeyTakeItem(tm?.CurrentUnit, idx);
                    RequestRefresh();
                });
            btn.interactable = !ItemInfo.IsDoll(it.Kind);
            if (ItemInfo.IsDoll(it.Kind))
            {
                var img = btn.targetGraphic as Image;
                if (img != null)
                    img.color = ItemInfo.GetDollGoldUiBg();
                var txt = btn.GetComponentInChildren<Text>();
                if (txt != null)
                    txt.color = ItemInfo.GetDollGoldText();
            }
            itemRows.Add(row);
        }
    }

    private void RebuildPickupList()
    {
        ClearItemRows();

        var turn = TurnManager.Instance;
        var unit = turn?.CurrentUnit;
        if (unit == null)
            return;

        var loot = GroundItemManager.Instance.GetLootInRange(unit.Cell, 1);
        if (loot.Count == 0)
        {
            var emptyRow = CreateListRow("Empty", ItemRowHeight * 1.5f);
            var empty = CreateText(emptyRow.transform, "Empty", new Vector2(0.05f, 0.1f), new Vector2(0.95f, 0.9f), 16, TextAnchor.MiddleCenter);
            empty.text = "附近没有掉落物\n点取消退出";
            itemRows.Add(emptyRow);
            return;
        }

        for (int i = 0; i < loot.Count; i++)
        {
            var entry = loot[i];
            var cell = entry.Cell;
            int gIndex = entry.Index;
            var kind = entry.Kind;

            var row = CreateListRow($"Loot_{i}", ItemRowHeight);
            var bg = row.AddComponent<Image>();
            bool faceDown = entry.FaceDown;
            bool isDoll = !faceDown && ItemInfo.IsDoll(kind);
            bg.sprite = UiTheme.RoundSprite();
            bg.type = Image.Type.Sliced;
            bg.color = isDoll
                ? ItemInfo.GetDollGoldUiBg()
                : faceDown
                    ? new Color(0.32f, 0.3f, 0.28f, 0.95f)
                    : UiTheme.Row;

            var label = CreateText(row.transform, "Name", new Vector2(0.02f, 0.05f), new Vector2(0.62f, 0.95f), 13, TextAnchor.MiddleLeft);
            int dist = GridManager.Instance.GetManhattanDistance(unit.Cell, cell);
            string lootName;
            if (faceDown)
                lootName = "未知卡牌";
            else if (kind == ItemKind.Arrow && entry.Charges > 0)
                lootName = $"弓箭×{entry.Charges}";
            else
                lootName = ItemInfo.GetDisplayName(kind);
            label.text = $"{lootName}\n<size=11>({cell.x},{cell.y}) 距{dist}</size>";
            if (isDoll)
                label.color = ItemInfo.GetDollGoldText();
            else if (faceDown)
                label.color = new Color(0.75f, 0.72f, 0.68f);

            var pickBtn = CreateSmallButton(row.transform, "拾取", new Vector2(0.64f, 0.15f), new Vector2(0.98f, 0.85f), () =>
            {
                ActionService.TryPickupOne(TurnManager.Instance?.CurrentUnit, cell, gIndex);
            });
            pickBtn.interactable = true;

            itemRows.Add(row);
        }
    }

    private void RebuildHiddenInventory(UnitActor unit)
    {
        ClearItemRows();
        if (itemListRoot == null)
            return;

        string who = unit != null ? RoleInfo.GetDisplayName(unit.Role) : "对手";
        var tipRow = CreateListRow("Hidden", ItemRowHeight * 2f);
        var tip = CreateText(tipRow.transform, "Hidden", new Vector2(0.08f, 0.1f), new Vector2(0.92f, 0.9f), 16, TextAnchor.MiddleCenter);
        tip.text = $"{who}（AI）行动中\n背包内容不可见";
        tip.color = new Color(0.65f, 0.7f, 0.66f);
        itemRows.Add(tipRow);
    }

    private void RebuildItemList(bool allowActions)
    {
        ClearItemRows();

        var turn = TurnManager.Instance;
        if (turn?.CurrentUnit == null || turn.Phase == TurnPhase.GameOver)
            return;

        var unit = turn.CurrentUnit;
        if (unit.IsDying)
        {
            var tipRow = CreateListRow("DyingTip", ItemRowHeight * 2.2f);
            var tip = CreateText(tipRow.transform, "DyingTip", new Vector2(0.05f, 0.05f), new Vector2(0.95f, 0.95f), 14, TextAnchor.MiddleCenter);
            tip.text = "濒死：物品已掉落\n不可拾取 / 不可用背包卡\n可移动、近战；脚下有血瓶可自救";
            tip.color = new Color(1f, 0.75f, 0.7f);
            itemRows.Add(tipRow);

            bool hasPotion = ActionService.HasGroundPotionAt(unit);
            var btnRow = CreateListRow("GroundPotion", ItemRowHeight);
            var useBtn = CreateSmallButton(btnRow.transform, "使用脚下血瓶", new Vector2(0.15f, 0.12f), new Vector2(0.85f, 0.88f), () =>
            {
                ActionService.TryUseGroundPotion(TurnManager.Instance?.CurrentUnit);
                GameUI.Instance?.RequestRefresh();
            });
            useBtn.interactable = allowActions && hasPotion && turn.Phase == TurnPhase.WaitingAction;
            itemRows.Add(btnRow);
            return;
        }

        var items = unit.Inventory.Items;
        if (items.Count == 0)
        {
            var emptyRow = CreateListRow("Empty", ItemRowHeight);
            var empty = CreateText(emptyRow.transform, "Empty", new Vector2(0.05f, 0.1f), new Vector2(0.95f, 0.9f), 16, TextAnchor.MiddleCenter);
            empty.text = "（空）";
            itemRows.Add(emptyRow);
            return;
        }

        for (int i = 0; i < items.Count; i++)
        {
            int index = i;
            var entry = items[i];
            var kind = entry.Kind;

            var row = CreateListRow($"Item_{i}", ItemRowHeight);
            var bg = row.AddComponent<Image>();
            bool isDoll = ItemInfo.IsDoll(kind);
            bg.sprite = UiTheme.RoundSprite();
            bg.type = Image.Type.Sliced;
            bg.color = isDoll ? ItemInfo.GetDollGoldUiBg() : UiTheme.Row;

            var label = CreateText(row.transform, "Name", new Vector2(0.02f, 0.05f), new Vector2(0.48f, 0.95f), 13, TextAnchor.MiddleLeft);
            string name = ItemInfo.GetDisplayName(kind);
            string desc = ItemInfo.GetShortDesc(kind);
            if (ItemInfo.IsStackableAmmo(kind))
            {
                name = $"{ItemInfo.GetDisplayName(kind)}×{entry.Charges}";
                desc = $"弹药 {entry.Charges} 支";
            }
            else if (ItemInfo.GetMaxCharges(kind) > 0)
            {
                desc = $"{desc} · 剩{entry.Charges}";
            }
            else if (kind == ItemKind.CursedBlade)
            {
                int next = Mathf.Max(0, entry.Charges) + 1;
                desc = $"{desc} · 下次第{next}次(己{next}/敌{2 * next})";
            }
            if (entry.Equipped)
                name = $"{name}（装备）";
            label.text = $"{name}\n<size=11>{desc}</size>";
            if (isDoll)
                label.color = ItemInfo.GetDollGoldText();

            bool isEquip = ItemInfo.IsEquipable(kind);
            string useLabel = "使用";
            if (kind == ItemKind.SkillUpgrade)
            {
                int n = unit.Inventory.CountOf(ItemKind.SkillUpgrade);
                useLabel = n >= 3 ? "升级(3)" : $"缺{3 - n}";
            }
            else if (isEquip)
            {
                if (!entry.Equipped)
                    useLabel = "装备";
                else if (kind == ItemKind.Bow)
                    useLabel = "射击";
                else if (kind == ItemKind.Crossbow)
                    useLabel = unit.CrossbowCharged
                        ? (unit.CrossbowChargedThisAction ? "已蓄力" : "射击")
                        : "蓄力";
                else if (kind == ItemKind.Flamethrower)
                    useLabel = unit.Inventory.CountOf(ItemKind.GasolineBottle) > 0 ? "喷射" : "缺油";
                else if (kind == ItemKind.Motorcycle)
                {
                    if (!entry.Equipped)
                        useLabel = "装备";
                    else if (unit.MotorcycleActiveRounds <= 0)
                        useLabel = unit.Inventory.CountOf(ItemKind.GasolineBottle) > 0 ? "发动" : "缺油";
                    else if (turn.HasMoved)
                        useLabel = $"卸下·{unit.MotorcycleActiveRounds}回";
                    else
                        useLabel = $"冲击·{unit.MotorcycleActiveRounds}";
                }
                else if (kind == ItemKind.GrappleHook)
                    useLabel = "抢夺";
                else
                    useLabel = "卸下";
            }
            if (entry.Equipped)
                bg.color = UiTheme.RowEquipped;
            else if (isDoll)
                bg.color = ItemInfo.GetDollGoldUiBg();

            bool canUseItems = allowActions && turn.Phase == TurnPhase.WaitingAction;
            bool canDiscardItems = allowActions && turn.Phase == TurnPhase.WaitingAction;
            bool crossbowLocked = isEquip && entry.Equipped && kind == ItemKind.Crossbow
                && unit.CrossbowCharged && unit.CrossbowChargedThisAction;
            var useBtn = CreateSmallButton(row.transform, useLabel, new Vector2(0.5f, 0.15f), new Vector2(0.74f, 0.85f), () =>
            {
                ItemUseService.TryBeginUse(TurnManager.Instance?.CurrentUnit, index);
                RequestRefresh();
            });
            var dropBtn = CreateSmallButton(row.transform, "弃置", new Vector2(0.76f, 0.15f), new Vector2(0.98f, 0.85f), () =>
            {
                PlayerInputController.Instance?.StartDiscardMode(index);
            });
            useBtn.interactable = canUseItems && !crossbowLocked;
            dropBtn.interactable = canDiscardItems;

            itemRows.Add(row);
        }
    }

    private static Button CreateCornerButton(
        Transform parent, string name, string label, Vector2 anchorMin, Vector2 anchorMax,
        UnityEngine.Events.UnityAction onClick)
    {
        var btn = UiTheme.CreateButton(parent, label, anchorMin, anchorMax, onClick,
            UiTheme.ButtonTintClay, 14);
        btn.gameObject.name = name;
        return btn;
    }

    private static void EnsureEventSystem()
    {
        if (FindFirstObjectByType<EventSystem>() != null)
            return;

        var es = new GameObject("EventSystem");
        es.AddComponent<EventSystem>();
        var inputSystemUi = System.Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
        if (inputSystemUi != null)
        {
            var module = es.AddComponent(inputSystemUi);
            inputSystemUi.GetMethod("AssignDefaultActions")?.Invoke(module, null);
        }
        else
            es.AddComponent<StandaloneInputModule>();
    }

    private static Text CreateText(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, int fontSize, TextAnchor align)
        => UiTheme.CreateText(parent, name, anchorMin, anchorMax, fontSize, align);

    private static RectTransform CreatePanel(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax)
        => UiTheme.CreateFramedPanel(parent, name, anchorMin, anchorMax);

    private static Button CreateEndTurnButton(Transform parent)
    {
        var go = new GameObject("EndTurn");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(1f, 0f);
        rt.anchorMax = new Vector2(1f, 0f);
        rt.pivot = new Vector2(1f, 0f);
        rt.sizeDelta = new Vector2(122f, 122f);
        rt.anchoredPosition = new Vector2(-18f, 18f);

        var img = go.AddComponent<Image>();
        img.sprite = SpriteFactory.CreateEndTurnButton(256);
        img.type = Image.Type.Simple;
        img.preserveAspect = true;
        img.color = Color.white;
        img.raycastTarget = true;
        img.alphaHitTestMinimumThreshold = 0.2f;

        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        var colors = btn.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.05f, 1.02f, 0.95f, 1f);
        colors.pressedColor = new Color(0.82f, 0.78f, 0.68f, 1f);
        colors.disabledColor = new Color(0.4f, 0.4f, 0.4f, 0.6f);
        btn.colors = colors;
        btn.onClick.AddListener(() =>
        {
            TurnManager.Instance?.RequestEndTurn();
            GameUI.Instance?.RequestRefresh();
        });

        var textGo = new GameObject("Label");
        textGo.transform.SetParent(go.transform, false);
        var trt = textGo.AddComponent<RectTransform>();
        trt.anchorMin = new Vector2(0.1f, 0.08f);
        trt.anchorMax = new Vector2(0.9f, 0.32f);
        trt.offsetMin = Vector2.zero;
        trt.offsetMax = Vector2.zero;
        var text = textGo.AddComponent<Text>();
        text.font = UiTheme.Font();
        text.text = "结束行动";
        text.alignment = TextAnchor.MiddleCenter;
        text.fontSize = 15;
        text.fontStyle = FontStyle.Bold;
        text.color = UiTheme.TextIvory;
        text.raycastTarget = false;

        var hintGo = new GameObject("Hint");
        hintGo.transform.SetParent(go.transform, false);
        var hrt = hintGo.AddComponent<RectTransform>();
        hrt.anchorMin = new Vector2(0.05f, -0.16f);
        hrt.anchorMax = new Vector2(0.95f, 0.04f);
        hrt.offsetMin = Vector2.zero;
        hrt.offsetMax = Vector2.zero;
        var hint = hintGo.AddComponent<Text>();
        hint.font = text.font;
        hint.text = "Enter";
        hint.alignment = TextAnchor.UpperCenter;
        hint.fontSize = 12;
        hint.color = UiTheme.TextMuted;
        hint.raycastTarget = false;

        return btn;
    }

    private static Button CreateButton(Transform parent, string label, ref float topY, float step, UnityEngine.Events.UnityAction onClick)
        => UiTheme.CreateStackedButton(parent, label, ref topY, step, onClick, UiTheme.ButtonTintMoss, 17);

    private static Button CreateSmallButton(Transform parent, string label, Vector2 anchorMin, Vector2 anchorMax, UnityEngine.Events.UnityAction onClick)
        => UiTheme.CreateButton(parent, label, anchorMin, anchorMax, onClick, UiTheme.ButtonTintMoss, 13);
}
