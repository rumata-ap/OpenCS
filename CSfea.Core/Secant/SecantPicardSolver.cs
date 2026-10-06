using CSfea.Sparse;

namespace CSfea.Core;

/// <summary>Состояние сечения КЭ после пересчёта: трещина, текучесть арматуры, отказ (деформации за пределом диаграммы).</summary>
public readonly record struct SecantSectionStatus(bool Cracked, bool Yielded, bool Failed);

/// <summary>Новая (целевая) секущая матрица оболочечного КЭ и его состояние.</summary>
public sealed record SecantShellEvaluation(ShellTangent Target, SecantSectionStatus Status);

/// <summary>Новая (целевая) секущая матрица стержневого КЭ и его состояние.</summary>
public sealed record SecantBeamEvaluation(double[,] Target, SecantSectionStatus Status);

/// <summary>
/// Нелинейный закон сечения оболочечного КЭ для секущего расчёта. Матрицы — в осях сечения (у повёрнутого сечения
/// сетки — оси внутреннего <see cref="RotatedShellResponse.Inner"/>), единицы CSfea. Пересчёт может добавлять
/// пробные трещины; <see cref="Commit"/> фиксирует их после сходимости шага, <see cref="Revert"/> отменяет.
/// <see cref="Evaluate"/> и <see cref="TrueForces"/> вызываются параллельно для разных КЭ.
/// </summary>
public interface ISecantShellState
{
    /// <summary>Секущее сечение, стоящее в сетке.</summary>
    SecantShellResponse Response { get; }

    /// <summary>Начальная (упругая) матрица — масштаб изменений жёсткости и нижняя граница диагонали.</summary>
    ShellTangent Initial { get; }

    /// <summary>Секущая матрица и состояние по деформациям центра КЭ.</summary>
    SecantShellEvaluation Evaluate(double[] epsM, double[] kappa, double[] gamma);

    /// <summary>Истинные усилия закона (для невязки) при текущем пробном состоянии.</summary>
    ShellForces TrueForces(double[] epsM, double[] kappa, double[] gamma);

    void Commit();
    void Revert();
}

/// <summary>
/// Нелинейный закон сечения стержневого КЭ для секущего расчёта (оси КЭ, единицы CSfea). Состояние — по трём точкам
/// Лобатто ξ = 0, ½, 1. Методы вызываются последовательно (сечения CScore мутируют фибры при расчёте).
/// </summary>
public interface ISecantBeamState
{
    SecantBeamResponse Response { get; }
    double[,] Initial { get; }

    /// <summary>Секущая матрица КЭ по деформациям (ε₀, κ_y, κ_z) в точках ξ = 0, ½, 1.</summary>
    SecantBeamEvaluation Evaluate(IReadOnlyList<(double Eps0, double KappaY, double KappaZ)> strains);

    /// <summary>Истинные усилия в точке ξ при текущем пробном состоянии (для невязки).</summary>
    BeamForces TrueForces(double xi, double eps0, double kappaY, double kappaZ);

    void Commit();
    void Revert();
}

/// <summary>Стадия нагружения: полная нагрузка в конце стадии (Н, Н·м; длина — NDof сетки) и число шагов.</summary>
public sealed record SecantLoadStage(string Name, double[] FEnd, int Steps = 1);

/// <summary>Параметры секущего расчёта (спека, раздел 4).</summary>
public sealed class SecantPicardOptions
{
    /// <summary>Наибольшее число итераций на шаге.</summary>
    public int MaxIterations { get; init; } = 50;

    /// <summary>Начальный коэффициент релаксации жёсткостей и границы Эйткена.</summary>
    public double Omega0 { get; init; } = 0.7;
    public double OmegaMin { get; init; } = 0.1;
    public double OmegaMax { get; init; } = 1.0;

    /// <summary>Допуск ‖Δu‖/‖u‖.</summary>
    public double TolDisplacement { get; init; } = 1e-4;

