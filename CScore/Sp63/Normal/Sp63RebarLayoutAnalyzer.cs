using CScore.Sp63;

namespace CScore.Sp63.Normal;

/// <summary>Извлекает два эффективных слоя точечной арматуры по выбранной оси.</summary>
public static class Sp63RebarLayoutAnalyzer
{
    /// <summary>Допуск объединения стержней в один уровень, м.</summary>
    public const double LayerTolerance = 1e-6;

    /// <summary>
    /// Анализирует точечные стержни, выбирая крайние слои относительно направления растяжения.
    /// </summary>
    /// <param name="section">Расчётное сечение.</param>
    /// <param name="axis">Ось изгиба.</param>
    /// <param name="calc">Вид расчёта для характеристик материалов.</param>
    /// <param name="tensionDirection">Направление растяжения: +1 по положительной оси, -1 по отрицательной.</param>
    public static Sp63NormalProfileAnalysis Analyze(
        CrossSection section,
        Sp63NormalAxis axis,
        CalcType calc,
        int tensionDirection)
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

        var rebarAreas = section.Areas
            .Where(area => area.Category == AreaCategory.RebarGroup)
            .ToList();
        if (rebarAreas.Any(area => area.SigSp != 0.0))
            return Failure("prestressed_rebar", "Sp63Normal_PrestressedRebar", "9.2");

        var bars = new List<BarData>();
        foreach (var area in rebarAreas)
        {
            if (area.Material is null)
                return Failure("missing_rebar_material", "Sp63Normal_MissingRebarMaterial", "8.1.8");
            if (area.Fibers.Any(fiber => fiber.TypeFiber != FiberType.point))
                return Failure("non_point_rebar", "Sp63Normal_NonPointRebar", "8.1.8");

            var chars = area.Material.GetChars(calc);
            if (chars is null || !IsPositiveFinite(Math.Abs(chars.Ft)) ||
                !IsPositiveFinite(Math.Abs(chars.Fc)))
                return Failure("missing_rebar_resistance", "Sp63Normal_MissingRebarResistance", "8.1.8");

            double rs = Math.Abs(chars.Ft);
            double rsc = Math.Abs(chars.Fc);
            foreach (var fiber in area.Fibers)
            {
                if (!IsPositiveFinite(fiber.Area))
                    return Failure("invalid_rebar_area", "Sp63Normal_InvalidRebarArea", "8.1.8");

                double coordinate = axis == Sp63NormalAxis.Mx ? fiber.Y : fiber.X;
                bars.Add(new BarData(fiber.X, fiber.Y, fiber.Area, coordinate, rs, rsc));
            }
        }

        if (bars.Count == 0)
            return Failure("missing_rebar", "Sp63Normal_MissingRebar", "8.1.8");

        var reference = bars[0];
        if (bars.Any(bar => !NearlyEqual(bar.Rs, reference.Rs) ||
                            !NearlyEqual(bar.Rsc, reference.Rsc)))
            return Failure("mixed_rebar_resistance", "Sp63Normal_MixedRebarResistance", "8.1.8");

        var layers = GroupLayers(bars);
        if (layers.Count < 2)
            return Failure("insufficient_rebar_layers", "Sp63Normal_InsufficientRebarLayers", "8.1.8");

        var minLayer = layers[0];
        var maxLayer = layers[^1];
        var tension = tensionDirection > 0 ? maxLayer : minLayer;
        var compression = tensionDirection > 0 ? minLayer : maxLayer;
        var rect = geometry.Geometry!;

        double height = axis == Sp63NormalAxis.Mx ? rect.Height : rect.Width;
        double width = axis == Sp63NormalAxis.Mx ? rect.Width : rect.Height;
        double compressionFace = tensionDirection > 0
            ? (axis == Sp63NormalAxis.Mx ? rect.MinY : rect.MinX)
            : (axis == Sp63NormalAxis.Mx ? rect.MaxY : rect.MaxX);

        double h0 = Math.Abs(compressionFace - tension.Coordinate);
        double aPrime = Math.Abs(compressionFace - compression.Coordinate);
        if (!IsPositiveFinite(width) || !IsPositiveFinite(height) ||
            !IsPositiveFinite(h0) || !IsPositiveFinite(aPrime))
            return Failure("invalid_rebar_geometry", "Sp63Normal_InvalidRebarGeometry", "8.1.8");

        double totalArea = bars.Sum(bar => bar.Area);
        double tensionResistance = tension.Rs * tension.Area;
        double compressionResistance = compression.Rsc * compression.Area;
        double maxResistance = Math.Max(tensionResistance, compressionResistance);
        double relativeDifference = maxResistance > 0
            ? Math.Abs(tensionResistance - compressionResistance) / maxResistance
            : 0.0;
        double xWithoutCompression = tension.Rs * tension.Area /
            (Math.Abs(concreteChars.Fc) * width);

        var tensionLayer = new Sp63NormalRebarLayer(
            tension.Coordinate, tension.Area, tension.Rs, tension.Rsc, tension.Bars);
        var compressionLayer = new Sp63NormalRebarLayer(
            compression.Coordinate, compression.Area, compression.Rs, compression.Rsc,
            compression.Bars);
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

    static List<LayerData> GroupLayers(List<BarData> bars)
    {
        var layers = new List<LayerData>();
        foreach (var bar in bars.OrderBy(bar => bar.Coordinate))
        {
            var layer = layers.LastOrDefault(existing =>
                Math.Abs(existing.Coordinate - bar.Coordinate) <= LayerTolerance);
            if (layer is null)
            {
                layers.Add(new LayerData(bar));
                continue;
            }

            layer.Add(bar);
        }

        return layers;
    }

    static Sp63NormalProfileAnalysis Failure(string code, string text, string reference) =>
        new(null, [new Sp63NormalMessage(code,
            Sp63NormalMessageKind.Applicability, reference, text)]);

    static bool NearlyEqual(double left, double right) =>
        Math.Abs(left - right) <= LayerTolerance * Math.Max(1.0, Math.Max(Math.Abs(left), Math.Abs(right)));

    static bool IsPositiveFinite(double value) => double.IsFinite(value) && value > 0;

    sealed record BarData(double X, double Y, double Area, double Coordinate,
        double Rs, double Rsc);

    sealed class LayerData
    {
        readonly List<(double X, double Y, double Area)> bars = [];

        public LayerData(BarData bar)
        {
            Coordinate = bar.Coordinate;
            Area = bar.Area;
            Rs = bar.Rs;
            Rsc = bar.Rsc;
            bars.Add((bar.X, bar.Y, bar.Area));
        }

        public double Coordinate { get; private set; }
        public double Area { get; private set; }
        public double Rs { get; }
        public double Rsc { get; }
        public IReadOnlyList<(double X, double Y, double Area)> Bars => bars;

        public void Add(BarData bar)
        {
            double oldArea = Area;
            Area += bar.Area;
            Coordinate = (Coordinate * oldArea + bar.Coordinate * bar.Area) / Area;
            bars.Add((bar.X, bar.Y, bar.Area));
        }
    }
}
