using CScore.Planar;

namespace CScore.PlateStrip;

/// <summary>Декларация того, как одна PlateStripBeamAnalogy участвует в double-counting
/// проверках: какой регион-источник, какой коридор (для проверки жёсткости) и какие теги
/// нагрузок (для проверки нагрузок) она затрагивает при данной политике. ReplacedRegionPolygon —
/// коридор полосы в координатах региона-источника; при DiagnosticOnly это ПОТЕНЦИАЛЬНО
/// замещаемый коридор (полоса ничего не заменяет, пока политика не ReplaceShellRegion) — имя
/// поля общее для обеих политик, чтобы не дублировать тип.</summary>
public sealed record ShellReplacementManifest(
    string StripId,
    int SourceRegionId,
    ShellReplacementPolicy Policy,
    IReadOnlyList<PlanarPoint2D> ReplacedRegionPolygon,
    IReadOnlyList<string> StripLoadSourceTags)
{
    /// <summary>Заменяемая часть коридора при CoupledWithExplicitPartition (Срез 7); пусто при
    /// остальных политиках. Задано init-свойством, а не позиционным параметром: манифест —
    /// позиционный record, и добавление в список сломало бы арность Deconstruct и порядок
    /// существующих with-инициализаторов.</summary>
    public IReadOnlyList<PlanarPoint2D> PartitionPolygon { get; init; } = [];

    /// <summary>Идентификаторы StripBoundaryInterface, покрывающих границу разбиения.</summary>
    public IReadOnlyList<string> BoundaryInterfaceIds { get; init; } = [];

    /// <summary>Полигон, на который эта запись претендует при проверке жёсткости: для частичной
    /// замены — разбиение, иначе — весь коридор.</summary>
    public IReadOnlyList<PlanarPoint2D> ClaimedPolygon =>
        Policy == ShellReplacementPolicy.CoupledWithExplicitPartition
            ? PartitionPolygon
            : ReplacedRegionPolygon;

    /// <summary>Единственное место, где собирается StripLoadSourceTags ("правило переноса
    /// нагрузки" родительской спеки) — теги всех StripLoad, реально попавших в StripLoadSet этой
    /// полосы через StripLoadMapper (Срез 4).</summary>
    public static ShellReplacementManifest From(PlateStripBeamAnalogy analogy, StripLoadSet stripLoads)
    {
        ArgumentNullException.ThrowIfNull(analogy);
        ArgumentNullException.ThrowIfNull(stripLoads);
        return new(
            analogy.Id,
            analogy.SourceRegionId,
            analogy.Policy,
            analogy.Geometry.Polygon,
            stripLoads.Loads.Select(load => load.SourceTag).ToList());
    }
}
