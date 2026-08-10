using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// 包装 Resources.Load，记录加载耗时供客户端性能分析。
/// </summary>
public static class ClientPerfResourceProbe
{
    public static T Load<T>(string path) where T : Object
    {
        var sw = Stopwatch.StartNew();
        T asset = null;
        try
        {
            asset = Resources.Load<T>(path);
        }
        finally
        {
            sw.Stop();
            bool ok = asset != null;
            float ms = (float)sw.Elapsed.TotalMilliseconds;
            if (ClientPerfSession.Instance != null && ClientPerfSession.Instance.IsActive)
                ClientPerfSession.Instance.EmitResourceLoad(path, typeof(T).Name, ms, ok);
            else if (ms >= 5f)
                Debug.Log($"[ClientPerf] Resources.Load {path} ({typeof(T).Name}) {ms:F2}ms ok={ok}");
        }
        return asset;
    }
}
