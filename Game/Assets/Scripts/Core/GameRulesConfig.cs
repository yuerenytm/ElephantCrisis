using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>
/// 从 StreamingAssets/Config/game_rules.yaml 加载规则（与 LogicSim / Tools/sim 共用）。
/// 解析失败时回退内置默认值（与现行规则一致）。
/// </summary>
public static class GameRulesConfig
{
    public struct RoleBaseStats
    {
        public int Move;
        public int Hp;
        public int Atk;
        public int Def;
        public int Bag;
        public int MagicResist;
    }

    private static bool loaded;

    private static readonly Dictionary<RoleType, RoleBaseStats> roleStats =
        new Dictionary<RoleType, RoleBaseStats>();

    private static readonly Dictionary<string, Dictionary<string, string>> sections =
        new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<ItemKind, int> deckCounts =
        new Dictionary<ItemKind, int>();

    private static readonly Dictionary<RoleType, Vector2Int> spawnCells =
        new Dictionary<RoleType, Vector2Int>();

    private static readonly Regex InlineRole = new Regex(
        @"^\s*(elephant|human|monkey|cat)\s*:\s*\{\s*move\s*:\s*(-?\d+)\s*,\s*hp\s*:\s*(-?\d+)\s*,\s*atk\s*:\s*(-?\d+)\s*,\s*def\s*:\s*(-?\d+)\s*,\s*bag\s*:\s*(-?\d+)\s*(,\s*magic_resist\s*:\s*(-?\d+))?\s*\}\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ScalarLine = new Regex(
        @"^([A-Za-z0-9_]+)\s*:\s*(.+?)\s*$",
        RegexOptions.Compiled);

    private static readonly Regex SpawnCellLine = new Regex(
        @"^\s*(elephant|human|monkey|cat)\s*:\s*\[\s*(-?\d+)\s*,\s*(-?\d+)\s*\]\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void AutoLoad()
    {
        EnsureLoaded();
    }

    public static void EnsureLoaded()
    {
        if (loaded)
            return;
        loaded = true;
        ApplyFallbacks();

        string path = ResolveRulesPath();
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            Debug.LogWarning($"[GameRulesConfig] 未找到 game_rules.yaml，使用内置默认值。尝试路径: {path}");
            return;
        }

        try
        {
            string text = File.ReadAllText(path);
            ParseDocument(text);
            ApplyParsedRoles();
            ApplyParsedDeck();
            Debug.Log(
                $"[GameRulesConfig] 已加载: {path} · deck 条目 {deckCounts.Count} · " +
                $"熔岩缩圈每 {LavaShrinkEveryRounds} 回合 · 天气锁 {WeatherLockMin}-{WeatherLockMax}");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[GameRulesConfig] 读取失败，使用内置默认值: {e.Message}");
            ApplyFallbacks();
        }
    }

    /// <summary>仅供测试/热重载：清空后再次 EnsureLoaded。</summary>
    public static void ReloadForEditor()
    {
        loaded = false;
        sections.Clear();
        deckCounts.Clear();
        roleStats.Clear();
        spawnCells.Clear();
        EnsureLoaded();
    }

    public static RoleBaseStats GetRoleStats(RoleType role)
    {
        EnsureLoaded();
        if (roleStats.TryGetValue(role, out var s))
            return s;
        return FallbackRole(role);
    }

    public static int GetDeckCount(ItemKind kind)
    {
        EnsureLoaded();
        if (deckCounts.TryGetValue(kind, out int n))
            return Mathf.Max(0, n);
        return 0;
    }

    /// <summary>牌组总印数（所有印数 &gt; 0 的条目之和）。</summary>
    public static int DeckTotal
    {
        get
        {
            EnsureLoaded();
            int total = 0;
            foreach (ItemKind kind in Enum.GetValues(typeof(ItemKind)))
                total += GetDeckCount(kind);
            return total;
        }
    }

    /// <summary>按 ItemKind 枚举顺序返回印数 &gt; 0 的条目。</summary>
    public static void CollectPositiveDeckCounts(List<KeyValuePair<ItemKind, int>> dst)
    {
        EnsureLoaded();
        if (dst == null)
            return;
        dst.Clear();
        foreach (ItemKind kind in Enum.GetValues(typeof(ItemKind)))
        {
            int n = GetDeckCount(kind);
            if (n > 0)
                dst.Add(new KeyValuePair<ItemKind, int>(kind, n));
        }
    }

    public static int LavaShrinkEveryRounds => Mathf.Max(1, GetInt("lava", "shrink_every_rounds", 5));
    public static int LavaTrueDamage => Mathf.Max(0, GetInt("combat", "lava_true_damage", 20));
    public static int BurningMagicDamage => Mathf.Max(0, GetInt("combat", "burning_magic_damage", 10));

