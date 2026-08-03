using UnityEngine;

public class MapVisual : MonoBehaviour
{
    public static MapVisual Instance;

    private Transform[,] tileRoots;
    private SpriteRenderer[,] cellRenderers;
    private SpriteRenderer[,] cliffSouthRenderers;
    private SpriteRenderer[,] cliffWestRenderers;
    private Transform root;

    private static readonly Color MoveHint = new Color(0.25f, 0.55f, 0.95f, 0.55f);
    private static readonly Color AttackHint = new Color(0.95f, 0.25f, 0.2f, 0.55f);
    private static readonly Color BombHint = new Color(0.95f, 0.7f, 0.15f, 0.4f);
    private static readonly Color BlastHint = new Color(1f, 0.2f, 0.05f, 0.55f);
    private static readonly Color BlastCenter = new Color(1f, 0.45f, 0.1f, 0.7f);
    /// <summary>不透明迷雾：遮地形，但用雾蓝灰而非纯黑，避免像坏屏。</summary>
    private static readonly Color FogColor = new Color(0.16f, 0.2f, 0.22f, 1f);

    private SpriteRenderer[,] overlayRenderers;
    private SpriteRenderer[,] fogRenderers;
    private Vector2Int? bombHoverCell;
    private int bombThrowRange = 5;
    private int bombBlastRadius = 2;
    private Vector2Int bombThrowerCell;
    private Sprite cliffSprite;
    private Transform backdropRoot;
    private MeshRenderer tableRenderer;
    private Material tableMaterial;
    private SpriteRenderer feltRenderer;
    private SpriteRenderer rimRenderer;
    private float boardW;
    private float tableWorldSize;
    private float boardD;

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
        if (tableMaterial != null)
        {
            Destroy(tableMaterial);
            tableMaterial = null;
        }
        if (backdropRoot != null)
            Destroy(backdropRoot.gameObject);

        root = new GameObject("Tiles").transform;
        root.SetParent(transform, false);

        tileRoots = new Transform[grid.gridWidth, grid.gridHeight];
        cellRenderers = new SpriteRenderer[grid.gridWidth, grid.gridHeight];
        cliffSouthRenderers = new SpriteRenderer[grid.gridWidth, grid.gridHeight];
        cliffWestRenderers = new SpriteRenderer[grid.gridWidth, grid.gridHeight];
        overlayRenderers = new SpriteRenderer[grid.gridWidth, grid.gridHeight];
        fogRenderers = new SpriteRenderer[grid.gridWidth, grid.gridHeight];

        BuildBackdrop(grid);

        var fogSprite = CreateFogSprite(32);
        var overlaySprite = SpriteFactory.CreateColorSprite(Color.white);
        cliffSprite = TerrainSpriteFactory.CreateHighlandCliff();
        float half = grid.cellSize * 0.5f;
        // 顶面精灵默认在 XY，绕 X 放平到 XZ
        var flatRot = Quaternion.Euler(90f, 0f, 0f);

        for (int x = 0; x < grid.gridWidth; x++)
        {
            for (int y = 0; y < grid.gridHeight; y++)
            {
                var cell = new Vector2Int(x, y);
                var go = new GameObject($"Tile_{x}_{y}");
                go.transform.SetParent(root, false);
                go.transform.position = grid.CellToWorld(cell);
                tileRoots[x, y] = go.transform;

                var top = new GameObject("Top");
                top.transform.SetParent(go.transform, false);
                top.transform.localRotation = flatRot;
                top.transform.localScale = Vector3.one * 0.98f;
                var sr = top.AddComponent<SpriteRenderer>();
                cellRenderers[x, y] = sr;

                var cliffS = new GameObject("CliffSouth");
                cliffS.transform.SetParent(go.transform, false);
                // 南侧面朝向 -Z（相机大致从南偏西看过来）
                // 崖壁贴图 32×16、PPU=32 → 固有高 0.5，缩放到 HighlandElevation
                float cliffScaleY = GridManager.HighlandElevation / (16f / 32f);
                cliffS.transform.localPosition = new Vector3(0f, 0f, -half * 0.98f);
                cliffS.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                cliffS.transform.localScale = new Vector3(0.98f, cliffScaleY, 1f);
                var csr = cliffS.AddComponent<SpriteRenderer>();
                csr.sprite = cliffSprite;
                cliffSouthRenderers[x, y] = csr;

                var cliffW = new GameObject("CliffWest");
                cliffW.transform.SetParent(go.transform, false);
                cliffW.transform.localPosition = new Vector3(-half * 0.98f, 0f, 0f);
                cliffW.transform.localRotation = Quaternion.Euler(0f, -90f, 0f);
                cliffW.transform.localScale = new Vector3(0.98f, cliffScaleY, 1f);
                var cwr = cliffW.AddComponent<SpriteRenderer>();
                cwr.sprite = cliffSprite;
                cliffWestRenderers[x, y] = cwr;

                var fog = new GameObject("Fog");
                fog.transform.SetParent(go.transform, false);
                fog.transform.localRotation = flatRot;
                fog.transform.localPosition = new Vector3(0f, 0.01f, 0f);
                fog.transform.localScale = Vector3.one * 0.99f;
                var fsr = fog.AddComponent<SpriteRenderer>();
                fsr.sprite = fogSprite;
                fsr.color = Color.clear;
                fogRenderers[x, y] = fsr;

                var overlay = new GameObject("Overlay");
                overlay.transform.SetParent(go.transform, false);
                overlay.transform.localRotation = flatRot;
                overlay.transform.localPosition = new Vector3(0f, 0.02f, 0f);
                overlay.transform.localScale = Vector3.one * 0.99f;
                var osr = overlay.AddComponent<SpriteRenderer>();
                osr.sprite = overlaySprite;
                osr.color = Color.clear;
                overlayRenderers[x, y] = osr;
            }
        }

