using System.Globalization;
using CSfea.Core;

namespace CSfea.CScoreBridge.Structural;

/// <summary>
/// Хорды режима «только P-δ» (<see cref="RcLocalPDelta.Analyze"/>) в единой нумерации КЭ сетки
/// (<see cref="StructuralMesh.SetLocalSecondOrder"/>; оболочки модели, затем стержни — порядок списков модели) и сводка:
/// вертикальных стержней и оболочек с хордой, участков стержней, уровней стен; <see cref="NoLineShells"/> — номера
/// оболочек, у узла которых нет линии уровня по плановой координате (ошибка).
/// </summary>
public sealed record RcLocalPDeltaResult(ElementChord?[] Chords, int Bars, int Shells, int BarSegments, int WallLevels,
    IReadOnlyList<int> NoLineShells)
{
    public bool HasErrors => NoLineShells.Count > 0;

    /// <summary>Сводка для журнала и отчёта.</summary>
    public string Summary => string.Format(CultureInfo.CurrentCulture,
        "Второй порядок «только P-δ»: K_σ по перемещениям без сдвига участков между уровнями — стержней {0} (участков {1}), " +
        "оболочек стен {2} (уровней {3}); прочие КЭ без K_σ.", Bars, BarSegments, Shells, WallLevels);
}

/// <summary>
/// Уровни и хорды режима «только P-δ» (спека второго порядка фрагмента, срез 1а) по топологии
/// <see cref="RcStructuralModel"/>. Вертикальный КЭ — ось стержня или плоскость оболочки в пределах 10° от вертикали
/// (остальные — без K_σ). Стержни: участок — цепочка вертикальных стержней между узлами-уровнями; уровень — узел с
/// невертикальным КЭ, с опорой, заданным перемещением или пружиной по ux/uy, узел жёсткого тела, конец цепочки (не два
/// вертикальных стержня в узле) или излом цепочки. Стены — компоненты компланарных вертикальных оболочек, связанных
/// рёбрами: уровни — отметки рядов узлов, общих с невертикальными оболочками (плиты), и низ/верх стены; хорда узла —
/// по плановой координате вдоль стены интерполяцией по соседним узлам линии уровня.
/// </summary>
public static class RcLocalPDelta
{
    private static readonly double VerticalCos = Math.Cos(10 * Math.PI / 180);
    private static readonly double ShellNz = Math.Sin(10 * Math.PI / 180);
    private static readonly double KinkCos = Math.Cos(0.5 * Math.PI / 180);
    private static readonly double ParallelCos = Math.Cos(1.0 * Math.PI / 180);

