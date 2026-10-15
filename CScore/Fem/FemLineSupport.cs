using CScore.Planar;

namespace CScore.Fem;

/// <summary>
/// Линейная связь на грани пластинчатого КонЭ (уровень КонЭ, как линейная опора RFEM): закрепление по маске и погонные
/// пружины в общих осях на всю сторону контура области КонЭ. Одна запись на грань. Грань опознаётся ключом
/// (<see cref="MemberTag"/>, <see cref="Loop"/>, <see cref="HoleIndex"/>, <see cref="StartVertex"/>) и концами
/// (<see cref="X1"/>…<see cref="Z2"/>) — после правки контура ключ восстанавливается по концам
/// (<see cref="FemLineSupportGeometry.Match"/>). При расчёте раскладывается на узлы сетки грани
/// (<see cref="FemLineSupportResolver"/>).
/// </summary>
public sealed class FemLineSupport
{
    public int Id { get; set; }
    public int SchemaId { get; set; }

    /// <summary>Тег пластинчатого КонЭ (<see cref="FemMember.ElemTag"/>).</summary>
    public string MemberTag { get; set; } = "";

    /// <summary>Контур грани: внешний или отверстие.</summary>
    public BoundaryLoop Loop { get; set; } = BoundaryLoop.Outer;

    /// <summary>Номер отверстия по порядку <see cref="PlanarRegion.Holes"/>; у внешнего контура — 0.</summary>
    public int HoleIndex { get; set; }

    /// <summary>Начальная вершина грани (открытого контура); грань — до следующей вершины по кругу.</summary>
    public int StartVertex { get; set; }

    /// <summary>Концы грани в общих осях при назначении, м.</summary>
    public double X1 { get; set; }
    public double Y1 { get; set; }
    public double Z1 { get; set; }
    public double X2 { get; set; }
    public double Y2 { get; set; }
    public double Z2 { get; set; }

    /// <summary>Закреплённые DOF в общих осях (биты 0–5: X Y Z UX UY UZ), как <see cref="FemNode.DofMask"/>.</summary>
    public int Mask { get; set; }

    /// <summary>
    /// Закрепить поворот кромки — вокруг нормали к грани в плоскости пластины («жёсткое» шарнирное опирание пластины
    /// Миндлина: без него узлы грани поворачиваются вдоль неё и прогиб завышается). Добавляется к <see cref="Mask"/> при
    /// раскладке, если нормаль параллельна общей оси (<see cref="FemLineSupportGeometry.EdgeRotationBit"/>).
    /// </summary>
    public bool FixEdgeRotation { get; set; }

    /// <summary>Погонные поступательные жёсткости, Н/м на метр грани (Н/м²).</summary>
    public double Kx { get; set; }
    public double Ky { get; set; }
    public double Kz { get; set; }

    /// <summary>Погонные поворотные жёсткости, Н·м/рад на метр грани.</summary>
    public double Kux { get; set; }
    public double Kuy { get; set; }
    public double Kuz { get; set; }

    /// <summary>Происхождение: <see cref="FemLoadOrigin.Manual"/> или «import:&lt;источник&gt;».</summary>
    public string Origin { get; set; } = FemLoadOrigin.Manual;

    /// <summary>Погонные жёсткости по DOF 0–5 (X Y Z UX UY UZ).</summary>
    public double[] Stiffnesses => [Kx, Ky, Kz, Kux, Kuy, Kuz];

    /// <summary>Задаёт погонные жёсткости по DOF 0–5; по закреплённым DOF жёсткость обнуляется.</summary>
    public void SetStiffnesses(IReadOnlyList<double> k)
    {
        if (k.Count != 6) throw new ArgumentException("Нужно 6 жёсткостей: X Y Z UX UY UZ.", nameof(k));
        var v = new double[6];
        for (int i = 0; i < 6; i++) v[i] = (Mask & (1 << i)) != 0 ? 0 : k[i];
        (Kx, Ky, Kz, Kux, Kuy, Kuz) = (v[0], v[1], v[2], v[3], v[4], v[5]);
    }

