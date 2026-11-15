using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 启动入口：默认进主菜单；热座 / AI 对战进入对局。
/// 重新开始 / 回主菜单均不切场景，避免 LoadScene 蓝屏。
/// </summary>
public class GameBootstrap : MonoBehaviour
{
    public static GameBootstrap Instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoBootstrap()
    {
        if (FindFirstObjectByType<GameBootstrap>() != null)
            return;
        var go = new GameObject("GameBootstrap");
        go.AddComponent<GameBootstrap>();
    }

    private bool matchRunning;
    private bool transitionQueued;

    /// <summary>
    /// 比配置加载（BeforeSceneLoad）更早执行：把"外部世界"注入核心。
    /// 顺序很关键——晚一步，GameRulesConfig 就会在空路径下退回内置默认值。
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
    private static void InjectHostServices()
    {
        GameRulesConfig.ConfigDirectory = System.IO.Path.Combine(
            UnityEngine.Application.streamingAssetsPath, "Config");
        Log.Info = UnityEngine.Debug.Log;
        Log.Warning = UnityEngine.Debug.LogWarning;
        Log.Error = UnityEngine.Debug.LogError;
    }

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void Start()
    {
        if (BootFlags.HeadlessRequested)
            return;
        ShowMainMenu();
    }

    /// <summary>
    /// 显示主菜单并清空当前对局。
    /// </summary>
    public void ShowMainMenu()
    {
        matchRunning = false;
        MatchConfig.Clear();
        WirePresentation(BootFlags.HeadlessRequested);   // 菜单按钮要能调到这里
        if (MainMenuUI.Instance == null)
            gameObject.AddComponent<MainMenuUI>();
        MainMenuUI.Instance.Show();
    }

    /// <summary>
    /// 请求重开：排队到帧末执行，避免在按钮回调里销毁 UI。
    /// </summary>
    public void RequestRestartMatch()
    {
        if (transitionQueued)
            return;
        transitionQueued = true;
        StartCoroutine(CoRestartMatch());
    }

    /// <summary>
    /// 兼容旧调用名。
    /// </summary>
    public void RequestRestartHotseat() => RequestRestartMatch();

    /// <summary>
    /// 请求回主菜单：同样排队到帧末执行。
    /// </summary>
    public void RequestReturnToMenu()
    {
        if (transitionQueued)
            return;
        transitionQueued = true;
        StartCoroutine(CoReturnToMenu());
    }

    /// <summary>
    /// 装配表现层：无头模式退化为空实现，有头模式注入真实实现与流程委托。
    /// 显示主菜单时就要调用一次——否则菜单按钮拿到的是空实现，点了没反应。
    /// </summary>
    private void WirePresentation(bool lean)
    {
        if (lean)
        {
            Presentation.Clear();
            return;
        }

        Presentation.Wire(new UnityPresentation
        {
            StartHotseatAction = StartHotseat,
            StartAiBattleAction = StartAiBattle,
            StartAdminAction = StartAdminMode,
            RestartAction = RequestRestartMatch,
            ReturnToMenuAction = RequestReturnToMenu,
        });
    }

    private IEnumerator CoRestartMatch()
    {
        yield return null;
        var mode = MatchConfig.Mode;
        var human = MatchConfig.HumanRole;
        TeardownGameplay();
        yield return null;
        matchRunning = false;
        transitionQueued = false;

        if (mode == GameMode.AdminMode)
            StartAdminMode(human);
        else if (mode == GameMode.AiBattle)
            StartAiBattle(human);
        else
            StartHotseat();
    }

    private IEnumerator CoReturnToMenu()
    {
        yield return null;
        TeardownGameplay();
        yield return null;
        matchRunning = false;
        transitionQueued = false;
        ShowMainMenu();
    }

    /// <summary>
    /// 清掉对局物件，不卸载场景。
    /// </summary>
    private void TeardownGameplay()
    {
        if (PlayerInputController.Instance != null)
            PlayerInputController.Instance.enabled = false;

        if (AiController.Instance != null)
            AiController.Instance.enabled = false;

        if (GameUI.Instance != null)
            GameUI.Instance.Teardown();

        var unitsRoot = GameObject.Find("Units");
        if (unitsRoot != null)
            Destroy(unitsRoot);

        if (MapVisual.Instance != null)
        {
            var go = MapVisual.Instance.gameObject;
            Destroy(go);
        }

        if (AtmosphereVisual.Instance != null && MatchConfig.IsLogicSim)
            Destroy(AtmosphereVisual.Instance.gameObject);

        if (GroundItemManager.Instance != null)
        {
            GroundItemManager.Instance.ClearAll();
            GroundItemManager.Instance = null;
        }

        if (HazardManager.Instance != null)
        {
            HazardManager.Instance.ClearAll();
            HazardManager.Instance = null;
        }

        if (GridManager.Instance != null)
        {
            GridManager.Instance.ClearAllOccupants();
            GridManager.Instance.InitTiles();
        }

        TurnManager.Instance?.ResetMatch();
        GameManager.Instance?.ClearMatch();
        Presentation.Clear();

        if (PlayerInputController.Instance != null && !MatchConfig.IsLogicSim)
            PlayerInputController.Instance.enabled = true;
    }

