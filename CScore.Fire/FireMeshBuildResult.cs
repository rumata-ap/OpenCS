using CSfea.Thermal;

namespace CScore.Fire;

/// <summary>
/// Результат построения тепловой сетки для огневого расчёта.
/// </summary>
public sealed class FireMeshBuildResult
{
    /// <summary>Тепловая треугольная сетка T3.</summary>
    public required HeatMesh Mesh { get; init; }

    /// <summary>Линейная T3-сетка до повышения до T6; <c>null</c> для линейного расчёта.</summary>
    public HeatMesh? LinearMesh { get; init; }

    /// <summary>Граничные рёбра сетки с привязкой к исходным рёбрам контура.</summary>
    public required IReadOnlyList<FireBoundaryEdgeInfo> BoundaryEdges { get; init; }

    /// <summary>Координаты арматурных точек и их локальные координаты в элементах.</summary>
    public required IReadOnlyList<FireRebarLocation> Rebars { get; init; }
}

/// <summary>
/// Метаданные граничного ребра для сопоставления с огневыми ГУ.
/// </summary>
public sealed class FireBoundaryEdgeInfo
{
    /// <summary>Индекс первого узла ребра в <see cref="FireMeshBuildResult.Mesh"/>.</summary>
    public int NodeA { get; init; }

    /// <summary>Индекс второго узла ребра в <see cref="FireMeshBuildResult.Mesh"/>.</summary>
    public int NodeB { get; init; }

    /// <summary>Длина ребра, м.</summary>
    public double LengthM { get; init; }

    /// <summary>Индекс исходного ребра контура (outer или hole).</summary>
    public int OriginalEdgeIndex { get; init; }

    /// <summary>Тип контура: <c>outer</c> или <c>hole</c>.</summary>
    public string ContourType { get; init; } = "outer";

    /// <summary>Индекс отверстия для <c>hole</c>; <c>null</c> для внешнего контура.</summary>
    public int? HoleIndex { get; init; }
}

/// <summary>
/// Положение арматурной точки в элементе сетки.
/// </summary>
public sealed class FireRebarLocation
{
    /// <summary>Порядковый номер арматурной точки.</summary>
    public int Id { get; init; }

    /// <summary>Координата X точки, м.</summary>
    public double X { get; init; }

    /// <summary>Координата Y точки, м.</summary>
    public double Y { get; init; }

    /// <summary>Индекс элемента T3, содержащего точку.</summary>
    public int ElementIndex { get; init; }

    /// <summary>Первая барицентрическая координата.</summary>
    public double Xi1 { get; init; }

    /// <summary>Вторая барицентрическая координата.</summary>
    public double Xi2 { get; init; }

    /// <summary>Третья барицентрическая координата (T3).</summary>
    public double Xi3 { get; init; }

    /// <summary>Веса функций формы в узлах элемента (длина 3 или 6); приоритет над Xi1–Xi3.</summary>
    public double[]? ShapeWeights { get; init; }
}
