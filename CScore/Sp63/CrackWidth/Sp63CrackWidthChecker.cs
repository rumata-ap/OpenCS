using CScore.Sp63.Normal;

namespace CScore.Sp63.CrackWidth;

/// <summary>
/// Проверяет ширину раскрытия трещин прямоугольного нормального сечения упрощённым
/// (не деформационно-модельным) путём по п. 8.2.9-8.2.16 СП 63: Mcrc через Wpl = 1.3·Wred,
/// σs через приведённое сечение с трещиной (ф. 8.134/8.135), ls по (8.136), ψs по (8.137).
/// Геометрия сечения и раскладка арматуры распознаются тем же
/// <see cref="Sp63RebarLayoutAnalyzer"/>, что и упрощённая проверка нормального сечения
/// (<see cref="Sp63NormalChecker"/>) — совпадающая архитектура: применимость (прямоугольник,
/// два эффективных слоя точечной арматуры, без преднапряжения) → расчёт → явный
/// <see cref="Sp63CrackWidthStatus.NotApplicable"/>, а не подмена строгого решателя
/// (<see cref="CScore.CrackWidthSolver"/>).
/// Сама формула σs/Mcrc/ls/ψs/acrc не дублируется: используется общая
/// <see cref="ShellSimplSolver.ComputeStripSls"/>, уже проверенная для плит — стержень
/// сводится к той же полосе, но с реальной шириной b вместо расчётной полосы 1 м
/// (усилия и площадь арматуры делятся на b, что для интенсивных результатов σs/ψs/ls/acrc
/// эквивалентно расчёту на полную ширину).
/// </summary>
public static class Sp63CrackWidthChecker
{
    const double ForceTolerance = 1e-9;
    const double MomentTolerance = 1e-9;

    /// <summary>Выполняет одноосную упрощённую проверку ширины раскрытия трещин.</summary>
    /// <param name="section">Расчётное сечение.</param>
    /// <param name="load">Нормальная сила и моменты в конвенции OpenCS (N: "+" — растяжение).</param>
    /// <param name="calc">Вид расчёта (вторая группа предельных состояний) для характеристик материалов.</param>
    /// <param name="options">Типизированные настройки формульного режима.</param>
    public static Sp63CrackWidthResult Check(
        CrossSection section,
        LoadItem load,
        CalcType calc,
        Sp63CrackWidthOptions options)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(load);
        ArgumentNullException.ThrowIfNull(options);

        if (options.ShapeKind != Sp63NormalShapeKind.Rectangular)
            return NotApplicable("unsupported_shape", "Sp63CrackWidth_ShapeNotSupported", "8.2");
        if (!Enum.IsDefined(options.Axis))
            return InvalidInput("invalid_axis", "Sp63CrackWidth_InvalidAxis", "8.2");
        if (!AreFinite(load.N, load.Mx, load.My))
            return InvalidInput("non_finite_load", "Sp63CrackWidth_NonFiniteLoad", "8.2");
        if (!IsFinitePositive(options.Phi1) || !IsFinitePositive(options.Phi2))
            return InvalidInput("invalid_phi", "Sp63CrackWidth_InvalidPhi", "8.2.10");
        if (!IsFinitePositive(options.AcrcLimMm))
            return InvalidInput("invalid_acrc_limit", "Sp63CrackWidth_InvalidAcrcLimit", "8.2.1");

        double moment = options.Axis == Sp63NormalAxis.Mx ? load.Mx : load.My;
        double otherMoment = options.Axis == Sp63NormalAxis.Mx ? load.My : load.Mx;
        if (Math.Abs(otherMoment) > MomentTolerance)
            return NotApplicable("biaxial_load", "Sp63CrackWidth_BiaxialLoad", "8.2");
        if (Math.Abs(moment) <= MomentTolerance)
            return NotApplicable("zero_moment", "Sp63CrackWidth_ZeroMoment", "8.2");

        int tensionDirection = Math.Sign(moment);
        var analysis = Sp63RebarLayoutAnalyzer.Analyze(section, options.Axis, calc, tensionDirection);
        if (analysis.Profile is null)
            return NotApplicable(analysis.Messages);
        var profile = analysis.Profile;

        var concreteArea = section.Areas.FirstOrDefault(area =>
            area.Category == AreaCategory.Region &&
            area.Material?.Type == MatType.Concrete);
        var concreteChars = concreteArea?.Material?.GetChars(calc);
        if (concreteChars is null || !IsFinitePositive(concreteChars.E) ||
            !IsFinitePositive(Math.Abs(concreteChars.Fc)) || !IsFinitePositive(concreteChars.Ft))
            return NotApplicable("missing_concrete_chars", "Sp63CrackWidth_MissingConcreteChars", "8.2.11");

        var rebarArea = section.Areas.FirstOrDefault(area =>
            area.Category == AreaCategory.RebarGroup && area.Material != null);
        var rebarChars = rebarArea?.Material?.GetChars(calc);
        if (rebarChars is null || !IsFinitePositive(rebarChars.E) || !IsFinitePositive(Math.Abs(rebarChars.Ft)))
            return NotApplicable("missing_rebar_chars", "Sp63CrackWidth_MissingRebarChars", "8.2.16");

        double b = profile.B;
        double ds = EffectiveDiameter(profile.TensionLayer);

        double mDes = Math.Abs(moment) / b;
        double nDes = load.N / b;
        double asT = profile.TensionLayer.Area / b;
        double asC = profile.CompressionLayer.Area / b;

