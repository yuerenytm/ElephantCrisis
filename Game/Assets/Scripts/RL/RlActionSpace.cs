/// <summary>RL 离散动作空间常量（与 ML-Agents Discrete branches 对齐）。</summary>
public static class RlActionSpace
{
    public const string BehaviorName = "ElephantCrisis";

    public const int OpCount = 12;
    public const int AxisCount = 7; // -3..+3
    public const int TargetCount = 4; // 0=none, 1..3 enemies

    public const int AxisRadius = 3;

    public enum Op
    {
        EndTurn = 0,
        Move = 1,
        Melee = 2,
        Shoot = 3,
        Bomb = 4,
        Pickup = 5,
        PotionSmall = 6,
        PotionLarge = 7,
        Equip = 8,
        Skill = 9,
        Leader = 10,
        Upgrade = 11
    }

    public static int AxisToOffset(int axisIndex)
    {
        return axisIndex - AxisRadius;
    }

    public static int OffsetToAxis(int offset)
    {
        return offset + AxisRadius;
    }
}