    /// <summary>
    /// LogicSim 局间清理（保留 Bootstrap 与系统组件）。
    /// </summary>
    public void TeardownForLogicSim()
    {
        // 必须同步销毁：延迟 Destroy 会使下一局同帧仍拿到已毁单例
        TeardownGameplayImmediate();
        matchRunning = false;
        transitionQueued = false;
    }

    private void TeardownGameplayImmediate()
    {
        if (PlayerInputController.Instance != null)
            PlayerInputController.Instance.enabled = false;

        if (AiController.Instance != null)
            AiController.Instance.enabled = false;

        if (GameUI.Instance != null)
            GameUI.Instance.Teardown();

        var unitsRoot = GameObject.Find("Units");
        if (unitsRoot != null)
            DestroyImmediate(unitsRoot);

        if (MapVisual.Instance != null)
            DestroyImmediate(MapVisual.Instance.gameObject);

        if (AtmosphereVisual.Instance != null)
            DestroyImmediate(AtmosphereVisual.Instance.gameObject);

        if (GroundItemManager.Instance != null)
        {
            GroundItemManager.Instance.ClearAll();
            GroundItemManager.Instance = null;
        }

        if (HazardManager.Instance != null)
        {
            HazardManager.Instance.ClearAll();
            HazardManager.Instance = null;
        }

        if (GridManager.Instance != null)
        {
            GridManager.Instance.ClearAllOccupants();
            // 保留 GridManager 组件，仅重置格子
            GridManager.Instance.InitTiles();
        }

        TurnManager.Instance?.ResetMatch();
        GameManager.Instance?.ClearMatch();
        Presentation.Clear();
    }

    /// <summary>
    /// 开始热座对局（四人都可操作）。
    /// </summary>
    public void StartHotseat()
    {
        MatchConfig.SetHotseat();
        StartMatch();
    }

    /// <summary>
    /// 开始 AI 对战，指定由人类操作的角色。
    /// </summary>
    public void StartAiBattle(RoleType humanRole)
    {
        MatchConfig.SetAiBattle(humanRole);
        StartMatch();
    }

    /// <summary>
    /// 开始管理员模式（可任意印牌 / 切天气 / 切时段）。
    /// </summary>
    public void StartAdminMode(RoleType humanRole)
    {
        MatchConfig.SetAdminMode(humanRole);
        StartMatch();
    }

    /// <summary>
    /// LogicSim：全 AI 瘦开局（调用前须 MatchConfig.SetLogicSim）。
    /// </summary>
    public void StartLogicSimMatch()
    {
        if (!MatchConfig.IsLogicSim)
            MatchConfig.SetLogicSim();
        matchRunning = false;
        StartMatch();
    }

    /// <summary>
    /// 局间清理后允许再次 StartMatch（LogicSim 连跑）。
    /// </summary>
    public void ResetMatchRunningFlag()
    {
        matchRunning = false;
    }

    /// <summary>
    /// 兼容旧入口。
    /// </summary>
    public void StartAiBattle() => MainMenuUI.Instance?.OnAiBattleClicked();

    /// <summary>
    /// 联机入口（预留，当前跳主菜单提示）。
    /// </summary>
    public void StartOnline() => MainMenuUI.Instance?.OnOnlineClicked();

    /// <summary>
    /// 兼容旧按钮：等同热座开局。
    /// </summary>
    [ContextMenu("Start Hotseat")]
    public void StartDemo() => StartHotseat();

