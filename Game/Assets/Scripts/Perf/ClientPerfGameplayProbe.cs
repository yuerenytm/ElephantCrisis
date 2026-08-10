using System;
using UnityEngine;

/// <summary>
/// 订阅战报/回合，把玩法行动写成 game_action；并驱动秒级采样 flush。
/// </summary>
public class ClientPerfGameplayProbe : MonoBehaviour
{
    private bool subscribed;
    private float nextSecondAt;
    private int secFrames;
    private float secSumDt;
    private float secMaxDt;
    private int secHitches;
    private int secLoads;
    private int secActions;
    private float lastMonoMb = -1f;

    private void OnEnable()
    {
        TrySubscribe();
        nextSecondAt = Time.realtimeSinceStartup + 1f;
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void Update()
    {
        TrySubscribe();
        var session = ClientPerfSession.Instance;
        if (session == null || !session.IsActive)
            return;

        if (Time.realtimeSinceStartup < nextSecondAt)
            return;

        float mono = GC.GetTotalMemory(false) / (1024f * 1024f);
        float monoDelta = lastMonoMb < 0 ? 0f : mono - lastMonoMb;
        lastMonoMb = mono;

        session.EmitSecondSample(
            secFrames,
            secSumDt,
            secMaxDt,
            secHitches,
            secLoads,
            secActions,
            mono,
            monoDelta);

        secFrames = 0;
        secSumDt = 0f;
        secMaxDt = 0f;
        secHitches = 0;
        secLoads = 0;
        secActions = 0;
        nextSecondAt = Time.realtimeSinceStartup + 1f;
    }

    public void NoteFrame(float dtMs, bool hitch)
    {
        secFrames++;
        secSumDt += dtMs;
        if (dtMs > secMaxDt)
            secMaxDt = dtMs;
        if (hitch)
            secHitches++;
    }

    public void NoteLoad() => secLoads++;

    public void NoteAction() => secActions++;

    private void TrySubscribe()
    {
        if (subscribed)
            return;
        if (TurnManager.Instance == null)
            return;
        TurnManager.Instance.OnLog += OnBattleLog;
        subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!subscribed || TurnManager.Instance == null)
            return;
        TurnManager.Instance.OnLog -= OnBattleLog;
        subscribed = false;
    }

    private void OnBattleLog(string msg)
    {
        if (string.IsNullOrEmpty(msg))
            return;
        var session = ClientPerfSession.Instance;
        if (session == null || !session.IsActive)
            return;

        string kind = Classify(msg);
        if (kind == null)
            return;
        session.EmitGameAction(kind, Truncate(msg, 120));
    }

    private static string Classify(string msg)
    {
        // 回合/环境类以战报为准；战斗/AOE/技能由 ClientPerfMark 结构化打点，避免双计
        if (msg.StartsWith("——", StringComparison.Ordinal))
            return "turn";
        if (msg.Contains("熔岩") || msg.Contains("天气") || msg.Contains("燃烧") || msg.Contains("中毒")
            || msg.Contains("晕眩") || msg.Contains("站在"))
            return "world_hazard";
        if (msg.Contains("近战") || msg.Contains("射击") || msg.Contains("撞击") || msg.Contains("投掷")
            || msg.Contains("火焰喷射") || msg.Contains("闪光") || msg.Contains("地雷")
            || msg.Contains("定时炸弹") || msg.Contains("香蕉") || msg.Contains("抢夺")
            || msg.Contains("隐匿") || msg.Contains("强化剂") || msg.Contains("技能"))
            return null;
        if (msg.Contains("拾取") || msg.Contains("丢弃") || msg.Contains("使用") || msg.Contains("装备"))
            return "item";
        if (msg.Contains("移动") || msg.Contains("走到"))
            return "move";
        return "log_other";
    }

    private static string Truncate(string s, int max)
    {
        if (string.IsNullOrEmpty(s) || s.Length <= max)
            return s ?? "";
        return s.Substring(0, max);
    }
}
