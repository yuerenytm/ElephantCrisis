using System;
using System.Collections.Generic;
using UnityEngine;

public enum TurnPhase
{
    WaitingAction,
    SelectingBombTarget,
    SelectingBananaTarget,
    SelectingMineTarget,
    SelectingDiscardTarget,
    SelectingShootTarget,
    SelectingReinforce,
    SelectingAmmo,
    SelectingTimedBombDelay,
    SelectingFlameDirection,
    SelectingMotorcycleRam,
    SelectingHookTarget,
    SelectingPickup,
    SelectingSkillReinforce,
    SelectingMonkeySkillMode,
    SelectingMonkeyMarkTarget,
    SelectingMonkeyMarkItem,
    SelectingMonkeyStealTarget,
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
    /// <summary>本行动是否已使用过普通近战攻击。</summary>
    public bool HasMeleeAttacked { get; private set; }
    /// <summary>结束回合时因超重进入强制弃置流程。</summary>
    public bool AwaitingCapacityTrim { get; private set; }

    public int PendingItemIndex { get; private set; } = -1;
    public ItemKind PendingAmmoKind { get; private set; } = ItemKind.Arrow;
    public UnitActor PendingSkillTargetUnit { get; set; }
    /// <summary>勾爪抢夺选物（复用 SelectingMonkeyMarkItem）。</summary>
    public bool PendingHookSteal { get; set; }

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
        PendingSkillTargetUnit = null;
        StealMarks.Reset();
        InventoryItem.ResetIdCounter();
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
        WeatherService.ResetForMatch(1);
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
            // 1) 着火等行动开始状态伤害优先于一切（含熔岩）
            unit.TickBurningOnTurnStart();
            if (unit.IsDead)
            {
                OnStateChanged?.Invoke();
                GameManager.Instance?.CheckWinConditions();
                if (Phase != TurnPhase.GameOver)
                    EndTurn();
                return;
            }

            // 1b) 领袖行动开始真伤（着火之后）
            unit.TickLeaderOnTurnStart();
            if (unit.IsDead)
            {
                OnStateChanged?.Invoke();
                GameManager.Instance?.CheckWinConditions();
                if (Phase != TurnPhase.GameOver)
                    EndTurn();
                return;
            }

            // 2) 解除以本人为施加者、到期的非 DoT 状态（中毒等；着火由上一步自行倒数）
            foreach (var u in units)
            {
                if (u != null && !u.IsDead)
                    u.TickStatusesFromApplier(unit.Role);
            }

            // 3) 熔岩：真伤后对受伤者施加着火（着火本身仍为法伤）
            if (GridManager.Instance.GetTileType(unit.Cell) == TileType.Lava)
            {
                int dealt = unit.TakeDamage(20, trueDamage: true);
                Log($"{RoleInfo.GetDisplayName(unit.Role)} 站在熔岩上，受到 {dealt} 点真伤");
                if (dealt > 0 && !unit.IsDead)
                    unit.ApplyStatus(StatusType.Burning, 1, unit);
                if (unit.IsDead)
                {
                    OnStateChanged?.Invoke();
                    GameManager.Instance?.CheckWinConditions();
                    if (Phase != TurnPhase.GameOver)
                        EndTurn();
                    return;
                }
            }

            // 3b) 冰地：20% 跌倒
            unit.TickIceTerrainOnTurnStart();

            // 3c) 晕眩：跳过本次行动
            if (unit.HasStatus(StatusType.Stun))
            {
                unit.ClearStatus(StatusType.Stun);
                Log($"{RoleInfo.GetDisplayName(unit.Role)} 处于【晕眩】，跳过行动");
                RefreshSelectionVisuals();
                OnTurnChanged?.Invoke();
                OnStateChanged?.Invoke();
                if (Phase != TurnPhase.GameOver)
                    EndTurn();
                return;
            }

            unit.ResetCrossbowActionFlags();
            DeckManager.Instance?.DrawFor(unit);
            Log($"—— 第{RoundNumber}轮 · {GameClock.GetStatusLine(RoundNumber)} · {WeatherService.GetDisplayName()} · {RoleInfo.GetDisplayName(unit.Role)} 的行动 ——");
            StealthService.CheckBreakFor(unit);
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

    public void EnterMineTargeting(int itemIndex)
    {
        Phase = TurnPhase.SelectingMineTarget;
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

    public void EnterSkillReinforce()
    {
        Phase = TurnPhase.SelectingSkillReinforce;
        PendingItemIndex = -1;
        NotifyActionDone();
    }

    public void EnterMonkeySkillChoice()
    {
        Phase = TurnPhase.SelectingMonkeySkillMode;
        PendingSkillTargetUnit = null;
        NotifyActionDone();
    }

    public void EnterMonkeyMarkTarget()
    {
        Phase = TurnPhase.SelectingMonkeyMarkTarget;
        PendingSkillTargetUnit = null;
        NotifyActionDone();
    }

    public void EnterMonkeyMarkItem()
    {
        Phase = TurnPhase.SelectingMonkeyMarkItem;
        NotifyActionDone();
    }

    public void EnterMonkeyStealTarget()
    {
        Phase = TurnPhase.SelectingMonkeyStealTarget;
        PendingSkillTargetUnit = null;
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

    public void EnterMotorcycleRam(int itemIndex)
    {
        Phase = TurnPhase.SelectingMotorcycleRam;
        PendingItemIndex = itemIndex;
        NotifyActionDone();
    }

    public void EnterHookTarget(int itemIndex)
    {
        Phase = TurnPhase.SelectingHookTarget;
        PendingItemIndex = itemIndex;
        PendingHookSteal = false;
        NotifyActionDone();
    }

    public void EnterHookItemPick(UnitActor target)
    {
        PendingSkillTargetUnit = target;
        PendingHookSteal = true;
        Phase = TurnPhase.SelectingMonkeyMarkItem;
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
        PendingSkillTargetUnit = null;
        PendingHookSteal = false;
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
        LogFor(unit, "背包已恢复至容量以内，结束行动");
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
                    u?.TickSkillCooldownOnFullRound();
                }

                if (RoundNumber % 5 == 0)
                    GameManager.Instance?.ApplyLavaShrink();

                WeatherService.OnFullRoundAdvanced(RoundNumber);

                Log($"—— 进入第{RoundNumber}回合 · {GameClock.GetStatusLine(RoundNumber)} · {WeatherService.GetDisplayName()}（时段视 {GameClock.GetBaseVisibility(RoundNumber)}）——");
                VisibilityService.RefreshWorld();
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
        // 管理员模式可见全部战报；普通 AI 对战隐藏对手私密行动
        if (MatchConfig.IsAiBattle && !MatchConfig.IsAdminMode && !MatchConfig.IsHumanControlled(subject))
            return;
        OnLog?.Invoke(msg);
    }
}
