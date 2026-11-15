/// <summary>
/// 确定性随机数：不依赖 UnityEngine，同种子必得同序列。
///
/// 为什么自己写：Unity 的 Random 是全局一条流，任何一处新调用都会平移
/// 整局结果——之前"火焰动画偷吃随机数"就是这么来的。逻辑用自己的流之后，
/// 表现层怎么摇都不会影响结算。
///
/// 算法：xorshift64*（快、周期足够、实现只有十行），用 splitmix64 播种，
/// 避免小整数种子产生弱序列。
/// </summary>
public static class Rng
{
    private static ulong state = 0x2545F4914F6CDD1DUL;

    /// <summary>用种子重置序列。同一种子必然产出同一串数。</summary>
    public static void Reset(int seed)
    {
        ulong z = (ulong)(uint)seed + 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        state = z ^ (z >> 31);
        if (state == 0)
            state = 0x9E3779B97F4A7C15UL;
    }

    /// <summary>下一个 32 位无符号随机数。</summary>
    public static uint NextUInt()
    {
        state ^= state >> 12;
        state ^= state << 25;
        state ^= state >> 27;
        return (uint)((state * 0x2545F4914F6CDD1DUL) >> 32);
    }

    /// <summary>[0,1) 的浮点随机数，语义同 Random.value。</summary>
    public static float Value => (NextUInt() >> 8) * (1f / 16777216f);

    /// <summary>整数区间，下界含、上界不含，语义同 Random.Range(int,int)。</summary>
    public static int Range(int minInclusive, int maxExclusive)
    {
        if (maxExclusive <= minInclusive)
            return minInclusive;
        return minInclusive + (int)(NextUInt() % (uint)(maxExclusive - minInclusive));
    }
}
