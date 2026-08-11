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

    private GameObject root;
    private GameObject roleSelectPanel;
    private GameObject mainButtonsRoot;
    private GameSettingsOverlay settingsOverlay;
    private bool roleSelectForAdmin;
    private Text roleSelectTitle;
    private Text roleSelectTip;
    private bool built;

    public bool IsVisible => root != null && root.activeSelf;

    private void Awake()
    {
        Instance = this;
        GameSettingsOverlay.ApplySavedDisplaySettings();
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
        settingsOverlay?.Hide();
        if (roleSelectPanel != null)
            roleSelectPanel.SetActive(false);
        if (mainButtonsRoot != null)
            mainButtonsRoot.SetActive(true);
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
            settingsOverlay?.Show();
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
        settingsOverlay = GameSettingsOverlay.Build(root.transform);
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
                ? "基于 AI 对战 · 正常能见度 · 可查 AI 背包/技能 · 虚空印牌 · 任选时段/天气"
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
            string text = $"{label}    移{move} 血{hp} 攻{atk} 防{def} 法抗{RoleInfo.GetMagicResist(role)}% 包{bag}";
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
四人各自为战（象、人、猴、猫）。四只玩偶各 1 张开局随牌组散落在地图上（背面朝上、与普通散落同色）；拾取入手后持有，界面以金色标识。
满足任一条件即获胜：
· 集齐四种玩偶（象 / 人 / 猴 / 猫各一）
· 成为场上唯一存活者
若多人同时阵亡且无人集齐玩偶，则为平局。

【对局模式】
· 热座：四人同机轮流操作
· AI 对战：自选一个角色，其余三人由 AI 按相同规则行动
· 管理员模式：基于 AI 对战；正常能见度（无透视）；可查看 AI 背包/技能/状态；行动中可虚空印牌；「氛围」面板可任选时段与天气（不自动变天）

【术语】
· 行动：轮到你时的一整段操作（移动、攻击、用牌、拾取、弃置等），直到结束行动。行动开始不摸牌
· 回合：四人各行动一次；时钟、天气、熔岩、多数冷却按回合计

【镜头操作】
第三人称跟背视角；开局镜头在你角色后方、朝向棋盘内侧。
· 滚轮：推近 / 拉远
· Shift + 滚轮：调整俯仰角（更平视 ↔ 更俯视）
· 按住 R：逆时针旋转视角
· 按住 Shift + R：顺时针旋转视角
· 右键拖移：平移视角（短按右键仍可取消瞄准等选择）
AI 对战 / 管理员模式下镜头锁定你的角色，不会跟着 AI 切走暴露位置。

【界面操作】
· 左键：移动 / 近战攻击 / 确认落点
· 右键：取消当前选择（瞄准、拾取、强化等）；拖移见上
· 边缘标签或快捷键 A / L / B：展开或收起「行动 / 战报 / 背包」
· 右下角「结束行动」或 Enter：结束当前行动（背包超重时会拒绝）
· 右侧背包：使用 / 弃置
· 左上角「重新开始 / 主菜单」：再开一局或回标题
· 悬停角色：底部显示属性；悬停蓝/红角标格：查看掉落物或陷阱
  蓝角标 = 掉落物（含弃置的地雷）；红角标 = 武装地雷 / 你放置的定时炸弹或香蕉皮

【怎么玩】
每名玩家行动开始时不摸牌。本行动你可以：
· 移动一次（蓝格为可走范围；不可走到自身能见度之外）
· 普通近战攻击一次（邻格左键）；弓 / 弩 / 炸弹等可多次使用（受弹药与手牌限制）
· 不限次数弃置物品；使用血瓶、强化剂等非攻击牌
· 拾取：点「拾取」，在半径 0～1 的掉落物中挑选（可超重拾取，但超重时无法结束行动）
热座：轮到谁谁操作。AI 对战：仅操控你所选角色。

【伤害】
· 物伤（普攻、多数炸弹与箭矢等）：扣血 = max(0, 伤害 − 防御)。破不开防（扣 0）不算「打中」，但木甲 / 铁甲 / 橡胶雨衣仍耗 1 耐久
· 法伤（着火、地雷、毒伤、喷火、猫近战等）：扣血 = ⌊伤害 × (1 − 法抗%)⌋，不减防、不耗甲耐久。能量护盾可吸收法伤
· 真伤（熔岩站立、领袖耗损、诅咒之刃等）：不减防、不耗甲、护盾不可吸收；真伤为无来源伤害

【濒死】
血量 < 1 进入濒死：移 1、能见度 1，其余属性不变，不清除其他状态；所有物品（含装备）掉落一地。
不可拾取、不可放技能、不可使用卡牌。仍可移动与近战。
轮到自己行动时，若脚下有血瓶可直接使用自救（无需拾取）；再受伤或 3 回合（12 个行动，含进入当次）无人救治则会死亡。

【角色基础】
· 象：移 3 · 血 100 · 攻 9 · 防 10 · 法抗 0% · 包 30
· 人：移 5 · 血 90 · 攻 10 · 防 8 · 法抗 20% · 包 48
· 猴：移 6 · 血 90 · 攻 8 · 防 6 · 法抗 20% · 包 36
· 猫：移 8 · 血 60 · 攻 6 · 防 4 · 法抗 50% · 包 30
猫的近战普攻按法伤结算（不减防、受法抗减免；装备近战武器加攻仍计入法伤）。
技能：象·威慑（被动，半径内减速他人）、人·强化（永久提升攻/防/移）、猴·抢夺（抢走他人非玩偶物品）、猫·隐匿（获得隐匿状态）。本命玩偶与技能升级卡可提升技能等级（上限 3）。
「领袖宣言」：每局限一次；持有任意三只玩偶且缺的一只在其他玩家身上时可发动：领袖状态 10 回合、从场上随机取至多 5 张牌、得知缺偶位置。

【昼夜与能见度】
第 1 回合虚拟时刻 6:00，每完整回合 +2 小时。时段×天气矩阵：
· 清晨/白天/黄昏 × 晴或雨：无限
· 白天雾：8；清晨/黄昏雾：6
· 黑夜晴：5；黑夜雨：4；黑夜雾：3
视野外为不透明迷雾；不可移出自身能见度。濒死能见度下限为 1；至少一层中毒时为 2；致盲为 0。
夜视镜（挂件）：仅黑夜——晴+3（→8）/雨+2（→6）/雾+1（→4）。
望远镜（挂件）：非黑夜——雾天能见度+2；晴/雨/雾可看见隐匿并可窥视可见角色背包；黑夜无效。
护身符（挂件，2 张）：装备中受致命伤时免伤一次，弃置自身，获 1 层临时能量护盾与隐匿；下次行动开始失去护盾与隐匿。

【天气】
开局晴天。变更后 5 回合内不变，第 6～10 回合内必再变一次；晴天下次必转为雨或雾。
· 晴天：能见度见矩阵；无额外攻防
· 雨天：全员防 −3；能见度见矩阵；法抗 +25%；立刻浇灭格子火焰与着火，雨天不可再燃
· 雾天：能见度见矩阵；法抗 +10%
天气同时影响法抗与时段氛围表现。
晴天弹 / 雨天弹 / 雾天弹（各 6）：立刻将天气转为对应天气（已是该天气不可用），重置下次自动变更。

【地图与熔岩】
地图 18×18；沙地 / 沼泽 / 冰地 / 丛林 / 高地斑块散布，四角出生附近多为普通地。
· 沙地：移 −1，法抗 −10%
· 沼泽：移 −1（不叠加其他移速惩罚），进入/每行动始站于其上叠 1 层中毒（最多 3 层），法抗 −20%；离开清除地形毒层
· 冰地：移 +1，行动开始 20% 跌倒（滑行靴免疫）；不铺火焰
· 丛林：进入无火丛林获得隐匿（离开或格上着火解除），法抗 +20%
· 高地：攻/防 +3、射程 +1（台地抬升）
每 5 个完整回合最外圈变为熔岩并向内收缩。站在熔岩上：行动开始先结算着火等状态，再受 20 真伤并可能再次着火。熔岩吞格时地上掉落物进弃牌堆。

【卡牌与装备（摘要）】
共用限量牌组（324 张）；开局每格随机散落 1 张（背面朝上，只知有牌不知种类），靠拾取补给。背包有容量；弹药一张一支占 1 点。行动中可超重，但超重无法结束行动。弃置留在地上可被捡起（正面可见，配色异于开局散落；掉落玩偶仍金色）；熔岩吞格时地上掉落物进弃牌堆。濒死时装备与物品掉落。
· 小 / 大血瓶：回 9 / 15，可救濒死
· 牛奶：清除自身中毒 / 着火 / 跌倒
· 晴 / 雨 / 雾天弹：立刻切换天气
· 炸弹 / 高爆炸弹：投掷 15 / 24 物伤（悬停可预览范围）
· 定时炸弹：安在脚下，选 1–5 回合（实际×4 行动，含本次）后爆，半径 4，仅自己可见
· 闪光弹：投掷 5 格，半径 2 内致盲 1 回合
· 回旋镖：射程 4，15 物伤；若使目标死亡则返回背包
· 汽油瓶 + 打火机：同时持有时可用打火机点燃汽油瓶投向 5 格内，半径 2 内 10 法伤 + 着火
· 香蕉皮：投到攻击距离内（仅你可见）；别人踩到跌倒；弃置后可见且不触发
· 地雷：放置红角标全员可见；踩上 15 法伤；弃置变为可捡掉落物
· 毒箭 / 火箭 / 弓箭：弹药类；弓 / 弩须装备后使用
· 匕首 / 破甲刃 / 诅咒之刃 / 长剑：近战武器；诅咒之刃按次数真伤（已移动不可用）；普攻每行动至多 1 次
· 火焰喷射器：直线 5 格 10 法伤 + 着火；须耗汽油瓶；路径火焰 2 回合（8 行动）
· 强化剂：攻 / 防 / 移永久 +1（三选一，无上限）
· 红牛：行动结束后额外行动一次（不结算行动开始效果）
· 木甲 / 铁甲 / 橡胶雨衣 / 荆棘护甲 / 战术背心 / 能量护盾：防具槽
· 滑板 / 摩托车 / 滑行靴：载具；摩托耗汽油发动 3 回合（移+3 / 冲击 6–10 宽3）；滑行靴冰地移+2且不跌倒
· 抢夺勾爪、护身符、肾上腺素、技能升级卡等：见牌面说明

【距离】
移动、攻击、投掷、爆炸、技能、拾取等一律用曼哈顿距离：|Δx| + |Δy|。";
}
