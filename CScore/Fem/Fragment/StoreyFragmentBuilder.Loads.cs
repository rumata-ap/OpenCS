using CScore.Fem.Loads;
using CScore.Planar;

namespace CScore.Fem.Fragment;

public static partial class StoreyFragmentBuilder
{
    /// <summary>Виды нагрузок — разбивка проверки равнодействующих.</summary>
    static class LoadCategory
    {
        public const string Nodes = "узловые";
        public const string Bars = "стержни";
        public const string Uniform = "пластины: равномерная";
        public const string Nodal = "пластины: по узлам";
        public const string Edge = "пластины: на ребро";
        public const string Point = "пластины: сосредоточенная";
    }

    /// <summary>Главный вектор и главный момент относительно точки приведения.</summary>
    sealed class Resultant
    {
        public PlanarVector3 F = PlanarVector3.Zero;
        public PlanarVector3 M = PlanarVector3.Zero;

        /// <summary>Сила <paramref name="f"/> и момент <paramref name="m"/> в точке с радиус-вектором <paramref name="r"/>.</summary>
        public void Add(PlanarVector3 r, PlanarVector3 f, PlanarVector3 m)
        {
            F += f;
            M += r.Cross(f) + m;
        }

        /// <summary>Линейная погонная нагрузка на отрезке ra → rb (радиус-векторы): Симпсон точен (подынтегральное — квадратичное).</summary>
        public void AddLine(PlanarVector3 ra, PlanarVector3 rb, PlanarVector3 qa, PlanarVector3 qb)
        {
            double l = (rb - ra).Length;
            var rm = (ra + rb) * 0.5;
            var qm = (qa + qb) * 0.5;
            F += (qa + qm * 4 + qb) * (l / 6);
            M += (ra.Cross(qa) + rm.Cross(qm) * 4 + rb.Cross(qb)) * (l / 6);
        }

        public void Add(Resultant other)
        {
            F += other.F;
            M += other.M;
        }
    }

    /// <summary>Состояние переноса нагрузок: родитель, КЭ полосы, точка приведения, равнодействующие родителя, накопители.</summary>
    sealed class LoadCtx
    {
        readonly Dictionary<(int LoadCase, string NodeTag, string Category), FemNodeLoad> _nodeLoads = [];
        readonly Dictionary<string, (List<PlanarVector3> Polygon, string? Part)> _shellClip = new(StringComparer.Ordinal);
        readonly Dictionary<string, string?> _barPart = new(StringComparer.Ordinal);

