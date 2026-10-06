using CSfea.Sparse;

namespace CSfea.Core;

/// <summary>Оболочечный КЭ совместной сетки: узлы контура (3 или 4, по обходу) и сечение.</summary>
public sealed record StructuralShell(int[] Nodes, IShellSectionResponse Section);

/// <summary>
/// Стержневой КЭ совместной сетки: узлы концов, сечение и опорный вектор ориентации
/// локальной оси y (как в <see cref="BeamElements.Beam3dFrame"/>; null — по умолчанию).
/// </summary>
public sealed record StructuralBeam(int I, int J, IBeamSectionResponse Section, double[]? RefVec = null);

/// <summary>
/// Совместная сетка оболочек и пространственных стержней с общими узлами (6 DOF/узел:
/// ux, uy, uz, θx, θy, θz). Элементные матрицы — существующие <see cref="ShellElementForces"/>,
/// <see cref="ShellCorotational"/>, <see cref="BeamElements"/>, <see cref="BeamCorotational"/>.
/// Решатели: линейный и шаговый Ньютон (геометрическая нелинейность: оболочки — CR или
/// фон Карман, стержни — CR) с признаком сходимости шага. Жёсткие тела — исключением ведомых DOF
/// (<see cref="RigidLinks"/>, линеаризованная связь); решатели работают в редуцированном пространстве,
/// перемещения возвращаются полными (с ведомыми узлами).
/// </summary>
public sealed class StructuralMesh : IFeaMesh
{
    /// <summary>DOF на узел.</summary>
    public int DofsPerNode => 6;

    /// <summary>Координаты узлов (N строк по 3).</summary>
    public double[][] Nodes { get; }

    /// <summary>Оболочечные КЭ.</summary>
    public IReadOnlyList<StructuralShell> Shells { get; }

    /// <summary>Стержневые КЭ.</summary>
    public IReadOnlyList<StructuralBeam> Beams { get; }

    /// <summary>Число узлов.</summary>
    public int NNodes => Nodes.Length;

    /// <summary>Полное число степеней свободы.</summary>
    public int NDof => 6 * NNodes;

    /// <summary>Жёсткие связи (null — нет).</summary>
    public RigidLinks? Links { get; }

    public StructuralMesh(double[][] nodes, IReadOnlyList<StructuralShell>? shells,
                          IReadOnlyList<StructuralBeam>? beams,
                          IReadOnlyList<RigidLink>? rigidLinks = null)
    {
        Links = rigidLinks is { Count: > 0 } ? new RigidLinks(nodes, rigidLinks) : null;
        Nodes = nodes;
        Shells = shells ?? Array.Empty<StructuralShell>();
        Beams = beams ?? Array.Empty<StructuralBeam>();
        foreach (var s in Shells)
        {
            if (s.Nodes.Length is not (3 or 4))
                throw new ArgumentException($"Оболочечный КЭ должен иметь 3 или 4 узла, задано {s.Nodes.Length}.");
            CheckNodes(s.Nodes);
        }
        foreach (var b in Beams)
        {
            CheckNodes(new[] { b.I, b.J });
            if (b.I == b.J) throw new ArgumentException($"Стержень с совпадающими узлами {b.I}.");
        }
    }

    private void CheckNodes(int[] ids)
    {
        foreach (int n in ids)
            if (n < 0 || n >= Nodes.Length)
                throw new ArgumentException($"Узел {n} вне диапазона 0..{Nodes.Length - 1}.");
    }

    /// <summary>Глобальные DOF набора узлов (6 на узел).</summary>
    public static int[] NodeDofs(IReadOnlyList<int> nodes)
    {
        var dofs = new int[6 * nodes.Count];
        for (int i = 0; i < nodes.Count; i++)
            for (int c = 0; c < 6; c++)
                dofs[6 * i + c] = 6 * nodes[i] + c;
        return dofs;
    }

    /// <summary>Координаты узлов оболочечного КЭ.</summary>
    public double[][] ShellCoords(int e)
    {
        var el = Shells[e].Nodes;
        var c = new double[el.Length][];
        for (int i = 0; i < el.Length; i++) c[i] = Nodes[el[i]];
        return c;
    }

    /// <summary>Координаты концов стержневого КЭ.</summary>
    public double[][] BeamCoords(int e) => new[] { Nodes[Beams[e].I], Nodes[Beams[e].J] };

    private static int[] BeamDofs(StructuralBeam b) => NodeDofs(new[] { b.I, b.J });

