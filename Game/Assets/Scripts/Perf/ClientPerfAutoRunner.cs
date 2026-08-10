using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// 命令行 -perfAuto：自动开始 AI 对局并采集性能；超时或回合上限后结束会话。
/// </summary>
public class ClientPerfAutoRunner : MonoBehaviour
{
    private float deadline = -1f;
    private bool matchStarted;
    private bool finishing;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        ClientPerfSettings.EnsureLoaded();
        bool auto = ClientPerfSettings.AutoEnabledByDefault || ClientPerfSession.HasArg("-perfAuto");
        if (!auto)
            return;

        var go = new GameObject("ClientPerfAutoRunner");
        DontDestroyOnLoad(go);
        go.AddComponent<ClientPerfAutoRunner>();
    }

    private void Start()
    {
        ClientPerfSession.EnsureExists();
        StartCoroutine(CoRun());
    }

    private IEnumerator CoRun()
    {
        // 等 Bootstrap / 主菜单就绪
        yield return null;
        yield return null;

        float wait = 0f;
        while (GameBootstrap.Instance == null && wait < 5f)
        {
            wait += Time.unscaledDeltaTime;
            yield return null;
        }

        if (GameBootstrap.Instance == null)
        {
            Debug.LogError("[ClientPerf] GameBootstrap missing, abort auto run");
            yield break;
        }

        var role = ParseRole(ClientPerfSettings.AutoHumanRole);
        Debug.Log($"[ClientPerf] Auto starting AI battle as {role}");
        GameBootstrap.Instance.StartAiBattle(role);
        matchStarted = true;
        deadline = Time.realtimeSinceStartup + ClientPerfSettings.AutoMaxSeconds;

        while (!finishing)
        {
            if (deadline > 0 && Time.realtimeSinceStartup >= deadline)
            {
                Finish("timeout");
                yield break;
            }

            if (TurnManager.Instance != null)
            {
                // RoundNumber 从 1 起，完整回合推进后递增
                int rounds = Mathf.Max(0, TurnManager.Instance.RoundNumber - 1);
                if (rounds >= ClientPerfSettings.AutoMaxFullRounds)
                {
                    Finish("max_rounds");
                    yield break;
                }
            }

            if (GameManager.Instance != null && GameManager.Instance.IsGameOver)
            {
                Finish("match_end");
                yield break;
            }

            yield return null;
        }
    }

    private void Finish(string reason)
    {
        if (finishing)
            return;
        finishing = true;
        if (ClientPerfSession.Instance != null)
            ClientPerfSession.Instance.EndSession(reason);
        Debug.Log($"[ClientPerf] Auto run finished: {reason}");
#if UNITY_EDITOR
        // 命令行 PerfAutoEditorBatch 依赖退出 PlayMode；手动 Play 时同样结束会话即可
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private static RoleType ParseRole(string name)
    {
        if (Enum.TryParse(name, true, out RoleType role))
            return role;
        return RoleType.Human;
    }
}
