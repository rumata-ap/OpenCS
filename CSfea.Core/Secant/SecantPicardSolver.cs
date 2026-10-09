using CSfea.Sparse;

namespace CSfea.Core;

/// <summary>
/// Состояние сечения КЭ после пересчёта: трещина, текучесть арматуры, отказ (деформации за пределом диаграммы);
/// <see cref="Cracks"/> — число треснувших слоёв/точек (рост между итерациями — событие трещинообразования).
/// </summary>
public readonly record struct SecantSectionStatus(bool Cracked, bool Yielded, bool Failed, int Cracks = 0);

/// <summary>Новая (целевая) секущая матрица оболочечного КЭ и его состояние.</summary>
public sealed record SecantShellEvaluation(ShellTangent Target, SecantSectionStatus Status);

/// <summary>
/// Новая (целевая) секущая матрица стержневого КЭ, его состояние и целевые сдвиговые жёсткости (null — сдвиг КЭ не
/// меняется).
/// </summary>
public sealed record SecantBeamEvaluation(double[,] Target, SecantSectionStatus Status, BeamShearStiffness? ShearTarget = null);

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

    /// <summary>Начальные сдвиговые жёсткости — нижняя граница секущих (<see cref="BeamShearStiffness.Rigid"/> — без сдвига).</summary>
    BeamShearStiffness InitialShear => BeamShearStiffness.Rigid;

    /// <summary>
    /// Секущая матрица КЭ по изгибным деформациям (ε₀, κ_y, κ_z) в точках ξ = 0, ½, 1 и углам сдвига КЭ
    /// <paramref name="gammaY"/>, <paramref name="gammaZ"/> (постоянным по длине).
    /// </summary>
    SecantBeamEvaluation Evaluate(IReadOnlyList<(double Eps0, double KappaY, double KappaZ)> strains, double gammaY = 0.0,
        double gammaZ = 0.0);

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
    /// Допуск изменения секущих жёсткостей, измеренного работой: max по КЭ Σᵢ|εᵢ·((S_new − S)·ε)ᵢ|, отнесённый к
    /// наибольшей по КЭ работе Σᵢ|εᵢ·(S_new·ε)ᵢ| (оболочки и стержни — каждые к своей).
    /// </summary>
    public double TolStiffness { get; init; } = 1e-3;

    /// <summary>Геометрическая нелинейность: оболочки — фон Карман, стержни — CR, при замороженных секущих.</summary>
    public bool Geometric { get; init; }

    /// <summary>
    /// Допуск Ньютона при <see cref="Geometric"/> (‖r‖/‖F‖). У схем с жёсткими связями и колоннами невязка упирается в
    /// округление ~1e-8 (плита Дорфмана: 1,5e-8…2,5e-8), поэтому меньший допуск недостижим.
    /// </summary>
    public double GeometricTolerance { get; init; } = 1e-6;

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

    /// <summary>
    /// Принятый (сошедшийся) шаг — сразу после фиксации состояний КЭ (<c>Commit</c>): состояния сечений отвечают
    /// этому шагу, по ним снимаются поля шага. Вызывается в потоке расчёта.
    /// </summary>
    public Action<SecantStepResult>? OnStepAccepted { get; init; }

    /// <summary>Копия с другим <see cref="OnStepAccepted"/>.</summary>
    public SecantPicardOptions With(Action<SecantStepResult>? onStepAccepted) => new()
    {
        MaxIterations = MaxIterations, Omega0 = Omega0, OmegaMin = OmegaMin, OmegaMax = OmegaMax,
        TolDisplacement = TolDisplacement, TolStiffness = TolStiffness, Geometric = Geometric,
        GeometricTolerance = GeometricTolerance, MaxBisections = MaxBisections, StiffnessFloor = StiffnessFloor,
        TrueResidualEachIteration = TrueResidualEachIteration, MaxDegreeOfParallelism = MaxDegreeOfParallelism, Log = Log,
        OnStepAccepted = onStepAccepted,
    };
}

