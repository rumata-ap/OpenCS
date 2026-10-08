using CSfea.Core;
using CSfea.Sparse;

namespace CSfea.CScoreBridge.Structural;

/// <summary>
/// Фабрика откликов сечений по варианту расчёта (линейный / секущий / прямой). Сечение оболочки возвращается в
/// осях сечения — поворот в оси КЭ выполняет построитель. Фабрика сама решает, делить ли отклик между КЭ
/// (у stateful-сечений нелинейных вариантов он свой на каждый КЭ).
/// </summary>
public interface IRcSectionFactory
{
    IShellSectionResponse Shell(RcShell shell);
    IBeamSectionResponse Beam(RcBeam beam);
}

/// <summary>
/// Линейные упругие сечения: оболочка — <see cref="RcShellSection.Elastic"/> или однородный изотропный слой бетона
/// (E и ν из <see cref="PlateSectionMaterials"/>, толщина <see cref="CScore.PlateSection.H"/>; арматура не
/// учитывается — как упругая жёсткость SCAD/ЛИРЫ); стержень — <see cref="RcBeamSection.Elastic"/>.
/// </summary>
public sealed class LinearRcSectionFactory : IRcSectionFactory
{
    private readonly Dictionary<string, IShellSectionResponse> _shells = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IBeamSectionResponse> _beams = new(StringComparer.Ordinal);

    public IShellSectionResponse Shell(RcShell shell)
    {
        var s = shell.Section;
        if (_shells.TryGetValue(s.Key, out var cached)) return cached;
        Laminate lam;
        if (s.Elastic != null) lam = s.Elastic;
        else if (s.Plate != null && s.PlateMaterials != null)
        {
            double e = s.PlateMaterials.ConcreteE_MPa * 1e6, nu = s.PlateMaterials.Nu;
            lam = new Laminate(new[] { new Ply(new OrthotropicMaterial(e, e, nu, e / (2 * (1 + nu))), 0.0, s.Plate.H) },
                               s.PlateMaterials.KShear);
        }
        else throw new InvalidOperationException($"Оболочка {shell.Id}: сечение «{s.Key}» не задано (нет ламината и PlateSection).");
        return _shells[s.Key] = new LinearLaminateResponse(lam);
    }

    public IBeamSectionResponse Beam(RcBeam beam)
    {
        var s = beam.Section;
        if (_beams.TryGetValue(s.Key, out var cached)) return cached;
        if (s.Elastic == null)
            throw new NotSupportedException(
                $"Стержень {beam.Id}: линейный расчёт сечения CScore «{s.Key}» пока не поддержан — задайте упругое сечение.");
        return _beams[s.Key] = new LinearBeamResponse(s.Elastic);
    }
}

/// <summary>Сетка, ГУ и векторы нагрузок, построенные из <see cref="RcStructuralModel"/>.</summary>
public sealed class RcStructuralMeshBuild
{
    public required StructuralMesh Mesh { get; init; }
    public required BoundaryConditions Bc { get; init; }

    /// <summary>Номер узла модели → индекс узла сетки.</summary>
    public required IReadOnlyDictionary<int, int> NodeIndex { get; init; }

    /// <summary>Номера узлов модели по индексам сетки.</summary>
    public required int[] NodeIds { get; init; }

    /// <summary>Номера оболочек и стержней модели по индексам КЭ сетки.</summary>
    public required int[] ShellIds { get; init; }
    public required int[] BeamIds { get; init; }

    /// <summary>Узловые векторы загружений (Н, Н·м; длина — NDof сетки).</summary>
    public required IReadOnlyDictionary<int, double[]> LoadCases { get; init; }

    /// <summary>Замечания построения (автоматические закрепления и т. п.).</summary>
    public required IReadOnlyList<string> Report { get; init; }

    /// <summary>Глобальный DOF узла модели.</summary>
    public int Dof(int nodeId, int component) => 6 * NodeIndex[nodeId] + component;

