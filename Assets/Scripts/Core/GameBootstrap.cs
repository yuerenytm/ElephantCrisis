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

        if (mode == GameMode.AiBattle)
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

        if (PlayerInputController.Instance != null)
            PlayerInputController.Instance.enabled = true;
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

        EnsureSystems();
        SetupCamera();

        var grid = GridManager.Instance;
        grid.InitTiles();
        grid.ClearAllOccupants();

        var map = MapVisual.Instance;
        map.Build(grid);
        map.RefreshAllTiles(grid);

        var units = SpawnUnits();
        GameManager.Instance.RegisterUnits(units);
        DeckManager.Instance.BuildDemoDeck();

        GameUI.Instance.Build();
        TurnManager.Instance.Setup(units);
        GameUI.Instance.ForceRefresh();
        PlayerInputController.Instance.RefreshHints();

        if (AiController.Instance != null)
        {
            AiController.Instance.enabled = MatchConfig.IsAiBattle;
            if (MatchConfig.IsAiBattle)
                AiController.Instance.EnsureSubscribed();
        }
    }

    private void EnsureSystems()
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
        if (GameUI.Instance == null)
            gameObject.AddComponent<GameUI>();
        if (AiController.Instance == null)
            gameObject.AddComponent<AiController>();

        if (MapVisual.Instance == null)
        {
            var mapGo = new GameObject("MapVisual");
            mapGo.AddComponent<MapVisual>();
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

        cam.orthographic = true;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.08f, 0.1f, 0.09f);
        float w = GridManager.Instance.gridWidth * GridManager.Instance.cellSize;
        float h = GridManager.Instance.gridHeight * GridManager.Instance.cellSize;
        cam.transform.position = new Vector3(w * 0.42f, h * 0.5f, -10f);
        cam.orthographicSize = h * 0.58f;
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
