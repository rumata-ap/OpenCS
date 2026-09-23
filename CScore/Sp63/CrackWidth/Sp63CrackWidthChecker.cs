using CScore.Sp63.Normal;

namespace CScore.Sp63.CrackWidth;

/// <summary>
/// Проверяет ширину раскрытия трещин нормального сечения упрощённым (не деформационно-модельным)
/// путём по п. 8.2.9–8.2.16 СП 63 для прямоугольных, тавровых и двутавровых сечений. Геометрия
/// распознаётся общей фабрикой <see cref="Sp63SlsSectionGeometryFactory"/>, все формульные
/// величины (Mcrc, σs, ψs, ls, acrc) считает общий <see cref="Sp63SlsSectionSolver"/>, кривизна
/// (режим LongAndShort) — тот же профиль через <see cref="Sp63Curvature"/>. Checker остаётся
/// оркестратором: применимость → составляющие (8.119)/(8.120) → выбор противоположного ряда
/// арматуры при сквозном растяжении → вердикт и <see cref="CheckDetail"/>.
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

        if (options.ShapeKind is not (Sp63NormalShapeKind.Rectangular or Sp63NormalShapeKind.Tee))
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
            return NotApplicable("biaxial_load", "Sp63CrackWidth_BiaxialLoad", "8.2.15");
        if (Math.Abs(moment) <= MomentTolerance)
            return NotApplicable("zero_moment", "Sp63CrackWidth_ZeroMoment", "8.2");

        int tensionDirection = Math.Sign(moment);
        if (!Sp63SlsSectionGeometryFactory.TryCreate(section, options.ShapeKind, options.Axis,
                calc, tensionDirection, out var geometry, out var geometryMessages))
            return NotApplicable(geometryMessages);

        var concreteArea = section.Areas.First(area =>
            area.Category == AreaCategory.Region && area.Material?.Type == MatType.Concrete);
        var concreteChars = concreteArea.Material!.GetChars(calc)!;
        var rebarArea = section.Areas.First(area =>
            area.Category == AreaCategory.RebarGroup && area.Material != null);
        var rebarChars = rebarArea.Material!.GetChars(calc)!;

        double absMoment = Math.Abs(moment);
        bool full = options.Mode == Sp63CrackWidthMode.LongAndShort;
        double share = full ? options.LongTermShare : 0.0;

        // Одна составляющая acrc,i (п. 8.2.15) от момента m и силы n с коэффициентом φ1
        // общим solver'ом по ориентированному профилю.
        Sp63SlsCrackTermResult Term(Sp63SlsSectionGeometry g, double m, double n, double phi1Term,
                bool opposite = false) =>
            Sp63SlsSectionSolver.ComputeCrackTerm(g, concreteChars, rebarChars, m, n,
                phi1Term, options.Phi2, options.AcrcLimMm, options.SigmaSCrc, options.WplGamma,
                opposite);

        // Полный режим (п. 8.2.7): acrc2 — полная нагрузка при φ1 = 1,0; acrc1/acrc3 —
        // длительная часть ψ·(N, M) при φ1 = 1,4 и 1,0. Одиночный — одна составляющая с φ1.
        CrackTerms Evaluate(Sp63SlsSectionGeometry g, bool opposite = false)
        {
            var main = Term(g, absMoment, load.N, full ? 1.0 : options.Phi1, opposite);
            if (!full || share <= 0.0) return new CrackTerms(main, null, null, g);
            return new CrackTerms(main,
                Term(g, share * absMoment, share * load.N, 1.4, opposite),
                Term(g, share * absMoment, share * load.N, 1.0, opposite), g);
        }

        var terms = Evaluate(geometry!);

        // При x_m ≤ 0 сечение растянуто насквозь: растянут и ряд, который момент не растягивает,
        // и у его грани тоже есть трещина. При несимметричном армировании решает он — так же,
        // как в плитной проверке Капра-Мори (ShellSimplSolver.DirectionStrip). Ряд выбирается
        // по полной нагрузке и сохраняется для всех составляющих: x_m от доли ψ не зависит.
        bool oppositeGoverns = false;
        if (terms.Main.Xm <= 0.0)
        {
            if (Sp63SlsSectionGeometryFactory.TryCreate(section, options.ShapeKind, options.Axis,
                    calc, -tensionDirection, out var oppositeGeometry, out _))
            {
                var opposite = Evaluate(oppositeGeometry!, opposite: true);
                if (opposite.Governing(full) > terms.Governing(full))
                {
                    terms = opposite;
                    oppositeGoverns = true;
                }
            }
        }

        var strip = terms.Main;
        double ds = terms.Geometry.TensionLayer.Diameter;
        double b = geometry!.Area(0.0, geometry.Height) / geometry.Height;

        var variables = new Dictionary<string, double>
        {
            ["N"] = load.N,
            ["M"] = moment,
            ["tensionDirection"] = oppositeGoverns ? -tensionDirection : tensionDirection,
            ["b"] = b,
            ["h"] = geometry.Height,
            ["h0"] = terms.Geometry.TensionLayer.Coordinate,
            ["aPrime"] = terms.Geometry.CompressionLayer.Coordinate,
            ["As"] = terms.Geometry.TensionLayer.Area,
            ["AsPrime"] = terms.Geometry.CompressionLayer.Area,
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
            ["sigma_s"] = strip.SigmaS / 1000.0,
            ["psi_s"] = strip.PsiS,
            ["ls"] = strip.Ls,
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
            variables["sigma_s_l"] = (terms.Long?.SigmaS ?? 0.0) / 1000.0;
            variables["psi_s_l"] = terms.Long?.PsiS ?? 1.0;
            variables["acrc1"] = acrc1;
            variables["acrc2"] = strip.Acrc;
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
                        ["acrc2"] = strip.Acrc,
                        ["acrc3"] = terms.Acrc3,
                        ["sigma_s"] = variables["sigma_s"],
                        ["psi_s"] = strip.PsiS
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
                    Applied = strip.Acrc,
                    Allowable = options.AcrcLimMm,
                    Variables = new Dictionary<string, double>(variables)
                }
            ];
            limitPassed = strip.Acrc <= options.AcrcLimMm + 1e-9;
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
            // Mcrc общего solver'а уже по полной геометрии — кривизна считается тем же профилем.
            double mcrcFull = strip.Mcrc;
            double mcrcLong = terms.Long?.Mcrc ?? strip.Mcrc;
            curvature = Sp63Curvature.Compute(
                new Sp63CurvatureSectionInput(geometry, concreteChars.E,
                    Math.Abs(concreteChars.Fc), rebarChars.E,
                    concreteChars.Class, options.Humidity),
                new Sp63CurvatureLoad(absMoment, load.N),
                new Sp63CurvatureLoad(share * absMoment, share * load.N),
                strip.Cracked, mcrcFull, mcrcLong);
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
    /// Составляющие ширины раскрытия одного ряда арматуры: <see cref="Main"/> — от полной
    /// нагрузки (acrc2 в полном режиме, единственная составляющая в одиночном);
    /// <see cref="Long"/>/<see cref="LongShort"/> — от длительной части при φ1 = 1,4 (acrc1)
    /// и φ1 = 1,0 (acrc3), отсутствуют в одиночном режиме и при ψ = 0.
    /// </summary>
    sealed record CrackTerms(Sp63SlsCrackTermResult Main, Sp63SlsCrackTermResult? Long,
        Sp63SlsCrackTermResult? LongShort, Sp63SlsSectionGeometry Geometry)
    {
        public double Acrc1 => Long?.Acrc ?? 0.0;
        public double Acrc3 => LongShort?.Acrc ?? 0.0;

        /// <summary>Непродолжительное раскрытие acrc1 + acrc2 − acrc3, ф. (8.120).</summary>
        public double ShortTotal => Acrc1 + Main.Acrc - Acrc3;

        /// <summary>Величина, по которой сравниваются ряды арматуры.</summary>
        public double Governing(bool full) => full ? ShortTotal : Main.Acrc;
    }

    /// <summary>
    /// Причины неприменимости геометрии и раскладки арматуры распознаёт общий
    /// <see cref="Sp63SlsSectionGeometryFactory"/>, поэтому коды сохраняются, а ключи локализации
    /// приводятся к задачным: «прямоугольный контур, выбранный как tee», недостаток характеристик.
    /// </summary>
    static Sp63CrackWidthMessage MapGeometryMessage(Sp63NormalMessage message) => new(
        message.Code,
        Sp63CrackWidthMessageKind.Applicability,
        message.NormReference,
        message.Code switch
        {
            "not_a_tee_shape" => "Sp63CrackWidth_TeeGeometryNotSupported",
            "missing_concrete_chars" => "Sp63CrackWidth_MissingConcreteChars",
            "missing_rebar_chars" => "Sp63CrackWidth_MissingRebarChars",
            _ => message.Text
        });

    static Sp63CrackWidthResult InvalidInput(string code, string text, string reference) => new()
    {
        Status = Sp63CrackWidthStatus.InvalidInput,
        Branch = "invalid_input",
        ApplicabilityMessages = [Message(code, Sp63CrackWidthMessageKind.Applicability, reference, text)]
    };

    static Sp63CrackWidthResult NotApplicable(string code, string text, string reference) =>
        NotApplicable([Message(code, Sp63CrackWidthMessageKind.Applicability, reference, text)]);

    static Sp63CrackWidthResult NotApplicable(IEnumerable<Sp63NormalMessage> messages)
    {
        // Причины геометрии и раскладки арматуры приходят из общей фабрики профиля; коды
        // сохраняются, ключи локализации приводятся к задачным (см. MapGeometryMessage).
        return NotApplicable(messages.Select(MapGeometryMessage).ToList());
    }

    static Sp63CrackWidthResult NotApplicable(List<Sp63CrackWidthMessage> messages) => new()
    {
        Status = Sp63CrackWidthStatus.NotApplicable,
        Branch = "not_applicable",
        ApplicabilityMessages = messages,
        InformationalMessages = SuggestFullModelIfNeeded(messages)
    };

    static readonly HashSet<string> FullModelSuggestionCodes =
    [
        "unsupported_geometry", "biaxial_load", "mixed_rebar_resistance",
        "insufficient_rebar_layers", "extra_rebar_layers", "non_point_rebar",
        "prestressed_rebar", "zero_moment", "not_a_tee_shape"
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
