/// <summary>RL 奖励系数（终局为主，过程弱辅助）。</summary>
public static class RlReward
{
    public const float Win = 2.0f;
    public const float Dead = -1.0f;
    public const float LoseAlive = -0.5f;
    public const float Draw = -0.2f; // 无人存活等真正平局（非回合上限）


    public const float DollGain = 0.1f;
    public const float DollLoss = -0.1f;
    public const float Kill = 0.25f;

    public const float DamageDealtPerHp = 0.003f;
    public const float DamageDealtStepCap = 0.1f;
    public const float DamageTakenPerHp = 0.001f;

    public const float IllegalAction = -0.01f;
}
