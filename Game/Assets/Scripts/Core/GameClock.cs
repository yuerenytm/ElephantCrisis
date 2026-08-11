/// <summary>
/// 虚拟时钟：第 1 回合 6:00，每完整回合推进 2 小时；按时段提供基础能见度。
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

    public const int NightVisionVisibility = 8;

    private static bool adminOverride;
    private static int adminHour;

    public static bool HasAdminOverride => adminOverride && MatchConfig.IsAdminMode;

    public static void ClearAdminOverride()
    {
        adminOverride = false;
        adminHour = 6;
    }

    /// <summary>管理员：任选时段。写入代表时刻（清晨6/白天12/黄昏18/黑夜0）。</summary>
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

    public static int GetRepresentativeHour(Period period)
    {
        switch (period)
        {
            case Period.Dawn: return 6;
            case Period.Day: return 12;
            case Period.Dusk: return 18;
            default: return 0;
        }
    }

    public static int GetHour(int roundNumber)
    {
        if (HasAdminOverride)
            return adminHour;
        int r = UnityEngine.Mathf.Max(1, roundNumber);
        return (6 + (r - 1) * 2) % 24;
    }

    public static Period GetPeriod(int roundNumber)
        => GetPeriodForHour(GetHour(roundNumber));

    public static Period GetPeriodForHour(int hour)
    {
        hour = ((hour % 24) + 24) % 24;
        if (hour >= 8 && hour <= 15)
            return Period.Day;
        if (hour >= 16 && hour <= 19)
            return Period.Dusk;
        if (hour >= 4 && hour <= 7)
            return Period.Dawn;
        return Period.Night; // 20–23, 0–3
    }

    public static int GetBaseVisibility(int roundNumber)
        => WeatherService.GetPeriodWeatherVisibility(GetPeriod(roundNumber), WeatherService.Current);

    /// <summary>仅按时段的标称值（不含天气）；展示/兼容用。现行结算见 WeatherService.GetPeriodWeatherVisibility。</summary>
    public static int GetBaseVisibilityForPeriod(Period period)
    {
        switch (period)
        {
            case Period.Day: return ItemInfo.FullMapVisibilityRadius;
            case Period.Dawn:
            case Period.Dusk: return ItemInfo.FullMapVisibilityRadius;
            default: return 5; // Night clear
        }
    }

    public static string GetTimeLabel(int roundNumber)
        => $"{GetHour(roundNumber)}:00";

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

    public static string GetPeriodName(int roundNumber)
        => GetPeriodName(GetPeriod(roundNumber));

    public static string GetStatusLine(int roundNumber)
    {
        string line = $"{GetTimeLabel(roundNumber)} · {GetPeriodName(roundNumber)}";
        if (HasAdminOverride)
            line += "·管";
        return line;
    }
}
