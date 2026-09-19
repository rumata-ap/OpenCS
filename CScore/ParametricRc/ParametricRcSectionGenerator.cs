namespace CScore.ParametricRc;

/// <summary>Результат материализации параметрического источника.</summary>
public sealed record ParametricRcGenerationResult(CrossSection Section, IReadOnlyList<string> Diagnostics);

/// <summary>Строит обычное волоконное сечение из параметров типовой формы.</summary>
public static class ParametricRcSectionGenerator
{
    const int CircleSegments = 32;

    /// <summary>Материализует параметры; ошибочный ввод не создаёт частичную геометрию.</summary>
    public static ParametricRcGenerationResult Generate(ParametricRcSectionDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var errors = Validate(definition);
        if (errors.Count != 0)
            return new(new CrossSection { Tag = definition.Tag }, errors);

        var concrete = new MaterialArea { Tag = "Бетон", Category = AreaCategory.Region };
        concrete.Hull = ClosedContour(OuterPoints(definition), "контур бетона", ContourType.Hull);
        if (definition.Shape == ParametricRcShape.Annulus)
            concrete.Contours.Add(ClosedContour(TemplatePoints.CirclePoints(definition.InnerDiameterM, CircleSegments), "отверстие", ContourType.Hole));
        concrete.SetWKT();

        var section = new CrossSection { Tag = definition.Tag, Areas = [concrete] };
        if (definition.Shape is ParametricRcShape.Circle or ParametricRcShape.Annulus)
            AddPolarRebar(section, concrete, definition.PolarRebar!);
        else
        {
            AddLayer(section, concrete, definition.LowerRebar, definition.WidthM, "Нижняя арматура");
            AddLayer(section, concrete, definition.UpperRebar, definition.WidthM, "Верхняя арматура");
        }
        return new(section, []);
    }

    static List<string> Validate(ParametricRcSectionDefinition d)
    {
        var errors = new List<string>();
        if (!(d.WidthM > 0) || !(d.HeightM > 0)) errors.Add("Габаритные размеры должны быть положительными.");
        if (d.Shape == ParametricRcShape.Annulus && !(d.InnerDiameterM > 0 && d.InnerDiameterM < d.WidthM))
            errors.Add("Внутренний диаметр кольца должен быть положительным и меньше наружного.");
        if (d.Shape is ParametricRcShape.Tee or ParametricRcShape.IBeam &&
            (!(d.WebThicknessM > 0) || d.WebThicknessM >= d.WidthM || !(d.FlangeThicknessM > 0) || 2 * d.FlangeThicknessM >= d.HeightM))
            errors.Add("Размеры стенки и полок несовместимы с габаритами.");
        foreach (var layer in new[] { d.UpperRebar, d.LowerRebar }.Where(x => x?.Enabled == true))
        {
            if (!(layer!.DiameterM > 0)) errors.Add("Диаметр продольной арматуры должен быть положительным.");
            if (layer.IsIdealized && (!(layer.AreaM2 > 0) || layer.Axis is null)) errors.Add("Расчётный слой требует площадь и ось изгиба.");
            if (!layer.IsIdealized && layer.Count < 1) errors.Add("Число физических стержней должно быть не менее одного.");
        }
        if (d.Shape is ParametricRcShape.Circle or ParametricRcShape.Annulus)
        {
            var bars = d.PolarRebar;
            if (bars is null || bars.Count < 7 || !(bars.DiameterM > 0) || !(bars.RadiusM > 0))
                errors.Add("Круг и кольцо требуют минимум семь физических стержней с положительными диаметром и радиусом.");
        }
        return errors;
    }

    static List<(double X, double Y)> OuterPoints(ParametricRcSectionDefinition d) => d.Shape switch
    {
        ParametricRcShape.Rectangle => TemplatePoints.RectPoints(d.WidthM, d.HeightM),
        ParametricRcShape.Tee => TemplatePoints.TeePoints(d.WidthM, d.HeightM, d.WebThicknessM, d.FlangeThicknessM),
        ParametricRcShape.IBeam => TemplatePoints.IBeamPoints(d.HeightM, d.WidthM, d.WebThicknessM, d.FlangeThicknessM),
        ParametricRcShape.Circle or ParametricRcShape.Annulus => TemplatePoints.CirclePoints(d.WidthM, CircleSegments),
        _ => throw new ArgumentOutOfRangeException()
    };

    static Contour ClosedContour(IEnumerable<(double X, double Y)> source, string tag, ContourType type)
    {
        var points = source.ToList();
        points.Add(points[0]);
        return new Contour(points.Select(p => p.X), points.Select(p => p.Y), tag) { Type = type };
    }

    static void AddLayer(CrossSection section, MaterialArea concrete, ParametricLongitudinalLayer? layer, double width, string tag)
    {
        if (layer?.Enabled != true) return;
        var area = new MaterialArea
        {
            Tag = tag, Category = AreaCategory.RebarGroup, HostArea = concrete,
            RebarRepresentation = layer.IsIdealized ? RebarRepresentation.IdealizedLayer : RebarRepresentation.PhysicalBars,
            IdealizedAxis = layer.Axis
        };
        if (layer.IsIdealized)
            area.Fibers.Add(Bar(0, layer.CoordinateM, layer.AreaM2, layer.DiameterM));
        else
        {
            double start = -width * 0.35, step = layer.Count == 1 ? 0 : width * 0.70 / (layer.Count - 1);
            double a = Math.PI * layer.DiameterM * layer.DiameterM / 4.0;
            for (int i = 0; i < layer.Count; i++) area.Fibers.Add(Bar(start + i * step, layer.CoordinateM, a, layer.DiameterM));
        }
        section.Areas.Add(area);
    }

    static void AddPolarRebar(CrossSection section, MaterialArea concrete, ParametricPolarRebar bars)
    {
        var area = new MaterialArea { Tag = "Продольная арматура", Category = AreaCategory.RebarGroup, HostArea = concrete };
        double a = Math.PI * bars.DiameterM * bars.DiameterM / 4.0;
        for (int i = 0; i < bars.Count; i++)
        {
            double angle = 2 * Math.PI * i / bars.Count;
            area.Fibers.Add(Bar(bars.RadiusM * Math.Cos(angle), bars.RadiusM * Math.Sin(angle), a, bars.DiameterM));
        }
        section.Areas.Add(area);
    }

    static Fiber Bar(double x, double y, double area, double diameter) => new(x, y)
    {
        TypeFiber = FiberType.point, Area = area, Diameter = diameter
    };
}