    /// <summary>
    /// Допуск изменения секущих жёсткостей, измеренного по усилиям: max|(S_new − S)·ε| по КЭ, отнесённый к max|S_new·ε|
    /// (компоненты — в масштабе √S₀ᵢᵢ начальной матрицы).
    /// </summary>
    public double TolStiffness { get; init; } = 1e-3;

    /// <summary>Геометрическая нелинейность: оболочки — фон Карман, стержни — CR, при замороженных секущих.</summary>
    public bool Geometric { get; init; }

    /// <summary>Дроблений шага пополам при несходимости (несходимость за MaxIterations/2 итераций — сигнал дробить).</summary>
    public int MaxBisections { get; init; } = 4;

    /// <summary>
    /// Нижняя граница диагонали секущей матрицы — доля начальной: у КЭ, где трещины прошли через всю толщину при
    /// двухосном растяжении, сдвиговой жёсткости в плоскости нет, и без границы матрица системы вырождена.
    /// </summary>
    public double StiffnessFloor { get; init; } = 1e-4;

    /// <summary>Истинная невязка на каждой итерации (иначе — только по сошедшемуся шагу).</summary>
    public bool TrueResidualEachIteration { get; init; }

    /// <summary>Параллельность пересчёта оболочек (−1 — по числу ядер).</summary>
    public int MaxDegreeOfParallelism { get; init; } = -1;

    /// <summary>Журнал итераций (строки).</summary>
    public Action<string>? Log { get; init; }
}

/// <summary>
/// Итерация Пикара: релаксация ω, изменения перемещений и жёсткостей (<see cref="DStiffness"/> — мера по усилиям, см.
/// <see cref="SecantPicardOptions.TolStiffness"/>), невязка (NaN — не считалась), состояние КЭ.
/// </summary>
public sealed record SecantIterationRecord(int Stage, int Step, double LoadFactor, int Iteration, double Omega,
    double DuRel, double DStiffness, double TrueResidual, int Cracked, int Yielded, int Failed);

/// <summary>
/// Шаг нагружения: <see cref="LoadFactor"/> — доля стадии (0…1), <see cref="IsRefinement"/> — промежуточный шаг
/// дробления, перемещения и состояние КЭ (индексы сетки) после шага.
/// </summary>
public sealed record SecantStepResult(int Stage, int Step, double LoadFactor, bool IsRefinement, bool Converged,
    int Iterations, double TrueResidual, double[] U, SecantSectionStatus[] Shells, SecantSectionStatus[] Beams);

/// <summary>Итог секущего расчёта.</summary>
public sealed class SecantPicardResult
{
    /// <summary>Принятые (сошедшиеся) шаги; последний несошедшийся — с <c>Converged = false</c>.</summary>
    public List<SecantStepResult> Steps { get; } = new();

    public List<SecantIterationRecord> Iterations { get; } = new();

    /// <summary>Все стадии пройдены.</summary>
    public bool Completed { get; internal set; }

    /// <summary>Стадия (с 0), на которой шаг не сошёлся даже после дробления — «предельная стадия»; null — нет.</summary>
    public int? LimitStage { get; internal set; }

    public string? Message { get; internal set; }

    /// <summary>Последний сошедшийся шаг каждой стадии (null — стадия не дошла до конца).</summary>
    public SecantStepResult? StageEnd(int stage)
        => Steps.LastOrDefault(s => s.Stage == stage && s.Converged && Math.Abs(s.LoadFactor - 1.0) < 1e-12);
}

