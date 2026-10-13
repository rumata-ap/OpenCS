using CScore;
using CScore.Fem;
using CScore.Planar;
using CScore.PlateStrip;
using OpenCS.Utilites;

namespace OpenCS.Services;

/// <summary>
/// Источник армирования вдоль полосы по ключу: КЭ сетки региона импортированной схемы
/// (заданное/подобранное армирование ЛИРЫ и SCAD) через общий <see cref="IPlateElementSectionSource"/>,
/// тот же, что у проверок по КЭ. Пустой ключ и «layout» — раскладка региона (источник не нужен).
/// </summary>
public static class StripRebarSourceFactory
{
    /// <summary>Ключ означает армирование КЭ сетки, а не раскладку региона.</summary>
    public static bool IsMeshKey(string? key) =>
        !string.IsNullOrEmpty(key) && key != FemCheckRebarSource.Layout;

    /// <summary>
    /// Источник по ключу; null — раскладка региона. Диагностики: неизвестный ключ и нет КонЭ региона — ошибки,
    /// предупреждения источника (классы материалов, привязки) — не ошибки.
    /// </summary>
    /// <param name="template">Сечение-шаблон: бетон, материалы, модель и диаграмма — от него.</param>
    public static (IStripRebarSource? Source, IReadOnlyList<FemValidationDiagnostic> Diagnostics) Create(
        DatabaseService database, IReadOnlyDictionary<int, Material> materials, int schemaId, PlanarRegion region,
        string key, PlateSection template)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(region);
        ArgumentNullException.ThrowIfNull(template);
        if (!IsMeshKey(key)) return (null, []);

        var data = FemCheckSchemaData.Load(database, schemaId);
        var member = data.Members.FirstOrDefault(m => m.PlanarRegionId == region.Id);
        if (member == null)
            return (null, [new("strip_rebar_source_member_not_found",
                $"У региона Id={region.Id} нет конструктивного элемента с КЭ сетки схемы.")]);

        var source = FemCheckContext.PlateSource(key, template, data, database.PlateSections);
        if (source == null)
            return (null, [new("strip_rebar_source_unknown", $"Неизвестный источник армирования «{key}».")]);

        var scope = FemCheckScope.ForMember(member, data.Mesh);
        var diagnostics = new List<FemValidationDiagnostic>();
        foreach (string error in data.Errors)
            diagnostics.Add(new("strip_rebar_source_file_error", error, false));

        var mesh = MeshElementStripRebarSource.Create(region.Frame, scope.Elements, data.MeshNodes, source);
        if (mesh.ElementCount == 0)
            diagnostics.Add(new("strip_rebar_source_no_elements",
                $"У конструктивного элемента «{member.ElemTag}» нет пластинчатых КЭ сетки — армирование взять неоткуда."));
        if (mesh.SkippedElements > 0)
            diagnostics.Add(new("strip_rebar_source_elements_skipped",
                $"{mesh.SkippedElements} КЭ региона без геометрии пропущены при поиске армирования вдоль полосы.", false));
        if (mesh.ElementsWithUnknownAxes > 0)
            diagnostics.Add(new("strip_rebar_source_axes_unknown",
                $"У {mesh.ElementsWithUnknownAxes} КЭ оси выдачи усилий неизвестны — приняты оси региона.", false));

        materials.TryGetValue(template.ConcreteMaterialId, out var concrete);
        materials.TryGetValue(template.RebarMaterialId, out var rebar);
        foreach (string warning in source.Warnings(mesh.Elements, concrete, rebar))
            diagnostics.Add(new("strip_rebar_source_warning", warning, false));

        return (mesh, diagnostics);
    }
}