    public static RcLocalPDeltaResult Analyze(RcStructuralModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        int nn = model.Nodes.Count, ns = model.Shells.Count, nb = model.Beams.Count;
        var index = new Dictionary<int, int>(nn);
        for (int i = 0; i < nn; i++) index.TryAdd(model.Nodes[i].Id, i);
        var p = model.Nodes.Select(n => new[] { n.X, n.Y, n.Z }).ToArray();
        double size = 0;
        if (nn > 0)
            for (int c = 0; c < 3; c++) size = Math.Max(size, p.Max(x => x[c]) - p.Min(x => x[c]));
        double tol = 1e-6 * size + 1e-9;
        var chords = new ElementChord?[ns + nb];

        var shellNodes = model.Shells.Select(s => s.NodeIds.Select(id => index[id]).ToArray()).ToArray();
        var beamNodes = model.Beams.Select(b => (I: index[b.NodeI], J: index[b.NodeJ])).ToArray();
        var normals = shellNodes.Select(nodes => Normal(nodes, p)).ToArray();
        var shellVertical = normals.Select(n => Math.Abs(n[2]) <= ShellNz).ToArray();
        var beamDir = beamNodes.Select(b => Unit(Sub(p[b.J], p[b.I]))).ToArray();
        var beamVertical = beamDir.Select(d => Math.Abs(d[2]) >= VerticalCos).ToArray();

        var nonVertical = new bool[nn];
        var nonVerticalShell = new bool[nn];
        for (int s = 0; s < ns; s++)
            if (!shellVertical[s])
                foreach (int n in shellNodes[s]) nonVertical[n] = nonVerticalShell[n] = true;
        var vertAt = new List<int>[nn];
        for (int b = 0; b < nb; b++)
        {
            if (!beamVertical[b]) { nonVertical[beamNodes[b].I] = nonVertical[beamNodes[b].J] = true; continue; }
            (vertAt[beamNodes[b].I] ??= []).Add(b);
            (vertAt[beamNodes[b].J] ??= []).Add(b);
        }
        var lateral = new bool[nn];
        void Lateral(int id) { if (index.TryGetValue(id, out int i)) lateral[i] = true; }
        foreach (var s in model.Supports) if ((s.Mask & 0b11) != 0) Lateral(s.NodeId);
        foreach (var s in model.Springs) if (s.Dof is 0 or 1) Lateral(s.NodeId);
        foreach (var lc in model.LoadCases)
            foreach (var pr in lc.Prescribed) if (pr.Dof is 0 or 1) Lateral(pr.NodeId);
        foreach (var rb in model.RigidBodies)
        {
            Lateral(rb.Master);
            foreach (int s in rb.Slaves) Lateral(s);
        }

        // ---- стержни
        bool Level(int n)
        {
            var at = vertAt[n];
            if (nonVertical[n] || lateral[n] || at is not { Count: 2 }) return true;
            return Math.Abs(Dot(beamDir[at[0]], beamDir[at[1]])) < KinkCos;
        }
        int Other(int b, int n) => beamNodes[b].I == n ? beamNodes[b].J : beamNodes[b].I;
        int Walk(int b, int n)
        {
            for (int guard = 0; guard <= nb && !Level(n); guard++)
            {
                b = vertAt[n]![0] == b ? vertAt[n]![1] : vertAt[n]![0];
                n = Other(b, n);
            }
            return n;
        }
        var segments = new HashSet<(int, int)>();
        int bars = 0;
        for (int b = 0; b < nb; b++)
        {
            if (!beamVertical[b]) continue;
            var (i, j) = beamNodes[b];
            bool iLow = p[i][2] <= p[j][2];
            int a = Walk(b, iLow ? i : j), top = Walk(b, iLow ? j : i);
            var axis = Sub(p[top], p[a]);
            double l = Norm(axis);
            if (l <= tol) continue;
            axis = axis.Select(x => x / l).ToArray();
            double Zeta(int n) => Dot(Sub(p[n], p[a]), axis) / l;
            chords[ns + b] = new ElementChord([ChordPoint.At(a), ChordPoint.At(a)], [ChordPoint.At(top), ChordPoint.At(top)],
                [Zeta(i), Zeta(j)], axis, l, Rotations: true);
            segments.Add((a, top));
            bars++;
        }

        // ---- стены
        var parent = Enumerable.Range(0, ns).ToArray();
        int Find(int x) { while (parent[x] != x) x = parent[x] = parent[parent[x]]; return x; }
        var edgeOwner = new Dictionary<(int, int), int>();
        for (int s = 0; s < ns; s++)
        {
            if (!shellVertical[s]) continue;
            var nodes = shellNodes[s];
            for (int k = 0; k < nodes.Length; k++)
            {
                int u = nodes[k], v = nodes[(k + 1) % nodes.Length];
                var key = u < v ? (u, v) : (v, u);
                if (!edgeOwner.TryGetValue(key, out int o)) { edgeOwner[key] = s; continue; }
                bool coplanar = Math.Abs(Dot(normals[o], normals[s])) >= ParallelCos
                    && Math.Abs(Dot(Sub(Centroid(shellNodes[s], p), p[shellNodes[o][0]]), normals[o])) <= 1e3 * tol;
                if (coplanar) parent[Find(s)] = Find(o);
            }
        }
        int shellsDone = 0, wallLevels = 0;
        var noLine = new List<int>();
        foreach (var group in Enumerable.Range(0, ns).Where(s => shellVertical[s]).GroupBy(Find))
        {
            var members = group.ToList();
            var n0 = normals[members[0]];
            var h = Unit([n0[1], -n0[0], 0.0]);   // n × ẑ — горизонталь вдоль стены
            var nodes = members.SelectMany(s => shellNodes[s]).Distinct().ToList();
            var zs = new List<double> { nodes.Min(n => p[n][2]), nodes.Max(n => p[n][2]) };
            zs.AddRange(nodes.Where(n => nonVerticalShell[n]).Select(n => p[n][2]));
            var levels = new List<double>();
            foreach (double z in zs.OrderBy(z => z))
                if (levels.Count == 0 || z - levels[^1] > 1e3 * tol) levels.Add(z);
            wallLevels += levels.Count;
            var lines = levels.Select(z => nodes.Where(n => Math.Abs(p[n][2] - z) <= 1e3 * tol)
                .Select(n => (S: Dot(p[n], h), Node: n)).OrderBy(x => x.S).ToArray()).ToArray();
            foreach (int s in members)
            {
                var sn = shellNodes[s];
                double zlo = sn.Min(n => p[n][2]), zhi = sn.Max(n => p[n][2]);
                int kb = levels.FindLastIndex(z => z <= zlo + 1e3 * tol);
                int kt = levels.FindIndex(z => z >= zhi - 1e3 * tol);
                if (kb < 0 || kt < 0 || kt <= kb) { noLine.Add(model.Shells[s].Id); continue; }
                double hz = levels[kt] - levels[kb];
                var bottom = new ChordPoint[sn.Length];
                var topPts = new ChordPoint[sn.Length];
                var zeta = new double[sn.Length];
                bool ok = true;
                for (int k = 0; k < sn.Length && ok; k++)
                {
                    double sc = Dot(p[sn[k]], h);
                    var bp = Interpolate(lines[kb], sc, 1e3 * tol);
                    var tp = Interpolate(lines[kt], sc, 1e3 * tol);
                    if (bp == null || tp == null) { ok = false; break; }
                    (bottom[k], topPts[k], zeta[k]) = (bp, tp, (p[sn[k]][2] - levels[kb]) / hz);
                }
                if (!ok) { noLine.Add(model.Shells[s].Id); continue; }
                chords[s] = new ElementChord(bottom, topPts, zeta, [0.0, 0.0, 1.0], hz, Rotations: false);
                shellsDone++;
            }
        }
        return new RcLocalPDeltaResult(chords, bars, shellsDone, segments.Count, wallLevels, noLine);
    }