/// <summary>
/// Секущий расчёт (вариант 1 спеки): на каждом шаге — итерации Пикара по полной нагрузке шага. Линейная задача с
/// замороженными секущими матрицами КЭ → деформации (центры оболочек, точки Лобатто стержней) → новые секущие по
/// нелинейным законам → релаксация жёсткостей S ← S + ω·(S_new − S) с ω по Эйткену (Айронс — Так, невязка —
/// (S_new − S)·ε по КЭ, ω ∈ [ω_min, ω_max]) → проверка: изменение жёсткостей, измеренное по усилиям КЭ, &lt; TolStiffness и
/// ‖Δu‖/‖u‖ &lt; TolDisplacement. После сходимости — Commit состояний (трещины). Несошедшийся шаг не принимается:
/// он дробится пополам, а если не сходится и после дроблений — стадия помечается предельной, расчёт прекращается.
/// Истинная невязка ‖F − F_int,true‖/‖F‖ по законам сечений в точках Гаусса — показатель качества, не критерий.
/// </summary>
public sealed class SecantPicardSolver
{
    private readonly StructuralMesh _mesh;
    private readonly BoundaryConditions _bc;
    private readonly ISecantShellState?[] _shells;
    private readonly ISecantBeamState?[] _beams;
    private readonly double[]?[] _shellAngle;   // угол поворота сечения (null — без поворота)
    private readonly SecantPicardOptions _o;

    public SecantPicardSolver(StructuralMesh mesh, BoundaryConditions bc,
        IReadOnlyList<ISecantShellState?> shells, IReadOnlyList<ISecantBeamState?> beams,
        SecantPicardOptions? options = null)
    {
        _mesh = mesh;
        _bc = bc;
        if (shells.Count != mesh.Shells.Count) throw new ArgumentException("Число состояний оболочек ≠ числу оболочек сетки.");
        if (beams.Count != mesh.Beams.Count) throw new ArgumentException("Число состояний стержней ≠ числу стержней сетки.");
        _shells = shells.ToArray();
        _beams = beams.ToArray();
        _o = options ?? new SecantPicardOptions();
        _shellAngle = new double[]?[_shells.Length];
        for (int e = 0; e < _shells.Length; e++)
        {
            if (_shells[e] is not { } st) continue;
            var sec = mesh.Shells[e].Section;
            if (sec is RotatedShellResponse rot)
            {
                if (!ReferenceEquals(rot.Inner, st.Response))
                    throw new ArgumentException($"Оболочка {e}: в сетке стоит не секущее сечение своего состояния.");
                _shellAngle[e] = new[] { rot.Angle };
            }
            else if (!ReferenceEquals(sec, st.Response))
                throw new ArgumentException($"Оболочка {e}: в сетке стоит не секущее сечение своего состояния.");
        }
        for (int e = 0; e < _beams.Length; e++)
            if (_beams[e] is { } st && !ReferenceEquals(mesh.Beams[e].Section, st.Response))
                throw new ArgumentException($"Стержень {e}: в сетке стоит не секущее сечение своего состояния.");
    }

    // ---------------- стадии и шаги ----------------

