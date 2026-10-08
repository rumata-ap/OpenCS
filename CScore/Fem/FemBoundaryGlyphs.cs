using CScore.Planar;

namespace CScore.Fem;

/// <summary>Отрезок глифа ГУ.</summary>
public readonly record struct FemGlyphSegment(PlanarVector3 A, PlanarVector3 B);

/// <summary>
/// Глифы ГУ сеточного уровня для 3D-вида — отрезки по слоям (каждый слой рисуется одной линией: на музее тысячи
/// опор и пружин). Счётчики — по исходным объектам, жёсткие тела прорежены (<see cref="RigidLinksShown"/> из
/// <see cref="RigidLinksTotal"/>).
/// </summary>
public sealed record FemBoundaryGlyphSet(
    IReadOnlyList<FemGlyphSegment> Supports,
    IReadOnlyList<FemGlyphSegment> Springs,
    IReadOnlyList<FemGlyphSegment> Hinges,
    IReadOnlyList<FemGlyphSegment> RigidBodies,
    int SupportNodes, int SpringNodes, int HingeEnds, int RigidBodyCount, int RigidLinksShown, int RigidLinksTotal)
{
    public static readonly FemBoundaryGlyphSet Empty = new([], [], [], [], 0, 0, 0, 0, 0, 0);

    public bool IsEmpty => SupportNodes == 0 && SpringNodes == 0 && HingeEnds == 0 && RigidBodyCount == 0;
}

/// <summary>
/// Строит глифы ГУ сеточного уровня: закрепления <see cref="FemMeshNodeSupport"/> (знак опор как у узлов схемы),
/// пружины (зигзаг по поступательным направлениям с K ≠ 0, спираль — по поворотным), шарниры концов стержней
/// (кружок у конца), жёсткие тела (линии от ведущего узла к ведомым). Объекты на отсутствующих узлах пропускаются —
/// их перечисляет <see cref="FemBoundaryResolver"/>.
/// </summary>
public static class FemBoundaryGlyphs
{
    static readonly PlanarVector3[] Axes = [new(1, 0, 0), new(0, 1, 0), new(0, 0, 1)];

    /// <param name="nodes">Узлы конструктивного уровня — для пружин <see cref="FemSpringTargetKinds.Node"/>.</param>
    /// <param name="size">Характерный размер знака, м (см. <see cref="GlyphSize"/>).</param>
    /// <param name="maxRigidLinks">Наибольшее число линий жёстких тел (берутся с равным шагом).</param>
    public static FemBoundaryGlyphSet Build(
        IReadOnlyList<FemNode> nodes,
        IReadOnlyList<FemMeshNode> meshNodes,
        IReadOnlyList<FemElement> meshElements,
        IReadOnlyList<FemMeshNodeSupport> supports,
        IReadOnlyList<FemSpring> springs,
        IReadOnlyList<FemRigidBody> rigidBodies,
        double size,
        int maxRigidLinks = 5000)
    {
        var mesh = new Dictionary<string, PlanarVector3>(StringComparer.Ordinal);
        foreach (var n in meshNodes) mesh.TryAdd(n.NodeTag, new PlanarVector3(n.X, n.Y, n.Z));
        var own = new Dictionary<string, PlanarVector3>(StringComparer.Ordinal);
        foreach (var n in nodes) own.TryAdd(n.NodeTag, new PlanarVector3(n.X, n.Y, n.Z));

        // Закрепления: маски одного узла объединяются.
        var masks = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var s in supports)
            if ((s.Mask & FemBoundaryDofs.All) != 0 && mesh.ContainsKey(s.NodeTag))
                masks[s.NodeTag] = masks.GetValueOrDefault(s.NodeTag) | (s.Mask & FemBoundaryDofs.All);
        var supportLines = new List<FemGlyphSegment>();
        foreach (var (tag, mask) in masks)
            for (int i = 0; i < 6; i++)
                if ((mask & (1 << i)) != 0)
                {
                    if (i < 3) AddTranslationSupport(supportLines, mesh[tag], Axes[i], size);
                    else AddRotationSupport(supportLines, mesh[tag], Axes[i - 3], size);
                }

