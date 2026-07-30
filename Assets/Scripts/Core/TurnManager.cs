using System;
using System.Collections.Generic;
using UnityEngine;

public enum TurnPhase
{
    WaitingAction,
    SelectingBombTarget,
    SelectingBananaTarget,
    SelectingDiscardTarget,
    SelectingShootTarget,
    SelectingReinforce,
    SelectingAmmo,
    SelectingTimedBombDelay,
    SelectingFlameDirection,
    SelectingPickup,
    MandatoryDiscard,
    GameOver
}

public class TurnManager : MonoBehaviour
{
    public static TurnManager Instance;

    public IReadOnlyList<UnitActor> Units => units;
    public UnitActor CurrentUnit { get; private set; }
    public int CurrentIndex { get; private set; }
    public int RoundNumber { get; private set; } = 1;
    public int GlobalTurnCount { get; private set; }
    public TurnPhase Phase { get; private set; } = TurnPhase.WaitingAction;

    public bool HasMoved { get; private set; }
    /// <summary>本行动回合是否已使用过普通近战攻击。</summary>
    public bool HasMeleeAttacked { get; private set; }
    /// <summary>结束回合时因超重进入强制弃置流程。</summary>
    public bool AwaitingCapacityTrim { get; private set; }

    public int PendingItemIndex { get; private set; } = -1;
    public ItemKind PendingAmmoKind { get; private set; } = ItemKind.Arrow;

    public event Action OnTurnChanged;
    public event Action OnStateChanged;
    public event Action<string> OnLog;

    private readonly List<UnitActor> units = new List<UnitActor>();

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void Awake()
    {
        Instance = this;
    }

    public void ResetMatch()
    {
        units.Clear();
        CurrentUnit = null;
        CurrentIndex = 0;
        RoundNumber = 1;
        GlobalTurnCount = 0;
        Phase = TurnPhase.WaitingAction;
        PendingItemIndex = -1;
        HasMoved = false;
        HasMeleeAttacked = false;
        AwaitingCapacityTrim = false;
    }

    public void Setup(List<UnitActor> allUnits)
    {
        units.Clear();
        units.AddRange(allUnits);
        CurrentIndex = 0;
        RoundNumber = 1;
        GlobalTurnCount = 0;
        Phase = TurnPhase.WaitingAction;
        PendingItemIndex = -1;
        HasMoved = false;
        HasMeleeAttacked = false;
        AwaitingCapacityTrim = false;
        BeginTurnFor(units[0], firstTurn: true);
    }

    public void BeginTurnFor(UnitActor unit, bool firstTurn = false)
    {
        CurrentUnit = unit;
        HasMoved = false;
        HasMeleeAttacked = false;
        AwaitingCapacityTrim = false;
        Phase = TurnPhase.WaitingAction;
        PendingItemIndex = -1;

        if (!firstTurn)
            GlobalTurnCount++;

        HazardManager.Instance?.RefreshHazardVisibility();

        if (unit != null && !unit.IsDead)
        {
            HazardManager.Instance?.ResolveBananaAtTurnStart(unit);
            if (unit.IsDead)
            {
                OnStateChanged?.Invoke();
                GameManager.Instance?.CheckWinConditions();
                if (Phase != TurnPhase.GameOver)
                    EndTurn();
                return;
            }

            HazardManager.Instance?.ResolveMineAt(unit);
            if (unit.IsDead)
            {
                OnStateChanged?.Invoke();
                GameManager.Instance?.CheckWinConditions();
                if (Phase != TurnPhase.GameOver)
                    EndTurn();
                return;
            }

            unit.TickBurningOnTurnStart();
            if (unit.IsDead)
            {
                OnStateChanged?.Invoke();
                GameManager.Instance?.CheckWinConditions();
                if (Phase != TurnPhase.GameOver)
                    EndTurn();
                return;
            }

            if (GridManager.Instance.GetTileType(unit.Cell) == TileType.Lava)
            {
                int dealt = unit.TakeDamage(9, trueDamage: true);
                Log($"{RoleInfo.GetDisplayName(unit.Role)} 站在熔岩上，受到 {dealt} 点真实伤害");
                if (unit.IsDead)
                {
                    OnStateChanged?.Invoke();
                    GameManager.Instance?.CheckWinConditions();
                    if (Phase != TurnPhase.GameOver)
                        EndTurn();
                    return;
                }
            }

            DeckManager.Instance?.DrawFor(unit);
            Log($"—— 第{RoundNumber}轮 · {RoleInfo.GetDisplayName(unit.Role)} 的回合 ——");
        }

        RefreshSelectionVisuals();
        OnTurnChanged?.Invoke();
        OnStateChanged?.Invoke();
    }

    public void NotifyActionDone() => OnStateChanged?.Invoke();

    public void MarkMoved()
    {
        HasMoved = true;
        NotifyActionDone();
    }

    public void MarkMeleeAttacked()
    {
        HasMeleeAttacked = true;
        NotifyActionDone();
    }

    public void EnterBombTargeting(int itemIndex)
    {
        Phase = TurnPhase.SelectingBombTarget;
        PendingItemIndex = itemIndex;
        NotifyActionDone();
    }

    public void EnterBananaTargeting(int itemIndex)
    {
        Phase = TurnPhase.SelectingBananaTarget;
        PendingItemIndex = itemIndex;
        NotifyActionDone();
    }

