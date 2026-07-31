/// <summary>
/// 虚拟时钟：第 1 回合 6:00，每完整回合推进 2 小时；按时段提供基础能见度。
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

    public static int GetHour(int roundNumber)
    {
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
        => GetBaseVisibilityForPeriod(GetPeriod(roundNumber));

    public static int GetBaseVisibilityForPeriod(Period period)
    {
        switch (period)
        {
            case Period.Day: return 10;
            case Period.Dawn: return 8;
            case Period.Dusk: return 7;
            default: return 5; // Night
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
        => $"{GetTimeLabel(roundNumber)} · {GetPeriodName(roundNumber)}";
}