    /// <summary>Сумма загружений с коэффициентами.</summary>
    public double[] Combination(IEnumerable<(int LoadCase, double Factor)> loads)
    {
        var f = new double[Mesh.NDof];
        foreach (var (lc, k) in loads)
        {
            if (!LoadCases.TryGetValue(lc, out var v)) throw new InvalidOperationException($"Нет загружения {lc}.");
            for (int i = 0; i < f.Length; i++) f[i] += k * v[i];
        }
        return f;
    }
}

/// <summary>
/// Построитель <see cref="StructuralMesh"/> из <see cref="RcStructuralModel"/>: сечения — через фабрику варианта
/// (оболочки поворачиваются из осей сечения в оси КЭ, <see cref="RotatedShellResponse"/>), закрепления и пружины —
/// в <see cref="BoundaryConditions"/>, жёсткие тела — в <see cref="RigidLink"/>, распределённые нагрузки — в
/// согласованные узловые силы (оболочки — ∫Nᵢ dA по функциям формы Q4 (Гаусс 2 × 2) / T3, стержни — концевые силы
/// и моменты wL²/12).
/// </summary>
public static class RcStructuralMeshBuilder
{
    public static RcStructuralMeshBuild Build(RcStructuralModel model, IRcSectionFactory factory)
    {
        var report = new List<string>();
        var nodeIds = model.Nodes.Select(n => n.Id).ToArray();
        var index = new Dictionary<int, int>();
        for (int i = 0; i < nodeIds.Length; i++)
            if (!index.TryAdd(nodeIds[i], i)) throw new ArgumentException($"Повтор узла {nodeIds[i]}.");
        int Idx(int id, string owner) => index.TryGetValue(id, out int i) ? i
            : throw new ArgumentException($"{owner}: нет узла {id}.");
        var coords = model.Nodes.Select(n => new[] { n.X, n.Y, n.Z }).ToArray();

        var shells = new List<StructuralShell>(model.Shells.Count);
        foreach (var s in model.Shells)
        {
            var nodes = s.NodeIds.Select(id => Idx(id, $"Оболочка {s.Id}")).ToArray();
            var resp = factory.Shell(s);
            if (s.SectionAxisX != null)
            {
                double alpha = SectionAngle(nodes.Select(n => coords[n]).ToArray(), s.SectionAxisX, s.Id);
                if (Math.Abs(alpha) > 1e-12) resp = new RotatedShellResponse(resp, alpha);
            }
            shells.Add(new StructuralShell(nodes, resp));
        }
        var beams = model.Beams.Select(b => new StructuralBeam(
            Idx(b.NodeI, $"Стержень {b.Id}"), Idx(b.NodeJ, $"Стержень {b.Id}"), factory.Beam(b), b.RefVec,
            b.ReleaseI, b.ReleaseJ)).ToArray();

        var links = new List<RigidLink>();
        foreach (var rb in model.RigidBodies)
        {
            int m = Idx(rb.Master, $"Жёсткое тело {rb.Id}");
            foreach (int s in rb.Slaves) links.Add(new RigidLink(m, Idx(s, $"Жёсткое тело {rb.Id}"), rb.Mask));
        }
        var mesh = new StructuralMesh(coords, shells, beams, links);

        var bc = new BoundaryConditions(mesh);
        foreach (var sup in model.Supports)
        {
            var dofs = Enumerable.Range(0, 6).Where(c => (sup.Mask & (1 << c)) != 0).ToArray();
            if (dofs.Length > 0) bc.Fix(new[] { Idx(sup.NodeId, "Опора") }, dofs);
        }
        foreach (var sp in model.Springs) bc.Spring(Idx(sp.NodeId, "Пружина"), sp.Dof, sp.Stiffness);
        AddFoundation(model, mesh, coords, bc, report);
        FixUnconnectedDofs(mesh, links, bc, nodeIds, report);
        FixReleasedDofs(mesh, links, bc, nodeIds, report);

        var loadCases = new Dictionary<int, double[]>();
        foreach (var lc in model.LoadCases)
            loadCases[lc.Id] = LoadVector(lc, model, mesh, index, coords);

        return new RcStructuralMeshBuild
        {
            Mesh = mesh, Bc = bc, NodeIndex = index, NodeIds = nodeIds,
            ShellIds = model.Shells.Select(s => s.Id).ToArray(), BeamIds = model.Beams.Select(b => b.Id).ToArray(),
            LoadCases = loadCases, Report = report,
        };
    }

