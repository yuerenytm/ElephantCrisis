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
    private Button menuBtn;

    private Text rightTitle;
    private Text hoverText;
    private GameObject hoverPanel;
    private Transform itemListRoot;
    private GameObject reinforcePanel;
    private GameObject ammoPanel;
    private GameObject delayPanel;
    private Transform statusIconRoot;
    private readonly List<GameObject> statusIconRows = new List<GameObject>();
    private readonly List<GameObject> itemRows = new List<GameObject>();
    private UnitActor hoveredUnit;
    private string hoveredCellInfo;
    private Vector2Int? hoveredCell;
    private bool built;
    private bool refreshDirty;
    private GameObject canvasRoot;

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
        if (!refreshDirty)
            return;
        refreshDirty = false;
        RefreshNow();
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
        reinforcePanel = null;
        ammoPanel = null;
        delayPanel = null;
        statusIconRoot = null;
        statusIconRows.Clear();
        endTurnBtn = null;
        restartBtn = null;
        pickupBtn = null;
        menuBtn = null;
        built = false;
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

        restartBtn = CreateCornerButton(canvasRoot.transform, "Restart", "重新开始",
            new Vector2(0.008f, 0.955f), new Vector2(0.11f, 0.992f),
            () => GameManager.Instance?.Restart());
        menuBtn = CreateCornerButton(canvasRoot.transform, "Menu", "主菜单",
            new Vector2(0.115f, 0.955f), new Vector2(0.21f, 0.992f),
            () => GameManager.Instance?.ReturnToMenu());

        titleText = CreateText(canvasRoot.transform, "Title", new Vector2(0.22f, 0.92f), new Vector2(0.7f, 0.99f), 24, TextAnchor.UpperLeft);
        statusText = CreateText(canvasRoot.transform, "Status", new Vector2(0.2f, 0.84f), new Vector2(0.7f, 0.92f), 17, TextAnchor.UpperLeft);
        logText = CreateText(canvasRoot.transform, "Log", new Vector2(0.2f, 0.14f), new Vector2(0.7f, 0.4f), 15, TextAnchor.LowerLeft);
        winnerText = CreateText(canvasRoot.transform, "Winner", new Vector2(0.25f, 0.45f), new Vector2(0.75f, 0.58f), 36, TextAnchor.MiddleCenter);
        winnerText.color = new Color(1f, 0.9f, 0.3f);
        winnerText.text = "";

        // 底部悬停属性条
        hoverPanel = new GameObject("HoverPanel");
        hoverPanel.transform.SetParent(canvasRoot.transform, false);
        var hpRt = hoverPanel.AddComponent<RectTransform>();
        hpRt.anchorMin = new Vector2(0.18f, 0.015f);
        hpRt.anchorMax = new Vector2(0.72f, 0.14f);
        hpRt.offsetMin = Vector2.zero;
        hpRt.offsetMax = Vector2.zero;
        var hpImg = hoverPanel.AddComponent<Image>();
        hpImg.color = new Color(0.05f, 0.08f, 0.07f, 0.88f);
        hoverText = CreateText(hoverPanel.transform, "HoverText", new Vector2(0.03f, 0.05f), new Vector2(0.97f, 0.95f), 15, TextAnchor.MiddleLeft);
        hoverPanel.SetActive(false);

        // 左侧：拾取 / 状态
        var left = CreatePanel(canvasRoot.transform, "LeftPanel", new Vector2(0.01f, 0.42f), new Vector2(0.18f, 0.88f));
        float y = 0.88f;
        float step = 0.28f;
        pickupBtn = CreateButton(left, "拾取", ref y, step, () =>
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
            RequestRefresh();
        });

        var statusTitle = CreateText(left, "StatusTitle", new Vector2(0.08f, 0.02f), new Vector2(0.92f, 0.28f), 14, TextAnchor.MiddleCenter);
        statusTitle.text = "当前状态";
        var statusGo = new GameObject("StatusIcons");
        statusGo.transform.SetParent(left, false);
        var srt = statusGo.AddComponent<RectTransform>();
        srt.anchorMin = new Vector2(0.06f, 0.32f);
        srt.anchorMax = new Vector2(0.94f, 0.58f);
        srt.offsetMin = Vector2.zero;
        srt.offsetMax = Vector2.zero;
        statusIconRoot = statusGo.transform;

        var right = CreatePanel(canvasRoot.transform, "RightPanel", new Vector2(0.72f, 0.02f), new Vector2(0.99f, 0.98f));
        rightTitle = CreateText(right, "RightTitle", new Vector2(0.05f, 0.92f), new Vector2(0.95f, 0.99f), 18, TextAnchor.MiddleCenter);
        rightTitle.text = "背包";

        var listGo = new GameObject("ItemList");
        listGo.transform.SetParent(right, false);
        var listRt = listGo.AddComponent<RectTransform>();
        listRt.anchorMin = new Vector2(0.03f, 0.28f);
        listRt.anchorMax = new Vector2(0.97f, 0.91f);
        listRt.offsetMin = Vector2.zero;
        listRt.offsetMax = Vector2.zero;
        itemListRoot = listGo.transform;

        reinforcePanel = new GameObject("ReinforcePanel");
        reinforcePanel.transform.SetParent(right, false);
        var rpRt = reinforcePanel.AddComponent<RectTransform>();
        rpRt.anchorMin = new Vector2(0.05f, 0.04f);
        rpRt.anchorMax = new Vector2(0.95f, 0.26f);
        rpRt.offsetMin = Vector2.zero;
        rpRt.offsetMax = Vector2.zero;
        var rpImg = reinforcePanel.AddComponent<Image>();
        rpImg.color = new Color(0.15f, 0.2f, 0.18f, 0.95f);

        float ry = 0.95f;
        float rstep = 0.3f;
        CreateButton(reinforcePanel.transform, "强化·攻击", ref ry, rstep, () =>
        {
            ItemUseService.TryConfirmReinforce(TurnManager.Instance?.CurrentUnit, StatBoost.Attack);
            RequestRefresh();
        });
        CreateButton(reinforcePanel.transform, "强化·防御", ref ry, rstep, () =>
        {
            ItemUseService.TryConfirmReinforce(TurnManager.Instance?.CurrentUnit, StatBoost.Defense);
            RequestRefresh();
        });
        CreateButton(reinforcePanel.transform, "强化·移动", ref ry, rstep, () =>
        {
            ItemUseService.TryConfirmReinforce(TurnManager.Instance?.CurrentUnit, StatBoost.Move);
            RequestRefresh();
        });
        reinforcePanel.SetActive(false);

        // 弹药选择
        ammoPanel = new GameObject("AmmoPanel");
        ammoPanel.transform.SetParent(right, false);
        var apRt = ammoPanel.AddComponent<RectTransform>();
        apRt.anchorMin = new Vector2(0.05f, 0.04f);
        apRt.anchorMax = new Vector2(0.95f, 0.26f);
        apRt.offsetMin = Vector2.zero;
        apRt.offsetMax = Vector2.zero;
        ammoPanel.AddComponent<Image>().color = new Color(0.15f, 0.18f, 0.22f, 0.95f);
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
        delayPanel.transform.SetParent(right, false);
        var dpRt = delayPanel.AddComponent<RectTransform>();
        dpRt.anchorMin = new Vector2(0.05f, 0.04f);
        dpRt.anchorMax = new Vector2(0.95f, 0.26f);
        dpRt.offsetMin = Vector2.zero;
        dpRt.offsetMax = Vector2.zero;
        delayPanel.AddComponent<Image>().color = new Color(0.22f, 0.12f, 0.12f, 0.95f);
        float dy = 0.95f;
        float dstep = 0.18f;
        for (int r = 1; r <= 5; r++)
        {
            int rounds = r;
            CreateButton(delayPanel.transform, $"{rounds}回合后爆", ref dy, dstep, () =>
                ItemUseService.TryConfirmTimedBombDelay(TurnManager.Instance?.CurrentUnit, rounds));
        }
        delayPanel.SetActive(false);

        // 必须在背包面板之后创建，并置顶，否则会被 RightPanel 挡住点击
        endTurnBtn = CreateEndTurnButton(canvasRoot.transform);
        endTurnBtn.transform.SetAsLastSibling();

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
        titleText.text = MatchConfig.IsAiBattle
            ? $"象群危机 AI  |  你={RoleInfo.GetDisplayName(MatchConfig.HumanRole)}  |  第{turn.RoundNumber}轮  |  {deckInfo}"
            : $"象群危机 Demo  |  第{turn.RoundNumber}轮  |  {deckInfo}";

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
                ? (humanTurn ? "（你）" : "（AI）")
                : "";
            string phaseHint = turn.Phase switch
            {
                TurnPhase.SelectingBombTarget => "瞄准炸弹（悬停预览，右键取消）",
                TurnPhase.SelectingBananaTarget => "投掷香蕉皮（右键取消）",
                TurnPhase.SelectingDiscardTarget => "瞄准弃置（右键取消）",
                TurnPhase.SelectingShootTarget => "瞄准射击（右键取消）",
                TurnPhase.SelectingReinforce => "选择强化（右键取消）",
                TurnPhase.SelectingAmmo => "选择弹药（右键取消）",
                TurnPhase.SelectingTimedBombDelay => "选择延时（右键取消）",
                TurnPhase.SelectingFlameDirection => "选择喷射方向（右键取消）",
                TurnPhase.SelectingPickup => "选择拾取（右键取消）",
                TurnPhase.MandatoryDiscard => "超重强制弃置（降至容量内才能结束）",
                _ => humanTurn ? "左键移动/近战" : "AI 行动中…"
            };
            statusText.text =
                $"当前行动：{RoleInfo.GetDisplayName(u.Role)}{controller}    " +
                $"移动{(turn.HasMoved ? "✓" : "○")}  普攻{(turn.HasMeleeAttacked ? "✓" : "○")}    {phaseHint}";
            winnerText.text = "";
        }

        var logSb = new StringBuilder();
        logSb.AppendLine("战报：");
        foreach (var line in logs)
            logSb.AppendLine("· " + line);
        logText.text = logSb.ToString();

        bool humanTurnActive = MatchConfig.IsHumanTurn();
        bool playing = turn.Phase != TurnPhase.GameOver && turn.CurrentUnit != null && !turn.CurrentUnit.IsDead;
        bool aiming = turn.Phase == TurnPhase.SelectingBombTarget
            || turn.Phase == TurnPhase.SelectingBananaTarget
            || turn.Phase == TurnPhase.SelectingDiscardTarget
            || turn.Phase == TurnPhase.SelectingShootTarget
            || turn.Phase == TurnPhase.SelectingReinforce
            || turn.Phase == TurnPhase.SelectingAmmo
            || turn.Phase == TurnPhase.SelectingTimedBombDelay
            || turn.Phase == TurnPhase.SelectingFlameDirection
            || turn.Phase == TurnPhase.SelectingPickup;

        bool canAct = playing && humanTurnActive;
        bool trimming = turn.Phase == TurnPhase.MandatoryDiscard || turn.AwaitingCapacityTrim;
        endTurnBtn.interactable = canAct && (!aiming || trimming) &&
            (turn.Phase == TurnPhase.WaitingAction || turn.Phase == TurnPhase.MandatoryDiscard);
        if (restartBtn != null)
            restartBtn.interactable = true;
        pickupBtn.interactable = canAct && !aiming && !trimming && turn.CurrentUnit != null && !turn.CurrentUnit.IsDying;
        if (menuBtn != null)
            menuBtn.interactable = true;

        reinforcePanel.SetActive(turn.Phase == TurnPhase.SelectingReinforce && canAct);
        if (ammoPanel != null)
            ammoPanel.SetActive(turn.Phase == TurnPhase.SelectingAmmo && canAct);
        if (delayPanel != null)
            delayPanel.SetActive(turn.Phase == TurnPhase.SelectingTimedBombDelay && canAct);

        // 状态图标：仅自己的回合显示详情；AI 回合不展示其状态以免泄密
        RebuildStatusIcons(humanTurnActive ? turn.CurrentUnit : null);

        if (turn.Phase == TurnPhase.SelectingPickup && canAct)
        {
            rightTitle.text = "附近掉落（半径0–1）";
            RebuildPickupList();
        }
        else if (MatchConfig.IsAiBattle && !humanTurnActive)
        {
            rightTitle.text = "背包";
            RebuildHiddenInventory(turn.CurrentUnit);
        }
        else
        {
            rightTitle.text = "背包";
            RebuildItemList(canAct && !aiming);
        }

        RefreshHoverPanel();
        PlayerInputController.Instance?.RefreshHints();
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
            empty.text = MatchConfig.IsAiBattle && !MatchConfig.IsHumanTurn() ? "（不可见）" : "（无）";
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

    private void RebuildPickupList()
    {
        foreach (var row in itemRows)
        {
            if (row != null)
                Destroy(row);
        }
        itemRows.Clear();

        var turn = TurnManager.Instance;
        var unit = turn?.CurrentUnit;
        if (unit == null)
            return;

        var loot = GroundItemManager.Instance.GetLootInRange(unit.Cell, 1);
        if (loot.Count == 0)
        {
            var empty = CreateText(itemListRoot, "Empty", new Vector2(0.05f, 0.8f), new Vector2(0.95f, 0.95f), 16, TextAnchor.MiddleCenter);
            empty.text = "附近没有掉落物\n点取消退出";
            itemRows.Add(empty.gameObject);
            return;
        }

        float rowH = Mathf.Min(0.1f, 0.9f / Mathf.Max(loot.Count, 1));
        for (int i = 0; i < loot.Count; i++)
        {
            float top = 1f - i * rowH;
            float bot = top - rowH + 0.008f;
            var entry = loot[i];
            var cell = entry.Cell;
            int gIndex = entry.Index;
            var kind = entry.Kind;

            var row = new GameObject($"Loot_{i}");
            row.transform.SetParent(itemListRoot, false);
            var rt = row.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, bot);
            rt.anchorMax = new Vector2(1f, top);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var bg = row.AddComponent<Image>();
            bg.color = new Color(0.18f, 0.16f, 0.1f, 0.92f);

            var label = CreateText(row.transform, "Name", new Vector2(0.02f, 0.05f), new Vector2(0.62f, 0.95f), 13, TextAnchor.MiddleLeft);
            int dist = GridManager.Instance.GetManhattanDistance(unit.Cell, cell);
            string lootName = kind == ItemKind.Arrow && entry.Charges > 0
                ? $"弓箭×{entry.Charges}"
                : ItemInfo.GetDisplayName(kind);
            label.text = $"{lootName}\n<size=11>({cell.x},{cell.y}) 距{dist}</size>";

            var pickBtn = CreateSmallButton(row.transform, "拾取", new Vector2(0.64f, 0.15f), new Vector2(0.98f, 0.85f), () =>
            {
                ActionService.TryPickupOne(TurnManager.Instance?.CurrentUnit, cell, gIndex);
                // 刷新延后到 LateUpdate，避免销毁当前按钮
            });
            pickBtn.interactable = true;

            itemRows.Add(row);
        }
    }

    private void RebuildHiddenInventory(UnitActor unit)
    {
        foreach (var row in itemRows)
        {
            if (row != null)
                Destroy(row);
        }
        itemRows.Clear();
        if (itemListRoot == null)
            return;

        string who = unit != null ? RoleInfo.GetDisplayName(unit.Role) : "对手";
        var tip = CreateText(itemListRoot, "Hidden", new Vector2(0.08f, 0.35f), new Vector2(0.92f, 0.65f), 16, TextAnchor.MiddleCenter);
        tip.text = $"{who}（AI）行动中\n背包内容不可见";
        tip.color = new Color(0.65f, 0.7f, 0.66f);
        itemRows.Add(tip.gameObject);
    }

    private void RebuildItemList(bool allowActions)
    {
        foreach (var row in itemRows)
        {
            if (row != null)
                Destroy(row);
        }
        itemRows.Clear();

        var turn = TurnManager.Instance;
        if (turn?.CurrentUnit == null || turn.Phase == TurnPhase.GameOver)
            return;

        var unit = turn.CurrentUnit;
        var items = unit.Inventory.Items;
        if (items.Count == 0)
        {
            var empty = CreateText(itemListRoot, "Empty", new Vector2(0.05f, 0.8f), new Vector2(0.95f, 0.95f), 16, TextAnchor.MiddleCenter);
            empty.text = "（空）";
            itemRows.Add(empty.gameObject);
            return;
        }

        // 自上而下排列，最多显示约 10 条
        float rowH = Mathf.Min(0.09f, 0.9f / Mathf.Max(items.Count, 1));
        for (int i = 0; i < items.Count; i++)
        {
            float top = 1f - i * rowH;
            float bot = top - rowH + 0.008f;
            int index = i;
            var entry = items[i];
            var kind = entry.Kind;

            var row = new GameObject($"Item_{i}");
            row.transform.SetParent(itemListRoot, false);
            var rt = row.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, bot);
            rt.anchorMax = new Vector2(1f, top);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var bg = row.AddComponent<Image>();
            bg.color = new Color(0.12f, 0.16f, 0.14f, 0.9f);

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
                if (entry.Equipped)
                    name = $"{name}（穿戴）";
            }
            label.text = $"{name}\n<size=11>{desc}</size>";

            bool canUse = ItemInfo.GetUseKind(kind) != ItemUseKind.None;
            bool isEquip = ItemInfo.GetUseKind(kind) == ItemUseKind.EquipToggle;
            string useLabel = isEquip ? (entry.Equipped ? "卸下" : "穿戴") : "使用";
            if (entry.Equipped)
                bg.color = new Color(0.14f, 0.28f, 0.2f, 0.95f);

            var useBtn = CreateSmallButton(row.transform, useLabel, new Vector2(0.5f, 0.15f), new Vector2(0.74f, 0.85f), () =>
            {
                ItemUseService.TryBeginUse(TurnManager.Instance?.CurrentUnit, index);
            });
            var dropBtn = CreateSmallButton(row.transform, "弃置", new Vector2(0.76f, 0.15f), new Vector2(0.98f, 0.85f), () =>
            {
                PlayerInputController.Instance?.StartDiscardMode(index);
            });
            bool canUseItems = allowActions && turn.Phase == TurnPhase.WaitingAction;
            bool canDiscardItems = allowActions &&
                (turn.Phase == TurnPhase.WaitingAction || turn.Phase == TurnPhase.MandatoryDiscard);
            useBtn.interactable = canUseItems && canUse;
            dropBtn.interactable = canDiscardItems;

            itemRows.Add(row);
        }
    }

    private static Button CreateCornerButton(
        Transform parent, string name, string label, Vector2 anchorMin, Vector2 anchorMax,
        UnityEngine.Events.UnityAction onClick)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var img = go.AddComponent<Image>();
        img.color = new Color(0.18f, 0.42f, 0.28f, 0.88f);
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        var colors = btn.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(0.85f, 1f, 0.9f, 1f);
        colors.pressedColor = new Color(0.7f, 0.9f, 0.75f, 1f);
        btn.colors = colors;
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
        text.color = new Color(0.75f, 1f, 0.82f, 1f);
        text.fontSize = 14;
        text.raycastTarget = false;
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
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        var text = go.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (text.font == null)
            text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        text.fontSize = fontSize;
        text.color = Color.white;
        text.alignment = align;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        text.supportRichText = true;
        return text;
    }

    private static RectTransform CreatePanel(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        var img = go.AddComponent<Image>();
        img.color = new Color(0f, 0f, 0f, 0.55f);
        return rt;
    }

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
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (text.font == null)
            text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        text.text = "结束回合";
        text.alignment = TextAnchor.MiddleCenter;
        text.fontSize = 15;
        text.fontStyle = FontStyle.Bold;
        text.color = new Color(0.96f, 0.9f, 0.72f, 1f);
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
        hint.color = new Color(0.78f, 0.7f, 0.48f, 0.9f);
        hint.raycastTarget = false;

        return btn;
    }

    private static Button CreateButton(Transform parent, string label, ref float topY, float step, UnityEngine.Events.UnityAction onClick)
    {
        float bottom = topY - step + 0.015f;
        var go = new GameObject(label);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.08f, bottom);
        rt.anchorMax = new Vector2(0.92f, topY);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        topY -= step;

        var img = go.AddComponent<Image>();
        img.color = new Color(0.2f, 0.45f, 0.35f, 0.95f);
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
        text.fontSize = 18;
        text.raycastTarget = false;
        return btn;
    }

    private static Button CreateSmallButton(Transform parent, string label, Vector2 anchorMin, Vector2 anchorMax, UnityEngine.Events.UnityAction onClick)
    {
        var go = new GameObject(label);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var img = go.AddComponent<Image>();
        img.color = new Color(0.25f, 0.5f, 0.4f, 0.95f);
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
        text.fontSize = 14;
        text.raycastTarget = false;
        return btn;
    }
}
