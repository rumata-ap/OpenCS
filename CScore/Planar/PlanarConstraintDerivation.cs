using CScore.Fem;

namespace CScore.Planar;

/// <summary>Политика автоматического получения constraint-объектов из FEM topology.</summary>
public sealed class PlanarConstraintDerivationOptions
{
    public const string DefaultAlgorithmVersion = "fem-driven-constraints-v1";
    public const PlanarDofMask AllDofs = PlanarDofMask.UX | PlanarDofMask.UY | PlanarDofMask.UZ |
                                          PlanarDofMask.RX | PlanarDofMask.RY | PlanarDofMask.RZ;
    public const PlanarDofMask Translations = PlanarDofMask.UX | PlanarDofMask.UY | PlanarDofMask.UZ;

    public string AlgorithmVersion { get; init; } = DefaultAlgorithmVersion;
    public double PlaneToleranceM { get; init; } = 1e-8;
    public double GeometryToleranceM { get; init; } = 1e-8;
    public double MinimumCurveLengthM { get; init; } = 1e-8;
    public bool AutomaticMode { get; init; } = true;
    public PlanarDofMask CommonNodeDofMask { get; init; } = AllDofs;
    public PlanarDofMask TransverseBarDofMask { get; init; } = Translations;
    public PlanarDofMask CoplanarBarDofMask { get; init; } = Translations;
    public PlanarDofMask WallLineDofMask { get; init; } = Translations;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(AlgorithmVersion))
            throw new InvalidOperationException("Версия алгоритма derivation не задана.");
        if (!double.IsFinite(PlaneToleranceM) || PlaneToleranceM <= 0)
            throw new InvalidOperationException("Допуск плоскости должен быть положительным конечным числом.");
        if (!double.IsFinite(GeometryToleranceM) || GeometryToleranceM <= 0)
            throw new InvalidOperationException("Геометрический допуск должен быть положительным конечным числом.");
        if (!double.IsFinite(MinimumCurveLengthM) || MinimumCurveLengthM <= 0)
            throw new InvalidOperationException("Минимальная длина curve должна быть положительным конечным числом.");
    }
}

/// <summary>Результат автоматического derivation для одного PlanarRegion.</summary>
public sealed class DerivedPlanarConstraintSet
{
    public IReadOnlyList<PlanarConstraintObject> Constraints { get; init; } = [];
    public string SourceFingerprint { get; init; } = "";
    public IReadOnlyList<FemValidationDiagnostic> Diagnostics { get; init; } = [];
    public bool IsCalculable => !Diagnostics.Any(diagnostic => diagnostic.IsError);
    public int SourceNodeCount { get; init; }
    public int PointLocusCount { get; init; }
    public int CurveLocusCount { get; init; }
    public int SourceMemberCount { get; init; }
}