    /// <summary>Маска DOF с ненулевой погонной жёсткостью.</summary>
    public int SpringMask
    {
        get
        {
            var k = Stiffnesses;
            int mask = 0;
            for (int i = 0; i < 6; i++)
                if (k[i] != 0) mask |= 1 << i;
            return mask;
        }
    }

    /// <summary>Связь что-то задаёт: закрепление или пружину.</summary>
    public bool IsActive => (Mask & FemBoundaryDofs.All) != 0 || FixEdgeRotation || SpringMask != 0;

    /// <summary>Ключ грани.</summary>
    public FemPlateEdgeKey Key => new(MemberTag, Loop, HoleIndex, StartVertex);

    /// <summary>Концы грани при назначении.</summary>
    public (PlanarVector3 A, PlanarVector3 B) Ends => (new(X1, Y1, Z1), new(X2, Y2, Z2));

    /// <summary>Привязывает связь к грани: ключ и концы.</summary>
    public void AssignEdge(FemPlateEdge edge)
    {
        (MemberTag, Loop, HoleIndex, StartVertex) = (edge.Key.MemberTag, edge.Key.Loop, edge.Key.HoleIndex, edge.Key.StartVertex);
        (X1, Y1, Z1, X2, Y2, Z2) = (edge.A.X, edge.A.Y, edge.A.Z, edge.B.X, edge.B.Y, edge.B.Z);
    }

    /// <summary>Копия с теми же параметрами (без Id).</summary>
    public FemLineSupport Clone() => (FemLineSupport)MemberwiseClone();
}

/// <summary>Ключ грани пластинчатого КонЭ: КонЭ, контур, номер отверстия, начальная вершина.</summary>
public readonly record struct FemPlateEdgeKey(string MemberTag, BoundaryLoop Loop, int HoleIndex, int StartVertex)
{
    /// <summary>«КонЭ 12, сторона 3» / «КонЭ 12, отверстие 1, сторона 2» (стороны с 1).</summary>
    public override string ToString() => Loop == BoundaryLoop.Outer
        ? $"{MemberTag}, сторона {StartVertex + 1}"
        : $"{MemberTag}, отверстие {HoleIndex + 1}, сторона {StartVertex + 1}";
}

/// <summary>Грань пластинчатого КонЭ в общих осях; <paramref name="Normal"/> — нормаль пластины.</summary>
public sealed record FemPlateEdge(FemPlateEdgeKey Key, PlanarVector3 A, PlanarVector3 B, PlanarVector3 Normal)
{
    public double Length => (B - A).Length;
}

/// <summary>Грани контуров пластинчатых КонЭ и опознание грани линейной связи.</summary>
public static class FemLineSupportGeometry
{
    /// <summary>Допуск совпадения концов грани, м.</summary>
    public const double Tolerance = FemMeshDiscretizer.CollinearToleranceM;

    /// <summary>Пластинчатый КонЭ, у которого есть грани (оболочка с областью).</summary>
    public static bool HasEdges(FemMember member) => member.ElemType == "shell" && member.PlanarRegionId != null;

    /// <summary>Стороны внешнего контура и отверстий области КонЭ в общих осях (нумерация — как у разметки сетки).</summary>
    public static List<FemPlateEdge> Edges(FemMember member, PlanarRegion region)
    {
        var result = new List<FemPlateEdge>();
        int holeIndex = 0;
        foreach (var contour in region.Contours)
        {
            if (contour.Type is not (ContourType.Hull or ContourType.Hole)) continue;
            var loop = contour.Type == ContourType.Hull ? BoundaryLoop.Outer : BoundaryLoop.Hole;
            int hole = loop == BoundaryLoop.Hole ? holeIndex++ : 0;
            var (x, y) = PlanarRegionTopologyValidator.ToOpenLoop(contour.X, contour.Y);
            if (x.Length < 2) continue;
            for (int i = 0; i < x.Length; i++)
            {
                int j = (i + 1) % x.Length;
                result.Add(new FemPlateEdge(new FemPlateEdgeKey(member.ElemTag, loop, hole, i),
                    ToGlobal(region.Frame, x[i], y[i]), ToGlobal(region.Frame, x[j], y[j]), region.Frame.LocalZ));
            }
        }
        return result;
    }

