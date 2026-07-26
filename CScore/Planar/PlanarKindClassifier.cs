namespace CScore.Planar;

/// <summary>Автоматическая геометрическая классификация plate/wall по нормали Frame3D.
/// Не меняет геометрию — только предлагает семантический тип; ручной override и явная
/// семантика импортёра (за рамками этого среза) имеют приоритет — см. FemMember.KindSource.</summary>
public static class PlanarKindClassifier
{
    /// <summary>cos(45°) — порог отнесения к плите (нормаль ближе к вертикали) или к стене
    /// (нормаль ближе к горизонтали).</summary>
    public const double PlateNormalCosineThreshold = 0.70710678;
    public const double AmbiguityBand = 0.02;

    public static string Classify(Frame3D frame, out bool ambiguous)
    {
        double verticalComponent = Math.Abs(frame.LocalZ.Z);
        ambiguous = Math.Abs(verticalComponent - PlateNormalCosineThreshold) < AmbiguityBand;
        return verticalComponent >= PlateNormalCosineThreshold ? "plate" : "wall";
    }
}
