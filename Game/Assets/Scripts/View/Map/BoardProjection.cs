using UnityEngine;

/// <summary>
/// 格子 ↔ 世界 / 屏幕的换算。放在表现层：这些是"怎么画"的知识，
/// 逻辑层只关心格子坐标（Int2），不关心它落在屏幕哪里。
/// </summary>
public static class BoardProjection
{
    /// <summary>棋盘原点（目前恒为世界原点）。</summary>
    public static Vector3 Origin => Vector3.zero;

    /// <summary>格子中心：XZ 为地面，Y 为抬升（高地 +0.25）。</summary>
    public static Vector3 CellToWorld(Int2 cell)
    {
        var grid = GridManager.Instance;
        float size = grid.cellSize;
        return Origin + new Vector3(
            cell.x * size + size * 0.5f,
            grid.GetCellHeight(cell),
            cell.y * size + size * 0.5f);
    }
    /// <summary>斜视正交相机下的点选：射线与各格顶面求交，取最近命中。</summary>
    public static bool TryScreenToCell(Camera cam, Vector2 screenPos, out Int2 cell)
    {
        cell = default;
        var grid = GridManager.Instance;
        if (cam == null || grid == null)
            return false;

        Ray ray = cam.ScreenPointToRay(screenPos);
        if (System.Math.Abs(ray.direction.y) < 1e-5f)
            return false;

        float bestT = float.MaxValue;
        bool hit = false;

        for (int x = 0; x < grid.gridWidth; x++)
        {
            for (int y = 0; y < grid.gridHeight; y++)
            {
                var c = new Int2(x, y);
                float planeY = Origin.y + grid.GetCellHeight(c);
                float t = (planeY - ray.origin.y) / ray.direction.y;
                if (t < 0f || t >= bestT)
                    continue;

                Vector3 p = ray.origin + ray.direction * t;
                float lx = p.x - Origin.x;
                float lz = p.z - Origin.z;
                float minX = x * grid.cellSize;
                float minZ = y * grid.cellSize;
                if (lx < minX || lx >= minX + grid.cellSize || lz < minZ || lz >= minZ + grid.cellSize)
                    continue;

                bestT = t;
                cell = c;
                hit = true;
            }
        }

        return hit;
    }
}
