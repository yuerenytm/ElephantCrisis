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
    /// <summary>
    /// 本行动是否已使用过普通近战攻击。
    /// </summary>
    public bool HasMeleeAttacked { get; private set; }
    public int PendingItemIndex { get; private set; } = -1;
    public ItemKind PendingAmmoKind { get; private set; } = ItemKind.Arrow;
    public UnitActor PendingSkillTargetUnit { get; set; }
    /// <summary>
    /// 勾爪抢夺选物（复用 SelectingMonkeyMarkItem）。
    /// </summary>
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

    /// <summary>
    /// 重置行动顺序与轮次（重开对局时用）。
    /// </summary>
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
        PendingSkillTargetUnit = null;
        StealMarks.Reset();
        InventoryItem.ResetIdCounter();
    }

    /// <summary>
    /// 开局：登记参战单位、复位轮次与天气，并开始第一个行动。
    /// </summary>
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
        GameClock.ClearAdminOverride();
        WeatherService.ResetForMatch(1);
        BeginTurnFor(units[0], firstTurn: true);
    }

    /// <summary>
    /// 行动开始的结算链：护身符→着火→领袖→状态同步→沼泽毒→熔岩→火焰→冰地→晕眩→弩标记，最后通知 AI/输入。
    /// </summary>
    public void BeginTurnFor(UnitActor unit, bool firstTurn = false)
    {
        CurrentUnit = unit;
        HasMoved = false;
        HasMeleeAttacked = false;
        Phase = TurnPhase.WaitingAction;
        PendingItemIndex = -1;

        if (!firstTurn)
            GlobalTurnCount++;

        HazardManager.Instance?.RefreshHazardVisibility();

        if (unit != null && !unit.IsDead)
        {
            // 0) 护身符：下次行动开始时失去临时护盾与隐匿
            unit.ClearAmuletBuffsOnActionStart();

            // 1) 着火 DoT（法伤）优先于熔岩等；毒伤改在行动结束结算
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

            // 2) 解除以本人为施加者、到期的状态；在沼且毒被清光时仅补存在性（不叠层）
            foreach (var u in units)
            {
                if (u != null && !u.IsDead)
                {
                    u.TickStatusesFromApplier(unit.Role);
                    u.SyncSwampPoison();
                }
            }

            // 2b) 当前行动者：仍在沼泽则叠 1 层地形中毒（每行动开始至多一层）
            unit.TickSwampPoisonOnTurnStart();

            // 3) 熔岩：真伤后对受伤者施加着火（着火本身仍为法伤）
            if (GridManager.Instance.GetTileType(unit.Cell) == TileType.Lava)
            {
                int lavaDmg = GameRulesConfig.LavaTrueDamage;
                int dealt = unit.TakeDamage(lavaDmg, trueDamage: true);
                LogicMatchLogger.Active?.EmitDamage("lava", LogicSimNaming.Role(unit.Role), "true", lavaDmg, dealt, "lava");
                Log($"{RoleInfo.GetDisplayName(unit.Role)} 站在熔岩上，受到 {dealt} 点真伤");
                if (dealt > 0 && !unit.IsDead)
                    unit.ApplyStatus(StatusType.Burning, GameRulesConfig.BurningRounds, unit);
                if (unit.IsDead)
                {
                    OnStateChanged?.Invoke();
                    GameManager.Instance?.CheckWinConditions();
                    if (Phase != TurnPhase.GameOver)
                        EndTurn();
                    return;
                }
            }

            // 3a) 汽油火焰：身处立刻着火
            HazardManager.Instance?.ApplyFlameBurningAtTurnStart(unit);

            // 3b) 冰地：20% 跌倒
            unit.TickIceTerrainOnTurnStart();

            // 3c) 晕眩：跳过本次行动（不通知 AI，避免同步 LogicSim 误行动）
            if (unit.HasStatus(StatusType.Stun))
            {
                unit.ClearStatus(StatusType.Stun);
                Log($"{RoleInfo.GetDisplayName(unit.Role)} 处于【晕眩】，跳过行动");
                LogicMatchLogger.Active?.EmitStunSkip(unit);
                RefreshSelectionVisuals();
                OnStateChanged?.Invoke();
                if (Phase != TurnPhase.GameOver)
                    EndTurn(skipped: true);
                return;
            }

            unit.ResetCrossbowActionFlags();
            Log($"—— 第{RoundNumber}轮 · {GameClock.GetStatusLine(RoundNumber)} · {WeatherService.GetDisplayName()} · {RoleInfo.GetDisplayName(unit.Role)} 的行动 ——");
            StealthService.CheckBreakFor(unit);
            LogicMatchLogger.Active?.EmitTurnStart(unit);
        }

        RefreshSelectionVisuals();
        OnTurnChanged?.Invoke();
        OnStateChanged?.Invoke();
    }

    /// <summary>
    /// 通知 UI 与 AI 刷新当前状态，不推进回合。
    /// </summary>
    public void NotifyActionDone() => OnStateChanged?.Invoke();

    /// <summary>
    /// 标记本行动已移动（禁止再次移动）。
    /// </summary>
    public void MarkMoved()
    {
        HasMoved = true;
        NotifyActionDone();
    }

    /// <summary>
    /// 标记本行动已普攻（禁止再次普攻）。
    /// </summary>
    public void MarkMeleeAttacked()
    {
        HasMeleeAttacked = true;
        NotifyActionDone();
    }

    /// <summary>
    /// 进入炸弹投掷的瞄准态。
    /// </summary>
    public void EnterBombTargeting(int itemIndex)
    {
        Phase = TurnPhase.SelectingBombTarget;
        PendingItemIndex = itemIndex;
        NotifyActionDone();
    }

    /// <summary>
    /// 进入香蕉皮投掷的瞄准态。
    /// </summary>
    public void EnterBananaTargeting(int itemIndex)
    {
        Phase = TurnPhase.SelectingBananaTarget;
        PendingItemIndex = itemIndex;
        NotifyActionDone();
    }

    /// <summary>
    /// 进入地雷安放的瞄准态。
    /// </summary>
    public void EnterMineTargeting(int itemIndex)
    {
        Phase = TurnPhase.SelectingMineTarget;
        PendingItemIndex = itemIndex;
        NotifyActionDone();
    }

    /// <summary>
    /// 进入弃置选格的瞄准态。
    /// </summary>
    public void EnterDiscardTargeting(int itemIndex)
    {
        Phase = TurnPhase.SelectingDiscardTarget;
        PendingItemIndex = itemIndex;
        NotifyActionDone();
    }

    /// <summary>
    /// 进入射击目标选择态，并记录所用弹药。
    /// </summary>
    public void EnterShootTargeting(int itemIndex, ItemKind ammo)
    {
        Phase = TurnPhase.SelectingShootTarget;
        PendingItemIndex = itemIndex;
        PendingAmmoKind = ammo;
        NotifyActionDone();
    }

    /// <summary>
    /// 进入弹药选择态（一件武器可选多种弹药时）。
    /// </summary>
    public void EnterAmmoChoice(int weaponIndex)
    {
        Phase = TurnPhase.SelectingAmmo;
        PendingItemIndex = weaponIndex;
        NotifyActionDone();
    }

    /// <summary>
    /// 进入强化卡属性选择态。
    /// </summary>
    public void EnterReinforceChoice(int itemIndex)
    {
        Phase = TurnPhase.SelectingReinforce;
        PendingItemIndex = itemIndex;
        NotifyActionDone();
    }

    /// <summary>
    /// 进入技能强化（永久属性）选择态。
    /// </summary>
    public void EnterSkillReinforce()
    {
        Phase = TurnPhase.SelectingSkillReinforce;
        PendingItemIndex = -1;
        NotifyActionDone();
    }

    /// <summary>
    /// 进入猴子技能的分支选择态。
    /// </summary>
    public void EnterMonkeySkillChoice()
    {
        Phase = TurnPhase.SelectingMonkeySkillMode;
        PendingSkillTargetUnit = null;
        NotifyActionDone();
    }

    /// <summary>
    /// 进入猴子标记的目标选择态。
    /// </summary>
    public void EnterMonkeyMarkTarget()
    {
        Phase = TurnPhase.SelectingMonkeyMarkTarget;
        PendingSkillTargetUnit = null;
        NotifyActionDone();
    }

    /// <summary>
    /// 进入猴子标记的物品选择态。
    /// </summary>
    public void EnterMonkeyMarkItem()
    {
        Phase = TurnPhase.SelectingMonkeyMarkItem;
        NotifyActionDone();
    }

    /// <summary>
    /// 进入猴子偷窃的目标选择态。
    /// </summary>
    public void EnterMonkeyStealTarget()
    {
        Phase = TurnPhase.SelectingMonkeyStealTarget;
        PendingSkillTargetUnit = null;
        NotifyActionDone();
    }

    /// <summary>
    /// 进入定时炸弹的延时回合选择态。
    /// </summary>
    public void EnterTimedBombDelay(int itemIndex)
    {
        Phase = TurnPhase.SelectingTimedBombDelay;
        PendingItemIndex = itemIndex;
        NotifyActionDone();
    }

    /// <summary>
    /// 进入喷火器的方向选择态。
    /// </summary>
    public void EnterFlameDirection(int itemIndex)
    {
        Phase = TurnPhase.SelectingFlameDirection;
        PendingItemIndex = itemIndex;
        NotifyActionDone();
    }

    /// <summary>
    /// 进入摩托车冲撞的方向选择态。
    /// </summary>
    public void EnterMotorcycleRam(int itemIndex)
    {
        Phase = TurnPhase.SelectingMotorcycleRam;
        PendingItemIndex = itemIndex;
        NotifyActionDone();
    }

    /// <summary>
    /// 进入钩爪的目标选择态。
    /// </summary>
    public void EnterHookTarget(int itemIndex)
    {
        Phase = TurnPhase.SelectingHookTarget;
        PendingItemIndex = itemIndex;
        PendingHookSteal = false;
        NotifyActionDone();
    }

    /// <summary>
    /// 进入钩爪夺取物品的选择态。
    /// </summary>
    public void EnterHookItemPick(UnitActor target)
    {
        PendingSkillTargetUnit = target;
        PendingHookSteal = true;
        Phase = TurnPhase.SelectingMonkeyMarkItem;
        NotifyActionDone();
    }

    /// <summary>
    /// 进入拾取模式，等待点击地面。
    /// </summary>
    public void EnterPickupMode()
    {
        Phase = TurnPhase.SelectingPickup;
        PendingItemIndex = -1;
        NotifyActionDone();
    }

    /// <summary>
    /// 退出瞄准态，回到 WaitingAction。
    /// </summary>
    public void CancelTargeting()
    {
        PendingItemIndex = -1;
        PendingSkillTargetUnit = null;
        PendingHookSteal = false;
        Phase = TurnPhase.WaitingAction;
        NotifyActionDone();
    }

    /// <summary>
    /// 请求结束行动：超重时拒绝并提示，不进入单独弃置阶段。
    /// </summary>
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
                if (unit.Inventory.IsOverCapacity)
                {
                    LogFor(unit,
                        $"背包仍超重（{unit.Inventory.UsedWeight:0.##}/{unit.Inventory.Capacity:0.##}），无法结束行动");
                    NotifyActionDone();
                    return false;
                }
                EndTurn();
                return true;
            }

            LogFor(unit,
                $"背包超载（{unit.Inventory.UsedWeight:0.##}/{unit.Inventory.Capacity:0.##}），无法结束行动，请先弃置至容量以内");
            NotifyActionDone();
            return false;
        }

        EndTurn(skipped: false);
        return true;
    }

    /// <summary>
    /// 行动结束结算并推进到下一个行动者；跨轮时统一结算状态、冷却、熔岩与天气。
    /// </summary>
    public void EndTurn(bool skipped = false)
    {
        if (Phase == TurnPhase.GameOver)
            return;

        var endingUnit = CurrentUnit;
        // 毒伤：行动结束结算（含晕眩跳过）；着火仍在行动开始
        if (endingUnit != null && !endingUnit.IsDead)
        {
            endingUnit.TickPoisonOnTurnEnd();
            if (endingUnit.IsDead)
            {
                OnStateChanged?.Invoke();
                GameManager.Instance?.CheckWinConditions();
                if (Phase == TurnPhase.GameOver)
                    return;
            }
        }

        // 定时炸弹 / 火焰地块 / 濒死：按行动倒数（规则回合×4，含当次行动）
        HazardManager.Instance?.TickTimedBombsOnActionEnd();
        HazardManager.Instance?.TickFlamesOnActionEnd();
        if (GameManager.Instance != null)
        {
            foreach (var u in GameManager.Instance.Units)
                u?.TickDyingOnActionEnd();
        }

        if (LogicMatchLogger.IsRecordingEvents && endingUnit != null)
        {
            LogicMatchLogger.Active.EmitTurnEnd(endingUnit, skipped: skipped);
            if (endingUnit.Inventory != null && endingUnit.Inventory.IsOverCapacity)
                LogicMatchLogger.Active.EmitOverweight(endingUnit);
            LogicMatchLogger.Active.EmitSnapshot();
        }

        GameManager.Instance?.CheckWinConditions();
        if (Phase == TurnPhase.GameOver)
            return;

        // 红牛：行动结束后额外行动（不结算行动开始 DoT）
        if (!skipped && endingUnit != null && !endingUnit.IsDead && endingUnit.PendingExtraActions > 0)
        {
            endingUnit.PendingExtraActions--;
            BeginExtraActionFor(endingUnit);
            return;
        }

        int start = CurrentIndex;
        bool advancedFullRound = false;
        do
        {
            CurrentIndex = (CurrentIndex + 1) % units.Count;
            if (CurrentIndex == 0)
            {
                RoundNumber++;
                advancedFullRound = true;

                foreach (var u in units)
                {
                    u?.TickStatusOnFullRound();
                    u?.TickSkillCooldownOnFullRound();
                }

                if (RoundNumber % GameRulesConfig.LavaShrinkEveryRounds == 0)
                    GameManager.Instance?.ApplyLavaShrink();

                WeatherService.OnFullRoundAdvanced(RoundNumber);

                Log($"—— 进入第{RoundNumber}回合 · {GameClock.GetStatusLine(RoundNumber)} · {WeatherService.GetDisplayName()}（矩阵视 {GameClock.GetBaseVisibility(RoundNumber)}）——");
                if (!MatchConfig.IsLogicSim)
                    VisibilityService.RefreshWorld();

                if (LogicMatchLogger.IsRecordingEvents)
                {
                    LogicMatchLogger.Active.EmitClock(GameClock.GetHour(RoundNumber), RoundNumber - 1);
                    LogicMatchLogger.Active.EmitWeather();
                    LogicMatchLogger.Active.EmitSnapshot();
                }
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

    /// <summary>
    /// 红牛额外行动：重置本行动移动/普攻等限制，不跑行动开始结算链。
    /// </summary>
    private void BeginExtraActionFor(UnitActor unit)
    {
        CurrentUnit = unit;
        HasMoved = false;
        HasMeleeAttacked = false;
        Phase = TurnPhase.WaitingAction;
        PendingItemIndex = -1;
        PendingSkillTargetUnit = null;
        PendingHookSteal = false;

        unit.ResetCrossbowActionFlags();
        Log($"—— {RoleInfo.GetDisplayName(unit.Role)} 因【红牛】获得额外行动" +
            (unit.PendingExtraActions > 0 ? $"（尚余预约 {unit.PendingExtraActions}）" : "") +
            " ——");
        LogicMatchLogger.Active?.EmitTurnStart(unit);
        RefreshSelectionVisuals();
        OnTurnChanged?.Invoke();
        OnStateChanged?.Invoke();
    }

    /// <summary>
    /// 停表：进入 GameOver 并清空当前单位。
    /// </summary>
    public void SetGameOver()
    {
        Phase = TurnPhase.GameOver;
        CurrentUnit = null;
        PendingItemIndex = -1;
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

    /// <summary>
    /// 写一条对局日志（同时进 UI 与 LogicSim 事件）。
    /// </summary>
    public void Log(string msg)
    {
        // LogicSim 无头跑局：战报已完整写入 events.jsonl，跳过 Debug.Log（否则批量跑局会刷出带堆栈的巨量日志）
        if (!MatchConfig.IsLogicSim)
            Debug.Log(msg);
        OnLog?.Invoke(msg);
    }

    /// <summary>
    /// 角色私密行动战报。热座全员可见；AI 对战仅当 subject 是本地玩家操控时写入战报。
    /// </summary>
    public void LogFor(UnitActor subject, string msg)
    {
        if (!MatchConfig.IsLogicSim)
            Debug.Log(msg);
        // 管理员模式可见全部战报；普通 AI 对战隐藏对手私密行动
        if (MatchConfig.IsAiBattle && !MatchConfig.IsAdminMode && !MatchConfig.IsHumanControlled(subject))
            return;
        OnLog?.Invoke(msg);
    }
}
