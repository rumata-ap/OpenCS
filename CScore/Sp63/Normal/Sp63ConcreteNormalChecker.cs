namespace CScore.Sp63.Normal;

/// <summary>
/// Проверяет прочность бетонного (без рабочей арматуры) прямоугольного, таврового или
/// двутаврового сечения по разделу 7 СП 63.13330.2018 (п. 7.1.2 — действие усилий в плоскости
/// симметрии): внецентренное сжатие (пп. 7.1.7–7.1.11), в том числе условие (7.4) для
/// элементов, где трещины не допускаются, и изгиб (п. 7.1.12). Арматура сечения, если она
/// есть, считается конструктивной и не учитывается (п. 7.1.6).
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

        if (options.ShapeKind is not (Sp63NormalShapeKind.Rectangular or Sp63NormalShapeKind.Tee))
            return NotApplicable("concrete_shape_not_supported",
                "Sp63Concrete_ShapeNotSupported", "7.1.2");
        if (!Enum.IsDefined(options.Axis))
            return InvalidInput("invalid_axis", "Sp63Normal_InvalidAxis", "7.1");
        if (!double.IsFinite(load.N) || !double.IsFinite(load.Mx) || !double.IsFinite(load.My))
            return InvalidInput("non_finite_load", "Sp63Normal_NonFiniteLoad", "7.1");

        double moment = options.Axis == Sp63NormalAxis.Mx ? load.Mx : load.My;
        double otherMoment = options.Axis == Sp63NormalAxis.Mx ? load.My : load.Mx;
        if (Math.Abs(otherMoment) > Sp63NormalTolerances.Moment)
            return NotApplicable("biaxial_load", "Sp63Normal_BiaxialLoad", "7.1");

        // Профиль, ориентированный от сжатой грани: compressedAtMin — сжата грань с меньшей
        // координатой высоты (положительный момент растягивает грань с большей координатой).
        Func<bool, Sp63ConcreteBandProfile> profileFor;
        bool isTee = options.ShapeKind == Sp63NormalShapeKind.Tee;
        if (isTee)
        {
            var tee = Sp63TeeGeometryPolicy.Classify(section, options.Axis);
            if (!tee.IsApplicable)
            {
                bool notATee = tee.Failure == Sp63TeeGeometryFailure.NotATeeShape;
                return NotApplicable(notATee ? "not_a_tee_shape" : "unsupported_geometry",
                    notATee ? "Sp63Normal_NotATeeShape" : "Sp63Normal_TeeGeometryNotSupported",
                    "7.1.2");
            }
            var teeGeometry = tee.Geometry!;
            profileFor = compressedAtMin => Sp63ConcreteBandProfile.Tee(teeGeometry, compressedAtMin);
        }
        else
        {
            var geometry = Sp63RectangularGeometryPolicy.Classify(section);
            if (!geometry.IsApplicable)
                return NotApplicable("unsupported_geometry", "Sp63Normal_GeometryNotSupported",
                    "геометрическая policy OpenCS");
            var rect = geometry.Geometry!;
            double hRect = options.Axis == Sp63NormalAxis.Mx ? rect.Height : rect.Width;
            double bRect = options.Axis == Sp63NormalAxis.Mx ? rect.Width : rect.Height;
            var rectangle = Sp63ConcreteBandProfile.Rectangle(bRect, hRect);
            profileFor = _ => rectangle;
        }
        var reference = profileFor(true);
        double h = reference.Height;

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
            ["h"] = h,
            ["A"] = reference.Area,
            ["RbTable"] = rb0,
            ["gammaB3"] = GammaB3,
            ["Rb"] = rb0 * GammaB3,
            ["Rbt"] = rbt
        };
        if (isTee)
            common["I"] = reference.Inertia;
        else
            common["b"] = reference.Bands[0].Width;
        var notes = new List<Sp63NormalMessage>
        {
            Message("concrete_gamma_b3", Sp63NormalMessageKind.Information, "6.1.12",
                "Sp63Concrete_GammaB3")
        };
        if (section.Areas.Any(area => area.Category == AreaCategory.RebarGroup))
            notes.Add(Message("concrete_rebar_ignored", Sp63NormalMessageKind.Information,
                "7.1.6", "Sp63Concrete_RebarIgnored"));
        if (isTee)
            notes.Add(Message("concrete_tee_full_flanges", Sp63NormalMessageKind.Information,
                "7.1.9", "Sp63Concrete_TeeFullFlanges"));

        var shape = new Shape(isTee, profileFor);
        if (load.N < -Sp63NormalTolerances.Force)
            return CheckCompression(section, Math.Abs(load.N), moment, calc, options,
                shape, rbt, common, notes);
        if (load.N > Sp63NormalTolerances.Force)
            return NotApplicable("concrete_tension_not_supported",
                "Sp63Concrete_TensionNotSupported", "7.1.1");
        if (Math.Abs(moment) > Sp63NormalTolerances.Moment)
            return CheckBending(moment, shape, rbt, common, notes);
        return NotApplicable("zero_load", "Sp63Normal_ZeroLoad", "7.1");
    }

    /// <summary>Форма сечения: признак тавра и профиль для заданной сжатой грани.</summary>
    sealed record Shape(bool IsTee, Func<bool, Sp63ConcreteBandProfile> ProfileFor);

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
        CalcType calc, Sp63NormalOptions options, Shape shape, double rbt,
        Dictionary<string, double> common, List<Sp63NormalMessage> notes)
    {
        var context = options.MemberContext;
        var reference = shape.ProfileFor(true);
        double h = reference.Height;
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
                i: reference.RadiusOfGyration,
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

        // Сжатая грань — по знаку момента. При M = 0 (только случайный эксцентриситет)
        // направление не определено: у тавра проверяются обе грани, выводится худшая.
        bool[] orientations = Math.Abs(moment) > Sp63NormalTolerances.Moment
            ? [moment > 0]
            : shape.IsTee ? [true, false] : [true];
        Evaluation? governing = null;
        foreach (bool compressedAtMin in orientations)
        {
            var evaluation = EvaluateCompression(shape.ProfileFor(compressedAtMin), shape.IsTee,
                n, eEta, rb, rbt, options.CracksNotAllowed, variables);
            if (evaluation.Failure is not null)
                return evaluation.Failure;
            if (governing is null || evaluation.MaxRatio > governing.MaxRatio)
                governing = evaluation;
        }
        if (orientations.Length == 2)
            notes.Add(Message("concrete_tee_both_faces", Sp63NormalMessageKind.Information,
                "7.1.7", "Sp63Concrete_TeeBothFaces"));
        notes.AddRange(governing!.Notes);

        var result = Calculated(governing.Branch, governing.Details, governing.Variables, notes);
        if (governing.Branch == "concrete_compression" && !shape.IsTee)
            AddAlternativeCheck(result, n, e0, h, reference.Bands[0].Width, rb, calc, context);
        result.Eta = etaResult;
        return result;
    }

    /// <summary>Результат проверки сжатия для одной ориентации профиля.</summary>
    sealed record Evaluation(string Branch, List<CheckDetail> Details,
        Dictionary<string, double> Variables, List<Sp63NormalMessage> Notes,
        Sp63NormalResult? Failure = null)
    {
        public double MaxRatio => Details.Count == 0 ? 0.0 : Details.Max(detail => detail.Ratio);
    }

    /// <summary>
    /// Внецентренное сжатие при заданной сжатой грани: в пределах сечения — (7.1) и, если
    /// трещины не допускаются, (7.4)/(7.5); за пределами сечения — (7.4)/(7.5) (п. 7.1.10).
    /// Для прямоугольника (7.4) принимает вид (7.5), Ab — вид (7.2).
    /// </summary>
    static Evaluation EvaluateCompression(Sp63ConcreteBandProfile profile, bool isTee,
        double n, double eEta, double rb, double rbt, bool cracksNotAllowed,
        Dictionary<string, double> baseVariables)
    {
        var variables = new Dictionary<string, double>(baseVariables);
        var details = new List<CheckDetail>();
        var notes = new List<Sp63NormalMessage>();
        double yc = profile.Centroid;
        double yt = profile.TensionFiberDistance;
        // (7.4): N ≤ Rbt·A / (A/I·e0·η·yt − 1); у прямоугольника A/I·yt = 6/h — это (7.5).
        double tensionDenominator = profile.Area / profile.Inertia * eEta * yt - 1.0;
        string tensionFormula = isTee ? "(7.4)" : "(7.5)";
        if (isTee)
        {
            variables["yc"] = yc;
            variables["yt"] = yt;
        }

        if (eEta < yc)
        {
            // Сила в пределах сечения: (7.1), центр тяжести Ab — в точке приложения силы.
            double ab;
            if (isTee)
            {
                ab = profile.CompressedZoneArea(yc - eEta, out double zoneHeight);
                variables["xZone"] = zoneHeight;
            }
            else
            {
                double h = profile.Height;
                ab = profile.Bands[0].Width * h * (1.0 - 2.0 * eEta / h);
            }
            double nUlt = rb * ab;
            variables["Ab"] = ab;
            variables["Nult"] = nUlt;
            details.Add(Detail("(7.1)",
                isTee ? "Sp63Concrete_CompressionCheckTee" : "Sp63Concrete_CompressionCheck",
                "7.1.9", n, nUlt, variables));

            if (cracksNotAllowed)
            {
                if (tensionDenominator <= 0.0)
                {
                    // Сила в ядре сечения: растянутой зоны нет, (7.4) выполняется.
                    notes.Add(Message("concrete_no_tension_zone", Sp63NormalMessageKind.Information,
                        "7.1.9", "Sp63Concrete_NoTensionZone"));
                }
                else
                {
                    if (!IsFinitePositive(rbt))
                        return new Evaluation("", [], variables, notes, NotApplicable(
                            "missing_concrete_tensile_resistance",
                            "Sp63Concrete_MissingTensileResistance", "7.1.9"));
                    double nCrack = rbt * profile.Area / tensionDenominator;
                    variables["NultCrack"] = nCrack;
                    details.Add(Detail(tensionFormula,
                        isTee ? "Sp63Concrete_CrackFreeCheckTee" : "Sp63Concrete_CrackFreeCheck",
                        "7.1.9", n, nCrack, variables));
                }
            }
            return new Evaluation("concrete_compression", details, variables, notes);
        }

        // Сила за пределами сечения: только с учётом растянутой зоны (п. 7.1.10).
        if (!IsFinitePositive(rbt))
            return new Evaluation("", [], variables, notes, NotApplicable(
                "missing_concrete_tensile_resistance",
                "Sp63Concrete_MissingTensileResistance", "7.1.10"));
        if (tensionDenominator <= 0.0)
            return new Evaluation("", [], variables, notes, NotApplicable(
                "unsupported_geometry", "Sp63Normal_GeometryNotSupported", "7.1.10"));
        double nOutside = rbt * profile.Area / tensionDenominator;
        variables["Nult"] = nOutside;
        details.Add(Detail(tensionFormula,
            isTee ? "Sp63Concrete_CompressionOutsideCheckTee" : "Sp63Concrete_CompressionOutsideCheck",
            "7.1.10", n, nOutside, variables));
        return new Evaluation("concrete_compression_outside", details, variables, notes);
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

    static Sp63NormalResult CheckBending(double moment, Shape shape, double rbt,
        Dictionary<string, double> common, List<Sp63NormalMessage> notes)
    {
        if (!IsFinitePositive(rbt))
            return NotApplicable("missing_concrete_tensile_resistance",
                "Sp63Concrete_MissingTensileResistance", "7.1.12");
        var profile = shape.ProfileFor(moment > 0);
        var variables = new Dictionary<string, double>(common);
        double w;
        if (shape.IsTee)
        {
            // (7.9): W — для крайнего растянутого волокна, W = I/yt.
            w = profile.Inertia / profile.TensionFiberDistance;
            variables["yt"] = profile.TensionFiberDistance;
            notes.Add(Message("concrete_tee_shear_stress_not_checked",
                Sp63NormalMessageKind.Warning, "7.1.4", "Sp63Concrete_TeeShearStressNotChecked"));
        }
        else
        {
            double h = profile.Height;
            w = profile.Bands[0].Width * h * h / 6.0;
        }
        double mUlt = rbt * w;
        variables["W"] = w;
        variables["Mult"] = mUlt;
        var detail = Detail("(7.8)",
            shape.IsTee ? "Sp63Concrete_BendingCheckTee" : "Sp63Concrete_BendingCheck",
            "7.1.12", Math.Abs(moment), mUlt, variables);
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
