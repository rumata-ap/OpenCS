using CScore.CalculationTrace;

namespace CScore.Sp63Punching;

/// <summary>Предупреждение расчёта: стабильный код и русский текст.</summary>
/// <param name="Code">Код для локализации в окнах (ключ <c>PunchingWarn_{Code}</c>).</param>
/// <param name="Text">Русский текст (отчёт, CLI, старые результаты).</param>
/// <param name="Args">Аргументы локализованного шаблона: id контуров и числа в инвариантной культуре.</param>
public sealed record PunchingWarning(string Code, string Text, IReadOnlyList<string>? Args = null);

/// <summary>Итоговая оценка узла.</summary>
public enum PunchingStatus
{
    /// <summary>Кисп по бетону ≤ 1 — «обеспечено по бетону».</summary>
    ConcreteSufficient,

    /// <summary>По бетону &gt; 1, с предельной арматурой (2Fb, 2Mb) ≤ 1 — «требуется армирование».</summary>
    ReinforcementRequired,

    /// <summary>&gt; 1 даже с предельной арматурой — «прочность не обеспечена».</summary>
    NotEnsured
}

/// <summary>Результат проверки одного варианта контура. Единицы: м, кН, кН·м.</summary>
public sealed record PunchingVariantResult(
    PunchingVariant Variant,
    ContourProperties Geometry,
    double Wbx, double Wby,
    double Fb, double Fsw, bool SwCounted, double Mbx, double Mby, double Mswx, double Mswy,
    double Fult, double Mxult, double Myult,
    double Fred, double MxStar, double MyStar,
    double KF, double KMRaw, double KM, double Utilization,
    double UtilizationConcreteOnly, double UtilizationMaxReinforcement,
    bool Governs,
    IReadOnlyList<CalculationTraceStep> Trace,
    PunchingSwDetail? Sw = null)
{
    /// <summary>Условие прочности выполнено.</summary>
    public bool Passed => Utilization <= 1.0;
}

/// <summary>Поперечная арматура в варианте контура (пп. 8.1.48, 8.1.50, 8.1.52). Значения — до порога 0,25Fb и предела 2Fb.</summary>
/// <param name="Cross">Крестообразная раскладка.</param>
/// <param name="AswLine">Площадь стержней одной линии в полосе ±0,5h0, м².</param>
/// <param name="Qsw">qsw равномерной раскладки, кН/м (у креста — 0).</param>
/// <param name="QX">qsw лучей вдоль X, кН/м.</param>
/// <param name="QY">qsw лучей вдоль Y, кН/м.</param>
/// <param name="Usw">Длина армированной части контура, м (у равномерной — u).</param>
/// <param name="FswRaw">0,8·Σqsw·L, кН.</param>
/// <param name="MswxRaw">0,8·qsw·Wswx (у креста — по участкам), кН·м.</param>
/// <param name="MswyRaw">То же относительно оси Y.</param>
/// <param name="YMax">Делитель Wbx (расстояние до волокна), м.</param>
/// <param name="XMax">Делитель Wby, м.</param>
/// <param name="Armed">Участки под лучами креста; null — равномерная.</param>
public sealed record PunchingSwDetail(
    bool Cross, double AswLine, double Qsw, double QX, double QY, double Usw,
    double FswRaw, double MswxRaw, double MswyRaw, double YMax, double XMax,
    PunchingArmedSegments? Armed);

/// <summary>Результат расчёта на продавливание.</summary>
/// <param name="Variants">Все варианты контура.</param>
/// <param name="GoverningIndex">Индекс определяющего варианта; −1 — вариантов нет.</param>
/// <param name="Utilization">Кисп определяющего варианта.</param>
/// <param name="Status">Нужна ли арматура и поможет ли она.</param>
/// <param name="UtilizationConcreteOnly">Кисп без поперечной арматуры (наибольший по вариантам у грани).</param>
/// <param name="UtilizationMaxReinforcement">Кисп при Fult = 2Fb, Mult = 2Mb.</param>
/// <param name="Fred">Продавливающая сила за вычетом нагрузки в основании пирамиды, кН.</param>
/// <param name="BaseArea">Площадь основания пирамиды, м².</param>
/// <param name="Warnings">Предупреждения.</param>
/// <param name="Detailing">Конструктивные проверки п. 10.3.17 (пусто — ряды не заданы).</param>
/// <param name="Rows">Счёт рядов в пирамиде (null — ряды не заданы).</param>
public sealed record PunchingResult(
    IReadOnlyList<PunchingVariantResult> Variants, int GoverningIndex,
    double Utilization, PunchingStatus Status,
    double UtilizationConcreteOnly, double UtilizationMaxReinforcement,
    double Fred, double BaseArea,
    IReadOnlyList<PunchingWarning> Warnings,
    IReadOnlyList<PunchingDetailingCheck>? Detailing = null,
    PunchingRowCount? Rows = null)
{
    /// <summary>Прочность на продавливание обеспечена с заданной арматурой.</summary>
    public bool Passed => GoverningIndex >= 0 && Utilization <= 1.0;

    /// <summary>Определяющий вариант.</summary>
    public PunchingVariantResult? Governing => GoverningIndex >= 0 ? Variants[GoverningIndex] : null;
}

