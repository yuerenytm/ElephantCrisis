using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// 命令行 -logicSim：连跑 N 局全 AI 对局，写出 match_*&#47;events.jsonl 与 batch_summary.json。
///
/// 它只是"宿主"：装配与推进都调核心（MatchSetup / MatchDriver），
/// 与 Tools/sim_runner（dotnet）走同一条路，因此两者可以逐局对齐。
/// 整局是同步跑完的，不需要协程。
/// </summary>
public class LogicSimRunner : MonoBehaviour
{
    private static readonly Dictionary<string, string> Strategies = new Dictionary<string, string>
    {
        { "elephant", "game_heuristic" },
        { "human", "game_heuristic" },
        { "monkey", "game_heuristic" },
        { "cat", "game_heuristic" }
    };

    public static bool IsRequested { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        IsRequested = HasArg("-logicSim");
        BootFlags.HeadlessRequested = IsRequested;
        if (!IsRequested)
            return;

        var go = new GameObject("LogicSimRunner");
        DontDestroyOnLoad(go);
        go.AddComponent<LogicSimRunner>();
    }

    private void Start()
    {
        int code = RunBatch();
        Quit(code);
    }

    // ------------------------------------------------------------ 一轮跑批

    private struct Options
    {
        public int Matches;
        public int Seed;
        public int MaxRounds;
        public string OutRoot;
        public string Collect;
    }

    private struct Row
    {
        public int Seed;
        public string Winner;
        public string Reason;
        public int Rounds;
        public string Path;
    }

    private int RunBatch()
    {
        var opt = ReadOptions();
        if (GameBootstrap.Instance == null)
        {
            Debug.LogError("[LogicSim] 缺少 GameBootstrap，无法启动");
            return 1;
        }

        var rows = new List<Row>(opt.Matches);
        var started = DateTime.UtcNow;

        for (int i = 0; i < opt.Matches; i++)
        {
            int seed = opt.Seed + i;
            Debug.Log($"[LogicSim] match {i + 1}/{opt.Matches} seed={seed}");
            rows.Add(RunOne(seed, opt));
        }

        WriteSummary(rows, (DateTime.UtcNow - started).TotalSeconds, opt.OutRoot);
        Debug.Log($"[LogicSim] 完成 {opt.Matches} 局 → {opt.OutRoot}");
        return 0;
    }

    private Row RunOne(int seed, Options opt)
    {
        Rng.Reset(seed);                    // 逻辑随机流（确定性）
        UnityEngine.Random.InitState(seed); // 表现随机流，与结算无关

        var logger = new LogicMatchLogger(seed, Strategies, opt.Collect);
        MatchConfig.SetLogicSim();

        MatchSetup.ResetAll();                                  // 每局一世界
        MatchSetup.AssembleLean();
        MatchDriver.PumpToEnd(SimpleHeuristicAi.TryActOnce, opt.MaxRounds);

        string matchDir = Path.Combine(opt.OutRoot, $"match_{seed}");
        if (Directory.Exists(matchDir))
            Directory.Delete(matchDir, true);
        Directory.CreateDirectory(matchDir);
        logger.Save(matchDir);

        var row = new Row
        {
            Seed = seed,
            Winner = GameManager.Instance?.WinnerRole.HasValue == true
                ? LogicSimNaming.Role(GameManager.Instance.WinnerRole.Value)
                : null,
            Reason = GameManager.Instance?.EndReason ?? "unknown",
            Rounds = TurnManager.Instance != null
                ? Math.Max(0, TurnManager.Instance.RoundNumber - 1)
                : 0,
            Path = matchDir.Replace("\\", "/")
        };

        LogicMatchLogger.ClearActive();
        MatchConfig.Clear();
        return row;
    }

    private Options ReadOptions()
    {
        string outRoot = GetStringArg("-out", null);
        if (string.IsNullOrEmpty(outRoot))
        {
            var data = Application.dataPath;
            var gameRoot = Directory.GetParent(data)?.FullName;
            var repoRoot = gameRoot != null ? Directory.GetParent(gameRoot)?.FullName : null;
            outRoot = repoRoot != null
                ? Path.Combine(repoRoot, "Tools", "sim", "output")
                : Path.Combine(Application.persistentDataPath, "LogicSim");
        }
        outRoot = Path.GetFullPath(outRoot);
        Directory.CreateDirectory(outRoot);

        string collect = GetStringArg("-collect", "both").ToLowerInvariant();
        if (collect != "balance" && collect != "logic" && collect != "both")
            collect = "both";

        var opt = new Options
        {
            Matches = Math.Max(1, GetIntArg("-matches", 1)),
            Seed = GetIntArg("-seed", 1),
            MaxRounds = Math.Max(5, GetIntArg("-maxRounds", GameRulesConfig.MaxFullRounds)),
            OutRoot = outRoot,
            Collect = collect
        };

        Debug.Log($"[LogicSim] out={opt.OutRoot} matches={opt.Matches} seed={opt.Seed} " +
                  $"maxRounds={opt.MaxRounds} collect={opt.Collect}");
        return opt;
    }

    // ------------------------------------------------------------ 落盘

    private void WriteSummary(List<Row> rows, double elapsed, string outRoot)
    {
        var sb = new StringBuilder();
        sb.Append("{\n");
        sb.Append("  \"source\": \"game_logic_sim\",\n");
        sb.Append("  \"elapsed_sec\": ")
          .Append(elapsed.ToString("0.###", CultureInfo.InvariantCulture)).Append(",\n");
        sb.Append("  \"matches\": ").Append(rows.Count).Append(",\n");
        sb.Append("  \"results\": [\n");

        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            string winner = r.Winner != null ? "\"" + r.Winner + "\"" : "null";
            sb.Append("    {\"seed\":").Append(r.Seed)
              .Append(",\"winner\":").Append(winner)
              .Append(",\"reason\":\"").Append(r.Reason).Append("\"")
              .Append(",\"full_rounds\":").Append(r.Rounds)
              .Append(",\"path\":\"").Append(r.Path).Append("\"}");
            sb.Append(i + 1 < rows.Count ? ",\n" : "\n");
        }

        sb.Append("  ]\n}\n");
        File.WriteAllText(Path.Combine(outRoot, "batch_summary.json"),
            sb.ToString(), new UTF8Encoding(false));
    }

    private static void Quit(int code)
    {
#if UNITY_EDITOR
        EditorApplication.isPlaying = false;
#else
        Application.Quit(code);
#endif
    }

    // ------------------------------------------------------------ 命令行

    /// <summary>命令行里是否带某标志。</summary>
    public static bool HasArg(string flag)
    {
        var args = Environment.GetCommandLineArgs();
        foreach (var a in args)
        {
            if (string.Equals(a, flag, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>读整数参数，缺省用 fallback。</summary>
    public static int GetIntArg(string flag, int fallback)
    {
        var args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase)
                && int.TryParse(args[i + 1], out int v))
                return v;
        }
        return fallback;
    }

    /// <summary>读字符串参数，缺省用 fallback。</summary>
    public static string GetStringArg(string flag, string fallback)
    {
        var args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }
        return fallback;
    }
}
