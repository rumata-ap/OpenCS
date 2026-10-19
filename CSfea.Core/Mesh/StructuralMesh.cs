using CSfea.Sparse;

namespace CSfea.Core;

/// <summary>Фазы линейного решения, с: сборка K_ff, численная факторизация, всё решение (с подстановками).</summary>
public sealed record LinearSolveTimings(double Assemble, double Factorize, double Total);

/// <summary>
/// Раскладка последнего <see cref="StructuralMesh.SolveNonlinear"/>, с: решений системы (шагов Ньютона), сборок F_int
/// (невязки, включая пробы line search), сборка касательной, факторизация, F_int, всего.
/// </summary>
public sealed record NewtonSolveTimings(int Solves, int FIntCalls, double Tangent, double Factorize, double FInt, double Total);

/// <summary>
/// Шаг Ньютона последнего <see cref="StructuralMesh.SolveNonlinear"/> (диагностика): невязка до шага, принятый α line
/// search и признак принятия (false — убывания не нашлось, шаг сделан с наименьшим α), невязка первой (α = 1) и последней
/// пробы, ‖Δu‖/‖u‖ полного шага по свободным DOF; <paramref name="Stop"/> — причина выхода из итераций без шага
/// (система не решена, приращение не конечно), иначе null.
/// </summary>
public sealed record NewtonStepTrace(int Step, int Iteration, double Residual, double Alpha, bool Accepted,
    double FirstTrial, double LastTrial, double StepNorm, string? Stop);

/// <summary>
/// Результат линейной задачи устойчивости <see cref="StructuralMesh.SolveBuckling"/>: наименьшие положительные
/// коэффициенты нагрузки λ (по возрастанию) и формы (полные DOF, наибольшая по модулю компонента — 1). Массивы короче
/// запрошенного числа форм, если положительных λ меньше (нет сжатия — пустые).
/// </summary>
public sealed record StructuralBucklingResult(double[] Lambda, double[][] Modes, int Iterations, bool Converged);

/// <summary>
/// Энергии стержня в форме потери устойчивости (<see cref="StructuralMesh.BeamEnergy"/>): U — изгиба, W — работа
/// продольной силы; Xy — плоскость xy местных осей (изгиб вокруг z, EIz), Xz — плоскость xz (вокруг y, EIy).
/// </summary>
public sealed record BeamModeEnergy(double Uxy, double Wxy, double Uxz, double Wxz);

/// <summary>
/// Упругие закрепления концов группы стержней в плоскости изгиба (эквивалентная стойка Масленникова: низ — конец I —
/// несмещаем): <paramref name="R1"/>, <paramref name="R2"/> — повороты концов I и J, Н·м/рад; <paramref name="R3"/> —
/// смещение конца J относительно I, Н/м; +∞ — закреплено, отрицательное — окружение не держит, а нагружает.
/// <paramref name="Interior"/> — доля усилий в промежуточных узлах группы от наибольшего концевого (0 — примыканий
/// нет, схема стойки точная).
/// </summary>
public sealed record PlaneRestraint(double R1, double R2, double R3, double Interior = 0);

/// <summary>Закрепления концов группы по плоскостям местных осей первого стержня (null — в плоскости формы нет).</summary>
public sealed record BeamGroupRestraints(PlaneRestraint? Xy, PlaneRestraint? Xz);

/// <summary>
/// Формы локальной задачи устойчивости группы стержней: коэффициенты α к продольным силам группы по возрастанию,
/// энергии группы в каждой форме (сумма <see cref="BeamModeEnergy"/> её стержней) и закрепления концов из формы
/// (<see cref="StructuralMesh.GroupRestraints"/>).
/// </summary>
public sealed record StructuralLocalModes(double[] Alpha, BeamModeEnergy[] Energy, BeamGroupRestraints[] Restraints);