    public SecantPicardResult Run(IReadOnlyList<SecantLoadStage> stages)
    {
        var result = new SecantPicardResult();
        var u = new double[_mesh.NDof];
        var fPrev = new double[_mesh.NDof];
        var shellStatus = new SecantSectionStatus[_shells.Length];
        var beamStatus = new SecantSectionStatus[_beams.Length];

        for (int si = 0; si < stages.Count; si++)
        {
            var stage = stages[si];
            if (stage.FEnd.Length != _mesh.NDof) throw new ArgumentException($"Стадия «{stage.Name}»: длина вектора нагрузки ≠ NDof.");
            int nSteps = Math.Max(1, stage.Steps);
            var fStart = fPrev;
            int stepNo = 0;
            bool ok = true;

            // Шаг [a, b] доли стадии; при несходимости — две половины (до MaxBisections).
            bool SolveRange(double a, double b, int depth)
            {
                var f = Lerp(fStart, stage.FEnd, b);
                bool allowAbort = depth < _o.MaxBisections;
                var attempt = Picard(si, stepNo + 1, b, f, u, allowAbort, result);
                if (attempt.Converged)
                {
                    u = attempt.U;
                    Array.Copy(attempt.ShellStatus, shellStatus, shellStatus.Length);
                    Array.Copy(attempt.BeamStatus, beamStatus, beamStatus.Length);
                    stepNo++;
                    bool refinement = depth > 0 && Math.Abs(b * nSteps - Math.Round(b * nSteps)) > 1e-9;
                    result.Steps.Add(new SecantStepResult(si, stepNo, b, refinement, true, attempt.Iterations,
                        attempt.TrueResidual, (double[])u.Clone(), (SecantSectionStatus[])shellStatus.Clone(),
                        (SecantSectionStatus[])beamStatus.Clone()));
                    Log($"стадия «{stage.Name}» λ = {b:0.####}: сошлось за {attempt.Iterations} ит., невязка {attempt.TrueResidual:e2}");
                    return true;
                }
                if (allowAbort)
                {
                    Log($"стадия «{stage.Name}» λ = {b:0.####}: нет сходимости — дробление шага");
                    double mid = 0.5 * (a + b);
                    return SolveRange(a, mid, depth + 1) && SolveRange(mid, b, depth + 1);
                }
                result.Steps.Add(new SecantStepResult(si, stepNo + 1, b, depth > 0, false, attempt.Iterations,
                    attempt.TrueResidual, attempt.U, attempt.ShellStatus, attempt.BeamStatus));
                return false;
            }

            for (int k = 1; k <= nSteps && ok; k++)
                ok = SolveRange((double)(k - 1) / nSteps, (double)k / nSteps, 0);
            if (!ok)
            {
                result.LimitStage = si;
                result.Message = $"Стадия «{stage.Name}»: шаг не сошёлся и после дробления — предельная стадия, расчёт остановлен.";
                Log(result.Message);
                return result;
            }
            fPrev = stage.FEnd;
        }
        result.Completed = true;
        return result;
    }

    private sealed record Attempt(bool Converged, int Iterations, double TrueResidual, double[] U,
        SecantSectionStatus[] ShellStatus, SecantSectionStatus[] BeamStatus);

    // ---------------- итерации Пикара одного шага ----------------