/// <summary>
/// Итерация Пикара: релаксация ω, изменения перемещений и жёсткостей (<see cref="DStiffness"/> — мера по работе, см.
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

/// <summary>
/// Прогресс секущего расчёта — после каждой итерации (<see cref="StepDone"/> = false) и после принятого шага (true).
/// <see cref="Stage"/> — с 0; <see cref="Step"/> — номер шага в стадии с учётом дроблений (как в журнале),
/// <see cref="StepCount"/> — заданное число шагов стадии; <see cref="LoadFactor"/> — доля стадии в конце шага;
/// <see cref="Fraction"/> — пройденная доля всех шагов всех стадий (0…1) для полосы прогресса.
/// </summary>
public sealed record SecantProgress(int Stage, int StageCount, string StageName, int Step, int StepCount,
    double LoadFactor, bool IsRefinement, int Iteration, double DuRel, double DStiffness, int Cracked, int Yielded,
    int Failed, bool StepDone, double Fraction);

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
/// ε·(S_new − S)·ε по КЭ, ω ∈ [ω_min, ω_max]) → проверка: изменение жёсткостей, измеренное работой в КЭ, &lt; TolStiffness и
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
    private readonly double _momentArm;   // средний размер КЭ — плечо узловых моментов в невязке

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
        double sumL = 0.0;
        int nL = 0;
        for (int e = 0; e < mesh.Shells.Count; e++)
        {
            var c = mesh.ShellCoords(e);
            for (int i = 0; i < c.Length; i++) { sumL += Dense.Norm(Dense.SubV(c[(i + 1) % c.Length], c[i])); nL++; }
        }
        for (int e = 0; e < mesh.Beams.Count; e++)
        {
            var c = mesh.BeamCoords(e);
            sumL += Dense.Norm(Dense.SubV(c[1], c[0]));
            nL++;
        }
        _momentArm = nL > 0 ? sumL / nL : 1.0;
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

    /// <summary>
    /// Расчёт по стадиям. Отмена проверяется перед каждой итерацией: незавершённый шаг откатывается (как несошедшийся)
    /// и бросается <see cref="OperationCanceledException"/>; принятые шаги остаются в состояниях КЭ.
    /// </summary>
    public SecantPicardResult Run(IReadOnlyList<SecantLoadStage> stages, IProgress<SecantProgress>? progress = null,
        CancellationToken ct = default)
    {
        var result = new SecantPicardResult();
        int totalSteps = stages.Sum(s => Math.Max(1, s.Steps));
        int stepsBefore = 0;
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
                bool refinement = depth > 0 && Math.Abs(b * nSteps - Math.Round(b * nSteps)) > 1e-9;
                SecantProgress Report(SecantIterationRecord r, bool done, double share) => new(si, stages.Count, stage.Name,
                    stepNo + 1, nSteps, b, refinement, r.Iteration, r.DuRel, r.DStiffness, r.Cracked, r.Yielded, r.Failed,
                    done, Math.Clamp((stepsBefore + share * nSteps) / totalSteps, 0.0, 1.0));
                SecantIterationRecord? last = null;
                Action<SecantIterationRecord>? onIteration = progress == null ? null : r =>
                {
                    last = r;
                    progress.Report(Report(r, false, a));
                };
                var attempt = Picard(si, stepNo + 1, b, f, u, allowAbort, result, onIteration, ct);
                if (attempt.Converged)
                {
                    if (last != null) progress!.Report(Report(last, true, b));
                    u = attempt.U;
                    Array.Copy(attempt.ShellStatus, shellStatus, shellStatus.Length);
                    Array.Copy(attempt.BeamStatus, beamStatus, beamStatus.Length);
                    stepNo++;
                    var accepted = new SecantStepResult(si, stepNo, b, refinement, true, attempt.Iterations,
                        attempt.TrueResidual, (double[])u.Clone(), (SecantSectionStatus[])shellStatus.Clone(),
                        (SecantSectionStatus[])beamStatus.Clone());
                    result.Steps.Add(accepted);
                    _o.OnStepAccepted?.Invoke(accepted);
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
            stepsBefore += nSteps;
        }
        result.Completed = true;
        return result;
    }

    private sealed record Attempt(bool Converged, int Iterations, double TrueResidual, double[] U,
        SecantSectionStatus[] ShellStatus, SecantSectionStatus[] BeamStatus);

    // ---------------- итерации Пикара одного шага ----------------

    private Attempt Picard(int stage, int step, double lambda, double[] f, double[] uStart, bool allowAbort,
                           SecantPicardResult result, Action<SecantIterationRecord>? onIteration, CancellationToken ct)
    {
        var shellSnap = _shells.Select(s => s?.Response.Matrix).ToArray();
        var beamSnap = _beams.Select(b => b?.Response.Matrix).ToArray();
        var beamShearSnap = _beams.Select(b => b?.Response.Shear).ToArray();
        var shellStatus = new SecantSectionStatus[_shells.Length];
        var beamStatus = new SecantSectionStatus[_beams.Length];
        var uPrev = uStart;
        double[]? rPrev = null;
        double omega = _o.Omega0;
        double omegaApplied = 1.0;   // ω последнего обновления жёсткостей (от него получено текущее u)
        double residual = double.NaN;
        int cracksPrev = -1;
        int abortAt = allowAbort ? Math.Max(1, _o.MaxIterations / 2) : _o.MaxIterations;

        // Шаг не принят (несходимость или отмена): состояние и секущие — как в начале шага.
        void Restore()
        {
            foreach (var s in _shells) s?.Revert();
            foreach (var b in _beams) b?.Revert();
            for (int e = 0; e < _shells.Length; e++) if (shellSnap[e] is { } m) _shells[e]!.Response.Update(m);
            for (int e = 0; e < _beams.Length; e++) if (beamSnap[e] is { } m) _beams[e]!.Response.Update(m);
            for (int e = 0; e < _beams.Length; e++) if (beamShearSnap[e] is { } q) _beams[e]!.Response.UpdateShear(q);
        }

        for (int it = 1; it <= _o.MaxIterations; it++)
        {
            if (ct.IsCancellationRequested)
            {
                Restore();
                ct.ThrowIfCancellationRequested();
            }
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var u = SolveFrozen(f, uPrev, out string? why);
            double tSolve = clock.Elapsed.TotalSeconds;
            if (u == null || !u.All(double.IsFinite))
            {
                Log($"  итерация {it}: линейная задача не решена — {why ?? "вырожденная матрица или срыв Ньютона"}");
                break;
            }

            var shellTargets = new ShellTangent?[_shells.Length];
            var beamT = new double[_beams.Length][,];
            var beamShearT = new BeamShearStiffness?[_beams.Length];
            var (dK, r, worst) = EvaluateAll(u, shellTargets, beamT, beamShearT, shellStatus, beamStatus);   // dK — мера по работе
            double tEval = clock.Elapsed.TotalSeconds - tSolve;

            double du = Dense.Norm(Dense.SubV(u, uPrev)) / Math.Max(Dense.Norm(u), 1e-300);
            // Приращение после неполного обновления (ω < 1) — доля полного шага Пикара: в критерий — полный шаг ‖Δu‖/ω,
            // иначе малый ω сам по себе даёт малое ‖Δu‖ и ложную сходимость.
            bool converged = dK < _o.TolStiffness && (it == 1 || du / Math.Min(omegaApplied, 1.0) < _o.TolDisplacement);
            if (_o.TrueResidualEachIteration || converged) residual = TrueResidual(f, u);
            int cracked = shellStatus.Count(s => s.Cracked) + beamStatus.Count(s => s.Cracked);
            int yielded = shellStatus.Count(s => s.Yielded) + beamStatus.Count(s => s.Yielded);
            int failed = shellStatus.Count(s => s.Failed) + beamStatus.Count(s => s.Failed);

            // Новые трещины — скачок секущих, а не колебание: полное обновление (классический секущий метод; жёсткости
            // только падают, перемещения растут монотонно — перелёта, создающего ложные трещины, это не даёт) и сброс
            // истории Эйткена. Иначе Эйткен принимает каждый скачок за колебание и прижимает ω к нижней границе, а
            // лавина трещин идёт по кольцу КЭ за итерацию.
            int cracks = shellStatus.Sum(s => s.Cracks) + beamStatus.Sum(s => s.Cracks);
            int newCracks = cracksPrev >= 0 ? cracks - cracksPrev : 0;
            bool crackEvent = newCracks > 0;
            cracksPrev = cracks;
            double aitken = double.NaN;
            if (crackEvent)
            {
                omega = _o.OmegaMax;
                rPrev = null;
            }
            // ω по Эйткену (Айронс — Так). Невязка неподвижной точки — r = ε·(S_new − S)·ε по компонентам: приращения
            // перемещений для этого не годятся — в них уже сидит прошлый ω.
            else if (!converged && rPrev != null)
            {
                var diff = Dense.SubV(r, rPrev);
                double dd = Dense.Dot(diff, diff);
                double w = dd > 0.0 ? -omega * Dense.Dot(rPrev, diff) / dd : _o.Omega0;
                aitken = w;
                omega = double.IsFinite(w) ? Math.Clamp(w, _o.OmegaMin, _o.OmegaMax) : _o.Omega0;
            }
            double applied = converged ? 1.0 : omega;
            var record = new SecantIterationRecord(stage, step, lambda, it, applied, du, dK,
                _o.TrueResidualEachIteration || converged ? residual : double.NaN, cracked, yielded, failed);
            result.Iterations.Add(record);
            onIteration?.Invoke(record);
            Log($"  ст.{stage} шаг {step} λ={lambda:0.####} ит.{it,2}: ω={applied:0.###} ‖Δu‖/‖u‖={du:e2} " +
                $"ΔW={dK:e2}{(double.IsNaN(residual) ? "" : $" невязка={residual:e2}")} трещин {cracked}" +
                $"{(newCracks > 0 ? $" (+{newCracks} сл.)" : "")}{(double.IsNaN(aitken) ? "" : $" Эйткен {aitken:0.###}")}, " +
                $"текучесть {yielded}, отказ {failed}; max ΔW — {worst}; решение {tSolve:0.0} с{SolveDetail()}, сечения {tEval:0.0} с" +
                EvalDetail());

            Relax(shellTargets, beamT, beamShearT, applied);
            if (converged)
            {
                foreach (var s in _shells) s?.Commit();
                foreach (var b in _beams) b?.Commit();
                return new Attempt(true, it, residual, u, shellStatus, beamStatus);
            }
            if (it >= abortAt) { uPrev = u; break; }
            uPrev = u;
            omegaApplied = applied;
            if (!crackEvent) rPrev = r;
        }

        Restore();
        return new Attempt(false, _o.MaxIterations, double.IsNaN(residual) ? TrueResidual(f, uPrev) : residual,
            uPrev, shellStatus, beamStatus);
    }

    // Раскладка фазы «сечения»: оболочки (стена; сумма Evaluate по потокам), стержни (стена; Evaluate — сечения CScore).
    private EvalTimings? _lastEval;

    private sealed record EvalTimings(double ShellWall, double ShellEvalSum, double BeamWall, double BeamEval, int Threads);

    private string EvalDetail() => _lastEval is { } t
        ? $" (оболочки {t.ShellWall:0.0} с, Σ по потокам {t.ShellEvalSum:0.0} с на {t.Threads}; " +
          $"стержни {t.BeamWall:0.0} с, из них CScore {t.BeamEval:0.0} с)"
        : "";

    // Раскладка линейного решения по фазам (только линейный путь по постоянному портрету).
    private string SolveDetail() => !_o.Geometric && _mesh.LastSolveTimings is { } t
        ? $" (сборка {t.Assemble:0.0}, факторизация {t.Factorize:0.0}, прочее {t.Total - t.Assemble - t.Factorize:0.0})"
        : "";

    /// <summary>Решение с замороженными секущими: линейное или (геометрическая нелинейность) Ньютон от <paramref name="uFrom"/>.</summary>
    private double[]? SolveFrozen(double[] f, double[] uFrom, out string? why)
    {
        why = null;
        try
        {
            if (!_o.Geometric) return _mesh.SolveLinear(f, _bc);
            var (u, history) = _mesh.SolveNonlinear(f, _bc, nSteps: 1, tol: _o.GeometricTolerance, maxIter: 30,
                corotational: false, u0: uFrom, f0: f);
            if (!history.AllConverged()) why = "Ньютон геометрической нелинейности не сошёлся";
            return why == null ? u : null;
        }
        catch (InvalidOperationException ex)
        {
            why = ex.Message;
            return null;
        }
    }

    /// <summary>
    /// Пересчёт всех КЭ: целевые матрицы (с нижней границей диагонали) и состояние. Изменение жёсткости меряется работой,
    /// которую оно меняет: ΔW_e = Σᵢ|εᵢ·ΔFᵢ|, ΔF = (S_new − S)·ε при текущих деформациях КЭ (S_new·ε — истинные усилия
    /// закона), отнесённой к наибольшей по КЭ работе W_e = Σᵢ|εᵢ·Fᵢ|. Мера однородна по размерности (N·ε и M·κ — Дж/м²
    /// у оболочки, Дж/м у стержня), и в неё не входят направления с ε ≈ 0: там секущий модуль бетона без растяжения
    /// неоднозначен (0 или E₀ по знаку ε) и может «мигать» от итерации к итерации, не влияя на решение. Вектор εᵢ·ΔFᵢ —
    /// невязка неподвижной точки для Эйткена.
    /// </summary>
    private (double DF, double[] R, string Worst) EvaluateAll(double[] u, ShellTangent?[] shellTargets,
        double[][,] beamTargets, BeamShearStiffness?[] beamShearTargets, SecantSectionStatus[] shellStatus,
        SecantSectionStatus[] beamStatus)
    {
        const int shellBlock = 8, beamBlock = 11;   // (N, M, Q) оболочки; (N, M_y, M_z) в трёх точках стержня и (Q_y, Q_z)
        var r = new double[_shells.Length * shellBlock + _beams.Length * beamBlock];
        var work = new double[_shells.Length + _beams.Length];
        var change = new double[_shells.Length + _beams.Length];
        long shellEvalTicks = 0;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        Parallel.For(0, _shells.Length, new ParallelOptions { MaxDegreeOfParallelism = _o.MaxDegreeOfParallelism }, e =>
        {
            if (_shells[e] is not { } st) return;
            var (eps, kappa, gamma) = ShellStrains(e, u);
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            var ev = st.Evaluate(eps, kappa, gamma);
            Interlocked.Add(ref shellEvalTicks, System.Diagnostics.Stopwatch.GetTimestamp() - t0);
            var target = FloorShell(ev.Target, st.Initial);
            shellTargets[e] = target;
            shellStatus[e] = ev.Status;
            var e6 = new[] { eps[0], eps[1], eps[2], kappa[0], kappa[1], kappa[2] };
            var df = Dense.MatVec(Dense.Sub(Full6(target), Full6(st.Response.Matrix)), e6);
            var ft = Dense.MatVec(Full6(target), e6);
            var dq = Dense.MatVec(Dense.Sub(target.As, st.Response.Matrix.As), gamma);
            var fq = Dense.MatVec(target.As, gamma);
            double w = 0.0, c = 0.0;
            for (int i = 0; i < 6; i++)
            {
                r[e * shellBlock + i] = e6[i] * df[i];
                c += Math.Abs(e6[i] * df[i]);
                w += Math.Abs(e6[i] * ft[i]);
            }
            for (int i = 0; i < 2; i++)
            {
                r[e * shellBlock + 6 + i] = gamma[i] * dq[i];
                c += Math.Abs(gamma[i] * dq[i]);
                w += Math.Abs(gamma[i] * fq[i]);
            }
            work[e] = w;
            change[e] = c;
        });

        double shellWall = clock.Elapsed.TotalSeconds;
        long beamEvalTicks = 0;
        int offset = _shells.Length * shellBlock;
        for (int e = 0; e < _beams.Length; e++)
        {
            if (_beams[e] is not { } st) continue;
            var (strains, gy, gz) = BeamStrains(e, u, st.Response);
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            var ev = st.Evaluate(strains, gy, gz);
            beamEvalTicks += System.Diagnostics.Stopwatch.GetTimestamp() - t0;
            var target = FloorBeam(ev.Target, st.Initial);
            beamTargets[e] = target;
            beamStatus[e] = ev.Status;
            var dk = Dense.Sub(target, st.Response.Matrix);
            double w = 0.0, c = 0.0;
            for (int p = 0; p < 3; p++)
            {
                var ep = new[] { strains[p].Item1, strains[p].Item2, strains[p].Item3 };
                var df = Dense.MatVec(dk, ep);
                var ft = Dense.MatVec(target, ep);
                for (int i = 0; i < 3; i++)
                {
                    r[offset + e * beamBlock + 3 * p + i] = ep[i] * df[i];
                    c += Math.Abs(ep[i] * df[i]);
                    w += Math.Abs(ep[i] * ft[i]);
                }
            }
            // Сдвиг: γ·ΔQ, ΔQ = (GA_new − GA)·γ; в работу — с весом трёх сечений (γ постоянна по КЭ).
            var cur = st.Response.Shear;
            if (ev.ShearTarget is { } qt)
            {
                var sh = FloorShear(qt, st.InitialShear);
                beamShearTargets[e] = sh;
                double[] g = [gy, gz], gaNew = [sh.GAvY, sh.GAvZ], gaCur = [cur.GAvY, cur.GAvZ];
                for (int i = 0; i < 2; i++)
                {
                    if (!double.IsFinite(gaNew[i]) || !double.IsFinite(gaCur[i])) continue;
                    double dq = (gaNew[i] - gaCur[i]) * g[i];
                    r[offset + e * beamBlock + 9 + i] = 3.0 * g[i] * dq;
                    c += 3.0 * Math.Abs(g[i] * dq);
                    w += 3.0 * Math.Abs(g[i] * gaNew[i] * g[i]);
                }
            }
            // Работа стержня — на единицу длины (три сечения), оболочки — на единицу площади: сравниваются внутри типа.
            work[_shells.Length + e] = w / 3.0;
            change[_shells.Length + e] = c / 3.0;
        }
        double tick = 1.0 / System.Diagnostics.Stopwatch.Frequency;
        _lastEval = new EvalTimings(shellWall, shellEvalTicks * tick, clock.Elapsed.TotalSeconds - shellWall,
            beamEvalTicks * tick, _o.MaxDegreeOfParallelism > 0 ? _o.MaxDegreeOfParallelism : Environment.ProcessorCount);

        double wShell = 0.0, wBeam = 0.0;
        for (int e = 0; e < _shells.Length; e++) wShell = Math.Max(wShell, work[e]);
        for (int e = 0; e < _beams.Length; e++) wBeam = Math.Max(wBeam, work[_shells.Length + e]);
        double dF = 0.0;
        int worstElem = -1;
        for (int k = 0; k < work.Length; k++)
        {
            double scale = k < _shells.Length ? wShell : wBeam;
            if (!(scale > 0.0)) continue;
            double v = change[k] / scale;
            if (v > dF) { dF = v; worstElem = k; }
        }
        // Невязка Эйткена — в долях наибольшей работы своего типа КЭ.
        for (int i = 0; i < r.Length; i++)
        {
            double scale = i < offset ? wShell : wBeam;
            r[i] = scale > 0.0 ? r[i] / scale : 0.0;
        }
        if (worstElem < 0) return (0.0, r, "");

        string[] shellNames = { "Nx", "Ny", "Nxy", "Mx", "My", "Mxy", "Qx", "Qy" };
        string[] beamNames = { "N", "My", "Mz" };
        string where;
        if (worstElem < _shells.Length)
        {
            int b0 = worstElem * shellBlock, k = 0;
            for (int i = 1; i < shellBlock; i++) if (Math.Abs(r[b0 + i]) > Math.Abs(r[b0 + k])) k = i;
            where = $"оболочка {worstElem} {shellNames[k]}";
            if (_o.Log != null && shellTargets[worstElem] is { } wt)
            {
                var (eps, kappa, _) = ShellStrains(worstElem, u);
                var e6 = new[] { eps[0], eps[1], eps[2], kappa[0], kappa[1], kappa[2] };
                string V(double[] v) => string.Join(" ", v.Select(x => x.ToString("e2")));
                where += $" (ε,κ: {V(e6)}; S·ε: {V(Dense.MatVec(Full6(_shells[worstElem]!.Response.Matrix), e6))}; " +
                         $"S_new·ε: {V(Dense.MatVec(Full6(wt), e6))}; {shellStatus[worstElem]})";
            }
        }
        else
        {
            int e = worstElem - _shells.Length, b0 = offset + e * beamBlock, k = 0;
            for (int i = 1; i < beamBlock; i++) if (Math.Abs(r[b0 + i]) > Math.Abs(r[b0 + k])) k = i;
            where = k >= 9 ? $"стержень {e} {(k == 9 ? "Qy" : "Qz")}" : $"стержень {e} ξ={0.5 * (k / 3)} {beamNames[k % 3]}";
            if (_o.Log != null && k < 9 && beamTargets[e] is { } bt)
            {
                var (strains, _, _) = BeamStrains(e, u, _beams[e]!.Response);
                var s = strains[k / 3];
                var ep = new[] { s.Item1, s.Item2, s.Item3 };
                string V(double[] v) => string.Join(" ", v.Select(x => x.ToString("e2")));
                where += $" (ε,κ: {V(ep)}; S·ε: {V(Dense.MatVec(_beams[e]!.Response.Matrix, ep))}; " +
                         $"S_new·ε: {V(Dense.MatVec(bt, ep))}; {beamStatus[e]})";
            }
        }
        return (dF, r, where);
    }

    private void Relax(ShellTangent?[] shellTargets, double[][,] beamTargets, BeamShearStiffness?[] beamShearTargets,
        double omega)
    {
        for (int e = 0; e < _shells.Length; e++)
        {
            if (shellTargets[e] is not { } t) continue;
            var cur = _shells[e]!.Response.Matrix;
            _shells[e]!.Response.Update(new ShellTangent(Mix(cur.A, t.A, omega), Mix(cur.B, t.B, omega),
                Mix(cur.D, t.D, omega), Mix(cur.As, t.As, omega)));
        }
        for (int e = 0; e < _beams.Length; e++)
        {
            if (beamTargets[e] is { } t) _beams[e]!.Response.Update(Mix(_beams[e]!.Response.Matrix, t, omega));
            if (beamShearTargets[e] is { } q)
            {
                var cur = _beams[e]!.Response.Shear;
                _beams[e]!.Response.UpdateShear(new BeamShearStiffness(MixShear(cur.GAvY, q.GAvY, omega),
                    MixShear(cur.GAvZ, q.GAvZ, omega)));
            }
        }
    }

    // ---------------- деформации КЭ ----------------

    /// <summary>Деформации центра оболочки в осях сечения.</summary>
    private (double[] Eps, double[] Kappa, double[] Gamma) ShellStrains(int e, double[] u)
    {
        var dofs = StructuralMesh.NodeDofs(_mesh.Shells[e].Nodes);
        var (eps, kappa, gamma) = ShellElementForces.CenterStrainsGlobal(_mesh.ShellCoords(e), Gather(u, dofs), _o.Geometric);
        return _mesh.Shells[e].Section is RotatedShellResponse rot ? rot.ToSection(eps, kappa, gamma) : (eps, kappa, gamma);
    }

    /// <summary>
    /// Изгибные деформации стержня в точках Лобатто (с пузырём продольного перемещения по секущей) и углы сдвига КЭ —
    /// по разделению перемещений КЭ Тимошенко при текущих секущих <paramref name="r"/>.
    /// </summary>
    private ((double, double, double)[] Strains, double GammaY, double GammaZ) BeamStrains(int e, double[] u,
        SecantBeamResponse r)
    {
        var (d, l) = BeamLocal(e, u);
        // У шарнира узел поворачивается независимо от конца стержня: деформации — по перемещениям конца.
        int rel = _mesh.Beams[e].Releases;
        if (rel != 0) d = BeamReleases.Recover(BeamElements.Beam3dKLocal(r.Matrix, r.GJ, l, r.Shear), rel, d);
        var (bend, gy, gz) = BeamElements.Beam3dShearSplit(r.Matrix, r.GJ, l, r.Shear, d);
        return (new[] { 0.0, 0.5, 1.0 }.Select(xi => BeamElements.Beam3dCoupledStrains(r.Matrix, l, bend, xi)).ToArray(), gy, gz);
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
    /// Истинная невязка ‖F − F_int,true‖ (<see cref="StructuralMesh.RelativeResidual"/>): узловые моменты — делённые на
    /// средний размер КЭ, масштаб — наибольшее из ‖F‖ и ‖Σ|F_int,e|‖ (у плиты небаланс мембранных усилий и моментов
    /// несопоставим с вертикальной нагрузкой: её уравновешивает упругая поперечная сила). Оболочки — истинные законы
    /// в точках Гаусса 2 × 2 (кинематика линейная или фон Карман); стержни и КЭ без нелинейного закона — по своему
    /// сечению в сетке.
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
        var fAbs = new double[_mesh.NDof];
        for (int e = 0; e < _shells.Length; e++)
        {
            var dofs = StructuralMesh.NodeDofs(_mesh.Shells[e].Nodes);
            for (int i = 0; i < dofs.Length; i++)
            {
                fInt[dofs[i]] += parts[e][i];
                fAbs[dofs[i]] += Math.Abs(parts[e][i]);
            }
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
                ? BeamCorotational.Beam3dInternalForce(coords, b.Section, ue, b.RefVec, b.Releases)
                : Dense.MatVec(BeamElements.Beam3dKGlobal(coords, b.Section, b.RefVec, b.Releases), ue);
            for (int i = 0; i < dofs.Length; i++)
            {
                fInt[dofs[i]] += fe[i];
                fAbs[dofs[i]] += Math.Abs(fe[i]);
            }
        }
        return _mesh.RelativeResidual(f, fInt, u, _bc, _momentArm, fAbs);
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

    private BeamShearStiffness FloorShear(BeamShearStiffness s, BeamShearStiffness init)
    {
        double k = _o.StiffnessFloor;
        return k <= 0.0 ? s : new BeamShearStiffness(Math.Max(s.GAvY, k * init.GAvY), Math.Max(s.GAvZ, k * init.GAvZ));
    }

    // Бесконечная жёсткость (без сдвига) смешивается только сама с собой.
    private static double MixShear(double cur, double target, double w)
        => double.IsFinite(cur) && double.IsFinite(target) ? cur + w * (target - cur) : target;

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