    private void StartMatch()
    {
        if (matchRunning)
            return;
        matchRunning = true;

        MainMenuUI.Instance?.Hide();
        GameRulesConfig.ValidateKeysOnce();

        // 有头游玩用时间播种；无头跑批由 LogicSimRunner 按 seed 播种
        if (!BootFlags.HeadlessRequested)
            Rng.Reset((int)(System.DateTime.Now.Ticks & 0x7FFFFFFF));

        bool lean = MatchConfig.IsLogicSim;
        WirePresentation(lean);
        EnsureSystems(lean);
        if (!lean)
            SetupCamera();

        var grid = GridManager.Instance;
        grid.InitTiles();
        grid.ClearAllOccupants();

        if (!lean)
        {
            var map = MapVisual.Instance;
            map.Build(grid);
            map.RefreshAllTiles(grid);
            AtmosphereVisual.Instance?.Build();
        }

        var units = SpawnUnits();
        GameManager.Instance.BeginMatch();
        DeckManager.Instance.BuildDemoDeck();
        DeckManager.Instance.ScatterDrawPileOntoMap();

        if (!lean)
            GameUI.Instance.Build();
        else if (PlayerInputController.Instance != null)
            PlayerInputController.Instance.enabled = false;

        // 开局快照须在 Setup/AI 泵开始之前（LogicSim 会在 Setup 内同步打完整局）
        LogicMatchLogger.Active?.EmitSnapshot();

        TurnManager.Instance.Setup(units);

        // Setup 之后再刷 UI：此前 CurrentUnit 为空，管理员模式会 NRE
        if (!lean)
        {
            GameUI.Instance.ForceRefresh();
            PlayerInputController.Instance.RefreshHints();
            VisibilityService.RefreshWorld();
            BoardCameraController.Instance?.SnapBehindFollowUnit();
        }

        if (AiController.Instance != null)
        {
            bool useHeuristicAi = MatchConfig.IsAiBattle;
            AiController.Instance.enabled = useHeuristicAi;
            if (useHeuristicAi)
                AiController.Instance.EnsureSubscribed();
        }
    }

    private void EnsureSystems(bool leanLogicSim = false)
    {
        if (GridManager.Instance == null)
            GridManager.Create();

        if (GameManager.Instance == null)
            GameManager.Instance = new GameManager();
        if (TurnManager.Instance == null)
            TurnManager.Instance = new TurnManager();
        if (PlayerInputController.Instance == null)
            gameObject.AddComponent<PlayerInputController>();
        if (GameUI.Instance == null && !leanLogicSim)
            gameObject.AddComponent<GameUI>();
        if (AiController.Instance == null)
            gameObject.AddComponent<AiController>();

        if (!leanLogicSim)
        {
            if (MapVisual.Instance == null)
            {
                var mapGo = new GameObject("MapVisual");
                mapGo.AddComponent<MapVisual>();
            }

            if (AtmosphereVisual.Instance == null)
            {
                var atmoGo = new GameObject("Atmosphere");
                atmoGo.AddComponent<AtmosphereVisual>();
            }
        }

        if (GroundItemManager.Instance == null)
        {
            var lootGo = new GameObject("GroundItems");
            GroundItemManager.Instance = new GroundItemManager();
        }

        if (HazardManager.Instance == null)
        {
            var hz = new GameObject("Hazards");
            HazardManager.Instance = new HazardManager();
        }

        if (DeckManager.Instance == null)
            DeckManager.Instance = new DeckManager();
    }

    private void SetupCamera()
    {
        var cam = Camera.main;
        if (cam == null)
        {
            var camGo = new GameObject("Main Camera");
            cam = camGo.AddComponent<Camera>();
            cam.tag = "MainCamera";
            camGo.AddComponent<AudioListener>();
        }

        cam.clearFlags = CameraClearFlags.SolidColor;
        var boardCam = cam.GetComponent<BoardCameraController>();
        if (boardCam == null)
            boardCam = cam.gameObject.AddComponent<BoardCameraController>();

        float w = GridManager.Instance.gridWidth * GridManager.Instance.cellSize;
        float d = GridManager.Instance.gridHeight * GridManager.Instance.cellSize;
        boardCam.FrameBoard(w, d);
    }

    private List<UnitActor> SpawnUnits()
    {
        var list = new List<UnitActor>();
        var roles = new[]
        {
            RoleType.Elephant,
            RoleType.Human,
            RoleType.Monkey,
            RoleType.Cat
        };
        var starts = new[]
        {
            GameRulesConfig.SpawnCell(roles[0]),
            GameRulesConfig.SpawnCell(roles[1]),
            GameRulesConfig.SpawnCell(roles[2]),
            GameRulesConfig.SpawnCell(roles[3])
        };

        var root = new GameObject("Units").transform;

        for (int i = 0; i < 4; i++)
        {
            var go = new GameObject(RoleInfo.GetDisplayName(roles[i]));
            go.transform.SetParent(root, false);

            // 逻辑单位是普通类；场景对象由 UnitHost 承载（无头模式不挂外皮）
            var unit = new UnitActor();
            unit.Setup(roles[i], starts[i]);   // 必须先 Setup：Role/MaxHp 等在外皮构建时要用

            if (!MatchConfig.IsLogicSim)
                go.AddComponent<UnitHost>().Bind(unit);
            list.Add(unit);
        }

        return list;
    }
}