    private Attempt Picard(int stage, int step, double lambda, double[] f, double[] uStart, bool allowAbort,
                           SecantPicardResult result)
    {
        var shellSnap = _shells.Select(s => s?.Response.Matrix).ToArray();
        var beamSnap = _beams.Select(b => b?.Response.Matrix).ToArray();
        var shellStatus = new SecantSectionStatus[_shells.Length];
        var beamStatus = new SecantSectionStatus[_beams.Length];
        var uPrev = uStart;
        double[]? rPrev = null;
        double omega = _o.Omega0;
        double residual = double.NaN;
        int abortAt = allowAbort ? Math.Max(1, _o.MaxIterations / 2) : _o.MaxIterations;

        for (int it = 1; it <= _o.MaxIterations; it++)
        {
            var u = SolveFrozen(f, uPrev);
            if (u == null || !u.All(double.IsFinite))
            {
                Log($"  итерация {it}: линейная задача не решена (вырожденная матрица или срыв Ньютона)");
                break;
            }

            var shellTargets = new ShellTangent?[_shells.Length];
            var beamT = new double[_beams.Length][,];
            var (dK, r, worst) = EvaluateAll(u, shellTargets, beamT, shellStatus, beamStatus);   // dK — мера по усилиям

            double du = Dense.Norm(Dense.SubV(u, uPrev)) / Math.Max(Dense.Norm(u), 1e-300);
            bool converged = dK < _o.TolStiffness && (it == 1 || du < _o.TolDisplacement);
            if (_o.TrueResidualEachIteration || converged) residual = TrueResidual(f, u);
            int cracked = shellStatus.Count(s => s.Cracked) + beamStatus.Count(s => s.Cracked);
            int yielded = shellStatus.Count(s => s.Yielded) + beamStatus.Count(s => s.Yielded);
            int failed = shellStatus.Count(s => s.Failed) + beamStatus.Count(s => s.Failed);

            // ω по Эйткену (Айронс — Так). Невязка неподвижной точки — r = (S_new − S)·ε (нормированная): приращения
            // перемещений для этого не годятся — в них уже сидит прошлый ω.
            if (!converged && rPrev != null)
            {
                var diff = Dense.SubV(r, rPrev);
                double dd = Dense.Dot(diff, diff);
                double w = dd > 0.0 ? -omega * Dense.Dot(rPrev, diff) / dd : _o.Omega0;
                omega = double.IsFinite(w) ? Math.Clamp(w, _o.OmegaMin, _o.OmegaMax) : _o.Omega0;
            }
            double applied = converged ? 1.0 : omega;
            result.Iterations.Add(new SecantIterationRecord(stage, step, lambda, it, applied, du, dK,
                _o.TrueResidualEachIteration || converged ? residual : double.NaN, cracked, yielded, failed));
            Log($"  ст.{stage} шаг {step} λ={lambda:0.####} ит.{it,2}: ω={applied:0.###} ‖Δu‖/‖u‖={du:e2} " +
                $"ΔF={dK:e2}{(double.IsNaN(residual) ? "" : $" невязка={residual:e2}")} трещин {cracked}, " +
                $"текучесть {yielded}, отказ {failed}; max ΔF — {worst}");

            Relax(shellTargets, beamT, applied);
            if (converged)
            {
                foreach (var s in _shells) s?.Commit();
                foreach (var b in _beams) b?.Commit();
                return new Attempt(true, it, residual, u, shellStatus, beamStatus);
            }
            if (it >= abortAt) { uPrev = u; break; }
            uPrev = u;
            rPrev = r;
        }

        // Шаг не принят: состояние и секущие — как в начале шага.
        foreach (var s in _shells) s?.Revert();
        foreach (var b in _beams) b?.Revert();
        for (int e = 0; e < _shells.Length; e++) if (shellSnap[e] is { } m) _shells[e]!.Response.Update(m);
        for (int e = 0; e < _beams.Length; e++) if (beamSnap[e] is { } m) _beams[e]!.Response.Update(m);
        return new Attempt(false, _o.MaxIterations, double.IsNaN(residual) ? TrueResidual(f, uPrev) : residual,
            uPrev, shellStatus, beamStatus);
    }

    /// <summary>Решение с замороженными секущими: линейное или (геометрическая нелинейность) Ньютон от <paramref name="uFrom"/>.</summary>
    private double[]? SolveFrozen(double[] f, double[] uFrom)
    {
        try
        {
            if (!_o.Geometric) return _mesh.SolveLinear(f, _bc);
            var (u, history) = _mesh.SolveNonlinear(f, _bc, nSteps: 1, tol: 1e-8, maxIter: 30,
                corotational: false, u0: uFrom, f0: f);
            return history.AllConverged() ? u : null;
        }
        catch (InvalidOperationException) { return null; }
    }

