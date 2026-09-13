using CScore.Sp63;

namespace CScore.Sp63.Normal;

/// <summary>Извлекает профиль таврового/двутаврового сечения по выбранной оси.</summary>
public static class Sp63TeeRebarLayoutAnalyzer
{
    /// <summary>
    /// Анализирует точечные стержни, выбирая крайние слои относительно направления
    /// растяжения и сжатую полку по направлению растяжения.
    /// </summary>
    public static Sp63TeeProfileAnalysis Analyze(CrossSection section,
        Sp63NormalAxis axis, CalcType calc, int tensionDirection)
    {
        ArgumentNullException.ThrowIfNull(section);
        if (tensionDirection is not (1 or -1))
            return new(null, [Failure("invalid_tension_direction",
                "Sp63Normal_InvalidTensionDirection", "8.1")]);

        var geometry = Sp63TeeGeometryPolicy.Classify(section, axis);
        if (!geometry.IsApplicable)
        {
            bool notATee = geometry.Failure == Sp63TeeGeometryFailure.NotATeeShape;
            return new(null, [new Sp63NormalMessage(
                notATee ? "not_a_tee_shape" : "unsupported_geometry",
                Sp63NormalMessageKind.Applicability,
                "8.1.11",
                notATee ? "Sp63Normal_NotATeeShape" : "Sp63Normal_TeeGeometryNotSupported")]);
        }

        var concreteArea = section.Areas.Single(area =>
            area.Category == AreaCategory.Region &&
            area.Material?.Type == MatType.Concrete);
        var concreteChars = concreteArea.Material!.GetChars(calc);
        if (concreteChars is null || !IsPositiveFinite(Math.Abs(concreteChars.Fc)))
            return new(null, [Failure("missing_concrete_resistance",
                "Sp63Normal_MissingConcreteResistance", "8.1.8")]);

        if (!Sp63RebarBarCollector.TryCollect(section, axis, calc, out var layers,
                out var rebarMessage))
            return new(null, [rebarMessage!]);

        var tee = geometry.Geometry!;
        var minLayer = layers[0];
        var maxLayer = layers[^1];
        var tension = tensionDirection > 0 ? maxLayer : minLayer;
        bool hasCompressionLayer = layers.Count > 1;
        var compression = tensionDirection > 0 ? minLayer : maxLayer;

        double compressionFace = tensionDirection > 0
            ? tee.HeightCoordMin : tee.HeightCoordMax;
        double h0 = Math.Abs(compressionFace - tension.Coordinate);
        double aPrime = hasCompressionLayer
            ? Math.Abs(compressionFace - compression.Coordinate)
            : 0.0;
        if (!IsPositiveFinite(tee.Bw) || !IsPositiveFinite(tee.H) ||
            !IsPositiveFinite(h0) || !IsNonNegativeFinite(aPrime))
            return new(null, [Failure("invalid_rebar_geometry",
                "Sp63Normal_InvalidRebarGeometry", "8.1.8")]);

        double totalArea = layers.Sum(layer => layer.Area);
        double tensionResistance = tension.Rs * tension.Area;
        double compressionResistance = hasCompressionLayer
            ? compression.Rsc * compression.Area
            : 0.0;
        double maxResistance = Math.Max(tensionResistance, compressionResistance);
        double relativeDifference = maxResistance > 0
            ? Math.Abs(tensionResistance - compressionResistance) / maxResistance
            : 0.0;

        double compressionFlangeThickness = tensionDirection > 0
            ? tee.BottomFlangeThickness : tee.TopFlangeThickness;
        double compressionFlangeWidth = tensionDirection > 0
            ? tee.BottomFlangeWidth : tee.TopFlangeWidth;

        var tensionLayer = new Sp63NormalRebarLayer(tension.Coordinate,
            tension.Area, tension.Rs, tension.Rsc, tension.Bars);
        var compressionLayer = hasCompressionLayer
            ? new Sp63NormalRebarLayer(compression.Coordinate, compression.Area,
                compression.Rs, compression.Rsc, compression.Bars)
            : new Sp63NormalRebarLayer(compressionFace, 0.0,
                tension.Rs, tension.Rsc, []);
        var profile = new Sp63TeeSectionProfile(tee.Bw, tee.H, h0, aPrime,
            tensionLayer, compressionLayer, totalArea,
            compressionFlangeWidth, compressionFlangeThickness,
            relativeDifference <= Sp63RebarBarCollector.LayerTolerance,
            relativeDifference);
        return new(profile, []);
    }

    static Sp63NormalMessage Failure(string code, string text, string reference) =>
        new(code, Sp63NormalMessageKind.Applicability, reference, text);

    static bool IsPositiveFinite(double value) => double.IsFinite(value) && value > 0;

    static bool IsNonNegativeFinite(double value) => double.IsFinite(value) && value >= 0;
}
