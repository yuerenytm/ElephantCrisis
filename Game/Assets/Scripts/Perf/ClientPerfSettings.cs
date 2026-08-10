using System;
using System.Globalization;
using System.IO;
using UnityEngine;

/// <summary>
/// 客户端性能采集配置：优先读 StreamingAssets/Config/client_perf.yaml，失败则用内置默认。
/// </summary>
public static class ClientPerfSettings
{
    public static float HitchMs { get; private set; } = 33.3f;
    public static int FrameSampleEvery { get; private set; }
    public static float MemoryIntervalSec { get; private set; } = 2f;
    public static float RecentLoadWindowMs { get; private set; } = 100f;
    public static int MaxRecentLoads { get; private set; } = 8;
    public static float RecentActionWindowMs { get; private set; } = 500f;
    public static int MaxRecentActions { get; private set; } = 16;
    public static bool AutoEnabledByDefault { get; private set; }
    public static string AutoHumanRole { get; private set; } = "Human";
    public static float AutoMaxSeconds { get; private set; } = 120f;
    public static int AutoMaxFullRounds { get; private set; } = 40;

    private static bool loaded;

    public static void EnsureLoaded()
    {
        if (loaded)
            return;
        loaded = true;
        string path = Path.Combine(Application.streamingAssetsPath, "Config", "client_perf.yaml");
        if (!File.Exists(path))
            return;
        try
        {
            ParseSimpleYaml(File.ReadAllText(path));
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[ClientPerf] 读取配置失败，使用默认值: {e.Message}");
        }
    }

    private static void ParseSimpleYaml(string text)
    {
        // 极简 key: value 解析（本文件结构固定，避免引入 YAML 依赖）
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#"))
                continue;
            int c = line.IndexOf(':');
            if (c <= 0)
                continue;
            string key = line.Substring(0, c).Trim();
            string val = line.Substring(c + 1).Trim();
            int hash = val.IndexOf('#');
            if (hash >= 0)
                val = val.Substring(0, hash).Trim();
            if (val.Length == 0)
                continue;

            switch (key)
            {
                case "hitch_ms":
                    HitchMs = ParseFloat(val, HitchMs);
                    break;
                case "frame_sample_every":
                    FrameSampleEvery = ParseInt(val, FrameSampleEvery);
                    break;
                case "memory_interval_sec":
                    MemoryIntervalSec = ParseFloat(val, MemoryIntervalSec);
                    break;
                case "recent_load_window_ms":
                    RecentLoadWindowMs = ParseFloat(val, RecentLoadWindowMs);
                    break;
                case "max_recent_loads":
                    MaxRecentLoads = ParseInt(val, MaxRecentLoads);
                    break;
                case "recent_action_window_ms":
                    RecentActionWindowMs = ParseFloat(val, RecentActionWindowMs);
                    break;
                case "max_recent_actions":
                    MaxRecentActions = ParseInt(val, MaxRecentActions);
                    break;
                case "enabled_by_default":
                    AutoEnabledByDefault = val.Equals("true", StringComparison.OrdinalIgnoreCase);
                    break;
                case "human_role":
                    AutoHumanRole = val;
                    break;
                case "max_seconds":
                    AutoMaxSeconds = ParseFloat(val, AutoMaxSeconds);
                    break;
                case "max_full_rounds":
                    AutoMaxFullRounds = ParseInt(val, AutoMaxFullRounds);
                    break;
            }
        }
    }

    private static float ParseFloat(string s, float fallback)
    {
        return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;
    }

    private static int ParseInt(string s, int fallback)
    {
        return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;
    }
}
