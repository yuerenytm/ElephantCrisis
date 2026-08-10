#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>命令行：Unity -executeMethod PerfAutoEditorBatch.Run -perfAuto</summary>
public static class PerfAutoEditorBatch
{
    public static void Run()
    {
        if (!ClientPerfSession.HasArg("-perfAuto") && !ClientPerfSession.HasArg("-perfCollect"))
        {
            Debug.LogError("[Perf] PerfAutoEditorBatch.Run requires -perfAuto or -perfCollect");
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
        EditorApplication.Exit(0);
    }
}
#endif
