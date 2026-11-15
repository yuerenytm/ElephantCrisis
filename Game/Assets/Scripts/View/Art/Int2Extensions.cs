using UnityEngine;

/// <summary>格坐标 Int2 与 Unity 类型互转（只在表现层使用）。</summary>
public static class Int2Extensions
{
    public static Vector2Int ToV2(this Int2 c) => new Vector2Int(c.X, c.Y);
}