        // Пружины: жёсткости одного узла складываются.
        var stiff = new Dictionary<(bool Own, string Tag), double[]>();
        foreach (var s in springs)
        {
            bool isOwn = s.TargetKind == FemSpringTargetKinds.Node;
            if (s.ActiveMask == 0 || !(isOwn ? own : mesh).ContainsKey(s.NodeTag)) continue;
            if (!stiff.TryGetValue((isOwn, s.NodeTag), out var k)) stiff[(isOwn, s.NodeTag)] = k = new double[6];
            var add = s.Stiffnesses;
            for (int i = 0; i < 6; i++) k[i] += add[i];
        }
        var springLines = new List<FemGlyphSegment>();
        foreach (var ((isOwn, tag), k) in stiff)
        {
            var p = (isOwn ? own : mesh)[tag];
            for (int i = 0; i < 6; i++)
                if (k[i] != 0)
                {
                    if (i < 3) AddTranslationSpring(springLines, p, Axes[i], size);
                    else AddRotationSpring(springLines, p, Axes[i - 3], size);
                }
        }

        // Шарниры: кружок на стержне у освобождённого конца.
        var hingeLines = new List<FemGlyphSegment>();
        int hingeEnds = 0;
        foreach (var e in meshElements)
        {
            if (e.ElemType != "beam" || (e.ReleaseI ?? 0) == 0 && (e.ReleaseJ ?? 0) == 0) continue;
            var tags = FemMemberGroup.ParseTags(e.NodeIdsJson);
            if (tags.Count < 2 || !mesh.TryGetValue(tags[0], out var pi) || !mesh.TryGetValue(tags[1], out var pj)) continue;
            if (((e.ReleaseI ?? 0) & FemBoundaryDofs.All) != 0) { AddHinge(hingeLines, pi, pj, size); hingeEnds++; }
            if (((e.ReleaseJ ?? 0) & FemBoundaryDofs.All) != 0) { AddHinge(hingeLines, pj, pi, size); hingeEnds++; }
        }

        // Жёсткие тела: крестик у ведущего узла и линии к ведомым, прореженные с общим шагом.
        var bodies = rigidBodies.Where(b => (b.Mask & FemBoundaryDofs.All) != 0 && mesh.ContainsKey(b.MasterNodeTag)).ToList();
        int total = bodies.Sum(b => b.SlaveNodeTags.Count(mesh.ContainsKey));
        int step = Math.Max(1, (int)Math.Ceiling(total / (double)Math.Max(1, maxRigidLinks)));
        var rigidLines = new List<FemGlyphSegment>();
        int link = 0, shown = 0;
        foreach (var b in bodies)
        {
            var master = mesh[b.MasterNodeTag];
            bool any = false;
            foreach (var slave in b.SlaveNodeTags)
            {
                if (!mesh.TryGetValue(slave, out var ps)) continue;
                if (link++ % step != 0) continue;
                rigidLines.Add(new FemGlyphSegment(master, ps));
                shown++;
                any = true;
            }
            if (!any) continue;
            double c = 0.3 * size;
            foreach (var a in Axes) rigidLines.Add(new FemGlyphSegment(master - a * c, master + a * c));
        }

