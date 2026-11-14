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
    IReadOnlyList<CalculationTraceStep> Trace)
{
    /// <summary>Условие прочности выполнено.</summary>
    public bool Passed => Utilization <= 1.0;
}

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
public sealed record PunchingResult(
    IReadOnlyList<PunchingVariantResult> Variants, int GoverningIndex,
    double Utilization, PunchingStatus Status,
    double UtilizationConcreteOnly, double UtilizationMaxReinforcement,
    double Fred, double BaseArea,
    IReadOnlyList<PunchingWarning> Warnings)
{
    /// <summary>Прочность на продавливание обеспечена с заданной арматурой.</summary>
    public bool Passed => GoverningIndex >= 0 && Utilization <= 1.0;

    /// <summary>Определяющий вариант.</summary>
    public PunchingVariantResult? Governing => GoverningIndex >= 0 ? Variants[GoverningIndex] : null;
}

/// <summary>Расчёт плит на продавливание по СП 63.13330.2018, пп. 8.1.46–8.1.52.</summary>
public static class PunchingChecker
{
    /// <summary>Выполняет расчёт всех вариантов контура.</summary>
    public static PunchingResult Check(PunchingInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!(input.H0 > 0)) throw new ArgumentException("Рабочая высота h0 должна быть больше нуля.", nameof(input));
        if (!(input.Rbt > 0)) throw new ArgumentException("Rbt должно быть больше нуля.", nameof(input));
        if (input.Forces.F < 0) throw new ArgumentException("Продавливающая сила F задаётся модулем (≥ 0).", nameof(input));
        if (input.Reinforcement is { } rr && (rr.Sw <= 0 || rr.Asw < 0 || rr.Rsw < 0))
            throw new ArgumentException("Шаг поперечной арматуры должен быть > 0, площадь и Rsw — ≥ 0.", nameof(input));

        var options = input.Options ?? new PunchingOptions();
        var edges = input.Edges ?? Array.Empty<PunchingSlabEdge>();
        var build = PunchingContourBuilder.Build(input);
        var warnings = new List<PunchingWarning>(build.Warnings);

        var f = input.Forces;
        double baseArea = PunchingContourBuilder.PyramidBaseArea(input.LoadArea, input.H0, edges);
        double fred = f.F - f.CounterPressure * baseArea;
        if (f.CounterPressure != 0 && fred <= 0)
            warnings.Add(new("fred_nonpositive",
                "Нагрузка в основании пирамиды не меньше продавливающей силы — продавливания нет (Кисп = 0)."));
        if (input.Reinforcement is { } r0 && r0.Qsw > 0)
            warnings.Add(new("sw_detailing_not_checked",
                "Конструктивные требования п. 10.3 к поперечной арматуре не проверяются."));

