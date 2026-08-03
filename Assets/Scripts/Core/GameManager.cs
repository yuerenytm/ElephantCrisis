using System.Collections.Generic;
using UnityEngine;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance;

    public List<UnitActor> Units { get; private set; } = new List<UnitActor>();
    public string WinnerText { get; private set; }
    public bool IsGameOver { get; private set; }

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public void RegisterUnits(List<UnitActor> units)
    {
        Units = new List<UnitActor>(units);
        IsGameOver = false;
        WinnerText = null;
    }

    public void ClearMatch()
    {
        Units = new List<UnitActor>();
        IsGameOver = false;
        WinnerText = null;
    }

    public void ApplyLavaShrink()
    {
        var changed = GridManager.Instance.ShrinkLavaRing();
        if (changed.Count > 0)
        {
            int swallowed = GroundItemManager.Instance.SwallowCellsToDiscard(changed);
            HazardManager.Instance?.SwallowCells(changed);
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
        }
    }

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
                EndGame($"{RoleInfo.GetDisplayName(unit.Role)} 集齐全部玩偶，获胜！");
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
            EndGame($"{RoleInfo.GetDisplayName(survivor.Role)} 成为唯一存活者，获胜！");
            return;
        }

        if (alive == 0)
            EndGame("平局（无人存活）");
    }

    private void EndGame(string text)
    {
        IsGameOver = true;
        WinnerText = text;
        TurnManager.Instance?.SetGameOver();
        TurnManager.Instance?.Log(text);
    }

    public void Restart()
    {
        GameBootstrap.Instance?.RequestRestartMatch();
    }

    public void ReturnToMenu()
    {
        GameBootstrap.Instance?.RequestReturnToMenu();
    }
}