/// <summary>
/// Локальная задача устойчивости группы стержней (<see cref="StructuralMesh.SolveLocalBuckling"/>): <paramref name="Sway"/>
/// — концы группы свободны, их держит остальная схема; <paramref name="Braced"/> — без смещения: поперечные перемещения
/// концов группы закреплены. <paramref name="Surround"/> — жёсткости остальной схемы при фактическом сочетании
/// (K + K_σ(N)), сведённой к концам группы: диагональ по поворотам, по смещению — относительное смещение концов под
/// парой равных и противоположных сил; связи между DOF отброшены.
/// </summary>
public sealed record StructuralLocalBuckling(StructuralLocalModes Sway, StructuralLocalModes Braced,
    BeamGroupRestraints? Surround = null);

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

    // ---------------- теория второго порядка (расчёт по деформированной схеме) ----------------

    /// <summary>
    /// Матрица начальных напряжений K_σ оболочки (глобальные оси, 6n × 6n) по мембранным усилиям сечения при
    /// перемещениях <paramref name="u"/> (линейная кинематика): Σ по точкам Гаусса Gᵀ·[Nx Nxy; Nxy Ny]·G для наклонов
    /// прогиба w и, если <paramref name="inPlane"/>, перемещений в плоскости u, v (продольный изгиб стены или пилона в
    /// своей плоскости).
    /// </summary>
    public double[,] ShellKGeometric(int e, double[] u, bool inPlane = true)
    {
        var coords = ShellCoords(e);
        var (xy, _, _) = ShellGeometry.ProjectToLocal(coords);
        int n = coords.Length;
        var t = ShellGeometry.BuildTMatrix(ShellGeometry.LocalFrame(coords), n);
        var uLoc = Dense.MatTVec(t, Gather(u, NodeDofs(Shells[e].Nodes)));
        var section = Shells[e].Section;
        var k = new double[5 * n, 5 * n];
        foreach (var g in ShellElementForces.GaussData(xy))
        {
            var (nf, _, _) = section.Forces(Dense.MatVec(g.Bm, uLoc), Dense.MatVec(g.Bb, uLoc), Dense.MatVec(g.Bs, uLoc));
            for (int i = 0; i < n; i++)
            {
                double gi0 = g.G[0, 5 * i + 2], gi1 = g.G[1, 5 * i + 2];
                for (int j = 0; j < n; j++)
                {
                    double gj0 = g.G[0, 5 * j + 2], gj1 = g.G[1, 5 * j + 2];
                    double h = g.Scale * (gi0 * (nf[0] * gj0 + nf[2] * gj1) + gi1 * (nf[2] * gj0 + nf[1] * gj1));
                    k[5 * i + 2, 5 * j + 2] += h;
                    if (!inPlane) continue;
                    k[5 * i, 5 * j] += h;
                    k[5 * i + 1, 5 * j + 1] += h;
                }
            }
        }
        return Dense.MatMul(Dense.MatMul(t, k), Dense.Transpose(t));
    }

    /// <summary>
    /// Матрица КЭ единой нумерации в теории второго порядка: K + K_σ(N), N — усилия КЭ при перемещениях
    /// <paramref name="u"/> и текущих жёсткостях сечений. Оболочки — <see cref="ShellKGeometric"/>; стержни — K_σ балки
    /// с функциями Эрмита по продольной силе КЭ (растяжение — плюс), шарниры конденсируются из суммы.
    /// </summary>
    public double[,] ElementKSecondOrder(int e, double[] u, bool shellInPlane = true)
    {
        if (e < Shells.Count)
        {
            var k = ElementK(e);
            Dense.AddScaledInPlace(k, ShellKGeometric(e, u, shellInPlane), 1.0);
            return k;
        }
        int i = e - Shells.Count;
        var b = Beams[i];
        var (t, l) = BeamElements.Beam3dT(BeamCoords(i), b.RefVec);
        var kl = BeamElements.Beam3dKLocal(b.Section, l);
        var d = Dense.MatVec(t, Gather(u, BeamDofs(b)));
        double axial = Dense.MatVec(BeamReleases.Condense(kl, b.Releases), d)[6];
        if (axial != 0.0)
        {
            kl = (double[,])kl.Clone();
            AddBeamKGeometric(kl, axial, l);
        }
        return Dense.MatMul(Dense.MatTMul(t, BeamReleases.Condense(kl, b.Releases)), t);
    }

    // K_σ стержня (местные оси, функции Эрмита): N/(30·L)·[36, 3L, −36, 3L; …] в обеих плоскостях изгиба; у плоскости
    // xz знаки связей прогиб — поворот обратные (w′ = −θy), как в Beam3dKLocal.
    private static void AddBeamKGeometric(double[,] k, double axial, double l)
    {
        double c = axial / (30.0 * l), a = 36.0 * c, b = 3.0 * l * c, d4 = 4.0 * l * l * c, d1 = -l * l * c;
        var xy = new[,] { { a, b, -a, b }, { b, d4, -b, d1 }, { -a, -b, a, -b }, { b, d1, -b, d4 } };
        var xz = new[,] { { a, -b, -a, -b }, { -b, d4, b, d1 }, { -a, b, a, b }, { -b, d1, b, d4 } };
        int[] ixy = [1, 5, 7, 11], ixz = [2, 4, 8, 10];
        for (int p = 0; p < 4; p++)
            for (int q = 0; q < 4; q++)
            {
                k[ixy[p], ixy[q]] += xy[p, q];
                k[ixz[p], ixz[q]] += xz[p, q];
            }
    }

    /// <summary>
    /// Решение по теории второго порядка: [K + K_σ(N)]·u = F, N — усилия КЭ при перемещениях
    /// <paramref name="uState"/> (обычно предыдущая итерация; N уточняется повторным вызовом). Матрица симметрична,
    /// решение — тем же Холецким, что и линейное; не положительно определённая K + K_σ — потеря устойчивости
    /// (<see cref="InvalidOperationException"/>, запасного LU нет: выше критической нагрузки решение не имеет смысла).
    /// </summary>
    public double[] SolveSecondOrder(double[] f, BoundaryConditions bc, double[] uState, bool shellInPlane = true)
    {
        if (bc.HasNonlinearSprings)
            throw new InvalidOperationException("SolveSecondOrder не поддерживает нелинейные пружины — используйте SolveNonlinear.");
        if (bc.UFixed.Any(v => v != 0))
            throw new NotSupportedException("SolveSecondOrder — только с нулевыми заданными перемещениями.");
        var fixedSys = SysFixed(bc.FixedDofs);
        lock (_cholGate)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var asm = Assembler(bc, fixedSys);
            var kff = asm.AssembleWith(e => ElementKSecondOrder(e, uState, shellInPlane), MaxDegreeOfParallelism);
            double tAssemble = clock.Elapsed.TotalSeconds;
            var fSys = SysVector(f);
            var fmod = Array.ConvertAll(asm.Free, d => fSys[d]);
            var chol = FactorizeCached(kff);
            if (!chol.LastFactorizationSpd)
            {
                string where = chol.FirstNonPositivePivot is int i and >= 0 && i < asm.Free.Length ? " — " + DescribeDof(asm.Free[i]) : "";
                throw new InvalidOperationException($"Матрица K + K_σ не положительно определена — потеря устойчивости{where}.");
            }
            var res = Full(DirichletReducer.Expand(NSys, asm.Free, chol.Solve(fmod), fixedSys, bc.UFixed));
            LastSolveTimings = new LinearSolveTimings(tAssemble, _lastFactorSeconds, clock.Elapsed.TotalSeconds);
            return res;
        }
    }

    /// <summary>
    /// Геометрическая матрица КЭ единой нумерации K_σ(N) (глобальные оси), линейная по усилиям КЭ при перемещениях
    /// <paramref name="u"/>: оболочки — <see cref="ShellKGeometric"/>; стержни — K_σ с функциями Эрмита по продольной
    /// силе КЭ (растяжение — плюс). У стержня с шарнирами — согласованная с конденсацией матрица Pᵀ·K_σ·P, где P —
    /// восстановление освобождённых DOF по сохранённым (<see cref="BeamReleases.Recover"/>); в отличие от
    /// <see cref="ElementKSecondOrder"/> она не зависит от N нелинейно — для линейной задачи устойчивости.
    /// Освобождённый поворот исключается по K без K_σ, поэтому шарнир концевого КЭ даёт P_cr немного выше, чем
    /// свободный поворот узла (2 КЭ на стержень — +0,6 %, 6 КЭ — 2·10⁻⁶); стержни для устойчивости дробятся.
    /// <paramref name="beamAxial"/> — заданные продольные силы стержней (индекс в <see cref="Beams"/>) вместо сил по
    /// <paramref name="u"/> (огибающая сжатия по сочетаниям); оболочки — всегда по <paramref name="u"/>.
    /// </summary>
    public double[,] ElementKGeometric(int e, double[] u, bool shellInPlane = true, double[]? beamAxial = null)
    {
        if (e < Shells.Count) return ShellKGeometric(e, u, shellInPlane);
        int i = e - Shells.Count;
        var b = Beams[i];
        var (t, l) = BeamElements.Beam3dT(BeamCoords(i), b.RefVec);
        var kl = BeamElements.Beam3dKLocal(b.Section, l);
        double axial = beamAxial?[i] ?? BeamAxialForce(i, u);
        var kg = new double[12, 12];
        if (axial == 0.0) return kg;
        AddBeamKGeometric(kg, axial, l);
        int mask = b.Releases;
        if (mask != 0)
        {
            var p = new double[12, 12];
            for (int c = 0; c < 12; c++)
            {
                if ((mask & (1 << c)) != 0) continue;
                var unit = new double[12];
                unit[c] = 1.0;
                var col = BeamReleases.Recover(kl, mask, unit);
                for (int r = 0; r < 12; r++) p[r, c] = col[r];
            }
            kg = Dense.MatMul(Dense.MatTMul(p, kg), p);
        }
        return Dense.MatMul(Dense.MatTMul(t, kg), t);
    }

    /// <summary>
    /// Продольная сила стержня <paramref name="beam"/> (индекс в <see cref="Beams"/>) по линейной жёсткости при
    /// перемещениях <paramref name="u"/>, растяжение — плюс: сила конца J по местной x без пролётных нагрузок, то есть
    /// средняя по длине. По ней собирается K_σ (<see cref="ElementKGeometric"/>).
    /// </summary>
    public double BeamAxialForce(int beam, double[] u)
    {
        var b = Beams[beam];
        var (t, l) = BeamElements.Beam3dT(BeamCoords(beam), b.RefVec);
        var kl = BeamElements.Beam3dKLocal(b.Section, l);
        var d = Dense.MatVec(t, Gather(u, BeamDofs(b)));
        return Dense.MatVec(BeamReleases.Condense(kl, b.Releases), d)[6];
    }

    /// <summary>
    /// Энергии стержня <paramref name="beam"/> в форме <paramref name="mode"/> (полные DOF) по плоскостям изгиба местных
    /// осей: U = ½·dᵀ·K·d — изгиба, W = ½·dᵀ·(−K_σ)·d — работа продольной силы <paramref name="axial"/> (растяжение —
    /// плюс, у сжатого W ≥ 0). d — местные перемещения концов, у шарниров освобождённые DOF восстановлены по сохранённым
    /// (та же конденсация, что в <see cref="ElementKGeometric"/>). По всем КЭ формы Σ(U + энергии N и кручения) = λ·ΣW.
    /// </summary>
    public BeamModeEnergy BeamEnergy(int beam, double[] mode, double axial)
        => BeamEnergyAt(beam, Gather(mode, BeamDofs(Beams[beam])), axial);

    // Энергии стержня по перемещениям его концов в глобальных осях (12 DOF).
    private BeamModeEnergy BeamEnergyAt(int beam, double[] dGlobal, double axial)
    {
        var b = Beams[beam];
        var (t, l) = BeamElements.Beam3dT(BeamCoords(beam), b.RefVec);
        var kl = BeamElements.Beam3dKLocal(b.Section, l);
        var d = BeamReleases.Recover(kl, b.Releases, Dense.MatVec(t, dGlobal));
        var kg = new double[12, 12];
        AddBeamKGeometric(kg, -axial, l);
        double Quad(double[,] k, int[] idx)
        {
            double s = 0;
            foreach (int p in idx)
                foreach (int q in idx) s += d[p] * k[p, q] * d[q];
            return 0.5 * s;
        }
        int[] xy = [1, 5, 7, 11], xz = [2, 4, 8, 10];
        return new BeamModeEnergy(Quad(kl, xy), Quad(kg, xy), Quad(kl, xz), Quad(kg, xz));
    }

    /// <summary>
    /// Закрепления концов соосной группы стержней <paramref name="group"/> (индексы в <see cref="Beams"/>) в форме
    /// <paramref name="mode"/> (полные DOF) при продольных силах стержней (по <paramref name="uRef"/>), умноженных на
    /// <paramref name="factor"/> (λ формы или α локальной задачи): из равновесия узла конца f + r·δ = 0, r = −f/δ, где
    /// f = (K + factor·K_σ)·d — усилие конца группы, δ — перемещение того же DOF; r₃ — по поперечной силе конца J и
    /// смещению J относительно I. С такими пружинами группа в форме в равновесии — стойка Масленникова даёт ту же критическую силу
    /// (с точностью дробления). Конец I — с меньшей координатой по местной x первого стержня. <paramref name="beamAxial"/> —
    /// заданные продольные силы стержней вместо сил по <paramref name="uRef"/> (см. <see cref="ElementKGeometric"/>).
    /// </summary>
    public BeamGroupRestraints GroupRestraints(IReadOnlyList<int> group, double[] uRef, double[] mode, double factor,
        double[]? beamAxial = null)
        => GroupRestraintsAt(group, group.Select(b => Gather(mode, BeamDofs(Beams[b]))).ToArray(),
            group.Select(b => beamAxial?[b] ?? BeamAxialForce(b, uRef)).ToArray(), factor);

    // Концы группы — узлы, к которым примыкает один её стержень: (позиция стержня в группе, конец 0/1, узел) для I и J;
    // null — концов не два.
    private ((int S, int End, int Node) I, (int S, int End, int Node) J)? GroupEnds(IReadOnlyList<int> group, double[,] axes0)
    {
        var degree = new Dictionary<int, int>();
        foreach (int b in group)
            foreach (int node in new[] { Beams[b].I, Beams[b].J }) degree[node] = degree.GetValueOrDefault(node) + 1;
        var ends = new List<(int S, int End, int Node)>();
        for (int s = 0; s < group.Count; s++)
            for (int end = 0; end < 2; end++)
            {
                int node = end == 0 ? Beams[group[s]].I : Beams[group[s]].J;
                if (degree[node] == 1) ends.Add((s, end, node));
            }
        if (ends.Count != 2) return null;
        double X(int node) => axes0[0, 0] * Nodes[node][0] + axes0[0, 1] * Nodes[node][1] + axes0[0, 2] * Nodes[node][2];
        return X(ends[0].Node) <= X(ends[1].Node) ? (ends[0], ends[1]) : (ends[1], ends[0]);
    }

    // Закрепления группы по перемещениям концов её стержней в глобальных осях (см. GroupRestraints).
    private BeamGroupRestraints GroupRestraintsAt(IReadOnlyList<int> group, double[][] dGlobal, double[] axial, double factor)
    {
        var (axes0, _) = BeamElements.Beam3dFrame(BeamCoords(group[0]), Beams[group[0]].RefVec);
        var ends = GroupEnds(group, axes0);
        if (ends == null) return new BeamGroupRestraints(null, null);
        var (ei, ej) = ends.Value;
        // Перемещения и усилия концов стержней в осях первого стержня: [u(3), θ(3)] и [F(3), M(3)]; усилия узла — сумма.
        var disp = new Dictionary<(int, int), double[]>();
        var force = new Dictionary<int, double[]>();
        var parts = new List<double[]>();   // усилия концов отдельных стержней — масштаб для доли промежуточных
        for (int s = 0; s < group.Count; s++)
        {
            var b = Beams[group[s]];
            var coords = BeamCoords(group[s]);
            var (t, l) = BeamElements.Beam3dT(coords, b.RefVec);
            var (axes, _) = BeamElements.Beam3dFrame(coords, b.RefVec);
            var kl = BeamElements.Beam3dKLocal(b.Section, l);
            var d = BeamReleases.Recover(kl, b.Releases, Dense.MatVec(t, dGlobal[s]));
            var kt = (double[,])kl.Clone();
            AddBeamKGeometric(kt, factor * axial[s], l);
            var f = Dense.MatVec(kt, d);
            if (b.Releases != 0)
            {
                // Как в ElementKGeometric: f = Pᵀ·(K + K_σ)·P·d — усилия освобождённых DOF переносятся на сохранённые.
                var g = f;
                f = new double[12];
                for (int c = 0; c < 12; c++)
                {
                    if ((b.Releases & (1 << c)) != 0) continue;
                    var unit = new double[12];
                    unit[c] = 1.0;
                    var col = BeamReleases.Recover(kl, b.Releases, unit);
                    for (int r = 0; r < 12; r++) f[c] += col[r] * g[r];
                }
            }
            for (int end = 0; end < 2; end++)
            {
                int node = end == 0 ? b.I : b.J;
                var dn = new double[6];
                var fp = new double[6];
                var fn = force.TryGetValue(node, out var acc) ? acc : force[node] = new double[6];
                for (int part = 0; part < 2; part++)
                    for (int a = 0; a < 3; a++)
                        for (int c = 0; c < 3; c++)
                        {
                            // местные оси стержня → глобальные → местные оси первого стержня
                            double m = 0;
                            for (int g = 0; g < 3; g++) m += axes0[a, g] * axes[c, g];
                            dn[3 * part + a] += m * d[6 * end + 3 * part + c];
                            fp[3 * part + a] += m * f[6 * end + 3 * part + c];
                        }
                for (int a = 0; a < 6; a++) fn[a] += fp[a];
                parts.Add(fp);
                disp[(s, end)] = dn;
            }
        }
        var dI = disp[(ei.S, ei.End)];
        var dJ = disp[(ej.S, ej.End)];
        var fI = force[ei.Node];
        var fJ = force[ej.Node];
        double len = Math.Sqrt(Enumerable.Range(0, 3).Sum(c => Math.Pow(Nodes[ej.Node][c] - Nodes[ei.Node][c], 2)));
        double Scale(int lat, int rot) => Math.Max(Math.Abs(dJ[lat] - dI[lat]), len * Math.Max(Math.Abs(dI[3 + rot]), Math.Abs(dJ[3 + rot])));
        double scaleAll = Math.Max(Scale(1, 2), Scale(2, 1));

        // Плоскость xy: прогиб по y, поворот вокруг z; xz: прогиб по z, поворот вокруг y.
        PlaneRestraint? Plane(int lat, int rot)
        {
            double scale = Scale(lat, rot);
            if (!(scale > 1e-6 * scaleAll)) return null;
            double tol = 1e-7 * scale;
            double Rot(double m, double theta) => Math.Abs(theta) * len > tol ? -m / theta : double.PositiveInfinity;
            double delta = dJ[lat] - dI[lat];
            double r3 = Math.Abs(delta) > tol ? -fJ[lat] / delta : double.PositiveInfinity;
            // Масштаб — наибольшее усилие конца стержня: концевые M, Q бывают нулевыми (шарниры без смещения).
            double endForce = parts.Max(p => Math.Max(Math.Abs(p[lat]) * len, Math.Abs(p[3 + rot])));
            double interior = 0;
            foreach (var (node, fn) in force)
                if (node != ei.Node && node != ej.Node)
                    interior = Math.Max(interior, Math.Max(Math.Abs(fn[lat]) * len, Math.Abs(fn[3 + rot])));
            return new PlaneRestraint(Rot(fI[3 + rot], dI[3 + rot]), Rot(fJ[3 + rot], dJ[3 + rot]), r3,
                endForce > 0 ? interior / endForce : 0);
        }
        return new BeamGroupRestraints(Plane(1, 2), Plane(2, 1));
    }

    /// <summary>
    /// Линейная задача устойчивости по недеформированной схеме: (K + λ·K_σ(N))·φ = 0, N — усилия КЭ при
    /// перемещениях <paramref name="uRef"/> (обычно линейное решение под расчётным сочетанием). K — линейная жёсткость
    /// с пружинами ГУ, положительно определённая; −K_σ знаконеопределена (растянутые КЭ). Ищутся
    /// <paramref name="nModes"/> наименьших положительных λ — итерации подпространства с проекцией Рэлея–Ритца
    /// (ν = 1/λ, наибольшие положительные ν задачи −K_σ·φ = ν·K·φ). Формы — полные (с ведомыми узлами), нормированы
    /// на наибольшую по модулю компоненту перемещений. <paramref name="beamAxial"/> — заданные продольные силы стержней
    /// вместо сил по <paramref name="uRef"/> (огибающая сжатия, см. <see cref="ElementKGeometric"/>).
    /// </summary>
    public StructuralBucklingResult SolveBuckling(double[] uRef, BoundaryConditions bc, int nModes = 1,
        double tol = 1e-8, int maxIter = 300, bool shellInPlane = true, double[]? beamAxial = null)
    {
        if (nModes < 1) throw new ArgumentOutOfRangeException(nameof(nModes));
        if (bc.HasNonlinearSprings)
            throw new InvalidOperationException("SolveBuckling не поддерживает нелинейные пружины.");
        if (bc.UFixed.Any(v => v != 0))
            throw new NotSupportedException("SolveBuckling — только с нулевыми заданными перемещениями.");
        var fixedSys = SysFixed(bc.FixedDofs);
        lock (_cholGate)
        {
            var asm = Assembler(bc, fixedSys);
            int nf = asm.Free.Length;
            // K и −K_σ: сборщик делит массив значений со следующей сборкой — копии.
            var kMat = asm.Assemble(MaxDegreeOfParallelism);
            var kValues = (double[])kMat.Values.Clone();
            var chol = FactorizeCached(kMat);
            if (!chol.LastFactorizationSpd)
            {
                string where = chol.FirstNonPositivePivot is int i and >= 0 && i < nf ? " — " + DescribeDof(asm.Free[i]) : "";
                throw new InvalidOperationException($"Матрица жёсткости не положительно определена (механизм){where}.");
            }
            var kg = asm.AssembleWith(e => ElementKGeometric(e, uRef, shellInPlane, beamAxial), MaxDegreeOfParallelism, springs: false);
            var colPtr = (int[])kg.ColPtr.Clone();
            var rowIdx = (int[])kg.RowIdx.Clone();
            var bValues = Array.ConvertAll(kg.Values, v => -v);
            var bMat = new CscMatrix(nf, nf, colPtr, rowIdx, bValues);

            // Сдвиг σ < λ₁: растянутые КЭ дают отрицательные ν = 1/λ, по модулю много больше положительных (короткие
            // растянутые стержни), и итерации без сдвига сходятся к ним. С (K − σ·B)⁻¹ ν′ = 1/(λ − σ): у форм около λ₁
            // ν′ велики, у отрицательных λ |ν′| < 1/σ. σ — бисекцией по положительной определённости K − σ·B (она
            // равносильна σ < λ₁), до λ₁/σ < 1,25.
            double Shifted(double s)
            {
                var v = new double[kValues.Length];
                for (int p = 0; p < v.Length; p++) v[p] = kValues[p] - s * bValues[p];
                chol = FactorizeCached(new CscMatrix(nf, nf, colPtr, rowIdx, v));
                return chol.LastFactorizationSpd ? 1 : 0;
            }
            double lo = 0, hi = double.NaN;
            for (double s = 1; s <= 1e12 && double.IsNaN(hi); s *= 4)
                if (Shifted(s) > 0) lo = s; else hi = s;
            if (double.IsNaN(hi) || lo == 0)
                for (double s = 0.25; s >= 1e-8 && lo == 0 && !double.IsNaN(hi); s *= 0.25)
                    if (Shifted(s) > 0) lo = s; else hi = s;
            while (!double.IsNaN(hi) && lo > 0 && hi > 1.25 * lo)
            {
                double s = Math.Sqrt(lo * hi);
                if (Shifted(s) > 0) lo = s; else hi = s;
            }
            double shift = double.IsNaN(hi) ? 0 : lo;
            Shifted(shift);

            var (nu, x, iterations, converged) = SubspaceIteration(chol, bMat, nf, nModes, tol, maxIter, MaxDegreeOfParallelism);

            var lambdas = new List<double>();
            var modes = new List<double[]>();
            for (int k = 0; k < nu.Length && lambdas.Count < nModes; k++)
            {
                if (!(nu[k] > 0)) break;
                var mode = Full(DirichletReducer.Expand(NSys, asm.Free, x[k], fixedSys, bc.UFixed));
                double scale = Dense.MaxAbs(mode);
                if (scale > 0) for (int j = 0; j < mode.Length; j++) mode[j] /= scale;
                lambdas.Add(shift + 1.0 / nu[k]);
                modes.Add(mode);
            }
            return new StructuralBucklingResult(lambdas.ToArray(), modes.ToArray(), iterations, converged);
        }
    }

    /// <summary>
    /// Локальные задачи устойчивости групп стержней (метод Бритвина–Айзенбергера, 2025): остальная схема — с K + K_σ(N)
    /// при перемещениях <paramref name="uRef"/> (фактическая нагрузка, λ = 1), продольные силы стержней группы
    /// умножаются на α. Матрица A = K + K_σ(N) всей схемы (положительно определена — схема при λ = 1 устойчива)
    /// раскладывается один раз; податливость F = (A⁻¹)_EE на DOF группы E — решениями с готовым множителем. Матрица
    /// группы — Шуров дополнитель A на E: C = F⁻¹ = S_rest + K_гр + K_σ,гр(N), поэтому
    /// K_гр + S_rest + α·K_σ,гр = C + (α − 1)·K_σ,гр и α = 1 + 1/ν, ν — положительные собственные значения
    /// G·φ = ν·C·φ, G = −K_σ,гр (симметрично: F^½·G·F^½·y = ν·y, φ = F^½·y). Без смещения поперечные перемещения
    /// концов группы (узлов, к которым примыкает один её стержень; оси — местные y, z первого стержня) закрепляются:
    /// F′ = F − F·B·(Bᵀ·F·B)⁻¹·Bᵀ·F. Группа — индексы <see cref="Beams"/>. <paramref name="beamAxial"/> — заданные
    /// продольные силы стержней вместо сил по <paramref name="uRef"/> (см. <see cref="ElementKGeometric"/>).
    /// </summary>
    public StructuralLocalBuckling[] SolveLocalBuckling(double[] uRef, BoundaryConditions bc, IReadOnlyList<int[]> groups,
        bool shellInPlane = true, double[]? beamAxial = null)
    {
        if (bc.HasNonlinearSprings)
            throw new InvalidOperationException("SolveLocalBuckling не поддерживает нелинейные пружины.");
        if (bc.UFixed.Any(v => v != 0))
            throw new NotSupportedException("SolveLocalBuckling — только с нулевыми заданными перемещениями.");
        var fixedSys = SysFixed(bc.FixedDofs);
        lock (_cholGate)
        {
            var asm = Assembler(bc, fixedSys);
            var a = asm.AssembleWith(e =>
            {
                var k = ElementK(e);
                Dense.AddScaledInPlace(k, ElementKGeometric(e, uRef, shellInPlane, beamAxial), 1.0);
                return k;
            }, MaxDegreeOfParallelism);
            var chol = FactorizeCached(a);
            if (!chol.LastFactorizationSpd)
            {
                string where = chol.FirstNonPositivePivot is int i and >= 0 && i < asm.Free.Length ? " — " + DescribeDof(asm.Free[i]) : "";
                throw new InvalidOperationException($"Матрица K + K_σ не положительно определена — схема неустойчива при сочетании{where}.");
            }
            var sysToFree = new int[NSys];
            Array.Fill(sysToFree, -1);
            for (int i = 0; i < asm.Free.Length; i++) sysToFree[asm.Free[i]] = i;
            var result = new StructuralLocalBuckling[groups.Count];
            Parallel.For(0, groups.Count, new ParallelOptions { MaxDegreeOfParallelism = MaxDegreeOfParallelism },
                g => result[g] = LocalBuckling(groups[g], uRef, chol, asm.Free.Length, sysToFree, shellInPlane, beamAxial));
            return result;
        }
    }

    // Локальная задача одной группы (см. SolveLocalBuckling).
    private StructuralLocalBuckling LocalBuckling(int[] group, double[] uRef, SupernodalCholeskySolver chol, int nf,
        int[] sysToFree, bool shellInPlane, double[]? beamAxial)
    {
        // Строки T полных DOF стержней группы в свободных неизвестных: (неизвестная, коэффициент), закреплённые опущены.
        (int Col, double Coef)[] Row(int dof)
        {
            if (Links == null) return sysToFree[dof] >= 0 ? [(sysToFree[dof], 1.0)] : [];
            return Links.Row(dof).Where(r => sysToFree[r.Col] >= 0).Select(r => (sysToFree[r.Col], r.Coef)).ToArray();
        }
        var pos = new Dictionary<int, int>();
        var free = new List<int>();
        var rows = new (int Col, double Coef)[group.Length][][];
        for (int s = 0; s < group.Length; s++)
        {
            var dofs = BeamDofs(Beams[group[s]]);
            rows[s] = new (int, double)[12][];
            for (int a = 0; a < 12; a++)
            {
                var r = Row(dofs[a]);
                for (int t = 0; t < r.Length; t++)
                {
                    if (!pos.TryGetValue(r[t].Col, out int p)) { pos[r[t].Col] = p = free.Count; free.Add(r[t].Col); }
                    r[t] = (p, r[t].Coef);
                }
                rows[s][a] = r;
            }
        }
        int n = free.Count;
        var empty = new StructuralLocalModes([], [], []);
        if (n == 0) return new StructuralLocalBuckling(empty, empty);

        var axial = group.Select(b => beamAxial?[b] ?? BeamAxialForce(b, uRef)).ToArray();
        var g = new double[n, n];
        for (int s = 0; s < group.Length; s++)
        {
            var kg = ElementKGeometric(Shells.Count + group[s], uRef, shellInPlane, beamAxial);
            for (int a = 0; a < 12; a++)
                foreach (var (pa, ta) in rows[s][a])
                    for (int b = 0; b < 12; b++)
                        foreach (var (pb, tb) in rows[s][b]) g[pa, pb] -= ta * kg[a, b] * tb;
        }

        var f = new double[n, n];
        for (int j = 0; j < n; j++)
        {
            var rhs = new double[nf];
            rhs[free[j]] = 1.0;
            var x = chol.Solve(rhs);
            for (int i = 0; i < n; i++) f[i, j] = x[free[i]];
        }
        for (int i = 0; i < n; i++)
            for (int j = 0; j < i; j++) f[i, j] = f[j, i] = 0.5 * (f[i, j] + f[j, i]);

        // Концы группы — узлы одного стержня группы; закрепления — поперечные оси первого стержня.
        var (axes, _) = BeamElements.Beam3dFrame(BeamCoords(group[0]), Beams[group[0]].RefVec);
        var degree = new Dictionary<int, int>();
        foreach (int b in group)
            foreach (int node in new[] { Beams[b].I, Beams[b].J }) degree[node] = degree.GetValueOrDefault(node) + 1;
        var constraints = new List<double[]>();
        for (int s = 0; s < group.Length; s++)
            for (int end = 0; end < 2; end++)
            {
                int node = end == 0 ? Beams[group[s]].I : Beams[group[s]].J;
                if (degree[node] != 1) continue;
                foreach (int axis in new[] { 1, 2 })
                {
                    var c = new double[n];
                    for (int comp = 0; comp < 3; comp++)
                        foreach (var (p, coef) in rows[s][6 * end + comp]) c[p] += axes[axis, comp] * coef;
                    if (c.Any(v => v != 0)) constraints.Add(c);
                }
            }

        StructuralLocalModes Modes(double[,] flex)
        {
            var (sv, v) = SymmetricEigen.Jacobi(flex);
            double sMax = sv.Length == 0 ? 0 : sv.Max();
            var root = new double[n, n];   // F^½
            for (int k = 0; k < n; k++)
            {
                if (!(sv[k] > 1e-14 * sMax)) continue;
                double r = Math.Sqrt(sv[k]);
                for (int i = 0; i < n; i++)
                    for (int j = 0; j < n; j++) root[i, j] += v[i, k] * r * v[j, k];
            }
            var (nu, y) = SymmetricEigen.Jacobi(Dense.MatMul(Dense.MatMul(root, g), root));
            double nuMax = nu.Length == 0 ? 0 : nu.Max();
            var order = Enumerable.Range(0, n).Where(k => nu[k] > 1e-10 * nuMax && nuMax > 0)
                .OrderByDescending(k => nu[k]).ToArray();
            var alpha = new double[order.Length];
            var energy = new BeamModeEnergy[order.Length];
            var restraints = new BeamGroupRestraints[order.Length];
            for (int m = 0; m < order.Length; m++)
            {
                int k = order[m];
                alpha[m] = 1.0 + 1.0 / nu[k];
                var phi = new double[n];
                for (int i = 0; i < n; i++)
                    for (int j = 0; j < n; j++) phi[i] += root[i, j] * y[j, k];
                double uxy = 0, wxy = 0, uxz = 0, wxz = 0;
                var ds = new double[group.Length][];
                for (int s = 0; s < group.Length; s++)
                {
                    var d = ds[s] = new double[12];
                    for (int a = 0; a < 12; a++)
                        foreach (var (p, coef) in rows[s][a]) d[a] += coef * phi[p];
                    var e = BeamEnergyAt(group[s], d, axial[s]);
                    (uxy, wxy, uxz, wxz) = (uxy + e.Uxy, wxy + e.Wxy, uxz + e.Uxz, wxz + e.Wxz);
                }
                energy[m] = new BeamModeEnergy(uxy, wxy, uxz, wxz);
                restraints[m] = GroupRestraintsAt(group, ds, axial, alpha[m]);
            }
            return new StructuralLocalModes(alpha, energy, restraints);
        }

        var surround = Surround(group, rows, f, g, axes);

        var sway = Modes(f);
        if (constraints.Count == 0) return new StructuralLocalBuckling(sway, sway, surround);
        // F′ = F − F·B·(Bᵀ·F·B)⁻¹·Bᵀ·F; B — закрепления по столбцам.
        var bm = new double[n, constraints.Count];
        for (int c = 0; c < constraints.Count; c++)
            for (int i = 0; i < n; i++) bm[i, c] = constraints[c][i];
        var fb = Dense.MatMul(f, bm);
        var braced = (double[,])f.Clone();
        Dense.AddScaledInPlace(braced, Dense.MatMul(fb, Dense.MatMul(DenseLinAlg.PseudoInverse(Dense.MatTMul(bm, fb)),
            Dense.Transpose(fb))), -1.0);
        for (int i = 0; i < n; i++)
            for (int j = 0; j < i; j++) braced[i, j] = braced[j, i] = 0.5 * (braced[i, j] + braced[j, i]);
        return new StructuralLocalBuckling(sway, Modes(braced), surround);
    }

    // Жёсткости остальной схемы, сведённой к концам группы (StructuralLocalBuckling.Surround): S = F⁻¹ − K_гр − K_σ,гр
    // на DOF группы (F⁻¹ = S_rest + K_гр + K_σ,гр), G = −K_σ,гр. Направление DOF конца c — по строкам T в осях первого
    // стержня; r = qᵀ·S·q, q = c/(cᵀc) (единичное перемещение DOF); закреплённый DOF (c = 0) — +∞.
    private BeamGroupRestraints? Surround(int[] group, (int Col, double Coef)[][][] rows, double[,] f, double[,] g,
        double[,] axes0)
    {
        if (GroupEnds(group, axes0) is not { } ends) return null;
        int n = f.GetLength(0);
        var (sv, v) = SymmetricEigen.Jacobi(f);
        double sMax = sv.Length == 0 ? 0 : sv.Max();
        var s = new double[n, n];
        for (int k = 0; k < n; k++)
        {
            if (!(sv[k] > 1e-14 * sMax)) continue;
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++) s[i, j] += v[i, k] * v[j, k] / sv[k];
        }
        for (int b = 0; b < group.Length; b++)
        {
            var ke = ElementK(Shells.Count + group[b]);
            for (int a = 0; a < 12; a++)
                foreach (var (pa, ta) in rows[b][a])
                    for (int c = 0; c < 12; c++)
                        foreach (var (pb, tb) in rows[b][c]) s[pa, pb] -= ta * ke[a, c] * tb;
        }
        Dense.AddScaledInPlace(s, g, 1.0);

        double[] Dir((int S, int End, int Node) e, int part, int axis)
        {
            var c = new double[n];
            for (int comp = 0; comp < 3; comp++)
                foreach (var (p, coef) in rows[e.S][6 * e.End + 3 * part + comp]) c[p] += axes0[axis, comp] * coef;
            double cc = c.Sum(x => x * x);
            if (cc > 0) for (int i = 0; i < n; i++) c[i] /= cc;
            return c;
        }
        double Quad(double[] a, double[] b)
        {
            double r = 0;
            for (int i = 0; i < n; i++)
                if (a[i] != 0)
                    for (int j = 0; j < n; j++) r += a[i] * s[i, j] * b[j];
            return r;
        }
        bool Fixed(double[] c) => c.All(x => x == 0);
        double Stiffness(double[] c) => Fixed(c) ? double.PositiveInfinity : Quad(c, c);

        PlaneRestraint Plane(int lat, int rot)
        {
            var cI = Dir(ends.I, 0, lat);
            var cJ = Dir(ends.J, 0, lat);
            double r3;
            if (Fixed(cI) || Fixed(cJ)) r3 = Fixed(cI) ? Stiffness(cJ) : Stiffness(cI);
            else
            {
                // Пара сил Q в J и −Q в I: Δ = Q·eᵀS⁻¹e, e = (−1, 1), r₃ = det/(s_II + s_JJ + 2·s_IJ).
                double sII = Quad(cI, cI), sJJ = Quad(cJ, cJ), sIJ = Quad(cI, cJ);
                double den = sII + sJJ + 2 * sIJ, det = sII * sJJ - sIJ * sIJ;
                r3 = Math.Abs(den) > 1e-12 * (Math.Abs(sII) + Math.Abs(sJJ)) ? det / den : double.PositiveInfinity;
            }
            return new PlaneRestraint(Stiffness(Dir(ends.I, 1, rot)), Stiffness(Dir(ends.J, 1, rot)), r3);
        }
        return new BeamGroupRestraints(Plane(1, 2), Plane(2, 1));
    }

    // Итерации подпространства для −K_σ·x = ν·K·x: X̄ = K⁻¹·B·X, проекции K_r = X̄ᵀ·B·X (= X̄ᵀ·K·X̄), B_r = X̄ᵀ·B·X̄,
    // K-ортонормированный базис проекции T (K_r = U·S·Uᵀ, T = U·S^−½ без почти нулевых S — ранг B бывает меньше
    // размера подпространства), C = Tᵀ·B_r·T (Якоби), X = X̄·T·W. Сходимость — по ν первых nModes положительных значений.
    private static (double[] Nu, double[][] X, int Iterations, bool Converged) SubspaceIteration(
        SupernodalCholeskySolver chol, CscMatrix b, int n, int nModes, double tol, int maxIter, int maxDop)
    {
        int q = Math.Min(n, Math.Max(2 * nModes, nModes + 8));
        var rng = new Random(20261018);
        var x = new double[q][];
        for (int j = 0; j < q; j++)
        {
            x[j] = new double[n];
            for (int i = 0; i < n; i++) x[j][i] = rng.NextDouble() - 0.5;
        }
        var po = new ParallelOptions { MaxDegreeOfParallelism = maxDop };
        double[] nuPrev = [];
        var nu = new double[q];
        for (int it = 1; it <= maxIter; it++)
        {
            q = x.Length;
            var y = new double[q][];
            var xb = new double[q][];
            Parallel.For(0, q, po, j =>
            {
                y[j] = b.Multiply(x[j]);
                xb[j] = chol.Solve(y[j]);
            });
            var by = new double[q][];
            Parallel.For(0, q, po, j => by[j] = b.Multiply(xb[j]));
            var kr = new double[q, q];
            var br = new double[q, q];
            for (int i = 0; i < q; i++)
                for (int j = 0; j <= i; j++)
                {
                    kr[i, j] = kr[j, i] = 0.5 * (Dense.Dot(xb[i], y[j]) + Dense.Dot(xb[j], y[i]));
                    br[i, j] = br[j, i] = 0.5 * (Dense.Dot(xb[i], by[j]) + Dense.Dot(xb[j], by[i]));
                }
            var t = KOrthonormalBasis(kr);
            int r = t.GetLength(1);
            if (r == 0) return ([], [], it, false);
            var c = Dense.MatMul(Dense.MatTMul(t, br), t);
            var (values, w) = SymmetricEigen.Jacobi(c);
            var order = Enumerable.Range(0, r).OrderByDescending(k => values[k]).ToArray();
            var z = Dense.MatMul(t, w);   // T·W, q×r
            var xn = new double[r][];
            Parallel.For(0, r, po, k =>
            {
                int col = order[k];
                var v = new double[n];
                for (int j = 0; j < q; j++)
                {
                    double zc = z[j, col];
                    if (zc == 0.0) continue;
                    var xj = xb[j];
                    for (int i = 0; i < n; i++) v[i] += zc * xj[i];
                }
                xn[k] = v;
            });
            x = xn;
            q = r;
            nu = new double[r];
            for (int k = 0; k < r; k++) nu[k] = values[order[k]];

            int check = 0;
            while (check < q && check < nModes && nu[check] > 0) check++;
            bool converged = nuPrev.Length == q && check > 0;
            for (int k = 0; converged && k < check; k++)
                converged = Math.Abs(nu[k] - nuPrev[k]) <= tol * Math.Abs(nu[k]);
            // Нет положительных ν — сжатия, способного вызвать потерю устойчивости, нет.
            if (converged || (it >= 5 && check == 0))
                return ((double[])nu.Clone(), x, it, converged);
            nuPrev = (double[])nu.Clone();
        }
        return ((double[])nu.Clone(), x, maxIter, false);
    }

    // Базис T (q×r) с Tᵀ·K_r·T = I по собственным векторам K_r; направления с S ≤ 1e-10·max S (линейная зависимость
    // столбцов X̄, ранг B меньше q) отбрасываются.
    private static double[,] KOrthonormalBasis(double[,] kr)
    {
        int q = kr.GetLength(0);
        var (s, u) = SymmetricEigen.Jacobi(kr);
        double max = s.Length == 0 ? 0 : s.Max();
        var keep = Enumerable.Range(0, q).Where(k => s[k] > 1e-10 * max && max > 0).ToArray();
        var t = new double[q, keep.Length];
        for (int c = 0; c < keep.Length; c++)
        {
            double f = 1.0 / Math.Sqrt(s[keep[c]]);
            for (int i = 0; i < q; i++) t[i, c] = u[i, keep[c]] * f;
        }
        return t;
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
            double[] Solve(int i)
            {
                // Заданные перемещения нулевые — правая часть есть нагрузка на свободных DOF.
                var fSys = SysVector(loads[i]);
                var fi = Array.ConvertAll(asm.Free, d => fSys[d]);
                var uFree = chol.LastFactorizationSpd ? chol.Solve(fi) : SolveNotSpd(chol, kff, fi, asm.Free);
                return Full(DirichletReducer.Expand(NSys, asm.Free, uFree, fixedSys, bc.UFixed));
            }
            // Прямой и обратный ходы по готовому множителю независимы (только чтение L) — правые части параллельно.
            if (chol.LastFactorizationSpd && loads.Count > 1)
                Parallel.For(0, loads.Count, new ParallelOptions { MaxDegreeOfParallelism = MaxDegreeOfParallelism },
                    i => result[i] = Solve(i));
            else
                for (int i = 0; i < loads.Count; i++) result[i] = Solve(i);
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
        var trace = new List<NewtonStepTrace>();
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
                catch (InvalidOperationException ex)
                {
                    trace.Add(new NewtonStepTrace(step, it, resid, 0.0, false, double.NaN, double.NaN, double.NaN, ex.Message));
                    break;
                }
                if (!duFree.All(double.IsFinite))
                {
                    trace.Add(new NewtonStepTrace(step, it, resid, 0.0, false, double.NaN, double.NaN, double.NaN, "приращение не конечно"));
                    break;
                }

                var uBak = free.Select(i => uSys[i]).ToArray();
                double stepNorm = Dense.Norm(duFree) / Math.Max(Dense.Norm(uBak), 1e-300);
                double alpha = 1.0;
                bool accepted = false;
                double firstTrial = double.NaN, lastTrial = double.NaN;
                if (lineSearch)
                {
                    for (int ls = 0; ls < maxLsSteps; ls++)
                    {
                        SetFree(uBak, duFree, alpha);
                        var rTrial = Residual(fStep);
                        lastTrial = IsFinite(rTrial, free) ? NormAt(rTrial, free) / fNorm : double.PositiveInfinity;
                        if (ls == 0) firstTrial = lastTrial;
                        if (lastTrial < resid) { accepted = true; break; }
                        alpha *= 0.5;
                    }
                }
                trace.Add(new NewtonStepTrace(step, it, resid, alpha, accepted, firstTrial, lastTrial, stepNorm, null));
                if (accepted) continue;
                SetFree(uBak, duFree, alpha);
            }
            if (verbose && !converged)
                Console.WriteLine($"  step {step}: не сошёлся за {maxIter} итераций");
            bc.CommitStep(u);
            CommitStep(u);
        }
        LastNewtonTimings = new NewtonSolveTimings(nSolves, nFInt, tTangent, tFactor, tFInt, clock.Elapsed.TotalSeconds);
        LastNewtonTrace = trace;
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

    /// <summary>
    /// Наибольший порядок системы, для которой при не положительно определённой матрице пробуется LU; больше — сразу
    /// исключение. Расчёт шагами с дроблением снижает порог: выше предельной нагрузки LU шаг не спасает, а на десятках
    /// тысяч неизвестных занимает минуты на решение.
    /// </summary>
    public int LuFallbackSize { get; set; } = LuFallbackMaxSize;

    // Не положительно определённая матрица: небольшая система — LU; большая (или вырожденная для LU) — исключение с
    // узлом и DOF первого неположительного ведущего элемента.
    private double[] SolveNotSpd(SupernodalCholeskySolver chol, CscMatrix a, double[] b, int[] free)
    {
        string where = chol.FirstNonPositivePivot is int i and >= 0 && i < free.Length ? " — " + DescribeDof(free[i]) : "";
        if (a.Cols <= LuFallbackSize)
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

    /// <summary>Шаги Ньютона последнего <see cref="SolveNonlinear"/>: line search и причина выхода (диагностика).</summary>
    public IReadOnlyList<NewtonStepTrace>? LastNewtonTrace { get; private set; }

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
