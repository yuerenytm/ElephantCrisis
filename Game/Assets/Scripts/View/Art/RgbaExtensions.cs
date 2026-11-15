using UnityEngine;

/// <summary>把中立的 Rgba 转成 Unity 的 Color（只在表现层使用）。</summary>
public static class RgbaExtensions
{
    public static Color ToColor(this Rgba c) => new Color(c.R, c.G, c.B, c.A);
}