        RefreshAllTiles(grid);
    }

    /// <summary>桌游桌面 + 棋盘托盘。桌面用大平面 UV 平铺，铺满相机可视范围。</summary>
    private void BuildBackdrop(GridManager grid)
    {
        backdropRoot = new GameObject("TableBackdrop").transform;
        backdropRoot.SetParent(transform, false);

        boardW = grid.gridWidth * grid.cellSize;
        boardD = grid.gridHeight * grid.cellSize;
        // 覆盖最大缩放下的视野 + 平移边距，避免露出纯色清屏。
        float boardMax = Mathf.Max(boardW, boardD);
        tableWorldSize = Mathf.Max(96f, boardMax * 5.5f);
        var center = new Vector3(boardW * 0.5f, -0.14f, boardD * 0.5f);
        var flatRot = Quaternion.Euler(90f, 0f, 0f);

        tableRenderer = CreateTablePlane(backdropRoot, "Table", center + Vector3.down * 0.03f,
            tableWorldSize, TableSurface.Current);

        feltRenderer = CreateFlatSprite(backdropRoot, "BoardFelt", center, flatRot,
            new Vector3(boardW + 1.6f, boardD + 1.6f, 1f), TableSurface.CreateBoardFeltSprite(), -150);

        rimRenderer = CreateFlatSprite(backdropRoot, "BoardRim", center + Vector3.down * 0.01f, flatRot,
            new Vector3(boardW + 2.4f, boardD + 2.4f, 1f), TableSurface.CreateBoardRimSprite(), -160);
    }

    public void ApplyTableStyle(TableStyle style)
    {
        if (tableMaterial == null)
            return;
        TableSurface.ApplyTableMaterial(tableMaterial, style, tableWorldSize);
    }

    private MeshRenderer CreateTablePlane(Transform parent, string name, Vector3 pos, float worldSize, TableStyle style)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = pos;
        go.transform.rotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;

        var mesh = new Mesh { name = "TablePlane" };
        float h = worldSize * 0.5f;
        mesh.vertices = new[]
        {
            new Vector3(-h, 0f, -h),
            new Vector3(h, 0f, -h),
            new Vector3(-h, 0f, h),
            new Vector3(h, 0f, h)
        };
        mesh.uv = new[]
        {
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(0f, 1f),
            new Vector2(1f, 1f)
        };
        mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        tableMaterial = TableSurface.CreateTableMaterial(style, worldSize);
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = tableMaterial;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        return mr;
    }

    private static SpriteRenderer CreateFlatSprite(Transform parent, string name, Vector3 pos, Quaternion rot,
        Vector3 scale, Sprite sprite, int sorting)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = pos;
        go.transform.rotation = rot;
        go.transform.localScale = scale;
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.sortingOrder = sorting;
        return sr;
    }

    private static Sprite CreateFogSprite(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float n = Mathf.PerlinNoise(x * 0.14f, y * 0.14f);
                float v = 0.75f + n * 0.25f;
                tex.SetPixel(x, y, new Color(v, v, v, 1f));
            }
        }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }

    public void RefreshAllTiles(GridManager grid)
    {
        if (cellRenderers == null || tileRoots == null)
            return;

        for (int x = 0; x < grid.gridWidth; x++)
        {
            for (int y = 0; y < grid.gridHeight; y++)
            {
                var cell = new Vector2Int(x, y);
                var type = grid.GetTileType(cell);
                bool alt = ((x + y) % 2) != 0;
                bool highland = type == TileType.Highland;

                tileRoots[x, y].position = grid.CellToWorld(cell);
                cellRenderers[x, y].sprite = TerrainSpriteFactory.CreateTop(type, alt);
                cellRenderers[x, y].sortingOrder = grid.GetSortOrder(cell, 0);

                cliffSouthRenderers[x, y].gameObject.SetActive(highland);
                cliffWestRenderers[x, y].gameObject.SetActive(highland);
                if (highland)
                {
                    cliffSouthRenderers[x, y].sortingOrder = grid.GetSortOrder(cell, 1);
                    cliffWestRenderers[x, y].sortingOrder = grid.GetSortOrder(cell, 1);
                }

                fogRenderers[x, y].sortingOrder = grid.GetSortOrder(cell, 3);
                overlayRenderers[x, y].sortingOrder = grid.GetSortOrder(cell, 4);
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
