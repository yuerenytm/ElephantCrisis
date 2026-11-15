using System;

/// <summary>
/// 日志出口：逻辑层只调这里，由宿主决定写到哪。
/// 默认全部丢弃，因此核心库在没有 Unity 的环境里也能安静运行。
/// </summary>
public static class Log
{
    /// <summary>普通信息。</summary>
    public static Action<string> Info = _ => { };

    /// <summary>警告。</summary>
    public static Action<string> Warning = _ => { };

    /// <summary>错误。</summary>
    public static Action<string> Error = _ => { };
}
