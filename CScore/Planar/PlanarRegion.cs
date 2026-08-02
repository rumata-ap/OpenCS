using CScore.PlateRebar;

namespace CScore.Planar;

/// <summary>Нормализованный плоский конструктивный объект (плита/стена), независимый от
/// конкретной КЭ-сетки. Геометрия копируется внутрь при создании — живой ссылки на исходный
/// Contour нет, SourceContourId хранится только для provenance.</summary>
public sealed class PlanarRegion
{
    public int Id { get; set; }
    public string Tag { get; set; } = "";
    public List<Contour> Contours { get; set; } = [];
    public Frame3D Frame { get; set; } = Frame3D.Identity;
    public bool FrameIsRecovered { get; set; }
    public List<BoundarySegment> BoundarySegments { get; set; } = [];
    /// <summary>Внутренние mesh-loci и будущие structural constraints региона.</summary>
    public List<PlanarConstraintObject> ConstraintObjects { get; set; } = [];
    public int? SourceContourId { get; set; }
    public string GeometryFingerprint { get; set; } = "";

    /// <summary>Пространственные зоны армирования (PlateRebarField.Zones) — привязаны к
    /// геометрии региона, а не к переиспользуемому PlateSection.</summary>
    public List<RebarZone> RebarZones { get; set; } = [];

    /// <summary>Шаг (в метрах) пробной ортогональной сетки точек, используемой для генерации
    /// секций-комбинаций армирования (срез 4) — приближение будущей триангуляции.</summary>
    public double RebarSectionGridStep { get; set; } = 0.3;

    /// <summary>Характерный размер элемента Gmsh-сетки этого региона, м (срез 2 дорожной карты
    /// Gmsh). Единственная per-region настройка сетки — остальные (алгоритм, режим элементов,
    /// timeout) берутся из глобальных GmshSettings в момент построения.</summary>
    public double MeshMaxElementSizeM { get; set; } = 0.2;

    [System.Text.Json.Serialization.JsonIgnore]
    public Contour? Hull
    {
        get => Contours.FirstOrDefault(c => c.Type == ContourType.Hull);
        set
        {
            int idx = Contours.FindIndex(c => c.Type == ContourType.Hull);
            if (idx >= 0) Contours[idx] = value; else if (value != null) Contours.Insert(0, value);
        }
    }

    [System.Text.Json.Serialization.JsonIgnore]
    public IEnumerable<Contour> Holes => Contours.Where(c => c.Type == ContourType.Hole);

    public void RecalcFingerprint() =>
        GeometryFingerprint = PlanarGeometryFingerprint.Compute(Contours, Frame, BoundarySegments, ConstraintObjects);

    public static PlanarRegion CreateFromContour(
        Contour source, IEnumerable<Contour>? holes = null, Frame3D? frame = null, string tag = "")
    {
        ArgumentNullException.ThrowIfNull(source);
        PlanarRegionTopologyValidator.ValidateLoop(source.X, source.Y, "внешний контур");
        var (hx, hy) = PlanarRegionTopologyValidator.NormalizeWinding(source.X, source.Y, ccw: true);

        var region = new PlanarRegion
        {
            Tag = tag.Length > 0 ? tag : source.Tag,
            SourceContourId = source.Id > 0 ? source.Id : null
        };
        region.Contours.Add(BuildClosedContour(hx, hy, source.Tag, ContourType.Hull));

        foreach (var hole in holes ?? Enumerable.Empty<Contour>())
        {
            PlanarRegionTopologyValidator.ValidateLoop(hole.X, hole.Y, "контур отверстия");
            var (wx, wy) = PlanarRegionTopologyValidator.NormalizeWinding(hole.X, hole.Y, ccw: false);
            region.Contours.Add(BuildClosedContour(wx, wy, hole.Tag, ContourType.Hole));
        }

        if (frame is not null)
        {
            frame.Validate();
            region.Frame = frame;
            region.FrameIsRecovered = false;
        }
        else
        {
            region.Frame = Frame3D.Identity;
            region.FrameIsRecovered = true;
        }

        region.RecalcFingerprint();
        return region;
    }

    /// <summary>Строит явно замкнутый Contour (дублирует первую вершину в конце — как ожидают
    /// Contour.CalcArea/WktHelper.PolygonArea) из «открытого» массива вершин. В обход
    /// Contour(IEnumerable&lt;double&gt;, IEnumerable&lt;double&gt;, string) — тот требует ≥5 точек и
    /// отклоняет треугольники.</summary>
    static Contour BuildClosedContour(IReadOnlyList<double> openX, IReadOnlyList<double> openY, string tag, ContourType type)
    {
        var x = openX.ToList();
        var y = openY.ToList();
        x.Add(x[0]);
        y.Add(y[0]);

        var c = new Contour { Tag = tag, Type = type, X = x, Y = y };
        c.SetWKT();
        c.Points = c.XYsToPoints();
        return c;
    }
}
