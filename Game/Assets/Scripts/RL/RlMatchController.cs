using System.Collections;
using System.Collections.Generic;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Policies;
using UnityEngine;

/// <summary>
/// RL 对局驱动：lean 开局、微操作步进、启发式座位、终局奖励与训练埋点。
/// </summary>
public class RlMatchController : MonoBehaviour
{
    public static RlMatchController Instance { get; private set; }

    public enum Curriculum
    {
        Bootstrap,
        SelfPlay
    }

    public List<UnitActor> Units { get; private set; } = new List<UnitActor>();
    public Curriculum Mode { get; private set; } = Curriculum.Bootstrap;
    public RoleType LearningRole { get; private set; } = RoleType.Human;

    private readonly Dictionary<RoleType, RlAgent> agents = new Dictionary<RoleType, RlAgent>();
    private readonly RlEpisodeProbe probe = new RlEpisodeProbe();
    private Coroutine loop;
    private bool episodeActive;
    private float stepTimeout = 5f;

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
        StopLoop();
    }

    public void Configure(Curriculum curriculum, RoleType learningRole, int maxRoundsIgnored = 0)
    {
        Mode = curriculum;
        LearningRole = learningRole;
        // 游戏无回合上限：熔岩缩圈直至决出胜负/平局；maxRounds 参数忽略
    }

    public bool IsRlControlled(UnitActor unit)
    {
        if (unit == null || !MatchConfig.IsRlTraining)
            return false;
        if (Mode == Curriculum.SelfPlay)
            return true;
        return unit.Role == LearningRole;
    }

    public bool TryGetAgent(RoleType role, out RlAgent agent)
    {
        return agents.TryGetValue(role, out agent);
    }

    public void BeginMatchSession()
    {
        StopLoop();
        BindAgentsFromUnits();
        episodeActive = true;
        RlTrainingStats.EnsureOutDir();
        loop = StartCoroutine(CoRunEpisodes());
    }

    public void StopLoop()
    {
        if (loop != null)
        {
            StopCoroutine(loop);
            loop = null;
        }
        episodeActive = false;
    }

    public void NotifyAgentStepped(RlAgent agent, RlActionExecutor.Result result)
    {
        probe.AfterWorldStep(Units, this);
    }

    private void BindAgentsFromUnits()
    {
        Units = GameManager.Instance != null
            ? new List<UnitActor>(GameManager.Instance.Units)
            : new List<UnitActor>();

        foreach (Transform child in transform)
        {
            if (child.GetComponent<RlAgent>() != null)
                Destroy(child.gameObject);
        }
        agents.Clear();

        foreach (var u in Units)
        {
            if (u == null)
                continue;
            if (!IsRlControlled(u) && Mode == Curriculum.Bootstrap)
                continue;

            var go = new GameObject($"RlAgent_{u.Role}");
            go.transform.SetParent(transform, false);
            var bp = go.AddComponent<BehaviorParameters>();
            bp.BehaviorName = RlActionSpace.BehaviorName;
            bp.BrainParameters.VectorObservationSize = RlObservationBuilder.Size;
            bp.BrainParameters.NumStackedVectorObservations = 1;
            bp.BrainParameters.ActionSpec = ActionSpec.MakeDiscrete(
                RlActionSpace.OpCount,
                RlActionSpace.AxisCount,
                RlActionSpace.AxisCount,
                RlActionSpace.TargetCount);
            bp.BehaviorType = BehaviorType.Default;

            if (Mode == Curriculum.SelfPlay
                && RlLeagueSampler.ShouldUseGhost(u.Role)
                && RlLeagueSampler.TryAssignModel(bp))
            {
                bp.BehaviorType = BehaviorType.InferenceOnly;
            }

            var agent = go.AddComponent<RlAgent>();
            agent.Bind(u, this);
            agents[u.Role] = agent;
        }

        probe.Begin(Units);
    }

    private IEnumerator CoRunEpisodes()
    {
        while (MatchConfig.IsRlTraining && episodeActive)
        {
            yield return CoPlayOneMatch();
            GameBootstrap.Instance?.TeardownForLogicSim();
            yield return null;
            MatchConfig.SetRlTraining();
            GameBootstrap.Instance?.ResetMatchRunningFlag();
            GameBootstrap.Instance?.StartRlTrainingMatch();
            yield return null;
            BindAgentsFromUnits();
            foreach (var kv in agents)
                kv.Value.OnEpisodeBegin();
        }
    }

    private IEnumerator CoPlayOneMatch()
    {
        int guard = 0;
        while (guard++ < 100000)
        {
            var turn = TurnManager.Instance;
            var gm = GameManager.Instance;
            if (turn == null || gm == null)
                yield break;
            if (turn.Phase == TurnPhase.GameOver || gm.IsGameOver)
                break;

            var unit = turn.CurrentUnit;
            if (unit == null || unit.IsDead)
                yield break;

            if (IsRlControlled(unit) && agents.TryGetValue(unit.Role, out var agent))
                yield return CoPlayRlTurn(unit, agent);
            else
                PlayHeuristicTurn(unit);

            probe.AfterWorldStep(Units, this);
            yield return null;
        }

        SettleTerminalRewardsAndStats();
    }

    private IEnumerator CoPlayRlTurn(UnitActor unit, RlAgent agent)
    {
        int safety = 0;
        while (safety++ < 24)
        {
            var turn = TurnManager.Instance;
            if (turn == null || turn.Phase == TurnPhase.GameOver)
                yield break;
            if (turn.CurrentUnit != unit || unit.IsDead)
                yield break;
            if (GameManager.Instance != null && GameManager.Instance.IsGameOver)
                yield break;

            agent.RequestMicroDecision();
            float t0 = Time.realtimeSinceStartup;
            while (agent.AwaitingAction && Time.realtimeSinceStartup - t0 < stepTimeout)
            {
                if (Academy.IsInitialized && !Academy.Instance.IsCommunicatorOn)
                    Academy.Instance.EnvironmentStep();
                yield return null;
            }

            if (agent.AwaitingAction)
            {
                turn.RequestEndTurn();
                yield break;
            }

            if (agent.LastActionEndedTurn)
                yield break;

            if (unit.IsDying && !ActionService.HasGroundPotionAt(unit))
            {
                turn.RequestEndTurn();
                yield break;
            }
        }

        var end = TurnManager.Instance;
        if (end != null && end.CurrentUnit == unit && end.Phase != TurnPhase.GameOver)
            end.RequestEndTurn();
    }

    private void PlayHeuristicTurn(UnitActor unit)
    {
        int safety = 0;
        while (safety++ < 24)
        {
            var turn = TurnManager.Instance;
            if (turn == null || turn.Phase == TurnPhase.GameOver)
                return;
            if (turn.CurrentUnit != unit || unit.IsDead)
                return;
            if (GameManager.Instance != null && GameManager.Instance.IsGameOver)
                return;

            if (!SimpleHeuristicAi.TryActOnce(unit))
                break;
            probe.AfterWorldStep(Units, this);
            if (unit.IsDying && !ActionService.HasGroundPotionAt(unit))
                break;
        }

        var end = TurnManager.Instance;
        if (end != null &&
            end.Phase != TurnPhase.GameOver &&
            end.CurrentUnit == unit &&
            !(GameManager.Instance?.IsGameOver ?? false))
        {
            end.RequestEndTurn();
        }
    }

    private void SettleTerminalRewardsAndStats()
    {
        var gm = GameManager.Instance;
        RoleType? winner = gm != null ? gm.WinnerRole : null;
        string reason = gm != null ? gm.EndReason : "unknown";
        int full = TurnManager.Instance != null
            ? Mathf.Max(0, TurnManager.Instance.RoundNumber - 1)
            : 0;

        foreach (var kv in agents)
        {
            var unit = FindUnit(kv.Key);
            float r = TerminalRewardFor(unit, winner, reason);
            kv.Value.ApplyTerminalReward(r);
        }

        string winKey = winner.HasValue ? winner.Value.ToString().ToLowerInvariant() : "none";
        RlTrainingStats.RecordMatch(
            winKey,
            reason ?? "unknown",
            full,
            probe.LeaderDeclarationsThisMatch,
            Units,
            Mode.ToString(),
            LearningRole.ToString().ToLowerInvariant());
    }

    private static float TerminalRewardFor(UnitActor unit, RoleType? winner, string reason)
    {
        if (winner.HasValue && unit != null && unit.Role == winner.Value)
            return RlReward.Win;

        // 真正平局（如无人存活）；游戏无回合超时强终
        if (reason == "draw" || !winner.HasValue)
            return RlReward.Draw;

        if (unit != null && unit.IsDead)
            return RlReward.Dead;

        return RlReward.LoseAlive;
    }

    private UnitActor FindUnit(RoleType role)
    {
        if (Units == null)
            return null;
        foreach (var u in Units)
        {
            if (u != null && u.Role == role)
                return u;
        }
        return null;
    }
}
