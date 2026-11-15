
/// <summary>
/// 单位的"外皮"：由表现层实现。逻辑层只持有它，不知道里面是模型还是精灵。
/// 无头模式下没有实现，所有调用自动空转。
/// </summary>
public interface IUnitView
{
    /// <summary>建模型 / 选中环 / 名字牌。</summary>
    void Attach(UnitActor unit);

    /// <summary>刷颜色（濒死偏红）、朝向、层级、可见性。</summary>
    void Refresh(UnitActor unit);

    /// <summary>跟随格子更新位置与排序。</summary>
    void PlaceAt(UnitActor unit, Int2 cell);

    /// <summary>选中高亮。</summary>
    void SetSelected(UnitActor unit, bool selected);

    /// <summary>迷雾：视野外隐藏渲染（逻辑仍在场上）。</summary>
    void SetWorldVisible(UnitActor unit, bool visible);
}