        public LoadCtx(Ctx c, StoreyFragmentParentSnapshot parent)
        {
            C = c;
            Parent = parent;
            MeshNodes = parent.MeshNodes.GroupBy(n => n.NodeTag).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            Band = BandElements(c, parent);
            BandTags = Band.Select(e => e.ElemTag).ToHashSet(StringComparer.Ordinal);
            Mesh = new FemLoadMeshContext(parent.MeshNodes, parent.MeshElements, parent.Groups, null,
                parent.Members.GroupBy(m => m.ElemTag).ToDictionary(g => g.Key, g => g.First().RotationDeg, StringComparer.Ordinal));
            Bars = BarParts(c);
            var xy = Band.SelectMany(e => FemMeshTopology.ReadNodeTags(e) ?? [])
                .Select(t => MeshNodes.GetValueOrDefault(t)).Where(n => n is not null).Select(n => (n!.X, n.Y))
                .Concat(c.Draft.Nodes.Select(n => (n.X, n.Y))).ToList();
            double x0 = xy.Count == 0 ? 0 : xy.Min(p => p.X), x1 = xy.Count == 0 ? 0 : xy.Max(p => p.X);
            double y0 = xy.Count == 0 ? 0 : xy.Min(p => p.Y), y1 = xy.Count == 0 ? 0 : xy.Max(p => p.Y);
            Origin = new PlanarVector3((x0 + x1) / 2, (y0 + y1) / 2, (c.Z1 + c.Z2) / 2);
            Reach = Math.Max(1, 0.5 * Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0)));
        }

        public Ctx C { get; }
        public StoreyFragmentParentSnapshot Parent { get; }
        public Dictionary<string, FemMeshNode> MeshNodes { get; }
        /// <summary>КЭ сетки родителя, заходящие в полосу.</summary>
        public List<FemElement> Band { get; }
        public HashSet<string> BandTags { get; }
        /// <summary>Сетка родителя для разрешения целей нагрузок на КЭ.</summary>
        public FemLoadMeshContext Mesh { get; }
        /// <summary>Части-стержни фрагмента.</summary>
        public List<BarPart> Bars { get; }
        /// <summary>КЭ и КонЭ родителя, нагрузки которых не нашли части фрагмента или вид не поддержан.</summary>
        public List<string> Lost { get; } = [];
        /// <summary>Точка приведения: центр габарита полосы в плане на средней отметке.</summary>
        public PlanarVector3 Origin { get; }
        /// <summary>Полудиагональ габарита полосы в плане (не меньше 1 м) — плечо масштаба при сравнении моментов.</summary>
        public double Reach { get; }
        /// <summary>Равнодействующие нагрузок родителя на полосу по (загружение, вид).</summary>
        public Dictionary<(int LoadCase, string Category), Resultant> ParentSums { get; } = [];
        /// <summary>Вид каждой созданной нагрузки фрагмента (по ссылке на объект).</summary>
        public Dictionary<object, string> CategoryOf { get; } = new(ReferenceEqualityComparer.Instance);

        Resultant Sum(int lc, string category)
        {
            if (!ParentSums.TryGetValue((lc, category), out var s)) ParentSums[(lc, category)] = s = new Resultant();
            return s;
        }

        /// <summary>Сила и момент родителя в точке <paramref name="at"/>.</summary>
        public void AddParent(int lc, string category, PlanarVector3 at, PlanarVector3 f, PlanarVector3 m) =>
            Sum(lc, category).Add(at - Origin, f, m);

        /// <summary>Линейная погонная нагрузка родителя на отрезке a → b.</summary>
        public void AddParentLine(int lc, string category, PlanarVector3 a, PlanarVector3 b, PlanarVector3 qa, PlanarVector3 qb) =>
            Sum(lc, category).AddLine(a - Origin, b - Origin, qa, qb);

        /// <summary>
        /// Часть КЭ-пластины в полосе (клип по z₁, z₂; пусто — КЭ вне полосы) и КонЭ фрагмента, содержащий её центр
        /// (сначала части своего КонЭ родителя, затем любые).
        /// </summary>
        public (List<PlanarVector3> Polygon, string? Part) ShellClip(FemElement e, FemElementGeometry g)
        {
            if (_shellClip.TryGetValue(e.ElemTag, out var cached)) return cached;
            var polygon = ClipBox(C, g.Contour.Select(i => g.Points[i]).ToList());
            string? part = null;
            var (area, centroid) = polygon.Count >= 3 ? AreaCentroid(polygon) : (0, PlanarVector3.Zero);
            if (area > C.Tol * C.Tol)
            {
                var shells = C.Draft.Members.Where(m => m.Region is not null).ToList();
                string? Find(IEnumerable<StoreyFragmentMember> list) =>
                    list.FirstOrDefault(m => RegionContains(m.Region!, centroid, C.Tol))?.Member.ElemTag;
                part = Find(shells.Where(m => m.ParentMemberTag == e.SourceMemberTag)) ?? Find(shells);
            }
            else polygon = [];
            return _shellClip[e.ElemTag] = (polygon, part);
        }

        /// <summary>КонЭ фрагмента для КЭ родителя: пластина — по центру части в полосе, стержень — по середине участка в полосе.</summary>
        public string? PartOf(FemElement e, FemElementGeometry g)
        {
            if (g.IsShell) return ShellClip(e, g).Part;
            if (_barPart.TryGetValue(e.ElemTag, out var cached)) return cached;
            string? part = null;
            double len = g.Length;
            if (len > 1e-9)
            {
                var axis = (g.Points[1] - g.Points[0]) * (1 / len);
                var (lo, hi) = ClipRange(C, g.Points[0], axis, 0, len);
                if (hi - lo > 1e-9)
                {
                    var mid = g.Points[0] + axis * ((lo + hi) / 2);
                    bool On(BarPart p) => (mid - p.A).Dot(p.Axis) is var s && s >= -C.Tol && s <= p.Length + C.Tol
                                          && (mid - (p.A + p.Axis * s)).Length <= SourceSnapM;
                    part = (Bars.FirstOrDefault(p => p.ParentTag == e.SourceMemberTag && On(p)) ?? Bars.FirstOrDefault(On))?.Tag;
                }
            }
            return _barPart[e.ElemTag] = part;
        }

        /// <summary>Узловая нагрузка фрагмента в точке: узел схемы фрагмента (новый — свободный), сумма по загружению и виду.</summary>
        public void AddNodeLoad(int lc, string category, PlanarVector3 at, PlanarVector3 f, PlanarVector3 m)
        {
            var node = C.Nodes.Get(at);
            if (!_nodeLoads.TryGetValue((lc, node.NodeTag, category), out var load))
            {
                _nodeLoads[(lc, node.NodeTag, category)] = load = new FemNodeLoad { LoadCaseId = lc };
                C.Draft.NodeLoads.Add(new FragmentNodeLoad(node.NodeTag, load));
                CategoryOf[load] = category;
            }
            load.Fx += f.X; load.Fy += f.Y; load.Fz += f.Z;
            load.Mx += m.X; load.My += m.Y; load.Mz += m.Z;
        }
    }

    /// <summary>
    /// Нагрузки фрагмента (спека разд. 4): копии загружений и определений, опорная сетка, перенос нагрузок всех видов,
    /// проверка равнодействующих.
    /// </summary>
    static void TransferLoads(Ctx c, StoreyFragmentParentSnapshot parent)
    {
        c.Draft.LoadCases.AddRange(parent.LoadCases.Select(FragmentLoadCopy.Copy));
        c.Draft.LoadDefinitions.AddRange(parent.LoadDefinitions.Select(FragmentLoadCopy.Copy));
        var l = new LoadCtx(c, parent);
        c.Draft.RefElements.AddRange(FragmentRefElement.From(l.Band.Where(e => e.ElemType is "beam" or "shell"), l.MeshNodes));
        TransferNodeLoads(l);
        var pieces = new List<BarPiece>();
        TransferMemberLoads(l, pieces);
        TransferElementLoads(l, pieces);
        FlushBarPieces(l, pieces);
        if (l.Lost.Count > 0)
        {
            var lost = l.Lost.Distinct().ToList();
            c.Warn("fragment_load_lost", $"Нагрузки не перенесены — нет части фрагмента или вид не поддержан: {Head(lost)}.", [.. lost]);
        }
        CheckResultants(l);
    }

    /// <summary>
    /// Узловые нагрузки сетки и схемы родителя в полосе → <see cref="FemNodeLoad"/> в узле схемы фрагмента в той же
    /// точке (внутри плиты или стены — свободный узел, мешер вставит точку); заданные перемещения — в тот же узел.
    /// </summary>
    static void TransferNodeLoads(LoadCtx l)
    {
        foreach (var load in l.Parent.MeshNodeLoads)
        {
            if (!l.MeshNodes.TryGetValue(load.MeshNodeTag, out var n) || !l.C.InBox(P(n))) continue;
            NodeLoad(l, load.LoadCaseId, new PlanarVector3(n.X, n.Y, n.Z),
                new PlanarVector3(load.Fx, load.Fy, load.Fz), new PlanarVector3(load.Mx, load.My, load.Mz));
        }
        var byId = l.Parent.Nodes.Where(n => n.Id > 0).GroupBy(n => n.Id).ToDictionary(g => g.Key, g => g.First());
        foreach (var load in l.Parent.NodeLoads)
        {
            if (!byId.TryGetValue(load.NodeId, out var n) || !l.C.InBox(P(n))) continue;
            NodeLoad(l, load.LoadCaseId, P(n), new PlanarVector3(load.Fx, load.Fy, load.Fz), new PlanarVector3(load.Mx, load.My, load.Mz));
        }
        foreach (var k in l.Parent.KinematicLoads)
        {
            if (!byId.TryGetValue(k.NodeId, out var n) || !l.C.InBox(P(n))) continue;
            var node = l.C.Nodes.Get(P(n));
            l.C.Draft.KinematicLoads.Add(new FragmentKinematicLoad(node.NodeTag,
                new FemKinematicLoad { LoadCaseId = k.LoadCaseId, Dof = k.Dof, Value = k.Value }));
        }
    }

    static void NodeLoad(LoadCtx l, int lc, PlanarVector3 at, PlanarVector3 f, PlanarVector3 m)
    {
        l.AddParent(lc, LoadCategory.Nodes, at, f, m);
        l.AddNodeLoad(lc, LoadCategory.Nodes, at, f, m);
    }
}
