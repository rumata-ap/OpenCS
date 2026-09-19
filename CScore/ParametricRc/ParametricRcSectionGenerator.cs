namespace CScore.ParametricRc;

/// <summary>Точная зона типовой формы, использованная и для preview, и для срезов.</summary>
public sealed record ParametricRcZone(
    ParametricStirrupZone Kind,
    IReadOnlyList<(double X, double Y)> Polygon,
    double MinX, double MaxX, double MinY, double MaxY)
{
    /// <summary>Проверяет, что зона имеет непустой прямоугольник для срезов.</summary>
    public bool IsUsable => MaxX > MinX && MaxY > MinY;
}

/// <summary>Результат материализации параметрического источника.</summary>
public sealed record ParametricRcGenerationResult(
    CrossSection Section, IReadOnlyList<string> Diagnostics)
{
    /// <summary>Точная карта зон, полученная из того же контура, что и бетон.</summary>
    public IReadOnlyDictionary<ParametricStirrupZone, ParametricRcZone> ZoneMap { get; init; } =
        new Dictionary<ParametricStirrupZone, ParametricRcZone>();

    /// <summary>Области, созданные генератором и предназначенные для junction.</summary>
    public IReadOnlyList<MaterialArea> GeneratedAreas { get; init; } = [];
}

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
        var zones = BuildZones(definition);
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
            AddStirrupCuts(section, definition);
        }
        return new(section, [])
        {
            ZoneMap = zones,
            GeneratedAreas = section.Areas.ToArray()
        };
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
            if (d.UpperRebar?.Enabled == true || d.LowerRebar?.Enabled == true)
                errors.Add("Для круга и кольца допускается только физическая равномерная полярная арматура.");
            var bars = d.PolarRebar;
            if (bars is null || bars.Count < 7 || !(bars.DiameterM > 0) || !(bars.RadiusM > 0))
                errors.Add("Круг и кольцо требуют минимум семь физических стержней с положительными диаметром и радиусом.");
            else if (bars.RadiusM + bars.DiameterM / 2 >= d.WidthM / 2 ||
                     (d.Shape == ParametricRcShape.Annulus && bars.RadiusM - bars.DiameterM / 2 <= d.InnerDiameterM / 2))
                errors.Add("Полярные стержни должны находиться в бетоне между наружной и внутренней гранями.");
        }
        else
        {
            foreach (var layer in new[] { d.UpperRebar, d.LowerRebar }.Where(x => x?.Enabled == true))
            {
                double half = layer!.DiameterM / 2;
                double limit = layer.Axis == IdealizedRebarAxis.My ? d.WidthM / 2 : d.HeightM / 2;
                if (Math.Abs(layer.CoordinateM) + half > limit)
                    errors.Add("Продольный слой выходит за габариты бетонного сечения.");
            }
        }
        foreach (var cut in d.StirrupCuts)
        {
            if (cut.Count < 0)
            {
                errors.Add($"Зона {cut.Zone}, направление {cut.Direction}: число срезов не может быть отрицательным.");
                continue;
            }
            if (cut.Count == 0) continue;
            if (d.Shape is ParametricRcShape.Circle or ParametricRcShape.Annulus)
            {
                errors.Add($"Зона {cut.Zone}, направление {cut.Direction}: поперечная арматура для круга и кольца недоступна.");
                continue;
            }
            if (!(cut.DiameterM > 0) || !(cut.SpacingM > 0) || cut.MaterialId <= 0 || !(cut.CoverM > 0))
                errors.Add($"Зона {cut.Zone}, направление {cut.Direction}: нужны положительные диаметр, шаг и защитный слой, а также материал.");
            if (BuildZones(d).TryGetValue(cut.Zone, out var zone))
            {
                double clearWidth = zone.MaxX - zone.MinX - 2 * cut.CoverM;
                double clearHeight = zone.MaxY - zone.MinY - 2 * cut.CoverM;
                if (clearWidth <= 0 || clearHeight <= 0)
                    errors.Add($"Зона {cut.Zone}, направление {cut.Direction}: защитный слой оставляет непригодный clear rectangle.");
            }
            else
                errors.Add($"Зона {cut.Zone}, направление {cut.Direction}: зона недоступна для формы {d.Shape}.");
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
            HostAreaId = concrete.Id,
            RebarRepresentation = layer.IsIdealized ? RebarRepresentation.IdealizedLayer : RebarRepresentation.PhysicalBars,
            IdealizedAxis = layer.Axis
        };
        if (layer.IsIdealized)
            area.Fibers.Add(layer.Axis == IdealizedRebarAxis.My
                ? Bar(layer.CoordinateM, 0, layer.AreaM2, layer.DiameterM)
                : Bar(0, layer.CoordinateM, layer.AreaM2, layer.DiameterM));
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
        var area = new MaterialArea { Tag = "Продольная арматура", Category = AreaCategory.RebarGroup, HostArea = concrete, HostAreaId = concrete.Id };
        double a = Math.PI * bars.DiameterM * bars.DiameterM / 4.0;
        for (int i = 0; i < bars.Count; i++)
        {
            double angle = 2 * Math.PI * i / bars.Count;
            area.Fibers.Add(Bar(bars.RadiusM * Math.Cos(angle), bars.RadiusM * Math.Sin(angle), a, bars.DiameterM));
        }
        section.Areas.Add(area);
    }

    static Dictionary<ParametricStirrupZone, ParametricRcZone> BuildZones(
        ParametricRcSectionDefinition definition)
    {
        var outer = OuterPoints(definition);
        double minX = outer.Min(p => p.X), maxX = outer.Max(p => p.X);
        double minY = outer.Min(p => p.Y), maxY = outer.Max(p => p.Y);
        double webHalf = definition.WebThicknessM / 2.0;
        var zones = new Dictionary<ParametricStirrupZone, ParametricRcZone>();

        void Add(ParametricStirrupZone kind, double x0, double x1,
            double y0, double y1)
        {
            var polygon = new List<(double X, double Y)>
            {
                (x0, y0), (x1, y0), (x1, y1), (x0, y1), (x0, y0)
            };
            zones[kind] = new(kind, polygon, x0, x1, y0, y1);
        }

        switch (definition.Shape)
        {
            case ParametricRcShape.Rectangle:
                Add(ParametricStirrupZone.Body, minX, maxX, minY, maxY);
                break;
            case ParametricRcShape.Tee:
                Add(ParametricStirrupZone.Flange, minX, maxX,
                    maxY - definition.FlangeThicknessM, maxY);
                Add(ParametricStirrupZone.Web, -webHalf, webHalf,
                    minY, maxY - definition.FlangeThicknessM);
                break;
            case ParametricRcShape.IBeam:
                Add(ParametricStirrupZone.BottomFlange, minX, maxX,
                    minY, minY + definition.FlangeThicknessM);
                Add(ParametricStirrupZone.Web, -webHalf, webHalf,
                    minY + definition.FlangeThicknessM,
                    maxY - definition.FlangeThicknessM);
                Add(ParametricStirrupZone.TopFlange, minX, maxX,
                    maxY - definition.FlangeThicknessM, maxY);
                break;
        }
        return zones;
    }

    static void AddStirrupCuts(CrossSection section, ParametricRcSectionDefinition definition)
    {
        var zones = BuildZones(definition);
        foreach (var set in definition.StirrupCuts.Where(x => x.Count > 0))
        {
            if (!zones.TryGetValue(set.Zone, out var zone) || !zone.IsUsable) continue;
            double minX = zone.MinX + set.CoverM, maxX = zone.MaxX - set.CoverM;
            double minY = zone.MinY + set.CoverM, maxY = zone.MaxY - set.CoverM;
            if (maxX <= minX || maxY <= minY) continue;
            var area = section.Areas.FirstOrDefault(a => a.Category == AreaCategory.Stirrups && a.MaterialId == set.MaterialId);
            if (area is null)
            {
                area = new MaterialArea { Tag = "Поперечная арматура", Category = AreaCategory.Stirrups, MaterialId = set.MaterialId };
                section.Areas.Add(area);
            }
            var group = new StirrupGroup { MaterialId = set.MaterialId, SpacingM = set.SpacingM };
            for (int i = 0; i < set.Count; i++)
            {
                double ratio = (i + 1.0) / (set.Count + 1.0);
                double x0, y0, x1, y1;
                if (set.Direction == ParametricStirrupDirection.Vertical)
                {
                    x0 = x1 = minX + ratio * (maxX - minX); y0 = minY; y1 = maxY;
                }
                else { x0 = minX; x1 = maxX; y0 = y1 = minY + ratio * (maxY - minY); }
                group.Elements.Add(new StirrupElement
                {
                    CenterlineContour = Contour.Polyline([x0, x1], [y0, y1], "срез"),
                    BarDiameterM = set.DiameterM,
                    BarAreaM2 = Math.PI * set.DiameterM * set.DiameterM / 4.0,
                    Source = new StirrupElementSource
                    {
                        Kind = StirrupElementKind.Cut,
                        Direction = set.Direction == ParametricStirrupDirection.Vertical ? StirrupCutDirection.Vertical : StirrupCutDirection.Horizontal,
                        Position = set.Direction == ParametricStirrupDirection.Vertical ? x0 : y0,
                        OffsetM = set.CoverM
                    }
                });
            }
            area.Stirrups.Add(group);
        }
    }

    static Fiber Bar(double x, double y, double area, double diameter) => new(x, y)
    {
        TypeFiber = FiberType.point, Area = area, Diameter = diameter
    };
}
