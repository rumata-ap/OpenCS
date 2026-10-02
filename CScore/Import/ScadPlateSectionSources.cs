using System.Globalization;
using CScore.Fem;

namespace CScore.Import;

/// <summary>
/// Источник <see cref="FemCheckRebarSource.Selected"/>: сечение пластинчатого КЭ из подобранной SCAD арматуры
/// (выгрузка плагина «Экспорт для OpenCS»). Площади AS1..AS4 — из выгрузки; привязки — из ЖБ-группы КЭ
/// (a1..a4 — как S1..S4: низ X, верх X, низ Y, верх Y; a3/a4 = 0 — берутся a1/a2), у КЭ без группы —
/// от слоёв шаблона той же грани. Толщина: своя толщина КЭ → толщина шаблона. Диаметры (для трещин) —
/// от слоёв шаблона той же грани.
/// </summary>
public sealed class ScadSelectedPlateSectionSource : IPlateElementSectionSource
{
    const string Label = "SCAD";

    readonly PlateElementSectionFactory _factory;
    readonly ScadSelectedRebarFile _file;
    readonly ScadConcreteGroupIndex? _groups;

    /// <param name="template">Сечение-шаблон цели.</param>
    /// <param name="file">Выгрузка плагина SCAD.</param>
    /// <param name="groups">ЖБ-группы схемы; null — не прочитаны (привязки — от шаблона).</param>
    public ScadSelectedPlateSectionSource(PlateSection template, ScadSelectedRebarFile file, ScadConcreteGroupIndex? groups)
    {
        _factory = new PlateElementSectionFactory(template);
        _file = file;
        _groups = groups;
    }

    /// <inheritdoc/>
    public string Key => FemCheckRebarSource.Selected;

    /// <inheritdoc/>
    public PlateElementSection Resolve(FemCheckScopeElement element)
    {
        if (element.ElemNum is not int num || !_file.Plates.TryGetValue(num, out var p))
            return PlateElementSection.Missing(ScadConcreteGroupIndex.NoSelectionReason(_groups, element.ElemNum), Label);
        if (p.As1 is not double as1 || p.As2 is not double as2 || p.As3 is not double as3 || p.As4 is not double as4)
            return PlateElementSection.Missing("подбор SCAD для КЭ не выполнен", Label);

        double h = element.Element.ThicknessM is > 0 and var own ? own : _factory.Template.H;
        var top = _factory.Top;
        var bottom = _factory.Bottom;

        double a1, a2, a3, a4;
        if (_groups?.Find(num) is { } g)
        {
            (a1, a2, a3, a4) = ScadConcreteGroupIndex.PlateCovers(g);
            if (!(a1 > 0 && a2 > 0 && a3 > 0 && a4 > 0) || a1 + a2 >= h || a3 + a4 >= h)
                return PlateElementSection.Missing(string.Format(CultureInfo.InvariantCulture,
                    "привязки ЖБ-группы SCAD {0} ({1:0.#}/{2:0.#}/{3:0.#}/{4:0.#} мм) не помещаются в толщину {5:0.#} мм",
                    g.Num, a1 * 1000, a2 * 1000, a3 * 1000, a4 * 1000, h * 1000), Label);
        }
        else
        {
            if (top == null)
                return PlateElementSection.Missing("КЭ нет в ЖБ-группах SCAD, а в сечении цели нет слоя арматуры у грани Z+ — " +
                                                   "привязку подобранной арматуры взять неоткуда", Label);
            if (bottom == null)
                return PlateElementSection.Missing("КЭ нет в ЖБ-группах SCAD, а в сечении цели нет слоя арматуры у грани Z− — " +
                                                   "привязку подобранной арматуры взять неоткуда", Label);
            (a1, a2, a3, a4) = (bottom.CoverX, top.CoverX, bottom.CoverY, top.CoverY);
        }

        var layers = new List<PlateRebarLayer>(2);
        PlateSectionSourceHelpers.AddLayer(layers, +1, h, as2, as4, a2, a4, top?.DiameterX ?? 0, top?.DiameterY ?? 0);
        PlateSectionSourceHelpers.AddLayer(layers, -1, h, as1, as3, a1, a3, bottom?.DiameterX ?? 0, bottom?.DiameterY ?? 0);
        var (section, key) = _factory.Get(h, layers);
        return new PlateElementSection(section, Label, key, null);
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> Warnings(IReadOnlyList<FemCheckScopeElement> elements, Material? concrete, Material? rebar)
    {
        var concreteClasses = new SortedSet<string>(StringComparer.Ordinal);
        var rebarClasses = new SortedSet<string>(StringComparer.Ordinal);
        int withoutGroup = 0;
        foreach (var e in elements)
        {
            if (e.ElemNum is not int num || !_file.Plates.ContainsKey(num)) continue;
            if (_groups?.Find(num) is not { } g) { withoutGroup++; continue; }
            if (g.ConcreteClass.Length > 0) concreteClasses.Add(g.ConcreteClass);
            if (g.LongitudinalRebarClass.Length > 0) rebarClasses.Add(g.LongitudinalRebarClass);
        }

        var warnings = new List<string>();
        PlateSectionSourceHelpers.AddMismatch(warnings, "SCAD", "бетона", concreteClasses, concrete);
        PlateSectionSourceHelpers.AddMismatch(warnings, "SCAD", "арматуры", rebarClasses, rebar);
        if (withoutGroup > 0)
            warnings.Add(_groups == null
                ? "ЖБ-группы SCAD у схемы не прочитаны — привязки подобранной арматуры взяты из сечения цели."
                : $"{withoutGroup} КЭ с подбором SCAD не входят в ЖБ-группы — привязки арматуры взяты из сечения цели.");
        if (_groups is { MultiGroupElements: > 0 } idx)
            warnings.Add($"{idx.MultiGroupElements} КЭ схемы входят в несколько ЖБ-групп SCAD — привязки взяты " +
                         "из группы с меньшим номером.");
        return warnings;
    }
}
