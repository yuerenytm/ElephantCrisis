using UnityEngine;
using System.Collections.Generic;

// 挂载在场景中一个空物体上，负责管理18x18网格的坐标转换、占用和地块类型
public class GridManager : MonoBehaviour
{
    public static GridManager Instance;

    [Header("网格设置")]
    public int gridWidth = 18;
    public int gridHeight = 18;
    public float cellSize = 1f;
    public Vector3 originPosition = Vector3.zero;

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

        InitTiles();
    }

    public void InitTiles()
    {
        tiles = new TileType[gridWidth, gridHeight];
        lavaInset = 0;
        for (int x = 0; x < gridWidth; x++)
            for (int y = 0; y < gridHeight; y++)
                tiles[x, y] = TileType.Normal;
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

    public Vector3 CellToWorld(Vector2Int cell)
    {
        return originPosition + new Vector3(cell.x * cellSize + cellSize * 0.5f, cell.y * cellSize + cellSize * 0.5f, 0f);
    }

    public Vector2Int WorldToCell(Vector3 worldPos)
    {
        Vector3 localPos = worldPos - originPosition;
        int x = Mathf.FloorToInt(localPos.x / cellSize);
        int y = Mathf.FloorToInt(localPos.y / cellSize);
        return new Vector2Int(x, y);
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
            Vector3 start = originPosition + new Vector3(x * cellSize, 0, 0);
            Vector3 end = originPosition + new Vector3(x * cellSize, gridHeight * cellSize, 0);
            Gizmos.DrawLine(start, end);
        }
        for (int y = 0; y <= gridHeight; y++)
        {
            Vector3 start = originPosition + new Vector3(0, y * cellSize, 0);
            Vector3 end = originPosition + new Vector3(gridWidth * cellSize, y * cellSize, 0);
            Gizmos.DrawLine(start, end);
        }
    }
}
