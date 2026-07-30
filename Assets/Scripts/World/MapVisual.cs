using UnityEngine;

public class MapVisual : MonoBehaviour
{
    public static MapVisual Instance;

    private SpriteRenderer[,] cellRenderers;
    private Transform root;

    private static readonly Color NormalColor = new Color(0.22f, 0.32f, 0.24f);
    private static readonly Color NormalAlt = new Color(0.18f, 0.28f, 0.22f);
    private static readonly Color LavaColor = new Color(0.85f, 0.28f, 0.1f);
    private static readonly Color MoveHint = new Color(0.25f, 0.55f, 0.95f, 0.55f);
    private static readonly Color AttackHint = new Color(0.95f, 0.25f, 0.2f, 0.55f);
    private static readonly Color BombHint = new Color(0.95f, 0.7f, 0.15f, 0.4f);
    private static readonly Color BlastHint = new Color(1f, 0.2f, 0.05f, 0.55f);
    private static readonly Color BlastCenter = new Color(1f, 0.45f, 0.1f, 0.7f);

    private SpriteRenderer[,] overlayRenderers;
    private Vector2Int? bombHoverCell;
    private int bombThrowRange = 5;
    private int bombBlastRadius = 2;
    private Vector2Int bombThrowerCell;

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public void Build(GridManager grid)
    {
        if (root != null)
            Destroy(root.gameObject);

        root = new GameObject("Tiles").transform;
        root.SetParent(transform, false);

        cellRenderers = new SpriteRenderer[grid.gridWidth, grid.gridHeight];
        overlayRenderers = new SpriteRenderer[grid.gridWidth, grid.gridHeight];

        var normalA = SpriteFactory.CreateBorderedSprite(NormalColor, new Color(0.12f, 0.16f, 0.12f), 32, 1);
        var normalB = SpriteFactory.CreateBorderedSprite(NormalAlt, new Color(0.12f, 0.16f, 0.12f), 32, 1);

        for (int x = 0; x < grid.gridWidth; x++)
        {
            for (int y = 0; y < grid.gridHeight; y++)
            {
                var cell = new Vector2Int(x, y);
                var go = new GameObject($"Tile_{x}_{y}");
                go.transform.SetParent(root, false);
                go.transform.position = grid.CellToWorld(cell);
                go.transform.localScale = Vector3.one * 0.98f;
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = ((x + y) % 2 == 0) ? normalA : normalB;
                sr.sortingOrder = 0;
                cellRenderers[x, y] = sr;

                var overlay = new GameObject("Overlay");
                overlay.transform.SetParent(go.transform, false);
                var osr = overlay.AddComponent<SpriteRenderer>();
                osr.sprite = SpriteFactory.CreateColorSprite(Color.white);
                osr.sortingOrder = 1;
                osr.color = Color.clear;
                overlayRenderers[x, y] = osr;
            }
        }
    }

    public void RefreshAllTiles(GridManager grid)
    {
        if (cellRenderers == null)
            return;

        var lavaSprite = SpriteFactory.CreateBorderedSprite(LavaColor, new Color(0.4f, 0.1f, 0.05f), 32, 1);
        var normalA = SpriteFactory.CreateBorderedSprite(NormalColor, new Color(0.12f, 0.16f, 0.12f), 32, 1);
        var normalB = SpriteFactory.CreateBorderedSprite(NormalAlt, new Color(0.12f, 0.16f, 0.12f), 32, 1);

        for (int x = 0; x < grid.gridWidth; x++)
        {
            for (int y = 0; y < grid.gridHeight; y++)
            {
                var type = grid.GetTileType(new Vector2Int(x, y));
                if (type == TileType.Lava)
                    cellRenderers[x, y].sprite = lavaSprite;
                else
                    cellRenderers[x, y].sprite = ((x + y) % 2 == 0) ? normalA : normalB;
            }
        }
    }

    public void ClearHints()
    {
        if (overlayRenderers == null)
            return;
        for (int x = 0; x < overlayRenderers.GetLength(0); x++)
            for (int y = 0; y < overlayRenderers.GetLength(1); y++)
                overlayRenderers[x, y].color = Color.clear;
    }

    public void ShowMoveHints(UnitActor unit)
    {
        ClearHints();
        if (unit == null || unit.IsDead)
            return;

        var grid = GridManager.Instance;
        int range = unit.CurrentMove;
        for (int x = 0; x < grid.gridWidth; x++)
        {
            for (int y = 0; y < grid.gridHeight; y++)
            {
                var cell = new Vector2Int(x, y);
                if (cell == unit.Cell)
                    continue;
                if (grid.GetManhattanDistance(unit.Cell, cell) > range)
                    continue;
                if (grid.IsCellOccupied(cell))
                    continue;
                overlayRenderers[x, y].color = MoveHint;
            }
        }
    }

    public void ShowAttackHints(UnitActor unit)
    {
        ClearHints();
        if (unit == null || unit.IsDead || unit.IsDying)
            return;

        var grid = GridManager.Instance;
        for (int x = 0; x < grid.gridWidth; x++)
        {
            for (int y = 0; y < grid.gridHeight; y++)
            {
                var cell = new Vector2Int(x, y);
                if (grid.GetManhattanDistance(unit.Cell, cell) != unit.AttackRange)
                    continue;
                var occ = grid.GetOccupant(cell);
                if (occ == null)
                    continue;
                var target = occ.GetComponent<UnitActor>();
                if (target == null || target.IsDead)
                    continue;
                overlayRenderers[x, y].color = AttackHint;
            }
        }
    }

