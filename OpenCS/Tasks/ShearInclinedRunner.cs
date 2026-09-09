using System.Text.Json;
using CScore;
using CScore.Sp63Shear;
using OpenCS.Utilites;

namespace OpenCS.Tasks;

/// <summary>Результат расчёта одной плоскости сдвига со всеми исходными данными.</summary>
/// <param name="Plane">Плоскость сдвига.</param>
/// <param name="Result">Проверки и стоянки.</param>
/// <param name="Input">Применённые расчётные данные.</param>
/// <param name="Geometry">Автоматически определённая геометрия обеих растянутых граней.</param>
/// <param name="Profile">Профиль усилий, по которому вёлся расчёт.</param>
public sealed record ShearInclinedPlaneOutcome(
    ShearPlane Plane, ShearInclinedResult Result,
    ShearInclinedInput Input, InclinedSectionGeometryPair Geometry, IForceProfile Profile);

/// <summary>
/// Выполняет расчёт наклонных сечений по СП 63.13330 для одной строки усилий
/// и формирует JSON-результат задачи.
/// </summary>
public static class ShearInclinedRunner
{
    /// <summary>Выполняет расчёт и возвращает CalcResult; исключения кодируются статусом error.</summary>
    public static CalcResult Run(
        CalcTask task, CrossSection section, LoadItem item,
        CalcSettings settings, TaskRunContext? ctx)
    {
        string created = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        try
        {
            var parameters = ShearInclinedParams.Parse(task.ParamsJson);
            if (!section.Areas.Any(area => area.Category == AreaCategory.Region &&
                                           area.Material?.Type == MatType.Concrete))
                throw new InvalidOperationException(
                    "В сечении нет бетонной области — расчёт наклонных сечений невозможен.");
            var outcomes = new List<ShearInclinedPlaneOutcome>();
            var warnings = new List<string>();
            var forceSet = ctx?.Database?.ForceSets.FirstOrDefault(fs => fs.Id == task.ForceSetId);

            var planes = Planes(parameters)
                .Where(plane => Math.Abs(ShearOf(item, plane)) > Sp63ShearApplicability.ForceZeroTolerance)
                .ToList();
            if (planes.Count == 0) planes = Planes(parameters).ToList();

            foreach (var plane in planes)
            {
                var applicability = Qualify(section, item, plane, parameters, forceSet, ctx?.Database);
                if (!applicability.HasNormativeVerdict &&
                    applicability.EffectiveMode != Sp63ShearApplicabilityMode.Research)
                    return NotApplicable(task, section, item, parameters, created, applicability);

                var outcome = Evaluate(task, section, item, plane, parameters, settings, ctx, warnings);
                if (outcome is not null) outcomes.Add(outcome);
            }

            if (outcomes.Count == 0)
                return Error(task, created, "Ни одна плоскость сдвига не рассчитана.");

            if (parameters.ConstructiveRequirements103Confirmed)
                warnings.Add(
                    "Конструктивные требования раздела 10.3 подтверждены пользователем — "
                    + "поперечная арматура учтена в расчёте.");
            warnings.Add(
                "Анкеровка по 10.3.21–10.3.28 не проверена; коэффициент включения арматуры "
                + $"k = {parameters.ResolveAnchorageFactor(settings):F2} задан пользователем.");

            return new CalcResult
            {
                TaskId = task.Id,
                TaskKind = task.Kind,
                TaskTag = task.Tag,
                Created = created,
                Status = "ok",
                DataJson = Serialize(task, section, item, parameters, outcomes, warnings)
            };
        }
        catch (Exception ex)
        {
            return Error(task, created, ex.Message);
        }
    }

