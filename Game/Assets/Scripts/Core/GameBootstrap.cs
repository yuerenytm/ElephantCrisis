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
        if (LogicSimRunner.IsRequested || RlTrainingRunner.IsRequested)
            return;
        ShowMainMenu();
    }

    public void ShowMainMenu()
    {
        matchRunning = false;
        MatchConfig.Clear();
        if (MainMenuUI.Instance == null)
            gameObject.AddComponent<MainMenuUI>();
        MainMenuUI.Instance.Show();
    }

    public void RequestRestartMatch()
    {
        if (transitionQueued)
            return;
        transitionQueued = true;
        StartCoroutine(CoRestartMatch());
    }

    /// <summary>兼容旧调用名。</summary>
    public void RequestRestartHotseat() => RequestRestartMatch();

    public void RequestReturnToMenu()
    {
        if (transitionQueued)
            return;
        transitionQueued = true;
        StartCoroutine(CoReturnToMenu());
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

    /// <summary>清掉对局物件，不卸载场景。</summary>
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
            Destroy(GroundItemManager.Instance.gameObject);
        }

        if (HazardManager.Instance != null)
        {
            HazardManager.Instance.ClearAll();
            Destroy(HazardManager.Instance.gameObject);
        }

        if (GridManager.Instance != null)
        {
            GridManager.Instance.ClearAllOccupants();
            GridManager.Instance.InitTiles();
        }

        TurnManager.Instance?.ResetMatch();
        GameManager.Instance?.ClearMatch();

        if (PlayerInputController.Instance != null && !MatchConfig.IsLeanRuntime)
            PlayerInputController.Instance.enabled = true;
    }

    /// <summary>LogicSim 局间清理（保留 Bootstrap 与系统组件）。</summary>
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
            DestroyImmediate(GroundItemManager.Instance.gameObject);
        }

        if (HazardManager.Instance != null)
        {
            HazardManager.Instance.ClearAll();
            DestroyImmediate(HazardManager.Instance.gameObject);
        }

        if (GridManager.Instance != null)
        {
            GridManager.Instance.ClearAllOccupants();
            // 保留 GridManager 组件，仅重置格子
            GridManager.Instance.InitTiles();
        }

        TurnManager.Instance?.ResetMatch();
        GameManager.Instance?.ClearMatch();
    }

    public void StartHotseat()
    {
        MatchConfig.SetHotseat();
        StartMatch();
    }

    public void StartAiBattle(RoleType humanRole)
    {
        MatchConfig.SetAiBattle(humanRole);
        StartMatch();
    }

    public void StartAdminMode(RoleType humanRole)
    {
        MatchConfig.SetAdminMode(humanRole);
        StartMatch();
    }

    /// <summary>LogicSim：全 AI 瘦开局（调用前须 MatchConfig.SetLogicSim）。</summary>
    public void StartLogicSimMatch()
    {
        if (!MatchConfig.IsLogicSim)
            MatchConfig.SetLogicSim();
        matchRunning = false;
        StartMatch();
    }

    /// <summary>RL 训练/评估：瘦开局（调用前须 MatchConfig.SetRlTraining）。</summary>
    public void StartRlTrainingMatch()
    {
        if (!MatchConfig.IsRlTraining)
            MatchConfig.SetRlTraining();
        matchRunning = false;
        StartMatch();
    }

    /// <summary>局间清理后允许再次 StartMatch（LogicSim / RL 连跑）。</summary>
    public void ResetMatchRunningFlag()
    {
        matchRunning = false;
    }

    /// <summary>兼容旧入口。</summary>
    public void StartAiBattle() => MainMenuUI.Instance?.OnAiBattleClicked();

    public void StartOnline() => MainMenuUI.Instance?.OnOnlineClicked();

    [ContextMenu("Start Hotseat")]
    public void StartDemo() => StartHotseat();

    private void StartMatch()
    {
        if (matchRunning)
            return;
        matchRunning = true;

        MainMenuUI.Instance?.Hide();

        bool lean = MatchConfig.IsLeanRuntime;
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
        GameManager.Instance.RegisterUnits(units);
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
            // RL 训练由 RlMatchController 接管，避免与启发式泵冲突
            bool useHeuristicAi = MatchConfig.IsAiBattle && !MatchConfig.IsRlTraining;
            AiController.Instance.enabled = useHeuristicAi;
            if (useHeuristicAi)
                AiController.Instance.EnsureSubscribed();
        }
    }

    private void EnsureSystems(bool leanLogicSim = false)
    {
        if (GridManager.Instance == null)
        {
            var go = new GameObject("GridManager");
            go.AddComponent<GridManager>();
        }

        if (GameManager.Instance == null)
            gameObject.AddComponent<GameManager>();
        if (TurnManager.Instance == null)
            gameObject.AddComponent<TurnManager>();
        if (PlayerInputController.Instance == null)
            gameObject.AddComponent<PlayerInputController>();
        if (GameUI.Instance == null && !leanLogicSim)
            gameObject.AddComponent<GameUI>();
        if (AiController.Instance == null)
            gameObject.AddComponent<AiController>();
        if (MatchConfig.IsRlTraining && RlMatchController.Instance == null)
            gameObject.AddComponent<RlMatchController>();

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
            lootGo.AddComponent<GroundItemManager>();
        }

        if (HazardManager.Instance == null)
        {
            var hz = new GameObject("Hazards");
            hz.AddComponent<HazardManager>();
        }

        if (DeckManager.Instance == null)
            gameObject.AddComponent<DeckManager>();
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
        var starts = new[]
        {
            new Vector2Int(2, 2),
            new Vector2Int(15, 2),
            new Vector2Int(2, 15),
            new Vector2Int(15, 15)
        };
        var roles = new[]
        {
            RoleType.Elephant,
            RoleType.Human,
            RoleType.Monkey,
            RoleType.Cat
        };

        var root = new GameObject("Units").transform;

        for (int i = 0; i < 4; i++)
        {
            var go = new GameObject(RoleInfo.GetDisplayName(roles[i]));
            go.transform.SetParent(root, false);
            var unit = go.AddComponent<UnitActor>();
            unit.Setup(roles[i], starts[i]);
            list.Add(unit);
        }

        return list;
    }
}