        return new FemBoundaryGlyphSet(supportLines, springLines, hingeLines, rigidLines,
            masks.Count, stiff.Count, hingeEnds, bodies.Count, shown, total);
    }

    /// <summary>
    /// Размер знака по сетке: 0,35 медианного размера КЭ (длина стержня, сторона пластины), в пределах 0,05…1 м;
    /// без КЭ — 0,28 м, как у знаков опор узлов схемы.
    /// </summary>
    public static double GlyphSize(IReadOnlyList<FemMeshNode> meshNodes, IReadOnlyList<FemElement> meshElements)
    {
        var pos = new Dictionary<string, PlanarVector3>(StringComparer.Ordinal);
        foreach (var n in meshNodes) pos.TryAdd(n.NodeTag, new PlanarVector3(n.X, n.Y, n.Z));
        int stride = Math.Max(1, meshElements.Count / 2000);
        var sizes = new List<double>();
        for (int i = 0; i < meshElements.Count; i += stride)
        {
            var tags = FemMemberGroup.ParseTags(meshElements[i].NodeIdsJson);
            if (tags.Count >= 2 && pos.TryGetValue(tags[0], out var a) && pos.TryGetValue(tags[1], out var b)
                && (b - a).Length is var l && l > 0 && double.IsFinite(l))
                sizes.Add(l);
        }
        if (sizes.Count == 0) return 0.28;
        sizes.Sort();
        return Math.Clamp(0.35 * sizes[sizes.Count / 2], 0.05, 1.0);
    }

    static (PlanarVector3 Side, PlanarVector3 Up) Frame(PlanarVector3 axis)
    {
        var side = (Math.Abs(axis.Z) < 0.9 ? axis.Cross(new PlanarVector3(0, 0, 1)) : axis.Cross(new PlanarVector3(0, 1, 0))).Normalize();
        return (side, axis.Cross(side).Normalize());
    }

    static void Polyline(List<FemGlyphSegment> target, IReadOnlyList<PlanarVector3> points)
    {
        for (int i = 0; i + 1 < points.Count; i++) target.Add(new FemGlyphSegment(points[i], points[i + 1]));
    }

    /// <summary>Знак поступательной связи — как у узлов схемы: стойка вдоль оси и «земля» крестом.</summary>
    static void AddTranslationSupport(List<FemGlyphSegment> t, PlanarVector3 node, PlanarVector3 axis, double s)
    {
        var (side, up) = Frame(axis);
        var b = node + axis * s;
        double w = 0.57 * s, d = 0.43 * s;
        t.Add(new(node, b));
        t.Add(new(b - side * w, b + side * w));
        t.Add(new(b - up * w, b + up * w));
        t.Add(new(b - side * d - up * d, b + side * d + up * d));
    }

    /// <summary>Знак поворотной связи — дуга со стрелкой вокруг оси.</summary>
    static void AddRotationSupport(List<FemGlyphSegment> t, PlanarVector3 node, PlanarVector3 axis, double s)
    {
        var (side, up) = Frame(axis);
        double r = 0.86 * s;
        var arc = new List<PlanarVector3>(13);
        for (int i = 0; i <= 12; i++)
        {
            double a = Math.PI * 1.6 * i / 12 + Math.PI * 0.2;
            arc.Add(node + side * (Math.Cos(a) * r) + up * (Math.Sin(a) * r));
        }
        Polyline(t, arc);
        var tip = arc[^1];
        t.Add(new(tip, tip - side * (0.36 * s) - up * (0.21 * s)));
        t.Add(new(tip, tip + side * (0.14 * s) - up * (0.36 * s)));
    }

    /// <summary>Поступательная пружина: зигзаг вдоль оси и «земля» поперёк.</summary>
    static void AddTranslationSpring(List<FemGlyphSegment> t, PlanarVector3 node, PlanarVector3 axis, double s)
    {
        var (side, _) = Frame(axis);
        double len = 1.3 * s, lead = 0.2 * s, amp = 0.22 * s;
        const int teeth = 6;
        var pts = new List<PlanarVector3> { node, node + axis * lead };
        double zig = len - 2 * lead;
        for (int i = 1; i < teeth * 2; i++)
            pts.Add(node + axis * (lead + zig * i / (teeth * 2)) + side * (i % 2 == 1 ? amp : -amp));
        var end = node + axis * len;
        pts.Add(node + axis * (len - lead));
        pts.Add(end);
        Polyline(t, pts);
        t.Add(new(end - side * (0.45 * s), end + side * (0.45 * s)));
    }

    /// <summary>Поворотная пружина: спираль вокруг оси в два витка.</summary>
    static void AddRotationSpring(List<FemGlyphSegment> t, PlanarVector3 node, PlanarVector3 axis, double s)
    {
        var (side, up) = Frame(axis);
        const int n = 32;
        var pts = new List<PlanarVector3>(n + 1);
        for (int i = 0; i <= n; i++)
        {
            double a = 4 * Math.PI * i / n, r = s * (0.15 + 0.75 * i / n);
            pts.Add(node + side * (Math.Cos(a) * r) + up * (Math.Sin(a) * r));
        }
        Polyline(t, pts);
    }

    /// <summary>Кружок шарнира на стержне у конца <paramref name="end"/> (в двух плоскостях через ось — виден с любой стороны).</summary>
    static void AddHinge(List<FemGlyphSegment> t, PlanarVector3 end, PlanarVector3 other, double s)
    {
        var d = other - end;
        double l = d.Length;
        if (!(l > 0)) return;
        var axis = d * (1 / l);
        double r = Math.Min(0.25 * s, 0.12 * l);
        var c = end + axis * r;
        var (side, up) = Frame(axis);
        foreach (var perp in (PlanarVector3[])[side, up])
        {
            const int n = 16;
            var pts = new List<PlanarVector3>(n + 1);
            for (int i = 0; i <= n; i++)
            {
                double a = 2 * Math.PI * i / n;
                pts.Add(c + axis * (Math.Cos(a) * r) + perp * (Math.Sin(a) * r));
            }
            Polyline(t, pts);
        }
    }
}