    /// <summary>Грани всех пластинчатых КонЭ с областью по тегу КонЭ.</summary>
    public static Dictionary<string, List<FemPlateEdge>> EdgesByMember(IEnumerable<FemMember> members,
        IEnumerable<PlanarRegion> regions)
    {
        var regionById = new Dictionary<int, PlanarRegion>();
        foreach (var r in regions) regionById.TryAdd(r.Id, r);
        var result = new Dictionary<string, List<FemPlateEdge>>(StringComparer.Ordinal);
        foreach (var m in members)
            if (HasEdges(m) && regionById.TryGetValue(m.PlanarRegionId!.Value, out var region))
                result.TryAdd(m.ElemTag, Edges(m, region));
        return result;
    }

    /// <summary>
    /// Грань связи: по ключу, если концы совпадают с сохранёнными; иначе — сторона того же КонЭ с теми же концами в
    /// любом порядке (контур перенумерован); null — грань потеряна.
    /// </summary>
    public static FemPlateEdge? Match(FemLineSupport support, IReadOnlyList<FemPlateEdge>? edges)
    {
        if (edges == null || edges.Count == 0) return null;
        var (a, b) = support.Ends;
        var byKey = edges.FirstOrDefault(e => e.Key == support.Key);
        if (byKey != null && SameEnds(byKey, a, b)) return byKey;
        return edges.FirstOrDefault(e => SameEnds(e, a, b));
    }

    /// <summary>
    /// Бит DOF поворота кромки (UX, UY или UZ): ось — нормаль к грани в плоскости пластины (n × t); null — ось не
    /// параллельна общей (наклонная грань: поворот в общих осях не закрепить без местных осей).
    /// </summary>
    public static int? EdgeRotationBit(FemPlateEdge edge)
    {
        var t = edge.B - edge.A;
        if (t.Length <= Tolerance || edge.Normal.Length < 1e-12) return null;
        var cross = edge.Normal.Cross(t);
        if (cross.Length < 1e-12) return null;
        var m = cross.Normalize();
        double[] c = [m.X, m.Y, m.Z];
        for (int i = 0; i < 3; i++)
            if (Math.Abs(Math.Abs(c[i]) - 1) < 1e-6) return 1 << (3 + i);
        return null;
    }

    static bool SameEnds(FemPlateEdge e, PlanarVector3 a, PlanarVector3 b) =>
        (Near(e.A, a) && Near(e.B, b)) || (Near(e.A, b) && Near(e.B, a));

    static bool Near(PlanarVector3 p, PlanarVector3 q) => (p - q).Length <= Tolerance;

    static PlanarVector3 ToGlobal(Frame3D frame, double u, double v) => frame.Origin + frame.LocalX * u + frame.LocalY * v;
}

/// <summary>Узел сетки на грани: тег, расстояние от начала грани и приходящаяся на узел длина, м.</summary>
public readonly record struct FemLineSupportNode(string NodeTag, double S, double Tributary);

/// <summary>Линейная связь, разложенная на узлы сетки своей грани.</summary>
public sealed record FemLineSupportChain(FemLineSupport Support, FemPlateEdge Edge, IReadOnlyList<FemLineSupportNode> Nodes);

/// <summary>Линейные связи схемы, сведённые к узловым ГУ сетки.</summary>
public sealed class FemLineSupportNodal
{
    public static readonly FemLineSupportNodal Empty = new() { Supports = [], Springs = [], Chains = [], Diagnostics = [] };

