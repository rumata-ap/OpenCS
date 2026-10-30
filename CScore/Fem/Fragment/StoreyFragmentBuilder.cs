using System.Globalization;
using System.Text.Json;
using CScore.Fem.Editing;
using CScore.Planar;

namespace CScore.Fem.Fragment;

/// <summary>
/// Построитель фрагмента этажа (спека <c>2026-10-23-fem-storey-fragment-builder-design.md</c>): из родителя по полосе
/// z₁…z₂ — новые КонЭ (стержни обрезаны и поделены на плитах, стены клипнуты и поделены по плитам, плиты скопированы,
/// КЭ без КонЭ собраны), сечения рассечения, импортные АЖТ → явный след. Чистое ядро, без БД.
/// </summary>
public static partial class StoreyFragmentBuilder
{
    /// <summary>|Δz|/L, ниже которого рассечённый стержень «почти лежит» в плоскости рассечения.</summary>
    public const double ShallowCutSine = 0.1;

    sealed record SourceMember(FemMember Member, PlanarRegion? Region, PlanarVector3 A, PlanarVector3 B, int DofA, int DofB);

    sealed record Slab(string MemberTag, PlanarRegion Region, double Z);

    /// <summary>Плоскость, ограничивающая фрагмент: внешняя нормаль, смещение (внутри — n·p ≤ смещения), сторона разреза.</summary>
    sealed record CutPlane(PlanarVector3 Normal, double Offset, FragmentCutSide Side)
    {
        /// <summary>Отстояние со знаком: больше нуля — снаружи фрагмента.</summary>
        public double Eval(PlanarVector3 p) => Normal.Dot(p) - Offset;
    }

    sealed class Ctx(StoreyFragmentRequest request, StoreyFragmentDraft draft)
    {
        public double Z1 => request.Z1;
        public double Z2 => request.Z2;
        public double Tol => request.ToleranceM;
        public FragmentPrism? Prism => request.Prism;
        /// <summary>z₁, z₂ и боковые грани призмы (если она задана).</summary>
        public IReadOnlyList<CutPlane> Planes { get; } =
        [
            new(new PlanarVector3(0, 0, -1), -request.Z1, FragmentCutSide.Bottom),
            new(new PlanarVector3(0, 0, 1), request.Z2, FragmentCutSide.Top),
            .. request.Prism?.Faces.Select(f => new CutPlane(f.Normal, f.Offset, FragmentCutSide.Lateral)) ?? [],
        ];
        public IEnumerable<CutPlane> Lateral => Planes.Where(p => p.Side == FragmentCutSide.Lateral);
        /// <summary>Точка внутри фрагмента (полоса и призма) или на его границе в допуске.</summary>
        public bool InBox(PlanarVector3 p) => Planes.All(pl => pl.Eval(p) <= Tol);
        public StoreyFragmentDraft Draft => draft;
        public FragmentNodeTable Nodes { get; } = new(draft.Nodes);
        public List<Slab> Slabs { get; } = [];
        /// <summary>Стержневые КЭ родителя по тегу КонЭ: концы и сечение — источник сечения частей фрагмента.</summary>
        public Dictionary<string, List<(PlanarVector3 A, PlanarVector3 B, int? SectionId)>> ParentBars { get; set; } = [];

        public void Error(string code, string message, params string[] keys) => draft.Diagnostics.Add(new(code, message, true, keys));
        public void Warn(string code, string message, params string[] keys) => draft.Diagnostics.Add(new(code, message, false, keys));
        /// <summary>Контур сечения по Id из снимка (правило (а): габарит колонны у грани призмы).</summary>
        public Func<int, IReadOnlyList<(double Y, double Z)>?>? SectionOutline { get; set; }
        public bool OnCutPlane(double z) => Math.Abs(z - Z1) <= Tol || Math.Abs(z - Z2) <= Tol;
        public void Add(FemMember source, FemMember part, PlanarRegion? region) => draft.Members.Add(new(part, region, source.ElemTag));
    }

