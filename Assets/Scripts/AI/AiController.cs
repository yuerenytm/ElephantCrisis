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

        actingUnit = unit;
        running = StartCoroutine(CoPlayTurn(unit));
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

            // 濒死治疗后若仍濒死且无药，结束
            if (unit.IsDying && unit.Inventory.CountOf(ItemKind.SmallPotion) <= 0)
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
