using System.Collections.Generic;

/// <summary>
/// 种子生长：每种特殊地形 1～2 个连通斑块，比例接近规则表；出生角附近保留普通地。
/// </summary>
public static class TerrainGenerator
{
    private static readonly TileType[] Biomes =
    {
        TileType.Sand,
        TileType.Swamp,
        TileType.Ice,
        TileType.Jungle,
        TileType.Highland
    };

    private static readonly Int2[] Ortho =
    {
        new Int2(1, 0),
        new Int2(-1, 0),
        new Int2(0, 1),
        new Int2(0, -1)
    };

    /// <summary>
    /// 生成地形：五种地形各 1~2 个连通斑块，出生点周围留白。
    /// </summary>
    public static void Generate(GridManager grid)
    {
        if (grid == null)
            return;

        int w = grid.gridWidth;
        int h = grid.gridHeight;
        int total = w * h;

        // 先铺满普通
        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
                grid.SetTileType(new Int2(x, y), TileType.Normal);

        var protectedCells = BuildProtectedSet(w, h);
        // 各特殊地形比例接近 biome_ratio；余下为普通
        int perBiome = System.Math.Max(GameRulesConfig.BiomeMinCells, (int)System.Math.Round(total * GameRulesConfig.BiomeRatio, System.MidpointRounding.AwayFromZero));

        var usedSeeds = new List<Int2>();
        foreach (var biome in Biomes)
        {
            int blobCount = Rng.Range(GameRulesConfig.BiomeBlobMin, GameRulesConfig.BiomeBlobMax + 1);
            int[] sizes = SplitSize(perBiome, blobCount);
            for (int b = 0; b < blobCount; b++)
            {
                if (!TryPickSeed(grid, protectedCells, usedSeeds, out var seed))
                    break;
                usedSeeds.Add(seed);
                GrowBlob(grid, seed, biome, sizes[b], protectedCells);
            }
        }

        // 出生点邻域再强制普通，防止生长挤入
        foreach (var c in protectedCells)
        {
            if (grid.GetTileType(c) != TileType.Lava)
                grid.SetTileType(c, TileType.Normal);
        }

        TurnManager.Instance?.Log(
            $"地形已生成（种子生长）：沙/沼/冰/丛/高地各约 {perBiome} 格");
    }

    private static HashSet<Int2> BuildProtectedSet(int w, int h)
    {
        var set = new HashSet<Int2>();
        int radius = GameRulesConfig.SpawnProtectRadius;
        foreach (RoleType role in System.Enum.GetValues(typeof(RoleType)))
        {
            var center = GameRulesConfig.SpawnCell(role);
            for (int dx = -radius; dx <= radius; dx++)
            {
                for (int dy = -radius; dy <= radius; dy++)
                {
                    var c = new Int2(center.x + dx, center.y + dy);
                    if (c.x >= 0 && c.x < w && c.y >= 0 && c.y < h)
                        set.Add(c);
                }
            }
        }
        return set;
    }

    private static int[] SplitSize(int total, int parts)
    {
        var sizes = new int[parts];
        if (parts == 1)
        {
            sizes[0] = total;
            return sizes;
        }

        // 两块：随机切分，每块至少约 biome_split_min_ratio
        int min = System.Math.Max(GameRulesConfig.BiomeSplitMinCells, (int)System.Math.Round(total * GameRulesConfig.BiomeSplitMinRatio, System.MidpointRounding.AwayFromZero));
        int max = total - min;
        if (max < min)
        {
            sizes[0] = total / 2;
            sizes[1] = total - sizes[0];
            return sizes;
        }
        sizes[0] = Rng.Range(min, max + 1);
        sizes[1] = total - sizes[0];
        return sizes;
    }

    private static bool TryPickSeed(
        GridManager grid, HashSet<Int2> protectedCells, List<Int2> usedSeeds, out Int2 seed)
    {
        seed = default;
        var candidates = new List<Int2>();
        for (int x = 0; x < grid.gridWidth; x++)
        {
            for (int y = 0; y < grid.gridHeight; y++)
            {
                var c = new Int2(x, y);
                if (protectedCells.Contains(c))
                    continue;
                if (grid.GetTileType(c) != TileType.Normal)
                    continue;
                bool far = true;
                for (int i = 0; i < usedSeeds.Count; i++)
                {
                    if (grid.GetManhattanDistance(c, usedSeeds[i]) < GameRulesConfig.BiomeSeedSpacing)
                    {
                        far = false;
                        break;
                    }
                }
                if (far)
                    candidates.Add(c);
            }
        }

        if (candidates.Count == 0)
        {
            // 放宽间距再试
            for (int x = 0; x < grid.gridWidth; x++)
            {
                for (int y = 0; y < grid.gridHeight; y++)
                {
                    var c = new Int2(x, y);
                    if (protectedCells.Contains(c))
                        continue;
                    if (grid.GetTileType(c) != TileType.Normal)
                        continue;
                    candidates.Add(c);
                }
            }
        }

        if (candidates.Count == 0)
            return false;
        seed = candidates[Rng.Range(0, candidates.Count)];
        return true;
    }

    private static void GrowBlob(
        GridManager grid, Int2 seed, TileType type, int target, HashSet<Int2> protectedCells)
    {
        if (target <= 0)
            return;

        var frontier = new List<Int2> { seed };
        grid.SetTileType(seed, type);
        int placed = 1;

        while (placed < target && frontier.Count > 0)
        {
            int idx = Rng.Range(0, frontier.Count);
            var cur = frontier[idx];
            // 打乱邻接顺序
            var dirs = new List<Int2>(Ortho);
            for (int i = dirs.Count - 1; i > 0; i--)
            {
                int j = Rng.Range(0, i + 1);
                (dirs[i], dirs[j]) = (dirs[j], dirs[i]);
            }

            bool grew = false;
            for (int d = 0; d < dirs.Count; d++)
            {
                var next = cur + dirs[d];
                if (!grid.IsValidCell(next))
                    continue;
                if (protectedCells.Contains(next))
                    continue;
                if (grid.GetTileType(next) != TileType.Normal)
                    continue;
                grid.SetTileType(next, type);
                frontier.Add(next);
                placed++;
                grew = true;
                if (placed >= target)
                    break;
            }

            if (!grew)
                frontier.RemoveAt(idx);
        }
    }
}
