namespace CScore.Sp63.Normal;

/// <summary>
/// Проверяет прочность бетонного (без рабочей арматуры) прямоугольного сечения по
/// разделу 7 СП 63.13330.2018: внецентренное сжатие (пп. 7.1.7–7.1.11) и изгиб (п. 7.1.12).
/// Арматура сечения, если она есть, считается конструктивной и не учитывается (п. 7.1.6).
/// </summary>
public static class Sp63ConcreteNormalChecker
{
    /// <summary>Коэффициент γb3 к Rb бетонных конструкций (п. 6.1.12, перечисление б).</summary>
    public const double GammaB3 = 0.9;

    /// <summary>Предельная гибкость l0/h для условия (7.3).</summary>
    public const double MaxAltSlenderness = 20.0;

    /// <summary>
    /// Выполняет одноосную проверку бетонного прямоугольного сечения.
    /// </summary>
    /// <param name="section">Расчётное сечение.</param>
    /// <param name="load">Нормальная сила и моменты в конвенции OpenCS (сжатие — N &lt; 0).</param>
    /// <param name="calc">Вид расчёта для разрешения характеристик бетона.</param>
    /// <param name="options">Типизированные настройки формульного режима.</param>
    public static Sp63NormalResult Check(CrossSection section, LoadItem load, CalcType calc,
        Sp63NormalOptions options)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(load);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.MemberContext);

        if (options.ShapeKind != Sp63NormalShapeKind.Rectangular)
            return NotApplicable("concrete_shape_not_supported",
                "Sp63Concrete_ShapeNotSupported", "7.1.9");
        if (!Enum.IsDefined(options.Axis))
            return InvalidInput("invalid_axis", "Sp63Normal_InvalidAxis", "7.1");
        if (!double.IsFinite(load.N) || !double.IsFinite(load.Mx) || !double.IsFinite(load.My))
            return InvalidInput("non_finite_load", "Sp63Normal_NonFiniteLoad", "7.1");

        double moment = options.Axis == Sp63NormalAxis.Mx ? load.Mx : load.My;
        double otherMoment = options.Axis == Sp63NormalAxis.Mx ? load.My : load.Mx;
        if (Math.Abs(otherMoment) > Sp63NormalTolerances.Moment)
            return NotApplicable("biaxial_load", "Sp63Normal_BiaxialLoad", "7.1");

        var geometry = Sp63RectangularGeometryPolicy.Classify(section);
        if (!geometry.IsApplicable)
            return NotApplicable("unsupported_geometry", "Sp63Normal_GeometryNotSupported",
                "геометрическая policy OpenCS");
        var rect = geometry.Geometry!;
        double h = options.Axis == Sp63NormalAxis.Mx ? rect.Height : rect.Width;
        double b = options.Axis == Sp63NormalAxis.Mx ? rect.Width : rect.Height;

        var concreteChars = section.Areas.FirstOrDefault(area =>
                area.Category == AreaCategory.Region &&
                area.Material?.Type == MatType.Concrete)?.Material?.GetChars(calc);
        double rb0 = Math.Abs(concreteChars?.Fc ?? 0.0);
        double rbt = Math.Abs(concreteChars?.Ft ?? 0.0);
        if (!IsFinitePositive(rb0))
            return NotApplicable("missing_concrete_resistance",
                "Sp63Normal_MissingConcreteResistance", "7.1.9");

        var common = new Dictionary<string, double>
        {
            ["N"] = load.N,
            ["M"] = moment,
            ["b"] = b,
            ["h"] = h,
            ["RbTable"] = rb0,
            ["gammaB3"] = GammaB3,
            ["Rb"] = rb0 * GammaB3,
            ["Rbt"] = rbt
        };
        var notes = new List<Sp63NormalMessage>
        {
            Message("concrete_gamma_b3", Sp63NormalMessageKind.Information, "6.1.12",
                "Sp63Concrete_GammaB3")
        };
        if (section.Areas.Any(area => area.Category == AreaCategory.RebarGroup))
            notes.Add(Message("concrete_rebar_ignored", Sp63NormalMessageKind.Information,
                "7.1.6", "Sp63Concrete_RebarIgnored"));

        if (load.N < -Sp63NormalTolerances.Force)
            return CheckCompression(section, Math.Abs(load.N), moment, calc, options,
                b, h, rbt, common, notes);
        if (load.N > Sp63NormalTolerances.Force)
            return NotApplicable("concrete_tension_not_supported",
                "Sp63Concrete_TensionNotSupported", "7.1.1");
        if (Math.Abs(moment) > Sp63NormalTolerances.Moment)
            return CheckBending(moment, b, h, rbt, common, notes);
        return NotApplicable("zero_load", "Sp63Normal_ZeroLoad", "7.1");
    }

    /// <summary>
    /// Коэффициент φ условия (7.3): при длительном действии нагрузки — по таблице 7.1
    /// (l0/h = 6; 10; 15; 20 → 0,92; 0,9; 0,8; 0,6) с линейной интерполяцией, при
    /// кратковременном — по линейному закону через φ = 0,9 при l0/h = 10 и φ = 0,85
    /// при l0/h = 20. Ниже l0/h = 6 принимается значение при 6 (начало таблицы 7.1).
    /// </summary>
    /// <param name="l0OverH">Гибкость l0/h, не более 20.</param>
    /// <param name="longTerm">Длительное действие нагрузки.</param>
    public static double Phi(double l0OverH, bool longTerm)
    {
        double lambda = Math.Max(l0OverH, 6.0);
        if (!longTerm)
            return 0.9 - 0.005 * (lambda - 10.0);
        double[] slenderness = [6.0, 10.0, 15.0, 20.0];
        double[] phi = [0.92, 0.9, 0.8, 0.6];
        for (int i = 1; i < slenderness.Length; i++)
        {
            if (lambda <= slenderness[i])
            {
                double t = (lambda - slenderness[i - 1]) / (slenderness[i] - slenderness[i - 1]);
                return phi[i - 1] + t * (phi[i] - phi[i - 1]);
            }
        }
        return phi[^1];
    }

    static Sp63NormalResult CheckCompression(CrossSection section, double n, double moment,
        CalcType calc, Sp63NormalOptions options, double b, double h, double rbt,
        Dictionary<string, double> common, List<Sp63NormalMessage> notes)
    {
        var context = options.MemberContext;
        if (context.ElementLengthOrRestraintDistance is not > 0)
            return NotApplicable("missing_accidental_eccentricity_length",
                "Sp63Normal_MissingAccidentalEccentricityLength", "7.1.7");
        if (context.StabilityMode == Sp63NormalStabilityMode.Member &&
            context.EffectiveLengthL0 is not > 0)
            return NotApplicable("missing_effective_length",
                "Sp63Normal_MissingEffectiveLength", "7.1.11");

        double rb = common["Rb"];
        double ea = Sp63MemberContext.AccidentalEccentricity(
            context.ElementLengthOrRestraintDistance!.Value, h);
        double e0Static = Math.Abs(moment) / n;
        double e0 = Sp63MemberContext.EffectiveEccentricity(e0Static, ea,
            context.StructuralScheme);
        var variables = new Dictionary<string, double>(common)
        {
            ["N"] = -n,
            ["ea"] = ea,
            ["e0Static"] = e0Static,
            ["e0"] = e0
        };
        notes.Add(Message("accidental_eccentricity", Sp63NormalMessageKind.Information,
            "7.1.7", "Sp63Normal_AccidentalEccentricity"));

        EccentricityAmplifier.EtaResult? etaResult = null;
        if (context.StabilityMode == Sp63NormalStabilityMode.Member)
        {
            var split = section.SplitStiffnessByMaterial();
            double eiConcrete = options.Axis == Sp63NormalAxis.Mx
                ? split.EIxConcrete
                : split.EIyConcrete;
            // D по п. 8.1.15 без учёта арматуры (п. 7.1.11): ks·Es·Is = 0.
            etaResult = EccentricityAmplifier.AmplifyFormula(
                n: -n,
                m0: n * e0,
                l0: context.EffectiveLengthL0!.Value,
                h: h,
                i: h / Math.Sqrt(12.0),
                eiConcrete: eiConcrete,
                eiRebar: 0.0,
                psi: context.Psi,
                slendernessThreshold: context.SlendernessThreshold);
            if (double.IsFinite(etaResult.Value.Ncr))
                variables["etaNcr"] = etaResult.Value.Ncr;
            if (!etaResult.Value.Stable)
            {
                variables["eta"] = 0.0;
                var stability = Detail("(7.7)", "Sp63Normal_StabilityCheck", "7.1.11",
                    n, etaResult.Value.Ncr, variables);
                notes.Add(Message("unstable_element", Sp63NormalMessageKind.Warning,
                    "7.1.11", "Sp63Normal_UnstableElement"));
                var unstable = Calculated("concrete_compression", [stability], variables, notes);
                unstable.StrengthPassed = false;
                unstable.Eta = etaResult;
                return unstable;
            }
        }
        else
        {
            notes.Add(Message("stability_excluded_explicitly", Sp63NormalMessageKind.Information,
                "7.1.8", "Sp63Normal_StabilityExcludedExplicitly"));
        }

        double eta = etaResult?.Eta ?? 1.0;
        double eEta = e0 * eta;
        variables["eta"] = eta;
        variables["e0Eta"] = eEta;

        Sp63NormalResult result;
        if (2.0 * eEta < h)
        {
            // Сила в пределах сечения: (7.1) без учёта растянутой зоны, Ab по (7.2).
            double ab = b * h * (1.0 - 2.0 * eEta / h);
            double nUlt = rb * ab;
            variables["Ab"] = ab;
            variables["Nult"] = nUlt;
            var detail = Detail("(7.1)", "Sp63Concrete_CompressionCheck", "7.1.9",
                n, nUlt, variables);
            result = Calculated("concrete_compression", [detail], variables, notes);
            AddAlternativeCheck(result, n, e0, h, b, rb, calc, context);
        }
        else
        {
            // Сила за пределами сечения: (7.5) с учётом растянутой зоны (п. 7.1.10).
            if (!IsFinitePositive(rbt))
                return NotApplicable("missing_concrete_tensile_resistance",
                    "Sp63Concrete_MissingTensileResistance", "7.1.10");
            double nUlt = rbt * b * h / (6.0 * eEta / h - 1.0);
            variables["Nult"] = nUlt;
            var detail = Detail("(7.5)", "Sp63Concrete_CompressionOutsideCheck", "7.1.10",
                n, nUlt, variables);
            result = Calculated("concrete_compression_outside", [detail], variables, notes);
        }
        result.Eta = etaResult;
        return result;
    }

    /// <summary>
    /// Справочное условие (7.3) N ≤ φ·Rb·A при e0 ≤ h/30 и l0 ≤ 20h; в вердикт не входит.
    /// </summary>
    static void AddAlternativeCheck(Sp63NormalResult result, double n, double e0, double h,
        double b, double rb, CalcType calc, Sp63MemberContext context)
    {
        // Относительный допуск: при e0 = ea = h/30 условие выполняется точно.
        if (e0 > h / 30.0 * (1.0 + 1e-9) || context.EffectiveLengthL0 is not > 0 ||
            context.EffectiveLengthL0.Value / h > MaxAltSlenderness * (1.0 + 1e-9))
        {
            result.InformationalMessages.Add(Message("concrete_alt_not_applied",
                Sp63NormalMessageKind.Information, "7.1.9", "Sp63Concrete_AltNotApplied"));
            return;
        }

        double l0OverH = context.EffectiveLengthL0.Value / h;
        bool longTerm = calc is CalcType.CL or CalcType.NL;
        double phi = Phi(l0OverH, longTerm);
        double area = b * h;
        double nUlt = phi * rb * area;
        var variables = new Dictionary<string, double>
        {
            ["N"] = n,
            ["e0"] = e0,
            ["h"] = h,
            ["l0OverH"] = l0OverH,
            ["phi"] = phi,
            ["phiLongTerm"] = longTerm ? 1.0 : 0.0,
            ["Rb"] = rb,
            ["A"] = area,
            ["Nult"] = nUlt
        };
        result.AlternativeChecks.Add(Detail("(7.3)", "Sp63Concrete_AltCompressionCheck",
            "7.1.9", n, nUlt, variables));
    }

    static Sp63NormalResult CheckBending(double moment, double b, double h, double rbt,
        Dictionary<string, double> common, List<Sp63NormalMessage> notes)
    {
        if (!IsFinitePositive(rbt))
            return NotApplicable("missing_concrete_tensile_resistance",
                "Sp63Concrete_MissingTensileResistance", "7.1.12");
        double w = b * h * h / 6.0;
        double mUlt = rbt * w;
        var variables = new Dictionary<string, double>(common)
        {
            ["W"] = w,
            ["Mult"] = mUlt
        };
        var detail = Detail("(7.8)", "Sp63Concrete_BendingCheck", "7.1.12",
            Math.Abs(moment), mUlt, variables);
        return Calculated("concrete_bending", [detail], variables, notes);
    }

    static CheckDetail Detail(string formula, string description, string reference,
        double applied, double allowable, Dictionary<string, double> variables) =>
        new()
        {
            Formula = formula,
            Description = description,
            NormReference = reference,
            Applied = Math.Max(0.0, applied),
            Allowable = allowable,
            Variables = new Dictionary<string, double>(variables)
        };

    static Sp63NormalResult Calculated(string branch, List<CheckDetail> details,
        Dictionary<string, double> variables, List<Sp63NormalMessage> notes) =>
        new()
        {
            Status = Sp63NormalStatus.Calculated,
            StrengthPassed = details.All(detail => detail.Passed),
            Branch = branch,
            StrengthDetails = details,
            Variables = variables,
            TraceSteps = Sp63NormalTraceBuilder.Build(branch, details, variables),
            InformationalMessages = notes
        };

    static Sp63NormalResult NotApplicable(string code, string text, string reference) =>
        new()
        {
            Status = Sp63NormalStatus.NotApplicable,
            Branch = "not_applicable",
            ApplicabilityMessages =
                [Message(code, Sp63NormalMessageKind.Applicability, reference, text)]
        };

    static Sp63NormalResult InvalidInput(string code, string text, string reference) =>
        new()
        {
            Status = Sp63NormalStatus.InvalidInput,
            Branch = "invalid_input",
            ApplicabilityMessages =
                [Message(code, Sp63NormalMessageKind.Applicability, reference, text)]
        };

    static Sp63NormalMessage Message(string code, Sp63NormalMessageKind kind,
        string reference, string text) => new(code, kind, reference, text);

    static bool IsFinitePositive(double value) => double.IsFinite(value) && value > 0;
}
