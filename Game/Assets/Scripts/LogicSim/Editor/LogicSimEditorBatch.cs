#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// 供命令行：Unity -batchmode -executeMethod LogicSimEditorBatch.Run ...
/// 进入 PlayMode，由 LogicSimRunner 连跑后退出。
/// </summary>
public static class LogicSimEditorBatch
{
    public static void Run()
    {
        if (!LogicSimRunner.HasArg("-logicSim"))
        {
            Debug.LogError("[LogicSim] EditorBatch.Run requires -logicSim in command line args");
            EditorApplication.Exit(2);
            return;
        }

        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        // Unity 6 部分环境下无 EnterPlayMode API；用 isPlaying 进入
        if (!EditorApplication.isPlaying)
            EditorApplication.isPlaying = true;
    }

    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredEditMode)
            return;
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        Debug.Log("[LogicSim] PlayMode ended, exiting Editor");
        EditorApplication.Exit(0);
    }
}
#endif