        var strip = ShellSimplSolver.ComputeStripSls(
            mDes, nDes, profile.Height, profile.H0, profile.APrime,
            asT, asC, ds, concreteChars, rebarChars, options.Phi1, options.Phi2, options.AcrcLimMm);

        bool limitPassed = strip.Acrc_mm <= options.AcrcLimMm + 1e-9;
        var variables = new Dictionary<string, double>
        {
            ["N"] = load.N,
            ["M"] = moment,
            ["tensionDirection"] = tensionDirection,
            ["b"] = b,
            ["h"] = profile.Height,
            ["h0"] = profile.H0,
            ["aPrime"] = profile.APrime,
            ["As"] = profile.TensionLayer.Area,
            ["AsPrime"] = profile.CompressionLayer.Area,
            ["ds"] = ds,
            ["Rbser"] = Math.Abs(concreteChars.Fc),
            ["Rbtser"] = concreteChars.Ft,
            ["Rsser"] = Math.Abs(rebarChars.Ft),
            ["Es"] = rebarChars.E,
            ["phi1"] = options.Phi1,
            ["phi2"] = options.Phi2,
            ["Mcrc"] = strip.Mcrc,
            ["xm"] = strip.Xm,
            ["zs"] = strip.Zs,
            ["sigma_s"] = strip.Sigma_s_MPa,
            ["psi_s"] = strip.Psi_s,
            ["ls"] = strip.Ls_m,
            ["acrcLimMm"] = options.AcrcLimMm
        };

        var detail = new CheckDetail
        {
            Formula = "(8.130)-(8.141)",
            Description = "Sp63CrackWidth_AcrcCheck",
            NormReference = "8.2.9-8.2.16",
            Applied = strip.Acrc_mm,
            Allowable = options.AcrcLimMm,
            Variables = new Dictionary<string, double>(variables)
        };

        var informational = new List<Sp63CrackWidthMessage>
        {
            new("compression_zone_neutral_axis", Sp63CrackWidthMessageKind.Information,
                "8.2.28", "Sp63CrackWidth_NeutralAxisNote")
        };
        if (!strip.Cracked)
            informational.Add(new Sp63CrackWidthMessage("not_cracked",
                Sp63CrackWidthMessageKind.Information, "8.2.11", "Sp63CrackWidth_NotCracked"));

        return new Sp63CrackWidthResult
        {
            Status = Sp63CrackWidthStatus.Calculated,
            LimitPassed = limitPassed,
            Cracked = strip.Cracked,
            Branch = strip.Cracked ? "cracked" : "not_cracked",
            Details = [detail],
            InformationalMessages = informational,
            Variables = variables
        };
    }

    static double EffectiveDiameter(Sp63NormalRebarLayer layer)
    {
        double totalArea = layer.Bars.Sum(bar => bar.Area);
        if (totalArea <= 1e-14)
            return 0.012;
        return layer.Bars.Sum(bar => bar.Diameter * bar.Area) / totalArea;
    }

    static Sp63CrackWidthResult InvalidInput(string code, string text, string reference) => new()
    {
        Status = Sp63CrackWidthStatus.InvalidInput,
        Branch = "invalid_input",
        ApplicabilityMessages = [Message(code, Sp63CrackWidthMessageKind.Applicability, reference, text)]
    };

    static Sp63CrackWidthResult NotApplicable(string code, string text, string reference)
    {
        var messages = new List<Sp63CrackWidthMessage>
            { Message(code, Sp63CrackWidthMessageKind.Applicability, reference, text) };
        return new Sp63CrackWidthResult
        {
            Status = Sp63CrackWidthStatus.NotApplicable,
            Branch = "not_applicable",
            ApplicabilityMessages = messages,
            InformationalMessages = SuggestFullModelIfNeeded(messages)
        };
    }

    static Sp63CrackWidthResult NotApplicable(IReadOnlyList<Sp63NormalMessage> messages)
    {
        // Геометрия и раскладка арматуры распознаются общим Sp63RebarLayoutAnalyzer — причины
        // неприменимости дословно совпадают с Sp63NormalChecker, поэтому переиспользуются те же
        // ключи локализации "Sp63Normal_*" вместо дублирующих "Sp63CrackWidth_*" строк.
        var mapped = messages
            .Select(message => new Sp63CrackWidthMessage(message.Code,
                Sp63CrackWidthMessageKind.Applicability, message.NormReference, message.Text))
            .ToList();
        return new Sp63CrackWidthResult
        {
            Status = Sp63CrackWidthStatus.NotApplicable,
            Branch = "not_applicable",
            ApplicabilityMessages = mapped,
            InformationalMessages = SuggestFullModelIfNeeded(mapped)
        };
    }

    static readonly HashSet<string> FullModelSuggestionCodes =
    [
        "unsupported_geometry", "biaxial_load", "mixed_rebar_resistance",
        "insufficient_rebar_layers", "non_point_rebar", "prestressed_rebar", "zero_moment"
    ];

    static List<Sp63CrackWidthMessage> SuggestFullModelIfNeeded(List<Sp63CrackWidthMessage> messages) =>
        messages.Any(message => FullModelSuggestionCodes.Contains(message.Code))
            ? [Message("suggest_full_model", Sp63CrackWidthMessageKind.Information, "8.2",
                "Sp63CrackWidth_SuggestFullModel")]
            : [];

    static Sp63CrackWidthMessage Message(string code, Sp63CrackWidthMessageKind kind,
        string reference, string text) => new(code, kind, reference, text);

    static bool AreFinite(params double[] values) => values.All(double.IsFinite);

    static bool IsFinitePositive(double value) => double.IsFinite(value) && value > 0;
}
