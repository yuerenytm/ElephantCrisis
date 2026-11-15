
/// <summary>掉落物的外观风格：具体颜色由表现层决定，逻辑层只说"这是哪一类"。</summary>
public enum LootStyle
{
    /// <summary>开局散落的背面牌。</summary>
    Scatter,
    /// <summary>正面朝上的玩偶（金色强调）。</summary>
    Doll,
    /// <summary>弃置 / 濒死掉落的普通牌。</summary>
    Drop
}

/// <summary>
/// 表现层出口：逻辑层只通过这里要画面，不直接操作任何具体表现类。
///
/// 无头模式（LogicSim）下装配点不注入实现，<see cref="Presentation.Current"/> 保持空实现，
/// 于是所有调用都变成空操作——调用点不需要写任何 if。
///
/// 视觉对象一律用 <c>int</c> 句柄表达：逻辑层不认识 GameObject / Transform，
/// 这样 Simulation 程序集可以脱离 Unity 编译。
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

    /// <summary>把战报镜射到控制台（无头批跑时抑制，避免刷出巨量堆栈）。</summary>
    void LogConsole(string message);

    /// <summary>进入"选格子 / 选单位 / 选方向"模式；具体模式按物品类型分派。</summary>
    void BeginItemTargeting(int itemIndex, ItemKind kind);

    /// <summary>进入射击模式并指定弹药。</summary>
    void BeginShootWithAmmo(int weaponIndex, ItemKind ammo);

    /// <summary>隐藏主菜单（开始对局时）。</summary>
    void HideMenu();

    /// <summary>开始一局对局（菜单 / 流程控制用）。</summary>
    void StartMatch(GameMode mode, RoleType human);

    /// <summary>重开一局。</summary>
    void RestartMatch();

    /// <summary>回到主菜单。</summary>
    void ReturnToMenu();

    // ---------------------------------------------------------- 视觉句柄

    /// <summary>创建火焰视觉，返回句柄（0 = 无）。</summary>
    int CreateFlameVisual(Int2 cell);

    /// <summary>创建标记视觉（地雷 / 定时炸弹 / 香蕉皮），返回句柄。</summary>
    int CreateMarkerVisual(Int2 cell, string kind);

    /// <summary>创建掉落物视觉，返回句柄。</summary>
    int CreateLootVisual(Int2 cell, LootStyle style);

    /// <summary>按句柄显示 / 隐藏。</summary>
    void SetVisualActive(int handle, bool active);

    /// <summary>更新定时炸弹的倒计时数字。</summary>
    void UpdateMarkerLabel(int handle, int actions);

    /// <summary>销毁某个视觉。</summary>
    void DestroyVisual(int handle);
}

/// <summary>
/// 表现层门面的持有者。装配点（有头模式的启动代码）决定用真实实现还是空实现，
/// 逻辑层只认 <see cref="Current"/>。
/// </summary>
public static class Presentation
{
    /// <summary>空实现：所有方法什么都不做，句柄一律返回 0。</summary>
    private sealed class NullPresentation : IPresentation
    {
        public void ApplyFog(UnitActor viewer) { }
        public void RefreshTiles() { }
        public void RefreshHints() { }
        public void RequestHud() { }
        public void LogConsole(string message) { }
        public void BeginItemTargeting(int itemIndex, ItemKind kind) { }
        public void BeginShootWithAmmo(int weaponIndex, ItemKind ammo) { }
        public void HideMenu() { }
        public void StartMatch(GameMode mode, RoleType human) { }
        public void RestartMatch() { }
        public void ReturnToMenu() { }
        public int CreateFlameVisual(Int2 cell) => 0;
        public int CreateMarkerVisual(Int2 cell, string kind) => 0;
        public int CreateLootVisual(Int2 cell, LootStyle style) => 0;
        public void SetVisualActive(int handle, bool active) { }
        public void UpdateMarkerLabel(int handle, int actions) { }
        public void DestroyVisual(int handle) { }
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
