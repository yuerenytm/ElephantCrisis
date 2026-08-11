using System.Collections.Generic;
using UnityEngine;

public enum WeatherType
{
    Clear,
    Rain,
    Fog
}

/// <summary>
/// 天气：开局晴天；变更后前 5 回合锁定，第 6～10 完整回合内必再变一次。
/// 管理员模式不自动变更，可自由切换。天气弹可强制切换并重置自动变更日程。
/// </summary>
public static class WeatherService
{
    private enum ChangeSource
    {
        Auto,
        Admin,
        Card
    }

    public static WeatherType Current { get; private set; } = WeatherType.Clear;

    private static int lastChangeRound = 1;
    private static int nextChangeRound = int.MaxValue;

    public static string GetDisplayName(WeatherType weather)
    {
        switch (weather)
        {
            case WeatherType.Rain: return "雨天";
            case WeatherType.Fog: return "雾天";
            default: return "晴天";
        }
    }

    public static string GetDisplayName() => GetDisplayName(Current);

    public static string GetShortEffect(WeatherType weather)
    {
        switch (weather)
        {
            case WeatherType.Rain: return "防-3 法抗+25；浇灭格火/着火且不可再燃；能见度见时段×天气表";
            case WeatherType.Fog: return "法抗+10；能见度见时段×天气表";
            default: return "能见度见时段×天气表";
        }
    }

    /// <summary>
    /// 时段×天气能见度矩阵（曼哈顿半径；无限=FullMapVisibilityRadius）。
    /// 清晨/白天/黄昏 × 晴/雨 → 无限；白天雾 8；清晨/黄昏雾 6；黑夜晴 5 / 雨 4 / 雾 3。
    /// </summary>
    public static int GetPeriodWeatherVisibility(GameClock.Period period, WeatherType weather)
    {
        if (period == GameClock.Period.Night)
        {
            switch (weather)
            {
                case WeatherType.Rain: return 4;
                case WeatherType.Fog: return 3;
                default: return 5;
            }
        }

        // 清晨 / 白天 / 黄昏
        if (weather == WeatherType.Fog)
            return period == GameClock.Period.Day ? 8 : 6;
        return ItemInfo.FullMapVisibilityRadius;
    }

    public static int GetVisibilityAfterWeather(int periodBaseVisibility, UnitActor unit = null)
    {
        // 兼容旧调用：改走时段×天气矩阵（忽略 periodBaseVisibility）
        int round = TurnManager.Instance != null ? TurnManager.Instance.RoundNumber : 1;
        var period = GameClock.GetPeriod(round);
        var weather = Current;
        if (weather == WeatherType.Rain && ItemInfo.HasEquippedRubberRaincoat(unit))
            weather = WeatherType.Clear;
        return GetPeriodWeatherVisibility(period, weather);
    }

    public static void ResetForMatch(int roundNumber = 1)
    {
        Current = WeatherType.Clear;
        lastChangeRound = Mathf.Max(1, roundNumber);
        if (MatchConfig.IsAdminMode)
            nextChangeRound = int.MaxValue;
        else
            ScheduleNextChange(lastChangeRound);

        TurnManager.Instance?.Log(
            $"天气：{GetDisplayName()}（{GetShortEffect(Current)}）" +
            (MatchConfig.IsAdminMode ? " · 管理员可任选天气/时段" : $" · 下次变更约第{nextChangeRound}回合"));
    }

    /// <summary>完整回合号推进到 newRound 后调用。</summary>
    public static void OnFullRoundAdvanced(int newRound)
    {
        if (MatchConfig.IsAdminMode)
            return;
        if (newRound < nextChangeRound)
            return;
        ChangeToRandom(newRound);
    }

    public static bool AdminSetWeather(WeatherType weather)
    {
        if (!MatchConfig.IsAdminMode)
            return false;
        ApplyWeather(weather, TurnManager.Instance != null ? TurnManager.Instance.RoundNumber : 1, ChangeSource.Admin);
        return true;
    }