    public void ShowBombHints(UnitActor unit, int throwRange, int blastRadius = 2)
    {
        bombThrowRange = throwRange;
        bombBlastRadius = blastRadius;
        bombHoverCell = null;
        if (unit != null)
            bombThrowerCell = unit.Cell;
        ClearHints();
        if (unit == null)
            return;

        var grid = GridManager.Instance;
        for (int x = 0; x < grid.gridWidth; x++)
        {
            for (int y = 0; y < grid.gridHeight; y++)
            {
                var cell = new Vector2Int(x, y);
                if (grid.GetManhattanDistance(unit.Cell, cell) <= throwRange)
                    overlayRenderers[x, y].color = BombHint;
            }
        }
    }

    /// <summary>炸弹瞄准时：悬浮格为中心，染色爆炸半径。</summary>
    public void UpdateBombHoverPreview(Vector2Int? hover)
    {
        if (overlayRenderers == null)
            return;
        if (bombHoverCell == hover)
            return;
        bombHoverCell = hover;

        var grid = GridManager.Instance;
        // 先恢复投掷范围底色
        for (int x = 0; x < grid.gridWidth; x++)
        {
            for (int y = 0; y < grid.gridHeight; y++)
            {
                var cell = new Vector2Int(x, y);
                if (grid.GetManhattanDistance(bombThrowerCell, cell) <= bombThrowRange)
                    overlayRenderers[x, y].color = BombHint;
                else
                    overlayRenderers[x, y].color = Color.clear;
            }
        }

        if (!hover.HasValue)
            return;
        if (grid.GetManhattanDistance(bombThrowerCell, hover.Value) > bombThrowRange)
            return;

        for (int x = 0; x < grid.gridWidth; x++)
        {
            for (int y = 0; y < grid.gridHeight; y++)
            {
                var cell = new Vector2Int(x, y);
                int d = grid.GetManhattanDistance(hover.Value, cell);
                if (d > bombBlastRadius)
                    continue;
                overlayRenderers[x, y].color = d == 0 ? BlastCenter : BlastHint;
            }
        }
    }

    public void ShowBananaHints(UnitActor unit)
    {
        ClearHints();
        if (unit == null) return;
        var grid = GridManager.Instance;
        int range = unit.AttackRange;
        for (int x = 0; x < grid.gridWidth; x++)
        {
            for (int y = 0; y < grid.gridHeight; y++)
            {
                var cell = new Vector2Int(x, y);
                int d = grid.GetManhattanDistance(unit.Cell, cell);
                if (d >= 1 && d <= range)
                    overlayRenderers[x, y].color = new Color(1f, 0.92f, 0.2f, 0.5f);
            }
        }
    }

    public void ShowFlameHints(UnitActor unit)
    {
        ClearHints();
        if (unit == null) return;
        var grid = GridManager.Instance;
        var dirs = new[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
        foreach (var dir in dirs)
        {
            for (int step = 1; step <= 5; step++)
            {
                var cell = unit.Cell + dir * step;
                if (!grid.IsValidCell(cell)) break;
                overlayRenderers[cell.x, cell.y].color = new Color(1f, 0.4f, 0.1f, 0.45f);
            }
            var adj = unit.Cell + dir;
            if (grid.IsValidCell(adj))
                overlayRenderers[adj.x, adj.y].color = new Color(1f, 0.15f, 0.05f, 0.7f);
        }
    }

    public void ShowDiscardHints(UnitActor unit)
    {
        ClearHints();
        if (unit == null)
            return;

        var grid = GridManager.Instance;
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                if (Mathf.Abs(dx) + Mathf.Abs(dy) > 1)
                    continue;
                var cell = unit.Cell + new Vector2Int(dx, dy);
                if (!grid.IsValidCell(cell))
                    continue;
                overlayRenderers[cell.x, cell.y].color = BombHint;
            }
        }
    }

    public void ShowShootHints(UnitActor unit, int range)
    {
        ClearHints();
        if (unit == null || unit.IsDead || unit.IsDying)
            return;

        var grid = GridManager.Instance;
        for (int x = 0; x < grid.gridWidth; x++)
        {
            for (int y = 0; y < grid.gridHeight; y++)
            {
                var cell = new Vector2Int(x, y);
                int dist = grid.GetManhattanDistance(unit.Cell, cell);
                if (dist < 1 || dist > range)
                    continue;
                var occ = grid.GetOccupant(cell);
                if (occ == null)
                    continue;
                var target = occ.GetComponent<UnitActor>();
                if (target == null || target.IsDead || target == unit)
                    continue;
                overlayRenderers[x, y].color = AttackHint;
            }
        }
    }

    public void ShowPickupHints(UnitActor unit)
    {
        ClearHints();
        if (unit == null)
            return;

        var grid = GridManager.Instance;
        var lootColor = new Color(0.95f, 0.85f, 0.25f, 0.55f);
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                if (Mathf.Abs(dx) + Mathf.Abs(dy) > 1)
                    continue;
                var cell = unit.Cell + new Vector2Int(dx, dy);
                if (!grid.IsValidCell(cell))
                    continue;
                if (!GroundItemManager.Instance.HasItems(cell))
                    continue;
                overlayRenderers[cell.x, cell.y].color = lootColor;
            }
        }
    }
}
