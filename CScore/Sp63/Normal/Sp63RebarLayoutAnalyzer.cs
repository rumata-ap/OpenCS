using CScore.Sp63;

namespace CScore.Sp63.Normal;

/// <summary>Извлекает крайние эффективные слои точечной арматуры по выбранной оси.</summary>
public static class Sp63RebarLayoutAnalyzer
{
    /// <summary>Допуск объединения стержней в один уровень, м.</summary>
    public const double LayerTolerance = Sp63RebarBarCollector.LayerTolerance;

    /// <summary>
    /// Анализирует точечные стержни, выбирая крайние слои относительно направления растяжения.
    /// </summary>
    /// <param name="section">Расчётное сечение.</param>
    /// <param name="axis">Ось изгиба.</param>
    /// <param name="calc">Вид расчёта для характеристик материалов.</param>
    /// <param name="tensionDirection">Направление растяжения: +1 по положительной оси, -1 по отрицательной.</param>
    /// <param name="requireAtLeastTwoLayers">Требовать отдельные растянутый и сжатый слои.</param>
    public static Sp63NormalProfileAnalysis Analyze(
        CrossSection section,
        Sp63NormalAxis axis,
        CalcType calc,
        int tensionDirection,
        bool requireAtLeastTwoLayers = false)
    {
        ArgumentNullException.ThrowIfNull(section);
        if (tensionDirection is not (1 or -1))
            return Failure("invalid_tension_direction", "Sp63Normal_InvalidTensionDirection", "8.1");

        var geometry = Sp63RectangularGeometryPolicy.Classify(section);
        if (!geometry.IsApplicable)
            return new Sp63NormalProfileAnalysis(null,
                geometry.Reasons.Select(_ => new Sp63NormalMessage(
                    "unsupported_geometry",
                    Sp63NormalMessageKind.Applicability,
                    "геометрическая policy OpenCS",
                    "Sp63Normal_GeometryNotSupported")).ToList());

        var concreteArea = section.Areas.Single(area =>
            area.Category == AreaCategory.Region &&
            area.Material?.Type == MatType.Concrete);
        var concreteChars = concreteArea.Material!.GetChars(calc);
        if (concreteChars is null || !IsPositiveFinite(Math.Abs(concreteChars.Fc)))
            return Failure("missing_concrete_resistance", "Sp63Normal_MissingConcreteResistance", "8.1.8");

        if (!Sp63RebarBarCollector.TryCollect(section, axis, calc, out var layers,
                out var rebarMessage, requireAtLeastTwoLayers))
            return new Sp63NormalProfileAnalysis(null, [rebarMessage!]);

        var minLayer = layers[0];
        var maxLayer = layers[^1];
        var tension = tensionDirection > 0 ? maxLayer : minLayer;
        bool hasCompressionLayer = layers.Count > 1;
        var compression = tensionDirection > 0 ? minLayer : maxLayer;
        var rect = geometry.Geometry!;

        double height = axis == Sp63NormalAxis.Mx ? rect.Height : rect.Width;
        double width = axis == Sp63NormalAxis.Mx ? rect.Width : rect.Height;
        double compressionFace = tensionDirection > 0
            ? (axis == Sp63NormalAxis.Mx ? rect.MinY : rect.MinX)
            : (axis == Sp63NormalAxis.Mx ? rect.MaxY : rect.MaxX);

        double h0 = Math.Abs(compressionFace - tension.Coordinate);
        double aPrime = hasCompressionLayer
            ? Math.Abs(compressionFace - compression.Coordinate)
            : 0.0;
        if (!IsPositiveFinite(width) || !IsPositiveFinite(height) ||
            !IsPositiveFinite(h0) || !IsNonNegativeFinite(aPrime))
            return Failure("invalid_rebar_geometry", "Sp63Normal_InvalidRebarGeometry", "8.1.8");

        double totalArea = layers.Sum(layer => layer.Area);
        double tensionResistance = tension.Rs * tension.Area;
        double compressionResistance = hasCompressionLayer
            ? compression.Rsc * compression.Area
            : 0.0;
        double maxResistance = Math.Max(tensionResistance, compressionResistance);
        double relativeDifference = maxResistance > 0
            ? Math.Abs(tensionResistance - compressionResistance) / maxResistance
            : 0.0;
        double xWithoutCompression = tension.Rs * tension.Area /
            (Math.Abs(concreteChars.Fc) * width);

        var tensionLayer = new Sp63NormalRebarLayer(
            tension.Coordinate, tension.Area, tension.Rs, tension.Rsc, tension.Bars)
        { IsIdealized = tension.IsIdealized };
        var compressionLayer = hasCompressionLayer
            ? new Sp63NormalRebarLayer(compression.Coordinate, compression.Area,
                compression.Rs, compression.Rsc, compression.Bars)
            { IsIdealized = compression.IsIdealized }
            : new Sp63NormalRebarLayer(compressionFace, 0.0,
                tension.Rs, tension.Rsc, []);
        var profile = new Sp63NormalSectionProfile(
            width,
            height,
            h0,
            aPrime,
            tensionLayer,
            compressionLayer,
            totalArea,
            relativeDifference <= LayerTolerance,
            relativeDifference,
            xWithoutCompression);
        return new Sp63NormalProfileAnalysis(profile, []);
    }

    static Sp63NormalProfileAnalysis Failure(string code, string text, string reference) =>
        new(null, [new Sp63NormalMessage(code,
            Sp63NormalMessageKind.Applicability, reference, text)]);

    static bool IsPositiveFinite(double value) => double.IsFinite(value) && value > 0;

    static bool IsNonNegativeFinite(double value) => double.IsFinite(value) && value >= 0;
}
