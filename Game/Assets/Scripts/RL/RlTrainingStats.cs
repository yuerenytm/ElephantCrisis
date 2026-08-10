using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// 训练埋点：滚动最近 Window 局的胜率/死亡率/领袖宣言等，写 JSONL + 汇总 JSON。
/// </summary>
public static class RlTrainingStats
{
    public const int Window = 100;

    public struct MatchRecord
    {
        public int Index;
        public string Winner;
        public string EndReason;
        public int FullRounds;
        public int LeaderDeclarations;
        public Dictionary<string, bool> Died;
        public Dictionary<string, bool> Survived;
        public string Curriculum;
        public string LearningRole;
    }

    private static readonly Queue<MatchRecord> recent = new Queue<MatchRecord>();
    private static int matchIndex;
    private static long decisionTotal;
    private static string outDir;
    private static bool dirReady;

    public static int MatchesRecorded => matchIndex;
    public static long DecisionsRecorded => decisionTotal;
    public static int WindowCount => recent.Count;

    /// <summary>每次 RL 微操作决策计 1；供工作台显示进度（≠ mlagents max_steps 时以 Trainer 为准）。</summary>
    public static void NoteDecision()
    {
        decisionTotal++;
        if (decisionTotal == 1 || decisionTotal % 50 == 0)
            WriteProgressLite();
    }

    public static void EnsureOutDir()
    {
        if (dirReady)
            return;
        dirReady = true;
        try
        {
            var data = Application.dataPath;
            var gameRoot = Directory.GetParent(data)?.FullName;
            var repoRoot = gameRoot != null ? Directory.GetParent(gameRoot)?.FullName : null;
            outDir = repoRoot != null
                ? Path.Combine(repoRoot, "Tools", "rl", "reports")
                : Path.Combine(Application.persistentDataPath, "RlTraining");
            Directory.CreateDirectory(outDir);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[RlStats] out dir failed: {e.Message}");
            outDir = Path.Combine(Application.persistentDataPath, "RlTraining");
            Directory.CreateDirectory(outDir);
        }
    }

    public static void RecordMatch(
        string winnerRole,
        string endReason,
        int fullRounds,
        int leaderDeclarations,
        IReadOnlyList<UnitActor> units,
        string curriculum,
        string learningRole)
    {
        EnsureOutDir();
        matchIndex++;

        var died = new Dictionary<string, bool>();
        var survived = new Dictionary<string, bool>();
        if (units != null)
        {
            foreach (var u in units)
            {
                if (u == null)
                    continue;
                string key = RoleKey(u.Role);
                died[key] = u.IsDead;
                survived[key] = !u.IsDead;
            }
        }

        var rec = new MatchRecord
        {
            Index = matchIndex,
            Winner = winnerRole ?? "none",
            EndReason = endReason ?? "unknown",
            FullRounds = fullRounds,
            LeaderDeclarations = leaderDeclarations,
            Died = died,
            Survived = survived,
            Curriculum = curriculum ?? "",
            LearningRole = learningRole ?? ""
        };

        recent.Enqueue(rec);
        while (recent.Count > Window)
            recent.Dequeue();

        AppendJsonl(rec);
        WriteSummary();
        Debug.Log(FormatConsoleSummary());
    }

    public static string FormatConsoleSummary()
    {
        if (recent.Count == 0)
            return "[RlStats] no matches yet";

        var wins = new Dictionary<string, int>();
        var deaths = new Dictionary<string, int>();
        var appear = new Dictionary<string, int>();
        int leaders = 0;
        var reasons = new Dictionary<string, int>();
        float roundsSum = 0f;

        foreach (var m in recent)
        {
            leaders += m.LeaderDeclarations;
            roundsSum += m.FullRounds;
            string w = m.Winner ?? "none";
            wins[w] = wins.TryGetValue(w, out var wc) ? wc + 1 : 1;
            reasons[m.EndReason] = reasons.TryGetValue(m.EndReason, out var rc) ? rc + 1 : 1;
            if (m.Died == null)
                continue;
            foreach (var kv in m.Died)
            {
                appear[kv.Key] = appear.TryGetValue(kv.Key, out var a) ? a + 1 : 1;
                if (kv.Value)
                    deaths[kv.Key] = deaths.TryGetValue(kv.Key, out var d) ? d + 1 : 1;
            }
        }

        int n = recent.Count;
        var sb = new StringBuilder();
        sb.Append($"[RlStats] last {n}/{Window} matches (total={matchIndex})");
        sb.Append($" avg_rounds={roundsSum / n:0.0}");
        sb.Append($" leader_declares={leaders} ({leaders / (float)n:0.00}/match)");
        sb.Append(" | win% ");
        foreach (var role in new[] { "elephant", "human", "monkey", "cat", "none" })
        {
            int w = wins.TryGetValue(role, out var x) ? x : 0;
            sb.Append($"{role}={100f * w / n:0.0}% ");
        }
        sb.Append("| death% ");
        foreach (var role in new[] { "elephant", "human", "monkey", "cat" })
        {
            int a = appear.TryGetValue(role, out var ap) ? ap : 0;
            int d = deaths.TryGetValue(role, out var de) ? de : 0;
            float rate = a > 0 ? 100f * d / a : 0f;
            sb.Append($"{role}={rate:0.0}% ");
        }
        return sb.ToString();
    }

    private static void AppendJsonl(MatchRecord rec)
    {
        if (string.IsNullOrEmpty(outDir))
            return;
        string path = Path.Combine(outDir, "training_matches.jsonl");
        var diedJson = DictBoolJson(rec.Died);
        string line =
            $"{{\"i\":{rec.Index},\"winner\":{Q(rec.Winner)},\"reason\":{Q(rec.EndReason)},\"rounds\":{rec.FullRounds}," +
            $"\"leader_declares\":{rec.LeaderDeclarations},\"curriculum\":{Q(rec.Curriculum)}," +
            $"\"learning_role\":{Q(rec.LearningRole)},\"died\":{diedJson}}}\n";
        File.AppendAllText(path, line, Encoding.UTF8);
    }