    /// <summary>
    /// Угол (рад) от оси x КЭ CSfea до проекции <paramref name="axis"/> на плоскость КЭ, против часовой вокруг
    /// нормали КЭ.
    /// </summary>
    public static double SectionAngle(double[][] coords, double[] axis, int shellId = 0)
    {
        var r = ShellGeometry.LocalFrame(coords);
        double ax = axis[0] * r[0, 0] + axis[1] * r[0, 1] + axis[2] * r[0, 2];
        double ay = axis[0] * r[1, 0] + axis[1] * r[1, 1] + axis[2] * r[1, 2];
        if (Math.Sqrt(ax * ax + ay * ay) < 1e-9 * Math.Max(Dense.Norm(axis), 1e-300))
            throw new ArgumentException($"Оболочка {shellId}: ось сечения перпендикулярна плоскости КЭ.");
        return Math.Atan2(ay, ax);
    }

    /// <summary>
    /// DOF узлов, не примыкающих ни к одному КЭ и не подчинённых связи (одиночные узлы, свободные DOF ведомых без КЭ),
    /// не имеют жёсткости — закрепляются с записью в отчёт. Ведущий узел без КЭ получает жёсткость от ведомых.
    /// </summary>
    private static void FixUnconnectedDofs(StructuralMesh mesh, List<RigidLink> links, BoundaryConditions bc,
                                           int[] nodeIds, List<string> report)
    {
        var hasElement = new bool[mesh.NNodes];
        foreach (var s in mesh.Shells) foreach (int n in s.Nodes) hasElement[n] = true;
        foreach (var b in mesh.Beams) { hasElement[b.I] = true; hasElement[b.J] = true; }
        var masters = links.Select(l => l.Master).ToHashSet();
        var slaveMask = new int[mesh.NNodes];
        foreach (var l in links) slaveMask[l.Slave] |= l.Mask;
        var orphan = new List<int>();
        var partial = new List<int>();
        for (int n = 0; n < mesh.NNodes; n++)
        {
            if (hasElement[n] || masters.Contains(n)) continue;
            int freeMask = RigidLink.All & ~slaveMask[n];
            if (freeMask == 0) continue;
            bc.Fix(new[] { n }, Enumerable.Range(0, 6).Where(c => (freeMask & (1 << c)) != 0));
            (slaveMask[n] == 0 ? orphan : partial).Add(nodeIds[n]);
        }
        if (orphan.Count > 0)
            report.Add($"Узлы без КЭ и связей закреплены ({orphan.Count}): {Preview(orphan)}.");
        if (partial.Count > 0)
            report.Add($"Свободные DOF ведомых узлов без КЭ закреплены ({partial.Count}): {Preview(partial)}.");
    }

