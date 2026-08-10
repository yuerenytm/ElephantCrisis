using System.Collections.Generic;
using System.IO;
using Unity.MLAgents.Policies;
using UnityEngine;

/// <summary>
/// Checkpoint league：登记历史模型路径，SelfPlay 中按比例将非学习席标为幽灵。
/// 命令行：-rlLeagueDir &lt;path&gt;，-rlGhostRatio 0~1。
/// 实际 NN 赋值优先走 mlagents self_play；本类提供目录扫描与座位策略钩子。
/// </summary>
public static class RlLeagueSampler
{
    private static readonly List<string> ModelPaths = new List<string>();
    private static float ghostRatio = 0.3f;
    private static string leagueDir;
    private static bool configured;

    public static IReadOnlyList<string> RegisteredModels => ModelPaths;

    public static void EnsureConfigured()
    {
        if (configured)
            return;
        configured = true;
        leagueDir = GetArgValue("-rlLeagueDir");
        if (float.TryParse(GetArgValue("-rlGhostRatio"), out var r))
            ghostRatio = Mathf.Clamp01(r);
        RefreshModelList();
    }

    public static void RefreshModelList()
    {
        ModelPaths.Clear();
        if (string.IsNullOrEmpty(leagueDir) || !Directory.Exists(leagueDir))
            return;
        foreach (var f in Directory.GetFiles(leagueDir, "*.onnx"))
            ModelPaths.Add(f);
        foreach (var f in Directory.GetFiles(leagueDir, "*.nn"))
            ModelPaths.Add(f);
        Debug.Log($"[RlLeague] models={ModelPaths.Count} dir={leagueDir}");
    }

    public static bool ShouldUseGhost(RoleType role)
    {
        EnsureConfigured();
        if (ModelPaths.Count == 0)
            return false;
        if (role == RoleType.Human)
            return false;
        return Random.value < ghostRatio;
    }

    public static bool TryAssignModel(BehaviorParameters bp)
    {
        EnsureConfigured();
        if (bp == null || ModelPaths.Count == 0)
            return false;
        string path = ModelPaths[Random.Range(0, ModelPaths.Count)];
        Debug.Log($"[RlLeague] ghost seat → {path} (load via ML-Agents self_play / ModelAsset)");
        return false;
    }

    private static string GetArgValue(string key)
    {
        var args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], key, System.StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }
        return null;
    }
}
