/// <summary>
/// 虚拟时钟：第 1 回合 start_hour，每完整回合推进 hours_per_round（见 game_rules.yaml）；按时段提供基础能见度。
/// 管理员模式可覆盖时段（不影响回合推进与熔岩等其它逻辑）。
/// </summary>
public static class GameClock
{
    public enum Period
    {
        Dawn,   // 清晨 4–7
        Day,    // 白天 8–15
        Dusk,   // 黄昏 16–19
        Night   // 黑夜 20–3
    }

    private static bool adminOverride;
    private static int adminHour;

    public static bool HasAdminOverride => adminOverride && MatchConfig.IsAdminMode;

    /// <summary>
    /// 清除管理员的时段覆盖。
    /// </summary>
    public static void ClearAdminOverride()
    {
        adminOverride = false;
        adminHour = GameRulesConfig.ClockStartHour;
    }

    /// <summary>
    /// 管理员：任选时段。写入代表时刻（清晨6/白天12/黄昏18/黑夜0）。
    /// </summary>
    public static bool AdminSetPeriod(Period period)
    {
        if (!MatchConfig.IsAdminMode)
            return false;
        adminOverride = true;
        adminHour = GetRepresentativeHour(period);
        TurnManager.Instance?.Log(
            $"管理员设定时段：{GetPeriodName(period)}（{adminHour}:00；能见度见时段×天气矩阵）");
        TurnManager.Instance?.NotifyActionDone();
        VisibilityService.RefreshAfterVisionRuleChange();
        return true;
    }

    /// <summary>
    /// 时段的代表小时（清晨 6 / 白天 12 / 黄昏 18 / 黑夜 0）。
    /// </summary>
    public static int GetRepresentativeHour(Period period)
    {
        switch (period)
        {
            case Period.Dawn: return GameRulesConfig.DawnRepHour;
            case Period.Day: return GameRulesConfig.DayRepHour;
            case Period.Dusk: return GameRulesConfig.DuskRepHour;
            default: return GameRulesConfig.NightRepHour;
        }
    }

    /// <summary>
    /// 回合号对应的小时（0~23）。
    /// </summary>
    public static int GetHour(int roundNumber)
    {
        if (HasAdminOverride)
            return adminHour;
        int r = UnityEngine.Mathf.Max(1, roundNumber);
        int start = GameRulesConfig.ClockStartHour;
        int step = GameRulesConfig.HoursPerRound;
        return (start + (r - 1) * step) % 24;
    }

    /// <summary>
    /// 回合号对应的时段。
    /// </summary>
    public static Period GetPeriod(int roundNumber)
        => GetPeriodForHour(GetHour(roundNumber));

    /// <summary>
    /// 小时对应的时段。
    /// </summary>
    public static Period GetPeriodForHour(int hour)
    {
        hour = ((hour % 24) + 24) % 24;
        if (hour >= GameRulesConfig.PeriodDayStart && hour < GameRulesConfig.PeriodDuskStart)
            return Period.Day;
        if (hour >= GameRulesConfig.PeriodDuskStart && hour < GameRulesConfig.PeriodNightStart)
            return Period.Dusk;
        if (hour >= GameRulesConfig.PeriodDawnStart && hour < GameRulesConfig.PeriodDayStart)
            return Period.Dawn;
        return Period.Night; // 20–23, 0–3
    }

    /// <summary>
    /// 时段基础能见度（未计天气）。
    /// </summary>
    public static int GetBaseVisibility(int roundNumber)
        => WeatherService.GetPeriodWeatherVisibility(GetPeriod(roundNumber), WeatherService.Current);

    /// <summary>
    /// 仅按时段的标称值（不含天气）；展示/兼容用。现行结算见 WeatherService.GetPeriodWeatherVisibility。
    /// </summary>
    public static int GetBaseVisibilityForPeriod(Period period)
    {
        switch (period)
        {
            case Period.Day: return ItemInfo.FullMapVisibilityRadius;
            case Period.Dawn:
            case Period.Dusk: return ItemInfo.FullMapVisibilityRadius;
            default: return GameRulesConfig.NightClearVisibility; // Night clear
        }
    }

    /// <summary>
    /// 形如 08:00 的时刻文本。
    /// </summary>
    public static string GetTimeLabel(int roundNumber)
        => $"{GetHour(roundNumber)}:00";

    /// <summary>
    /// 时段的中文名。
    /// </summary>
    public static string GetPeriodName(Period period)
    {
        switch (period)
        {
            case Period.Dawn: return "清晨";
            case Period.Day: return "白天";
            case Period.Dusk: return "黄昏";
            default: return "黑夜";
        }
    }

    /// <summary>
    /// 回合号对应时段的中文名。
    /// </summary>
    public static string GetPeriodName(int roundNumber)
        => GetPeriodName(GetPeriod(roundNumber));

    /// <summary>
    /// 状态栏一行文本：时刻 + 时段。
    /// </summary>
    public static string GetStatusLine(int roundNumber)
    {
        string line = $"{GetTimeLabel(roundNumber)} · {GetPeriodName(roundNumber)}";
        if (HasAdminOverride)
            line += "·管";
        return line;
    }
}
