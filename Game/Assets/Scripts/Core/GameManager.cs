using System.Collections.Generic;
using UnityEngine;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance;

    public List<UnitActor> Units { get; private set; } = new List<UnitActor>();
    public string WinnerText { get; private set; }
    public bool IsGameOver { get; private set; }
    /// <summary>
    /// LogicSim / 结算用：获胜角色；平局或 max_rounds 时为 null。
    /// </summary>
    public RoleType? WinnerRole { get; private set; }
    public string EndReason { get; private set; }

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>
    /// 登记本局参战单位并清空结果状态。
    /// </summary>
    public void RegisterUnits(List<UnitActor> units)
    {
        Units = new List<UnitActor>(units);
        IsGameOver = false;
        WinnerText = null;
        WinnerRole = null;
        EndReason = null;
    }

    /// <summary>
    /// 清空对局数据（单位名单与结果）。
    /// </summary>
    public void ClearMatch()
    {
        Units = new List<UnitActor>();
        IsGameOver = false;
        WinnerText = null;
        WinnerRole = null;
        EndReason = null;
    }

    /// <summary>
    /// 熔岩缩一圈：吞掉落物与危险物、刷新视觉、让单位重新贴地。
    /// </summary>
    public void ApplyLavaShrink()
    {
        var changed = GridManager.Instance.ShrinkLavaRing();
        if (changed.Count > 0)
        {
            int swallowed = GroundItemManager.Instance.SwallowCellsToDiscard(changed);
            HazardManager.Instance?.SwallowCells(changed);
            if (!MatchConfig.IsLeanRuntime)
                MapVisual.Instance?.RefreshAllTiles(GridManager.Instance);
            // 高地被熔岩覆盖后高度归零，单位需贴回新高度
            if (Units != null)
            {
                foreach (var u in Units)
                {
                    if (u != null && !u.IsDead)
                        u.PlaceAt(u.Cell, true);
                }
            }
            TurnManager.Instance?.Log($"熔岩收缩！新增 {changed.Count} 格熔岩，吞噬掉落物 {swallowed} 件入弃牌堆");
            if (LogicMatchLogger.IsRecordingEvents)
            {
                LogicMatchLogger.Active.EmitLavaShrink(GridManager.Instance.LavaInset);
                LogicMatchLogger.Active.EmitSnapshot();
            }
        }
    }

    /// <summary>
    /// 判定胜负：集齐玩偶 / 唯一存活 / 全灭平局。
    /// </summary>
    public void CheckWinConditions()
    {
        if (IsGameOver)
            return;

        foreach (var unit in Units)
        {
            if (unit == null || unit.IsDead)
                continue;
            if (unit.Inventory.HasAllDolls())
            {
                EndGame($"{RoleInfo.GetDisplayName(unit.Role)} 集齐全部玩偶，获胜！",
                    unit.Role, "all_dolls");
                return;
            }
        }

        UnitActor survivor = null;
        int alive = 0;
        foreach (var unit in Units)
        {
            if (unit == null || unit.IsDead)
                continue;
            alive++;
            survivor = unit;
        }

        if (alive == 1 && survivor != null)
        {
            EndGame($"{RoleInfo.GetDisplayName(survivor.Role)} 成为唯一存活者，获胜！",
                survivor.Role, "last_standing");
            return;
        }

        if (alive == 0)
            EndGame("平局（无人存活）", null, "draw");
    }

    /// <summary>
    /// LogicSim 回合上限强制结束。
    /// </summary>
    public void ForceEndForLogicSim(string reason)
    {
        if (IsGameOver)
            return;
        EndGame($"LogicSim 结束（{reason}）", null, reason ?? "max_rounds");
    }

    private void EndGame(string text, RoleType? winnerRole, string reason)
    {
        IsGameOver = true;
        WinnerText = text;
        WinnerRole = winnerRole;
        EndReason = reason;
        TurnManager.Instance?.SetGameOver();
        TurnManager.Instance?.Log(text);

        if (LogicMatchLogger.IsRecording)
        {
            int full = TurnManager.Instance != null
                ? Mathf.Max(0, TurnManager.Instance.RoundNumber - 1)
                : 0;
            string w = winnerRole.HasValue ? LogicSimNaming.Role(winnerRole.Value) : null;
            LogicMatchLogger.Active.FinishMatch(w, reason, full);
        }
    }

    /// <summary>
    /// 重开一局。
    /// </summary>
    public void Restart()
    {
        GameBootstrap.Instance?.RequestRestartMatch();
    }

    /// <summary>
    /// 回主菜单。
    /// </summary>
    public void ReturnToMenu()
    {
        GameBootstrap.Instance?.RequestReturnToMenu();
    }
}