    /// <summary>Закрепления узлов граней (<c>Origin = "line:&lt;Id&gt;"</c>).</summary>
    public required IReadOnlyList<FemMeshNodeSupport> Supports { get; init; }
    /// <summary>Пружины узлов граней: погонная жёсткость × приходящаяся длина.</summary>
    public required IReadOnlyList<FemSpring> Springs { get; init; }
    /// <summary>Узлы каждой учтённой связи — для реакций по грани и выделения.</summary>
    public required IReadOnlyList<FemLineSupportChain> Chains { get; init; }
    public required IReadOnlyList<FemValidationDiagnostic> Diagnostics { get; init; }

    /// <summary>Добавляет закрепления и пружины граней к узловым ГУ схемы — вход <see cref="FemBoundaryResolver"/>.</summary>
    public (List<FemMeshNodeSupport> Supports, List<FemSpring> Springs) AppendTo(
        IEnumerable<FemMeshNodeSupport> supports, IEnumerable<FemSpring> springs) =>
        ([.. supports, .. Supports], [.. springs, .. Springs]);
}

/// <summary>
/// Раскладка линейных связей на узлы сетки: узлы КЭ своего КонЭ (<see cref="FemElement.SourceMemberTag"/>), лежащие на
/// грани (±<see cref="FemLineSupportGeometry.Tolerance"/>), по порядку вдоль неё. Закрепление — маска каждому узлу,
/// пружина — погонная жёсткость × приходящаяся длина (половина прилегающих отрезков; сумма по грани = k′·L).
/// Связи без грани, КонЭ без сетки и разрывы цепочки — диагностики; такие связи пропускаются.
/// </summary>
public static class FemLineSupportResolver
{
    /// <summary>Префикс <see cref="FemMeshNodeSupport.Origin"/> / <see cref="FemSpring.Origin"/> записей граней.</summary>
    public const string OriginPrefix = "line:";

    public static FemLineSupportNodal ToNodal(
        IReadOnlyList<FemLineSupport> supports,
        IReadOnlyDictionary<string, List<FemPlateEdge>> edgesByMember,
        IReadOnlyList<FemMeshNode> meshNodes,
        IReadOnlyList<FemElement> meshElements)
    {
        if (supports.Count == 0) return FemLineSupportNodal.Empty;
        var diagnostics = new List<FemValidationDiagnostic>();
        var nodeByTag = new Dictionary<string, FemMeshNode>(StringComparer.Ordinal);
        foreach (var n in meshNodes) nodeByTag.TryAdd(n.NodeTag, n);

        // Узлы КЭ каждого КонЭ со связями.
        var memberTags = supports.Select(s => s.MemberTag).ToHashSet(StringComparer.Ordinal);
        var nodesByMember = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var e in meshElements)
        {
            if (e.SourceMemberTag is not { } m || !memberTags.Contains(m)) continue;
            if (FemMeshTopology.ReadNodeTags(e) is not { } tags) continue;
            if (!nodesByMember.TryGetValue(m, out var set)) nodesByMember[m] = set = new(StringComparer.Ordinal);
            foreach (var t in tags) set.Add(t);
        }