    public static int WeatherLockMin => Mathf.Max(1, GetInt("weather", "lock_min", 6));
    public static int WeatherLockMax
    {
        get
        {
            int min = WeatherLockMin;
            int max = GetInt("weather", "lock_max", 10);
            return Mathf.Max(min, max);
        }
    }

    public static int RainDefMod => GetInt("weather", "rain_def_mod", -3);
    public static int RainMagicResist => GetInt("weather", "rain_magic_resist", 25);
    public static int FogMagicResist => GetInt("weather", "fog_magic_resist", 10);

    public static int ClockStartHour => ((GetInt("clock", "start_hour", 6) % 24) + 24) % 24;
    public static int HoursPerRound => Mathf.Max(1, GetInt("clock", "hours_per_round", 2));

    public static int MaxFullRounds => Mathf.Max(1, GetInt("match", "max_full_rounds", 80));
    public static int DrawPerTurn => Mathf.Max(1, GetInt("match", "draw_per_turn", 1));
    public static int ActionsPerRound => Mathf.Max(1, GetInt("match", "actions_per_round", 4));
    public static int PickupRange => Mathf.Max(1, GetInt("match", "pickup_range", 1));

    public static int GridWidth => Mathf.Max(1, GetInt("grid", "width", 18));
    public static int GridHeight => Mathf.Max(1, GetInt("grid", "height", 18));

    /// <summary>出生格（来自 grid.spawn_cells）。</summary>
    public static Vector2Int SpawnCell(RoleType role)
    {
        EnsureLoaded();
        if (spawnCells.TryGetValue(role, out var c))
            return c;
        return FallbackSpawnCell(role);
    }

    public static float IceTripChance
    {
        get
        {
            float v = GetFloat("terrain", "ice_trip_chance", 0.2f);
            return Mathf.Clamp01(v);
        }
    }

    // ---------- 战斗基础 ----------
    public static int MeleeRange => Mathf.Max(1, GetInt("combat", "melee_range", 1));
    public static int CursedBladeTargetMult => GetInt("combat", "cursed_blade_target_mult", 2);
    public static int CursedBladeSelfMult => GetInt("combat", "cursed_blade_self_mult", 1);
    public static int MoveMinAlive => Mathf.Max(1, GetInt("combat", "move_min_alive", 1));

    // ---------- 虚拟时钟时段 ----------
    public static int PeriodDawnStart => GetInt("clock", "period_dawn_start", 4);
    public static int PeriodDayStart => GetInt("clock", "period_day_start", 8);
    public static int PeriodDuskStart => GetInt("clock", "period_dusk_start", 16);
    public static int PeriodNightStart => GetInt("clock", "period_night_start", 20);
    public static int DawnRepHour => GetInt("clock", "dawn_rep_hour", 6);
    public static int DayRepHour => GetInt("clock", "day_rep_hour", 12);
    public static int DuskRepHour => GetInt("clock", "dusk_rep_hour", 18);
    public static int NightRepHour => GetInt("clock", "night_rep_hour", 0);

    // ---------- 能见度矩阵 ----------
    public static int FullMapVisibility => Mathf.Max(1, GetInt("visibility", "full_map", 40));
    public static int NightClearVisibility => GetInt("visibility", "night_clear", 5);
    public static int NightRainVisibility => GetInt("visibility", "night_rain", 4);
    public static int NightFogVisibility => GetInt("visibility", "night_fog", 3);
    public static int FogDayVisibility => GetInt("visibility", "fog_day", 8);
    public static int FogDawnDuskVisibility => GetInt("visibility", "fog_dawn_dusk", 6);

