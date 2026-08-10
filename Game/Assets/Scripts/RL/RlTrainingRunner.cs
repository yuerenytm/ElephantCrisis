using System;
using System.Collections;
using System.Globalization;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// 命令行 -rlTrain / -rlEval：进入 RL lean 对局并由 RlMatchController 驱动。
/// </summary>
public class RlTrainingRunner : MonoBehaviour
{
    public static bool IsRequested { get; private set; }

    private RlMatchController.Curriculum curriculum = RlMatchController.Curriculum.Bootstrap;
    private RoleType learningRole = RoleType.Human;
    private int maxEpisodes = 0; // 0 = unlimited (train)
    private int episodesDone;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        IsRequested = HasArg("-rlTrain") || HasArg("-rlEval");
        if (!IsRequested)
            return;

        var go = new GameObject("RlTrainingRunner");
        DontDestroyOnLoad(go);
        go.AddComponent<RlTrainingRunner>();
    }

    private void Start()
    {
        ParseArgs();
        StartCoroutine(CoBoot());
    }

    private void ParseArgs()
    {
        maxEpisodes = GetIntArg("-episodes", HasArg("-rlEval") ? 20 : 0);
        int seed = GetIntArg("-seed", 1);
        UnityEngine.Random.InitState(seed);
        string cur = GetStringArg("-rlCurriculum", "bootstrap");
        curriculum = cur != null && cur.Equals("selfplay", StringComparison.OrdinalIgnoreCase)
            ? RlMatchController.Curriculum.SelfPlay
            : RlMatchController.Curriculum.Bootstrap;
        string role = GetStringArg("-rlRole", "human");
        learningRole = ParseRole(role);
        RlLeagueSampler.EnsureConfigured();
        Debug.Log($"[RL] curriculum={curriculum} role={learningRole} seed={seed} episodes={maxEpisodes} (no maxRounds; lava decides)");
    }

    private IEnumerator CoBoot()
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
            Debug.LogError("[RL] GameBootstrap missing");
            Quit(1);
            yield break;
        }

        MainMenuUI.Instance?.Hide();
        Time.timeScale = HasArg("-rlEval") ? 5f : 20f;

        MatchConfig.SetRlTraining();
        GameBootstrap.Instance.StartRlTrainingMatch();
        yield return null;

        var ctrl = RlMatchController.Instance;
        if (ctrl == null)
        {
            var go = GameBootstrap.Instance.gameObject;
            ctrl = go.GetComponent<RlMatchController>() ?? go.AddComponent<RlMatchController>();
        }

        ctrl.Configure(curriculum, learningRole);
        ctrl.BeginMatchSession();

        if (maxEpisodes > 0)
            StartCoroutine(CoStopAfterEpisodes());
    }

    private IEnumerator CoStopAfterEpisodes()
    {
        // 粗略：按时间与对局结束次数；评估时用固定局数后退出
        while (episodesDone < maxEpisodes)
        {
            yield return new WaitForSecondsRealtime(0.5f);
            if (GameManager.Instance != null && GameManager.Instance.IsGameOver)
            {
                episodesDone++;
                Debug.Log($"[RL] eval episode {episodesDone}/{maxEpisodes} winner={GameManager.Instance.WinnerRole} reason={GameManager.Instance.EndReason}");
                // 等待控制器开下一局
                yield return new WaitForSecondsRealtime(0.2f);
            }
        }

        Debug.Log("[RL] eval finished");
        Quit(0);
    }

    private static RoleType ParseRole(string s)
    {
        if (string.IsNullOrEmpty(s))
            return RoleType.Human;
        s = s.Trim().ToLowerInvariant();
        return s switch
        {
            "elephant" => RoleType.Elephant,
            "monkey" => RoleType.Monkey,
            "cat" => RoleType.Cat,
            _ => RoleType.Human
        };
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

    public static int GetIntArg(string key, int fallback)
    {
        string s = GetStringArg(key, null);
        if (s != null && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v))
            return v;
        return fallback;
    }

    public static string GetStringArg(string key, string fallback)
    {
        var args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], key, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }
        return fallback;
    }

    private static void Quit(int code)
    {
#if UNITY_EDITOR
        EditorApplication.isPlaying = false;
#else
        Application.Quit(code);
#endif
    }
}
