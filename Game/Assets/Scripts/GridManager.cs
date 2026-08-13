using UnityEngine;
using System.Collections.Generic;

// 挂载在场景中一个空物体上，负责管理18x18网格的坐标转换、占用和地块类型
public class GridManager : MonoBehaviour
{
    public static GridManager Instance;

    [Header("网格设置")]
    public int gridWidth = GameRulesConfig.GridWidth;
    public int gridHeight = GameRulesConfig.GridHeight;
    public float cellSize = 1.25f;
    public Vector3 originPosition = Vector3.zero;

    /// <summary>高地相对地面的抬升高度（世界 Y）。</summary>
    public const float HighlandElevation = 0.45f;

    private Dictionary<Vector2Int, GameObject> occupiedCells = new Dictionary<Vector2Int, GameObject>();
    private TileType[,] tiles;
    private int lavaInset;

    private void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
        {
            Destroy(gameObject);
            return;
        }

        // 网格尺寸以 game_rules.yaml 为唯一真源（Inspector 默认值仅作回退）
        gridWidth = GameRulesConfig.GridWidth;
        gridHeight = GameRulesConfig.GridHeight;

        InitTiles();
    }

    public void InitTiles()
    {
        tiles = new TileType[gridWidth, gridHeight];
        lavaInset = 0;
        for (int x = 0; x < gridWidth; x++)
            for (int y = 0; y < gridHeight; y++)
                tiles[x, y] = TileType.Normal;
        TerrainGenerator.Generate(this);
    }

    public bool IsValidCell(Vector2Int cell)
    {
        return cell.x >= 0 && cell.x < gridWidth && cell.y >= 0 && cell.y < gridHeight;
    }

    public TileType GetTileType(Vector2Int cell)
    {
        if (!IsValidCell(cell) || tiles == null)
            return TileType.Normal;
        return tiles[cell.x, cell.y];
    }

    public void SetTileType(Vector2Int cell, TileType type)
    {
        if (!IsValidCell(cell) || tiles == null)
            return;
        tiles[cell.x, cell.y] = type;
    }

    public int LavaInset => lavaInset;

    // 最外层非熔岩一圈变为熔岩，返回本轮新变熔岩的格子
    public List<Vector2Int> ShrinkLavaRing()
    {
        var changed = new List<Vector2Int>();
        if (tiles == null)
            InitTiles();

        int nextInset = lavaInset + 1;
        if (nextInset * 2 >= gridWidth || nextInset * 2 >= gridHeight)
            return changed;

        for (int x = 0; x < gridWidth; x++)
        {
            for (int y = 0; y < gridHeight; y++)
            {
                bool onRing = x == lavaInset || y == lavaInset
                    || x == gridWidth - 1 - lavaInset
                    || y == gridHeight - 1 - lavaInset;
                if (!onRing)
                    continue;
                if (tiles[x, y] == TileType.Lava)
                    continue;
                tiles[x, y] = TileType.Lava;
                changed.Add(new Vector2Int(x, y));
            }
        }

        lavaInset = nextInset;
        return changed;
    }

    public void ClearAllOccupants()
    {
        occupiedCells.Clear();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public bool IsCellOccupied(Vector2Int cell)
    {
        return occupiedCells.ContainsKey(cell);
    }

    public void SetCellOccupied(Vector2Int cell, GameObject occupant)
    {
        occupiedCells[cell] = occupant;
    }

    public void ClearCell(Vector2Int cell)
    {
        if (occupiedCells.ContainsKey(cell))
            occupiedCells.Remove(cell);
    }

    public GameObject GetOccupant(Vector2Int cell)
    {
        occupiedCells.TryGetValue(cell, out GameObject occupant);
        return occupant;
    }

    public float GetCellHeight(Vector2Int cell)
        => TerrainInfo.GetElevation(GetTileType(cell));

    /// <summary>格子中心：XZ 为地面，Y 为抬升（高地 +0.25）。</summary>
    public Vector3 CellToWorld(Vector2Int cell)
    {
        return originPosition + new Vector3(
            cell.x * cellSize + cellSize * 0.5f,
            GetCellHeight(cell),
            cell.y * cellSize + cellSize * 0.5f);
    }

    /// <summary>由世界坐标粗映射格子（用 XZ；忽略高度差时的点击请用 TryScreenToCell）。</summary>
    public Vector2Int WorldToCell(Vector3 worldPos)
    {
        Vector3 localPos = worldPos - originPosition;
        int x = Mathf.FloorToInt(localPos.x / cellSize);
        int y = Mathf.FloorToInt(localPos.z / cellSize);
        return new Vector2Int(x, y);
    }

    /// <summary>斜视正交相机下的点选：射线与各格顶面求交，取最近命中。</summary>
    public bool TryScreenToCell(Camera cam, Vector2 screenPos, out Vector2Int cell)
    {
        cell = default;
        if (cam == null)
            return false;

        Ray ray = cam.ScreenPointToRay(screenPos);
        if (Mathf.Abs(ray.direction.y) < 1e-5f)
            return false;

        float bestT = float.MaxValue;
        bool hit = false;

        for (int x = 0; x < gridWidth; x++)
        {
            for (int y = 0; y < gridHeight; y++)
            {
                var c = new Vector2Int(x, y);
                float planeY = originPosition.y + GetCellHeight(c);
                float t = (planeY - ray.origin.y) / ray.direction.y;
                if (t < 0f || t >= bestT)
                    continue;

                Vector3 p = ray.origin + ray.direction * t;
                float lx = p.x - originPosition.x;
                float lz = p.z - originPosition.z;
                float minX = x * cellSize;
                float minZ = y * cellSize;
                if (lx < minX || lx >= minX + cellSize || lz < minZ || lz >= minZ + cellSize)
                    continue;

                bestT = t;
                cell = c;
                hit = true;
            }
        }

        return hit;
    }

    /// <summary>2.5D 绘制排序：偏北（更大 cell.y）先画，抬升略后画。</summary>
    public int GetSortOrder(Vector2Int cell, int layer)
    {
        int elev = GetCellHeight(cell) > 0.01f ? 2 : 0;
        return cell.y * 20 + cell.x + elev + layer;
    }

    public int GetManhattanDistance(Vector2Int a, Vector2Int b)
    {
        return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.gray;
        for (int x = 0; x <= gridWidth; x++)
        {
            Vector3 start = originPosition + new Vector3(x * cellSize, 0f, 0f);
            Vector3 end = originPosition + new Vector3(x * cellSize, 0f, gridHeight * cellSize);
            Gizmos.DrawLine(start, end);
        }
        for (int y = 0; y <= gridHeight; y++)
        {
            Vector3 start = originPosition + new Vector3(0f, 0f, y * cellSize);
            Vector3 end = originPosition + new Vector3(gridWidth * cellSize, 0f, y * cellSize);
            Gizmos.DrawLine(start, end);
        }
    }
}
