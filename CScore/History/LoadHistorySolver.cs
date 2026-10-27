namespace CScore.History;

/// <summary>
/// Решатель истории нагружения стадийного сечения. Этапы <see cref="StageKind.Load"/> — управление по
/// нагрузке с тёплым стартом от предыдущего шага того же пути; несошедшийся шаг останавливает путь.
/// Без <see cref="LoadHistoryOptions.Hysteresis"/> материал нелинейно-упругий: разгрузка идёт по кривой нагружения.
/// С гистерезисом состояния волокон фиксируются после каждого принятого шага (пробные решения — от последнего
/// зафиксированного состояния).
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
        ApplyFailsRoles();
        if (_o.Hysteresis is not null)
            _section.EnableHysteresis(_o.Hysteresis, _o.Calc, RebarRupture, _o.Failure.SofteningStrain);

        var solver = new StrainSolver(_section, _o.Calc, ten: _o.Ten, tol: _o.Tol,
            maxIter: _o.MaxIter, h: _o.H, centralJacobian: _o.CentralJacobian);
        var k = new Kurvature();
        var fPrev = new Load();

        for (int si = 0; si < stages.Count; si++)
        {
            var st = stages[si];
            if (st.Kind == StageKind.Failure)
            {
                RunFailure(run, si, phi, fPrev.N, k);
                run.ActivationPlanes = _section.ActivationPlanes.ToArray();
                run.MPeak = run.Steps.Max(s => Math.Abs(s.Mu));
                return run;
            }

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
                bool ok = solver.Converged;
                int it = solver.Iterations;
                double res = solver.Residual;
                bool tenStep = _o.Ten;
                string? fallback = null;
                if (!ok)
                {
                    fallback = _o.Fallback == LoadStepFallback.WithoutConcreteTension
                        ? "without_concrete_tension" : "curvature_control";
                    if (TryFallback(target, k, first, out var kf, out it, out res, out tenStep))
                    {
                        kNew = kf;
                        ok = true;
                    }
                }
                var step = MakeStep(si, st.Kind, i, target, kNew, phi, ok, it, res, ten: tenStep);
                if (fallback is not null) step.Warnings.Add(fallback);
                run.Steps.Add(step);
                Tick();
                if (!ok)
                {
                    run.Status = "partial";
                    run.StopReason = "load_not_converged";
                    run.ActivationPlanes = _section.ActivationPlanes.ToArray();
                    return run;
                }
                _section.CommitState(kNew, tenStep);
                k = kNew;
            }
            fPrev = st.Target;
        }

        run.ActivationPlanes = _section.ActivationPlanes.ToArray();
        run.MPeak = run.Steps.Count > 0 ? run.Steps.Max(s => Math.Abs(s.Mu)) : null;
        return run;
    }

    HistoryStep MakeStep(int stageIndex, StageKind kind, int step, Load target, Kurvature k, double phi,
        bool converged, int iterations, double residual, bool isLimitPoint = false, bool? ten = null)
    {
        var f = _section.Integral(k, _o.Calc, ten ?? _o.Ten);
        double cos = Math.Cos(phi), sin = Math.Sin(phi);
        var parts = Enumerable.Range(0, _section.ActivePartCount).Select(p =>
        {
            var u = PartUtilization.Evaluate(_section, p, k, _o.Calc);
            if (!_section.HysteresisEnabled) return u;
            var (rc, rs) = _section.ResidualStrains(p, k, ten ?? _o.Ten);
            return new PartStepState
            {
                PartIndex = u.PartIndex, ConcreteEpsMin = u.ConcreteEpsMin, ConcreteEpsMax = u.ConcreteEpsMax,
                RebarEpsMin = u.RebarEpsMin, RebarEpsMax = u.RebarEpsMax, EtaConcrete = u.EtaConcrete,
                EtaRebar = u.EtaRebar, ConcreteMinPoint = u.ConcreteMinPoint, RebarMaxPoint = u.RebarMaxPoint,
                ConcreteResidual = rc, RebarResidual = rs,
            };
        }).ToList();
        var warnings = new List<string>();
        // При гистерезисе отказ необратим — это свойство модели, а не оговорка.
        if (kind == StageKind.Load && !_section.HysteresisEnabled && parts.Any(p => Exceeds(p, PartLimitRole.Fails)))
            warnings.Add("fails_beyond_limit");
        return new HistoryStep
        {
            StageIndex = stageIndex, Kind = kind, Step = step,
            ActivePartCount = _section.ActivePartCount,
            Target = target, Result = f, K = k,
            C = k.ky * cos + k.kz * sin,
            Mu = f.Mx * cos + f.My * sin,
            Converged = converged, Iterations = iterations, Residual = residual,
            IsLimitPoint = isLimitPoint,
            Warnings = warnings,
            Parts = parts,
        };
    }

    /// <summary>
    /// Продолжение несошедшегося шага по нагрузке способом <see cref="LoadHistoryOptions.Fallback"/>.
    /// <paramref name="ten"/> — учёт растяжения бетона, при котором получено решение.
    /// </summary>
    bool TryFallback(Load target, Kurvature kPrev, bool first,
        out Kurvature k, out int iterations, out double residual, out bool ten)
    {
        ten = _o.Ten;
        if (_o.Fallback == LoadStepFallback.WithoutConcreteTension)
        {
            k = kPrev; iterations = 0; residual = double.NaN;
            if (!_o.Ten) return false;
            var s = new StrainSolver(_section, _o.Calc, ten: false, tol: _o.Tol,
                maxIter: _o.MaxIter, h: _o.H, centralJacobian: _o.CentralJacobian);
            k = s.Solve(target.N, target.Mx, target.My, first ? null : kPrev);
            iterations = s.Iterations;
            residual = s.Residual;
            ten = false;
            return s.Converged;
        }
        return TryCurvatureBridge(target, kPrev, out k, out iterations, out residual);
    }

    /// <summary>
    /// Проход несошедшегося шага по кривизне: при N шага и направлении его момента u кривизна вдоль u
    /// шагами c_max/100 (с дроблением) меняется от текущей до пересечения проекции момента с заданной,
    /// затем бисекция по c. Так проходится провал момента после трещинообразования. Промежуточные точки
    /// в историю не пишутся. Нет пересечения до ±c_max — шаг не сошёлся.
    /// </summary>
    bool TryCurvatureBridge(Load target, Kurvature kPrev, out Kurvature k, out int iterations, out double residual)
    {
        k = kPrev; iterations = 0; residual = double.NaN;
        int its = 0; // счётчик итераций (out-параметр недоступен локальной функции)
        double mTarget = Math.Sqrt(target.Mx * target.Mx + target.My * target.My);
        if (mTarget < 1e-12) return false;
        double phiT = Math.Atan2(target.My, target.Mx);
        double cos = Math.Cos(phiT), sin = Math.Sin(phiT);
        var ctrl = new CurvatureControlSolver(_section, _o);

        double Mu(Kurvature kk)
        {
            var f = _section.Integral(kk, _o.Calc, _o.Ten);
            return f.Mx * cos + f.My * sin;
        }

        double c = kPrev.ky * cos + kPrev.kz * sin;
        if (!ctrl.TrySolve(target.N, phiT, c, kPrev, out var kc, out int it, out _)) return false;
        its += it;
        double mu = Mu(kc);
        double dir = mTarget > mu ? 1 : -1;
        double cLimit = EstimateCMax(phiT);
        double dc = cLimit / 100;

        // После трещин при учёте растяжения бетона M(c) пилообразна, и в отдельных точках по c равновесия
        // нет (N перескакивает через ноль). Такие точки перешагиваются: сначала шаг дробится, затем — удлиняется.
        double[] stepFactors = [1, 0.5, 0.25, 0.125, 0.0625, 0.03125, 1.5, 2, 3, 4];

        for (int n = 0; n < 400; n++)
        {
            _o.Token.ThrowIfCancellationRequested();
            double step = dc;
            Kurvature kNew = default; bool ok = false;
            foreach (double factor in stepFactors)
            {
                step = dc * factor;
                if (ok = ctrl.TrySolve(target.N, phiT, c + dir * step, kc, out kNew, out it, out _)) break;
            }
            if (!ok) return false;
            its += it;
            double cNew = c + dir * step;
            double muNew = Mu(kNew);

            if ((muNew - mTarget) * (mu - mTarget) <= 0
                && TryBisect(c, mu, kc, cNew, muNew, kNew, out var kHit))
            {
                k = kHit;
                var f = _section.Integral(k, _o.Calc, _o.Ten);
                double rN = f.N - target.N, rX = f.Mx - target.Mx, rY = f.My - target.My;
                residual = Math.Sqrt(rN * rN + rX * rX + rY * rY);
                iterations = its;
                return true;
            }
            // Пересечения нет или оно на скачке M (бисекция не дала равновесия) — ищем дальше.

            c = cNew; kc = kNew; mu = muNew;
            if (Math.Abs(c) > cLimit) return false;
        }
        return false;

        // Бисекция по c между (cLo, muLo) и (cHi, muHi); точку без равновесия обходит, сдвигая середину.
        bool TryBisect(double cLo, double muLo, Kurvature kLo, double cHi, double muHi, Kurvature kHi, out Kurvature kHit)
        {
            kHit = kHi;
            double fLo = muLo - mTarget, fMid = muHi - mTarget;
            for (int b = 0; b < 60 && Math.Abs(fMid) >= 0.5 * _o.Tol; b++)
            {
                bool solved = false;
                Kurvature kMid = default;
                double cMid = cLo;
                foreach (double w in (double[])[0.5, 0.25, 0.75])
                {
                    cMid = cLo + w * (cHi - cLo);
                    if (solved = ctrl.TrySolve(target.N, phiT, cMid, kLo, out kMid, out it, out _)) break;
                }
                if (!solved) return false;
                its += it;
                fMid = Mu(kMid) - mTarget;
                kHit = kMid;
                if (fMid * fLo > 0) { cLo = cMid; kLo = kMid; fLo = fMid; } else cHi = cMid;
            }
            var f = _section.Integral(kHit, _o.Calc, _o.Ten);
            double rN = f.N - target.N, rX = f.Mx - target.Mx, rY = f.My - target.My;
            return Math.Sqrt(rN * rN + rX * rX + rY * rY) < 2 * _o.Tol;
        }
    }

    /// <summary>Оборачивает диаграммы областей с ролью «Разрушается» в <see cref="SofteningDiagramm"/>.
    /// Словарь диаграмм заменяется целиком: у клонов CloneForCalc он общий с оригиналом.
    /// При гистерезисе арматура не оборачивается: её отказ — <see cref="Hysteresis.RuptureDamage"/> в модели стали.</summary>
    void ApplyFailsRoles()
    {
        foreach (var part in _section.Parts)
        {
            var roles = _o.RoleOf(part.Index);
            foreach (var area in part.Areas)
            {
                if (!MaterialArea.IsCalcActive(area) || !area.Diagramms.TryGetValue(_o.Calc, out var d)) continue;
                if (d is SofteningDiagramm) continue;
                bool concrete = LimitSurface.StrainCriterionGeometry.IsConcrete(area);
                var role = concrete ? roles.Concrete : roles.Rebar;
                if (role != PartLimitRole.Fails) continue;
                if (!concrete && _o.Hysteresis is not null) continue;
                double limit = concrete
                    ? PartUtilization.LimitConcrete(area, _o.Calc)
                    : LimitSurface.UlsStrainCriterion.RebarEpsSu(area, _o.Calc);
                area.Diagramms = new Dictionary<CalcType, Diagramm>(area.Diagramms)
                {
                    [_o.Calc] = new SofteningDiagramm(d, limit, _o.Failure.SofteningStrain),
                };
            }
        }
    }

    /// <summary>εs2 арматуры части с ролью «Разрушается» (отказ в модели стали); иначе null.</summary>
    double? RebarRupture(MaterialArea area)
    {
        var part = _section.Parts.FirstOrDefault(p => p.Areas.Contains(area));
        if (part is null || LimitSurface.StrainCriterionGeometry.IsConcrete(area)) return null;
        return _o.RoleOf(part.Index).Rebar == PartLimitRole.Fails
            ? LimitSurface.UlsStrainCriterion.RebarEpsSu(area, _o.Calc) : null;
    }

    /// <summary>Шаг пути для воспроизведения: плоскость, число включённых частей, учёт растяжения бетона.</summary>
    public sealed record ReplayStep(Kurvature K, int Parts, bool Ten);

    /// <summary>
    /// Копия источника с состояниями волокон после шагов 0…upTo пути (без Ньютона, те же обёртки ролей и тот же
    /// порядок фиксаций, что у решателя). Без гистерезиса — копия с включёнными частями.
    /// Часть включается перед первым шагом с бо́льшим числом частей при своей плоскости включения — как в решателе.
    /// </summary>
    public static StagedSection Replay(StagedSection source, LoadHistoryOptions o, IReadOnlyList<ReplayStep> steps,
        IReadOnlyList<Kurvature> activationPlanes, bool withHistory, int upTo)
    {
        var s = (StagedSection)source.CloneForCalc();
        var solver = new LoadHistorySolver(s, o);
        if (withHistory) s.ResetHistory(); else s.ActivateAll();
        solver.ApplyFailsRoles();
        if (o.Hysteresis is not null)
            s.EnableHysteresis(o.Hysteresis, o.Calc, solver.RebarRupture, o.Failure.SofteningStrain);
        for (int j = 0; j <= upTo && j < steps.Count; j++)
        {
            while (withHistory && s.ActivePartCount < steps[j].Parts)
                s.ActivatePart(s.ActivePartCount, activationPlanes[s.ActivePartCount]);
            s.CommitState(steps[j].K, steps[j].Ten);
        }
        return s;
    }

    bool Exceeds(PartStepState p, PartLimitRole role)
    {
        var r = _o.RoleOf(p.PartIndex);
        return (r.Concrete == role && p.EtaConcrete >= 1) || (r.Rebar == role && p.EtaRebar >= 1);
    }

    /// <summary>Наибольшее использование по частям с ролью «Ограничивает» и его источник.</summary>
    (double eta, int part, string material, (double X, double Y) point) Governing(IReadOnlyList<PartStepState> parts)
    {
        (double, int, string, (double, double)) best = (0, -1, "", default);
        foreach (var p in parts)
        {
            var r = _o.RoleOf(p.PartIndex);
            if (r.Concrete == PartLimitRole.Limits && p.EtaConcrete > best.Item1)
                best = (p.EtaConcrete, p.PartIndex, "concrete", p.ConcreteMinPoint);
            if (r.Rebar == PartLimitRole.Limits && p.EtaRebar > best.Item1)
                best = (p.EtaRebar, p.PartIndex, "rebar", p.RebarMaxPoint);
        }
        return best;
    }

    /// <summary>
    /// Этап разрушения: шаги по проекции кривизны c от конца предыдущего этапа до c_max при N = N₀;
    /// остановка по падению момента, по c_max или по потере сходимости. Переход использования через 1
    /// уточняется бисекцией (точка M_lim).
    /// </summary>
    void RunFailure(LoadHistoryRun run, int stageIndex, double phi, double n0, Kurvature kStart)
    {
        var ctrl = new CurvatureControlSolver(_section, _o);
        double cos = Math.Cos(phi), sin = Math.Sin(phi);
        double c0 = kStart.ky * cos + kStart.kz * sin;
        double cMax = _o.Failure.CMax ?? EstimateCMax(phi);
        if (cMax <= c0) throw new InvalidOperationException(
            $"Предельная кривизна {cMax:G4} не больше текущей {c0:G4}: этап разрушения не выполнить.");
        double dc = (cMax - c0) / _o.Failure.Steps;

        var k = kStart;
        double c = c0, mPeak = run.Steps.Count > 0 ? run.Steps.Max(s => Math.Abs(s.Mu)) : 0;
        double prevEta = Governing(run.Steps.Count > 0 ? run.Steps[^1].Parts : []).eta;
        int stepNo = 0;

        while (c < cMax - 1e-15)
        {
            _o.Token.ThrowIfCancellationRequested();
            double step = Math.Min(dc, cMax - c);
            Kurvature kNew = default; int it = 0; double res = 0; bool ok = false;
            for (int halving = 0; halving <= 5 && !ok; halving++, step /= 2)
                ok = ctrl.TrySolve(n0, phi, c + step, k, out kNew, out it, out res);
            if (!ok) { run.StopReason = "failure_not_converged"; return; }
            step *= 2; // последнее деление не применялось

            double cNew = c + step;
            var st = MakeStep(stageIndex, StageKind.Failure, stepNo + 1, new Load { N = n0 }, kNew, phi, true, it, res);
            var gov = Governing(st.Parts);

            if (run.MLim is null && prevEta < 1 && gov.eta >= 1)
            {
                // Точка предела лежит перед шагом — она и получает очередной номер.
                RefineLimit(run, ctrl, stageIndex, phi, n0, c, k, cNew, ref stepNo);
                st = MakeStep(stageIndex, StageKind.Failure, stepNo + 1, new Load { N = n0 }, kNew, phi, true, it, res);
            }
            stepNo++;

            run.Steps.Add(st);
            Tick();
            _section.CommitState(kNew, _o.Ten);
            mPeak = Math.Max(mPeak, Math.Abs(st.Mu));
            c = cNew; k = kNew; prevEta = gov.eta;

            if (Math.Abs(st.Mu) < _o.Failure.DropRatio * mPeak) { run.StopReason = "drop"; return; }
        }
        run.StopReason = "c_max";
    }

    /// <summary>Бисекция по c между последней точкой ниже предела и первой за пределом (отн. точность 1e-4).</summary>
    void RefineLimit(LoadHistoryRun run, CurvatureControlSolver ctrl, int stageIndex, double phi, double n0,
        double cLo, Kurvature kLo, double cHi, ref int stepNo)
    {
        Kurvature kMid = kLo; int it = 0; double res = 0;
        for (int i = 0; i < 60 && Math.Abs(cHi - cLo) > 1e-4 * Math.Abs(cHi); i++)
        {
            double cMid = 0.5 * (cLo + cHi);
            if (!ctrl.TrySolve(n0, phi, cMid, kLo, out kMid, out it, out res)) break;
            var parts = Enumerable.Range(0, _section.ActivePartCount)
                .Select(p => PartUtilization.Evaluate(_section, p, kMid, _o.Calc)).ToList();
            if (Governing(parts).eta >= 1) cHi = cMid; else { cLo = cMid; kLo = kMid; }
        }
        ctrl.TrySolve(n0, phi, cHi, kLo, out var kLim, out it, out res);
        var st = MakeStep(stageIndex, StageKind.Failure, ++stepNo, new Load { N = n0 }, kLim, phi, true, it, res, isLimitPoint: true);
        var gov = Governing(st.Parts);
        run.Steps.Add(st);
        _section.CommitState(kLim, _o.Ten);
        run.MLim = Math.Abs(st.Mu);
        run.GoverningPart = gov.part;
        run.GoverningMaterial = gov.material;
        run.GoverningPoint = gov.point;
    }

    /// <summary>c_max = 1,5·(|εb2| + εs2)/h_u по включённым частям; пределы — по частям с ролью «Ограничивает» (если их нет — по всем).</summary>
    double EstimateCMax(double phi)
    {
        double cos = Math.Cos(phi), sin = Math.Sin(phi);
        double lo = double.PositiveInfinity, hi = double.NegativeInfinity, eb2 = 0, es2 = 0;
        bool anyLimits = Enumerable.Range(0, _section.ActivePartCount).Any(p =>
            _o.RoleOf(p).Concrete == PartLimitRole.Limits || _o.RoleOf(p).Rebar == PartLimitRole.Limits);
        for (int p = 0; p < _section.ActivePartCount; p++)
        {
            var roles = _o.RoleOf(p);
            foreach (var area in _section.Parts[p].Areas.Where(MaterialArea.IsCalcActive))
            {
                bool concrete = LimitSurface.StrainCriterionGeometry.IsConcrete(area);
                if (concrete)
                {
                    foreach (var cont in LimitSurface.StrainCriterionGeometry.Contours(area))
                        foreach (var (x, y) in LimitSurface.StrainCriterionGeometry.OpenVertices(cont))
                        { double u = y * cos + x * sin; lo = Math.Min(lo, u); hi = Math.Max(hi, u); }
                    if (!anyLimits || roles.Concrete == PartLimitRole.Limits)
                        eb2 = Math.Max(eb2, Math.Abs(PartUtilization.LimitConcrete(area, _o.Calc)));
                }
                else
                {
                    foreach (var f in area.Fibers.Where(f => f.TypeFiber == FiberType.point))
                    { double u = f.Y * cos + f.X * sin; lo = Math.Min(lo, u); hi = Math.Max(hi, u); }
                    if (!anyLimits || roles.Rebar == PartLimitRole.Limits)
                        es2 = Math.Max(es2, LimitSurface.UlsStrainCriterion.RebarEpsSu(area, _o.Calc));
                }
            }
        }
        double h = hi - lo;
        if (!(h > 0)) throw new InvalidOperationException("Не удалось определить размер сечения вдоль направления изгиба.");
        return 1.5 * (eb2 + es2) / h;
    }

    void Tick()
    {
        _doneSteps++;
        _o.Progress?.Invoke(_totalSteps > 0 ? Math.Min(1.0, (double)_doneSteps / _totalSteps) : 1.0);
    }
}
