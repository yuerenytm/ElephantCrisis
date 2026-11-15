using System;

/// <summary>
/// 无头推进器：把一整局推进到结束。
///
/// 抽出来的原因：Unity 侧的 AiController 与 dotnet 跑批器需要**同一套**推进规则，
/// 各写一份必然漂移。区别只在于"谁来出一手"——由调用方传进来。
/// </summary>
public static class MatchDriver
{
    /// <summary>
    /// 推进到对局结束。返回是否正常结束（false 表示触发了安全上限）。
    /// </summary>
    /// <param name="tryActOnce">让当前单位走一步；返回 false 表示它已经无法再行动。</param>
    /// <param name="maxFullRounds">回合上限（含）。</param>
    public static bool PumpToEnd(Func<UnitActor, bool> tryActOnce, int maxFullRounds)
    {
        if (tryActOnce == null)
            return false;

        int guard = 0;
        while (guard++ < 50000)
        {
            var turn = TurnManager.Instance;
            if (turn == null || turn.Phase == TurnPhase.GameOver)
                return true;
            if (GameManager.Instance != null && GameManager.Instance.IsGameOver)
                return true;

            int full = System.Math.Max(0, turn.RoundNumber - 1);
            if (full >= maxFullRounds)
            {
                GameManager.Instance?.ForceEndForLogicSim("max_rounds");
                return true;
            }

            var unit = turn.CurrentUnit;
            if (unit == null || unit.IsDead)
                return true;
            if (MatchConfig.IsHumanControlled(unit))
                return true;

            PlayTurn(unit, tryActOnce);
        }

        return false;
    }

    private static void PlayTurn(UnitActor unit, Func<UnitActor, bool> tryActOnce)
    {
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

            if (!tryActOnce(unit))
                break;

            // 濒死且地上没有药水：这一手结束后不能再行动
            if (unit.IsDying && !ActionService.HasGroundPotionAt(unit))
                break;
        }

        // 收尾：仍轮到它且对局未结束，就主动结束回合（否则会卡住）
        var endTurn = TurnManager.Instance;
        if (endTurn != null &&
            endTurn.Phase != TurnPhase.GameOver &&
            endTurn.CurrentUnit == unit &&
            !(GameManager.Instance?.IsGameOver ?? false))
        {
            endTurn.RequestEndTurn();
        }
    }
}
