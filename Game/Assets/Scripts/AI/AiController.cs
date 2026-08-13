using System.Collections;
using UnityEngine;

/// <summary>AI 对战：在非人类角色回合用启发式策略自动行动。</summary>
public class AiController : MonoBehaviour
{
    public static AiController Instance;

    [SerializeField] private float stepDelay = 0.4f;
    [SerializeField] private float turnStartDelay = 0.55f;

    private Coroutine running;
    private UnitActor actingUnit;
    private bool logicSimPumping;

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
        Unsubscribe();
    }

    private void OnEnable()
    {
        Subscribe();
    }

    private void OnDisable()
    {
        StopAi();
        Unsubscribe();
    }

    private void Subscribe()
    {
        if (TurnManager.Instance == null)
            return;
        TurnManager.Instance.OnTurnChanged -= OnTurnChanged;
        TurnManager.Instance.OnTurnChanged += OnTurnChanged;
    }

    private void Unsubscribe()
    {
        if (TurnManager.Instance != null)
            TurnManager.Instance.OnTurnChanged -= OnTurnChanged;
    }

    public void EnsureSubscribed()
    {
        Subscribe();
        OnTurnChanged();
    }

    private void OnTurnChanged()
    {
        StopAi();

        if (!MatchConfig.IsAiBattle)
            return;

        var turn = TurnManager.Instance;
        if (turn == null || turn.Phase == TurnPhase.GameOver)
            return;

        var unit = turn.CurrentUnit;
        if (unit == null || unit.IsDead)
            return;
        if (MatchConfig.IsHumanControlled(unit))
            return;

        if (MatchConfig.IsLogicSim)
        {
            // 避免 RequestEndTurn → BeginTurnFor → OnTurnChanged 递归爆栈：外层泵推进整局
            if (logicSimPumping)
                return;
            logicSimPumping = true;
            try
            {
                PumpLogicSim();
            }
            finally
            {
                logicSimPumping = false;
            }
            return;
        }

        actingUnit = unit;
        running = StartCoroutine(CoPlayTurn(unit));
    }

    private void PumpLogicSim()
    {
        int maxFull = LogicSimRunner.GetIntArg("-maxRounds", GameRulesConfig.MaxFullRounds);
        int guard = 0;
        while (guard++ < 50000)
        {
            var turn = TurnManager.Instance;
            if (turn == null || turn.Phase == TurnPhase.GameOver)
                break;
            if (GameManager.Instance != null && GameManager.Instance.IsGameOver)
                break;

            int full = Mathf.Max(0, turn.RoundNumber - 1);
            if (full >= maxFull)
            {
                GameManager.Instance?.ForceEndForLogicSim("max_rounds");
                break;
            }

            var unit = turn.CurrentUnit;
            if (unit == null || unit.IsDead)
                break;
            if (MatchConfig.IsHumanControlled(unit))
                break;

            PlayTurnSync(unit);
        }
    }

    private void StopAi()
    {
        if (running != null)
        {
            StopCoroutine(running);
            running = null;
        }
        actingUnit = null;
    }

    /// <summary>LogicSim：无延时同步推进当前单位一整回合并结束行动。</summary>
    private void PlayTurnSync(UnitActor unit)
    {
        actingUnit = unit;
        int safety = 0;
        while (safety++ < 24)
        {
            var turn = TurnManager.Instance;
            if (turn == null || turn.Phase == TurnPhase.GameOver)
                break;
            if (turn.CurrentUnit != unit || unit.IsDead)
                break;
            if (GameManager.Instance != null && GameManager.Instance.IsGameOver)
                break;

            if (!SimpleHeuristicAi.TryActOnce(unit))
                break;

            if (unit.IsDying && !ActionService.HasGroundPotionAt(unit))
                break;
        }

        var endTurn = TurnManager.Instance;
        if (endTurn != null &&
            endTurn.Phase != TurnPhase.GameOver &&
            endTurn.CurrentUnit == unit &&
            !(GameManager.Instance?.IsGameOver ?? false))
        {
            endTurn.RequestEndTurn();
        }

        actingUnit = null;
    }

    private IEnumerator CoPlayTurn(UnitActor unit)
    {
        yield return new WaitForSeconds(turnStartDelay);

        int safety = 0;
        while (safety++ < 12)
        {
            var turn = TurnManager.Instance;
            if (turn == null || turn.Phase == TurnPhase.GameOver)
                yield break;
            if (turn.CurrentUnit != unit || unit.IsDead)
                yield break;
            if (GameManager.Instance != null && GameManager.Instance.IsGameOver)
                yield break;

            bool acted = SimpleHeuristicAi.TryActOnce(unit);
            GameUI.Instance?.RequestRefresh();
            PlayerInputController.Instance?.RefreshHints();

            if (!acted)
                break;

            if (unit.IsDying && !ActionService.HasGroundPotionAt(unit))
                break;

            yield return new WaitForSeconds(stepDelay);
        }

        var endTurn = TurnManager.Instance;
        if (endTurn != null &&
            endTurn.Phase != TurnPhase.GameOver &&
            endTurn.CurrentUnit == unit &&
            !(GameManager.Instance?.IsGameOver ?? false))
        {
            endTurn.RequestEndTurn();
            GameUI.Instance?.RequestRefresh();
        }

        running = null;
        actingUnit = null;
    }
}