    /// <summary>
    /// Узлы, где шарниры всех сходящихся стержней оставили DOF без жёсткости (например, все стержни узла фермы
    /// освобождены по My и Mz): нулевой собственный вектор блока 6×6 узла (сконденсированные стержни + пружины, без
    /// закреплённых DOF). Вектор вдоль глобального DOF — DOF закрепляется (на решение не влияет: жёсткости и реакции по
    /// нему нет), запись в отчёт; косой вектор (стержни разной ориентации) одним DOF не закрепить — только запись в отчёт:
    /// система изменяема, как сообщил бы SCAD. Проверяются узлы у шарниров без оболочек и жёстких связей (оболочка даёт
    /// узлу жёсткость по всем DOF, у связей — свои DOF ведущего).
    /// </summary>
    private static void FixReleasedDofs(StructuralMesh mesh, List<RigidLink> links, BoundaryConditions bc,
                                        int[] nodeIds, List<string> report)
    {
        var candidates = new HashSet<int>();
        foreach (var b in mesh.Beams)
        {
            if (b.ReleaseI != 0) candidates.Add(b.I);
            if (b.ReleaseJ != 0) candidates.Add(b.J);
        }
        foreach (var s in mesh.Shells) foreach (int n in s.Nodes) candidates.Remove(n);
        foreach (var l in links) { candidates.Remove(l.Master); candidates.Remove(l.Slave); }
        if (candidates.Count == 0) return;

        var blocks = candidates.ToDictionary(n => n, _ => new double[6, 6]);
        for (int e = 0; e < mesh.Beams.Count; e++)
        {
            var b = mesh.Beams[e];
            bool hasI = blocks.TryGetValue(b.I, out var ki), hasJ = blocks.TryGetValue(b.J, out var kj);
            if (!hasI && !hasJ) continue;
            var k = BeamElements.Beam3dKGlobal(mesh.BeamCoords(e), b.Section, b.RefVec, b.Releases);
            for (int a = 0; a < 6; a++)
                for (int c = 0; c < 6; c++)
                {
                    if (hasI) ki![a, c] += k[a, c];
                    if (hasJ) kj![a, c] += k[6 + a, 6 + c];
                }
        }
        var springs = bc.AssembleKSpring().ToCsc();
        for (int col = 0; col < springs.Cols; col++)
            for (int p = springs.ColPtr[col]; p < springs.ColPtr[col + 1]; p++)
            {
                int row = springs.RowIdx[p];
                if (row / 6 == col / 6 && blocks.TryGetValue(col / 6, out var kn)) kn[row % 6, col % 6] += springs.Values[p];
            }

        var fixedSet = bc.FixedDofs.ToHashSet();
        var fixedDofs = new List<string>();
        var oblique = new List<int>();
        foreach (var (n, k) in blocks)
        {
            var free = Enumerable.Range(0, 6).Where(c => !fixedSet.Contains(6 * n + c)).ToArray();
            if (free.Length == 0) continue;
            // Поступательные и вращательные члены разной размерности: масштаб по диагонали (D·K·D, единичная диагональ)
            // нулевых векторов не меняет — у неотрицательно определённой матрицы нулевой диагональный член значит нулевую
            // строку.
            int nf = free.Length;
            var d = new double[nf];
            for (int a = 0; a < nf; a++)
            {
                double kaa = k[free[a], free[a]];
                d[a] = kaa > 0 ? 1 / Math.Sqrt(kaa) : 0.0;
            }
            var m = new double[nf, nf];
            for (int a = 0; a < nf; a++)
                for (int c = 0; c < nf; c++) m[a, c] = d[a] == 0 || d[c] == 0 ? 0.0 : k[free[a], free[c]] * d[a] * d[c];
            var (vals, vecs) = JacobiEigen(m);
            bool anyOblique = false;
            for (int v = 0; v < nf; v++)
            {
                if (vals[v] > 1e-9) continue;
                // Вектор в исходных DOF: x = D·y (у DOF с нулевой диагональю — сам y).
                var x = new double[nf];
                double norm = 0, maxAbs = 0;
                int big = 0;
                for (int a = 0; a < nf; a++)
                {
                    x[a] = d[a] == 0 ? vecs[a, v] : vecs[a, v] * d[a];
                    norm += x[a] * x[a];
                    if (Math.Abs(x[a]) > maxAbs) { maxAbs = Math.Abs(x[a]); big = a; }
                }
                if (maxAbs * maxAbs > (1 - 1e-9) * norm)
                {
                    bc.Fix(new[] { n }, new[] { free[big] });
                    fixedDofs.Add($"{nodeIds[n]} {DofNames[free[big]]}");
                }
                else anyOblique = true;
            }
            if (anyOblique) oblique.Add(nodeIds[n]);
        }
        if (fixedDofs.Count > 0)
            report.Add($"DOF узлов без жёсткости из-за шарниров стержней закреплены ({fixedDofs.Count}): {Preview(fixedDofs)}.");
        if (oblique.Count > 0)
            report.Add($"Узлы, изменяемые из-за шарниров стержней (нулевая жёсткость не по глобальной оси) ({oblique.Count}): " +
                       $"{Preview(oblique)}.");
    }

