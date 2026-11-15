using UnityEngine;

/// <summary>
/// 表现层出口：逻辑层只通过这里要画面，不直接碰 MapVisual / GameUI / 输入控制器。
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

    /// <summary>隐藏主菜单（开始对局时）。</summary>
    void HideMenu();
}

/// <summary>
/// 表现层门面的持有者。装配点（<see cref="GameBootstrap"/>）决定用真实实现还是空实现，
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
        public void HideMenu() { }
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
