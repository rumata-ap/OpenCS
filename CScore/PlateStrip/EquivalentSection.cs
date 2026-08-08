using CScore.Fem;

namespace CScore.PlateStrip;

/// <summary>Сохраняемый линейный результат редукции полосы в стержневое сечение.</summary>
public sealed class EquivalentSection
{
    public int Id { get; set; }
    public int Num { get; set; }
    public string Tag { get; set; } = "";
    public string Description { get; set; } = "";
    public int SourceSchemaId { get; set; }
    public int SourceRegionId { get; set; }
    public int SourcePlateSectionId { get; set; }
    /// <summary>Снимок PlanarRegion.GeometryFingerprint на момент последней успешной сборки.</summary>
    public string SourceRegionFingerprint { get; set; } = "";
    /// <summary>Доля длины полосы (0..1), на которой резолвится раскладка армирования по ширине.</summary>
    public double SpanStationFraction { get; set; } = 0.5;
    public PlateStripBeamAnalogy Strip { get; set; } = new();
    public ReductionPolicy ReductionPolicy { get; set; }
    public EquivalentSectionSourceKind SourceKind { get; set; }
    public int WidthIntegrationPoints { get; set; }
    public double[,] BeamTangent { get; set; } = new double[3, 3];
    public double EA { get; set; }
    public double EIy { get; set; }
    public double EIz { get; set; }
    /// <summary>Крутильная жёсткость Среза 2, пока не редуцируется.</summary>
    public double TorsionalStiffness => 0.0;
    public bool IsCalculable { get; set; }
    public bool IsStale { get; set; }
    public List<FemValidationDiagnostic> Diagnostics { get; set; } = [];
    public string InputFingerprint { get; set; } = "";
    public string ResultFingerprint { get; set; } = "";

    /// <summary>Вычислить доменные силы [N, My, Mz] для линейного состояния.</summary>
    public double[] Forces(BeamStrainState state)
    {
        var result = new double[3];
        var vector = new[] { state.Eps0, state.KappaY, state.KappaZ };
        for (int i = 0; i < 3; i++)
        for (int j = 0; j < 3; j++)
            result[i] += BeamTangent[i, j] * vector[j];
        return result;
    }

    /// <summary>Вернуть копию доменной матрицы касательной жёсткости.</summary>
    public double[,] Tangent()
    {
        var result = new double[3, 3];
        Array.Copy(BeamTangent, result, BeamTangent.Length);
        return result;
    }
}

/// <summary>Результат построения эквивалентного сечения.</summary>
public sealed record EquivalentSectionBuildResult(
    bool IsCalculable,
    EquivalentSection? Section,
    IReadOnlyList<FemValidationDiagnostic> Diagnostics);
