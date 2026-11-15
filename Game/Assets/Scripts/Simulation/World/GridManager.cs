#if UNITY_EDITOR
using UnityEngine;
#endif
using System.Collections.Generic;

// 挂载在场景中一个空物体上，负责管理18x18网格的坐标转换、占用和地块类型
public class GridManager
{
    public static GridManager Instance;

    public int gridWidth = GameRulesConfig.GridWidth;
    public int gridHeight = GameRulesConfig.GridHeight;
    public float cellSize = 1.25f;

    /// <summary>
    /// 高地相对地面的抬升高度（世界 Y）。
    /// </summary>
    public const float HighlandElevation = 0.45f;

    private Dictionary<Int2, UnitActor> occupiedCells = new Dictionary<Int2, UnitActor>();
    private TileType[,] tiles;
    private int lavaInset;

    /// <summary>创建并初始化网格（由装配点调用一次）。</summary>
    public static GridManager Create()
    {
        var grid = new GridManager();
        Instance = grid;
        grid.Initialize();
        return grid;
    }

    private void Initialize()
    {

        // 网格尺寸以 game_rules.yaml 为唯一真源（Inspector 默认值仅作回退）
        gridWidth = GameRulesConfig.GridWidth;
        gridHeight = GameRulesConfig.GridHeight;

        InitTiles();
    }

    /// <summary>
    /// 初始化地形数组并生成地形。
    /// </summary>
    public void InitTiles()
    {
        tiles = new TileType[gridWidth, gridHeight];
        lavaInset = 0;
        for (int x = 0; x < gridWidth; x++)
            for (int y = 0; y < gridHeight; y++)
                tiles[x, y] = TileType.Normal;
        TerrainGenerator.Generate(this);
    }

    /// <summary>
    /// 坐标是否在网格内。
    /// </summary>
    public bool IsValidCell(Int2 cell)
    {
        return cell.x >= 0 && cell.x < gridWidth && cell.y >= 0 && cell.y < gridHeight;
    }

    /// <summary>
    /// 取地形类型；越界静默返回 Normal。
    /// </summary>
    public TileType GetTileType(Int2 cell)
    {
        if (!IsValidCell(cell) || tiles == null)
            return TileType.Normal;
        return tiles[cell.x, cell.y];
    }

    /// <summary>
    /// 写地形类型。
    /// </summary>
    public void SetTileType(Int2 cell, TileType type)
    {
        if (!IsValidCell(cell) || tiles == null)
            return;
        tiles[cell.x, cell.y] = type;
    }

    public int LavaInset => lavaInset;

    // 最外层非熔岩一圈变为熔岩，返回本轮新变熔岩的格子
    /// <summary>
    /// 最外一圈变为熔岩，返回本轮新变的格子。
    /// </summary>
    public List<Int2> ShrinkLavaRing()
    {
        var changed = new List<Int2>();
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
                changed.Add(new Int2(x, y));
            }
        }

        lavaInset = nextInset;
        return changed;
    }

    /// <summary>
    /// 清空占用表（开局 / 清理时用）。
    /// </summary>
    public void ClearAllOccupants()
    {
        occupiedCells.Clear();
    }


    /// <summary>
    /// 该格是否被占用。
    /// </summary>
    public bool IsCellOccupied(Int2 cell)
    {
        return occupiedCells.ContainsKey(cell);
    }

    /// <summary>
    /// 登记占用者。
    /// </summary>
    public void SetCellOccupied(Int2 cell, UnitActor occupant)
    {
        occupiedCells[cell] = occupant;
    }

    /// <summary>
    /// 清除占用。
    /// </summary>
    public void ClearCell(Int2 cell)
    {
        if (occupiedCells.ContainsKey(cell))
            occupiedCells.Remove(cell);
    }

    /// <summary>
    /// 取占用者。
    /// </summary>
    public UnitActor GetOccupant(Int2 cell)
    {
        occupiedCells.TryGetValue(cell, out UnitActor occupant);
        return occupant;
    }

    /// <summary>
    /// 格子的世界高度（高地会抬高）。
    /// </summary>
    public float GetCellHeight(Int2 cell)
        => TerrainInfo.GetElevation(GetTileType(cell));

    /// <summary>
    /// 2.5D 绘制排序：偏北（更大 cell.y）先画，抬升略后画。
    /// </summary>
    public int GetSortOrder(Int2 cell, int layer)
    {
        int elev = GetCellHeight(cell) > 0.01f ? 2 : 0;
        return cell.y * 20 + cell.x + elev + layer;
    }

    /// <summary>
    /// 两格之间的曼哈顿距离。
    /// </summary>
    public int GetManhattanDistance(Int2 a, Int2 b)
    {
        return System.Math.Abs(a.x - b.x) + System.Math.Abs(a.y - b.y);
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        Gizmos.color = Color.gray;
        for (int x = 0; x <= gridWidth; x++)
        {
            Vector3 start = new Vector3(x * cellSize, 0f, 0f);
            Vector3 end = new Vector3(x * cellSize, 0f, gridHeight * cellSize);
            Gizmos.DrawLine(start, end);
        }
        for (int y = 0; y <= gridHeight; y++)
        {
            Vector3 start = new Vector3(0f, 0f, y * cellSize);
            Vector3 end = new Vector3(gridWidth * cellSize, 0f, y * cellSize);
            Gizmos.DrawLine(start, end);
        }
    }
#endif
}
