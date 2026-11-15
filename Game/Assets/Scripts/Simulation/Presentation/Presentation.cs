using UnityEngine;

/// <summary>
/// 表现层出口：逻辑层只通过这里要画面，不直接操作任何具体表现类。
///
/// 无头模式（LogicSim）下装配点不注入实现，<see cref="Presentation.Current"/> 保持空实现，
/// 于是所有调用都变成空操作——**调用点不需要写任何 if**。
/// </summary>
public interface IPresentation
{
    /// <summary>按视野角色刷新迷雾与氛围。</summary>
    void ApplyFog(UnitActor viewer);

    /// <summary>重绘地图（熔岩收缩后）。</summary>
    void RefreshTiles();

    /// <summary>刷新输入提示（射程 / 可攻击目标高亮）。</summary>
    void RefreshHints();

    /// <summary>请求刷新 HUD。</summary>
    void RequestHud();

    /// <summary>进入"选格子 / 选单位 / 选方向"模式；具体模式按物品类型分派。</summary>
    void BeginItemTargeting(int itemIndex, ItemKind kind);

    /// <summary>进入射击模式并指定弹药。</summary>
    void BeginShootWithAmmo(int weaponIndex, ItemKind ammo);

    /// <summary>开始一局对局（菜单 / 流程控制用）。</summary>
    void StartMatch(GameMode mode, RoleType human);

    /// <summary>重开一局（表现层负责销毁重建）。</summary>
    void RestartMatch();

    /// <summary>回到主菜单。</summary>
    void ReturnToMenu();

    /// <summary>把战报镜射到控制台（无头批跑时抑制，避免刷出巨量堆栈）。</summary>
    void LogConsole(string message);

    /// <summary>隐藏主菜单（开始对局时）。</summary>
    void HideMenu();

    /// <summary>给掉落物对象挂上精灵与广告牌。</summary>
    void StyleLoot(GameObject go, Color fill, Color border, Vector2Int cell, bool emphasize);

    /// <summary>创建火焰地块的视觉对象。</summary>
    GameObject BuildFlameVisual(Vector2Int cell, Transform parent);

    /// <summary>创建红色标记（地雷 / 定时炸弹）。</summary>
    GameObject BuildRedMarker(Vector2Int cell, Transform parent, string name);

    /// <summary>构建角色模型。</summary>
    GameObject BuildRoleModel(RoleType role, Transform parent);

    /// <summary>构建选中环。</summary>
    GameObject BuildSelectRing(Transform parent);

    /// <summary>给定时炸弹挂倒计时标签（纯表现）。</summary>
    void BuildBombLabel(GameObject marker, int actions);

    /// <summary>移除对象上的广告牌组件（棋子要直立）。</summary>
    void RemoveBillboard(GameObject go);
}

/// <summary>
/// 表现层门面的持有者。装配点（有头模式的启动代码）决定用真实实现还是空实现，
/// 逻辑层只认 <see cref="Current"/>。
/// </summary>
public static class Presentation
{
    /// <summary>空实现：所有方法什么都不做。</summary>
    private sealed class NullPresentation : IPresentation
    {
        public void ApplyFog(UnitActor viewer) { }
        public void RefreshTiles() { }
        public void RefreshHints() { }
        public void RequestHud() { }
        public void BeginItemTargeting(int itemIndex, ItemKind kind) { }
        public void BeginShootWithAmmo(int weaponIndex, ItemKind ammo) { }
        public void StartMatch(GameMode mode, RoleType human) { }
        public void RestartMatch() { }
        public void ReturnToMenu() { }
        public void LogConsole(string message) { }
        public void HideMenu() { }
        public void StyleLoot(GameObject go, Color fill, Color border, Vector2Int cell, bool emphasize) { }
        public GameObject BuildFlameVisual(Vector2Int cell, Transform parent) => null;
        public GameObject BuildRedMarker(Vector2Int cell, Transform parent, string name) => null;
        public GameObject BuildRoleModel(RoleType role, Transform parent) => null;
        public GameObject BuildSelectRing(Transform parent) => null;
        public void BuildBombLabel(GameObject marker, int actions) { }
        public void RemoveBillboard(GameObject go) { }
    }

    private static IPresentation current = new NullPresentation();

    /// <summary>当前表现实现；未注入时为空实现。</summary>
    public static IPresentation Current => current;

    /// <summary>是否已注入真实实现（= 有画面）。</summary>
    public static bool Enabled { get; private set; }

    /// <summary>装配真实表现（有头模式调用一次）。</summary>
    public static void Wire(IPresentation implementation)
    {
        current = implementation ?? new NullPresentation();
        Enabled = implementation != null;
    }

    /// <summary>退回空实现（无头模式 / 拆场景时调用）。</summary>
    public static void Clear()
    {
        current = new NullPresentation();
        Enabled = false;
    }
}
