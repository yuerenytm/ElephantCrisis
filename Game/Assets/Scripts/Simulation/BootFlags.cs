/// <summary>
/// 启动方式标记：由命令行入口在启动时设置一次。
/// 放在 Simulation 里是为了让装配点（App）不必认识具体工具类。
/// </summary>
public static class BootFlags
{
    /// <summary>本次进程是命令行无头跑批（-logicSim）。</summary>
    public static bool HeadlessRequested;
}
