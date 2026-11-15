using UnityEngine;

/// <summary>格坐标 Int2 与 Unity 类型互转（只在表现层使用）。</summary>
public static class Int2Extensions
{
    public static Vector2Int ToV2(this Int2 c) => new Vector2Int(c.X, c.Y);

    public static Int2 ToInt2(this Vector2Int v) => new Int2(v.x, v.y);

    /// <summary>格子中心的世界坐标（棋盘所在平面 y = 0）。</summary>
    public static Vector3 ToWorld(this Int2 c, GridManager grid)
        => BoardProjection.CellToWorld(new Vector2Int(c.X, c.Y));
}
