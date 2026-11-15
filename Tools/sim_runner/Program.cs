using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

/// <summary>
/// 无头跑批器：不启动 Unity，直接用 dotnet 跑对局。
///
/// 规则来自与 Unity 同一批源码（Simulation + SimpleHeuristicAi），
/// 本文件只负责"把一局拼起来、推到底、落盘"——即宿主职责。
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        int matches = Arg(args, "-matches", 10);
        int seedStart = Arg(args, "-seed", 50000);
        int maxRounds = Arg(args, "-maxRounds", 0);
        string collect = ArgStr(args, "-collect", "balance");
        string outRoot = ArgStr(args, "-out", Path.Combine(FindRepoRoot(), "Tools/sim/output/runner"));

        // ---- 宿主服务注入（核心不认识 Unity，也不猜环境）
        GameRulesConfig.ConfigDirectory = Path.Combine(
            FindRepoRoot(), "Game/Assets/StreamingAssets/Config");
        GameRulesConfig.JsonReader = json => JsonSerializer.Deserialize<GameRulesConfig.ConfigDocument>(
            json, new JsonSerializerOptions { IncludeFields = true });
        Log.Info = Console.WriteLine;
        Log.Warning = m => Console.Error.WriteLine("[warn] " + m);
        Log.Error = m => Console.Error.WriteLine("[error] " + m);

        GameRulesConfig.EnsureLoaded();

        int rounds = maxRounds > 0 ? maxRounds : GameRulesConfig.MaxFullRounds;
        Directory.CreateDirectory(outRoot);

        var results = new List<(int seed, string winner, string reason, int rounds)>();
        var started = DateTime.UtcNow;

        for (int i = 0; i < matches; i++)
        {
            int seed = seedStart + i;
            var r = RunOne(seed, rounds, collect, outRoot);
            results.Add(r);
            Console.WriteLine($"[{i + 1}/{matches}] seed={seed} winner={r.winner ?? "-"} reason={r.reason} rounds={r.rounds}");
        }

        var elapsed = DateTime.UtcNow - started;
        Console.WriteLine($"完成 {matches} 局，用时 {elapsed.TotalSeconds:0.0}s（{elapsed.TotalSeconds / Math.Max(1, matches):0.00}s/局）");

        WriteSummary(outRoot, results, elapsed);
        return 0;
    }

    private static (int seed, string winner, string reason, int rounds) RunOne(
        int seed, int maxRounds, string collect, string outRoot)
    {
        Rng.Reset(seed);
        MatchConfig.SetLogicSim();

        var strategies = new Dictionary<string, string>
        {
            { "elephant", "game_heuristic" },
            { "human", "game_heuristic" },
            { "monkey", "game_heuristic" },
            { "cat", "game_heuristic" }
        };
        var logger = new LogicMatchLogger(seed, strategies, collect);

        AssembleMatch();

        MatchDriver.PumpToEnd(SimpleHeuristicAi.TryActOnce, maxRounds);

        string matchDir = Path.Combine(outRoot, $"match_{seed}");
        if (Directory.Exists(matchDir))
            Directory.Delete(matchDir, true);
        Directory.CreateDirectory(matchDir);
        logger.Save(matchDir);

        string winner = GameManager.Instance?.WinnerRole.HasValue == true
            ? LogicSimNaming.Role(GameManager.Instance.WinnerRole.Value)
            : null;
        string reason = GameManager.Instance?.EndReason ?? "unknown";
        int fullRounds = TurnManager.Instance != null
            ? Math.Max(0, TurnManager.Instance.RoundNumber - 1)
            : 0;

        // 局间清理：Unity 侧由 TeardownForLogicSim 做，这里是普通对象，置空即可
        LogicMatchLogger.ClearActive();
        MatchConfig.Clear();
        ResetInstances();

        return (seed, winner, reason, fullRounds);
    }

    /// <summary>拼一局：与 Unity 宿主同一段序列（MatchSetup）。</summary>
    private static void AssembleMatch()
    {
        ResetInstances();
        MatchSetup.AssembleLean();
    }

    private static void ResetInstances()
    {
        GridManager.Instance = null;
        TurnManager.Instance = null;
        GameManager.Instance = null;
        GroundItemManager.Instance = null;
        HazardManager.Instance = null;
        DeckManager.Instance = null;
    }

    private static void WriteSummary(
        string outRoot, List<(int seed, string winner, string reason, int rounds)> results, TimeSpan elapsed)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("{\n  \"runner\": \"dotnet\",\n  \"matches\": [\n");
        for (int i = 0; i < results.Count; i++)
        {
            var r = results[i];
            sb.Append($"    {{ \"seed\": {r.seed}, \"winner\": {(r.winner == null ? "null" : "\"" + r.winner + "\"")}, ")
              .Append($"\"reason\": \"{r.reason}\", \"full_rounds\": {r.rounds} }}");
            sb.Append(i == results.Count - 1 ? "\n" : ",\n");
        }
        sb.Append("  ],\n");
        sb.Append($"  \"elapsed_seconds\": {elapsed.TotalSeconds:0.0}\n}}\n");
        File.WriteAllText(Path.Combine(outRoot, "runner_summary.json"), sb.ToString());
    }

    // ------------------------------------------------------------ 参数

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Game/Assets/Scripts")))
            dir = dir.Parent;
        return dir?.FullName ?? Directory.GetCurrentDirectory();
    }

    private static int Arg(string[] args, string name, int fallback)
        => int.TryParse(ArgStr(args, name, null), out int v) ? v : fallback;

    private static string ArgStr(string[] args, string name, string fallback)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }
        return fallback;
    }
}