/// <summary>
/// Геометрия узла, не зависящая от усилий: варианты контура, их характеристики и площадь основания пирамиды.
/// Строится один раз для всех строк набора усилий (пакет) — усилия на неё не влияют.
/// </summary>
public sealed class PunchingGeometryCache
{
    PunchingGeometryCache(PunchingContourBuilder.BuildResult build, double baseArea, IReadOnlyDictionary<string, ContourProperties> props,
        IReadOnlyDictionary<string, PunchingArmedSegments> armed)
    {
        Build = build; BaseArea = baseArea; Properties = props; Armed = armed;
    }

    /// <summary>Варианты контура и предупреждения построения.</summary>
    public PunchingContourBuilder.BuildResult Build { get; }
    /// <summary>Площадь основания пирамиды продавливания, м².</summary>
    public double BaseArea { get; }
    /// <summary>Характеристики контура по id варианта.</summary>
    public IReadOnlyDictionary<string, ContourProperties> Properties { get; }
    /// <summary>Участки под лучами креста по id варианта основной зоны (пусто — не крест).</summary>
    public IReadOnlyDictionary<string, PunchingArmedSegments> Armed { get; }

    /// <summary>Строит геометрию по исходным данным (усилия не используются).</summary>
    public static PunchingGeometryCache Prepare(PunchingInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        input = input.Normalized();
        var build = PunchingContourBuilder.Build(input);
        var props = build.Variants.Where(v => v.Kind != PunchingVariantKind.PylonReduced)
            .ToDictionary(v => v.Id, v => ContourProperties.Compute(v.Contour), StringComparer.Ordinal);
        double baseArea = PunchingContourBuilder.PyramidBaseArea(input);
        var armed = new Dictionary<string, PunchingArmedSegments>(StringComparer.Ordinal);
        if (input.Reinforcement?.Cross is { } cross)
            foreach (var v in build.Variants.Where(v => v.Zone == PunchingZone.Main && props.ContainsKey(v.Id)))
            {
                var g = props[v.Id];
                armed[v.Id] = PunchingSwLayout.Armed(v.Contour, input.LoadArea, cross, new PlanPoint(g.Xc, g.Yc));
            }
        return new PunchingGeometryCache(build, baseArea, props, armed);
    }
}

/// <summary>Расчёт плит на продавливание по СП 63.13330.2018, пп. 8.1.46–8.1.52.</summary>
public static class PunchingChecker
{
    /// <summary>Выполняет расчёт всех вариантов контура.</summary>
    /// <param name="input">Исходные данные.</param>
    /// <param name="includeTrace">false — без пошаговой трассировки (пакет по тысячам строк); числа те же.</param>
    /// <param name="geometry">Готовая геометрия того же узла (пакет); null — строится здесь.</param>
    public static PunchingResult Check(PunchingInput input, bool includeTrace = true, PunchingGeometryCache? geometry = null)
        => Check(input, includeTrace, geometry, 1.0);