    /// <summary>天气弹：强制切换天气；已是目标天气则失败。非管理员模式会重置下次自动变更日程。</summary>
    public static bool TryApplyFromCard(WeatherType weather)
    {
        if (Current == weather)
            return false;
        int round = TurnManager.Instance != null ? TurnManager.Instance.RoundNumber : 1;
        ApplyWeather(weather, round, ChangeSource.Card);
        return true;
    }

    public static int GetDefMod(UnitActor unit = null)
    {
        if (Current != WeatherType.Rain)
            return 0;
        if (ItemInfo.HasEquippedRubberRaincoat(unit))
            return 0;
        return -3;
    }

    /// <summary>天气法抗修正（百分比，平值相加，下限 0 由 UnitActor.MagicResist 统一夹取）。雨天 +25、雾天 +10。</summary>
    public static int GetMagicResistMod(UnitActor unit = null)
    {
        switch (Current)
        {
            case WeatherType.Rain: return 25;
            case WeatherType.Fog: return 10;
            default: return 0;
        }
    }

    public static bool BlocksBurning => Current == WeatherType.Rain;

    /// <summary>下次自动变更的完整回合号（管理员模式下为 MaxValue）。</summary>
    public static int NextChangeRound => nextChangeRound;

    private static void ScheduleNextChange(int fromRound)
    {
        // 变更后第 6～10 回合（含）内必变一次；第 5 回合仍锁定
        nextChangeRound = fromRound + Random.Range(6, 11);
    }

    private static void ChangeToRandom(int round)
    {
        WeatherType next;
        if (Current == WeatherType.Clear)
        {
            // 晴天必然变为雨/雾
            next = Random.value < 0.5f ? WeatherType.Rain : WeatherType.Fog;
        }
        else
        {
            var options = new List<WeatherType>(2);
            if (Current != WeatherType.Clear) options.Add(WeatherType.Clear);
            if (Current != WeatherType.Rain) options.Add(WeatherType.Rain);
            if (Current != WeatherType.Fog) options.Add(WeatherType.Fog);
            next = options[Random.Range(0, options.Count)];
        }

        ApplyWeather(next, round, ChangeSource.Auto);
    }

    private static void ApplyWeather(WeatherType weather, int round, ChangeSource source)
    {
        var prev = Current;
        Current = weather;
        lastChangeRound = Mathf.Max(1, round);

        if (source == ChangeSource.Admin || MatchConfig.IsAdminMode)
            nextChangeRound = int.MaxValue;
        else
            ScheduleNextChange(lastChangeRound);

        if (weather == WeatherType.Rain)
        {
            ClearAllBurning();
            HazardManager.Instance?.ClearAllFlames("雨天浇灭了场上的火焰");
        }

        string extra;
        switch (source)
        {
            case ChangeSource.Admin:
                extra = "（管理员切换）";
                break;
            case ChangeSource.Card:
                extra = MatchConfig.IsAdminMode
                    ? "（天气弹）"
                    : $"（天气弹） · 下次变更约第{nextChangeRound}回合";
                break;
            default:
                extra = $" · 下次变更约第{nextChangeRound}回合";
                break;
        }

        TurnManager.Instance?.Log(
            $"天气变更：{GetDisplayName(prev)} → {GetDisplayName(weather)}（{GetShortEffect(weather)}）{extra}");

        // 先通知 UI，再强制按新天气重算能见度（迷雾半径 / 移动提示 / 状态「视」）
        TurnManager.Instance?.NotifyActionDone();
        VisibilityService.RefreshAfterVisionRuleChange();
    }

    private static void ClearAllBurning()
    {
        var units = GameManager.Instance?.Units ?? TurnManager.Instance?.Units;
        if (units == null)
            return;
        foreach (var u in units)
        {
            if (u == null || u.IsDead || !u.HasStatus(StatusType.Burning))
                continue;
            u.ClearStatus(StatusType.Burning, "clear_rain");
            TurnManager.Instance?.Log(
                $"{RoleInfo.GetDisplayName(u.Role)} 的【着火】被雨天熄灭");
        }
    }
}
