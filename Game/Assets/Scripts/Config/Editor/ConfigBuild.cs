#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// 配置构建入口：把 Design/*.csv 生成为 game_rules.json。
///
/// 解决的是"改了表忘了跑脚本，游戏静默读旧值"这个问题：
/// - 菜单：Tools/象群危机/重新生成配置 —— 手动强制跑一次
/// - 进 PlayMode 前：若任一 CSV 比 json 新，自动跑；失败则**阻止进入 Play**
///
/// 之所以能自动跑：工程是代码自举、无场景装配，进 Play 前的钩子足够早，
/// 不会出现"半局已加载旧配置"的情况。
/// </summary>
[InitializeOnLoad]
public static class ConfigBuild
{
    private const string MenuRoot = "Tools/象群危机/";
    private const int TimeoutMs = 60000;

    /// <summary>仓库根（Game/Assets 上两级）。</summary>
    private static string RepoRoot =>
        Directory.GetParent(Directory.GetParent(Application.dataPath).FullName).FullName;

    private static string ScriptsPath => Path.Combine(RepoRoot, "Tools", "config", "build.py");
    private static string TablesDir => Path.Combine(RepoRoot, "Design");
    private static string OutputPath =>
        Path.Combine(Application.dataPath, "StreamingAssets", "Config", "game_rules.json");

    static ConfigBuild()
    {
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    // ------------------------------------------------------------ 钩子

    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.ExitingEditMode)
            return;

        if (!TableIsNewerThanOutput())
            return; // 表没动过，不必每次进 Play 都跑

        var (code, output) = Run();
        if (code == 0)
        {
            Debug.Log("[ConfigBuild] 表比配置新，已在进入 Play 前自动重新生成。");
            AssetDatabase.Refresh();
            return;
        }

        EditorApplication.isPlaying = false; // 阻止进入 PlayMode
        Debug.LogError($"[ConfigBuild] 配置生成失败，已阻止进入 Play：\n{output}");
        EditorUtility.DisplayDialog(
            "配置生成失败",
            $"Design/*.csv 校验未通过，已阻止进入 Play（避免跑旧配置）。\n\n{Trim(output)}",
            "好");
    }

    /// <summary>任一 CSV 的修改时间晚于输出 json。</summary>
    private static bool TableIsNewerThanOutput()
    {
        if (!File.Exists(OutputPath))
            return true;
        var outputTime = File.GetLastWriteTimeUtc(OutputPath);
        if (!Directory.Exists(TablesDir))
            return false;
        foreach (var file in Directory.GetFiles(TablesDir, "*.csv"))
        {
            if (File.GetLastWriteTimeUtc(file) > outputTime)
                return true;
        }
        return false;
    }

    // ------------------------------------------------------------ 菜单

    [MenuItem(MenuRoot + "重新生成配置（Design/*.csv → json）", false, 10)]
    public static void BuildManually()
    {
        var (code, output) = Run();
        if (code == 0)
        {
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("配置已生成", Trim(output), "好");
        }
        else
        {
            Debug.LogError($"[ConfigBuild] 生成失败：\n{output}");
            EditorUtility.DisplayDialog("配置生成失败", Trim(output), "好");
        }
    }

    [MenuItem(MenuRoot + "打开策划表目录（Design/）", false, 11)]
    public static void RevealTables()
    {
        if (Directory.Exists(TablesDir))
            EditorUtility.RevealInFinder(TablesDir);
        else
            EditorUtility.DisplayDialog("找不到目录", TablesDir, "好");
    }

    [MenuItem(MenuRoot + "检查配置与表是否同步", false, 12)]
    public static void CheckSync()
    {
        bool stale = TableIsNewerThanOutput();
        EditorUtility.DisplayDialog(
            stale ? "表比配置新" : "已同步",
            stale ? "Design/*.csv 有改动但未生成配置，点菜单里的「重新生成配置」。" : "配置是最新的。",
            "好");
    }

    // ------------------------------------------------------------ 执行

    /// <summary>调用 python 跑 build.py；返回 (退出码, 合并输出)。</summary>
    private static (int code, string output) Run()
    {
        if (!File.Exists(ScriptsPath))
            return (2, $"找不到构建脚本：{ScriptsPath}");

        foreach (var exe in new[] { "python", "py" })
        {
            var result = TryRun(exe, $"\"{ScriptsPath}\"");
            if (result.code != -1) // -1 = 该可执行文件不存在
                return result;
        }
        return (2, "找不到 python。请确认 python 在 PATH 中，或手动运行：\n" +
                   $"  cd \"{RepoRoot}\"\n  python Tools/config/build.py");
    }

    private static (int code, string output) TryRun(string exe, string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = arguments,
                WorkingDirectory = RepoRoot,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };

            using var process = Process.Start(psi);
            if (process == null)
                return (2, $"无法启动 {exe}");

            string stdout = process.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();
            if (!process.WaitForExit(TimeoutMs))
            {
                try { process.Kill(); } catch { /* 已退出 */ }
                return (3, "构建超时（60 秒）：\n" + stdout + stderr);
            }
            return (process.ExitCode, (stdout + stderr).Trim());
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return (-1, ""); // 该可执行文件不在 PATH
        }
        catch (Exception e)
        {
            return (2, e.Message);
        }
    }

    private static string Trim(string text, int max = 1200)
        => string.IsNullOrEmpty(text) ? "（无输出）"
           : text.Length <= max ? text : text.Substring(0, max) + "\n……";
}
#endif
