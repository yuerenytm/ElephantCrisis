using UnityEngine;

/// <summary>
/// 单位在场景里的"外壳"：持有纯逻辑的 <see cref="UnitActor"/> 与外皮 <see cref="UnitView"/>。
///
/// 为什么要它：UnitActor 要变成不依赖 Unity 的普通类，就不能自己挂在 GameObject 上；
/// 场景对象这一侧的责任全部由本类承担（位置、外皮、显隐）。
/// </summary>
public sealed class UnitHost : MonoBehaviour
{
    private static readonly System.Collections.Generic.Dictionary<UnitActor, UnitHost> Registry
        = new System.Collections.Generic.Dictionary<UnitActor, UnitHost>();

    /// <summary>被承载的逻辑单位。</summary>
    public UnitActor Unit { get; private set; }

    /// <summary>按逻辑单位取场景外壳；无头模式或已销毁时为 null。</summary>
    public static UnitHost Of(UnitActor unit)
        => unit != null && Registry.TryGetValue(unit, out var host) ? host : null;

    private void OnDestroy()
    {
        if (Unit != null)
            Registry.Remove(Unit);
    }

    /// <summary>绑定逻辑单位并挂上外皮（有头模式调用一次）。</summary>
    public void Bind(UnitActor unit)
    {
        Unit = unit;
        Registry[unit] = this;
        var view = gameObject.AddComponent<UnitView>();
        view.Attach(unit);
        unit.AttachView(view);

        // Setup 期间还没有外皮，位置与着色要在这里补一次
        view.PlaceAt(unit, unit.Cell);
        unit.RefreshVisual();
    }
}
