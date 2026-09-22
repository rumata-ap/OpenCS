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
        if (options.Mode == Sp63CrackWidthMode.LongAndShort &&
            !IsFinitePositive(options.AcrcLimShortMm))
            return InvalidInput("invalid_acrc_limit", "Sp63CrackWidth_InvalidAcrcLimit", "8.2.6");
        if (options.Mode == Sp63CrackWidthMode.LongAndShort &&
            (!double.IsFinite(options.LongTermShare) || options.LongTermShare is < 0 or > 1))
            return InvalidInput("invalid_long_term_share", "Sp63CrackWidth_InvalidLongTermShare", "8.2.5");

        double moment = options.Axis == Sp63NormalAxis.Mx ? load.Mx : load.My;
        double otherMoment = options.Axis == Sp63NormalAxis.Mx ? load.My : load.Mx;
        if (Math.Abs(otherMoment) > MomentTolerance)
            return NotApplicable("biaxial_load", "Sp63CrackWidth_BiaxialLoad", "8.2");
        if (Math.Abs(moment) <= MomentTolerance)
            return NotApplicable("zero_moment", "Sp63CrackWidth_ZeroMoment", "8.2");

        int tensionDirection = Math.Sign(moment);
        var analysis = Sp63RebarLayoutAnalyzer.Analyze(section, options.Axis, calc,
            tensionDirection, requireAtLeastTwoLayers: true);
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
        double dsOwn = EffectiveDiameter(profile.TensionLayer);
        double dsOpposite = EffectiveDiameter(profile.CompressionLayer);
        double asT = profile.TensionLayer.Area / b;
        double asC = profile.CompressionLayer.Area / b;
        double absMoment = Math.Abs(moment);
        bool full = options.Mode == Sp63CrackWidthMode.LongAndShort;
        double share = full ? options.LongTermShare : 0.0;

        // Одна составляющая acrc,i (п. 8.2.15) от момента m и силы n с коэффициентом φ1
        // для своего ряда арматуры или для ряда у грани, которую момент не растягивает.
        ShellSimplStripResult Strip(double m, double n, double phi1, bool opposite) => opposite
            ? ShellSimplSolver.ComputeOppositeRowSls(
                m / b, n / b, profile.Height, profile.H0, profile.APrime,
                asT, asC, dsOpposite, concreteChars, rebarChars, phi1, options.Phi2,
                options.AcrcLimMm, options.SigmaSCrc, options.WplGamma)
            : ShellSimplSolver.ComputeStripSls(
                m / b, n / b, profile.Height, profile.H0, profile.APrime,
                asT, asC, dsOwn, concreteChars, rebarChars, phi1, options.Phi2,
                options.AcrcLimMm, options.SigmaSCrc, options.WplGamma);

        // Полный режим (п. 8.2.7): acrc2 — полная нагрузка при φ1 = 1,0; acrc1/acrc3 —
        // длительная часть ψ·(N, M) при φ1 = 1,4 и 1,0. Одиночный — одна составляющая с φ1.
        CrackTerms Evaluate(bool opposite)
        {
            var main = Strip(absMoment, load.N, full ? 1.0 : options.Phi1, opposite);
            if (!full || share <= 0.0) return new CrackTerms(main, null, null);
            return new CrackTerms(main,
                Strip(share * absMoment, share * load.N, 1.4, opposite),
                Strip(share * absMoment, share * load.N, 1.0, opposite));
        }

        var terms = Evaluate(opposite: false);
        double ds = dsOwn;

        // При x_m ≤ 0 сечение растянуто насквозь: растянут и ряд, который момент не растягивает,
        // и у его грани тоже есть трещина. При несимметричном армировании решает он — так же,
        // как в плитной проверке Капра-Мори (ShellSimplSolver.DirectionStrip). Ряд выбирается
        // по полной нагрузке и сохраняется для всех составляющих: x_m от доли ψ не зависит.
        bool oppositeGoverns = false;
        if (terms.Main.Xm <= 0.0)
        {
            var opposite = Evaluate(opposite: true);
            if (opposite.Governing(full) > terms.Governing(full))
            {
                terms = opposite;
                ds = dsOpposite;
                oppositeGoverns = true;
            }
        }

        var strip = terms.Main;
        var variables = new Dictionary<string, double>
        {
            ["N"] = load.N,
            ["M"] = moment,
            ["tensionDirection"] = oppositeGoverns ? -tensionDirection : tensionDirection,
            ["b"] = b,
            ["h"] = profile.Height,
            ["h0"] = strip.H0,
            ["aPrime"] = strip.A_prime,
            ["As"] = strip.As_t * b,
            ["AsPrime"] = strip.As_c * b,
            ["ds"] = ds,
            ["Rbser"] = Math.Abs(concreteChars.Fc),
            ["Rbtser"] = concreteChars.Ft,
            ["Rsser"] = Math.Abs(rebarChars.Ft),
            ["Es"] = rebarChars.E,
            ["phi1"] = full ? 1.0 : options.Phi1,
            ["phi2"] = options.Phi2,
            ["Mcrc"] = strip.Mcrc,
            ["xm"] = strip.Xm,
            ["zs"] = strip.Zs,
            ["sigma_s"] = strip.Sigma_s_MPa,
            ["psi_s"] = strip.Psi_s,
            ["ls"] = strip.Ls_m,
            ["acrcLimMm"] = options.AcrcLimMm
        };

        List<CheckDetail> details;
        bool limitPassed;
        if (full)
        {
            double acrc1 = terms.Acrc1;
            double acrcShort = terms.ShortTotal;
            variables["psiL"] = share;
            variables["Ml"] = share * moment;
            variables["Nl"] = share * load.N;
            variables["sigma_s_l"] = terms.Long?.Sigma_s_MPa ?? 0.0;
            variables["psi_s_l"] = terms.Long?.Psi_s ?? 1.0;
            variables["acrc1"] = acrc1;
            variables["acrc2"] = strip.Acrc_mm;
            variables["acrc3"] = terms.Acrc3;
            variables["acrcLimShortMm"] = options.AcrcLimShortMm;
            details =
            [
                new CheckDetail
                {
                    Formula = "(8.119)",
                    Description = "Sp63CrackWidth_AcrcLongCheck",
                    NormReference = "8.2.6, 8.2.7",
                    Applied = acrc1,
                    Allowable = options.AcrcLimMm,
                    Variables = new Dictionary<string, double>
                    {
                        ["acrc1"] = acrc1,
                        ["Ml"] = share * moment,
                        ["sigma_s_l"] = variables["sigma_s_l"],
                        ["psi_s_l"] = variables["psi_s_l"],
                        ["phi1"] = 1.4
                    }
                },
                new CheckDetail
                {
                    Formula = "(8.120)",
                    Description = "Sp63CrackWidth_AcrcShortCheck",
                    NormReference = "8.2.6, 8.2.7",
                    Applied = acrcShort,
                    Allowable = options.AcrcLimShortMm,
                    Variables = new Dictionary<string, double>
                    {
                        ["acrc1"] = acrc1,
                        ["acrc2"] = strip.Acrc_mm,
                        ["acrc3"] = terms.Acrc3,
                        ["sigma_s"] = strip.Sigma_s_MPa,
                        ["psi_s"] = strip.Psi_s
                    }
                }
            ];
            limitPassed = acrc1 <= options.AcrcLimMm + 1e-9 &&
                acrcShort <= options.AcrcLimShortMm + 1e-9;
        }
        else
        {
            details =
            [
                new CheckDetail
                {
                    Formula = "(8.130)-(8.141)",
                    Description = "Sp63CrackWidth_AcrcCheck",
                    NormReference = "8.2.9-8.2.16",
                    Applied = strip.Acrc_mm,
                    Allowable = options.AcrcLimMm,
                    Variables = new Dictionary<string, double>(variables)
                }
            ];
            limitPassed = strip.Acrc_mm <= options.AcrcLimMm + 1e-9;
        }

        var informational = new List<Sp63CrackWidthMessage>
        {
            new("compression_zone_neutral_axis", Sp63CrackWidthMessageKind.Information,
                "8.2.28", "Sp63CrackWidth_NeutralAxisNote")
        };
        if (full)
            informational.Add(new Sp63CrackWidthMessage("long_term_share", Sp63CrackWidthMessageKind.Information,
                "8.2.5", "Sp63CrackWidth_LongTermShareNote"));
        if (oppositeGoverns)
            informational.Add(new Sp63CrackWidthMessage("through_tension_opposite_row",
                Sp63CrackWidthMessageKind.Information, "8.2.16", "Sp63CrackWidth_ThroughTensionOppositeRow"));
        if (!strip.Cracked)
            informational.Add(new Sp63CrackWidthMessage("not_cracked",
                Sp63CrackWidthMessageKind.Information, "8.2.11", "Sp63CrackWidth_NotCracked"));
        else if (full && terms.Long is not { Cracked: true })
            informational.Add(new Sp63CrackWidthMessage("long_term_not_cracked",
                Sp63CrackWidthMessageKind.Information, "8.2.4", "Sp63CrackWidth_LongTermNotCracked"));

        Sp63CurvatureResult? curvature = null;
        if (full)
        {
            // Mcrc полосы вычислен на единицу ширины — переводится на полную ширину b.
            double mcrcFull = strip.Mcrc * b;
            double mcrcLong = (terms.Long?.Mcrc ?? strip.Mcrc) * b;
            curvature = Sp63Curvature.Compute(
                new Sp63CurvatureInput(b, profile.Height, profile.H0, profile.APrime,
                    profile.TensionLayer.Area, profile.CompressionLayer.Area,
                    concreteChars.E, Math.Abs(concreteChars.Fc), rebarChars.E,
                    concreteChars.Class, options.Humidity),
                absMoment, load.N, share, strip.Cracked, mcrcFull, mcrcLong);
            if (curvature is null)
                informational.Add(new Sp63CrackWidthMessage("curvature_not_computed",
                    Sp63CrackWidthMessageKind.Information, "8.2.23",
                    concreteChars.Class > 0
                        ? "Sp63CrackWidth_CurvatureThroughTension"
                        : "Sp63CrackWidth_CurvatureNoConcreteClass"));
            else
                informational.Add(new Sp63CrackWidthMessage("curvature_note",
                    Sp63CrackWidthMessageKind.Information, "8.2.25",
                    "Sp63CrackWidth_CurvatureNote"));
        }

        return new Sp63CrackWidthResult
        {
            Status = Sp63CrackWidthStatus.Calculated,
            LimitPassed = limitPassed,
            Cracked = strip.Cracked,
            Branch = strip.Cracked ? "cracked" : "not_cracked",
            Details = details,
            InformationalMessages = informational,
            Variables = variables,
            Curvature = curvature
        };
    }

    /// <summary>
    /// Составляющие ширины раскрытия одного ряда арматуры: <paramref name="Main"/> — от полной
    /// нагрузки (acrc2 в полном режиме, единственная составляющая в одиночном);
    /// <paramref name="Long"/>/<paramref name="LongShort"/> — от длительной части при φ1 = 1,4
    /// (acrc1) и φ1 = 1,0 (acrc3), отсутствуют в одиночном режиме и при ψ = 0.
    /// </summary>
    sealed record CrackTerms(ShellSimplStripResult Main, ShellSimplStripResult? Long,
        ShellSimplStripResult? LongShort)
    {
        public double Acrc1 => Long?.Acrc_mm ?? 0.0;
        public double Acrc3 => LongShort?.Acrc_mm ?? 0.0;

        /// <summary>Непродолжительное раскрытие acrc1 + acrc2 − acrc3, ф. (8.120).</summary>
        public double ShortTotal => Acrc1 + Main.Acrc_mm - Acrc3;

        /// <summary>Величина, по которой сравниваются ряды арматуры.</summary>
        public double Governing(bool full) => full ? ShortTotal : Main.Acrc_mm;
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