    /// <summary>Рассчитывает одну плоскость сдвига.</summary>
    public static ShearInclinedPlaneOutcome? Evaluate(
        CalcTask task, CrossSection section, LoadItem item, ShearPlane plane,
        ShearInclinedParams parameters, CalcSettings settings,
        TaskRunContext? ctx, List<string> warnings)
    {
        double pairedMoment = plane == ShearPlane.Vy ? item.Mx : item.My;
        var geometry = InclinedSectionGeometryPair.Resolve(section, plane, task.CalcType);
        var defaults = geometry.For(pairedMoment);
        var stirrups = StirrupResolver.Resolve(section, plane, task.CalcType);
        warnings.AddRange(stirrups.Warnings);

        var overrides = plane == ShearPlane.Vy ? parameters.OverridesVy : parameters.OverridesVx;

        // Ручное Rsw пересчитывает qsw пропорционально, если сам qsw не задан явно.
        double qsw = stirrups.Qsw;
        if (overrides?.Rsw is double requestedRsw && stirrups.Rsw > 0.0)
        {
            double manualRsw = StirrupResolver.LimitRsw(requestedRsw);
            if (Math.Abs(requestedRsw) > StirrupResolver.MaxRsw)
                AddOnce(warnings,
                    $"Заданное Rsw = {Math.Abs(requestedRsw) / 1000.0:G} МПа превышает "
                    + $"максимум {StirrupResolver.MaxRsw / 1000.0:G} МПа по табл. 6.15 — "
                    + $"принято {manualRsw / 1000.0:G} МПа.");
            qsw *= manualRsw / stirrups.Rsw;
        }
        qsw = overrides?.Qsw ?? qsw;

        // П. 8.1.33 учитывает поперечную арматуру только при соблюдении требований 10.3.
        // Пока пользователь их не подтвердил, хомуты не повышают несущую способность.
        if (!parameters.ConstructiveRequirements103Confirmed && qsw > 0.0)
        {
            qsw = 0.0;
            AddOnce(warnings,
                "Конструктивные требования раздела 10.3 не подтверждены — поперечная арматура "
                + "в расчёте не учтена (qsw = 0). Подтвердите требования в параметрах задачи, "
                + "чтобы включить хомуты в расчёт.");
        }

        var input = new ShearInclinedInput(
            B: overrides?.B ?? defaults.B,
            H0: overrides?.H0 ?? defaults.H0,
            Rb: overrides?.Rb ?? defaults.Rb,
            Rbt: overrides?.Rbt ?? defaults.Rbt,
            Qsw: qsw,
            Sw: stirrups.Sw,
            Ns: overrides?.Ns ?? defaults.Ns,
            Kind: parameters.ResolveElementKind(),
            AnchorageFactor: parameters.ResolveAnchorageFactor(settings),
            StationStep: parameters.ResolveStationStep(settings),
            ProjectionStep: parameters.ResolveProjectionStep(settings),
            MomentZoneLength: parameters.ResolveMomentZoneLength(settings),
            BarCutoffs: parameters.BarCutoffs,
            CheckMoment: parameters.CheckMoment,
            PhiNOverride: overrides?.PhiN,
            FixedB: overrides?.B,
            FixedH0: overrides?.H0,
            FixedNs: overrides?.Ns);

        var forceSet = ctx?.Database?.ForceSets.FirstOrDefault(fs => fs.Id == task.ForceSetId);
        var build = ShearInclinedProfileFactory.Build(parameters, item, plane, forceSet, ctx?.Database);
        if (build.Profile is null)
            throw new InvalidOperationException(build.Error ?? "Не удалось построить профиль усилий.");
        warnings.AddRange(build.Warnings);

        var result = ShearInclinedChecker.Check(
            input, build.Profile, geometry, parameters.DirectionSign());
        return new ShearInclinedPlaneOutcome(plane, result, input, geometry, build.Profile);
    }

    /// <summary>Проверяет границы нормативного применения до запуска формул СП.</summary>
    static Sp63ShearApplicabilityResult Qualify(
        CrossSection section, LoadItem item, ShearPlane plane, ShearInclinedParams parameters,
        ForceSet? forceSet, DatabaseService? database)
    {
        var overrides = plane == ShearPlane.Vy ? parameters.OverridesVy : parameters.OverridesVx;
        var profileBuild = ShearInclinedProfileFactory.Build(parameters, item, plane, forceSet, database);
        double torsion = profileBuild.Profile is null
            ? Math.Abs(item.T)
            : profileBuild.Profile.MaxAbsT(profileBuild.Profile.StationRange.Min,
                                           profileBuild.Profile.StationRange.Max);
        double orthogonalShear = OrthogonalShearOf(item, plane);
        // В FEM-профиле LoadItem служит только точкой входа и может содержать нули.
        // Вторую поперечную силу следует брать из той же эпюры, иначе пространственная
        // комбинация могла бы ошибочно получить нормативный вердикт.
        if (parameters.ForceSource == "fem_profile")
        {
            var otherPlane = plane == ShearPlane.Vy ? ShearPlane.Vx : ShearPlane.Vy;
            var other = ShearInclinedProfileFactory.Build(
                parameters, item, otherPlane, forceSet, database).Profile;
            if (other is not null)
                orthogonalShear = other.MaxAbsQ(other.StationRange.Min, other.StationRange.Max);
        }
        var rectangle = RectangularShearSectionClassifier.Classify(section);
        var result = Sp63ShearApplicability.Evaluate(new Sp63ShearApplicabilityInput(
            parameters.ApplicabilityMode, rectangle.IsSupported, overrides?.B,
            parameters.EquivalentSectionConfirmed, orthogonalShear, torsion,
            overrides?.PhiN is not null));

        if (!rectangle.IsSupported && result.EffectiveMode == Sp63ShearApplicabilityMode.StandardAuto)
            return result with { Reasons = result.Reasons.Concat(rectangle.Reasons).Distinct().ToList() };
        return result;
    }