    /// <summary>
    /// Пересчёт всех КЭ: целевые матрицы (с нижней границей диагонали) и состояние. Изменение жёсткости меряется тем,
    /// что оно меняет, — разностью обобщённых усилий ΔF = (S_new − S)·ε при текущих деформациях КЭ (S_new·ε — истинные
    /// усилия закона). Компоненты делятся на √S₀ᵢᵢ начальной матрицы (соизмеримы N и M); мера — max|ΔF| по всем КЭ,
    /// отнесённый к max|S_new·ε|. Элементы матрицы при деформациях ≈ 0 (например, поперечная жёсткость полосы, где
    /// знак ε меняется от итерации к итерации) на меру не влияют. Вектор ΔF — невязка неподвижной точки для Эйткена.
    /// </summary>
    private (double DF, double[] R, string Worst) EvaluateAll(double[] u, ShellTangent?[] shellTargets,
        double[][,] beamTargets, SecantSectionStatus[] shellStatus, SecantSectionStatus[] beamStatus)
    {
        const int shellBlock = 8, beamBlock = 9;   // (N, M, Q) оболочки; (N, M_y, M_z) в трёх точках стержня
        var r = new double[_shells.Length * shellBlock + _beams.Length * beamBlock];
        var fScale = new double[_shells.Length];
        Parallel.For(0, _shells.Length, new ParallelOptions { MaxDegreeOfParallelism = _o.MaxDegreeOfParallelism }, e =>
        {
            if (_shells[e] is not { } st) return;
            var (eps, kappa, gamma) = ShellStrains(e, u);
            var ev = st.Evaluate(eps, kappa, gamma);
            var target = FloorShell(ev.Target, st.Initial);
            shellTargets[e] = target;
            shellStatus[e] = ev.Status;
            var k0 = Full6(st.Initial);
            var dk = Dense.Sub(Full6(target), Full6(st.Response.Matrix));
            var e6 = new[] { eps[0], eps[1], eps[2], kappa[0], kappa[1], kappa[2] };
            var df = Dense.MatVec(dk, e6);
            var ft = Dense.MatVec(Full6(target), e6);
            var dq = Dense.MatVec(Dense.Sub(target.As, st.Response.Matrix.As), gamma);
            var fq = Dense.MatVec(target.As, gamma);
            double scale = 0.0;
            for (int i = 0; i < 6; i++)
            {
                double sc = Math.Sqrt(Math.Abs(k0[i, i]));
                r[e * shellBlock + i] = sc > 0.0 ? df[i] / sc : 0.0;
                if (sc > 0.0) scale = Math.Max(scale, Math.Abs(ft[i]) / sc);
            }
            for (int i = 0; i < 2; i++)
            {
                double sc = Math.Sqrt(Math.Abs(st.Initial.As[i, i]));
                r[e * shellBlock + 6 + i] = sc > 0.0 ? dq[i] / sc : 0.0;
                if (sc > 0.0) scale = Math.Max(scale, Math.Abs(fq[i]) / sc);
            }
            fScale[e] = scale;
        });
        double fMax = fScale.Length > 0 ? fScale.Max() : 0.0;

        int offset = _shells.Length * shellBlock;
        for (int e = 0; e < _beams.Length; e++)
        {
            if (_beams[e] is not { } st) continue;
            var strains = BeamStrains(e, u, st.Response.Matrix);
            var ev = st.Evaluate(strains);
            var target = FloorBeam(ev.Target, st.Initial);
            beamTargets[e] = target;
            beamStatus[e] = ev.Status;
            var dk = Dense.Sub(target, st.Response.Matrix);
            for (int p = 0; p < 3; p++)
            {
                var ep = new[] { strains[p].Item1, strains[p].Item2, strains[p].Item3 };
                var df = Dense.MatVec(dk, ep);
                var ft = Dense.MatVec(target, ep);
                for (int i = 0; i < 3; i++)
                {
                    double sc = Math.Sqrt(Math.Abs(st.Initial[i, i]));
                    r[offset + e * beamBlock + 3 * p + i] = sc > 0.0 ? df[i] / sc : 0.0;
                    if (sc > 0.0) fMax = Math.Max(fMax, Math.Abs(ft[i]) / sc);
                }
            }
        }

        if (!(fMax > 0.0)) return (0.0, r, "");
        int worst = 0;
        for (int i = 1; i < r.Length; i++) if (Math.Abs(r[i]) > Math.Abs(r[worst])) worst = i;
        for (int i = 0; i < r.Length; i++) r[i] /= fMax;
        string[] shellNames = { "Nx", "Ny", "Nxy", "Mx", "My", "Mxy", "Qx", "Qy" };
        string[] beamNames = { "N", "My", "Mz" };
        string where = worst < offset
            ? $"оболочка {worst / shellBlock} {shellNames[worst % shellBlock]}"
            : $"стержень {(worst - offset) / beamBlock} ξ={0.5 * ((worst - offset) % beamBlock / 3)} {beamNames[(worst - offset) % 3]}";
        return (Math.Abs(r[worst]), r, where);
    }

