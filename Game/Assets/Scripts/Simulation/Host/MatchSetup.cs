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

    /// <summary>
    /// 丢弃所有系统单例，让下一局从零开始（"每局一世界"）。
    ///
    /// 必须在**每局开始前**调用：只 reset 不丢弃会让上一局的残留影响下一局，
    /// 同一种子在"单跑"和"批跑第 N 局"里结果就会不同。两个宿主都走这里。
    /// </summary>
    public static void ResetAll()
    {
        GridManager.Instance = null;
        TurnManager.Instance = null;
        GameManager.Instance = null;
        GroundItemManager.Instance = null;
        HazardManager.Instance = null;
        DeckManager.Instance = null;

        InventoryItem.ResetIdCounter();
        // 天气不在这里重置：TurnManager.Setup 会做，且它会消耗随机数，
        // 这里多做一次就会让整条随机流错位（同种子跑出不同对局）。
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
