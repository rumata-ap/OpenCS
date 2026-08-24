namespace CScore.Sp63Shear;

/// <summary>
/// Выполняет проверки наклонных сечений по пп. 8.1.32–8.1.35: перебирает стоянки вдоль
/// элемента и длины проекции наклонного сечения, отбирая наиболее опасные сочетания.
/// Растянутая грань — а значит h0, Ns и b — определяется знаком момента в каждой стоянке.
/// </summary>
public static class ShearInclinedChecker
{
    /// <summary>Итог перебора проекций по поперечной силе для одной стоянки.</summary>
    readonly record struct ShearOutcome(
        CheckDetail Detail, double Applied, double Qb, double Qsw, double CriticalC, string? Note);

    /// <summary>Итог перебора проекций по моменту для одной стоянки.</summary>
    readonly record struct MomentOutcome(
        CheckDetail Detail, double Applied, double Ms, double Msw, double CriticalC);

    /// <summary>Выполняет проверки для заданного направления (0 — перебрать оба).</summary>
    public static ShearInclinedResult Check(
        ShearInclinedInput input, IForceProfile profile,
        InclinedSectionGeometryPair geometry, int direction)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(geometry);

        if (direction == 0)
        {
            var backward = Check(input, profile, geometry, -1);
            var forward = Check(input, profile, geometry, +1);
            return forward.Utilization > backward.Utilization ? forward : backward;
        }

        var warnings = geometry.TensionPositive.Warnings
            .Concat(geometry.TensionNegative.Warnings)
            .Distinct()
            .ToList();
        var stations = new List<StationResult>();

        CheckDetail? strip = null, shear = null, shearMin = null, moment = null, momentMin = null;
        bool usedPositive = false, usedNegative = false;
        bool zeroMoment = false, skippedNearSupport = false;

        foreach (double station in Stations(input, profile))
        {
            double n = profile.N(station);
            double m = profile.M(station);

            // Растянутая грань — по знаку момента именно в этой стоянке.
            var side = geometry.For(m);
            var stationInput = input.WithGeometry(side);
            var phi = ResolvePhiN(stationInput, n, side);

            if (side.TensionOnPositiveSide) usedPositive = true; else usedNegative = true;
            if (Math.Abs(m) <= InclinedSectionGeometryPair.MomentEpsilon && geometry.SidesDiffer)
                zeroMoment = true;

            var projections = Projections(stationInput, profile, station, direction).ToList();
            if (projections.Count == 0) skippedNearSupport = true;

            double appliedAtStation = profile.MaxAbsQ(station, station);
            var stripDetail = StripDetail(stationInput, appliedAtStation, phi, station);
            var minDetail = MinShearDetail(
                stationInput, profile, station, direction, appliedAtStation, phi, warnings);

            ShearOutcome? shearOutcome = projections.Count > 0
                ? WorstShear(stationInput, profile, projections, station, direction, phi.Value)
                : null;

            MomentOutcome? momentOutcome = null;
            CheckDetail? momentMinDetail = null;
            if (input.CheckMoment && stationInput.Ns > 0.0 && projections.Count > 0 &&
                MomentCheckZones.IsInZone(station, stationInput, profile))
            {
                momentOutcome = WorstMoment(stationInput, profile, projections, station, direction);
                momentMinDetail = SimplifiedMoment(stationInput, profile, station, direction);
            }

            stations.Add(new StationResult(
                S: station, N: n, PhiN: phi.Value,
                TensionOnPositiveSide: side.TensionOnPositiveSide,
                Q: shearOutcome?.Applied ?? appliedAtStation,
                CriticalC: shearOutcome?.CriticalC ?? double.NaN,
                Qb: shearOutcome?.Qb ?? double.NaN,
                Qsw: shearOutcome?.Qsw ?? double.NaN,
                Eta: shearOutcome?.Detail.Ratio ?? double.NaN,
                MomentApplied: momentOutcome?.Applied ?? double.NaN,
                CriticalCMoment: momentOutcome?.CriticalC ?? double.NaN,
                Ms: momentOutcome?.Ms ?? double.NaN,
                Msw: momentOutcome?.Msw ?? double.NaN,
                EtaM: momentOutcome?.Detail.Ratio ?? double.NaN));

            strip = Worse(strip, stripDetail);
            shear = Worse(shear, shearOutcome?.Detail);
            shearMin = Worse(shearMin, minDetail);
            moment = Worse(moment, momentOutcome?.Detail);
            momentMin = Worse(momentMin, momentMinDetail);

            if (shearOutcome?.Note is { } note) AddOnce(warnings, note);
        }

        if (usedPositive && usedNegative)
            AddOnce(warnings,
                "Момент меняет знак по длине элемента: растянутая грань, h0 и Ns определены "
                + "отдельно для каждой стоянки — расчёт фактически разбит на участки.");
        if (zeroMoment)
            AddOnce(warnings,
                "В части стоянок момент нулевой — принята сторона с меньшей рабочей высотой.");
        if (skippedNearSupport)
            AddOnce(warnings,
                "Для стоянок ближе h0 к опоре наклонное сечение до опоры не помещается: "
                + "проверка (8.56) для них не выполнялась, действует приопорное условие (8.60).");

