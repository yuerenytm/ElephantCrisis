using System.Collections.Generic;

/// <summary>
/// 一局的装配序列：重建系统 → 生成地图 → 造单位 → 造牌库 → 开局快照 → 开始回合。
///
/// 为什么放在核心：Unity 宿主与 dotnet 跑批器必须用**同一段顺序**，
/// 否则随机数的消耗点会错位，同样的种子会跑出不同的对局
/// （实测：手写两份时 100 局里 98 局不同）。
/// </summary>
public static class MatchSetup
{
    /// <summary>
    /// 装配一局并开始回合。调用前需已完成 Rng 播种。
    /// 返回四个单位（顺序固定：象 / 人 / 猴 / 猫）。
    /// </summary>
    public static List<UnitActor> AssembleLean()
    {
        EnsureSystems();

        var grid = GridManager.Instance;
        grid.InitTiles();
        grid.ClearAllOccupants();

        var units = SpawnUnits();

        GameManager.Instance.BeginMatch();
        DeckManager.Instance.BuildDemoDeck();
        DeckManager.Instance.ScatterDrawPileOntoMap();

        // 开局快照必须在 Setup 之前：Setup 会立刻开始第一回合
        LogicMatchLogger.Active?.EmitSnapshot();

        TurnManager.Instance.Setup(units);
        return units;
    }

    /// <summary>按需创建核心系统（已存在则复用）。</summary>
    public static void EnsureSystems()
    {
        if (GridManager.Instance == null)
            GridManager.Create();
        if (GameManager.Instance == null)
            GameManager.Instance = new GameManager();
        if (TurnManager.Instance == null)
            TurnManager.Instance = new TurnManager();
        if (GroundItemManager.Instance == null)
            GroundItemManager.Instance = new GroundItemManager();
        if (HazardManager.Instance == null)
            HazardManager.Instance = new HazardManager();
        if (DeckManager.Instance == null)
            DeckManager.Instance = new DeckManager();
    }

    /// <summary>四个单位按固定顺序出生。</summary>
    public static List<UnitActor> SpawnUnits()
    {
        var roles = new[] { RoleType.Elephant, RoleType.Human, RoleType.Monkey, RoleType.Cat };
        var units = new List<UnitActor>(roles.Length);
        foreach (var role in roles)
        {
            var unit = new UnitActor();
            unit.Setup(role, GameRulesConfig.SpawnCell(role));
            units.Add(unit);
        }
        return units;
    }
}
