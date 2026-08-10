using UnityEngine;

/// <summary>
/// 逐帧探测卡顿：超过 hitch_ms 记 hitch，并附带最近 resource_load / game_action。
/// </summary>
public class ClientPerfFrameProbe : MonoBehaviour
{
    private void Update()
    {
        var session = ClientPerfSession.Instance;
        if (session == null || !session.IsActive)
            return;

        float dtMs = Time.unscaledDeltaTime * 1000f;
        bool hitch = dtMs >= ClientPerfSettings.HitchMs;
        session.NoteFrame(dtMs, hitch);
        session.TickMemoryIfNeeded();

        if (hitch)
            session.EmitHitch(dtMs, Time.frameCount);
    }
}
