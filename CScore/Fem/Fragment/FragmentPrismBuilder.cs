using System.Globalization;
using System.Text.Json;
using CScore.Planar;

namespace CScore.Fem.Fragment;

/// <summary>Призма по выбранным элементам или отказ (диагностика с ошибкой).</summary>
public sealed record FragmentPrismResult(FragmentPrism? Prism, IReadOnlyList<FemValidationDiagnostic> Diagnostics);

/// <summary>
/// Призма фрагмента по выбранным вертикальным колоннам и стенам (спека <c>2026-10-30-fem-element-fragment-prism-design.md</c>,
/// разд. 2.1): у одного элемента — прямоугольник по нему (габарит сечения колонны в его осях или стена с толщиной) с
/// отступом d с каждой стороны; у нескольких — габарит их прямоугольников в глобальных X/Y. Чистый класс, без БД.
/// </summary>
public static class FragmentPrismBuilder
{
    /// <summary>|Δz|/L, начиная с которого стержень считается колонной.</summary>
    public const double ColumnSine = 0.9;

    /// <summary>|LocalZ.Z| области, до которого она считается вертикальной стеной.</summary>
    public const double WallNormalZ = 1e-4;

    /// <summary>Призма по КонЭ <paramref name="memberTags"/> родителя с отступом <paramref name="d"/>, м.</summary>
    public static FragmentPrismResult Build(StoreyFragmentParentSnapshot parent, IReadOnlyCollection<string> memberTags, double d, double tol = 0.001)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(memberTags);
        var diagnostics = new List<FemValidationDiagnostic>();
        void Error(string code, string message, params string[] keys) => diagnostics.Add(new(code, message, true, keys));
        if (!(d > 0))
        {
            Error("fragment_prism_offset_invalid", $"Отступ d = {d:0.###} м должен быть больше нуля.");
            return new(null, diagnostics);
        }
        if (memberTags.Count == 0)
        {
            Error("fragment_prism_empty", "Не выбрано ни одной колонны или стены.");
            return new(null, diagnostics);
        }