    /// <summary>Поперечная сила выбранной плоскости.</summary>
    static double ShearOf(LoadItem item, ShearPlane plane) =>
        plane == ShearPlane.Vy ? item.Vy : item.Vx;

    /// <summary>Поперечная сила перпендикулярной плоскости.</summary>
    static double OrthogonalShearOf(LoadItem item, ShearPlane plane) =>
        plane == ShearPlane.Vy ? item.Vx : item.Vy;

    /// <summary>Добавляет оговорку, если её ещё нет в списке.</summary>
    static void AddOnce(List<string> warnings, string message)
    {
        if (!warnings.Contains(message)) warnings.Add(message);
    }

    /// <summary>Плоскости сдвига, подлежащие расчёту.</summary>
    static IEnumerable<ShearPlane> Planes(ShearInclinedParams parameters) => parameters.Planes switch
    {
        "vy" => [ShearPlane.Vy],
        "vx" => [ShearPlane.Vx],
        _ => [ShearPlane.Vy, ShearPlane.Vx]
    };

    /// <summary>Формирует JSON результата задачи.</summary>
    static string Serialize(
        CalcTask task, CrossSection section, LoadItem item, ShearInclinedParams parameters,
        IReadOnlyList<ShearInclinedPlaneOutcome> outcomes, IReadOnlyList<string> warnings)
    {
        var inputs = new Dictionary<string, object>();
        var profiles = new Dictionary<string, object>();
        var details = new List<object>();
        var stations = new List<object>();
        double utilization = 0.0;
        double utilizationExact = 0.0;
        bool zeroCapacity = false;

        foreach (var outcome in outcomes)
        {
            string key = outcome.Plane == ShearPlane.Vy ? "vy" : "vx";
            inputs[key] = new
            {
                b = outcome.Input.B,
                h0 = outcome.Input.H0,
                qsw = outcome.Input.Qsw,
                sw = outcome.Input.Sw,
                ns = outcome.Input.Ns,
                rb = outcome.Input.Rb,
                rbt = outcome.Input.Rbt,
                autoB = outcome.Geometry.For(0.0).B,
                autoH0 = outcome.Geometry.For(0.0).H0,
                autoNs = outcome.Geometry.For(0.0).Ns,
                autoBTensionPositive = outcome.Geometry.TensionPositive.B,
                autoH0TensionPositive = outcome.Geometry.TensionPositive.H0,
                autoNsTensionPositive = outcome.Geometry.TensionPositive.Ns,
                autoBTensionNegative = outcome.Geometry.TensionNegative.B,
                autoH0TensionNegative = outcome.Geometry.TensionNegative.H0,
                autoNsTensionNegative = outcome.Geometry.TensionNegative.Ns,
                constructive103Confirmed = parameters.ConstructiveRequirements103Confirmed
            };

            // Профиль сохраняется целиком: отчёт обязан строить диаграмму по проекции C
            // тем же профилем, что и расчёт, а не подставным ConstantProfile.
            profiles[key] = DescribeProfile(parameters, item, outcome.Plane, outcome.Profile);

            foreach (var detail in outcome.Result.Details)
            {
                bool finite = double.IsFinite(detail.Ratio);
                if (!finite) zeroCapacity = true;
                details.Add(new
                {
                    plane = key,
                    formula = detail.Formula,
                    description = detail.Description,
                    normRef = detail.NormReference,
                    applied = detail.Applied,
                    allowable = detail.Allowable,
                    ratio = finite ? detail.Ratio : (double?)null,
                    status = finite ? "ok" : "no_capacity",
                    passed = detail.Passed,
                    variables = detail.Variables
                });
            }

            foreach (var station in outcome.Result.Stations)
                stations.Add(new
                {
                    plane = key,
                    s = station.S,
                    n = station.N,
                    phiN = station.PhiN,
                    tensionOnPositiveSide = station.TensionOnPositiveSide,
                    q = station.Q,
                    cCrit = Nullable(station.CriticalC),
                    qb = Nullable(station.Qb),
                    qsw = Nullable(station.Qsw),
                    eta = Nullable(station.Eta),
                    mApplied = Nullable(station.MomentApplied),
                    cCritMoment = Nullable(station.CriticalCMoment),
                    ms = Nullable(station.Ms),
                    msw = Nullable(station.Msw),
                    etaM = Nullable(station.EtaM)
                });

            double planeUtilization = outcome.Result.Utilization;
            if (double.IsFinite(planeUtilization))
                utilization = Math.Max(utilization, planeUtilization);
            else
                zeroCapacity = true;

            double planeExact = outcome.Result.UtilizationExact;
            if (double.IsFinite(planeExact))
                utilizationExact = Math.Max(utilizationExact, planeExact);
        }

        var allWarnings = outcomes
            .SelectMany(o => o.Result.Warnings)
            .Concat(warnings)
            .Distinct()
            .ToList();

        return JsonSerializer.Serialize(new
        {
            sectionTag = section.Tag,
            forceLabel = item.Label,
            calcType = task.CalcType.ToString(),
            elementKind = parameters.ElementKind,
            forceSource = parameters.ForceSource,
            direction = parameters.DirectionSign(),
            applicability = new
            {
                requestedMode = parameters.ApplicabilityMode,
                effectiveMode = parameters.ResolveApplicabilityMode(),
                status = parameters.ResolveApplicabilityMode() == Sp63ShearApplicabilityMode.Research
                    ? "research" : "ok",
                hasNormativeVerdict = parameters.ResolveApplicabilityMode() != Sp63ShearApplicabilityMode.Research,
                reasons = Array.Empty<string>()
            },
            inputs,
            profile = profiles,
            details,
            stations,
            warnings = allWarnings,
            // Нулевая несущая способность — это отказ, а не «нет значения»: коэффициент
            // использования пишется как null со статусом, чтобы сводка не сортировала
            // отказы по искусственному числу.
            utilization = zeroCapacity ? (double?)null : utilization,
            utilizationStatus = zeroCapacity ? "no_capacity" : "ok",
            // Дублирующее поле для совместимости ранее сохранённых результатов:
            // итоговый коэффициент также определяется только точными проверками.
            utilizationExact = zeroCapacity ? (double?)null : utilizationExact
        });
    }

