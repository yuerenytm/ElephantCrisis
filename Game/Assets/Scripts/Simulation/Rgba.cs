/// <summary>
/// 中立的颜色值（0~1）。逻辑层只描述"什么颜色"，
/// 具体渲染成 UnityEngine.Color 是表现层的事——核心库因此不必认识 Unity。
/// </summary>
public readonly struct Rgba
{
    public readonly float R;
    public readonly float G;
    public readonly float B;
    public readonly float A;

    public Rgba(float r, float g, float b, float a = 1f)
    {
        R = r;
        G = g;
        B = b;
        A = a;
    }

    /// <summary>白色。</summary>
    public static Rgba White => new Rgba(1f, 1f, 1f, 1f);
}