    private static readonly string[] DofNames = { "X", "Y", "Z", "UX", "UY", "UZ" };

    // Собственные числа и векторы (столбцы) малой симметричной матрицы — вращения Якоби.
    private static (double[] Values, double[,] Vectors) JacobiEigen(double[,] a0)
    {
        int n = a0.GetLength(0);
        var a = (double[,])a0.Clone();
        var v = new double[n, n];
        for (int i = 0; i < n; i++) v[i, i] = 1.0;
        for (int sweep = 0; sweep < 100; sweep++)
        {
            double off = 0;
            for (int p = 0; p < n; p++)
                for (int q = p + 1; q < n; q++) off += a[p, q] * a[p, q];
            if (off < 1e-30) break;
            for (int p = 0; p < n; p++)
                for (int q = p + 1; q < n; q++)
                {
                    if (Math.Abs(a[p, q]) < 1e-300) continue;
                    double theta = (a[q, q] - a[p, p]) / (2 * a[p, q]);
                    double t = Math.Sign(theta == 0 ? 1.0 : theta) / (Math.Abs(theta) + Math.Sqrt(theta * theta + 1));
                    double c = 1 / Math.Sqrt(t * t + 1), s = t * c;
                    for (int k = 0; k < n; k++)
                    {
                        double akp = a[k, p], akq = a[k, q];
                        a[k, p] = c * akp - s * akq;
                        a[k, q] = s * akp + c * akq;
                    }
                    for (int k = 0; k < n; k++)
                    {
                        double apk = a[p, k], aqk = a[q, k];
                        a[p, k] = c * apk - s * aqk;
                        a[q, k] = s * apk + c * aqk;
                    }
                    for (int k = 0; k < n; k++)
                    {
                        double vkp = v[k, p], vkq = v[k, q];
                        v[k, p] = c * vkp - s * vkq;
                        v[k, q] = s * vkp + c * vkq;
                    }
                }
        }
        var vals = new double[n];
        for (int i = 0; i < n; i++) vals[i] = a[i, i];
        return (vals, v);
    }

    /// <summary>
    /// Упругое основание оболочек (Винклер, C1 вдоль нормали КЭ) — узловыми пружинами-матрицами: узлу КЭ добавляется
    /// C1·wᵢ·n·nᵀ по поступательным DOF, wᵢ — доля площади узла (<see cref="AreaWeights"/>), n — нормаль КЭ; вклады
    /// смежных КЭ суммируются. Матрица сосредоточенная (диагональ по узлам), как у узловых связей основания SCAD/ЛИРЫ.
    /// </summary>
    private static void AddFoundation(RcStructuralModel model, StructuralMesh mesh, double[][] coords,
                                      BoundaryConditions bc, List<string> report)
    {
        var k = new Dictionary<int, double[,]>();
        int shells = 0;
        double area = 0;
        for (int e = 0; e < model.Shells.Count; e++)
        {
            if (model.Shells[e].FoundationC1 is not { } c1 || c1 == 0) continue;
            if (!(c1 > 0) || double.IsInfinity(c1))
                throw new ArgumentException($"Оболочка {model.Shells[e].Id}: коэффициент постели C1 = {c1} — нужен положительный.");
            var nodes = mesh.Shells[e].Nodes;
            var xyz = nodes.Select(n => coords[n]).ToArray();
            var n0 = Normal(xyz);
            var w = AreaWeights(xyz);
            for (int i = 0; i < nodes.Length; i++)
            {
                if (!k.TryGetValue(nodes[i], out var m)) k[nodes[i]] = m = new double[6, 6];
                for (int a = 0; a < 3; a++)
                    for (int b = 0; b < 3; b++) m[a, b] += c1 * w[i] * n0[a] * n0[b];
                area += w[i];
            }
            shells++;
        }
        foreach (var (node, m) in k) bc.SpringMatrix(node, m);
        if (shells > 0)
            report.Add($"Упругое основание C1: оболочек {shells}, площадь {area:F2} м², узлов {k.Count}.");
    }