    // ---------- 状态 ----------
    public static int TripDef => GetInt("status", "trip_def", -3);
    public static int TripMove => GetInt("status", "trip_move", -1);
    public static int TripRounds => GetInt("status", "trip_rounds", 3);
    public static int PoisonPerStackAtk => GetInt("status", "poison_per_stack_atk", -2);
    public static int PoisonPerStackDef => GetInt("status", "poison_per_stack_def", -2);
    public static int PoisonPerStackMove => GetInt("status", "poison_per_stack_move", -2);
    public static int PoisonEndDamagePerStack => GetInt("status", "poison_end_damage_per_stack", 2);
    public static int PoisonVisibility => GetInt("status", "poison_visibility", 2);
    public static int PoisonMaxStacks => Mathf.Max(1, GetInt("status", "poison_max_stacks", 3));
    public static int LeaderAtk => GetInt("status", "leader_atk", 9);
    public static int LeaderDef => GetInt("status", "leader_def", 9);
    public static int LeaderMove => GetInt("status", "leader_move", 3);
    public static int LeaderStartDamage => GetInt("status", "leader_start_damage", 6);
    public static int AdrenalineMove => GetInt("status", "adrenaline_move", 2);
    public static int AdrenalineAtk => GetInt("status", "adrenaline_atk", 3);
    public static int AdrenalineDuration => GetInt("status", "adrenaline_duration", 3);
    public static int AdrenalineHpThresholdPct => GetInt("status", "adrenaline_hp_threshold_pct", 30);
    public static int DyingVisibility => GetInt("status", "dying_visibility", 1);
    public static int DyingActions => GetInt("status", "dying_actions", 12);
    public static int BlindVisibility => GetInt("status", "blind_visibility", 0);
    public static int HiddenRevealRange => GetInt("status", "hidden_reveal_range", 1);
    public static int PoisonRounds => Mathf.Max(1, GetInt("status", "poison_rounds", 1));
    public static int BurningRounds => Mathf.Max(1, GetInt("status", "burning_rounds", 1));
    public static int StunRounds => Mathf.Max(1, GetInt("status", "stun_rounds", 1));
    public static int BlindRounds => Mathf.Max(1, GetInt("status", "blind_rounds", 1));

    // ---------- 技能 ----------
    public static int HumanCooldown(int level) => GetInt("skills", "human_cooldown_lv" + level, LevelFallback(level, 8, 6, 5));
    public static int MonkeyCooldown(int level) => GetInt("skills", "monkey_cooldown_lv" + level, LevelFallback(level, 5, 4, 3));
    public static int CatCooldown(int level) => GetInt("skills", "cat_cooldown_lv" + level, LevelFallback(level, 5, 4, 3));
    public static int MonkeyRadius(int level) => GetInt("skills", "monkey_radius_lv" + level, LevelFallback(level, 2, 3, 4));
    public static int DeterrenceOuterRadius(int level) => GetInt("skills", "deterrence_outer_lv" + level, LevelFallback(level, 3, 5, 7));
    public static int DeterrenceInnerRadius => GetInt("skills", "deterrence_inner_radius", 2);
    public static int DeterrenceInnerPenalty => GetInt("skills", "deterrence_inner_penalty", 2);
    public static int DeterrenceOuterPenalty => GetInt("skills", "deterrence_outer_penalty", 1);
    public static int HumanReinforceAmount => GetInt("skills", "human_reinforce_amount", 1);

    // ---------- 领袖宣言 ----------
    public static int LeaderDuration => GetInt("leader", "duration", 10);
    public static int LeaderRandomDraw => GetInt("leader", "random_draw", 5);
    public static int LeaderDollRequired => GetInt("leader", "doll_required", 3);

    // ---------- 地形 ----------
    public static int SandMove => GetInt("terrain", "sand_move", -1);
    public static int IceMove => GetInt("terrain", "ice_move", 1);
    public static int HighlandAtk => GetInt("terrain", "highland_atk", 3);
    public static int HighlandDef => GetInt("terrain", "highland_def", 3);
    public static int HighlandRange => GetInt("terrain", "highland_range", 1);
    public static int JungleMagicResist => GetInt("terrain", "jungle_magic_resist", 20);
    public static int SandMagicResist => GetInt("terrain", "sand_magic_resist", -10);
    public static int SwampMagicResist => GetInt("terrain", "swamp_magic_resist", -20);
    public static float BiomeRatio => GetFloat("terrain", "biome_ratio", 0.10f);
    public static int BiomeMinCells => GetInt("terrain", "biome_min_cells", 8);
    public static int BiomeBlobMin => Mathf.Max(1, GetInt("terrain", "biome_blob_min", 1));
    public static int BiomeBlobMax => Mathf.Max(1, GetInt("terrain", "biome_blob_max", 2));
    public static float BiomeSplitMinRatio => GetFloat("terrain", "biome_split_min_ratio", 0.30f);
    public static int BiomeSplitMinCells => GetInt("terrain", "biome_split_min_cells", 4);
    public static int SpawnProtectRadius => GetInt("terrain", "spawn_protect_radius", 2);
    public static int BiomeSeedSpacing => GetInt("terrain", "biome_seed_spacing", 4);