    // Точка линии уровня с плановой координатой s: узел линии (|Δs| ≤ tol) или интерполяция по соседним; вне охвата — null.
    private static ChordPoint? Interpolate((double S, int Node)[] line, double s, double tol)
    {
        if (line.Length == 0) return null;
        for (int j = 0; j < line.Length; j++)
            if (Math.Abs(line[j].S - s) <= tol) return ChordPoint.At(line[j].Node);
        for (int j = 0; j + 1 < line.Length; j++)
            if (line[j].S < s && s < line[j + 1].S)
            {
                double w = (s - line[j].S) / (line[j + 1].S - line[j].S);
                return new ChordPoint([line[j].Node, line[j + 1].Node], [1 - w, w]);
            }
        return null;
    }

    private static double[] Normal(int[] nodes, double[][] p)
    {
        // Ньюэлл: нормаль многоугольника.
        var n = new double[3];
        for (int k = 0; k < nodes.Length; k++)
        {
            var a = p[nodes[k]];
            var b = p[nodes[(k + 1) % nodes.Length]];
            n[0] += (a[1] - b[1]) * (a[2] + b[2]);
            n[1] += (a[2] - b[2]) * (a[0] + b[0]);
            n[2] += (a[0] - b[0]) * (a[1] + b[1]);
        }
        return Unit(n);
    }

    private static double[] Centroid(int[] nodes, double[][] p)
    {
        var c = new double[3];
        foreach (int n in nodes) for (int k = 0; k < 3; k++) c[k] += p[n][k] / nodes.Length;
        return c;
    }

    private static double[] Sub(double[] a, double[] b) => [a[0] - b[0], a[1] - b[1], a[2] - b[2]];
    private static double Dot(double[] a, double[] b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
    private static double Norm(double[] a) => Math.Sqrt(Dot(a, a));
    private static double[] Unit(double[] a)
    {
        double l = Norm(a);
        return l > 0 ? [a[0] / l, a[1] / l, a[2] / l] : [0.0, 0.0, 0.0];
    }
}