        var outSupports = new List<FemMeshNodeSupport>();
        var outSprings = new List<FemSpring>();
        var chains = new List<FemLineSupportChain>();
        var lost = new List<string>();
        var noMesh = new List<string>();
        var gaps = new List<string>();
        var skew = new List<string>();
        foreach (var s in supports)
        {
            if (!s.IsActive) continue;
            var edge = FemLineSupportGeometry.Match(s, edgesByMember.GetValueOrDefault(s.MemberTag));
            if (edge == null) { lost.Add(s.Key.ToString()); continue; }
            if (!nodesByMember.TryGetValue(s.MemberTag, out var own)) { noMesh.Add(s.MemberTag); continue; }

            var chain = Chain(edge, own, nodeByTag, out bool gap);
            if (gap) { gaps.Add(edge.Key.ToString()); continue; }
            chains.Add(new FemLineSupportChain(s, edge, chain));

            string origin = OriginPrefix + s.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
            int mask = s.Mask & FemBoundaryDofs.All;
            if (s.FixEdgeRotation)
            {
                if (FemLineSupportGeometry.EdgeRotationBit(edge) is { } bit) mask |= bit;
                else skew.Add(edge.Key.ToString());
            }
            var k = s.Stiffnesses;
            bool spring = s.SpringMask != 0;
            foreach (var node in chain)
            {
                if (mask != 0)
                    outSupports.Add(new FemMeshNodeSupport { SchemaId = s.SchemaId, NodeTag = node.NodeTag, Mask = mask, Origin = origin });
                if (spring)
                {
                    var f = new FemSpring { SchemaId = s.SchemaId, TargetKind = FemSpringTargetKinds.MeshNode, NodeTag = node.NodeTag, Origin = origin };
                    f.SetStiffnesses(k.Select(v => v * node.Tributary).ToArray());
                    outSprings.Add(f);
                }
            }
        }
        Add(diagnostics, "line_support_edge_lost", "Линейные связи потеряли грань (контур КонЭ изменён или КонЭ удалён) и не учитываются", lost, false);
        Add(diagnostics, "line_support_no_mesh", "Линейные связи на КонЭ без сетки не учитываются — выполните дискретизацию; КонЭ", noMesh, false);
        Add(diagnostics, "line_support_edge_rotation_skew",
            "Поворот кромки не закреплён — грань не параллельна общим осям (местные оси граней не поддерживаются)", skew, false);
        Add(diagnostics, "line_support_gap", "Сетка не согласована с гранью (меньше двух узлов или разрыв цепочки) — линейные связи не учитываются", gaps, true);
        return new FemLineSupportNodal { Supports = outSupports, Springs = outSprings, Chains = chains, Diagnostics = diagnostics };
    }

    /// <summary>
    /// Узлы <paramref name="own"/> на грани по возрастанию расстояния от начала с приходящейся длиной. Разрыв — меньше
    /// двух узлов или нет узла в одном из концов грани (сетка не дошла до части стороны).
    /// </summary>
    public static List<FemLineSupportNode> Chain(FemPlateEdge edge, IEnumerable<string> own,
        IReadOnlyDictionary<string, FemMeshNode> nodeByTag, out bool gap)
    {
        var a = edge.A;
        var d = edge.B - a;
        double length = d.Length;
        var points = new List<(string Tag, double S)>();
        if (length > FemLineSupportGeometry.Tolerance)
        {
            var u = d * (1 / length);
            foreach (var tag in own)
            {
                if (!nodeByTag.TryGetValue(tag, out var n)) continue;
                var r = new PlanarVector3(n.X, n.Y, n.Z) - a;
                double s = r.Dot(u);
                if (s < -FemLineSupportGeometry.Tolerance || s > length + FemLineSupportGeometry.Tolerance) continue;
                if ((r - u * s).Length > FemLineSupportGeometry.Tolerance) continue;
                points.Add((tag, Math.Clamp(s, 0, length)));
            }
        }
        points.Sort((p, q) => p.S.CompareTo(q.S));
        gap = points.Count < 2
              || points[0].S > FemLineSupportGeometry.Tolerance
              || points[^1].S < length - FemLineSupportGeometry.Tolerance;
        var result = new List<FemLineSupportNode>(points.Count);
        if (gap) return result;
        for (int i = 0; i < points.Count; i++)
        {
            double left = i > 0 ? points[i].S - points[i - 1].S : 0;
            double right = i < points.Count - 1 ? points[i + 1].S - points[i].S : 0;
            result.Add(new FemLineSupportNode(points[i].Tag, points[i].S, (left + right) / 2));
        }
        return result;
    }

    static void Add(List<FemValidationDiagnostic> diagnostics, string code, string text, List<string> items, bool isError)
    {
        if (items.Count == 0) return;
        var distinct = items.Distinct(StringComparer.Ordinal).ToArray();
        string list = string.Join(", ", distinct.Take(20)) + (distinct.Length > 20 ? $" … всего {distinct.Length}" : "");
        diagnostics.Add(new FemValidationDiagnostic(code, $"{text}: {list}.", isError, distinct));
    }
}
