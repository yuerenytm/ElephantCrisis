using System.Collections.Generic;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine;

/// <summary>单角色 ML-Agents Agent（共享 BehaviorName）。</summary>
public class RlAgent : Agent
{
    public UnitActor Unit { get; private set; }
    public RlMatchController Match { get; set; }

    private readonly float[] obsScratch = new float[RlObservationBuilder.Size];
    private bool awaitingAction;
    private bool lastActionEndedTurn;
    private float episodeReturn;

    public bool AwaitingAction => awaitingAction;
    public bool LastActionEndedTurn => lastActionEndedTurn;
    public float EpisodeReturn => episodeReturn;

    public void Bind(UnitActor unit, RlMatchController match)
    {
        Unit = unit;
        Match = match;
        episodeReturn = 0f;
        MaxStep = 10000;
    }

    public override void OnEpisodeBegin()
    {
        awaitingAction = false;
        lastActionEndedTurn = false;
        episodeReturn = 0f;
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        var units = Match != null ? Match.Units : GameManager.Instance?.Units;
        if (Unit == null)
        {
            for (int i = 0; i < RlObservationBuilder.Size; i++)
                sensor.AddObservation(0f);
            return;
        }
        RlObservationBuilder.Write(Unit, units as List<UnitActor> ?? new List<UnitActor>(), obsScratch);
        for (int i = 0; i < RlObservationBuilder.Size; i++)
            sensor.AddObservation(obsScratch[i]);
    }

    public override void WriteDiscreteActionMask(IDiscreteActionMask actionMask)
    {
        var units = Match != null ? Match.Units : GameManager.Instance?.Units;
        RlActionMask.Write(Unit, units as List<UnitActor>, actionMask);
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        awaitingAction = false;
        lastActionEndedTurn = false;
        if (Unit == null || Unit.IsDead || Match == null)
            return;

        var disc = actions.DiscreteActions;
        int op = disc.Length > 0 ? disc[0] : 0;
        int dx = disc.Length > 1 ? disc[1] : RlActionSpace.AxisRadius;
        int dy = disc.Length > 2 ? disc[2] : RlActionSpace.AxisRadius;
        int tgt = disc.Length > 3 ? disc[3] : 0;

        var result = RlActionExecutor.Execute(Unit, Match.Units, op, dx, dy, tgt);
        RlTrainingStats.NoteDecision();
        lastActionEndedTurn = result.EndedTurn;
        if (Mathf.Abs(result.ShapingReward) > 1e-6f)
        {
            AddReward(result.ShapingReward);
            episodeReturn += result.ShapingReward;
        }

        Match.NotifyAgentStepped(this, result);
    }

    public void RequestMicroDecision()
    {
        awaitingAction = true;
        lastActionEndedTurn = false;
        RequestDecision();
    }

    public void ApplyTerminalReward(float reward)
    {
        AddReward(reward);
        episodeReturn += reward;
        EndEpisode();
    }

    public void AddShapingReward(float reward)
    {
        if (Mathf.Abs(reward) < 1e-8f)
            return;
        AddReward(reward);
        episodeReturn += reward;
    }
}