        var details = new List<CheckDetail>();
        if (strip is not null) details.Add(strip);
        if (shear is not null) details.Add(shear);
        if (shearMin is not null) details.Add(shearMin);
        if (moment is not null) details.Add(moment);
        if (momentMin is not null) details.Add(momentMin);

        if (input.CheckMoment && geometry.TensionPositive.Ns <= 0.0 &&
            geometry.TensionNegative.Ns <= 0.0)
            warnings.Add("Растянутая продольная арматура отсутствует — проверки по 8.1.35 не выполнены.");

        return new ShearInclinedResult
        {
            Plane = geometry.TensionNegative.Plane,
            Details = details,
            Stations = stations,
            Warnings = warnings
        };
    }

    /// <summary>Строит кривую несущей способности по длине проекции для одной стоянки.</summary>
    public static IReadOnlyList<ProjectionPoint> ProjectionCurve(
        ShearInclinedInput input, IForceProfile profile,
        InclinedSectionGeometryPair geometry, double station, int direction)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(geometry);

        int dir = direction == 0 ? -1 : direction;
        var side = geometry.For(profile.M(station));
        var stationInput = input.WithGeometry(side);
        var phi = ResolvePhiN(stationInput, profile.N(station), side);
        var points = new List<ProjectionPoint>();

        foreach (double c in Projections(stationInput, profile, station, dir))
        {
            var model = new InclinedSectionModel(station, dir, c);
            double qb = ShearFormulas.ConcreteShear(stationInput, c, phi.Value);
            double qsw = ShearFormulas.StirrupShear(stationInput, c, phi.Value, out _);
            points.Add(new ProjectionPoint(c, qb, qsw, qb + qsw, model.AppliedShear(profile)));
        }
        return points;
    }

    /// <summary>Добавляет оговорку, если её ещё нет в списке.</summary>
    static void AddOnce(List<string> warnings, string message)
    {
        if (!warnings.Contains(message)) warnings.Add(message);
    }

    /// <summary>Коэффициент φn: ручное значение из параметров либо расчёт по 8.1.34.</summary>
    static PhiNResult ResolvePhiN(
        ShearInclinedInput input, double n, InclinedSectionGeometry geometry)
    {
        if (input.PhiNOverride is not double manual)
            return PhiNCalculator.Compute(input.Kind, n, geometry);

        return new PhiNResult(manual, n < 0.0,
            $"φn = {manual:F3} задан пользователем вручную.");
    }

    /// <summary>Наиболее опасное наклонное сечение по поперечной силе для одной стоянки.</summary>
    static ShearOutcome WorstShear(
        ShearInclinedInput input, IForceProfile profile, IReadOnlyList<double> projections,
        double station, int direction, double phiN)
    {
        double worstRatio = -1.0, bestC = projections[0], appliedAt = 0.0;
        double qbAt = 0.0, qswAt = 0.0;
        string? note = null;

        foreach (double c in projections)
        {
            var model = new InclinedSectionModel(station, direction, c);
            double qb = ShearFormulas.ConcreteShear(input, c, phiN);
            double qsw = ShearFormulas.StirrupShear(input, c, phiN, out string? localNote);
            double applied = model.AppliedShear(profile);
            double capacity = qb + qsw;
            double ratio = capacity > 0.0 ? applied / capacity : double.PositiveInfinity;

            if (ratio > worstRatio)
            {
                worstRatio = ratio;
                bestC = c;
                appliedAt = applied;
                qbAt = qb;
                qswAt = qsw;
                note = localNote;
            }
        }

        var detail = new CheckDetail
        {
            Formula = "8.56",
            Description = "Наклонное сечение на действие поперечной силы",
            NormReference = "СП 63.13330, п. 8.1.33",
            Applied = appliedAt,
            Allowable = qbAt + qswAt,
            Variables =
            {
                ["s"] = station, ["C"] = bestC, ["Qb"] = qbAt, ["Qsw"] = qswAt,
                ["phiN"] = phiN, ["b"] = input.B, ["h0"] = input.H0, ["qsw"] = input.Qsw
            }
        };
        return new ShearOutcome(detail, appliedAt, qbAt, qswAt, bestC, note);
    }

    /// <summary>Проверка бетонной полосы (8.55) для одной стоянки.</summary>
    static CheckDetail StripDetail(
        ShearInclinedInput input, double applied, PhiNResult phi, double station) => new()
    {
        Formula = "8.55",
        Description = "Полоса между наклонными сечениями",
        NormReference = "СП 63.13330, п. 8.1.32",
        Applied = applied,
        Allowable = ShearFormulas.StripCapacity(input, phi.Value, phi.AppliesToStrip),
        Variables =
        {
            ["s"] = station, ["phiN"] = phi.AppliesToStrip ? phi.Value : 1.0,
            ["b"] = input.B, ["h0"] = input.H0, ["Rb"] = input.Rb
        }
    };

    /// <summary>Упрощённая проверка по поперечной силе (8.60) для одной стоянки.</summary>
    static CheckDetail MinShearDetail(
        ShearInclinedInput input, IForceProfile profile, double station, int direction,
        double applied, PhiNResult phi, List<string> warnings)
    {
        double d = profile.HasSupport(direction)
            ? profile.SupportDistanceAt(station, direction)
            : 0.0;
        double qbMin = ShearFormulas.MinConcreteShear(input, phi.Value, d);
        double qswMin = ShearFormulas.MinStirrupShear(input, d, out string? note);
        if (note is not null) AddOnce(warnings, note);

        return new CheckDetail
        {
            Formula = "8.60",
            Description = "Упрощённая проверка по поперечной силе",
            NormReference = "СП 63.13330, п. 8.1.33",
            Applied = applied,
            Allowable = qbMin + qswMin,
            Variables =
            {
                ["s"] = station, ["d"] = d, ["Qb,min"] = qbMin, ["Qsw,min"] = qswMin,
                ["phiN"] = phi.Value
            }
        };
    }

    /// <summary>Наиболее опасное наклонное сечение по моменту (8.63) для одной стоянки.</summary>
    static MomentOutcome WorstMoment(
        ShearInclinedInput input, IForceProfile profile, IReadOnlyList<double> projections,
        double station, int direction)
    {
        double ms = MomentFormulas.LongitudinalMoment(input);
        double worstRatio = -1.0, bestC = projections[0], appliedAt = 0.0, mswAt = 0.0;

        foreach (double c in projections)
        {
            var model = new InclinedSectionModel(station, direction, c);
            double applied = model.AppliedMoment(profile);
            double msw = MomentFormulas.StirrupMoment(input, c);
            double capacity = ms + msw;
            double ratio = capacity > 0.0 ? applied / capacity : double.PositiveInfinity;

            if (ratio > worstRatio)
            {
                worstRatio = ratio;
                bestC = c;
                appliedAt = applied;
                mswAt = msw;
            }
        }

        var detail = new CheckDetail
        {
            Formula = "8.63",
            Description = "Наклонное сечение на действие момента",
            NormReference = "СП 63.13330, п. 8.1.35",
            Applied = appliedAt,
            Allowable = ms + mswAt,
            Variables =
            {
                ["s"] = station, ["C"] = bestC, ["Ms"] = ms, ["Msw"] = mswAt,
                ["k"] = input.AnchorageFactor, ["h0"] = input.H0
            }
        };
        return new MomentOutcome(detail, appliedAt, ms, mswAt, bestC);
    }

    /// <summary>Упрощённая проверка по моменту при C = 2·h0.</summary>
    static CheckDetail SimplifiedMoment(
        ShearInclinedInput input, IForceProfile profile, double station, int direction)
    {
        double c = 2.0 * input.H0;
        var model = new InclinedSectionModel(station, direction, c);
        double ms = MomentFormulas.LongitudinalMoment(input);
        double msw = MomentFormulas.SimplifiedStirrupMoment(input);

        return new CheckDetail
        {
            Formula = "8.63s",
            Description = "Упрощённая проверка по моменту при C = 2·h0",
            NormReference = "СП 63.13330, п. 8.1.35",
            Applied = model.AppliedMoment(profile),
            Allowable = ms + msw,
            Variables = { ["s"] = station, ["C"] = c, ["Ms"] = ms, ["Msw"] = msw }
        };
    }

    /// <summary>Координаты стоянок вдоль элемента.</summary>
    static IEnumerable<double> Stations(ShearInclinedInput input, IForceProfile profile)
    {
        var (min, max) = profile.StationRange;
        if (max - min <= 1e-9)
        {
            yield return min;
            yield break;
        }

        double step = input.StationStepOrAuto(max - min);
        for (double s = min; s <= max + 1e-9; s += step)
            yield return Math.Min(s, max);
    }

    /// <summary>
    /// Длины проекции наклонного сечения для одной стоянки. Пустая последовательность
    /// означает, что до опоры меньше h0 и наклонное сечение построить нельзя:
    /// для такой стоянки остаются (8.55) и приопорное условие (8.60) с поправкой d/h0.
    /// </summary>
    static IEnumerable<double> Projections(
        ShearInclinedInput input, IForceProfile profile, double station, int direction)
    {
        double min = input.H0;
        double max = 2.0 * input.H0;

        if (profile.HasSupport(direction))
        {
            double available = profile.SupportDistanceAt(station, direction);
            if (available < min - 1e-12) yield break;
            max = Math.Min(max, available);
        }

        double step = input.ProjectionStepOrAuto();
        for (double c = min; c <= max + 1e-12; c += step)
            yield return Math.Min(c, max);
    }

    /// <summary>Возвращает более опасную из двух проверок.</summary>
    static CheckDetail? Worse(CheckDetail? current, CheckDetail? candidate)
    {
        if (candidate is null) return current;
        if (current is null) return candidate;
        return candidate.Ratio > current.Ratio ? candidate : current;
    }
}
