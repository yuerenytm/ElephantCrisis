using UnityEngine;

public class MapVisual : MonoBehaviour
{
    public static MapVisual Instance;

    private Transform[,] tileRoots;
    private SpriteRenderer[,] cellRenderers;
    /// <summary>高地实心台体（Mesh），比精灵崖壁稳定，连片即等高台地。</summary>
    private Transform[,] highlandSlabs;
    /// <summary>丛林格上的灌木/蕨叶装饰根节点。</summary>
    private Transform[,] jungleDecorRoots;
    private Sprite[] junglePropSprites;
    private Transform root;

    private static readonly Color MoveHint = new Color(0.25f, 0.55f, 0.95f, 0.55f);
    private static readonly Color AttackHint = new Color(0.95f, 0.25f, 0.2f, 0.55f);
    private static readonly Color BombHint = new Color(0.95f, 0.7f, 0.15f, 0.4f);
    private static readonly Color BlastHint = new Color(1f, 0.2f, 0.05f, 0.55f);
    private static readonly Color BlastCenter = new Color(1f, 0.45f, 0.1f, 0.7f);
    /// <summary>视野内缘软过渡格数（曼哈顿），越大边缘越柔。</summary>
    private const float FogSoftCells = 2.25f;

    /// <summary>战争迷雾视觉：雾天=浓白雾；晴天=空气透视；雨天=冷灰蓝。</summary>
    private struct VisionVeilStyle
    {
        public Color Tint;
        public float Density;
        /// <summary>棋盘内视野外不透明下限（须挡住地形信息）。</summary>
        public float BoardMinOpaque;
        /// <summary>棋盘外树林不透明下限（晴天可更低，露出远树剪影）。</summary>
        public float ForestMinOpaque;
        public float TextureStrength;
        public float SoftCells;
        /// <summary>上层雾浓度倍率（晴天压低，避免「云墙」）。</summary>
        public float UpperLayerMul;
        public float WispAlpha;
        public bool ShowWisps;
    }
    /// <summary>贴图相对格子超采样，双线性后更连续、纹理更细。</summary>
    private const int FogTexelsPerCell = 4;
    /// <summary>立体雾：多层抬升 + 漂浮雾团。</summary>
    private static readonly float[] FogLayerHeights = { 0.05f, 0.42f, 0.9f, 1.45f };
    private static readonly float[] FogLayerAlphaMul = { 1f, 0.7f, 0.45f, 0.28f };
    private static readonly float[] FogLayerNoiseSeed = { 0f, 17.3f, 41.7f, 73.1f };
    private const int FogWispCount = 72;
    private static readonly Color HighlandRock = new Color(0.4f, 0.32f, 0.24f);

