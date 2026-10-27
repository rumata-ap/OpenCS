using System.Globalization;

namespace CScore.Fem.Fragment;

/// <summary>
/// КЭ фрагмента глазами источника родителя (спека разд. 5): номер и тег КЭ родителя, его ТЗА и жёсткость, толщина —
/// своя, иначе КЭ родителя; КонЭ — фрагмента. Источники ЛИРЫ/SCAD ищут армирование по этим данным.
/// </summary>
public static class ParentMappedElement
{
    /// <summary>Причина «нет армирования» у КЭ фрагмента без пары в опорной сетке.</summary>
    public const string UnmappedReason = "КЭ фрагмента без пары в опорной сетке родителя";

    /// <summary>Элемент цели с данными КЭ родителя; null — КЭ фрагмента без пары.</summary>
    public static FemCheckScopeElement? Map(FemCheckScopeElement element, FragmentParentMap map)
    {
        if (!map.ByTag.TryGetValue(element.Element.ElemTag, out var r)) return null;
        var proxy = element.Element.ShallowCopy();
        proxy.ElemTag = r.Tag;
        proxy.ReinforcementTypeIds = r.ReinforcementTypeIds;
        proxy.StiffnessNum = r.StiffnessNum ?? proxy.StiffnessNum;
        proxy.ThicknessM = proxy.ThicknessM ?? r.ThicknessM;
        int? num = int.TryParse(r.Tag, NumberStyles.None, CultureInfo.InvariantCulture, out int n) ? n : null;
        return new FemCheckScopeElement(num, proxy, element.Member);
    }

    /// <summary>Сопоставленные элементы цели (без пары — пропускаются).</summary>
    public static IReadOnlyList<FemCheckScopeElement> MapAll(IEnumerable<FemCheckScopeElement> elements, FragmentParentMap map) =>
        elements.Select(e => Map(e, map)).OfType<FemCheckScopeElement>().ToList();
}

/// <summary>
/// Источник армирования пластин родителя (ЛИРА, SCAD) для КЭ фрагмента: КЭ подменяется КЭ родителя по опорной сетке
/// (<see cref="ParentMappedElement"/>), интерфейс источника не меняется.
/// </summary>
public sealed class ParentMappedPlateSectionSource(IPlateElementSectionSource inner, FragmentParentMap map) : IPlateElementSectionSource
{
    readonly PlateElementSection _unmapped = PlateElementSection.Missing(ParentMappedElement.UnmappedReason);

    /// <inheritdoc/>
    public string Key => inner.Key;

    /// <inheritdoc/>
    public PlateElementSection Resolve(FemCheckScopeElement element) =>
        ParentMappedElement.Map(element, map) is { } parent ? inner.Resolve(parent) : _unmapped;

    /// <inheritdoc/>
    public IReadOnlyList<string> Warnings(IReadOnlyList<FemCheckScopeElement> elements, Material? concrete, Material? rebar) =>
        inner.Warnings(ParentMappedElement.MapAll(elements, map), concrete, rebar);
}

/// <summary>
/// Источник сечения стержней родителя (ЛИРА, SCAD) для КЭ фрагмента: КЭ подменяется КЭ родителя по опорной сетке.
/// Стержень фрагмента — часть КЭ родителя, номера сечений вдоль КЭ не совпадают: сечение — по КЭ родителя в целом
/// (<c>sectionNum</c> не передаётся, <see cref="PerSection"/> = false).
/// </summary>
public sealed class ParentMappedBarSectionSource(IBarElementSectionSource inner, FragmentParentMap map) : IBarElementSectionSource
{
    /// <inheritdoc/>
    public string Key => inner.Key;

    /// <inheritdoc/>
    public bool PerSection => false;

    /// <inheritdoc/>
    public string? MissingReason(FemCheckScopeElement element) =>
        ParentMappedElement.Map(element, map) is { } parent ? inner.MissingReason(parent) : ParentMappedElement.UnmappedReason;

    /// <inheritdoc/>
    public BarElementSection Resolve(FemCheckScopeElement element, int? sectionNum) =>
        ParentMappedElement.Map(element, map) is { } parent
            ? inner.Resolve(parent, null)
            : BarElementSection.Missing(ParentMappedElement.UnmappedReason);
}
