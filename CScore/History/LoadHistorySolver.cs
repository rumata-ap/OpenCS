namespace CScore.History;

/// <summary>
/// Решатель истории нагружения стадийного сечения. Этапы <see cref="StageKind.Load"/> — управление по
/// нагрузке с тёплым стартом от предыдущего шага того же пути; несошедшийся шаг останавливает путь.
/// Материал нелинейно-упругий: разгрузка идёт по кривой нагружения.
/// </summary>
public sealed class LoadHistorySolver
{
    readonly StagedSection _section;
    readonly LoadHistoryOptions _o;
    int _totalSteps, _doneSteps;

    public LoadHistorySolver(StagedSection section, LoadHistoryOptions options)
    {
        _section = section;
        _o = options;
    }

    /// <summary>Проверка этапов: части по порядку и по одному разу, разрушение только последним, шагов ≥ 1.</summary>
    public static void Validate(IReadOnlyList<HistoryStage> stages, int partCount)
    {
        if (stages.Count == 0) throw new ArgumentException("История не содержит этапов.");
        int next = 1;
        for (int i = 0; i < stages.Count; i++)
        {
            var st = stages[i];
            if (st.Steps < 1) throw new ArgumentException($"Этап «{st.Tag}»: число шагов должно быть не меньше 1.");
            if (st.Kind == StageKind.Failure && i != stages.Count - 1)
                throw new ArgumentException($"Этап «{st.Tag}»: доведение до разрушения допускается только последним этапом.");
            if (st.ActivatePart is int p)
            {
                if (p != next || p >= partCount)
                    throw new ArgumentException($"Этап «{st.Tag}»: часть {p} нельзя включить (ожидается часть {next} из {partCount - 1}).");
                next++;
            }
        }
    }

    public LoadHistoryResult Run(IReadOnlyList<HistoryStage> stages, bool compareWithoutHistory)
    {
        Validate(stages, _section.Parts.Count);
        double phi = ResolvePhi(stages);
        _totalSteps = stages.Sum(s => s.Kind == StageKind.Load ? s.Steps : _o.Failure.Steps) * (compareWithoutHistory ? 2 : 1);
        _doneSteps = 0;

        var with = RunPath(stages, phi, withHistory: true);
        LoadHistoryRun? without = compareWithoutHistory ? RunPath(stages, phi, withHistory: false) : null;
        return new LoadHistoryResult { WithHistory = with, WithoutHistory = without, Phi = phi };
    }

    static double ResolvePhi(IReadOnlyList<HistoryStage> stages)
    {
        static bool NonZero(Load l) => Math.Abs(l.Mx) + Math.Abs(l.My) > 1e-12;
        var fail = stages.FirstOrDefault(s => s.Kind == StageKind.Failure);
        if (fail is not null && NonZero(fail.Target)) return Math.Atan2(fail.Target.My, fail.Target.Mx);
        var last = stages.LastOrDefault(s => s.Kind == StageKind.Load && NonZero(s.Target));
        return last is null ? 0.0 : Math.Atan2(last.Target.My, last.Target.Mx);
    }

    LoadHistoryRun RunPath(IReadOnlyList<HistoryStage> stages, double phi, bool withHistory)
    {
        var run = new LoadHistoryRun();
        if (withHistory) _section.ResetHistory(); else _section.ActivateAll();

        var solver = new StrainSolver(_section, _o.Calc, ten: _o.Ten, tol: _o.Tol,
            maxIter: _o.MaxIter, h: _o.H, centralJacobian: _o.CentralJacobian);
        var k = new Kurvature();
        var fPrev = new Load();

        for (int si = 0; si < stages.Count; si++)
        {
            var st = stages[si];
            if (st.Kind == StageKind.Failure)
                throw new NotSupportedException("Этап разрушения реализуется в срезе 2.");

            if (withHistory && st.ActivatePart is int part)
                _section.ActivatePart(part, k);

            for (int i = 1; i <= st.Steps; i++)
            {
                _o.Token.ThrowIfCancellationRequested();
                double t = (double)i / st.Steps;
                var target = new Load
                {
                    N = fPrev.N + (st.Target.N - fPrev.N) * t,
                    Mx = fPrev.Mx + (st.Target.Mx - fPrev.Mx) * t,
                    My = fPrev.My + (st.Target.My - fPrev.My) * t,
                };
                bool first = run.Steps.Count == 0;
                var kNew = solver.Solve(target.N, target.Mx, target.My, first ? null : k);
                var step = MakeStep(si, st.Kind, i, target, kNew, phi, solver.Converged, solver.Iterations, solver.Residual);
                run.Steps.Add(step);
                Tick();
                if (!solver.Converged)
                {
                    run.Status = "partial";
                    run.StopReason = "load_not_converged";
                    run.ActivationPlanes = _section.ActivationPlanes.ToArray();
                    return run;
                }
                k = kNew;
            }
            fPrev = st.Target;
        }

        run.ActivationPlanes = _section.ActivationPlanes.ToArray();
        run.MPeak = run.Steps.Count > 0 ? run.Steps.Max(s => Math.Abs(s.Mu)) : null;
        return run;
    }

    HistoryStep MakeStep(int stageIndex, StageKind kind, int step, Load target, Kurvature k, double phi,
        bool converged, int iterations, double residual, bool isLimitPoint = false)
    {
        var f = _section.Integral(k, _o.Calc, _o.Ten);
        double cos = Math.Cos(phi), sin = Math.Sin(phi);
        var parts = Enumerable.Range(0, _section.ActivePartCount)
            .Select(p => PartUtilization.Evaluate(_section, p, k, _o.Calc)).ToList();
        return new HistoryStep
        {
            StageIndex = stageIndex, Kind = kind, Step = step,
            ActivePartCount = _section.ActivePartCount,
            Target = target, Result = f, K = k,
            C = k.ky * cos + k.kz * sin,
            Mu = f.Mx * cos + f.My * sin,
            Converged = converged, Iterations = iterations, Residual = residual,
            IsLimitPoint = isLimitPoint,
            Parts = parts,
        };
    }

    void Tick()
    {
        _doneSteps++;
        _o.Progress?.Invoke(_totalSteps > 0 ? Math.Min(1.0, (double)_doneSteps / _totalSteps) : 1.0);
    }
}