    // ---------------- сборка ----------------

    /// <summary>Линейная K (COO): оболочки — касательная при u = 0, стержни — линейный КЭ.</summary>
    public CooMatrix AssembleK()
    {
        var coo = NewCoo();
        for (int e = 0; e < Shells.Count; e++)
        {
            var coords = ShellCoords(e);
            var ke = ShellElementForces.ElementKTangentGlobal(coords, Shells[e].Section, new double[6 * coords.Length]);
            coo.AddBlock(NodeDofs(Shells[e].Nodes), ke);
        }
        for (int e = 0; e < Beams.Count; e++)
        {
            var b = Beams[e];
            coo.AddBlock(BeamDofs(b), BeamElements.Beam3dKGlobal(BeamCoords(e), b.Section, b.RefVec));
        }
        return coo;
    }

    /// <summary>
    /// Вектор внутренних сил F_int(u). <paramref name="corotational"/>: оболочки — CR
    /// (true) или фон Карман (false); стержни — всегда CR.
    /// </summary>
    public double[] AssembleFInternal(double[] u, bool corotational = true)
    {
        var f = new double[NDof];
        for (int e = 0; e < Shells.Count; e++)
        {
            var dofs = NodeDofs(Shells[e].Nodes);
            var ue = Gather(u, dofs);
            var fe = corotational
                ? ShellCorotational.ElementCR(ShellCoords(e), Shells[e].Section, ue).FGlobal
                : ShellElementForces.ElementFInternalGlobal(ShellCoords(e), Shells[e].Section, ue);
            for (int i = 0; i < dofs.Length; i++) f[dofs[i]] += fe[i];
        }
        for (int e = 0; e < Beams.Count; e++)
        {
            var b = Beams[e];
            var dofs = BeamDofs(b);
            var fe = BeamCorotational.Beam3dInternalForce(BeamCoords(e), b.Section, Gather(u, dofs), b.RefVec);
            for (int i = 0; i < dofs.Length; i++) f[dofs[i]] += fe[i];
        }
        return f;
    }

    /// <summary>Касательная K_T(u) (COO); кинематика — как в <see cref="AssembleFInternal"/>.</summary>
    public CooMatrix AssembleKTangent(double[] u, bool corotational = true)
    {
        var coo = NewCoo();
        for (int e = 0; e < Shells.Count; e++)
        {
            var dofs = NodeDofs(Shells[e].Nodes);
            var ue = Gather(u, dofs);
            var ke = corotational
                ? ShellCorotational.ElementCR(ShellCoords(e), Shells[e].Section, ue).KGlobal
                : ShellElementForces.ElementKTangentGlobal(ShellCoords(e), Shells[e].Section, ue);
            coo.AddBlock(dofs, ke);
        }
        for (int e = 0; e < Beams.Count; e++)
        {
            var b = Beams[e];
            var dofs = BeamDofs(b);
            coo.AddBlock(dofs, BeamCorotational.Beam3dTangent(BeamCoords(e), b.Section, Gather(u, dofs), b.RefVec));
        }
        return coo;
    }

    private CooMatrix NewCoo()
        => new(NDof, NDof, Shells.Count * 24 * 24 + Beams.Count * 12 * 12);

    /// <summary>Зафиксировать состояние сечений после шага (каждое сечение — один раз).</summary>
    public void CommitStep(double[] u)
    {
        foreach (var s in DistinctShellSections()) s.Commit();
        foreach (var b in DistinctBeamSections()) b.Commit();
    }

    /// <summary>Сбросить состояние всех сечений.</summary>
    public void ResetSectionHistory()
    {
        foreach (var s in DistinctShellSections()) s.Reset();
        foreach (var b in DistinctBeamSections()) b.Reset();
    }

    private IEnumerable<IShellSectionResponse> DistinctShellSections()
        => Shells.Select(s => s.Section).Distinct(ReferenceEqualityComparer.Instance).Cast<IShellSectionResponse>();

    private IEnumerable<IBeamSectionResponse> DistinctBeamSections()
        => Beams.Select(b => b.Section).Distinct(ReferenceEqualityComparer.Instance).Cast<IBeamSectionResponse>();

    // ---------------- решатели ----------------