    public void EnterDiscardTargeting(int itemIndex)
    {
        Phase = TurnPhase.SelectingDiscardTarget;
        PendingItemIndex = itemIndex;
        NotifyActionDone();
    }

    public void EnterShootTargeting(int itemIndex, ItemKind ammo)
    {
        Phase = TurnPhase.SelectingShootTarget;
        PendingItemIndex = itemIndex;
        PendingAmmoKind = ammo;
        NotifyActionDone();
    }

    public void EnterAmmoChoice(int weaponIndex)
    {
        Phase = TurnPhase.SelectingAmmo;
        PendingItemIndex = weaponIndex;
        NotifyActionDone();
    }

    public void EnterReinforceChoice(int itemIndex)
    {
        Phase = TurnPhase.SelectingReinforce;
        PendingItemIndex = itemIndex;
        NotifyActionDone();
    }

    public void EnterTimedBombDelay(int itemIndex)
    {
        Phase = TurnPhase.SelectingTimedBombDelay;
        PendingItemIndex = itemIndex;
        NotifyActionDone();
    }

    public void EnterFlameDirection(int itemIndex)
    {
        Phase = TurnPhase.SelectingFlameDirection;
        PendingItemIndex = itemIndex;
        NotifyActionDone();
    }

    public void EnterPickupMode()
    {
        Phase = TurnPhase.SelectingPickup;
        PendingItemIndex = -1;
        NotifyActionDone();
    }

    public void CancelTargeting()
    {
        PendingItemIndex = -1;
        if (AwaitingCapacityTrim)
        {
            Phase = TurnPhase.MandatoryDiscard;
            NotifyActionDone();
            return;
        }
        Phase = TurnPhase.WaitingAction;
        NotifyActionDone();
    }

    /// <summary>玩家/系统请求结束回合：超重时进入强制弃置，清完后才真正换手。</summary>
    public bool RequestEndTurn()
    {
        if (Phase == TurnPhase.GameOver)
            return false;

        var unit = CurrentUnit;
        if (unit != null && !unit.IsDead && unit.Inventory != null && unit.Inventory.IsOverCapacity)
        {
            if (!MatchConfig.IsHumanControlled(unit))
            {
                ActionService.AutoDiscardToCapacity(unit);
                AwaitingCapacityTrim = false;
                EndTurn();
                return true;
            }

            AwaitingCapacityTrim = true;
            Phase = TurnPhase.MandatoryDiscard;
            PendingItemIndex = -1;
            LogFor(unit,
                $"背包超重（{unit.Inventory.UsedWeight:0.##}/{unit.Inventory.Capacity:0.##}），请弃置物品至容量以内");
            NotifyActionDone();
            return false;
        }

        AwaitingCapacityTrim = false;
        EndTurn();
        return true;
    }

    /// <summary>强制弃置成功降到容量内后继续结束回合。</summary>
    public void ContinueEndTurnAfterDiscard()
    {
        if (!AwaitingCapacityTrim)
            return;
        var unit = CurrentUnit;
        if (unit == null || unit.Inventory == null || unit.Inventory.IsOverCapacity)
            return;
        LogFor(unit, "背包已恢复至容量以内，结束回合");
        AwaitingCapacityTrim = false;
        EndTurn();
    }

    public void EndTurn()
    {
        if (Phase == TurnPhase.GameOver)
            return;

        AwaitingCapacityTrim = false;

        GameManager.Instance?.CheckWinConditions();
        if (Phase == TurnPhase.GameOver)
            return;

        int start = CurrentIndex;
        bool advancedFullRound = false;
        do
        {
            CurrentIndex = (CurrentIndex + 1) % units.Count;
            if (CurrentIndex == 0)
            {
                RoundNumber++;
                advancedFullRound = true;

                HazardManager.Instance?.TickTimedBombsOnFullRound();

                foreach (var u in units)
                {
                    u?.TickDyingOnFullRound();
                    u?.TickStatusOnFullRound();
                }

                if (RoundNumber % 5 == 0)
                    GameManager.Instance?.ApplyLavaShrink();
            }
        }
        while (units[CurrentIndex].IsDead && CurrentIndex != start);

        if (advancedFullRound)
            GameManager.Instance?.CheckWinConditions();

        if (Phase == TurnPhase.GameOver)
            return;

        if (units[CurrentIndex].IsDead)
        {
            GameManager.Instance?.CheckWinConditions();
            return;
        }

        BeginTurnFor(units[CurrentIndex]);
    }

    public void SetGameOver()
    {
        Phase = TurnPhase.GameOver;
        CurrentUnit = null;
        PendingItemIndex = -1;
        AwaitingCapacityTrim = false;
        RefreshSelectionVisuals();
        OnTurnChanged?.Invoke();
        OnStateChanged?.Invoke();
    }

    private void RefreshSelectionVisuals()
    {
        foreach (var u in units)
        {
            if (u != null)
                u.SetSelected(u == CurrentUnit && !u.IsDead);
        }
    }

    public void Log(string msg)
    {
        Debug.Log(msg);
        OnLog?.Invoke(msg);
    }

    /// <summary>
    /// 角色私密行动战报。热座全员可见；AI 对战仅当 subject 是本地玩家操控时写入战报。
    /// </summary>
    public void LogFor(UnitActor subject, string msg)
    {
        Debug.Log(msg);
        if (MatchConfig.IsAiBattle && !MatchConfig.IsHumanControlled(subject))
            return;
        OnLog?.Invoke(msg);
    }
}