    private SpriteRenderer[,] overlayRenderers;
    private Transform fogVolumeRoot;
    private SpriteRenderer[] fogLayerRenderers;
    private Texture2D[] fogLayerTextures;
    private Sprite[] fogLayerSprites;
    private Transform[] fogWispRoots;
    private SpriteRenderer[] fogWispRenderers;
    private Sprite fogWispSprite;
    private Vector2Int? bombHoverCell;
    private int bombThrowRange = 5;
    private int bombBlastRadius = 2;
    private Vector2Int bombThrowerCell;
    private Material highlandMaterial;
    private Transform decorRoot;
    private float boardW;
    private float boardD;
    /// <summary>近棋盘装饰林（地砖+密树）；更外圈稀疏树 + 超大地坪盖住真空。</summary>
    private const int ForestMargin = 10;
    private const int ForestOuterMargin = 28;
    /// <summary>迷雾贴图向棋盘外延伸的格数，用于盖住装饰树林。</summary>
    private const int FogForestPad = ForestOuterMargin;
    private const int FogBaseSortingOrder = 12000;
    private const float ForestGroundPlaneSize = 160f;
    private int fogPadCells;

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
        if (highlandMaterial != null)
        {
            Destroy(highlandMaterial);
            highlandMaterial = null;
        }
        DestroyFogResources();
    }

    private void DestroyFogResources()
    {
        if (fogLayerSprites != null)
        {
            for (int i = 0; i < fogLayerSprites.Length; i++)
            {
                if (fogLayerSprites[i] != null)
                    Destroy(fogLayerSprites[i]);
            }
        }
        if (fogLayerTextures != null)
        {
            for (int i = 0; i < fogLayerTextures.Length; i++)
            {
                if (fogLayerTextures[i] != null)
                    Destroy(fogLayerTextures[i]);
            }
        }
        if (fogWispSprite != null)
        {
            Destroy(fogWispSprite);
            fogWispSprite = null;
        }
        fogVolumeRoot = null;
        fogLayerRenderers = null;
        fogLayerTextures = null;
        fogLayerSprites = null;
        fogWispRoots = null;
        fogWispRenderers = null;
    }

    public void Build(GridManager grid)
    {
        if (root != null)
            Destroy(root.gameObject);
        if (decorRoot != null)
            Destroy(decorRoot.gameObject);
        DestroyFogResources();

        root = new GameObject("Tiles").transform;
        root.SetParent(transform, false);

        tileRoots = new Transform[grid.gridWidth, grid.gridHeight];
        cellRenderers = new SpriteRenderer[grid.gridWidth, grid.gridHeight];
        highlandSlabs = new Transform[grid.gridWidth, grid.gridHeight];
        jungleDecorRoots = new Transform[grid.gridWidth, grid.gridHeight];
        overlayRenderers = new SpriteRenderer[grid.gridWidth, grid.gridHeight];
        junglePropSprites = new[]
        {
            TerrainSpriteFactory.CreateJungleProp(0),
            TerrainSpriteFactory.CreateJungleProp(1),
            TerrainSpriteFactory.CreateJungleProp(2)
        };

        BuildForestBorder(grid);
        EnsureHighlandMaterial();
        BuildContinuousFogPlane(grid);

        var overlaySprite = SpriteFactory.CreateColorSprite(Color.white);
        float elev = GridManager.HighlandElevation;
        float cell = grid.cellSize;
        // 顶面精灵默认在 XY，绕 X 放平到 XZ
        var flatRot = Quaternion.Euler(90f, 0f, 0f);

        for (int x = 0; x < grid.gridWidth; x++)
        {
            for (int y = 0; y < grid.gridHeight; y++)
            {
                var cellPos = new Vector2Int(x, y);
                var go = new GameObject($"Tile_{x}_{y}");
                go.transform.SetParent(root, false);
                go.transform.position = grid.CellToWorld(cellPos);
                tileRoots[x, y] = go.transform;

                // 实心台体：根节点在台面高度时，台体向下落到地面
                var slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
                slab.name = "HighlandSlab";
                slab.transform.SetParent(go.transform, false);
                slab.transform.localPosition = new Vector3(0f, -elev * 0.5f, 0f);
                slab.transform.localScale = new Vector3(cell * 1.002f, elev, cell * 1.002f);
                var col = slab.GetComponent<Collider>();
                if (col != null)
                    Destroy(col);
                var mr = slab.GetComponent<MeshRenderer>();
                mr.sharedMaterial = highlandMaterial;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                slab.SetActive(false);
                highlandSlabs[x, y] = slab.transform;

                var top = new GameObject("Top");
                top.transform.SetParent(go.transform, false);
                top.transform.localRotation = flatRot;
                top.transform.localPosition = new Vector3(0f, 0.01f, 0f);
                top.transform.localScale = Vector3.one * cell;
                var sr = top.AddComponent<SpriteRenderer>();
                cellRenderers[x, y] = sr;

                var overlay = new GameObject("Overlay");
                overlay.transform.SetParent(go.transform, false);
                overlay.transform.localRotation = flatRot;
                overlay.transform.localPosition = new Vector3(0f, 0.03f, 0f);
                overlay.transform.localScale = Vector3.one * (cell * 1.01f);
                var osr = overlay.AddComponent<SpriteRenderer>();
                osr.sprite = overlaySprite;
                osr.color = Color.clear;
                overlayRenderers[x, y] = osr;
            }
        }

        RefreshAllTiles(grid);
    }

    private void BuildContinuousFogPlane(GridManager grid)
    {
        DestroyFogResources();

        int layers = FogLayerHeights.Length;
        fogLayerRenderers = new SpriteRenderer[layers];
        fogLayerTextures = new Texture2D[layers];
        fogLayerSprites = new Sprite[layers];

        fogPadCells = FogForestPad;
        int fogW = grid.gridWidth + fogPadCells * 2;
        int fogH = grid.gridHeight + fogPadCells * 2;
        int tw = fogW * FogTexelsPerCell;
        int th = fogH * FogTexelsPerCell;
        float boardWLocal = grid.gridWidth * grid.cellSize;
        float boardDLocal = grid.gridHeight * grid.cellSize;
        var boardCenter = grid.originPosition + new Vector3(boardWLocal * 0.5f, 0f, boardDLocal * 0.5f);
        var flatRot = Quaternion.Euler(90f, 0f, 0f);

        fogVolumeRoot = new GameObject("FogVolume").transform;
        fogVolumeRoot.SetParent(root, false);
        fogVolumeRoot.position = boardCenter;

        for (int i = 0; i < layers; i++)
        {
            var tex = new Texture2D(tw, th, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                name = $"FogOfWar_L{i}"
            };
            var clear = new Color(1f, 1f, 1f, 0f);
            for (int y = 0; y < th; y++)
                for (int x = 0; x < tw; x++)
                    tex.SetPixel(x, y, clear);
            tex.Apply(false, false);
            fogLayerTextures[i] = tex;

            var spr = Sprite.Create(tex, new Rect(0, 0, tw, th), new Vector2(0.5f, 0.5f), FogTexelsPerCell);
            fogLayerSprites[i] = spr;

            var layerGo = new GameObject($"FogLayer_{i}");
            layerGo.transform.SetParent(fogVolumeRoot, false);
            // 上层略放大 + 抬高，斜视时盖住远树树冠
            float s = 1f + i * 0.04f;
            float layerY = FogLayerHeights[i] + i * 0.35f;
            layerGo.transform.localPosition = new Vector3(0f, layerY, 0f);
            layerGo.transform.localRotation = flatRot;
            layerGo.transform.localScale = new Vector3(grid.cellSize * s, grid.cellSize * s, 1f);

            var sr = layerGo.AddComponent<SpriteRenderer>();
            sr.sprite = spr;
            sr.color = Color.white;
            // 必须高于装饰林 sortingOrder（约可达上千），否则远树会穿出雾面
            sr.sortingOrder = FogBaseSortingOrder + i;
            sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            sr.receiveShadows = false;
            fogLayerRenderers[i] = sr;
        }

        BuildFogWisps();
    }

    private void BuildFogWisps()
    {
        fogWispSprite = CreateSoftWispSprite(64);
        fogWispRoots = new Transform[FogWispCount];
        fogWispRenderers = new SpriteRenderer[FogWispCount];

        var wispsRoot = new GameObject("FogWisps").transform;
        wispsRoot.SetParent(fogVolumeRoot, false);

        for (int i = 0; i < FogWispCount; i++)
        {
            var go = new GameObject($"Wisp_{i}");
            go.transform.SetParent(wispsRoot, false);
            go.AddComponent<CameraBillboard>();
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = fogWispSprite;
            sr.color = new Color(1f, 1f, 1f, 0.35f);
            sr.sortingOrder = FogBaseSortingOrder + 20;
            sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            sr.receiveShadows = false;
            go.SetActive(false);
            fogWispRoots[i] = go.transform;
            fogWispRenderers[i] = sr;
        }
    }

    private static Sprite CreateSoftWispSprite(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        float cx = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float nx = (x - cx) / cx;
                float ny = (y - cx) / cx;
                float d = Mathf.Sqrt(nx * nx + ny * ny);
                float a = Mathf.Clamp01(1f - d);
                a = a * a * (0.55f + Mathf.PerlinNoise(x * 0.08f, y * 0.08f) * 0.45f);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size * 0.55f);
    }

    private void Update()
    {
        // 上层雾面缓慢漂移，斜视下产生视差体积感
        if (fogVolumeRoot == null || fogLayerRenderers == null)
            return;
        float t = Time.time;
        for (int i = 1; i < fogLayerRenderers.Length; i++)
        {
            if (fogLayerRenderers[i] == null)
                continue;
            var tr = fogLayerRenderers[i].transform;
            float amp = 0.08f + i * 0.05f;
            float ox = Mathf.Sin(t * (0.11f + i * 0.03f) + i) * amp;
            float oz = Mathf.Cos(t * (0.09f + i * 0.025f) + i * 1.7f) * amp;
            float y = FogLayerHeights[i] + i * 0.35f;
            tr.localPosition = new Vector3(ox, y, oz);
        }
        if (fogWispRoots == null)
            return;
        for (int i = 0; i < fogWispRoots.Length; i++)
        {
            if (fogWispRoots[i] == null || !fogWispRoots[i].gameObject.activeSelf)
                continue;
            var p = fogWispRoots[i].localPosition;
            p.y += Mathf.Sin(t * 0.7f + i * 0.4f) * 0.0018f;
            fogWispRoots[i].localPosition = p;
        }
    }

    private void EnsureHighlandMaterial()
    {
        if (highlandMaterial != null)
            return;
        var shader = Shader.Find("Unlit/Color")
            ?? Shader.Find("Universal Render Pipeline/Unlit")
            ?? Shader.Find("Sprites/Default")
            ?? Shader.Find("Standard");
        highlandMaterial = new Material(shader) { name = "HighlandSlab" };
        if (highlandMaterial.HasProperty("_BaseColor"))
            highlandMaterial.SetColor("_BaseColor", HighlandRock);
        if (highlandMaterial.HasProperty("_Color"))
            highlandMaterial.SetColor("_Color", HighlandRock);
        highlandMaterial.color = HighlandRock;
    }

    /// <summary>18×18 外茂密装饰树林：仅表现，不可走、不可选。超大地坪防止露出真空。</summary>
    private void BuildForestBorder(GridManager grid)
    {
        decorRoot = new GameObject("ForestBorder").transform;
        decorRoot.SetParent(transform, false);

        boardW = grid.gridWidth * grid.cellSize;
        boardD = grid.gridHeight * grid.cellSize;
        var flatRot = Quaternion.Euler(90f, 0f, 0f);
        var boardCenter = grid.originPosition + new Vector3(boardW * 0.5f, 0f, boardD * 0.5f);

        // 超大连续地坪：拉远 / 宽屏也不露清屏色
        var groundGo = new GameObject("ForestGroundPlane");
        groundGo.transform.SetParent(decorRoot, false);
        groundGo.transform.SetPositionAndRotation(boardCenter + Vector3.down * 0.05f, flatRot);
        groundGo.transform.localScale = new Vector3(ForestGroundPlaneSize, ForestGroundPlaneSize, 1f);
        var gsr = groundGo.AddComponent<SpriteRenderer>();
        gsr.sprite = SpriteFactory.CreateColorSprite(new Color(0.11f, 0.22f, 0.11f));
        gsr.sortingOrder = -800;

        var floorA = TerrainSpriteFactory.CreateForestFloor(false);
        var floorB = TerrainSpriteFactory.CreateForestFloor(true);
        var trees = new[]
        {
            TerrainSpriteFactory.CreateForestTree(0),
            TerrainSpriteFactory.CreateForestTree(1),
            TerrainSpriteFactory.CreateForestTree(2)
        };

        int w = grid.gridWidth;
        int h = grid.gridHeight;
        float cell = grid.cellSize;

        for (int x = -ForestOuterMargin; x < w + ForestOuterMargin; x++)
        {
            for (int y = -ForestOuterMargin; y < h + ForestOuterMargin; y++)
            {
                if (x >= 0 && x < w && y >= 0 && y < h)
                    continue;

                int distOut = 0;
                if (x < 0) distOut = Mathf.Max(distOut, -x);
                if (y < 0) distOut = Mathf.Max(distOut, -y);
                if (x >= w) distOut = Mathf.Max(distOut, x - w + 1);
                if (y >= h) distOut = Mathf.Max(distOut, y - h + 1);

                bool nearRing = distOut <= ForestMargin;
                // 外圈隔格生成，控制数量
                if (!nearRing && (((x + y) & 1) != 0 || distOut % 2 != 0))
                    continue;

                Vector3 center = grid.originPosition + new Vector3(
                    x * cell + cell * 0.5f, 0f, y * cell + cell * 0.5f);

                if (nearRing)
                {
                    bool alt = ((x + y) & 1) != 0;
                    var floorGo = new GameObject($"ForestFloor_{x}_{y}");
                    floorGo.transform.SetParent(decorRoot, false);
                    floorGo.transform.SetPositionAndRotation(center, flatRot);
                    floorGo.transform.localScale = Vector3.one * 1.02f;
                    var fsr = floorGo.AddComponent<SpriteRenderer>();
                    fsr.sprite = alt ? floorB : floorA;
                    fsr.sortingOrder = y * 20 + x - 40;
                }

                int treeCount = nearRing
                    ? (distOut <= 2 ? 2 : 3)
                    : (distOut <= 18 ? 2 : 1);

                int hash = x * 73856093 ^ y * 19349663;
                for (int t = 0; t < treeCount; t++)
                {
                    int h2 = hash + t * 83492791;
                    float ox = (((h2 >> 3) & 255) / 255f - 0.5f) * cell * 0.75f;
                    float oz = (((h2 >> 11) & 255) / 255f - 0.5f) * cell * 0.75f;
                    float scale = (nearRing ? 0.9f : 1.1f) + (((h2 >> 19) & 255) / 255f) * 0.55f;

                    var treeGo = new GameObject($"Tree_{x}_{y}_{t}");
                    treeGo.transform.SetParent(decorRoot, false);
                    treeGo.transform.position = center + new Vector3(ox, 0.02f, oz);
                    treeGo.transform.localScale = Vector3.one * scale;
                    var tsr = treeGo.AddComponent<SpriteRenderer>();
                    tsr.sprite = trees[Mathf.Abs(h2) % trees.Length];
                    tsr.sortingOrder = y * 20 + x + 8 + t;
                    treeGo.AddComponent<CameraBillboard>();
                }
            }
        }
    }

    /// <summary>桌面设置已移除对局表现；保留空实现以免菜单报错。</summary>
    public void ApplyTableStyle(TableStyle style)
    {
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

                tileRoots[x, y].position = grid.CellToWorld(cell);
                cellRenderers[x, y].sprite = TerrainSpriteFactory.CreateTop(type, alt);
                cellRenderers[x, y].sortingOrder = grid.GetSortOrder(cell, 0);
                // 精灵默认 1 单位边长；缩放到 cellSize 才能铺满格、不留缝
                float fill = grid.cellSize * 1.002f;
                cellRenderers[x, y].transform.localScale = Vector3.one * fill;

                bool highland = type == TileType.Highland;
                if (highlandSlabs[x, y] != null)
                    highlandSlabs[x, y].gameObject.SetActive(highland);

                overlayRenderers[x, y].sortingOrder = grid.GetSortOrder(cell, 4);
            }
        }

        RebuildJungleDecor(grid);
        ApplyFog(VisibilityService.GetFogViewer());
    }

    private void RebuildJungleDecor(GridManager grid)
    {
        if (jungleDecorRoots == null || tileRoots == null)
            return;
        if (junglePropSprites == null || junglePropSprites.Length == 0)
        {
            junglePropSprites = new[]
            {
                TerrainSpriteFactory.CreateJungleProp(0),
                TerrainSpriteFactory.CreateJungleProp(1),
                TerrainSpriteFactory.CreateJungleProp(2)
            };
        }

        float cell = grid.cellSize;
        for (int x = 0; x < grid.gridWidth; x++)
        {
            for (int y = 0; y < grid.gridHeight; y++)
            {
                if (jungleDecorRoots[x, y] != null)
                {
                    Destroy(jungleDecorRoots[x, y].gameObject);
                    jungleDecorRoots[x, y] = null;
                }

                if (grid.GetTileType(new Vector2Int(x, y)) != TileType.Jungle)
                    continue;

                var rootGo = new GameObject("JungleDecor");
                rootGo.transform.SetParent(tileRoots[x, y], false);
                jungleDecorRoots[x, y] = rootGo.transform;

                int hash = x * 73856093 ^ y * 19349663 ^ 0x5f3759df;
                // 一株稍大的主丛 + 3～4 株填角，整体更厚
                int count = 4 + ((hash >> 5) & 1);
                for (int t = 0; t < count; t++)
                {
                    int h2 = hash + t * 83492791;
                    bool main = t == 0;
                    float cornerX = ((h2 & 1) == 0 ? -1f : 1f);
                    float cornerZ = (((h2 >> 1) & 1) == 0 ? -1f : 1f);
                    float ox = main
                        ? (((h2 >> 8) & 255) / 255f - 0.5f) * cell * 0.22f
                        : cornerX * cell * (0.22f + ((h2 >> 8) & 255) / 255f * 0.2f);
                    float oz = main
                        ? (((h2 >> 16) & 255) / 255f - 0.5f) * cell * 0.22f
                        : cornerZ * cell * (0.22f + ((h2 >> 16) & 255) / 255f * 0.2f);
                    float scale = main
                        ? 0.95f + ((h2 >> 20) & 255) / 255f * 0.25f
                        : 0.7f + ((h2 >> 20) & 255) / 255f * 0.28f;

                    var prop = new GameObject($"JungleProp_{t}");
                    prop.transform.SetParent(rootGo.transform, false);
                    prop.transform.localPosition = new Vector3(ox, 0.02f, oz);
                    prop.transform.localScale = Vector3.one * scale;
                    var sr = prop.AddComponent<SpriteRenderer>();
                    sr.sprite = junglePropSprites[Mathf.Abs(h2) % junglePropSprites.Length];
                    sr.sortingOrder = grid.GetSortOrder(new Vector2Int(x, y), 8 + t);
                    prop.AddComponent<CameraBillboard>();
                }
            }
        }
    }

    /// <summary>
    /// 迷雾：整板连续贴图 + 视野边缘软过渡；视野外仍隐藏地形/高地以免泄密。
    /// </summary>
    public void ApplyFog(UnitActor viewer)
    {
        if (tileRoots == null || cellRenderers == null)
            return;
        var grid = GridManager.Instance;
        if (grid == null)
            return;

        int vis = viewer != null ? viewer.CurrentVisibility : int.MaxValue;
        float viewerFx = viewer != null ? viewer.Cell.x + 0.5f : 0f;
        float viewerFy = viewer != null ? viewer.Cell.y + 0.5f : 0f;

        for (int x = 0; x < grid.gridWidth; x++)
        {
            for (int y = 0; y < grid.gridHeight; y++)
            {
                var cell = new Vector2Int(x, y);
                bool seen = viewer == null || VisibilityService.CanSeeCell(viewer, cell);
                bool highland = grid.GetTileType(cell) == TileType.Highland;

                cellRenderers[x, y].enabled = seen;

                if (highlandSlabs[x, y] != null)
                    highlandSlabs[x, y].gameObject.SetActive(highland && seen);

                if (jungleDecorRoots != null && jungleDecorRoots[x, y] != null)
                    jungleDecorRoots[x, y].gameObject.SetActive(seen);

                // 迷雾下取消高地抬升，否则雾面本身会露出地势
                Vector3 pos = grid.CellToWorld(cell);
                if (!seen)
                    pos.y = 0f;
                tileRoots[x, y].position = pos;
            }
        }

        RebuildFogVolume(grid, viewer, vis, viewerFx, viewerFy);
    }

    private void RebuildFogVolume(GridManager grid, UnitActor viewer, int vis, float viewerFx, float viewerFy)
    {
        if (fogLayerTextures == null || fogLayerRenderers == null)
            return;

        var style = GetVisionVeilStyle();
        int tw = fogLayerTextures[0].width;
        int th = fogLayerTextures[0].height;
        float invTpc = 1f / FogTexelsPerCell;
        float soft = Mathf.Max(0.35f, style.SoftCells);
        int pad = fogPadCells;
        var fogCells = new System.Collections.Generic.List<Vector2Int>(128);

        for (int py = 0; py < th; py++)
        {
            float cellY = (py + 0.5f) * invTpc - pad;
            int gy = Mathf.FloorToInt(cellY);
            for (int px = 0; px < tw; px++)
            {
                float cellX = (px + 0.5f) * invTpc - pad;
                int gx = Mathf.FloorToInt(cellX);
                bool onBoard = grid.IsValidCell(new Vector2Int(gx, gy));
                float mask = ComputeFogMask(viewer, vis, soft, viewerFx, viewerFy, cellX, cellY, gx, gy, grid);
                if (style.ShowWisps && mask > 0.55f && (px % FogTexelsPerCell == FogTexelsPerCell / 2)
                    && (py % FogTexelsPerCell == FogTexelsPerCell / 2))
                    fogCells.Add(new Vector2Int(gx, gy));

                for (int layer = 0; layer < fogLayerTextures.Length; layer++)
                {
                    float layerMask = mask;
                    if (layer > 0 && mask > 0.001f && mask < 0.999f)
                        layerMask = Mathf.Clamp01(mask * (1.15f + layer * 0.12f) - layer * 0.08f);
                    else if (layer > 0 && mask >= 0.999f)
                        layerMask = 1f;

                    float layerAlphaMul = FogLayerAlphaMul[layer];
                    if (layer > 0)
                        layerAlphaMul *= style.UpperLayerMul;

                    SampleFogAppearance(
                        cellX, cellY, layerMask * layerAlphaMul * style.Density,
                        FogLayerNoiseSeed[layer], layer, style.TextureStrength,
                        out Color fogPix);

                    // 棋盘内：必须挡住地形信息；棋盘外：晴天可半透露出远树剪影
                    if (mask >= 0.99f)
                    {
                        float floorA = onBoard ? style.BoardMinOpaque : style.ForestMinOpaque;
                        if (layer == 0)
                            fogPix.a = Mathf.Max(fogPix.a, floorA);
                        else if (!onBoard)
                            fogPix.a = Mathf.Min(fogPix.a, floorA * layerAlphaMul + 0.15f);
                    }
                    fogLayerTextures[layer].SetPixel(px, py, fogPix);
                }
            }
        }

        for (int layer = 0; layer < fogLayerTextures.Length; layer++)
        {
            fogLayerTextures[layer].Apply(false, false);
            if (fogLayerRenderers[layer] != null)
            {
                fogLayerRenderers[layer].color = style.Tint;
                fogLayerRenderers[layer].enabled = true;
            }
        }

        PlaceFogWisps(grid, fogCells, style);
    }

    /// <summary>
    /// 雾天：浓团雾（色随时段）；晴天：空气透视；雨天：冷灰幕。黑夜三种都压暗，但非纯黑。
    /// </summary>
    private static VisionVeilStyle GetVisionVeilStyle()
    {
        int round = TurnManager.Instance != null ? TurnManager.Instance.RoundNumber : 1;
        var period = GameClock.GetPeriod(round);
        var weather = WeatherService.Current;
        bool night = period == GameClock.Period.Night;

        if (weather == WeatherType.Fog)
        {
            return new VisionVeilStyle
            {
                Tint = GetFogWeatherTint(period),
                Density = night ? 1.05f : 1f,
                BoardMinOpaque = night ? 0.97f : 0.95f,
                ForestMinOpaque = night ? 0.96f : 0.94f,
                TextureStrength = 1f,
                SoftCells = FogSoftCells,
                UpperLayerMul = 1f,
                WispAlpha = night ? 0.22f : 0.32f,
                ShowWisps = true
            };
        }

        if (weather == WeatherType.Rain)
        {
            return new VisionVeilStyle
            {
                Tint = GetRainWeatherTint(period),
                Density = night ? 0.98f : 0.92f,
                BoardMinOpaque = night ? 0.95f : 0.91f,
                ForestMinOpaque = night ? 0.88f : 0.78f,
                TextureStrength = night ? 0.45f : 0.55f,
                SoftCells = 2.8f,
                UpperLayerMul = night ? 0.85f : 0.7f,
                WispAlpha = night ? 0.12f : 0.18f,
                ShowWisps = true
            };
        }

        // 晴天：空气透视——柔边更宽、上层更薄、远林半透剪影
        return new VisionVeilStyle
        {
            Tint = GetClearWeatherTint(period),
            Density = night ? 0.95f : 0.82f,
            BoardMinOpaque = night ? 0.95f : 0.9f,
            ForestMinOpaque = night ? 0.82f : 0.48f,
            TextureStrength = 0.12f,
            SoftCells = night ? 2.8f : 3.8f,
            UpperLayerMul = night ? 0.5f : 0.28f,
            WispAlpha = 0.06f,
            ShowWisps = false
        };
    }

    private static Color GetFogWeatherTint(GameClock.Period period)
    {
        switch (period)
        {
            case GameClock.Period.Dawn:
                return new Color(0.9f, 0.82f, 0.74f, 1f);   // 暖乳白雾
            case GameClock.Period.Dusk:
                return new Color(0.78f, 0.68f, 0.62f, 1f);   // 暮色灰雾
            case GameClock.Period.Night:
                return new Color(0.16f, 0.18f, 0.26f, 1f);   // 夜雾：靛灰
            default:
                return new Color(0.94f, 0.95f, 0.97f, 1f);   // 白天银白雾
        }
    }

    private static Color GetRainWeatherTint(GameClock.Period period)
    {
        switch (period)
        {
            case GameClock.Period.Dawn:
                return new Color(0.68f, 0.72f, 0.78f, 1f);   // 清晨湿冷灰
            case GameClock.Period.Dusk:
                return new Color(0.55f, 0.52f, 0.62f, 1f);   // 黄昏紫灰雨幕
            case GameClock.Period.Night:
                return new Color(0.1f, 0.12f, 0.18f, 1f);    // 夜雨：深青黑
            default:
                return new Color(0.62f, 0.7f, 0.8f, 1f);     // 白天冷蓝灰
        }
    }

    private static Color GetClearWeatherTint(GameClock.Period period)
    {
        switch (period)
        {
            case GameClock.Period.Dawn:
                return new Color(0.93f, 0.86f, 0.78f, 1f);   // 暖杏
            case GameClock.Period.Dusk:
                return new Color(0.9f, 0.72f, 0.62f, 1f);    // 暮橙
            case GameClock.Period.Night:
                return new Color(0.1f, 0.12f, 0.2f, 1f);     // 夜幕靛
            default:
                return new Color(0.78f, 0.88f, 0.96f, 1f);   // 晴空蓝白
        }
    }

    private static float ComputeFogMask(
        UnitActor viewer, int vis, float soft,
        float viewerFx, float viewerFy,
        float cellX, float cellY, int gx, int gy, GridManager grid)
    {
        if (viewer == null)
            return 0f;
        if (vis >= ItemInfo.FullMapVisibilityRadius)
            return 0f;

        float dist = Mathf.Abs(cellX - viewerFx) + Mathf.Abs(cellY - viewerFy);
        bool onBoard = grid != null && grid.IsValidCell(new Vector2Int(gx, gy));
        if (onBoard)
        {
            if (!VisibilityService.CanSeeCell(viewer, new Vector2Int(gx, gy)))
                return 1f;
        }
        else
        {
            // 棋盘外装饰林：按同款曼哈顿视野，视野外整片起雾盖住远树
            if (dist > vis + 0.01f)
                return 1f;
        }

        float t = Mathf.Clamp01((dist - (vis - soft)) / soft);
        return t * t * (3f - 2f * t);
    }

    private void PlaceFogWisps(GridManager grid, System.Collections.Generic.List<Vector2Int> fogCells, VisionVeilStyle style)
    {
        if (fogWispRoots == null || fogVolumeRoot == null)
            return;

        if (!style.ShowWisps || fogCells == null || fogCells.Count == 0)
        {
            for (int i = 0; i < fogWispRoots.Length; i++)
            {
                if (fogWispRoots[i] != null)
                    fogWispRoots[i].gameObject.SetActive(false);
            }
            return;
        }

        Vector3 volumeOrigin = fogVolumeRoot.position;
        float cell = grid.cellSize;
        // 晴天少絮；雾天全开
        int useCount = style.TextureStrength > 0.8f
            ? fogWispRoots.Length
            : Mathf.Max(8, fogWispRoots.Length / 3);

        for (int i = 0; i < fogWispRoots.Length; i++)
        {
            if (i >= useCount)
            {
                fogWispRoots[i].gameObject.SetActive(false);
                continue;
            }

            var cellPos = fogCells[(i * 17 + 5) % fogCells.Count];
            Vector3 world = grid.originPosition + new Vector3(
                cellPos.x * cell + cell * 0.5f, 0f, cellPos.y * cell + cell * 0.5f);
            float jx = (Mathf.PerlinNoise(i * 0.37f, cellPos.x * 0.2f) - 0.5f) * cell * 0.85f;
            float jz = (Mathf.PerlinNoise(i * 0.41f + 3f, cellPos.y * 0.2f) - 0.5f) * cell * 0.85f;
            float jy = 0.5f + (i % 6) * 0.35f + Mathf.PerlinNoise(i * 0.2f, 9f) * 0.55f;
            Vector3 local = new Vector3(world.x - volumeOrigin.x + jx, jy, world.z - volumeOrigin.z + jz);
            fogWispRoots[i].localPosition = local;
            float scale = 2.1f + (i % 5) * 0.45f;
            fogWispRoots[i].localScale = Vector3.one * scale;
            if (fogWispRenderers[i] != null)
            {
                float a = style.WispAlpha + (i % 4) * 0.04f;
                Color c = style.Tint;
                fogWispRenderers[i].color = new Color(c.r, c.g, c.b, a);
            }
            fogWispRoots[i].gameObject.SetActive(true);
        }
    }

    /// <summary>textureStrength：1=团雾纹理，0=均匀天空霾。</summary>
    private static void SampleFogAppearance(
        float cellX, float cellY, float maskAlpha, float seed, int layer, float textureStrength, out Color pix)
    {
        if (maskAlpha <= 0.001f)
        {
            pix = new Color(1f, 1f, 1f, 0f);
            return;
        }

        float warp = Mathf.PerlinNoise(cellX * 0.28f + 11.3f + seed, cellY * 0.28f + 3.7f + seed);
        float wx = cellX + (warp - 0.5f) * (2.4f + layer * 0.35f) * Mathf.Lerp(0.35f, 1f, textureStrength);
        float wy = cellY + (Mathf.PerlinNoise(cellX * 0.28f + 40.1f + seed, cellY * 0.28f + 17.9f) - 0.5f)
            * (2.4f + layer * 0.35f) * Mathf.Lerp(0.35f, 1f, textureStrength);

        float n0 = Mathf.PerlinNoise(wx * 0.22f, wy * 0.22f);
        float n1 = Mathf.PerlinNoise(wx * 0.55f + 19f + seed, wy * 0.55f + 7f);
        float n2 = Mathf.PerlinNoise(wx * 1.35f + 51f, wy * 1.35f + 23f + seed);
        float n3 = Mathf.PerlinNoise(wx * 3.1f + 88f + seed, wy * 3.1f + 61f);
        float fbm = n0 * 0.45f + n1 * 0.3f + n2 * 0.18f + n3 * 0.07f;

        // 晴天：接近均匀亮霾；雾天：深浅团块
        float lumFlat = 0.96f;
        float lumCloud = 0.78f + fbm * 0.22f;
        float lum = Mathf.Lerp(lumFlat, lumCloud, textureStrength);
        float r = Mathf.Clamp01(lum + 0.02f * textureStrength);
        float g = Mathf.Clamp01(lum);
        float b = Mathf.Clamp01(lum + 0.04f * textureStrength);

        float density = Mathf.Lerp(0.92f, 0.55f + fbm * 0.5f, textureStrength);
        float wisps = Mathf.Lerp(1f, Mathf.SmoothStep(0.25f, 0.85f, n1 * 0.6f + n2 * 0.4f), textureStrength);
        float a = maskAlpha * Mathf.Lerp(0.9f, Mathf.Lerp(0.65f, 1.05f, density), textureStrength) * wisps;
        a = Mathf.Clamp01(a);

        pix = new Color(r, g, b, a);
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
        // 终点 6–10；并淡标 3 宽走廊
        var soft = new Color(AttackHint.r, AttackHint.g, AttackHint.b, AttackHint.a * 0.45f);
        foreach (var d in dirs)
        {
            int sx = d.x;
            int sy = d.y;
            for (int dist = 6; dist <= 10; dist++)
            {
                var end = unit.Cell + d * dist;
                if (!grid.IsValidCell(end))
                    break;
                bool endOk = !StealthService.BlocksMovementFor(unit, end);
                for (int step = 1; step <= dist; step++)
                {
                    var center = unit.Cell + new Vector2Int(sx * step, sy * step);
                    for (int w = -1; w <= 1; w++)
                    {
                        var cell = sx != 0
                            ? new Vector2Int(center.x, center.y + w)
                            : new Vector2Int(center.x + w, center.y);
                        if (!grid.IsValidCell(cell))
                            continue;
                        bool isEnd = cell == end;
                        if (isEnd && !endOk)
                            continue;
                        var cur = overlayRenderers[cell.x, cell.y].color;
                        var next = isEnd && endOk ? AttackHint : soft;
                        if (cur.a < next.a || (isEnd && endOk))
                            overlayRenderers[cell.x, cell.y].color = next;
                    }
                }
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