    /// <summary>Линейная статика K·u = F с ГУ (Дирихле + линейные пружины).</summary>
    public double[] SolveLinear(double[] f, BoundaryConditions bc)
    {
        if (bc.HasNonlinearSprings)
            throw new InvalidOperationException(
                "SolveLinear не поддерживает нелинейные пружины — используйте SolveNonlinear.");
        var k = AssembleK();
        var kSpring = bc.AssembleKSpring();
        if (kSpring.Count > 0) AppendInto(k, kSpring);
        var fixedSys = SysFixed(bc.FixedDofs);
        var reduced = DirichletReducer.Reduce(SysMatrix(k), SysVector(f), fixedSys, bc.UFixed);
        var uFree = SparseLuSolver.SolveOnce(reduced.Kff, reduced.Fmod);
        return Full(DirichletReducer.Expand(NSys, reduced.Free, uFree, fixedSys, bc.UFixed));
    }

    /// <summary>
    /// Опорные реакции на закреплённых DOF (с силами, переданными через жёсткие связи на ведущие
    /// узлы): R = [Tᵀ·(F_int + K_spring·u + F_nl − F)] на закреплённых DOF, остальные — 0. F_int по
    /// умолчанию K·u (линейный расчёт); для нелинейного передать <paramref name="fInternal"/>.
    /// <paramref name="fExternal"/> — внешняя нагрузка: с ней реакция полная (включает нагрузку,
    /// приложенную прямо в опорном узле), без неё — как <see cref="Reactions.Compute"/>.
    /// </summary>
    public double[] ComputeReactions(double[] u, BoundaryConditions bc, double[]? fInternal = null,
                                     double[]? fExternal = null)
    {
        var total = fInternal != null ? (double[])fInternal.Clone() : AssembleK().ToCsc().Multiply(u);
        var kSpring = bc.AssembleKSpring();
        if (kSpring.Count > 0)
        {
            var ks = kSpring.ToCsc().Multiply(u);
            for (int i = 0; i < total.Length; i++) total[i] += ks[i];
        }
        if (bc.HasNonlinearSprings)
        {
            var fnl = bc.AssembleFSpringNonlinear(u);
            for (int i = 0; i < total.Length; i++) total[i] += fnl[i];
        }
        if (fExternal != null)
            for (int i = 0; i < total.Length; i++) total[i] -= fExternal[i];
        var sys = SysVector(total);
        var r = new double[NDof];
        foreach (int d in bc.FixedDofs)
            r[d] = sys[Links?.ToReducedIndex(d) ?? d];
        return r;
    }

    // ---------------- пространство решения (с учётом жёстких связей) ----------------

    private int NSys => Links?.NReduced ?? NDof;
    private CooMatrix SysMatrix(CooMatrix k) => Links?.ReduceMatrix(k) ?? k;
    private double[] SysVector(double[] f) => Links?.ReduceVector(f) ?? f;
    private double[] Full(double[] uSys) => Links?.Expand(uSys) ?? uSys;
    private int[] SysFixed(int[] fixedDofs) => Links?.ReduceFixedDofs(fixedDofs) ?? fixedDofs;