    // ---------- 卡牌数值（键 = ItemKind 蛇形名 + 后缀） ----------
    public static int SmallPotionHeal => Item(ItemKind.SmallPotion, "heal", 9);
    public static int LargePotionHeal => Item(ItemKind.LargePotion, "heal", 15);
    public static int BombDamage => Item(ItemKind.Bomb, "damage", 15);
    public static int MegaBombDamage => Item(ItemKind.MegaBomb, "damage", 24);
    public static int BombBlastRadius => Item(ItemKind.Bomb, "radius", 2);
    public static int BombRange => Item(ItemKind.Bomb, "range", 5);
    public static int TimedBombDamage => Item(ItemKind.TimedBomb, "damage", 15);
    public static int TimedBombRadius => Item(ItemKind.TimedBomb, "radius", 4);
    public static int TimedBombMinRounds => Item(ItemKind.TimedBomb, "min_rounds", 1);
    public static int TimedBombMaxRounds => Item(ItemKind.TimedBomb, "max_rounds", 5);
    public static int BoomerangRange => Item(ItemKind.Boomerang, "range", 4);
    public static int BoomerangDamage => Item(ItemKind.Boomerang, "damage", 15);
    public static int MolotovDamage => GetInt("items", "molotov_damage", 10);
    public static int MolotovRadius => GetInt("items", "molotov_radius", 2);
    public static int MolotovRange => GetInt("items", "molotov_range", 5);
    public static int MolotovFlameRounds => GetInt("items", "molotov_flame_rounds", 2);
    public static int FlamethrowerDamage => Item(ItemKind.Flamethrower, "damage", 10);
    public static int FlamethrowerRange => Item(ItemKind.Flamethrower, "range", 5);
    public static int FlamethrowerFlameRounds => Item(ItemKind.Flamethrower, "flame_rounds", 2);
    public static int FlashbangRadius => Item(ItemKind.Flashbang, "radius", 2);
    public static int FlashbangRange => Item(ItemKind.Flashbang, "range", 5);
    public static int FlashbangBlindRounds => Item(ItemKind.Flashbang, "blind_rounds", 1);
    public static int MineDamage => Item(ItemKind.Mine, "damage", 15);
    public static int BowAtk => Item(ItemKind.Bow, "atk", 2);
    public static int BowRange => Item(ItemKind.Bow, "range", 3);
    public static int CrossbowAtk => Item(ItemKind.Crossbow, "atk", 10);
    public static int CrossbowRange => Item(ItemKind.Crossbow, "range", 5);
    public static int DaggerAtk => Item(ItemKind.Dagger, "atk", 6);
    public static int DaggerRange => Item(ItemKind.Dagger, "range", 1);
    public static int LongswordAtk => Item(ItemKind.Longsword, "atk", 8);
    public static int LongswordRange => Item(ItemKind.Longsword, "range", 2);
    public static int ArmorPiercingBladeAtk => Item(ItemKind.ArmorPiercingBlade, "atk", 6);
    public static int ArmorPiercingBladeRange => Item(ItemKind.ArmorPiercingBlade, "range", 1);
    public static int CursedBladeRange => Item(ItemKind.CursedBlade, "range", 1);
    public static int WoodArmorDef => Item(ItemKind.WoodArmor, "def", 4);
    public static int WoodArmorCharges => Item(ItemKind.WoodArmor, "charges", 5);
    public static int IronArmorDef => Item(ItemKind.IronArmor, "def", 8);
    public static int IronArmorCharges => Item(ItemKind.IronArmor, "charges", 8);
    public static int RubberRaincoatDef => Item(ItemKind.RubberRaincoat, "def", 3);
    public static int RubberRaincoatCharges => Item(ItemKind.RubberRaincoat, "charges", 3);
    public static int ThornsArmorDef => Item(ItemKind.ThornsArmor, "def", 2);
    public static int ThornsArmorCharges => Item(ItemKind.ThornsArmor, "charges", 4);
    public static int ThornsReflectPct => GetInt("items", "thorns_reflect_pct", 50);
    public static int TacticalVestDef => Item(ItemKind.TacticalVest, "def", 2);
    public static int TacticalVestCharges => Item(ItemKind.TacticalVest, "charges", 6);
    public static int TacticalVestBag => Item(ItemKind.TacticalVest, "bag", 5);
    public static int TacticalVestExtraMitigation => Item(ItemKind.TacticalVest, "extra_mitigation", 2);
    public static int EnergyShieldCharges => Item(ItemKind.EnergyShield, "charges", 3);
    public static int SkateboardMove => Item(ItemKind.Skateboard, "move", 2);
    public static int IceSkatesIceMove => Item(ItemKind.IceSkates, "ice_move", 2);
    public static int MotorcycleMove => Item(ItemKind.Motorcycle, "move", 3);
    public static int MotorcycleRamDamage => Item(ItemKind.Motorcycle, "ram_damage", 10);
    public static int MotorcycleRamMin => Item(ItemKind.Motorcycle, "ram_min", 6);
    public static int MotorcycleRamMax => Item(ItemKind.Motorcycle, "ram_max", 10);
    public static int MotorcycleRamWidth => Item(ItemKind.Motorcycle, "ram_width", 3);
    public static int MotorcycleDuration => Item(ItemKind.Motorcycle, "duration", 3);
    public static int NightVisionClear => Item(ItemKind.NightVision, "clear", 3);
    public static int NightVisionRain => Item(ItemKind.NightVision, "rain", 2);
    public static int NightVisionFog => Item(ItemKind.NightVision, "fog", 1);
    public static int TelescopeFog => Item(ItemKind.Telescope, "fog", 2);
    public static int GrappleHookRange => Item(ItemKind.GrappleHook, "range", 3);
    public static int AmmoPerCard => GetInt("items", "ammo_per_card", 1);
    public static int ShootAmmoCost => GetInt("items", "shoot_ammo_cost", 1);
    public static int FuelCost => GetInt("items", "fuel_cost", 1);
    public static int DollElephantDef => Item(ItemKind.DollElephant, "def", 3);
    public static int DollHumanAtk => Item(ItemKind.DollHuman, "atk", 3);
    public static int DollHumanBag => Item(ItemKind.DollHuman, "bag", 3);
    public static int DollMonkeyMove => Item(ItemKind.DollMonkey, "move", 1);
    public static int DollMonkeyAtk => Item(ItemKind.DollMonkey, "atk", 3);
    public static int DollCatMove => Item(ItemKind.DollCat, "move", 2);
    public static int SkillUpgradeCards => Item(ItemKind.SkillUpgrade, "cards", 3);
    public static int SkillLevelBase => Item(ItemKind.SkillUpgrade, "level_base", 1);
    public static int SkillLevelMax => Item(ItemKind.SkillUpgrade, "level_max", 3);
    public static int ReinforceAmount => Item(ItemKind.Reinforce, "amount", 1);
    public static int RedBullExtraActions => Item(ItemKind.RedBull, "extra_actions", 1);
    public static float ItemWeight => GetFloat("items", "weight_per_card", 1f);
    public static int ArmorDurabilityLoss => Mathf.Max(1, GetInt("items", "armor_durability_loss", 1));
    public static int AmuletShieldCharges => Mathf.Max(1, GetInt("items", "amulet_shield_charges", 1));

