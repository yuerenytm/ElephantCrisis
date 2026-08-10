using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>
/// 从 StreamingAssets/Config/game_rules.yaml 加载规则（与 sim 共用同一份）。
/// 当前本体使用 roles 基础五维；解析失败时回退内置默认值。
/// </summary>
public static class GameRulesConfig
{
    public struct RoleBaseStats
    {
        public int Move;
        public int Hp;
        public int Atk;
        public int Def;
        public int Bag;
    }

    private static bool loaded;
    private static readonly Dictionary<RoleType, RoleBaseStats> roleStats = new Dictionary<RoleType, RoleBaseStats>();

    private static readonly Regex InlineRole = new Regex(
        @"^\s*(elephant|human|monkey|cat)\s*:\s*\{\s*move\s*:\s*(-?\d+)\s*,\s*hp\s*:\s*(-?\d+)\s*,\s*atk\s*:\s*(-?\d+)\s*,\s*def\s*:\s*(-?\d+)\s*,\s*bag\s*:\s*(-?\d+)\s*\}\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void AutoLoad()
    {
        EnsureLoaded();
    }

    public static void EnsureLoaded()
    {
        if (loaded)
            return;
        loaded = true;
        ApplyFallbackStats();

        string path = ResolveRulesPath();
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            Debug.LogWarning($"[GameRulesConfig] 未找到 game_rules.yaml，使用内置角色数值。尝试路径: {path}");
            return;
        }

        try
        {
            string text = File.ReadAllText(path);
            if (TryParseRoles(text, out var parsed) && parsed.Count > 0)
            {
                foreach (var kv in parsed)
                    roleStats[kv.Key] = kv.Value;
                Debug.Log($"[GameRulesConfig] 已加载角色数值: {path}");
            }
            else
            {
                Debug.LogWarning($"[GameRulesConfig] 未能解析 roles，保留内置数值: {path}");
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[GameRulesConfig] 读取失败，使用内置数值: {e.Message}");
        }
    }

    public static RoleBaseStats GetRoleStats(RoleType role)
    {
        EnsureLoaded();
        if (roleStats.TryGetValue(role, out var s))
            return s;
        return Fallback(role);
    }

    public static string ResolveRulesPath()
    {
        string streaming = Path.Combine(Application.streamingAssetsPath, "Config", "game_rules.yaml");
        if (File.Exists(streaming))
            return streaming;

#if UNITY_EDITOR
        // 编辑器下偶发 StreamingAssets 路径异常时，回退到工程相对路径
        string project = Path.GetFullPath(Path.Combine(Application.dataPath, "StreamingAssets", "Config", "game_rules.yaml"));
        if (File.Exists(project))
            return project;
#endif
        return streaming;
    }

    private static bool TryParseRoles(string text, out Dictionary<RoleType, RoleBaseStats> result)
    {
        result = new Dictionary<RoleType, RoleBaseStats>();
        if (string.IsNullOrEmpty(text))
            return false;

        bool inRoles = false;
        using (var reader = new StringReader(text))
        {
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith("#"))
                    continue;

                if (!inRoles)
                {
                    if (trimmed == "roles:" || trimmed.StartsWith("roles:"))
                        inRoles = true;
                    continue;
                }

                // 下一顶层 key 结束 roles 段
                if (trimmed.Length > 0 && !char.IsWhiteSpace(line, 0) && !trimmed.StartsWith("roles"))
                {
                    if (trimmed.EndsWith(":") && !trimmed.Contains("{"))
                        break;
                }

                var m = InlineRole.Match(line);
                if (!m.Success)
                    continue;

                RoleType role = ParseRole(m.Groups[1].Value);
                result[role] = new RoleBaseStats
                {
                    Move = int.Parse(m.Groups[2].Value),
                    Hp = int.Parse(m.Groups[3].Value),
                    Atk = int.Parse(m.Groups[4].Value),
                    Def = int.Parse(m.Groups[5].Value),
                    Bag = int.Parse(m.Groups[6].Value),
                };
            }
        }

        return result.Count > 0;
    }

    private static RoleType ParseRole(string key)
    {
        switch (key.ToLowerInvariant())
        {
            case "elephant": return RoleType.Elephant;
            case "human": return RoleType.Human;
            case "monkey": return RoleType.Monkey;
            case "cat": return RoleType.Cat;
            default: return RoleType.Human;
        }
    }

    private static void ApplyFallbackStats()
    {
        roleStats[RoleType.Elephant] = Fallback(RoleType.Elephant);
        roleStats[RoleType.Human] = Fallback(RoleType.Human);
        roleStats[RoleType.Monkey] = Fallback(RoleType.Monkey);
        roleStats[RoleType.Cat] = Fallback(RoleType.Cat);
    }

    private static RoleBaseStats Fallback(RoleType role)
    {
        switch (role)
        {
            case RoleType.Elephant:
                return new RoleBaseStats { Move = 3, Hp = 100, Atk = 9, Def = 10, Bag = 30 };
            case RoleType.Human:
                return new RoleBaseStats { Move = 5, Hp = 90, Atk = 10, Def = 8, Bag = 48 };
            case RoleType.Monkey:
                return new RoleBaseStats { Move = 6, Hp = 90, Atk = 8, Def = 6, Bag = 36 };
            case RoleType.Cat:
                return new RoleBaseStats { Move = 8, Hp = 60, Atk = 6, Def = 4, Bag = 30 };
            default:
                return new RoleBaseStats { Move = 4, Hp = 90, Atk = 9, Def = 8, Bag = 30 };
        }
    }
}