        var ctx = new Context(input, options, fred, baseArea, warnings);
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
            warnings.DistinctBy(w => w.Code + w.Text).ToList());
    }

    static bool Eligible(PunchingVariant v, PunchingOptions o)
        => v.Kind != PunchingVariantKind.PylonReduced || o.PylonReducedGoverns;

    sealed record Context(PunchingInput Input, PunchingOptions Options, double Fred, double BaseArea,
        List<PunchingWarning> Warnings);

    static PunchingVariantResult Evaluate(Context ctx, PunchingVariant v)
        => v.Kind == PunchingVariantKind.PylonReduced ? EvaluateReduced(ctx, v) : EvaluateContour(ctx, v);

    static PunchingVariantResult EvaluateContour(Context ctx, PunchingVariant v)
    {
        var input = ctx.Input;
        var f = input.Forces;
        double h0 = input.H0, rbt = input.Rbt, F = f.F;
        var g = ContourProperties.Compute(v.Contour);
        var o = PunchingContourBuilder.AreaCentroid(input.LoadArea);
        double ex = g.Xc - o.X, ey = g.Yc - o.Y;
        double share = f.MomentsAreMloc ? 0.5 : 1.0;
        double mx0 = share * f.Mx, my0 = share * f.My;
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

        double qsw = v.Zone == PunchingZone.Main ? input.Reinforcement?.Qsw ?? 0 : 0;
        double fswRaw = 0.8 * qsw * g.U;               // (8.91)
        bool counted = qsw > 0 && fswRaw >= 0.25 * fb * (1 - 1e-12);
        if (qsw > 0 && !counted)
            ctx.Warnings.Add(new("sw_not_counted",
                $"Контур «{v.Id}»: Fsw,ult < 0,25·Fb,ult — поперечная арматура не учитывается (п. 8.1.48).", [v.Id]));
        double fsw = counted ? fswRaw : 0;
        double mswx = counted ? 0.8 * qsw * wbx : 0, mswy = counted ? 0.8 * qsw * wby : 0; // (8.97), Wsw = Wb (8.1.52)

        double fult = Math.Min(fb + fsw, 2 * fb);
        double mxult = Math.Min(mbx + mswx, 2 * mbx), myult = Math.Min(mby + mswy, 2 * mby);

        var (kF, kMRaw, kM, k) = Utilization(ctx.Fred, mxStar, myStar, fult, mxult, myult);
        var (_, _, _, kb) = Utilization(ctx.Fred, mxStar, myStar, fb, mbx, mby);
        var (_, _, _, kmax) = Utilization(ctx.Fred, mxStar, myStar, 2 * fb, 2 * mbx, 2 * mby);
        if (kMRaw > kM + 1e-15)
            ctx.Warnings.Add(new("m_limited_half_kf",
                $"Контур «{v.Id}»: вклад моментов ограничен половиной вклада силы (п. 8.1.46).", [v.Id]));

        var trace = PunchingTrace.Build(new PunchingTraceData(v, g, wbx, wby, h0, rbt, input.Reinforcement,
            qsw, fswRaw, fsw, counted, fb, mbx, mby, mswx, mswy, fult, mxult, myult, F, ctx.Fred, mx0, my0,
            o, ex, ey, s, ctx.Options.EccentricityX, ctx.Options.EccentricityY, mxStar, myStar, kF, kMRaw, kM, k));

        return new PunchingVariantResult(v, g, wbx, wby, fb, fsw, counted, mbx, mby, mswx, mswy,
            fult, mxult, myult, ctx.Fred, mxStar, myStar, kF, kMRaw, kM, k, kb, kmax, false, trace);
    }

    static PunchingVariantResult EvaluateReduced(Context ctx, PunchingVariant v)
    {
        var input = ctx.Input;
        var rect = (PunchingLoadArea.Rectangle)input.LoadArea;
        var gp = GalyautdinovPerimeter.Compute(rect.Bx, rect.By, input.H0);
        double factor = GalyautdinovPerimeter.ReinforcementFactor(ctx.Options.LongitudinalRatio);
        double fb = input.Rbt * gp.URed * input.H0 * factor;   // (5.34), (5.35)
        double qsw = input.Reinforcement?.Qsw ?? 0;
        double fswRaw = 0.8 * qsw * gp.URed;
        bool counted = qsw > 0 && fswRaw >= 0.25 * fb;
        double fsw = counted ? fswRaw : 0;
        double fult = Math.Min(fb + fsw, 2 * fb);
        double kF = ctx.Fred > 0 ? ctx.Fred / fult : 0;
        double kb = ctx.Fred > 0 ? ctx.Fred / fb : 0;
        double kmax = ctx.Fred > 0 ? ctx.Fred / (2 * fb) : 0;
        var g = new ContourProperties(gp.URed, 0, 0, 0, 0, 0, 0, 0, 0);
        var trace = PunchingTrace.BuildReduced(v, gp, Math.Min(rect.Bx, rect.By), factor, input.H0, input.Rbt, fb, fsw, fult, ctx.Fred, kF);
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
