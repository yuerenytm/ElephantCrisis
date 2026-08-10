/// <summary>
/// 轻量埋点入口：会话未开时 no-op，业务代码可安全调用。
/// </summary>
public static class ClientPerfMark
{
    public static void Action(string kind, string detail = null)
    {
        var s = ClientPerfSession.Instance;
        if (s == null || !s.IsActive)
            return;
        s.EmitGameAction(kind, detail);
    }
}
