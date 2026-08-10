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
/// 客户端性能采集会话：写 JSONL 到 persistentDataPath/ElephantPerf/session_*/。
/// </summary>
public class ClientPerfSession : MonoBehaviour
{
    public static ClientPerfSession Instance { get; private set; }

    public bool IsActive { get; private set; }
    public string SessionDir { get; private set; }
    public string SessionId { get; private set; }

    private readonly List<string> pending = new List<string>(256);
    private readonly List<(float tMs, string path)> recentLoads = new List<(float, string)>(16);
    private readonly List<(float tMs, string kind)> recentActions = new List<(float, string)>(32);
    private StreamWriter writer;
    private float startRealtime;
    private int hitchCount;
    private int loadCount;
    private int actionCount;
    private float nextMemoryAt;
    private int frameCounter;
    private ClientPerfGameplayProbe gameplayProbe;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Boot()
    {
        ClientPerfSettings.EnsureLoaded();
        bool want = ClientPerfSettings.AutoEnabledByDefault || HasArg("-perfAuto") || HasArg("-perfCollect");
        if (!want)
            return;
        EnsureExists();
    }

    public static ClientPerfSession EnsureExists()
    {
        if (Instance != null)
            return Instance;
        var go = new GameObject("ClientPerfSession");
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<ClientPerfSession>();
        go.AddComponent<ClientPerfFrameProbe>();
        go.AddComponent<ClientPerfGameplayProbe>();
        return Instance;
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

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        if (GetComponent<ClientPerfFrameProbe>() == null)
            gameObject.AddComponent<ClientPerfFrameProbe>();
        if (GetComponent<ClientPerfGameplayProbe>() == null)
            gameObject.AddComponent<ClientPerfGameplayProbe>();
        gameplayProbe = GetComponent<ClientPerfGameplayProbe>();
        ClientPerfSettings.EnsureLoaded();
        BeginSession();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            EndSession("destroy");
            Instance = null;
        }
    }

    private void OnApplicationQuit()
    {
        EndSession("quit");
    }

    public void BeginSession()
    {
        if (IsActive)
            return;
        ClientPerfSettings.EnsureLoaded();
        SessionId = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        string root = Path.Combine(Application.persistentDataPath, "ElephantPerf");
        SessionDir = Path.Combine(root, "session_" + SessionId);
        Directory.CreateDirectory(SessionDir);
        writer = new StreamWriter(Path.Combine(SessionDir, "events.jsonl"), false, Encoding.UTF8) { AutoFlush = true };
        startRealtime = Time.realtimeSinceStartup;
        IsActive = true;
        hitchCount = 0;
        loadCount = 0;
        actionCount = 0;
        frameCounter = 0;
        nextMemoryAt = Time.realtimeSinceStartup + ClientPerfSettings.MemoryIntervalSec;
        EmitRaw($"{{\"t_ms\":0,\"type\":\"session_start\",\"session_id\":\"{SessionId}\",\"platform\":\"{Application.platform}\",\"unity_version\":\"{Application.unityVersion}\",\"product_name\":\"{Escape(Application.productName)}\"}}");
        Debug.Log($"[ClientPerf] session started → {SessionDir}");
    }

    public void EndSession(string reason)
    {
        if (!IsActive)
            return;
        float dur = (Time.realtimeSinceStartup - startRealtime) * 1000f;
        Emit("session_end", $"\"reason\":\"{Escape(reason)}\",\"duration_ms\":{F(dur)},\"hitch_count\":{hitchCount},\"load_count\":{loadCount},\"action_count\":{actionCount}");
        Flush();
        try
        {
            File.WriteAllText(
                Path.Combine(SessionDir, "meta.json"),
                $"{{\"session_id\":\"{SessionId}\",\"dir\":\"{Escape(SessionDir)}\",\"reason\":\"{Escape(reason)}\",\"hitch_count\":{hitchCount},\"load_count\":{loadCount},\"action_count\":{actionCount},\"duration_ms\":{F(dur)}}}",
                Encoding.UTF8);
        }
        catch { /* ignore */ }

        writer?.Dispose();
        writer = null;
        IsActive = false;
        Debug.Log($"[ClientPerf] session ended ({reason}) → {SessionDir}");
    }

    public float NowMs() => (Time.realtimeSinceStartup - startRealtime) * 1000f;

    public void EmitHitch(float dtMs, int frame)
    {
        if (!IsActive)
            return;
        hitchCount++;
        float t = NowMs();
        var loads = BuildRecentLoadsJson(t);
        var actions = BuildRecentActionsJson(t);
        Emit("hitch", $"\"dt_ms\":{F(dtMs)},\"frame\":{frame},\"recent_loads\":{loads},\"recent_actions\":{actions}");
    }

    public void EmitFrame(float dtMs, int frame)
    {
        if (!IsActive)
            return;
        Emit("frame", $"\"dt_ms\":{F(dtMs)},\"frame\":{frame}");
    }

    public void EmitResourceLoad(string path, string assetType, float dtMs, bool ok)
    {
        if (!IsActive)
            return;
        loadCount++;
        float t = NowMs();
        recentLoads.Add((t, path ?? ""));
        while (recentLoads.Count > ClientPerfSettings.MaxRecentLoads)
            recentLoads.RemoveAt(0);
        Emit("resource_load", $"\"path\":\"{Escape(path)}\",\"asset_type\":\"{Escape(assetType)}\",\"dt_ms\":{F(dtMs)},\"ok\":{(ok ? "true" : "false")}");
        if (gameplayProbe != null)
            gameplayProbe.NoteLoad();
    }

    public void EmitGameAction(string kind, string detail = null)
    {
        if (!IsActive || string.IsNullOrEmpty(kind))
            return;
        actionCount++;
        float t = NowMs();
        recentActions.Add((t, kind));
        while (recentActions.Count > ClientPerfSettings.MaxRecentActions)
            recentActions.RemoveAt(0);
        string fields = $"\"kind\":\"{Escape(kind)}\"";
        if (!string.IsNullOrEmpty(detail))
            fields += $",\"detail\":\"{Escape(detail)}\"";
        Emit("game_action", fields);
        if (gameplayProbe != null)
            gameplayProbe.NoteAction();
    }

    public void EmitSecondSample(
        int frames,
        float sumDtMs,
        float maxDtMs,
        int hitches,
        int loads,
        int actions,
        float monoMb,
        float monoDeltaMb)
    {
        if (!IsActive)
            return;
        float avg = frames > 0 ? sumDtMs / frames : 0f;
        float fps = avg > 0.01f ? 1000f / avg : 0f;
        Emit(
            "second_sample",
            $"\"frames\":{frames},\"fps\":{F(fps)},\"avg_dt_ms\":{F(avg)},\"max_dt_ms\":{F(maxDtMs)},\"hitch_count\":{hitches},\"load_count\":{loads},\"action_count\":{actions},\"mono_mb\":{F(monoMb)},\"mono_delta_mb\":{F(monoDeltaMb)}");
    }

    public void TickMemoryIfNeeded()
    {
        if (!IsActive)
            return;
        if (Time.realtimeSinceStartup < nextMemoryAt)
            return;
        nextMemoryAt = Time.realtimeSinceStartup + ClientPerfSettings.MemoryIntervalSec;
        long mono = GC.GetTotalMemory(false);
        long total = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong();
        Emit("memory", $"\"mono_mb\":{F(mono / (1024f * 1024f))},\"total_alloc_mb\":{F(total / (1024f * 1024f))}");
    }

    public void NoteFrame(float dtMs, bool hitch)
    {
        frameCounter++;
        int every = ClientPerfSettings.FrameSampleEvery;
        if (every > 0 && frameCounter % every == 0)
            EmitFrame(dtMs, Time.frameCount);
        if (gameplayProbe != null)
            gameplayProbe.NoteFrame(dtMs, hitch);
    }

    private string BuildRecentLoadsJson(float nowMs)
    {
        float win = ClientPerfSettings.RecentLoadWindowMs;
        var sb = new StringBuilder("[");
        bool first = true;
        for (int i = recentLoads.Count - 1; i >= 0; i--)
        {
            var (t, path) = recentLoads[i];
            if (nowMs - t > win)
                break;
            if (!first)
                sb.Append(',');
            first = false;
            sb.Append('\"').Append(Escape(path)).Append('\"');
        }
        sb.Append(']');
        return sb.ToString();
    }

    private string BuildRecentActionsJson(float nowMs)
    {
        float win = ClientPerfSettings.RecentActionWindowMs;
        var sb = new StringBuilder("[");
        bool first = true;
        for (int i = recentActions.Count - 1; i >= 0; i--)
        {
            var (t, kind) = recentActions[i];
            if (nowMs - t > win)
                break;
            if (!first)
                sb.Append(',');
            first = false;
            sb.Append('\"').Append(Escape(kind)).Append('\"');
        }
        sb.Append(']');
        return sb.ToString();
    }

    private void Emit(string type, string fields)
    {
        EmitRaw($"{{\"t_ms\":{F(NowMs())},\"type\":\"{type}\",{fields}}}");
    }

    private void EmitRaw(string line)
    {
        pending.Add(line);
        if (pending.Count >= 32)
            Flush();
    }

    private void Flush()
    {
        if (writer == null || pending.Count == 0)
            return;
        for (int i = 0; i < pending.Count; i++)
            writer.WriteLine(pending[i]);
        pending.Clear();
    }

    private void LateUpdate()
    {
        Flush();
    }

    private static string Escape(string s)
    {
        if (string.IsNullOrEmpty(s))
            return "";
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    private static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

#if UNITY_EDITOR
    [MenuItem("ElephantCrisis/Perf/Start Collecting Session")]
    private static void MenuStart()
    {
        EnsureExists().BeginSession();
    }

    [MenuItem("ElephantCrisis/Perf/Stop Session")]
    private static void MenuStop()
    {
        if (Instance != null)
            Instance.EndSession("manual");
    }
#endif
}
