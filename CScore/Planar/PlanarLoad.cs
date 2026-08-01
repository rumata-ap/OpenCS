using CScore.Fem;

namespace CScore.Planar;

/// <summary>Тип независимой от mesh нагрузки плоского региона.</summary>
public enum PlanarLoadKind
{
    Surface,
    Boundary,
    Point
}

/// <summary>Система координат вектора нагрузки.</summary>
public enum PlanarLoadCoordinateSystem
{
    Local,
    Global
}

/// <summary>Нагрузка PlanarRegion до её дискретизации по узлам конкретного snapshot.</summary>
public sealed class PlanarLoad
{
    public string Tag { get; init; } = "";
    public PlanarLoadKind Kind { get; init; } = PlanarLoadKind.Surface;
    public PlanarLoadCoordinateSystem CoordinateSystem { get; init; } = PlanarLoadCoordinateSystem.Local;
    public PlanarVector3 Components { get; init; } = PlanarVector3.Zero;
    public PlanarBoundaryKey? BoundaryKey { get; init; }
    public double PointU { get; init; }
    public double PointV { get; init; }
    public double PointToleranceM { get; init; } = 1e-9;

    /// <summary>Проверяет форму нагрузки до обращения к mesh mapper.</summary>
    public void Validate()
    {
        if (!Components.IsFinite)
            throw new ArgumentException($"Нагрузка «{Tag}» содержит нечисловую компоненту.");

        switch (Kind)
        {
            case PlanarLoadKind.Surface:
                if (BoundaryKey is not null)
                    throw new ArgumentException($"Поверхностная нагрузка «{Tag}» не должна иметь BoundaryKey.");
                break;
            case PlanarLoadKind.Boundary:
                if (BoundaryKey is null)
                    throw new ArgumentException($"Для краевой нагрузки «{Tag}» не задан BoundaryKey.");
                break;
            case PlanarLoadKind.Point:
                if (BoundaryKey is not null)
                    throw new ArgumentException($"Точечная нагрузка «{Tag}» не должна иметь BoundaryKey.");
                if (!double.IsFinite(PointU) || !double.IsFinite(PointV))
                    throw new ArgumentException($"Точка нагрузки «{Tag}» содержит нечисловую координату.");
                if (!double.IsFinite(PointToleranceM) || PointToleranceM < 0)
                    throw new ArgumentException($"Допуск точки нагрузки «{Tag}» должен быть конечным и неотрицательным.");
                break;
            default:
                throw new ArgumentException($"Нагрузка «{Tag}» имеет неизвестный тип.");
        }
    }
}

/// <summary>Явный набор mesh-сущностей для одной роли границы.</summary>
public sealed record PlanarBoundarySet(
    BoundaryRole Role,
    IReadOnlyList<PlanarBoundaryKey> BoundaryKeys,
    IReadOnlyList<int> NodeIndices,
    IReadOnlyList<(int A, int B)> Edges);

/// <summary>Результат проверки и группировки boundary mappings.</summary>
public sealed record PlanarBoundaryContractResult(
    bool IsCalculable,
    IReadOnlyList<FemValidationDiagnostic> Diagnostics,
    IReadOnlyList<PlanarBoundarySet> Sets);

/// <summary>Результат переноса независимых нагрузок на узлы snapshot.</summary>
public sealed record PlanarLoadMappingResult(
    bool IsCalculable,
    IReadOnlyList<FemValidationDiagnostic> Diagnostics,
    IReadOnlyDictionary<int, PlanarVector3> NodalLoads,
    IReadOnlyList<PlanarBoundarySet> BoundarySets,
    PlanarVector3 AppliedForceGlobal,
    PlanarVector3 AppliedMomentAboutOriginGlobal,
    PlanarVector3 MappedForceGlobal,
    PlanarVector3 MappedMomentAboutOriginGlobal);

/// <summary>Допуски проверки равновесия узловой дискретизации.</summary>
public sealed record PlanarLoadMappingOptions
{
    public double RelativeTolerance { get; init; } = 1e-9;
    public double ForceAbsoluteTolerance { get; init; } = 1e-9;
    public double MomentAbsoluteTolerance { get; init; } = 1e-9;

    public void Validate()
    {
        if (!double.IsFinite(RelativeTolerance) || RelativeTolerance < 0)
            throw new ArgumentOutOfRangeException(nameof(RelativeTolerance));
        if (!double.IsFinite(ForceAbsoluteTolerance) || ForceAbsoluteTolerance < 0)
            throw new ArgumentOutOfRangeException(nameof(ForceAbsoluteTolerance));
        if (!double.IsFinite(MomentAbsoluteTolerance) || MomentAbsoluteTolerance < 0)
            throw new ArgumentOutOfRangeException(nameof(MomentAbsoluteTolerance));
    }
}