    /// <summary>Черновик фрагмента по полосе запроса; при отказе — черновик с ошибкой в диагностике.</summary>
    public static StoreyFragmentDraft Build(StoreyFragmentParentSnapshot parent, StoreyFragmentRequest request)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(request);
        var draft = new StoreyFragmentDraft();
        var c = new Ctx(request, draft) { SectionOutline = parent.SectionOutline };
        if (request.Z2 - request.Z1 <= request.ToleranceM)
        {
            c.Error("fragment_band_empty", $"Отметка z₂ = {request.Z2:0.###} м должна быть выше z₁ = {request.Z1:0.###} м.");
            return draft;
        }
        if (parent.MeshElements.Count == 0)
        {
            c.Error("fragment_parent_no_mesh", "У схемы-родителя нет сетки — фрагмент не строится.");
            return draft;
        }

        var sources = Resolve(c, parent);
        c.ParentBars = ParentBars(parent);
        draft.ParentMeshStepM = ParentMeshStep(c, parent);
        sources.AddRange(Uncovered(c, parent));
        foreach (var s in sources.Where(IsHorizontalShell)) AddSlab(c, s);
        if (draft.HasErrors) return draft;
        if (c.Slabs.Count == 0)
        {
            c.Error("fragment_no_slab", "В полосе нет ни одной горизонтальной плиты — сдвиньте отметки z₁/z₂.");
            return draft;
        }
        foreach (var s in sources.Where(s => s.Member.ElemType == "beam")) CutBar(c, s);
        foreach (var s in sources.Where(s => s.Region is not null && !IsHorizontalShell(s))) CutWall(c, s);
        if (draft.HasErrors) return draft;
        AddSupportNodes(c, parent);
        TransferBoundary(c, parent);
        TransferRigidBodies(c, parent);
        ReportNotTransferred(c, parent);
        TransferLoads(c, parent);
        return draft;
    }

    static List<SourceMember> Resolve(Ctx c, StoreyFragmentParentSnapshot parent)
    {
        var nodeByTag = parent.Nodes.GroupBy(n => n.NodeTag).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var regionById = parent.Regions.Where(r => r.Id > 0).GroupBy(r => r.Id).ToDictionary(g => g.Key, g => g.First());
        var result = new List<SourceMember>();
        foreach (var m in parent.Members)
        {
            if (m.ElemType == "beam")
            {
                var ids = NodeTags(m);
                if (ids.Length < 2
                    || !nodeByTag.TryGetValue(ids[0].ToString(CultureInfo.InvariantCulture), out var a)
                    || !nodeByTag.TryGetValue(ids[^1].ToString(CultureInfo.InvariantCulture), out var b))
                {
                    c.Warn("fragment_member_broken", $"У стержня {m.ElemTag} нет узлов — пропущен.", m.ElemTag);
                    continue;
                }
                result.Add(new(m, null, P(a), P(b), a.DofMask, b.DofMask));
            }
            else if (m.ElemType == "shell" && m.PlanarRegionId is int id && regionById.TryGetValue(id, out var region))
                result.Add(new(m, region, default, default, 0, 0));
            else if (m.ElemType == "shell")
                c.Warn("fragment_member_broken", $"У пластины {m.ElemTag} нет области — пропущена.", m.ElemTag);
        }
        return result;
    }

    static bool IsHorizontalShell(SourceMember s) => s.Region is not null && Math.Abs(s.Region.Frame.LocalZ.Z) > 1 - 1e-6;

    /// <summary>Горизонтальная плита: на границе полосы — отказ, внутри (и внутри призмы) — копия как есть, иначе — обрезка призмой.</summary>
    static void AddSlab(Ctx c, SourceMember s)
    {
        var source = s.Region!;
        double z = source.Frame.Origin.Z;
        if (c.OnCutPlane(z))
        {
            c.Error("fragment_member_on_cut_plane",
                $"Плита {s.Member.ElemTag} на отметке {z:0.###} м лежит на границе полосы — сдвиньте z₁/z₂ к середине этажа.",
                s.Member.ElemTag);
            return;
        }
        if (z < c.Z1 || z > c.Z2) return;
        if (!InsidePrism(c, source))
        {
            ClipSlab(c, s, z);
            return;
        }
        string tag = s.Member.ElemTag;
        var region = NewRegion(source, tag, Points(source.RequireHull()), source.Holes.Select(Points).ToList());
        region.RebarZones.AddRange(source.RebarZones.Select(zone => zone.Clone()));
        region.BoundarySegments.AddRange(source.BoundarySegments);
        region.ConstraintObjects.AddRange(source.ConstraintObjects);
        region.RecalcFingerprint();
        c.Add(s.Member, CopyPart(s.Member, tag, []), region);
        c.Slabs.Add(new(tag, region, z));
    }

    /// <summary>Закреплённые узлы родителя в полосе переходят во фрагмент.</summary>
    static void AddSupportNodes(Ctx c, StoreyFragmentParentSnapshot parent)
    {
        foreach (var n in parent.Nodes.Where(n => n.DofMask != 0 && c.InBox(P(n))))
            c.Nodes.Get(P(n), n.DofMask);
    }

    // ---------------- помощники ----------------

    static PlanarVector3 P(FemNode n) => new(n.X, n.Y, n.Z);

    static PlanarVector3 P(FemMeshNode n) => new(n.X, n.Y, n.Z);

    static PlanarVector3 At(PlanarVector3 a, PlanarVector3 d, double t) => new(a.X + d.X * t, a.Y + d.Y * t, a.Z + d.Z * t);

    static int[] NodeTags(FemMember m) => JsonSerializer.Deserialize<int[]>(m.NodeIdsJson) ?? [];

    /// <summary>Копия КонЭ для фрагмента: новые тег и узлы, сетка — своя; явный след АЖТ сохраняется (положение то же).</summary>
    static FemMember CopyPart(FemMember source, string tag, int[] nodeTags)
    {
        var part = PasteFragmentCommand.CopyOf(source, 0, tag, nodeTags);
        part.RigidJunctionTraceJson = source.RigidJunctionTraceJson;
        return part;
    }

    /// <summary>Вершины контура без повтора первой.</summary>
    static List<PlanarPoint2D> Points(Contour contour)
    {
        int n = contour.X.Count;
        if (n > 1 && Math.Abs(contour.X[0] - contour.X[n - 1]) < 1e-12 && Math.Abs(contour.Y[0] - contour.Y[n - 1]) < 1e-12) n--;
        return Enumerable.Range(0, n).Select(i => new PlanarPoint2D(contour.X[i], contour.Y[i])).ToList();
    }

    /// <summary>Замкнутый контур из открытого списка вершин (допускается треугольник — в обход конструктора Contour).</summary>
    static Contour ToContour(IReadOnlyList<PlanarPoint2D> points, string tag)
    {
        var contour = new Contour
        {
            Tag = tag,
            X = points.Select(p => p.U).Append(points[0].U).ToList(),
            Y = points.Select(p => p.V).Append(points[0].V).ToList(),
        };
        contour.SetWKT();
        contour.Points = contour.XYsToPoints();
        return contour;
    }

    /// <summary>Новая область в рамке <paramref name="source"/> с её шагами сетки и раскладки (без зон и ограничений).</summary>
    static PlanarRegion NewRegion(
        PlanarRegion source, string tag, IReadOnlyList<PlanarPoint2D> outer, IReadOnlyList<IReadOnlyList<PlanarPoint2D>> holes)
    {
        var region = PlanarRegion.CreateFromContour(ToContour(outer, tag), holes.Select(h => ToContour(h, tag)), source.Frame, tag);
        region.MeshMaxElementSizeM = source.MeshMaxElementSizeM;
        region.RebarSectionGridStep = source.RebarSectionGridStep;
        return region;
    }

    /// <summary>Точка в плоскости области, внутри внешнего контура (или на нём) и не строго внутри отверстия.</summary>
    static bool RegionContains(PlanarRegion region, PlanarVector3 point, double tol)
    {
        var local = PlanarBoundaryFrameConverter.ToLocalPoint(region.Frame, point);
        if (Math.Abs(local.Z) > tol) return false;
        var q = new PlanarPoint2D(local.X, local.Y);
        return PlanarPolygonOps.Contains(Points(region.RequireHull()), q, tol)
               && !region.Holes.Any(h => PlanarPolygonOps.Contains(Points(h), q, -tol));
    }

    /// <summary>Имена частей: одна — тег; две по высоте — «· низ»/«· верх»; иначе «· 1…n».</summary>
    static string[] PartNames(string tag, int count, bool twoLevels) => count switch
    {
        1 => [tag],
        2 when twoLevels => [$"{tag} · низ", $"{tag} · верх"],
        _ => Enumerable.Range(1, count).Select(k => $"{tag} · {k}").ToArray(),
    };
}