    private static void WriteSummary()
    {
        if (string.IsNullOrEmpty(outDir) || recent.Count == 0)
            return;

        int n = recent.Count;
        var wins = new Dictionary<string, int>();
        var deaths = new Dictionary<string, int>();
        var appear = new Dictionary<string, int>();
        int leaders = 0;
        var reasons = new Dictionary<string, int>();
        float roundsSum = 0f;
        int learningWins = 0;
        string learnRole = null;

        foreach (var m in recent)
        {
            leaders += m.LeaderDeclarations;
            roundsSum += m.FullRounds;
            learnRole = m.LearningRole;
            if (!string.IsNullOrEmpty(m.LearningRole)
                && string.Equals(m.Winner, m.LearningRole, StringComparison.OrdinalIgnoreCase))
                learningWins++;

            wins[m.Winner] = wins.TryGetValue(m.Winner, out var wc) ? wc + 1 : 1;
            reasons[m.EndReason] = reasons.TryGetValue(m.EndReason, out var rc) ? rc + 1 : 1;
            if (m.Died == null)
                continue;
            foreach (var kv in m.Died)
            {
                appear[kv.Key] = appear.TryGetValue(kv.Key, out var a) ? a + 1 : 1;
                if (kv.Value)
                    deaths[kv.Key] = deaths.TryGetValue(kv.Key, out var d) ? d + 1 : 1;
            }
        }

        var sb = new StringBuilder();
        sb.Append("{\n");
        sb.Append($"  \"window\": {n},\n");
        sb.Append($"  \"window_max\": {Window},\n");
        sb.Append($"  \"matches_total\": {matchIndex},\n");
        sb.Append($"  \"rl_decisions_total\": {decisionTotal},\n");
        sb.Append($"  \"avg_full_rounds\": {roundsSum / n:0.###},\n");
        sb.Append($"  \"leader_declarations_sum\": {leaders},\n");
        sb.Append($"  \"leader_declarations_per_match\": {leaders / (float)n:0.###},\n");
        if (!string.IsNullOrEmpty(learnRole))
        {
            sb.Append($"  \"learning_role\": {Q(learnRole)},\n");
            sb.Append($"  \"learning_role_win_rate\": {learningWins / (float)n:0.###},\n");
        }
        sb.Append("  \"win_rate\": {\n");
        AppendRateDict(sb, wins, n, "    ");
        sb.Append("  },\n");
        sb.Append("  \"death_rate\": {\n");
        bool first = true;
        foreach (var role in new[] { "elephant", "human", "monkey", "cat" })
        {
            int a = appear.TryGetValue(role, out var ap) ? ap : 0;
            int d = deaths.TryGetValue(role, out var de) ? de : 0;
            float rate = a > 0 ? d / (float)a : 0f;
            if (!first) sb.Append(",\n");
            first = false;
            sb.Append($"    {Q(role)}: {rate:0.###}");
        }
        sb.Append("\n  },\n");
        sb.Append("  \"end_reasons\": {\n");
        AppendCountDict(sb, reasons, "    ");
        sb.Append("\n  },\n");
        sb.Append($"  \"updated_utc\": {Q(DateTime.UtcNow.ToString("o"))}\n");
        sb.Append("}\n");

        File.WriteAllText(Path.Combine(outDir, "training_stats.json"), sb.ToString(), Encoding.UTF8);
        WriteProgressLite();
    }

    private static void WriteProgressLite()
    {
        EnsureOutDir();
        if (string.IsNullOrEmpty(outDir))
            return;
        try
        {
            var sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append($"  \"rl_decisions_total\": {decisionTotal},\n");
            sb.Append($"  \"matches_total\": {matchIndex},\n");
            sb.Append($"  \"updated_utc\": {Q(DateTime.UtcNow.ToString("o"))}\n");
            sb.Append("}\n");
            File.WriteAllText(Path.Combine(outDir, "training_progress.json"), sb.ToString(), Encoding.UTF8);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[RlStats] progress write failed: {e.Message}");
        }
    }

    private static void AppendRateDict(StringBuilder sb, Dictionary<string, int> counts, int n, string indent)
    {
        bool first = true;
        foreach (var role in new[] { "elephant", "human", "monkey", "cat", "none" })
        {
            int c = counts.TryGetValue(role, out var x) ? x : 0;
            if (!first) sb.Append(",\n");
            first = false;
            sb.Append($"{indent}{Q(role)}: {c / (float)n:0.###}");
        }
        sb.Append('\n');
    }

    private static void AppendCountDict(StringBuilder sb, Dictionary<string, int> counts, string indent)
    {
        bool first = true;
        foreach (var kv in counts)
        {
            if (!first) sb.Append(",\n");
            first = false;
            sb.Append($"{indent}{Q(kv.Key)}: {kv.Value}");
        }
    }

    private static string DictBoolJson(Dictionary<string, bool> d)
    {
        if (d == null || d.Count == 0)
            return "{}";
        var sb = new StringBuilder("{");
        bool first = true;
        foreach (var kv in d)
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append(Q(kv.Key)).Append(':').Append(kv.Value ? "true" : "false");
        }
        sb.Append('}');
        return sb.ToString();
    }

    private static string RoleKey(RoleType r) => r.ToString().ToLowerInvariant();

    private static string Q(string s)
    {
        if (s == null) return "null";
        return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }
}
