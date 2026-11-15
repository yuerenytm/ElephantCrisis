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
        GameRulesConfig.JsonReader =
            json => UnityEngine.JsonUtility.FromJson<GameRulesConfig.ConfigDocument>(json);
        GameRulesConfig.EnsureLoaded();   // 核心不再自己挂 RuntimeInitialize 钩子，由宿主决定
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
        WirePresentation();   // 菜单按钮要能调到这里
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
    private void WirePresentation()
    {
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

        if (AtmosphereVisual.Instance != null)
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

        // 每局一世界：管理器全部丢弃，下一局由 EnsureSystems 重建。
        // 只 reset 不丢弃会残留状态——批跑的第二局就会被第一局影响，
        // 同一种子在不同批次里结果不同（dotnet 跑批器暴露了这点）。
        TurnManager.Instance = null;
        GameManager.Instance = null;
        DeckManager.Instance = null;
        GridManager.Instance = null;

        if (PlayerInputController.Instance != null)
            PlayerInputController.Instance.enabled = true;
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

    private void StartMatch()
    {
        if (matchRunning)
            return;
        matchRunning = true;

        MainMenuUI.Instance?.Hide();
        GameRulesConfig.ValidateKeysOnce();

        // 有头游玩用时间播种；无头跑批由 LogicSimRunner 按 seed 播种
        Rng.Reset((int)(System.DateTime.Now.Ticks & 0x7FFFFFFF));

        WirePresentation();
        EnsureSystems();
        SetupCamera();

        var grid = GridManager.Instance;
        grid.InitTiles();
        grid.ClearAllOccupants();

        var map = MapVisual.Instance;
        map.Build(grid);
        map.RefreshAllTiles(grid);
        AtmosphereVisual.Instance?.Build();

        var units = SpawnUnits();
        GameManager.Instance.BeginMatch();
        DeckManager.Instance.BuildDemoDeck();
        DeckManager.Instance.ScatterDrawPileOntoMap();

        GameUI.Instance.Build();

        // 开局快照须在 Setup 之前（Setup 会立刻开始第一回合）
        LogicMatchLogger.Active?.EmitSnapshot();

        TurnManager.Instance.Setup(units);

        // Setup 之后再刷 UI：此前 CurrentUnit 为空，管理员模式会 NRE
        GameUI.Instance.ForceRefresh();
        PlayerInputController.Instance.RefreshHints();
        VisibilityService.RefreshWorld();
        BoardCameraController.Instance?.SnapBehindFollowUnit();

        if (AiController.Instance != null)
        {
            bool useHeuristicAi = MatchConfig.IsAiBattle;
            AiController.Instance.enabled = useHeuristicAi;
            if (useHeuristicAi)
                AiController.Instance.EnsureSubscribed();
        }
    }

    private void EnsureSystems()
    {
        if (GridManager.Instance == null)
            GridManager.Create();

        if (GameManager.Instance == null)
            GameManager.Instance = new GameManager();
        if (TurnManager.Instance == null)
            TurnManager.Instance = new TurnManager();
        if (PlayerInputController.Instance == null)
            gameObject.AddComponent<PlayerInputController>();
        if (GameUI.Instance == null)
            gameObject.AddComponent<GameUI>();
        if (AiController.Instance == null)
            gameObject.AddComponent<AiController>();

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

    /// <summary>
    /// 造四个单位并挂上场景外壳。单位本身由核心创建（与无头模式同一份逻辑），
    /// 这里只负责 GameObject 与 UnitHost。
    /// </summary>
    private List<UnitActor> SpawnUnits()
    {
        var units = MatchSetup.SpawnUnits();

        var root = new GameObject("Units").transform;
        foreach (var unit in units)
        {
            var go = new GameObject(RoleInfo.GetDisplayName(unit.Role));
            go.transform.SetParent(root, false);
            go.AddComponent<UnitHost>().Bind(unit);
        }

        return units;
    }
}