    private void Relax(ShellTangent?[] shellTargets, double[][,] beamTargets, double omega)
    {
        for (int e = 0; e < _shells.Length; e++)
        {
            if (shellTargets[e] is not { } t) continue;
            var cur = _shells[e]!.Response.Matrix;
            _shells[e]!.Response.Update(new ShellTangent(Mix(cur.A, t.A, omega), Mix(cur.B, t.B, omega),
                Mix(cur.D, t.D, omega), Mix(cur.As, t.As, omega)));
        }
        for (int e = 0; e < _beams.Length; e++)
            if (beamTargets[e] is { } t) _beams[e]!.Response.Update(Mix(_beams[e]!.Response.Matrix, t, omega));
    }

    // ---------------- деформации КЭ ----------------

    /// <summary>Деформации центра оболочки в осях сечения.</summary>
    private (double[] Eps, double[] Kappa, double[] Gamma) ShellStrains(int e, double[] u)
    {
        var dofs = StructuralMesh.NodeDofs(_mesh.Shells[e].Nodes);
        var (eps, kappa, gamma) = ShellElementForces.CenterStrainsGlobal(_mesh.ShellCoords(e), Gather(u, dofs), _o.Geometric);
        return _mesh.Shells[e].Section is RotatedShellResponse rot ? rot.ToSection(eps, kappa, gamma) : (eps, kappa, gamma);
    }

    /// <summary>Деформации стержня в точках Лобатто (с пузырём продольного перемещения по секущей <paramref name="s"/>).</summary>
    private (double, double, double)[] BeamStrains(int e, double[] u, double[,] s)
    {
        var (d, l) = BeamLocal(e, u);
        return new[] { 0.0, 0.5, 1.0 }.Select(xi => BeamElements.Beam3dCoupledStrains(s, l, d, xi)).ToArray();
    }

    /// <summary>Локальные перемещения стержня: линейно — T·u, при геометрической нелинейности — деформационные CR.</summary>
    private (double[] D, double L) BeamLocal(int e, double[] u)
    {
        var b = _mesh.Beams[e];
        var ue = Gather(u, StructuralMesh.NodeDofs(new[] { b.I, b.J }));
        var coords = _mesh.BeamCoords(e);
        if (_o.Geometric)
        {
            var (r, l0) = BeamElements.Beam3dFrame(coords, b.RefVec);
            return (BeamCorotational.Beam3dPLocal(coords, r, l0, ue), l0);
        }
        var (t, l) = BeamElements.Beam3dT(coords, b.RefVec);
        return (Dense.MatVec(t, ue), l);
    }

    // ---------------- истинная невязка ----------------

    /// <summary>
    /// ‖F − F_int,true‖/‖F‖ по поступательным DOF (узловые силы; моменты с силами несоизмеримы): оболочки —
    /// истинные законы в точках Гаусса 2 × 2 (кинематика линейная или фон Карман); стержни и КЭ без нелинейного
    /// закона — по своему сечению в сетке.
    /// </summary>
    private double TrueResidual(double[] f, double[] u)
    {
        var parts = new double[_shells.Length][];
        Parallel.For(0, _shells.Length, new ParallelOptions { MaxDegreeOfParallelism = _o.MaxDegreeOfParallelism }, e =>
        {
            var dofs = StructuralMesh.NodeDofs(_mesh.Shells[e].Nodes);
            IShellSectionResponse sec = _mesh.Shells[e].Section;
            if (_shells[e] is { } st)
            {
                IShellSectionResponse tr = new TrueShellResponse(st);
                sec = _shellAngle[e] is { } a ? new RotatedShellResponse(tr, a[0]) : tr;
            }
            parts[e] = ShellElementForces.ElementFInternalGlobal(_mesh.ShellCoords(e), sec, Gather(u, dofs), _o.Geometric);
        });
        var fInt = new double[_mesh.NDof];
        for (int e = 0; e < _shells.Length; e++)
        {
            var dofs = StructuralMesh.NodeDofs(_mesh.Shells[e].Nodes);
            for (int i = 0; i < dofs.Length; i++) fInt[dofs[i]] += parts[e][i];
        }
        for (int e = 0; e < _beams.Length; e++)
        {
            var b = _mesh.Beams[e];
            var dofs = StructuralMesh.NodeDofs(new[] { b.I, b.J });
            // Стержни — по своей секущей КЭ: одна средняя (по податливости в трёх сечениях Лобатто) матрица на КЭ
            // согласована с законом в этих сечениях, а истинные усилия в точках Гаусса при ней заведомо не
            // уравновешены (жёсткость вдоль КЭ меняется в разы у границы трещин) — такая «невязка» мерила бы схему
            // КЭ, а не сходимость.
            var coords = _mesh.BeamCoords(e);
            var ue = Gather(u, dofs);
            var fe = _o.Geometric
                ? BeamCorotational.Beam3dInternalForce(coords, b.Section, ue, b.RefVec)
                : Dense.MatVec(BeamElements.Beam3dKGlobal(coords, b.Section, b.RefVec), ue);
            for (int i = 0; i < dofs.Length; i++) fInt[dofs[i]] += fe[i];
        }
        return _mesh.RelativeResidual(f, fInt, u, _bc, translationalOnly: true);
    }

