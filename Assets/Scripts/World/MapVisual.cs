using UnityEngine;

public class MapVisual : MonoBehaviour
{
    public static MapVisual Instance;

    private SpriteRenderer[,] cellRenderers;
    private Transform root;

    private static readonly Color MoveHint = new Color(0.25f, 0.55f, 0.95f, 0.55f);
    private static readonly Color AttackHint = new Color(0.95f, 0.25f, 0.2f, 0.55f);
    private static readonly Color BombHint = new Color(0.95f, 0.7f, 0.15f, 0.4f);
    private static readonly Color BlastHint = new Color(1f, 0.2f, 0.05f, 0.55f);
    private static readonly Color BlastCenter = new Color(1f, 0.45f, 0.1f, 0.7f);
    /// <summary>不透明迷雾：视野外完全遮住地形色。</summary>
    private static readonly Color FogColor = new Color(0.04f, 0.05f, 0.07f, 1f);

    private SpriteRenderer[,] overlayRenderers;
    private SpriteRenderer[,] fogRenderers;
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
        fogRenderers = new SpriteRenderer[grid.gridWidth, grid.gridHeight];

        var fogSprite = SpriteFactory.CreateColorSprite(Color.white);

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
                sr.sortingOrder = 0;
                cellRenderers[x, y] = sr;

                var fog = new GameObject("Fog");
                fog.transform.SetParent(go.transform, false);
                var fsr = fog.AddComponent<SpriteRenderer>();
                fsr.sprite = fogSprite;
                fsr.sortingOrder = 1;
                fsr.color = Color.clear;
                fogRenderers[x, y] = fsr;

                var overlay = new GameObject("Overlay");
                overlay.transform.SetParent(go.transform, false);
                var osr = overlay.AddComponent<SpriteRenderer>();
                osr.sprite = SpriteFactory.CreateColorSprite(Color.white);
                osr.sortingOrder = 2;
                osr.color = Color.clear;
                overlayRenderers[x, y] = osr;
            }
        }

        RefreshAllTiles(grid);
    }

    public void RefreshAllTiles(GridManager grid)
    {
        if (cellRenderers == null)
            return;

        for (int x = 0; x < grid.gridWidth; x++)
        {
            for (int y = 0; y < grid.gridHeight; y++)
            {
                var type = grid.GetTileType(new Vector2Int(x, y));
                bool alt = ((x + y) % 2) != 0;
                var fill = TerrainInfo.GetFillColor(type, alt);
                var border = TerrainInfo.GetBorderColor(type);
                cellRenderers[x, y].sprite = SpriteFactory.CreateBorderedSprite(fill, border, 32, 1);
            }
        }

        ApplyFog(VisibilityService.GetFogViewer());
    }

    /// <summary>迷雾：视野外地块遮罩。</summary>
    public void ApplyFog(UnitActor viewer)
    {
        if (fogRenderers == null)
            return;
        var grid = GridManager.Instance;
        if (grid == null)
            return;

        for (int x = 0; x < grid.gridWidth; x++)
        {
            for (int y = 0; y < grid.gridHeight; y++)
            {
                var cell = new Vector2Int(x, y);
                bool seen = viewer == null || VisibilityService.CanSeeCell(viewer, cell);
                fogRenderers[x, y].color = seen ? Color.clear : FogColor;
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
                if (!VisibilityService.CanMoveTo(unit, cell))
                    continue;
                // 对不可见（隐匿）占格者不显示阻挡，避免占格泄露
                if (StealthService.BlocksMovementFor(unit, cell))
                    continue;
                overlayRenderers[x, y].color = MoveHint;
            }
        }
    }

    public void ShowAttackHints(UnitActor unit)
    {
        ClearHints();
        if (unit == null || unit.IsDead)
            return;

        var grid = GridManager.Instance;
        for (int x = 0; x < grid.gridWidth; x++)
        {
            for (int y = 0; y < grid.gridHeight; y++)
            {
                var cell = new Vector2Int(x, y);
                int dist = grid.GetManhattanDistance(unit.Cell, cell);
                if (dist < 1 || dist > unit.AttackRange)
                    continue;
                var occ = grid.GetOccupant(cell);
                if (occ == null)
                    continue;
                var target = occ.GetComponent<UnitActor>();
                if (target == null || target.IsDead)
                    continue;
                if (!VisibilityService.CanSeeCell(unit, cell))
                    continue;
                if (!StealthService.CanTargetDespiteHidden(unit, target))
                    continue;
                overlayRenderers[x, y].color = AttackHint;
            }
        }
    }

    public void ShowMotorcycleRamHints(UnitActor unit)
    {
        ClearHints();
        if (unit == null || unit.IsDead)
            return;
        var grid = GridManager.Instance;
        var dirs = new[]
        {
            new Vector2Int(1, 0), new Vector2Int(-1, 0),
            new Vector2Int(0, 1), new Vector2Int(0, -1)
        };
        foreach (var d in dirs)
        {
            for (int dist = 5; dist <= 10; dist++)
            {
                var cell = unit.Cell + d * dist;
                if (!grid.IsValidCell(cell))
                    break;
                if (StealthService.BlocksMovementFor(unit, cell))
                    continue;
                overlayRenderers[cell.x, cell.y].color = AttackHint;
            }
        }
    }

    public void ShowHookHints(UnitActor unit)
    {
        ClearHints();
        if (unit == null || unit.IsDead)
            return;
        var grid = GridManager.Instance;
        for (int x = 0; x < grid.gridWidth; x++)
        {
            for (int y = 0; y < grid.gridHeight; y++)
            {
                var cell = new Vector2Int(x, y);
                if (grid.GetManhattanDistance(unit.Cell, cell) > 3 || cell == unit.Cell)
                    continue;
                var target = StealthService.GetOccupantUnit(cell);
                if (target == null)
                    continue;
                if (!VisibilityService.CanSeeUnit(unit, target))
                    continue;
                if (!StealthService.CanTargetDespiteHidden(unit, target))
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
        if (unit == null || unit.IsDead)
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