    /// <summary>Конечное значение либо null — NaN в JSON недопустим.</summary>
    static double? Nullable(double value) => double.IsFinite(value) ? value : null;

    /// <summary>
    /// Сериализуемое описание профиля усилий: его достаточно, чтобы отчёт построил
    /// тот же профиль и повторил расчёт кривой по проекции C.
    /// </summary>
    static object DescribeProfile(
        ShearInclinedParams parameters, LoadItem item, ShearPlane plane, IForceProfile profile)
    {
        double q = plane == ShearPlane.Vy ? item.Vy : item.Vx;
        double m = plane == ShearPlane.Vy ? item.Mx : item.My;

        if (profile is SampledProfile sampled)
            return new
            {
                kind = "sampled",
                supportAtStart = parameters.SupportAtStart,
                supportAtEnd = parameters.SupportAtEnd,
                length = sampled.Length,
                samples = sampled.Samples
                    .Select(sample => new { s = sample.S, q = sample.Q, m = sample.M, n = sample.N, t = sample.T })
                    .ToList()
            };

        if (profile is UniformLoadProfile)
            return new
            {
                kind = "uniform_load",
                q0 = q,
                m0 = m,
                n0 = item.N,
                t0 = item.T,
                load = parameters.DistributedLoad,
                supportDistance = parameters.DistanceToSupport,
                supportAtStart = parameters.SupportAtStart,
                supportAtEnd = parameters.SupportAtEnd
            };

        return new
        {
            kind = "constant",
            q0 = q,
            m0 = m,
            n0 = item.N,
            t0 = item.T,
            supportDistance = parameters.DistanceToSupport
        };
    }

    /// <summary>Формирует результат без нормативного вердикта, не подставляя фиктивный коэффициент.</summary>
    static CalcResult NotApplicable(
        CalcTask task, CrossSection section, LoadItem item, ShearInclinedParams parameters,
        string created, Sp63ShearApplicabilityResult applicability) => new()
    {
        TaskId = task.Id,
        TaskKind = task.Kind,
        TaskTag = task.Tag,
        Created = created,
        Status = "ok",
        DataJson = JsonSerializer.Serialize(new
        {
            sectionTag = section.Tag,
            forceLabel = item.Label,
            calcType = task.CalcType.ToString(),
            elementKind = parameters.ElementKind,
            forceSource = parameters.ForceSource,
            direction = parameters.DirectionSign(),
            applicability = new
            {
                requestedMode = applicability.RequestedMode,
                effectiveMode = applicability.EffectiveMode,
                status = applicability.Status,
                hasNormativeVerdict = applicability.HasNormativeVerdict,
                reasons = applicability.Reasons
            },
            inputs = new { },
            profile = new { },
            details = Array.Empty<object>(),
            stations = Array.Empty<object>(),
            warnings = applicability.Reasons,
            utilization = (double?)null,
            utilizationStatus = "not_applicable",
            utilizationExact = (double?)null
        })
    };

    /// <summary>Результат с ошибкой.</summary>
    static CalcResult Error(CalcTask task, string created, string message) => new()
    {
        TaskId = task.Id,
        TaskKind = task.Kind,
        TaskTag = task.Tag,
        Created = created,
        Status = "error",
        DataJson = JsonSerializer.Serialize(new { error = message })
    };
}
