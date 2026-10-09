using CSfea.Sparse;

namespace CSfea.Core;

/// <summary>Фазы линейного решения, с: сборка K_ff, численная факторизация, всё решение (с подстановками).</summary>
public sealed record LinearSolveTimings(double Assemble, double Factorize, double Total);

/// <summary>
/// Раскладка последнего <see cref="StructuralMesh.SolveNonlinear"/>, с: решений системы (шагов Ньютона), сборок F_int
/// (невязки, включая пробы line search), сборка касательной, факторизация, F_int, всего.
/// </summary>
public sealed record NewtonSolveTimings(int Solves, int FIntCalls, double Tangent, double Factorize, double FInt, double Total);

/// <summary>Оболочечный КЭ совместной сетки: узлы контура (3 или 4, по обходу) и сечение.</summary>
public sealed record StructuralShell(int[] Nodes, IShellSectionResponse Section);

/// <summary>
/// Стержневой КЭ совместной сетки: узлы концов, сечение и опорный вектор ориентации
/// локальной оси y (как в <see cref="BeamElements.Beam3dFrame"/>; null — по умолчанию).
/// <paramref name="ReleaseI"/>, <paramref name="ReleaseJ"/> — шарниры концов: маски освобождённых местных DOF (биты 0–5:
/// u, v, w, θx, θy, θz — усилия N, Qy, Qz, T, My, Mz), см. <see cref="BeamReleases"/>.
/// </summary>
public sealed record StructuralBeam(int I, int J, IBeamSectionResponse Section, double[]? RefVec = null,
    int ReleaseI = 0, int ReleaseJ = 0)
{
    /// <summary>12-битная маска шарниров КЭ (<see cref="BeamReleases.Mask"/>).</summary>
    public int Releases => BeamReleases.Mask(ReleaseI, ReleaseJ);
}

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
            _ = b.Releases;   // проверка масок
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

    /// <summary>Число КЭ: сначала оболочки, затем стержни (единая нумерация <see cref="ElementDofs"/>, <see cref="ElementK"/>).</summary>
    public int ElementCount => Shells.Count + Beams.Count;

    /// <summary>Глобальные DOF КЭ единой нумерации.</summary>
    public int[] ElementDofs(int e) => e < Shells.Count ? NodeDofs(Shells[e].Nodes) : BeamDofs(Beams[e - Shells.Count]);

    /// <summary>Матрица жёсткости КЭ единой нумерации (глобальные оси) — как в <see cref="AssembleK"/>.</summary>
    public double[,] ElementK(int e)
    {
        if (e < Shells.Count)
        {
            var coords = ShellCoords(e);
            return ShellElementForces.ElementKTangentGlobal(coords, Shells[e].Section, new double[6 * coords.Length]);
        }
        int i = e - Shells.Count;
        var b = Beams[i];
        return BeamElements.Beam3dKGlobal(BeamCoords(i), b.Section, b.RefVec, b.Releases);
    }

    /// <summary>
    /// Запись КЭ единой нумерации (неизменяемая): та же ссылка — те же узлы, сечение и шарниры. Вместе с
    /// <see cref="ElementHasConstantStiffness"/> — ключ кэша матрицы КЭ.
    /// </summary>
    internal object ElementRecord(int e) => e < Shells.Count ? Shells[e] : Beams[e - Shells.Count];

    /// <summary>Матрица КЭ не меняется от решения к решению (линейное сечение).</summary>
    public bool ElementHasConstantStiffness(int e)
        => e < Shells.Count ? Shells[e].Section.IsConstantStiffness : Beams[e - Shells.Count].Section.IsConstantStiffness;

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
            coo.AddBlock(BeamDofs(b), BeamElements.Beam3dKGlobal(BeamCoords(e), b.Section, b.RefVec, b.Releases));
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
        // Векторы оболочек — параллельно пачками, раскладка по порядку (сумма не зависит от числа потоков).
        var po = new ParallelOptions { MaxDegreeOfParallelism = MaxDegreeOfParallelism };
        var fes = new double[Math.Min(TangentBatch, Shells.Count)][];
        for (int e0 = 0; e0 < Shells.Count; e0 += TangentBatch)
        {
            int count = Math.Min(TangentBatch, Shells.Count - e0);
            Parallel.For(0, count, po, i =>
            {
                int e = e0 + i;
                var ue = Gather(u, NodeDofs(Shells[e].Nodes));
                fes[i] = corotational
                    ? ShellCorotational.ElementFCR(ShellCoords(e), Shells[e].Section, ue)
                    : ShellElementForces.ElementFInternalGlobal(ShellCoords(e), Shells[e].Section, ue);
            });
            for (int i = 0; i < count; i++)
            {
                var dofs = NodeDofs(Shells[e0 + i].Nodes);
                var fe = fes[i];
                for (int k = 0; k < dofs.Length; k++) f[dofs[k]] += fe[k];
            }
        }
        for (int e = 0; e < Beams.Count; e++)
        {
            var b = Beams[e];
            var dofs = BeamDofs(b);
            var fe = BeamCorotational.Beam3dInternalForce(BeamCoords(e), b.Section, Gather(u, dofs), b.RefVec, b.Releases);
            for (int i = 0; i < dofs.Length; i++) f[dofs[i]] += fe[i];
        }
        return f;
    }

    /// <summary>
    /// Касательная K_T(u) (COO); кинематика — как в <see cref="AssembleFInternal"/>. CR-оболочки — численная
    /// ∂F/∂u (<see cref="ShellCorotational.ElementNumericalTangentCR"/>), матрицы оболочек — параллельно пачками.
    /// </summary>
    public CooMatrix AssembleKTangent(double[] u, bool corotational = true)
    {
        var coo = NewCoo();
        var po = new ParallelOptions { MaxDegreeOfParallelism = MaxDegreeOfParallelism };
        var ke = new double[Math.Min(TangentBatch, Shells.Count)][,];
        for (int e0 = 0; e0 < Shells.Count; e0 += TangentBatch)
        {
            int count = Math.Min(TangentBatch, Shells.Count - e0);
            Parallel.For(0, count, po, i =>
            {
                int e = e0 + i;
                var ue = Gather(u, NodeDofs(Shells[e].Nodes));
                ke[i] = corotational
                    ? ShellCorotational.ElementNumericalTangentCR(ShellCoords(e), Shells[e].Section, ue)
                    : ShellElementForces.ElementKTangentGlobal(ShellCoords(e), Shells[e].Section, ue);
            });
            for (int i = 0; i < count; i++)
            {
                coo.AddBlock(NodeDofs(Shells[e0 + i].Nodes), ke[i]);
                ke[i] = null!;
            }
        }
        for (int e = 0; e < Beams.Count; e++)
        {
            var b = Beams[e];
            var dofs = BeamDofs(b);
            coo.AddBlock(dofs, BeamCorotational.Beam3dTangent(BeamCoords(e), b.Section, Gather(u, dofs), b.RefVec,
                releases: b.Releases));
        }
        return coo;
    }

    // Симметризованная касательная КЭ единой нумерации (для Холецкого): оболочки CR — численная ∂F/∂u, фон Карман —
    // аналитическая (симметрична), стержни CR — ½(K + Kᵀ).
    private double[,] ElementTangentSymmetric(int e, double[] u, bool corotational)
    {
        var dofs = ElementDofs(e);
        var ue = Gather(u, dofs);
        if (e < Shells.Count)
            return corotational
                ? ShellCorotational.ElementNumericalTangentCR(ShellCoords(e), Shells[e].Section, ue, symmetrize: true)
                : ShellElementForces.ElementKTangentGlobal(ShellCoords(e), Shells[e].Section, ue);
        int i = e - Shells.Count;
        var b = Beams[i];
        var k = BeamCorotational.Beam3dTangent(BeamCoords(i), b.Section, ue, b.RefVec, releases: b.Releases);
        int m = k.GetLength(0);
        for (int a = 0; a < m; a++)
            for (int c = a + 1; c < m; c++)
            {
                double avg = 0.5 * (k[a, c] + k[c, a]);
                k[a, c] = avg; k[c, a] = avg;
            }
        return k;
    }

    // Пачка матриц оболочек касательной: параллельный расчёт, раскладка в COO по порядку.
    private const int TangentBatch = 4096;

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
        var fixedSys = SysFixed(bc.FixedDofs);
        if (bc.UFixed.All(v => v == 0))
            lock (_cholGate)
            {
                // Нулевые заданные перемещения: K_ff собирается сразу по постоянному портрету.
                var clock = System.Diagnostics.Stopwatch.StartNew();
                var asm = Assembler(bc, fixedSys);
                var kff = asm.Assemble(MaxDegreeOfParallelism);
                double tAssemble = clock.Elapsed.TotalSeconds;
                var fSys = SysVector(f);
                var fmod = Array.ConvertAll(asm.Free, d => fSys[d]);
                var u = SolveSymmetric(kff, fmod, asm.Free);
                var res = Full(DirichletReducer.Expand(NSys, asm.Free, u, fixedSys, bc.UFixed));
                LastSolveTimings = new LinearSolveTimings(tAssemble, _lastFactorSeconds, clock.Elapsed.TotalSeconds);
                return res;
            }
        var k = AssembleK();
        var kSpring = bc.AssembleKSpring();
        if (kSpring.Count > 0) AppendInto(k, kSpring);
        var reduced = DirichletReducer.Reduce(SysMatrix(k), SysVector(f), fixedSys, bc.UFixed);
        var uFree = SolveSymmetric(reduced.Kff, reduced.Fmod, reduced.Free);
        return Full(DirichletReducer.Expand(NSys, reduced.Free, uFree, fixedSys, bc.UFixed));
    }

    /// <summary>Линейная статика для нескольких нагрузок: одна сборка и одна факторизация K на все правые части.</summary>
    public double[][] SolveLinear(IReadOnlyList<double[]> loads, BoundaryConditions bc)
    {
        if (bc.HasNonlinearSprings)
            throw new InvalidOperationException(
                "SolveLinear не поддерживает нелинейные пружины — используйте SolveNonlinear.");
        if (bc.UFixed.Any(v => v != 0))
            throw new NotSupportedException("SolveLinear для нескольких нагрузок — только с нулевыми заданными перемещениями.");
        if (loads.Count == 0) return [];
        var fixedSys = SysFixed(bc.FixedDofs);
        var result = new double[loads.Count][];
        lock (_cholGate)
        {
            var asm = Assembler(bc, fixedSys);
            var kff = asm.Assemble(MaxDegreeOfParallelism);
            var chol = FactorizeCached(kff);
            for (int i = 0; i < loads.Count; i++)
            {
                // Заданные перемещения нулевые — правая часть есть нагрузка на свободных DOF.
                var fSys = SysVector(loads[i]);
                var fi = Array.ConvertAll(asm.Free, d => fSys[d]);
                var uFree = chol.LastFactorizationSpd ? chol.Solve(fi) : SolveNotSpd(chol, kff, fi, asm.Free);
                result[i] = Full(DirichletReducer.Expand(NSys, asm.Free, uFree, fixedSys, bc.UFixed));
            }
        }
        return result;
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

    /// <summary>
    /// Относительная невязка ‖F − F_int − K_spring·u − F_nl‖ на свободных DOF пространства решения (силы через жёсткие
    /// связи приведены к ведущим узлам). <paramref name="fInternal"/> — внутренние силы КЭ при <paramref name="u"/>
    /// (например, по истинным законам сечений). Знаменатель — ‖F‖ или, если задан <paramref name="fInternalAbs"/>
    /// (сборка модулей вкладов КЭ, без взаимного погашения), наибольшее из ‖F‖ и ‖F_int,abs‖: у плиты внутренние
    /// мембранные усилия на порядок больше узловых нагрузок, и мерить их небаланс нагрузкой бессмысленно.
    /// <paramref name="momentArm"/> &gt; 0 — узловые моменты входят в норму делёнными на это плечо (M/ℓ — сила);
    /// 0 — все DOF без масштаба.
    /// </summary>
    public double RelativeResidual(double[] f, double[] fInternal, double[] u, BoundaryConditions bc,
                                   double momentArm = 0.0, double[]? fInternalAbs = null)
    {
        var r = Dense.SubV(f, fInternal);
        var kSpring = bc.AssembleKSpring();
        if (kSpring.Count > 0)
        {
            var ks = kSpring.ToCsc().Multiply(u);
            for (int i = 0; i < r.Length; i++) r[i] -= ks[i];
        }
        if (bc.HasNonlinearSprings)
        {
            var fnl = bc.AssembleFSpringNonlinear(u);
            for (int i = 0; i < r.Length; i++) r[i] -= fnl[i];
        }
        var free = DirichletReducer.FreeDofs(NSys, SysFixed(bc.FixedDofs));
        // Вес DOF пространства решения: поворот (по полному DOF; жёсткие связи сохраняют DOF ведущих узлов) — 1/ℓ.
        var w = Enumerable.Repeat(1.0, NSys).ToArray();
        if (momentArm > 0.0)
            for (int d = 0; d < NDof; d++)
                if (d % 6 >= 3 && (Links?.ToReducedIndex(d) ?? d) is int k and >= 0) w[k] = 1.0 / momentArm;
        double Norm(double[] v)
        {
            var sys = SysVector(v);
            double sum = 0.0;
            foreach (int i in free) sum += sys[i] * w[i] * sys[i] * w[i];
            return Math.Sqrt(sum);
        }
        double scale = Norm(f);
        if (fInternalAbs != null) scale = Math.Max(scale, Norm(fInternalAbs));
        return Norm(r) / Math.Max(scale, 1e-300);
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
    /// (сводка — <see cref="NonlinearConvergence"/>); несошедшийся шаг не прерывает расчёт. Система шага — симметризованная
    /// K_T, многопоточный суперузловой Холецкий; с нелинейными пружинами — точная K_T и LU.
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
        var clock = System.Diagnostics.Stopwatch.StartNew();
        int nSolves = 0, nFInt = 0;
        double tTangent = 0.0, tFactor = 0.0, tFInt = 0.0;

        double[] FIntTotal(double[] uu)
        {
            double t0 = clock.Elapsed.TotalSeconds;
            nFInt++;
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
            tFInt += clock.Elapsed.TotalSeconds - t0;
            return fi;
        }

        double[] Residual(double[] fStep) => SysVector(Dense.SubV(fStep, FIntTotal(u)));

        void SetFree(double[] uBak, double[] du, double alpha)
        {
            for (int i = 0; i < free.Length; i++) uSys[free[i]] = uBak[i] + alpha * du[i];
            u = Full(uSys);
        }

        // Шаг Ньютона: симметризованная K_T по постоянному портрету и многопоточный Холецкий (не SPD — LU для небольших
        // систем, см. SolveNotSpd). Кососимметричная часть точной ∂F/∂u CR отбрасывается — сходимость линейная вместо
        // квадратичной, зато решение многопоточное и без LU.
        double[] SolveTangentCholesky(double[] uu, double[] r)
        {
            lock (_cholGate)
            {
                double t0 = clock.Elapsed.TotalSeconds;
                var asm = Assembler(bc, fixedDofs);
                var kff = asm.AssembleWith(e => ElementTangentSymmetric(e, uu, corotational), MaxDegreeOfParallelism);
                tTangent += clock.Elapsed.TotalSeconds - t0;
                var rf = Array.ConvertAll(asm.Free, d => r[d]);
                var chol = FactorizeCached(kff);
                tFactor += _lastFactorSeconds;
                return chol.LastFactorizationSpd ? chol.Solve(rf) : SolveNotSpd(chol, kff, rf, asm.Free);
            }
        }

        // Нелинейные пружины (их касательная вне портрета сборщика): точная K_T и LU.
        double[] SolveTangentLu(double[] uu, double[] r)
        {
            var kt = AssembleKTangent(uu, corotational);
            if (kSpringLinCoo.Count > 0) AppendInto(kt, kSpringLinCoo);
            AppendInto(kt, bc.AssembleKSpringTangent(uu));
            var reduced = DirichletReducer.Reduce(SysMatrix(kt), r, fixedDofs, null);
            return SparseLuSolver.SolveOnce(reduced.Kff, reduced.Fmod);
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

                double[] duFree;
                nSolves++;
                try { duFree = bc.HasNonlinearSprings ? SolveTangentLu(u, r) : SolveTangentCholesky(u, r); }
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
        LastNewtonTimings = new NewtonSolveTimings(nSolves, nFInt, tTangent, tFactor, tFInt, clock.Elapsed.TotalSeconds);
        return (u, history);
    }

    // ---------------- утилиты ----------------

    /// <summary>
    /// Симметричная система: многопоточный суперузловой Холецкий (AMD); если матрица не положительно определена — LU
    /// (только для небольших систем, см. <see cref="SolveNotSpd"/>). <paramref name="free"/> — DOF пространства решения
    /// для неизвестных системы (для сообщения о вырожденности).
    /// </summary>
    private double[] SolveSymmetric(CscMatrix a, double[] b, int[] free)
    {
        lock (_cholGate)
        {
            var chol = FactorizeCached(a);
            return chol.LastFactorizationSpd ? chol.Solve(b) : SolveNotSpd(chol, a, b, free);
        }
    }

    /// <summary>Наибольший порядок системы, для которой при не-SPD матрице пробуется LU (без упорядочивания он медленный).</summary>
    public const int LuFallbackMaxSize = 50_000;

    // Не положительно определённая матрица: небольшая система — LU; большая (или вырожденная для LU) — исключение с
    // узлом и DOF первого неположительного ведущего элемента.
    private double[] SolveNotSpd(SupernodalCholeskySolver chol, CscMatrix a, double[] b, int[] free)
    {
        string where = chol.FirstNonPositivePivot is int i and >= 0 && i < free.Length ? " — " + DescribeDof(free[i]) : "";
        if (a.Cols <= LuFallbackMaxSize)
        {
            try { return SparseLuSolver.SolveOnce(a, b); }
            catch (InvalidOperationException ex)
            {
                throw new InvalidOperationException($"{ex.Message} Матрица жёсткости не положительно определена{where}.", ex);
            }
        }
        throw new InvalidOperationException(
            $"Матрица жёсткости не положительно определена (механизм или потеря устойчивости){where}.");
    }

    /// <summary>Описание DOF пространства решения: узел сетки (индекс и координаты) и направление.</summary>
    public string DescribeDof(int sysDof)
    {
        int full = Links?.ToFullIndex(sysDof) ?? sysDof;
        int node = full / 6;
        string[] names = ["ux", "uy", "uz", "rx", "ry", "rz"];
        var x = Nodes[node];
        return string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"узел сетки №{node} ({x[0]:0.###}; {x[1]:0.###}; {x[2]:0.###}), {names[full % 6]}");
    }

    /// <summary>Число потоков сборки K_ff и факторизации (−1 — все логические процессоры).</summary>
    public int MaxDegreeOfParallelism { get; set; } = -1;

    // Кэш сборки K_ff по постоянному портрету (на ГУ и набор закреплений).
    private KffAssembler? _assembler;

    private KffAssembler Assembler(BoundaryConditions bc, int[] fixedSys)
    {
        if (_assembler == null || !_assembler.Matches(bc, fixedSys))
        {
            _assembler = null;
            _assembler = KffAssembler.Build(this, bc, fixedSys);
        }
        return _assembler;
    }

    // Кэш символики Холецкого: портрет Kff от решения к решению (итерации Пикара) обычно один и тот же — упорядочивание
    // и суперузлы считаются заново, только если портрет изменился (сборка пропускает точные нули, так что он может
    // зависеть от значений). Множитель L живёт вместе с сеткой.
    private readonly object _cholGate = new();
    private SupernodalCholeskySolver? _chol;
    private int[]? _cholColPtr, _cholRowIdx;

    /// <summary>Сколько раз выполнялся символический анализ Холецкого (для диагностики кэша).</summary>
    public int CholeskyAnalyses { get; private set; }

    /// <summary>Фазы последнего <see cref="SolveLinear(double[], BoundaryConditions)"/> по постоянному портрету (null — не было).</summary>
    public LinearSolveTimings? LastSolveTimings { get; private set; }

    /// <summary>Раскладка времени последнего <see cref="SolveNonlinear"/>.</summary>
    public NewtonSolveTimings? LastNewtonTimings { get; private set; }

    private double _lastFactorSeconds;

    private SupernodalCholeskySolver FactorizeCached(CscMatrix a)
    {
        if (_chol == null || !a.ColPtr.AsSpan().SequenceEqual(_cholColPtr) || !a.RowIdx.AsSpan().SequenceEqual(_cholRowIdx))
        {
            _chol = null;   // старый множитель отпускаем до выделения нового
            var chol = new SupernodalCholeskySolver
            {
                MaxDegreeOfParallelism = MaxDegreeOfParallelism > 0 ? MaxDegreeOfParallelism : Environment.ProcessorCount,
            };
            chol.AnalyzePattern(a);
            (_chol, _cholColPtr, _cholRowIdx) = (chol, a.ColPtr, a.RowIdx);
            CholeskyAnalyses++;
        }
        var clock = System.Diagnostics.Stopwatch.StartNew();
        _chol.Factorize(a);
        _lastFactorSeconds = clock.Elapsed.TotalSeconds;
        return _chol;
    }

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