    /// <summary>按 ItemKind 蛇形名读取 items 段下 kind_field 的整数值。</summary>
    public static int Item(ItemKind kind, string field, int fallback)
        => GetInt("items", ItemKindToYamlKey(kind) + "_" + field, fallback);

    private static int LevelFallback(int level, int lv1, int lv2, int lv3)
    {
        switch (level)
        {
            case 3: return lv3;
            case 2: return lv2;
            default: return lv1;
        }
    }

    public static int GetInt(string section, string key, int fallback)
    {
        EnsureLoaded();
        if (!sections.TryGetValue(section, out var map))
            return fallback;
        if (!map.TryGetValue(key, out string raw))
            return fallback;
        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v))
            return v;
        if (float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float f))
            return Mathf.RoundToInt(f);
        return fallback;
    }

    public static float GetFloat(string section, string key, float fallback)
    {
        EnsureLoaded();
        if (!sections.TryGetValue(section, out var map))
            return fallback;
        if (!map.TryGetValue(key, out string raw))
            return fallback;
        if (float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
            return v;
        return fallback;
    }

    public static string ResolveRulesPath()
    {
        string streaming = Path.Combine(Application.streamingAssetsPath, "Config", "game_rules.yaml");
        if (File.Exists(streaming))
            return streaming;

#if UNITY_EDITOR
        string project = Path.GetFullPath(Path.Combine(Application.dataPath, "StreamingAssets", "Config", "game_rules.yaml"));
        if (File.Exists(project))
            return project;
#endif
        return streaming;
    }

    public static string ToSnakeCase(string pascal)
    {
        if (string.IsNullOrEmpty(pascal))
            return pascal;
        var sb = new StringBuilder(pascal.Length + 8);
        for (int i = 0; i < pascal.Length; i++)
        {
            char c = pascal[i];
            if (char.IsUpper(c) && i > 0)
                sb.Append('_');
            sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }

    public static string ItemKindToYamlKey(ItemKind kind) => ToSnakeCase(kind.ToString());

    private static void ParseDocument(string text)
    {
        sections.Clear();
        if (string.IsNullOrEmpty(text))
            return;

        string section = null;
        int sectionIndent = -1;
        // nested block under a section (e.g. clock.visibility) — store as flat keys with parent prefix skipped;
        // we only keep direct children of top-level sections for scalars.
        string nested = null;
        int nestedIndent = -1;

        using (var reader = new StringReader(text))
        {
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                if (line.TrimStart().StartsWith("#"))
                    continue;
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                int indent = CountIndent(line);
                string trimmed = line.Trim();

                // Top-level section header: "deck:"
                if (indent == 0 && trimmed.EndsWith(":") && !trimmed.Contains(" "))
                {
                    section = trimmed.Substring(0, trimmed.Length - 1).Trim();
                    sectionIndent = 0;
                    nested = null;
                    nestedIndent = -1;
                    if (!sections.ContainsKey(section))
                        sections[section] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    continue;
                }

                if (section == null)
                    continue;

                // Leaving section
                if (indent == 0)
                {
                    section = null;
                    nested = null;
                    continue;
                }

                // roles inline map
                if (string.Equals(section, "roles", StringComparison.OrdinalIgnoreCase))
                {
                    var m = InlineRole.Match(line);
                    if (m.Success)
                    {
                        // Applied later in ApplyParsedRoles via dedicated parse — also stash raw for completeness
                        EnsureSection("roles")[m.Groups[1].Value] = "inline";
                    }
                    continue;
                }

                // Nested map header under section: e.g. "visibility:"
                if (indent > sectionIndent
                    && trimmed.EndsWith(":")
                    && !trimmed.Contains("{")
                    && trimmed.IndexOf(' ') < 0)
                {
                    nested = trimmed.Substring(0, trimmed.Length - 1);
                    nestedIndent = indent;
                    continue;
                }

                if (nested != null && indent <= nestedIndent)
                    nested = null;

                // Skip deeper nesting content for now (spawn_cells lists etc.)
                if (nested != null)
                {
                    var nestMatch = ScalarLine.Match(trimmed);
                    if (nestMatch.Success)
                    {
                        string nkey = nestMatch.Groups[1].Value;
                        string nval = StripComment(nestMatch.Groups[2].Value);
                        EnsureSection(section)[$"{nested}.{nkey}"] = nval;
                    }
                    continue;
                }

                var sm = ScalarLine.Match(trimmed);
                if (!sm.Success)
                    continue;
                string key = sm.Groups[1].Value;
                string val = StripComment(sm.Groups[2].Value);
                if (val.StartsWith("{") || val.StartsWith("["))
                    continue; // complex / list — ignore here
                EnsureSection(section)[key] = val;
            }
        }

        // Second pass for roles (keep existing robust parser)
        if (TryParseRoles(text, out var parsedRoles))
        {
            foreach (var kv in parsedRoles)
                roleStats[kv.Key] = kv.Value;
        }

        if (TryParseSpawnCells(text, out var parsedSpawns))
        {
            foreach (var kv in parsedSpawns)
                spawnCells[kv.Key] = kv.Value;
        }
    }

    private static Dictionary<string, string> EnsureSection(string name)
    {
        if (!sections.TryGetValue(name, out var map))
        {
            map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            sections[name] = map;
        }
        return map;
    }

    private static void ApplyParsedRoles()
    {
        // roleStats already filled in ParseDocument via TryParseRoles; ensure fallbacks for missing
        foreach (RoleType r in Enum.GetValues(typeof(RoleType)))
        {
            if (!roleStats.ContainsKey(r))
                roleStats[r] = FallbackRole(r);
        }
    }

    private static void ApplyParsedDeck()
    {
        if (!sections.TryGetValue("deck", out var map) || map.Count == 0)
            return;

        // YAML 有 deck 段时：以表为准（未列出的卡 = 0），覆盖 fallback
        deckCounts.Clear();
        foreach (ItemKind kind in Enum.GetValues(typeof(ItemKind)))
        {
            string key = ItemKindToYamlKey(kind);
            if (map.TryGetValue(key, out string raw)
                && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n))
            {
                deckCounts[kind] = Mathf.Max(0, n);
            }
            else
            {
                deckCounts[kind] = 0;
            }
        }
    }

    private static bool TryParseRoles(string text, out Dictionary<RoleType, RoleBaseStats> result)
    {
        result = new Dictionary<RoleType, RoleBaseStats>();
        if (string.IsNullOrEmpty(text))
            return false;

        bool inRoles = false;
        using (var reader = new StringReader(text))
        {
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith("#"))
                    continue;

                if (!inRoles)
                {
                    if (trimmed == "roles:" || trimmed.StartsWith("roles:"))
                        inRoles = true;
                    continue;
                }

                if (trimmed.Length > 0 && !char.IsWhiteSpace(line, 0) && !trimmed.StartsWith("roles"))
                {
                    if (trimmed.EndsWith(":") && !trimmed.Contains("{"))
                        break;
                }

                var m = InlineRole.Match(line);
                if (!m.Success)
                    continue;

                RoleType role = ParseRole(m.Groups[1].Value);
                result[role] = new RoleBaseStats
                {
                    Move = int.Parse(m.Groups[2].Value),
                    Hp = int.Parse(m.Groups[3].Value),
                    Atk = int.Parse(m.Groups[4].Value),
                    Def = int.Parse(m.Groups[5].Value),
                    Bag = int.Parse(m.Groups[6].Value),
                    MagicResist = m.Groups.Count > 8 && m.Groups[8].Success
                        ? int.Parse(m.Groups[8].Value)
                        : FallbackRole(role).MagicResist,
                };
            }
        }

        return result.Count > 0;
    }

    private static RoleType ParseRole(string key)
    {
        switch (key.ToLowerInvariant())
        {
            case "elephant": return RoleType.Elephant;
            case "human": return RoleType.Human;
            case "monkey": return RoleType.Monkey;
            case "cat": return RoleType.Cat;
            default: return RoleType.Human;
        }
    }

    private static bool TryParseSpawnCells(string text, out Dictionary<RoleType, Vector2Int> result)
    {
        result = new Dictionary<RoleType, Vector2Int>();
        if (string.IsNullOrEmpty(text))
            return false;

        bool inSpawn = false;
        using (var reader = new StringReader(text))
        {
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith("#"))
                    continue;

                if (!inSpawn)
                {
                    if (trimmed == "spawn_cells:" || trimmed.StartsWith("spawn_cells:"))
                        inSpawn = true;
                    continue;
                }

                // 退出：回到顶层（顶格的非 spawn_cells 行）
                if (trimmed.Length > 0 && !char.IsWhiteSpace(line, 0) && !trimmed.StartsWith("spawn_cells"))
                    break;

                var m = SpawnCellLine.Match(line);
                if (!m.Success)
                    continue;
                result[ParseRole(m.Groups[1].Value)] = new Vector2Int(
                    int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value));
            }
        }
        return result.Count > 0;
    }

    private static void ApplyFallbacks()
    {
        roleStats[RoleType.Elephant] = FallbackRole(RoleType.Elephant);
        roleStats[RoleType.Human] = FallbackRole(RoleType.Human);
        roleStats[RoleType.Monkey] = FallbackRole(RoleType.Monkey);
        roleStats[RoleType.Cat] = FallbackRole(RoleType.Cat);

        spawnCells[RoleType.Elephant] = FallbackSpawnCell(RoleType.Elephant);
        spawnCells[RoleType.Human] = FallbackSpawnCell(RoleType.Human);
        spawnCells[RoleType.Monkey] = FallbackSpawnCell(RoleType.Monkey);
        spawnCells[RoleType.Cat] = FallbackSpawnCell(RoleType.Cat);

        deckCounts.Clear();
        foreach (var kv in FallbackDeck())
            deckCounts[kv.Key] = kv.Value;
    }

    private static Vector2Int FallbackSpawnCell(RoleType role)
    {
        switch (role)
        {
            case RoleType.Elephant: return new Vector2Int(2, 2);
            case RoleType.Human: return new Vector2Int(15, 2);
            case RoleType.Monkey: return new Vector2Int(2, 15);
            case RoleType.Cat: return new Vector2Int(15, 15);
            default: return new Vector2Int(2, 2);
        }
    }

    private static RoleBaseStats FallbackRole(RoleType role)
    {
        switch (role)
        {
            case RoleType.Elephant:
                return new RoleBaseStats { Move = 3, Hp = 100, Atk = 9, Def = 10, Bag = 30, MagicResist = 0 };
            case RoleType.Human:
                return new RoleBaseStats { Move = 5, Hp = 90, Atk = 10, Def = 8, Bag = 48, MagicResist = 20 };
            case RoleType.Monkey:
                return new RoleBaseStats { Move = 6, Hp = 90, Atk = 8, Def = 6, Bag = 36, MagicResist = 20 };
            case RoleType.Cat:
                return new RoleBaseStats { Move = 8, Hp = 60, Atk = 6, Def = 4, Bag = 30, MagicResist = 50 };
            default:
                return new RoleBaseStats { Move = 4, Hp = 90, Atk = 9, Def = 8, Bag = 30, MagicResist = 20 };
        }
    }

    /// <summary>与改表前 DeckManager.BuildDemoDeck 硬编码一致，供缺文件时回退。</summary>
    private static IEnumerable<KeyValuePair<ItemKind, int>> FallbackDeck()
    {
        yield return new KeyValuePair<ItemKind, int>(ItemKind.SmallPotion, 10);
        yield return new KeyValuePair<ItemKind, int>(ItemKind.LargePotion, 4);
        yield return new KeyValuePair<ItemKind, int>(ItemKind.Bomb, 10);
        yield return new KeyValuePair<ItemKind, int>(ItemKind.MegaBomb, 4);
        yield return new KeyValuePair<ItemKind, int>(ItemKind.TimedBomb, 6);
        yield return new KeyValuePair<ItemKind, int>(ItemKind.Reinforce, 10);
        yield return new KeyValuePair<ItemKind, int>(ItemKind.Bow, 6);
        yield return new KeyValuePair<ItemKind, int>(ItemKind.Crossbow, 2);
        yield return new KeyValuePair<ItemKind, int>(ItemKind.Dagger, 6);
        yield return new KeyValuePair<ItemKind, int>(ItemKind.Longsword, 4);
        yield return new KeyValuePair<ItemKind, int>(ItemKind.ArmorPiercingBlade, 8);
        yield return new KeyValuePair<ItemKind, int>(ItemKind.CursedBlade, 2);
        yield return new KeyValuePair<ItemKind, int>(ItemKind.Arrow, 30);
        yield return new KeyValuePair<ItemKind, int>(ItemKind.PoisonArrow, 12);
        yield return new KeyValuePair<ItemKind, int>(ItemKind.FireRocket, 12);
        yield return new KeyValuePair<ItemKind, int>(ItemKind.BananaPeel, 10);
        yield return new KeyValuePair<ItemKind, int>(ItemKind.Mine, 10);
        yield return new KeyValuePair<ItemKind, int>(ItemKind.Flamethrower, 2);
        yield return new KeyValuePair<ItemKind, int>(ItemKind.WoodArmor, 6);
        yield return new KeyValuePair<ItemKind, int>(ItemKind.IronArmor, 2);
        yield return new KeyValuePair<ItemKind, int>(ItemKind.RubberRaincoat, 6);
        yield return new KeyValuePair<ItemKind, int>(ItemKind.ThornsArmor, 6);
        yield return new KeyValuePair<ItemKind, int>(ItemKind.TacticalVest, 6);
        yield return new KeyValuePair<ItemKind, int>(ItemKind.EnergyShield, 6);
        yield return new KeyValuePair<ItemKind, int>(ItemKind.Adrenaline, 10);
        yield return new KeyValuePair<ItemKind, int>(ItemKind.NightVision, 4);
        yield return new KeyValuePair<ItemKind, int>(ItemKind.Telescope, 4);
        yield return new KeyValuePair<ItemKind, int>(ItemKind.Skateboard, 6);
        yield return new KeyValuePair<ItemKind, int>(ItemKind.Motorcycle, 2);
        yield return new KeyValuePair<ItemKind, int>(ItemKind.IceSkates, 4);
        yield return new KeyValuePair<ItemKind, int>(ItemKind.GrappleHook, 6);
        yield return new KeyValuePair<ItemKind, int>(ItemKind.Amulet, 2);
        yield return new KeyValuePair<ItemKind, int>(ItemKind.Flashbang, 6);
        yield return new KeyValuePair<ItemKind, int>(ItemKind.Boomerang, 6);
        yield return new KeyValuePair<ItemKind, int>(ItemKind.Milk, 8);
        yield return new KeyValuePair<ItemKind, int>(ItemKind.WeatherClear, 6);
        yield return new KeyValuePair<ItemKind, int>(ItemKind.WeatherRain, 6);
        yield return new KeyValuePair<ItemKind, int>(ItemKind.WeatherFog, 6);
        yield return new KeyValuePair<ItemKind, int>(ItemKind.RedBull, 12);
        yield return new KeyValuePair<ItemKind, int>(ItemKind.GasolineBottle, 20);
        yield return new KeyValuePair<ItemKind, int>(ItemKind.Lighter, 8);
        yield return new KeyValuePair<ItemKind, int>(ItemKind.SkillUpgrade, 24);
        yield return new KeyValuePair<ItemKind, int>(ItemKind.DollElephant, 1);
        yield return new KeyValuePair<ItemKind, int>(ItemKind.DollHuman, 1);
        yield return new KeyValuePair<ItemKind, int>(ItemKind.DollMonkey, 1);
        yield return new KeyValuePair<ItemKind, int>(ItemKind.DollCat, 1);
    }

    private static int CountIndent(string line)
    {
        int n = 0;
        while (n < line.Length && (line[n] == ' ' || line[n] == '\t'))
            n++;
        return n;
    }

    private static string StripComment(string raw)
    {
        if (string.IsNullOrEmpty(raw))
            return raw;
        int hash = raw.IndexOf('#');
        if (hash >= 0)
            raw = raw.Substring(0, hash);
        return raw.Trim().Trim('"').Trim('\'');
    }
}
