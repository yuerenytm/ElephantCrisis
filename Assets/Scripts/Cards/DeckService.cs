using UnityEngine;

// 兼容旧调用；实际逻辑在 DeckManager
public static class DeckService
{
    public static void DrawFor(UnitActor unit)
    {
        DeckManager.Instance?.DrawFor(unit);
    }
}