    private static string Preview<T>(List<T> ids)
        => string.Join(", ", ids.Take(10)) + (ids.Count > 10 ? ", …" : "");

    private static double[] LoadVector(RcLoadCase lc, RcStructuralModel model, StructuralMesh mesh,
                                       Dictionary<int, int> index, double[][] coords)
    {
        var f = new double[mesh.NDof];
        foreach (var p in lc.Nodal)
        {
            if (!index.TryGetValue(p.NodeId, out int n)) throw new ArgumentException($"Загружение {lc.Id}: нет узла {p.NodeId}.");
            for (int c = 0; c < 6 && c < p.Force.Length; c++) f[6 * n + c] += p.Force[c];
        }
        var shellById = model.Shells.Select((s, i) => (s.Id, i)).ToDictionary(t => t.Id, t => t.i);
        foreach (var q in lc.Shells)
        {
            if (!shellById.TryGetValue(q.ShellId, out int e)) throw new ArgumentException($"Загружение {lc.Id}: нет оболочки {q.ShellId}.");
            var nodes = mesh.Shells[e].Nodes;
            var xyz = nodes.Select(n => coords[n]).ToArray();
            var dir = q.Direction ?? Normal(xyz);
            var w = AreaWeights(xyz);
            for (int k = 0; k < nodes.Length; k++)
                for (int c = 0; c < 3; c++) f[6 * nodes[k] + c] += q.Pressure * w[k] * dir[c];
        }
        var beamById = model.Beams.Select((b, i) => (b.Id, i)).ToDictionary(t => t.Id, t => t.i);
        foreach (var q in lc.Beams)
        {
            if (!beamById.TryGetValue(q.BeamId, out int e)) throw new ArgumentException($"Загружение {lc.Id}: нет стержня {q.BeamId}.");
            var b = mesh.Beams[e];
            var fe = BeamUniformLoad(new[] { coords[b.I], coords[b.J] }, q.Force, b.RefVec, b.Releases == 0 ? null : b);
            for (int c = 0; c < 6; c++) { f[6 * b.I + c] += fe[c]; f[6 * b.J + c] += fe[6 + c]; }
        }
        foreach (var q in lc.BeamEnds)
        {
            if (!beamById.TryGetValue(q.BeamId, out int e)) throw new ArgumentException($"Загружение {lc.Id}: нет стержня {q.BeamId}.");
            if (q.Forces.Length != 12) throw new ArgumentException($"Загружение {lc.Id}, стержень {q.BeamId}: нужно 12 концевых сил.");
            var b = mesh.Beams[e];
            var fe = b.Releases == 0 ? q.Forces : CondensedEndLoad(new[] { coords[b.I], coords[b.J] }, q.Forces, b);
            for (int c = 0; c < 6; c++) { f[6 * b.I + c] += fe[c]; f[6 * b.J + c] += fe[6 + c]; }
        }
        return f;
    }