        var memberByTag = parent.Members.GroupBy(m => m.ElemTag).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var nodeByTag = parent.Nodes.GroupBy(n => n.NodeTag).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var regionById = parent.Regions.Where(r => r.Id > 0).GroupBy(r => r.Id).ToDictionary(g => g.Key, g => g.First());
        var rectangles = new List<IReadOnlyList<PlanarPoint2D>>();
        FragmentPrism? single = null;
        foreach (var tag in memberTags)
        {
            if (!memberByTag.TryGetValue(tag, out var m))
            {
                Error("fragment_prism_member_missing", $"Элемент {tag} не найден в схеме.", tag);
                continue;
            }
            FragmentPrism? prism = null;
            if (m.ElemType == "beam" && Ends(m, nodeByTag) is { } e && IsColumn(e.A, e.B))
                prism = Column(m, e.A, e.B, SectionId(parent, m), parent.SectionOutline, d, tol);
            else if (m.ElemType == "shell" && m.PlanarRegionId is int id && regionById.TryGetValue(id, out var region)
                     && Math.Abs(region.Frame.LocalZ.Z) <= WallNormalZ)
                prism = Wall(region, m.ThicknessM ?? 0, d);
            if (prism is null)
            {
                Error("fragment_prism_not_vertical",
                    $"Элемент {tag} — не вертикальная колонна и не вертикальная стена: фрагмент строится только вокруг них.", tag);
                continue;
            }
            single = prism;
            rectangles.Add(prism.Corners());
        }
        if (diagnostics.Any(x => x.IsError)) return new(null, diagnostics);
        if (rectangles.Count == 1) return new(single, diagnostics);
        return new(Bounds(rectangles.SelectMany(r => r)), diagnostics);
    }

    /// <summary>Стержень — колонна: |Δz|/L ≥ <see cref="ColumnSine"/>.</summary>
    public static bool IsColumn(PlanarVector3 a, PlanarVector3 b)
    {
        double len = (b - a).Length;
        return len > 1e-12 && Math.Abs(b.Z - a.Z) / len >= ColumnSine;
    }

    /// <summary>
    /// Прямоугольник колонны в плане с отступом <paramref name="d"/>: габарит контура сечения в осях стержня, повёрнутый
    /// по <see cref="FemMember.RotationDeg"/>; сечения нет — квадрат 2d × 2d вокруг оси. Снос оси в плане больше допуска —
    /// габарит прямоугольников у обоих концов в глобальных X/Y.
    /// </summary>
    public static FragmentPrism Column(
        FemMember member, PlanarVector3 a, PlanarVector3 b, int? sectionId,
        Func<int, IReadOnlyList<(double Y, double Z)>?>? sectionOutline, double d, double tol)
    {
        var outline = sectionId is int sid ? sectionOutline?.Invoke(sid) : null;
        if (outline is not { Count: >= 3 })
        {
            var square = Bounds([new(a.X, a.Y), new(b.X, b.Y)]);
            return square with { HalfLength = square.HalfLength + d, HalfWidth = square.HalfWidth + d };
        }
        var (_, y, z) = BeamLocalAxisConvention.Frame(a, b, member.RotationDeg);
        double y0 = outline.Min(p => p.Y), y1 = outline.Max(p => p.Y), z0 = outline.Min(p => p.Z), z1 = outline.Max(p => p.Z);
        FragmentPrism At(PlanarVector3 o)
        {
            // Центр габарита сечения и его оси, спроецированные на план (у вертикальной колонны — точно).
            var c = o + y * ((y0 + y1) / 2) + z * ((z0 + z1) / 2);
            return FragmentPrism.Create(c.X, c.Y, y.X, y.Y, (y1 - y0) / 2 + d, (z1 - z0) / 2 + d);
        }
        double drift = Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
        return drift <= tol ? At(a) : Bounds(At(a).Corners().Concat(At(b).Corners()));
    }

    /// <summary>Прямоугольник вертикальной стены в плане: вдоль стены — протяжённость контура, поперёк — толщина; + d.</summary>
    public static FragmentPrism Wall(PlanarRegion region, double thickness, double d)
    {
        var f = region.Frame;
        var n = new PlanarVector3(f.LocalZ.X, f.LocalZ.Y, 0);
        double nl = n.Length;
        n = nl < 1e-12 ? new PlanarVector3(0, 1, 0) : n * (1 / nl);
        var t = new PlanarVector3(-n.Y, n.X, 0);
        var hull = region.RequireHull();
        var s = Enumerable.Range(0, hull.X.Count)
            .Select(i => PlanarBoundaryFrameConverter.ToGlobalPoint(f, new PlanarVector3(hull.X[i], hull.Y[i], 0)))
            .Select(p => p.X * t.X + p.Y * t.Y).ToList();
        double s0 = s.Min(), s1 = s.Max(), offset = f.Origin.X * n.X + f.Origin.Y * n.Y, mid = (s0 + s1) / 2;
        return FragmentPrism.Create(t.X * mid + n.X * offset, t.Y * mid + n.Y * offset, t.X, t.Y, (s1 - s0) / 2 + d, thickness / 2 + d);
    }

    /// <summary>Сечение колонны: у КонЭ, иначе — у КЭ родителя этого КонЭ (по большинству КЭ).</summary>
    public static int? SectionId(StoreyFragmentParentSnapshot parent, FemMember member) =>
        member.CrossSectionId ?? parent.MeshElements
            .Where(e => e.ElemType == "beam" && e.SourceMemberTag == member.ElemTag && e.CrossSectionId is not null)
            .GroupBy(e => e.CrossSectionId!.Value).OrderByDescending(g => g.Count()).ThenBy(g => g.Key)
            .Select(g => (int?)g.Key).FirstOrDefault();

    /// <summary>Габарит точек в глобальных X/Y.</summary>
    static FragmentPrism Bounds(IEnumerable<PlanarPoint2D> points)
    {
        var list = points.ToList();
        double x0 = list.Min(p => p.U), x1 = list.Max(p => p.U), y0 = list.Min(p => p.V), y1 = list.Max(p => p.V);
        return FragmentPrism.Create((x0 + x1) / 2, (y0 + y1) / 2, 1, 0, (x1 - x0) / 2, (y1 - y0) / 2);
    }

    static (PlanarVector3 A, PlanarVector3 B)? Ends(FemMember m, Dictionary<string, FemNode> nodeByTag)
    {
        int[] ids;
        try { ids = JsonSerializer.Deserialize<int[]>(m.NodeIdsJson) ?? []; }
        catch (JsonException) { return null; }
        if (ids.Length < 2
            || !nodeByTag.TryGetValue(ids[0].ToString(CultureInfo.InvariantCulture), out var a)
            || !nodeByTag.TryGetValue(ids[^1].ToString(CultureInfo.InvariantCulture), out var b)) return null;
        return (new(a.X, a.Y, a.Z), new(b.X, b.Y, b.Z));
    }
}