    /// <summary>Сечение для невязки: истинные усилия закона, касательная не нужна.</summary>
    private sealed class TrueShellResponse(ISecantShellState state) : IShellSectionResponse
    {
        public ShellForces Forces(double[] epsM, double[] kappa, double[] gamma) => state.TrueForces(epsM, kappa, gamma);
        public ShellTangent Tangent(double[] epsM, double[] kappa, double[] gamma) => state.Response.Matrix;
        public void Commit() { }
        public void Reset() { }
    }

    // ---------------- матрицы ----------------

    private ShellTangent FloorShell(ShellTangent t, ShellTangent init)
    {
        double k = _o.StiffnessFloor;
        if (k <= 0.0) return t;
        var a = (double[,])t.A.Clone();
        var d = (double[,])t.D.Clone();
        for (int i = 0; i < 3; i++)
        {
            a[i, i] = Math.Max(a[i, i], k * init.A[i, i]);
            d[i, i] = Math.Max(d[i, i], k * init.D[i, i]);
        }
        return new ShellTangent(a, t.B, d, t.As);
    }

    private double[,] FloorBeam(double[,] s, double[,] init)
    {
        double k = _o.StiffnessFloor;
        if (k <= 0.0) return s;
        var r = (double[,])s.Clone();
        for (int i = 0; i < 3; i++) r[i, i] = Math.Max(r[i, i], k * init[i, i]);
        return r;
    }

    private static double[,] Full6(ShellTangent m)
    {
        var r = new double[6, 6];
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
            {
                r[i, j] = m.A[i, j];
                r[i, j + 3] = m.B[i, j];
                r[i + 3, j] = m.B[j, i];
                r[i + 3, j + 3] = m.D[i, j];
            }
        return r;
    }

    private static double[,] Mix(double[,] cur, double[,] target, double w)
    {
        int n = cur.GetLength(0), m = cur.GetLength(1);
        var r = new double[n, m];
        for (int i = 0; i < n; i++)
            for (int j = 0; j < m; j++)
                r[i, j] = cur[i, j] + w * (target[i, j] - cur[i, j]);
        return r;
    }

    // ---------------- утилиты ----------------

    private static double[] Lerp(double[] a, double[] b, double t)
    {
        var r = new double[a.Length];
        for (int i = 0; i < r.Length; i++) r[i] = a[i] + t * (b[i] - a[i]);
        return r;
    }

    private static double[] Gather(double[] u, int[] dofs)
    {
        var r = new double[dofs.Length];
        for (int i = 0; i < dofs.Length; i++) r[i] = u[dofs[i]];
        return r;
    }

    private void Log(string s) => _o.Log?.Invoke(s);
}
