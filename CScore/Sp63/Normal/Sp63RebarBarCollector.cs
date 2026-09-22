using CScore.Sp63;

namespace CScore.Sp63.Normal;

/// <summary>Собранный точечный стержень арматуры с сопротивлениями.</summary>
internal sealed record Sp63CollectedBar(double X, double Y, double Area,
    double Diameter, double Coordinate, double Rs, double Rsc, bool IsIdealized);

/// <summary>Уровень арматуры, объединяющий стержни с близкими координатами.</summary>
internal sealed record Sp63CollectedLayer(double Coordinate, double Area, double Rs,
    double Rsc, IReadOnlyList<(double X, double Y, double Area, double Diameter)> Bars,
    bool IsIdealized);

/// <summary>Сбор и группировка точечной арматуры, общие для прямоугольного и таврового режимов.</summary>
internal static class Sp63RebarBarCollector
{
    /// <summary>Допуск объединения стержней в один уровень, м.</summary>
    public const double LayerTolerance = 1e-6;

    /// <summary>Собирает и группирует точечные стержни по уровням.</summary>
    public static bool TryCollect(CrossSection section, Sp63NormalAxis axis,
        CalcType calc, out List<Sp63CollectedLayer> layers,
        out Sp63NormalMessage? message, bool requireAtLeastTwoLayers = false)
    {
        layers = [];
        if (!TryCollectBars(section, calc, out var bars, out message))
            return false;

        var withCoordinates = bars
            .Select(bar => bar with { Coordinate = axis == Sp63NormalAxis.Mx ? bar.Y : bar.X })
            .ToList();
        layers = GroupLayers(withCoordinates);
        if (requireAtLeastTwoLayers && layers.Count < 2)
            return Failure("insufficient_rebar_layers", "Sp63Normal_InsufficientRebarLayers",
                "8.1.8", out message);
        return true;
    }

    /// <summary>
    /// Собирает точечные стержни без привязки к оси изгиба (Coordinate = 0) и
    /// проверяет общие условия: без преднапряжения, точечные волокна, единые Rs/Rsc.
    /// </summary>
    public static bool TryCollectBars(CrossSection section, CalcType calc,
        out List<Sp63CollectedBar> bars, out Sp63NormalMessage? message)
    {
        bars = [];
        message = null;
        var rebarAreas = section.Areas
            .Where(area => area.Category == AreaCategory.RebarGroup)
            .ToList();
        if (rebarAreas.Any(area => area.SigSp != 0.0))
            return Failure("prestressed_rebar", "Sp63Normal_PrestressedRebar", "9.2",
                out message);

        foreach (var area in rebarAreas)
        {
            if (area.Material is null)
                return Failure("missing_rebar_material", "Sp63Normal_MissingRebarMaterial",
                    "8.1.8", out message);
            if (area.Fibers.Any(fiber => fiber.TypeFiber != FiberType.point))
                return Failure("non_point_rebar", "Sp63Normal_NonPointRebar", "8.1.8",
                    out message);

            var chars = area.Material.GetChars(calc);
            double rsc = chars?.GetRscOrLegacyFc() ?? 0.0;
            if (chars is null || !IsPositiveFinite(Math.Abs(chars.Ft)) ||
                !IsPositiveFinite(rsc))
                return Failure("missing_rebar_resistance",
                    "Sp63Normal_MissingRebarResistance", "8.1.8", out message);

            double rs = Math.Abs(chars.Ft);
            foreach (var fiber in area.Fibers)
            {
                if (!IsPositiveFinite(fiber.Area))
                    return Failure("invalid_rebar_area", "Sp63Normal_InvalidRebarArea",
                        "8.1.8", out message);
                bars.Add(new Sp63CollectedBar(fiber.X, fiber.Y, fiber.Area, fiber.Diameter,
                    0.0, rs, rsc,
                    area.RebarRepresentation == RebarRepresentation.IdealizedLayer));
            }
        }

        if (bars.Count == 0)
            return Failure("missing_rebar", "Sp63Normal_MissingRebar", "8.1.8", out message);

        var reference = bars[0];
        if (bars.Any(bar => !NearlyEqual(bar.Rs, reference.Rs) ||
                            !NearlyEqual(bar.Rsc, reference.Rsc)))
            return Failure("mixed_rebar_resistance", "Sp63Normal_MixedRebarResistance",
                "8.1.8", out message);
        return true;
    }

    static List<Sp63CollectedLayer> GroupLayers(List<Sp63CollectedBar> bars)
    {
        var grouped = new List<Sp63CollectedLayer>();
        foreach (var bar in bars.OrderBy(bar => bar.Coordinate))
        {
            int index = grouped.FindLastIndex(existing =>
                Math.Abs(existing.Coordinate - bar.Coordinate) <= LayerTolerance);
            if (index < 0)
            {
                grouped.Add(new Sp63CollectedLayer(bar.Coordinate, bar.Area, bar.Rs,
                    bar.Rsc, [(bar.X, bar.Y, bar.Area, bar.Diameter)], bar.IsIdealized));
                continue;
            }

            var existingLayer = grouped[index];
            double totalArea = existingLayer.Area + bar.Area;
            double coordinate = (existingLayer.Coordinate * existingLayer.Area +
                bar.Coordinate * bar.Area) / totalArea;
            var barsList = new List<(double X, double Y, double Area, double Diameter)>(
                existingLayer.Bars) { (bar.X, bar.Y, bar.Area, bar.Diameter) };
            grouped[index] = existingLayer with
            {
                Coordinate = coordinate,
                Area = totalArea,
                Bars = barsList,
                IsIdealized = existingLayer.IsIdealized || bar.IsIdealized
            };
        }

        return grouped;
    }

    static bool Failure(string code, string text, string reference,
        out Sp63NormalMessage? message)
    {
        message = new Sp63NormalMessage(code, Sp63NormalMessageKind.Applicability,
            reference, text);
        return false;
    }

    static bool NearlyEqual(double left, double right) =>
        Math.Abs(left - right) <= LayerTolerance *
        Math.Max(1.0, Math.Max(Math.Abs(left), Math.Abs(right)));

    static bool IsPositiveFinite(double value) => double.IsFinite(value) && value > 0;
}
