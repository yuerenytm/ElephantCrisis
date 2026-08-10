using System.Collections.Generic;
using UnityEngine;

/// <summary>单局过程奖励探测：人偶/伤害/击杀，以及领袖宣言计数。</summary>
public class RlEpisodeProbe
{
    private readonly Dictionary<RoleType, int> lastHp = new Dictionary<RoleType, int>();
    private readonly Dictionary<RoleType, int> lastDolls = new Dictionary<RoleType, int>();
    private readonly Dictionary<RoleType, bool> lastDead = new Dictionary<RoleType, bool>();
    private readonly Dictionary<RoleType, bool> lastLeader = new Dictionary<RoleType, bool>();

    public int LeaderDeclarationsThisMatch { get; private set; }

    public void Begin(List<UnitActor> units)
    {
        lastHp.Clear();
        lastDolls.Clear();
        lastDead.Clear();
        lastLeader.Clear();
        LeaderDeclarationsThisMatch = 0;
        Snapshot(units);
    }

    public void Snapshot(List<UnitActor> units)
    {
        if (units == null)
            return;
        foreach (var u in units)
        {
            if (u == null)
                continue;
            lastHp[u.Role] = u.Hp;
            lastDolls[u.Role] = u.Inventory != null ? u.Inventory.CountDolls() : 0;
            lastDead[u.Role] = u.IsDead;
            lastLeader[u.Role] = u.HasUsedLeaderDeclaration;
        }
    }

    /// <summary>
    /// 任意行动后调用：给 RL agent 发过程奖，并更新领袖埋点。
    /// </summary>
    public void AfterWorldStep(List<UnitActor> units, RlMatchController match)
    {
        if (units == null || match == null)
            return;

        foreach (var u in units)
        {
            if (u == null)
                continue;

            bool leaderNow = u.HasUsedLeaderDeclaration;
            if (leaderNow && lastLeader.TryGetValue(u.Role, out bool was) && !was)
                LeaderDeclarationsThisMatch++;
            lastLeader[u.Role] = leaderNow;

            int dolls = u.Inventory != null ? u.Inventory.CountDolls() : 0;
            int hp = u.Hp;
            bool dead = u.IsDead;

            int prevDolls = lastDolls.TryGetValue(u.Role, out var pd) ? pd : dolls;
            int prevHp = lastHp.TryGetValue(u.Role, out var ph) ? ph : hp;
            bool prevDead = lastDead.TryGetValue(u.Role, out var pdead) && pdead;

            float shaping = 0f;
            int dollDelta = dolls - prevDolls;
            if (dollDelta > 0)
                shaping += dollDelta * RlReward.DollGain;
            else if (dollDelta < 0)
                shaping += (-dollDelta) * RlReward.DollLoss;

            int hpLost = prevHp - hp;
            if (hpLost > 0 && !prevDead)
                shaping += -RlReward.DamageTakenPerHp * hpLost;

            // 击杀：他人从存活→死亡时，若当前行动者是 RL，在 Executor 里已发；这里只补全非行动者探测到的击杀归属困难，跳过

            if (Mathf.Abs(shaping) > 1e-6f && match.IsRlControlled(u) && match.TryGetAgent(u.Role, out var agent))
                agent.AddShapingReward(shaping);

            lastDolls[u.Role] = dolls;
            lastHp[u.Role] = hp;
            lastDead[u.Role] = dead;
        }
    }

    /// <summary>行动者造成的伤害 / 击杀 /（可选）由 executor 传入。</summary>
    public static float RewardForAttacker(int damageDealt, bool killed)
    {
        float r = 0f;
        if (damageDealt > 0)
            r += Mathf.Min(RlReward.DamageDealtStepCap, damageDealt * RlReward.DamageDealtPerHp);
        if (killed)
            r += RlReward.Kill;
        return r;
    }
}