    /// <summary>Расчёт с qsw раскладки, умноженным на <paramref name="swScale"/> (подбор).</summary>
    internal static PunchingResult Check(PunchingInput input, bool includeTrace, PunchingGeometryCache? geometry, double swScale)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!(input.H0 > 0)) throw new ArgumentException("Рабочая высота h0 должна быть больше нуля.", nameof(input));
        if (!(input.Rbt > 0)) throw new ArgumentException("Rbt должно быть больше нуля.", nameof(input));
        if (input.Forces.F < 0) throw new ArgumentException("Продавливающая сила F задаётся модулем (≥ 0).", nameof(input));
        if (input.Reinforcement is { } rr) ValidateReinforcement(rr);
        if (input.Wall is { } wl && (!(wl.A1 > 0) || wl.A2 is <= 0))
            throw new ArgumentException("Толщина стены должна быть больше нуля.", nameof(input));
        bool wallCrossIgnored = input.Wall is not null && input.Reinforcement?.Cross is not null;
        input = input.Normalized();

        var options = input.Options ?? new PunchingOptions();
        var edges = input.Edges ?? Array.Empty<PunchingSlabEdge>();
        var build = geometry?.Build ?? PunchingContourBuilder.Build(input);
        var warnings = new List<PunchingWarning>(build.Warnings);
        if (wallCrossIgnored) warnings.Add(PunchingContourBuilder.WallCrossIgnoredWarning);

        var f = input.Forces;
        double baseArea = geometry?.BaseArea ?? PunchingContourBuilder.PyramidBaseArea(input);
        double fred = f.F - f.CounterPressure * baseArea;
        if (f.CounterPressure != 0 && fred <= 0)
            warnings.Add(new("fred_nonpositive",
                "Нагрузка в основании пирамиды не меньше продавливающей силы — продавливания нет (Кисп = 0)."));
        var sw = SwSetup.From(input, warnings);
        var detailing = PunchingSwLayout.Detailing(input);
        var bad = detailing.Where(d => d.State is PunchingDetailingState.Violated or PunchingDetailingState.Allowed).ToList();
        if (bad.Count > 0)
            warnings.Add(new("sw_detailing_violated",
                $"Требования п. 10.3.17 не выполнены ({string.Join(", ", bad.Select(b => PunchingSwLayout.DetailingName(b.Code)))}): учитываются только стержни, " +
                "пересекающие пирамиду продавливания (п. 8.1.48), при условии их анкеровки — проверьте анкеровку.",
                [string.Join(", ", bad.Select(b => b.Code))]));

        var ctx = new Context(input, options, fred, baseArea, warnings, includeTrace, geometry, sw, swScale);
        var results = build.Variants.Select(v => Evaluate(ctx, v)).ToList();

        int gov = -1;
        double util = 0;
        for (int i = 0; i < results.Count; i++)
        {
            if (!Eligible(results[i].Variant, options)) continue;
            if (gov < 0 || results[i].Utilization > util) { gov = i; util = results[i].Utilization; }
        }
        if (gov < 0)
            warnings.Add(new("no_variants", "Не построено ни одного расчётного контура."));
        else
            results[gov] = results[gov] with { Governs = true };

        var statusSet = results.Where(r => Eligible(r.Variant, options) && r.Variant.Zone == PunchingZone.Main).ToList();
        double kb = statusSet.Count > 0 ? statusSet.Max(r => r.UtilizationConcreteOnly) : 0;
        double kmax = statusSet.Count > 0 ? statusSet.Max(r => r.UtilizationMaxReinforcement) : 0;
        var status = kb <= 1.0 ? PunchingStatus.ConcreteSufficient
            : kmax <= 1.0 ? PunchingStatus.ReinforcementRequired
            : PunchingStatus.NotEnsured;

        return new PunchingResult(results, gov, util, status, kb, kmax, fred, baseArea,
            warnings.DistinctBy(w => w.Code + w.Text).ToList(), detailing, sw.Rows);
    }

    static bool Eligible(PunchingVariant v, PunchingOptions o)
        => v.Kind != PunchingVariantKind.PylonReduced || o.PylonReducedGoverns;

    sealed record Context(PunchingInput Input, PunchingOptions Options, double Fred, double BaseArea,
        List<PunchingWarning> Warnings, bool IncludeTrace, PunchingGeometryCache? Geometry, SwSetup Sw, double SwScale);

    /// <summary>qsw раскладки, общие для всех вариантов.</summary>
    internal sealed record SwSetup(PunchingShearReinforcement? R, double AswLine, double Q, double QX, double QY, PunchingRowCount? Rows)
    {
        public bool Any => R is not null && (Q > 0 || QX > 0 || QY > 0);

        public static SwSetup From(PunchingInput input, List<PunchingWarning> warnings)
        {
            var r = input.Reinforcement;
            if (r is null) return new SwSetup(null, 0, 0, 0, 0, null);
            double h0 = input.H0;
            PunchingRowCount? rc = r.Rows is { } rows ? PunchingSwLayout.CountRows(rows, h0) : null;
            if (rc is { NearFace: > 0 })
                warnings.Add(new("sw_rows_near_face",
                    $"Ряды поперечной арматуры ближе h0/3 к грани площади ({rc.NearFace} шт.) в расчёте не учитываются.",
                    [rc.NearFace.ToString(System.Globalization.CultureInfo.InvariantCulture)]));
            double asw = PunchingSwLayout.AswPerLine(r, h0);
            if (r.Rows is null && (r.Asw > 0 || r.Cross is not null))
                warnings.Add(new("sw_detailing_not_checked",
                    "Конструктивные требования п. 10.3 к поперечной арматуре не проверяются (ряды не заданы)."));
            if (r.Cross is not null)
            {
                var (qx, qy) = PunchingSwLayout.QCross(r, h0);
                return new SwSetup(r, asw, 0, qx, qy, rc);
            }
            return new SwSetup(r, asw, PunchingSwLayout.QUniform(r, h0), 0, 0, rc);
        }
    }

    static void ValidateReinforcement(PunchingShearReinforcement r)
    {
        if (r.Asw < 0 || r.Rsw < 0)
            throw new ArgumentException("Площадь поперечной арматуры и Rsw должны быть ≥ 0.", nameof(r));
        if (r.Cross is null && !(r.Sw > 0))
            throw new ArgumentException("Шаг поперечной арматуры вдоль контура должен быть > 0.", nameof(r));
        if (r.Rows is { } rows && (rows.Count < 1 || rows.BarArea < 0 || rows.FirstRow < 0 || (rows.Count > 1 && !(rows.Step > 0))))
            throw new ArgumentException("Ряды поперечной арматуры: число ≥ 1, расстояние до первого ряда ≥ 0, шаг > 0.", nameof(r));
        foreach (var a in new[] { r.Cross?.AlongX, r.Cross?.AlongY })
            if (a is not null && (a.Width < 0 || (a.Width > 0 && !(a.Sw > 0))))
                throw new ArgumentException("Лучи креста: ширина ≥ 0, шаг линий > 0.", nameof(r));
    }

    static PunchingVariantResult Evaluate(Context ctx, PunchingVariant v)
        => v.Kind == PunchingVariantKind.PylonReduced ? EvaluateReduced(ctx, v) : EvaluateContour(ctx, v);

    static PunchingVariantResult EvaluateContour(Context ctx, PunchingVariant v)
    {
        var input = ctx.Input;
        var f = input.Forces;
        double h0 = input.H0, rbt = input.Rbt, F = f.F;
        var g = ctx.Geometry?.Properties.GetValueOrDefault(v.Id) ?? ContourProperties.Compute(v.Contour);
        var o = PunchingContourBuilder.LoadCenter(input);
        double ex = g.Xc - o.X, ey = g.Yc - o.Y;
        double share = f.MomentsAreMloc ? 0.5 : 1.0;
        double mx0 = share * f.Mx, my0 = share * f.My;
        // СП 430 п. 6.3.8: у торца стены — момент только из плоскости стены (Mx, вектор вдоль стены), эксцентриситет
        // вдоль стены не учитывается; у угла — только сила.
        var wall = input.Wall;
        bool wallNode = wall is not null && v.Kind is PunchingVariantKind.WallEnd or PunchingVariantKind.WallEndEdge or PunchingVariantKind.WallCorner;
        if (wallNode)
        {
            my0 = 0; ex = 0;
            if (wall!.Kind == PunchingWallKind.Corner) { mx0 = 0; ey = 0; }
        }
        double s = f.LoadSide == PunchingLoadSide.FromAbove ? 1.0 : -1.0;

        // Момент относительно центра тяжести контура (п. 8.1.46).
        double mxSigned = mx0 + s * F * ey;
        double mySigned = my0 - s * F * ex;
        double mxStar = Star(ctx.Options.EccentricityX, mx0, F * ey, mxSigned);
        double myStar = Star(ctx.Options.EccentricityY, my0, F * ex, mySigned);

        // Моменты сопротивления (8.98).
        double wbx = g.WbxMin, wby = g.WbyMin;
        if (ctx.Options.Wb == SectionModulusMode.StressSide)
        {
            if (ctx.Options.EccentricityX == EccentricityMode.Auto && ctx.Options.EccentricityY == EccentricityMode.Auto)
            {
                // Сторона, где касательные напряжения от момента складываются с напряжениями от F.
                if (mxSigned != 0)
                {
                    double d = -Math.Sign(mxSigned) * s > 0 ? g.YPlus : g.YMinus;
                    if (d > 0) wbx = g.Ibx / d;
                }
                if (mySigned != 0)
                {
                    double d = Math.Sign(mySigned) * s > 0 ? g.XPlus : g.XMinus;
                    if (d > 0) wby = g.Iby / d;
                }
            }
            else
                ctx.Warnings.Add(new("wb_stress_side_needs_auto",
                    "«Уточнённый» момент сопротивления требует учёта эксцентриситета по знакам (Auto) — взят наименьший W."));
        }

        double fb = rbt * g.U * h0;                     // (8.88), (8.89)
        double mbx = rbt * wbx * h0, mby = rbt * wby * h0; // (8.94)

        var swd = v.Zone == PunchingZone.Main ? SwDetail(ctx, v, g, wbx, wby) : null;
        double fswRaw = swd?.FswRaw ?? 0;
        bool counted = fswRaw > 0 && fswRaw >= 0.25 * fb * (1 - 1e-12);
        if (fswRaw > 0 && !counted && ctx.SwScale == 1.0)
            ctx.Warnings.Add(new("sw_not_counted",
                $"Контур «{v.Id}»: Fsw,ult < 0,25·Fb,ult — поперечная арматура не учитывается (п. 8.1.48).", [v.Id]));
        double fsw = counted ? fswRaw : 0;
        double mswx = counted ? swd!.MswxRaw : 0, mswy = counted ? swd!.MswyRaw : 0;
        double qsw = swd?.Qsw ?? 0;

        double fult = Math.Min(fb + fsw, 2 * fb);
        double mxult = Math.Min(mbx + mswx, 2 * mbx), myult = Math.Min(mby + mswy, 2 * mby);

        var (kF, kMRaw, kM, k) = Utilization(ctx.Fred, mxStar, myStar, fult, mxult, myult);
        var (_, _, _, kb) = Utilization(ctx.Fred, mxStar, myStar, fb, mbx, mby);
        var (_, _, _, kmax) = Utilization(ctx.Fred, mxStar, myStar, 2 * fb, 2 * mbx, 2 * mby);
        if (kMRaw > kM + 1e-15)
            ctx.Warnings.Add(new("m_limited_half_kf",
                $"Контур «{v.Id}»: вклад моментов ограничен половиной вклада силы (п. 8.1.46).", [v.Id]));

        IReadOnlyList<CalculationTraceStep> trace = !ctx.IncludeTrace ? [] : PunchingTrace.Build(new PunchingTraceData(v, g, wbx, wby, h0, rbt, input.Reinforcement,
            qsw, fswRaw, fsw, counted, fb, mbx, mby, mswx, mswy, fult, mxult, myult, F, ctx.Fred, mx0, my0,
            o, ex, ey, s, ctx.Options.EccentricityX, ctx.Options.EccentricityY, mxStar, myStar, kF, kMRaw, kM, k, swd, ctx.Sw.Rows, wallNode ? wall : null));

        return new PunchingVariantResult(v, g, wbx, wby, fb, fsw, counted, mbx, mby, mswx, mswy,
            fult, mxult, myult, ctx.Fred, mxStar, myStar, kF, kMRaw, kM, k, kb, kmax, false, trace, swd);
    }

    /// <summary>
    /// Fsw, Msw варианта основной зоны. Равномерная: (8.91), (8.97) с Wsw = Wb. Крест: по участкам под лучами,
    /// Wsw — по тем же правилам, что Wb (относительно центра тяжести контура бетона, тот же делитель).
    /// </summary>
    static PunchingSwDetail? SwDetail(Context ctx, PunchingVariant v, ContourProperties g, double wbx, double wby)
    {
        var sw = ctx.Sw;
        if (!sw.Any) return null;
        double s = ctx.SwScale;
        double ymax = wbx > 0 ? g.Ibx / wbx : 0, xmax = wby > 0 ? g.Iby / wby : 0;
        if (sw.R!.Cross is not { } cross)
        {
            double q = s * sw.Q;
            return new PunchingSwDetail(false, s * sw.AswLine, q, 0, 0, g.U,
                0.8 * q * g.U, 0.8 * q * wbx, 0.8 * q * wby, ymax, xmax, null);   // (8.91), (8.97), п. 8.1.52
        }
        var armed = ctx.Geometry?.Armed.GetValueOrDefault(v.Id)
            ?? PunchingSwLayout.Armed(v.Contour, ctx.Input.LoadArea, cross, new PlanPoint(g.Xc, g.Yc));
        double qx = s * sw.QX, qy = s * sw.QY;
        double fsw = 0.8 * (qx * armed.LX + qy * armed.LY);
        double mswx = ymax > 0 ? 0.8 * (qx * armed.IbxX + qy * armed.IbxY) / ymax : 0;
        double mswy = xmax > 0 ? 0.8 * (qx * armed.IbyX + qy * armed.IbyY) / xmax : 0;
        return new PunchingSwDetail(true, s * sw.AswLine, 0, qx, qy, armed.LX + armed.LY, fsw, mswx, mswy, ymax, xmax, armed);
    }

    static PunchingVariantResult EvaluateReduced(Context ctx, PunchingVariant v)
    {
        var input = ctx.Input;
        var rect = (PunchingLoadArea.Rectangle)input.LoadArea;
        var gp = GalyautdinovPerimeter.Compute(rect.Bx, rect.By, input.H0);
        double factor = GalyautdinovPerimeter.ReinforcementFactor(ctx.Options.LongitudinalRatio);
        double fb = input.Rbt * gp.URed * input.H0 * factor;   // (5.34), (5.35)
        double qsw = ctx.SwScale * ctx.Sw.Q;
        if (ctx.Sw.R?.Cross is not null && ctx.Sw.Any)
            ctx.Warnings.Add(new("sw_cross_reduced_ignored",
                "Приведённый периметр пилона: крестообразная арматура не учитывается (только равномерная вдоль контура)."));
        double fswRaw = 0.8 * qsw * gp.URed;
        bool counted = qsw > 0 && fswRaw >= 0.25 * fb;
        double fsw = counted ? fswRaw : 0;
        double fult = Math.Min(fb + fsw, 2 * fb);
        double kF = ctx.Fred > 0 ? ctx.Fred / fult : 0;
        double kb = ctx.Fred > 0 ? ctx.Fred / fb : 0;
        double kmax = ctx.Fred > 0 ? ctx.Fred / (2 * fb) : 0;
        var g = new ContourProperties(gp.URed, 0, 0, 0, 0, 0, 0, 0, 0);
        IReadOnlyList<CalculationTraceStep> trace = !ctx.IncludeTrace ? [] : PunchingTrace.BuildReduced(v, gp, Math.Min(rect.Bx, rect.By), factor, input.H0, input.Rbt, fb, fsw, fult, ctx.Fred, kF);
        return new PunchingVariantResult(v, g, 0, 0, fb, fsw, counted, 0, 0, 0, 0, fult, 0, 0,
            ctx.Fred, 0, 0, kF, 0, 0, kF, kb, kmax, false, trace);
    }

    static double Star(EccentricityMode mode, double m, double fe, double signed) => mode switch
    {
        EccentricityMode.Auto => Math.Abs(signed),
        EccentricityMode.Loading => Math.Abs(m) + Math.Abs(fe),
        EccentricityMode.Unloading => Math.Abs(Math.Abs(m) - Math.Abs(fe)),
        _ => Math.Abs(m)
    };

    /// <summary>(8.93), (8.95), (8.96): Кисп = F/Fult + min(Mx/Mxult + My/Myult; 0,5·F/Fult).</summary>
    static (double KF, double KMRaw, double KM, double K) Utilization(
        double fred, double mx, double my, double fult, double mxult, double myult)
    {
        if (fred <= 0) return (0, 0, 0, 0);
        double kF = fult > 0 ? fred / fult : double.PositiveInfinity;
        double kMRaw = Ratio(mx, mxult) + Ratio(my, myult);
        double kM = Math.Min(kMRaw, 0.5 * kF);
        return (kF, kMRaw, kM, kF + kM);
    }

    static double Ratio(double m, double mult) => m <= 0 ? 0 : mult > 0 ? m / mult : double.PositiveInfinity;
}
