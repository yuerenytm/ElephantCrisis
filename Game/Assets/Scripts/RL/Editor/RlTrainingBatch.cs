#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// 命令行：Unity -batchmode -executeMethod RlTrainingBatch.Run -rlTrain ...
/// </summary>
public static class RlTrainingBatch
{
    public static void Run()
    {
        if (!RlTrainingRunner.HasArg("-rlTrain") && !RlTrainingRunner.HasArg("-rlEval"))
        {
            Debug.LogError("[RL] RlTrainingBatch.Run requires -rlTrain or -rlEval");
            EditorApplication.Exit(2);
            return;
        }

        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        if (!EditorApplication.isPlaying)
            EditorApplication.isPlaying = true;
    }

    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredEditMode)
            return;
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        Debug.Log("[RL] PlayMode ended, exiting Editor");
        EditorApplication.Exit(0);
    }
}
#endif
