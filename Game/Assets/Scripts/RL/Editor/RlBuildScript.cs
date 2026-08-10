#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// 构建 RL 训练用 Player（广场模式：mlagents env_path + num_envs 多开）。
///
/// 用法（菜单）：Tools/RL/构建训练 Player → Tools/rl/build/ElephantCrisis.exe
/// 用法（命令行，供 CI/脚本）：
///   Unity -batchmode -nographics -quit -projectPath Game \
///     -executeMethod RlBuildScript.Build -rlBuildOut Tools/rl/build
///
/// build 出的 exe 直接带 -rlTrain/-rlCurriculum/-rlRole 参数即可被 RlTrainingRunner
/// 的 RuntimeInitializeOnLoadMethod 接管，无需 Editor。
/// </summary>
public static class RlBuildScript
{
    private const string MenuRoot = "Tools/RL";

    public static void Build()
    {
        var path = DoBuild(RlBuildArgs.GetArg("-rlBuildOut", DefaultBuildDir()));
        if (path == null)
            EditorApplication.Exit(1);
        Debug.Log($"[RL Build] OK -> {path}");
    }

    public static string DoBuild(string outDir)
    {
        outDir = outDir.Trim('"');

        var buildDir = Path.GetFullPath(outDir);
        Directory.CreateDirectory(buildDir);

        var scenes = new[] { "Assets/Scenes/SampleScene.unity" };
        var options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = Path.Combine(buildDir, BuildFileName()),
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None,
        };

        var report = BuildPipeline.BuildPlayer(options);
        var result = report.summary.result;
        if (result != BuildResult.Succeeded)
        {
            Debug.LogError($"[RL Build] failed: {result}");
            return null;
        }

        return options.locationPathName;
    }

    private static string BuildFileName()
    {
#if UNITY_EDITOR_WIN
        return "ElephantCrisis.exe";
#else
        return "ElephantCrisis";
#endif
    }

    private static string DefaultBuildDir()
    {
        // Game/Assets -> Game -> 仓库根 -> Tools/rl/build
        var root = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
        return Path.Combine(root, "Tools", "rl", "build");
    }

    [MenuItem(MenuRoot + "/构建训练 Player")]
    public static void BuildFromMenu()
    {
        var path = DoBuild(DefaultBuildDir());
        if (path != null)
            EditorUtility.RevealInFinder(path);
    }
}

/// <summary>命令行参数小工具（避免与 RlTrainingRunner 的静态解析耦合）。</summary>
public static class RlBuildArgs
{
    public static string GetArg(string key, string fallback)
    {
        var args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], key, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }
        return fallback;
    }
}
#endif
