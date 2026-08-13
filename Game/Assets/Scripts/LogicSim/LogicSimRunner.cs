using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// 命令行 -logicSim：进程内连跑 N 局全 AI 逻辑仿真，写出 match_*/events.jsonl。
/// </summary>
public class LogicSimRunner : MonoBehaviour
{
    public static bool IsRequested { get; private set; }

    private int matches = 1;
    private int baseSeed = 1;
    private int maxFullRounds = GameRulesConfig.MaxFullRounds;
    private string outRoot;
    private string collect = "both";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        IsRequested = HasArg("-logicSim");
        if (!IsRequested)
            return;

        var go = new GameObject("LogicSimRunner");
        DontDestroyOnLoad(go);
        go.AddComponent<LogicSimRunner>();
    }

    private void Start()
    {
        ParseArgs();
        StartCoroutine(CoRunBatch());
    }

    private void ParseArgs()
    {
        matches = Mathf.Max(1, GetIntArg("-matches", 1));
        baseSeed = GetIntArg("-seed", 1);
        maxFullRounds = Mathf.Max(5, GetIntArg("-maxRounds", GameRulesConfig.MaxFullRounds));
        outRoot = GetStringArg("-out", null);
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
        collect = GetStringArg("-collect", "both").ToLowerInvariant();
        if (collect != "balance" && collect != "logic" && collect != "both")
            collect = "both";
        Debug.Log($"[LogicSim] out={outRoot} matches={matches} seed={baseSeed} maxRounds={maxFullRounds} collect={collect}");
    }

    private IEnumerator CoRunBatch()
    {
        yield return null;
        yield return null;

        float wait = 0f;
        while (GameBootstrap.Instance == null && wait < 10f)
        {
            wait += Time.unscaledDeltaTime;
            yield return null;
        }

        if (GameBootstrap.Instance == null)
        {
            Debug.LogError("[LogicSim] GameBootstrap missing");
            Quit(1);
            yield break;
        }

        MainMenuUI.Instance?.Hide();
        Time.timeScale = 20f;

        var results = new List<Dictionary<string, object>>();
        var t0 = Time.realtimeSinceStartup;

        for (int i = 0; i < matches; i++)
        {
            int seed = baseSeed + i;
            Debug.Log($"[LogicSim] === match {i + 1}/{matches} seed={seed} ===");
            yield return CoRunOne(seed, results);
            yield return null;
        }

        WriteBatchSummary(results, Time.realtimeSinceStartup - t0);
        Debug.Log($"[LogicSim] done {matches} matches in {Time.realtimeSinceStartup - t0:0.00}s → {outRoot}");
        Time.timeScale = 1f;
        Quit(0);
    }

    private IEnumerator CoRunOne(int seed, List<Dictionary<string, object>> results)
    {
        UnityEngine.Random.InitState(seed);

        var strategies = new Dictionary<string, string>
        {
            { "elephant", "game_heuristic" },
            { "human", "game_heuristic" },
            { "monkey", "game_heuristic" },
            { "cat", "game_heuristic" }
        };

        var logger = new LogicMatchLogger(seed, strategies, collect);
        MatchConfig.SetLogicSim();
        GameBootstrap.Instance.StartLogicSimMatch();

        float deadline = Time.realtimeSinceStartup + 180f;
        while (true)
        {
            if (GameManager.Instance != null && GameManager.Instance.IsGameOver)
                break;

            var tm = TurnManager.Instance;
            if (tm != null)
            {
                int full = Mathf.Max(0, tm.RoundNumber - 1);
                if (full >= maxFullRounds)
                {
                    GameManager.Instance?.ForceEndForLogicSim("max_rounds");
                    break;
                }
            }

            if (Time.realtimeSinceStartup > deadline)
            {
                GameManager.Instance?.ForceEndForLogicSim("timeout");
                break;
            }

            yield return null;
        }

        string matchDir = Path.Combine(outRoot, $"match_{seed}");
        if (Directory.Exists(matchDir))
            Directory.Delete(matchDir, true);

        // EndGame 已 FinishMatch；若异常未写则补
        if (GameManager.Instance != null && GameManager.Instance.IsGameOver
            && !string.IsNullOrEmpty(GameManager.Instance.EndReason))
        {
            // already finished inside EndGame
        }

        logger.Save(matchDir);

        string winner = GameManager.Instance?.WinnerRole.HasValue == true
            ? LogicSimNaming.Role(GameManager.Instance.WinnerRole.Value)
            : null;
        string reason = GameManager.Instance?.EndReason ?? "unknown";
        int fullRounds = TurnManager.Instance != null
            ? Mathf.Max(0, TurnManager.Instance.RoundNumber - 1)
            : 0;

        results.Add(new Dictionary<string, object>
        {
            { "seed", seed },
            { "winner", winner },
            { "reason", reason },
            { "full_rounds", fullRounds },
            { "path", matchDir.Replace("\\", "/") }
        });

        GameBootstrap.Instance.TeardownForLogicSim();
        MatchConfig.Clear();
        LogicMatchLogger.ClearActive();
    }

    private void WriteBatchSummary(List<Dictionary<string, object>> results, float elapsed)
    {
        var path = Path.Combine(outRoot, "batch_summary.json");
        var sb = new StringBuilder();
        sb.Append("{\n");
        sb.Append("  \"source\": \"game_logic_sim\",\n");
        sb.Append("  \"elapsed_sec\": ").Append(elapsed.ToString("0.###", CultureInfo.InvariantCulture)).Append(",\n");
        sb.Append("  \"matches\": ").Append(results.Count).Append(",\n");
        sb.Append("  \"results\": [\n");
        for (int i = 0; i < results.Count; i++)
        {
            var r = results[i];
            string w = r["winner"] != null ? "\"" + r["winner"] + "\"" : "null";
            sb.Append("    {\"seed\":").Append(r["seed"])
                .Append(",\"winner\":").Append(w)
                .Append(",\"reason\":\"").Append(r["reason"]).Append("\"")
                .Append(",\"full_rounds\":").Append(r["full_rounds"])
                .Append(",\"path\":\"").Append(r["path"]).Append("\"}");
            sb.Append(i + 1 < results.Count ? ",\n" : "\n");
        }
        sb.Append("  ]\n}\n");
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }

    private static void Quit(int code)
    {
#if UNITY_EDITOR
        EditorApplication.isPlaying = false;
#else
        Application.Quit(code);
#endif
    }

    public static bool HasArg(string flag)
    {
        var args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

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