    /// <summary>Концевые силы КЭ без шарниров (глобальные оси) → приведённые к сохранённым DOF шарнирного КЭ.</summary>
    private static double[] CondensedEndLoad(double[][] ends, double[] global, StructuralBeam released)
    {
        var (r, l) = BeamElements.Beam3dFrame(ends, released.RefVec);
        var local = new double[12];
        for (int blk = 0; blk < 4; blk++)
            for (int i = 0; i < 3; i++)
                for (int k = 0; k < 3; k++) local[3 * blk + i] += r[i, k] * global[3 * blk + k];
        local = BeamReleases.CondenseLoad(BeamElements.Beam3dKLocal(released.Section, l), released.Releases, local);
        var g = new double[12];
        for (int blk = 0; blk < 4; blk++)
            for (int i = 0; i < 3; i++)
                for (int k = 0; k < 3; k++) g[3 * blk + i] += r[k, i] * local[3 * blk + k];
        return g;
    }

    private static double[] Normal(double[][] xyz)
    {
        var r = ShellGeometry.LocalFrame(xyz);
        return new[] { r[2, 0], r[2, 1], r[2, 2] };
    }

    /// <summary>Доли площади на узлы: Q4 — ∫Nᵢ dA (билинейная интерполяция, Гаусс 2 × 2), T3 — A/3.</summary>
    public static double[] AreaWeights(double[][] p)
    {
        if (p.Length == 3)
        {
            double a = 0.5 * Dense.Norm(Dense.Cross(Dense.SubV(p[1], p[0]), Dense.SubV(p[2], p[0])));
            return new[] { a / 3, a / 3, a / 3 };
        }
        var w = new double[4];
        double g = 1 / Math.Sqrt(3);
        double[] xi = { -1, 1, 1, -1 }, eta = { -1, -1, 1, 1 };
        foreach (double gx in new[] { -g, g })
            foreach (double gy in new[] { -g, g })
            {
                var dx = new double[3];
                var dy = new double[3];
                var n = new double[4];
                for (int k = 0; k < 4; k++)
                {
                    n[k] = 0.25 * (1 + xi[k] * gx) * (1 + eta[k] * gy);
                    for (int c = 0; c < 3; c++)
                    {
                        dx[c] += p[k][c] * 0.25 * xi[k] * (1 + eta[k] * gy);
                        dy[c] += p[k][c] * 0.25 * eta[k] * (1 + xi[k] * gx);
                    }
                }
                double j = Dense.Norm(Dense.Cross(dx, dy));
                for (int k = 0; k < 4; k++) w[k] += n[k] * j;
            }
        return w;
    }

    /// <summary>
    /// Согласованные узловые силы стержня (12, глобальные) от равномерной погонной нагрузки w (глобальные оси):
    /// силы wL/2 на концы, моменты Mz = ±w_y·L²/12, My = ∓w_z·L²/12 в локальных осях (w' = −θy). У стержня с шарнирами
    /// (<paramref name="released"/>) нагрузка приводится к сохранённым DOF его матрицей (<see cref="BeamReleases.CondenseLoad"/>):
    /// у шарнирного конца момент закрепления переходит в поперечные силы — wL/2 ± wL/8 у балки с одним шарниром.
    /// </summary>
    public static double[] BeamUniformLoad(double[][] ends, double[] wGlobal, double[]? refVec,
                                           StructuralBeam? released = null)
    {
        var (r, l) = BeamElements.Beam3dFrame(ends, refVec);
        var wl = Dense.MatVec(r, wGlobal);
        var local = new double[12];
        for (int c = 0; c < 3; c++) { local[c] = wl[c] * l / 2; local[6 + c] = wl[c] * l / 2; }
        double m = l * l / 12;
        local[5] = wl[1] * m; local[11] = -wl[1] * m;
        local[4] = -wl[2] * m; local[10] = wl[2] * m;
        if (released is { Releases: not 0 })
            local = BeamReleases.CondenseLoad(BeamElements.Beam3dKLocal(released.Section, l), released.Releases, local);
        var g = new double[12];
        for (int blk = 0; blk < 4; blk++)
            for (int i = 0; i < 3; i++)
                for (int k = 0; k < 3; k++) g[3 * blk + i] += r[k, i] * local[3 * blk + k];
        return g;
    }
}
