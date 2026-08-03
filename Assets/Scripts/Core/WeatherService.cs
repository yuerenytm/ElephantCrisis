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
/// 管理员模式不自动变更，可自由切换。
/// </summary>
public static class WeatherService
{
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
            case WeatherType.Rain: return "防-3 视-1；灭火且不可着火";
            case WeatherType.Fog: return "视-2";
            default: return "无额外修正";
        }
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
            (MatchConfig.IsAdminMode ? " · 管理员可自由切换" : $" · 下次变更约第{nextChangeRound}回合"));
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
        ApplyWeather(weather, TurnManager.Instance != null ? TurnManager.Instance.RoundNumber : 1, fromAdmin: true);
        return true;
    }

    /// <summary>管理员：晴→雨→雾→晴。</summary>
    public static bool AdminCycleWeather()
    {
        if (!MatchConfig.IsAdminMode)
            return false;
        WeatherType next = Current switch
        {
            WeatherType.Clear => WeatherType.Rain,
            WeatherType.Rain => WeatherType.Fog,
            _ => WeatherType.Clear
        };
        return AdminSetWeather(next);
    }

    public static int GetVisibilityAfterWeather(int periodBaseVisibility)
    {
        switch (Current)
        {
            case WeatherType.Rain:
                return Mathf.Max(0, periodBaseVisibility - 1);
            case WeatherType.Fog:
                return Mathf.Max(0, periodBaseVisibility - 2);
            default:
                // 晴天：不改时段基础能见度
                return periodBaseVisibility;
        }
    }

    public static int GetDefMod() => Current == WeatherType.Rain ? -3 : 0;

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

        ApplyWeather(next, round, fromAdmin: false);
    }

    private static void ApplyWeather(WeatherType weather, int round, bool fromAdmin)
    {
        var prev = Current;
        Current = weather;
        lastChangeRound = Mathf.Max(1, round);

        if (!fromAdmin && !MatchConfig.IsAdminMode)
            ScheduleNextChange(lastChangeRound);
        else
            nextChangeRound = int.MaxValue;

        if (weather == WeatherType.Rain)
            ClearAllBurning();

        string extra = fromAdmin
            ? "（管理员切换）"
            : $" · 下次变更约第{nextChangeRound}回合";
        TurnManager.Instance?.Log(
            $"天气变更：{GetDisplayName(prev)} → {GetDisplayName(weather)}（{GetShortEffect(weather)}）{extra}");

        VisibilityService.RefreshWorld();
        TurnManager.Instance?.NotifyActionDone();
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
            u.ClearStatus(StatusType.Burning);
            TurnManager.Instance?.Log(
                $"{RoleInfo.GetDisplayName(u.Role)} 的【着火】被雨天熄灭");
        }
    }
}