    /// <summary>
    /// Шаговый Ньютон с backtracking line search. Нагрузка шага s: F₀ + (s/nSteps)·(F − F₀),
    /// где F₀ — нагрузка начального состояния <paramref name="u0"/> (по умолчанию 0); предписанные
    /// смещения — так же. История — <see cref="ShellMesh.NewtonRecord"/> с признаком сходимости
    /// (сводка — <see cref="NonlinearConvergence"/>); несошедшийся шаг не прерывает расчёт.
    /// </summary>
    public (double[] U, List<ShellMesh.NewtonRecord> History) SolveNonlinear(
        double[] f, BoundaryConditions bc,
        int nSteps = 10, double tol = 1e-6, int maxIter = 25,
        bool lineSearch = true, int maxLsSteps = 15, bool corotational = true,
        double[]? u0 = null, double[]? f0 = null, bool verbose = false)
    {
        int ndof = NDof;
        var fixedDofs = SysFixed(bc.FixedDofs);   // индексы в пространстве решения
        var uFixedArr = bc.UFixed;
        int[] free = DirichletReducer.FreeDofs(NSys, fixedDofs);

        var kSpringLinCoo = bc.AssembleKSpring();
        var kSpringLinCsc = kSpringLinCoo.Count > 0 ? kSpringLinCoo.ToCsc() : null;

        // uSys — неизвестные решения; u = T·uSys — полные перемещения (с ведомыми узлами).
        var uSys = u0 == null ? new double[NSys] : Links != null ? Links.Restrict(u0) : (double[])u0.Clone();
        var u = Full(uSys);
        var fStart = f0 ?? new double[ndof];
        var uFixedStart = fixedDofs.Select(d => uSys[d]).ToArray();
        var history = new List<ShellMesh.NewtonRecord>();

        double[] FIntTotal(double[] uu)
        {
            var fi = AssembleFInternal(uu, corotational);
            if (kSpringLinCsc != null)
            {
                var ks = kSpringLinCsc.Multiply(uu);
                for (int i = 0; i < fi.Length; i++) fi[i] += ks[i];
            }
            if (bc.HasNonlinearSprings)
            {
                var fnl = bc.AssembleFSpringNonlinear(uu);
                for (int i = 0; i < fi.Length; i++) fi[i] += fnl[i];
            }
            return fi;
        }

        double[] Residual(double[] fStep) => SysVector(Dense.SubV(fStep, FIntTotal(u)));

        void SetFree(double[] uBak, double[] du, double alpha)
        {
            for (int i = 0; i < free.Length; i++) uSys[free[i]] = uBak[i] + alpha * du[i];
            u = Full(uSys);
        }

        double fNorm = Math.Max(NormAt(SysVector(f), free), 1.0);

        for (int step = 1; step <= nSteps; step++)
        {
            double lam = (double)step / nSteps;
            var fStep = new double[ndof];
            for (int i = 0; i < ndof; i++) fStep[i] = fStart[i] + lam * (f[i] - fStart[i]);
            for (int t = 0; t < fixedDofs.Length; t++)
                uSys[fixedDofs[t]] = uFixedStart[t] + lam * (uFixedArr[t] - uFixedStart[t]);
            u = Full(uSys);

            bool converged = false;
            for (int it = 1; it <= maxIter; it++)
            {
                var r = Residual(fStep);
                double resid = NormAt(r, free) / fNorm;
                bool ok = resid < tol;
                history.Add(new ShellMesh.NewtonRecord(step, it, resid, ok));
                if (verbose)
                    Console.WriteLine($"  step {step}/{nSteps}  iter {it,2}  ||r||/||F||={resid:e3}");
                if (ok) { converged = true; break; }
                if (it == maxIter) break;

                var kt = AssembleKTangent(u, corotational);
                if (kSpringLinCoo.Count > 0) AppendInto(kt, kSpringLinCoo);
                if (bc.HasNonlinearSprings) AppendInto(kt, bc.AssembleKSpringTangent(u));
                var reduced = DirichletReducer.Reduce(SysMatrix(kt), r, fixedDofs, null);
                double[] duFree;
                try { duFree = SparseLuSolver.SolveOnce(reduced.Kff, reduced.Fmod); }
                catch (InvalidOperationException) { break; }
                if (!duFree.All(double.IsFinite)) break;

                var uBak = free.Select(i => uSys[i]).ToArray();
                double alpha = 1.0;
                if (lineSearch)
                {
                    bool accepted = false;
                    for (int ls = 0; ls < maxLsSteps; ls++)
                    {
                        SetFree(uBak, duFree, alpha);
                        var rTrial = Residual(fStep);
                        if (IsFinite(rTrial, free) && NormAt(rTrial, free) / fNorm < resid) { accepted = true; break; }
                        alpha *= 0.5;
                    }
                    if (accepted) continue;
                }
                SetFree(uBak, duFree, alpha);
            }
            if (verbose && !converged)
                Console.WriteLine($"  step {step}: не сошёлся за {maxIter} итераций");
            bc.CommitStep(u);
            CommitStep(u);
        }
        return (u, history);
    }

    // ---------------- утилиты ----------------

    private static void AppendInto(CooMatrix target, CooMatrix src)
    {
        var csc = src.ToCsc();
        for (int c = 0; c < csc.Cols; c++)
            for (int p = csc.ColPtr[c]; p < csc.ColPtr[c + 1]; p++)
                target.Add(csc.RowIdx[p], c, csc.Values[p]);
    }

    private static double[] Gather(double[] u, int[] dofs)
    {
        var r = new double[dofs.Length];
        for (int i = 0; i < dofs.Length; i++) r[i] = u[dofs[i]];
        return r;
    }

    private static double NormAt(double[] v, int[] idx)
    {
        double s = 0.0;
        foreach (int i in idx) s += v[i] * v[i];
        return Math.Sqrt(s);
    }

    private static bool IsFinite(double[] v, int[] idx)
    {
        foreach (int i in idx) if (!double.IsFinite(v[i])) return false;
        return true;
    }
}
