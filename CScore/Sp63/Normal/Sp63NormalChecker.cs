using CScore.Sp63;

namespace CScore.Sp63.Normal;

/// <summary>Проверяет прямоугольное нормальное сечение по формулам СП 63.</summary>
public static class Sp63NormalChecker
{
    // Коды сообщений о неприменимости, для которых стоит явно предложить переход
    // к НДМ: отверстия/несколько бетонных областей, двуосный изгиб, сложная арматура.
    internal static readonly HashSet<string> NdmSuggestionCodes =
    [
        "unsupported_geometry",
        "biaxial_load",
        "mixed_rebar_resistance",
        "insufficient_rebar_layers",
        "non_point_rebar",
        "prestressed_rebar",
        "unsupported_load_case_for_tee",
        "annular_radius_ratio",
        "circular_tension_not_supported",
        "circular_insufficient_bars",
        "circular_rebar_not_centered",
        "circular_rebar_unequal_areas",
        "circular_rebar_not_on_circle",
        "circular_rebar_non_uniform",
        "circular_rebar_class_above_a400"
    ];

    /// <summary>
    /// Выполняет одноосную проверку прямоугольного или таврового железобетонного сечения.
    /// </summary>
    /// <param name="section">Расчётное сечение.</param>
    /// <param name="load">Нормальная сила и моменты в конвенции OpenCS.</param>
    /// <param name="calc">Вид расчёта для разрешения характеристик материалов.</param>
    /// <param name="options">Типизированные настройки формульного режима.</param>
    /// <param name="spanLength">Пролёт для определения эффективной ширины сжатой полки тавра.</param>
    public static Sp63NormalResult Check(
        CrossSection section,
        LoadItem load,
        CalcType calc,
        Sp63NormalOptions options,
        double? spanLength = null)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(load);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.MemberContext);

        // Круг и кольцо осесимметричны: диспетчеризация до общей проверки biaxial_load.
        if (options.ShapeKind is Sp63NormalShapeKind.Circular or Sp63NormalShapeKind.Annular)
            return Sp63CircularNormalChecker.Check(section, load, calc, options);

        if (options.ShapeKind is not (Sp63NormalShapeKind.Rectangular or Sp63NormalShapeKind.Tee))
            return NotApplicable("unsupported_shape", "Sp63Normal_ShapeNotSupported", "8.1");
        if (!Enum.IsDefined(options.Axis))
            return InvalidInput("invalid_axis", "Sp63Normal_InvalidAxis", "8.1");
        if (!AreFinite(load.N, load.Mx, load.My))
            return InvalidInput("non_finite_load", "Sp63Normal_NonFiniteLoad", "8.1");

        double moment = options.Axis == Sp63NormalAxis.Mx ? load.Mx : load.My;
        double otherMoment = options.Axis == Sp63NormalAxis.Mx ? load.My : load.Mx;
        if (Math.Abs(otherMoment) > Sp63NormalTolerances.Moment)
            return NotApplicable("biaxial_load", "Sp63Normal_BiaxialLoad", "8.1");

        if (options.ShapeKind == Sp63NormalShapeKind.Tee)
            return Sp63TeeNormalChecker.Check(section, load, calc, options, spanLength);

        if (load.N < -Sp63NormalTolerances.Force)
            return CheckCompression(section, load.N, moment, calc, options);
        if (load.N > Sp63NormalTolerances.Force)
        {
            if (Math.Abs(moment) <= Sp63NormalTolerances.Moment)
                return CheckCentralTension(section, load.N, calc, options);
            return CheckEccentricTension(section, load.N, moment, calc, options);
        }
        if (Math.Abs(moment) > Sp63NormalTolerances.Moment)
            return CheckBending(section, moment, calc, options);

        return NotApplicable("zero_load", "Sp63Normal_ZeroLoad", "8.1");
    }

    static Sp63NormalResult CheckBending(CrossSection section, double moment,
        CalcType calc, Sp63NormalOptions options)
    {
        int tensionDirection = Math.Sign(moment);
        if (!TryBuildProfile(section, options.Axis, calc, tensionDirection,
                out var profile, out var messages))
            return NotApplicable(messages);
        if (!Sp63NormalMaterialResolver.TryResolve(section, calc,
                profile!.TensionLayer.Rs, profile.CompressionLayer.Rsc,
                out var material, out var materialMessage))
            return NotApplicable(materialMessage!);

        double x = Sp63NormalFormulas.BendingX(
            profile!.TensionLayer.Rs,
            profile.TensionLayer.Area,
            profile.CompressionLayer.Rsc,
            profile.CompressionLayer.Area,
            material.Rb,
            profile.B);
        if (!double.IsFinite(x) || x < -Sp63NormalTolerances.Force)
            return NotApplicable("invalid_compression_zone", "Sp63Normal_InvalidCompressionZone", "8.1.8");

        double xiR = Sp63NormalFormulas.XiR(material.Rs, material.Es, material.EpsilonB2);
        double xi = Math.Max(0.0, x) / profile.H0;
        bool symmetricBranch = x <= 2.0 * profile.APrime + Sp63NormalTolerances.Force;
        double allowable = symmetricBranch
            ? Sp63NormalFormulas.SymmetricMoment(
                material.Rs,
                profile.TensionLayer.Area,
                profile.H0,
                profile.APrime,
                profile.PrecomputedXWithoutCompressionRebar,
                compressionRebarWasExcluded: true)
            : Sp63NormalFormulas.BendingMoment(
                material.Rb,
                profile.B,
                x,
                profile.H0,
                profile.CompressionLayer.Rsc,
                profile.CompressionLayer.Area,
                profile.APrime);
        if (!IsFinitePositive(allowable))
            return NotApplicable("nonpositive_capacity", "Sp63Normal_NonpositiveCapacity", "8.1.8");

        var variables = CommonVariables(loadN: 0.0, moment, tensionDirection,
            profile, material, x, xi, xiR);
        variables["compressionRebarExcluded"] =
            profile.CompressionLayer.Area <= Sp63NormalTolerances.RebarArea ? 1.0 : 0.0;
        var informational = XiMessage(xi, xiR);
        if (symmetricBranch)
        {
            double effectiveAprime = profile.PrecomputedXWithoutCompressionRebar <
                                     2.0 * profile.APrime
                ? profile.PrecomputedXWithoutCompressionRebar / 2.0
                : profile.APrime;
            variables["symmetricBranchApplied"] = 1.0;
            variables["symmetricXWithoutCompressionRebar"] =
                profile.PrecomputedXWithoutCompressionRebar;
            variables["symmetricAprimeEffective"] = effectiveAprime;
            informational.Add(new Sp63NormalMessage(
                "symmetric_branch",
                Sp63NormalMessageKind.Information,
                "8.1.9",
                "Sp63Normal_SymmetricBranch"));
        }

        variables["symmetricBranchApplied"] = symmetricBranch ? 1.0 : 0.0;
        var detail = Detail(
            symmetricBranch ? "(8.9)" : "(8.4)",
            symmetricBranch ? "Sp63Normal_SymmetricBendingCheck" : "Sp63Normal_BendingCheck",
            symmetricBranch ? "8.1.9" : "8.1.8",
            Math.Abs(moment), allowable, variables);
        return Calculated("bending", [detail], variables, informational,
            profile!, options);
    }

    static Sp63NormalResult CheckCentralTension(CrossSection section, double n,
        CalcType calc, Sp63NormalOptions options)
    {
        if (!TryBuildProfile(section, options.Axis, calc, -1,
                out var profile, out var messages))
            return NotApplicable(messages);
        if (!Sp63NormalMaterialResolver.TryResolve(section, calc,
                profile!.TensionLayer.Rs, profile.CompressionLayer.Rsc,
                out var material, out var materialMessage))
            return NotApplicable(materialMessage!);

        double allowable = Sp63NormalFormulas.CentralTensionCapacity(
            material.Rs, profile!.TotalRebarArea);
        if (!IsFinitePositive(allowable))
            return NotApplicable("nonpositive_capacity", "Sp63Normal_NonpositiveCapacity", "8.1.18");

        var variables = new Dictionary<string, double>
        {
            ["N"] = n,
            ["centralTensionCapacity"] = allowable,
            ["totalRebarArea"] = profile.TotalRebarArea,
            ["Rs"] = material.Rs
        };
        var detail = Detail("(8.19)", "Sp63Normal_CentralTensionCheck", "8.1.18",
            n, allowable, variables);
        return Calculated("central_tension", [detail], variables, [],
            profile!, options);
    }

    static Sp63NormalResult CheckEccentricTension(CrossSection section, double n,
        double moment, CalcType calc, Sp63NormalOptions options)
    {
        int tensionDirection = Math.Sign(moment);
        if (!TryBuildProfile(section, options.Axis, calc, tensionDirection,
                out var profile, out var messages))
            return NotApplicable(messages);
        if (!Sp63NormalMaterialResolver.TryResolve(section, calc,
                profile!.TensionLayer.Rs, profile.CompressionLayer.Rsc,
                out var material, out var materialMessage))
            return NotApplicable(materialMessage!);

        double lineCoordinate = profile!.Height / 2.0 + Math.Abs(moment) / n;
        bool between = lineCoordinate >= profile.APrime - Sp63NormalTolerances.Moment &&
                       lineCoordinate <= profile.H0 + Sp63NormalTolerances.Moment;
        double xiR = Sp63NormalFormulas.XiR(material.Rs, material.Es, material.EpsilonB2);
        var variables = CommonVariables(n, moment, tensionDirection, profile,
            material, x: 0.0, xi: 0.0, xiR);
        variables["tensionLineCoordinate"] = lineCoordinate;

        if (between)
        {
            double e = Math.Max(0.0, profile.H0 - lineCoordinate);
            double ePrime = Math.Max(0.0, lineCoordinate - profile.APrime);
            double mUlt = material.Rs * profile.CompressionLayer.Area *
                          (profile.H0 - profile.APrime);
            double mPrimeUlt = material.Rs * profile.TensionLayer.Area *
                               (profile.H0 - profile.APrime);
            variables["tensionE"] = e;
            variables["tensionEPrime"] = ePrime;
            variables["tensionMUlt"] = mUlt;
            variables["tensionMPrimeUlt"] = mPrimeUlt;
            var first = Detail("(8.20)", "Sp63Normal_EccentricTensionCheck", "8.1.19",
                n * e, mUlt, variables);
            var second = Detail("(8.21)", "Sp63Normal_EccentricTensionPrimeCheck", "8.1.19",
                n * ePrime, mPrimeUlt, variables);
            return Calculated("eccentric_tension_between", [first, second], variables,
                XiMessage(0.0, xiR), profile!, options);
        }

        double rawX = Sp63NormalFormulas.TensionOutsideX(
            material.Rs,
            profile.TensionLayer.Area,
            profile.CompressionLayer.Rsc,
            profile.CompressionLayer.Area,
            n,
            material.Rb,
            profile.B);
        var limited = Sp63NormalFormulas.LimitTensionX(rawX, xiR, profile.H0);
        double allowable = Sp63NormalFormulas.BendingMoment(
            material.Rb,
            profile.B,
            limited.UsedX,
            profile.H0,
            profile.CompressionLayer.Rsc,
            profile.CompressionLayer.Area,
            profile.APrime);
        double lever = lineCoordinate > profile.H0
            ? lineCoordinate - profile.H0
            : profile.APrime - lineCoordinate;
        variables["tensionXRaw"] = limited.RawX;
        variables["tensionUsedX"] = limited.UsedX;
        variables["tensionXWasLimited"] = limited.WasLimited ? 1.0 : 0.0;
        variables["tensionLever"] = Math.Abs(lever);
        variables["tensionMUlt"] = allowable;
        var detailOutside = Detail("(8.20)", "Sp63Normal_EccentricTensionCheck", "8.1.19",
            n * Math.Abs(lever), allowable, variables);
        return Calculated("eccentric_tension_outside", [detailOutside], variables,
            XiMessage(limited.UsedX / profile.H0, xiR), profile!, options);
    }

    static Sp63NormalResult CheckCompression(CrossSection section, double signedN,
        double moment, CalcType calc, Sp63NormalOptions options)
    {
        double n = Math.Abs(signedN);
        var context = options.MemberContext;
        if (context.ElementLengthOrRestraintDistance is not > 0)
            return NotApplicable("missing_accidental_eccentricity_length",
                "Sp63Normal_MissingAccidentalEccentricityLength", "8.1.7");
        if (context.StabilityMode == Sp63NormalStabilityMode.Member &&
            context.EffectiveLengthL0 is not > 0)
            return NotApplicable("missing_effective_length",
                "Sp63Normal_MissingEffectiveLength", "8.1.15");

        var directions = Math.Abs(moment) > Sp63NormalTolerances.Moment
            ? [Math.Sign(moment)]
            : new[] { -1, 1 };
        Sp63NormalResult? worst = null;
        foreach (int direction in directions)
        {
            var candidate = CheckCompressionDirection(section, n, moment, calc,
                options, direction);
            if (candidate.Status != Sp63NormalStatus.Calculated)
                return candidate;
            if (worst is null ||
                candidate.StrengthDetails.Max(detail => detail.Ratio) >
                worst.StrengthDetails.Max(detail => detail.Ratio))
                worst = candidate;
        }

        return worst!;
    }

    static Sp63NormalResult CheckCompressionDirection(CrossSection section, double n,
        double moment, CalcType calc, Sp63NormalOptions options, int tensionDirection)
    {
        if (!TryBuildProfile(section, options.Axis, calc, tensionDirection,
                out var profile, out var messages))
            return NotApplicable(messages);
        if (!Sp63NormalMaterialResolver.TryResolve(section, calc,
                profile!.TensionLayer.Rs, profile.CompressionLayer.Rsc,
                out var material, out var materialMessage))
            return NotApplicable(materialMessage!);

        var context = options.MemberContext;
        double ea = Sp63MemberContext.AccidentalEccentricity(
            context.ElementLengthOrRestraintDistance!.Value, profile!.Height);
        double e0Static = Math.Abs(moment) / n;
        double e0 = Sp63MemberContext.EffectiveEccentricity(
            e0Static, ea, context.StructuralScheme);
        double xiR = Sp63NormalFormulas.XiR(material.Rs, material.Es, material.EpsilonB2);
        double xLow = Sp63NormalFormulas.CompressionXLowXi(
            n,
            profile.TensionLayer.Rs,
            profile.TensionLayer.Area,
            profile.CompressionLayer.Rsc,
            profile.CompressionLayer.Area,
            material.Rb,
            profile.B);
        if (!IsFinitePositive(xLow))
            return NotApplicable("invalid_compression_zone", "Sp63Normal_InvalidCompressionZone", "8.1.10");

        double xiLow = xLow / profile.H0;
        double x = xiLow <= xiR
            ? xLow
            : Sp63NormalFormulas.CompressionXHighXi(
                n,
                profile.TensionLayer.Rs,
                profile.TensionLayer.Area,
                profile.CompressionLayer.Rsc,
                profile.CompressionLayer.Area,
                material.Rb,
                profile.B,
                profile.H0,
                xiR);
        if (!IsFinitePositive(x))
            return NotApplicable("invalid_compression_zone", "Sp63Normal_InvalidCompressionZone", "8.1.10");

        EccentricityAmplifier.EtaResult? etaResult = null;
        if (context.StabilityMode == Sp63NormalStabilityMode.Member)
        {
            var split = section.SplitStiffnessByMaterial();
            double eiConcrete = options.Axis == Sp63NormalAxis.Mx
                ? split.EIxConcrete
                : split.EIyConcrete;
            double eiRebar = options.Axis == Sp63NormalAxis.Mx
                ? split.EIxRebar
                : split.EIyRebar;
            double m0 = tensionDirection * n * e0;
            etaResult = EccentricityAmplifier.AmplifyFormula(
                n: -n,
                m0: m0,
                l0: context.EffectiveLengthL0!.Value,
                h: profile.Height,
                // Прямоугольник брутто: i = h/√12 (п. 8.1.2 — условие l0/i).
                i: profile.Height / Math.Sqrt(12.0),
                eiConcrete: eiConcrete,
                eiRebar: eiRebar,
                psi: context.Psi,
                slendernessThreshold: context.SlendernessThreshold);
            if (!etaResult.Value.Stable)
            {
                var stabilityVariables = CommonVariables(-n, moment,
                    tensionDirection, profile, material, x, x / profile.H0, xiR);
                stabilityVariables["ea"] = ea;
                stabilityVariables["e0Static"] = e0Static;
                stabilityVariables["e0"] = e0;
                stabilityVariables["eta"] = double.IsFinite(etaResult.Value.Eta)
                    ? etaResult.Value.Eta
                    : 0.0;
                stabilityVariables["etaNcr"] = etaResult.Value.Ncr;
                stabilityVariables["etaMEff"] = etaResult.Value.MEff;
                var stabilityDetail = Detail("(8.15)", "Sp63Normal_StabilityCheck",
                    "8.1.15", n, etaResult.Value.Ncr, stabilityVariables);
                var unstable = Calculated("compression", [stabilityDetail],
                    stabilityVariables, [new Sp63NormalMessage(
                        "unstable_element",
                        Sp63NormalMessageKind.Warning,
                        "8.1.15",
                        "Sp63Normal_UnstableElement")], profile!, options);
                unstable.StrengthPassed = false;
                unstable.Eta = etaResult;
                return unstable;
            }
        }

        double xi = x / profile.H0;
        double allowable = Sp63NormalFormulas.BendingMoment(
            material.Rb,
            profile.B,
            x,
            profile.H0,
            profile.CompressionLayer.Rsc,
            profile.CompressionLayer.Area,
            profile.APrime);
        double eta = etaResult?.Eta ?? 1.0;
        double e = Sp63NormalFormulas.CompressionE(eta, e0,
            profile.H0, profile.APrime);
        var variables = CommonVariables(-n, moment, tensionDirection, profile,
            material, x, xi, xiR);
        variables["ea"] = ea;
        variables["e0Static"] = e0Static;
        variables["e0"] = e0;
        variables["e"] = e;
        variables["compressionXLow"] = xLow;
        variables["compressionXBranchHighXi"] = xiLow > xiR ? 1.0 : 0.0;
        variables["eta"] = eta;
        variables["etaMEff"] = etaResult?.MEff ?? tensionDirection * n * e0;
        if (etaResult is { } etaValue && double.IsFinite(etaValue.Ncr))
            variables["etaNcr"] = etaValue.Ncr;
        var informational = XiMessage(xi, xiR);
        informational.Add(new Sp63NormalMessage(
            "accidental_eccentricity",
            Sp63NormalMessageKind.Information,
            "8.1.7",
            "Sp63Normal_AccidentalEccentricity"));
        if (context.StabilityMode == Sp63NormalStabilityMode.SectionOnlyExplicit)
            informational.Add(new Sp63NormalMessage(
                "stability_excluded_explicitly",
                Sp63NormalMessageKind.Information,
                "8.1.15",
                "Sp63Normal_StabilityExcludedExplicitly"));

        var detail = Detail("(8.10)", "Sp63Normal_CompressionCheck", "8.1.10",
            n * e, allowable, variables);
        var result = Calculated("compression", [detail], variables, informational,
            profile!, options);
        result.Eta = etaResult;
        AddAlternativeCompressionCheck(result, section, n, e0, calc, profile!,
            material, context);
        return result;
    }

    /// <summary>
    /// Справочная проверка альтернативным методом п. 8.1.16: N ≤ φ·(Rb·A + Rsc·As,tot).
    /// При невыполнении условий применимости (e0 ≤ h/30, l0/h ≤ 20, класс бетона
    /// в таблице 8.1) добавляет только информационное сообщение.
    /// </summary>
    static void AddAlternativeCompressionCheck(Sp63NormalResult result, CrossSection section,
        double n, double e0, CalcType calc, Sp63NormalSectionProfile profile,
        Sp63NormalMaterialResolver.MaterialValues material, Sp63MemberContext context)
    {
        const string reference = "8.1.16";
        double h = profile.Height;
        // Относительный допуск: при e0 = ea = h/30 условие выполняется точно.
        if (e0 > h / 30.0 * (1.0 + 1e-9))
        {
            result.InformationalMessages.Add(Message("alt_compression_eccentricity",
                Sp63NormalMessageKind.Information, reference,
                "Sp63Normal_AltCompressionEccentricity"));
            return;
        }
        if (context.EffectiveLengthL0 is not > 0)
        {
            result.InformationalMessages.Add(Message("alt_compression_no_l0",
                Sp63NormalMessageKind.Information, reference,
                "Sp63Normal_AltCompressionNoL0"));
            return;
        }

        double l0OverH = context.EffectiveLengthL0.Value / h;
        if (l0OverH > Sp63AlternativeCompression.MaxSlenderness)
        {
            result.InformationalMessages.Add(Message("alt_compression_slenderness",
                Sp63NormalMessageKind.Information, reference,
                "Sp63Normal_AltCompressionSlenderness"));
            return;
        }

        bool longTerm = calc is CalcType.CL or CalcType.NL;
        double? phi;
        int? concreteClass = null;
        if (longTerm)
        {
            string? tag = section.Areas.FirstOrDefault(area =>
                area.Category == AreaCategory.Region &&
                area.Material?.Type == MatType.Concrete)?.Material?.Tag;
            concreteClass = Sp63AlternativeCompression.ParseConcreteClass(tag);
            phi = concreteClass is { } cls
                ? Sp63AlternativeCompression.PhiLongTerm(cls, l0OverH)
                : null;
        }
        else
        {
            phi = Sp63AlternativeCompression.PhiShortTerm(l0OverH);
        }
        if (phi is not { } phiValue)
        {
            result.InformationalMessages.Add(Message("alt_compression_concrete_class",
                Sp63NormalMessageKind.Information, "8.1.16, табл. 8.1",
                "Sp63Normal_AltCompressionConcreteClass"));
            return;
        }

        double concreteArea = profile.B * h;
        double nUlt = Sp63AlternativeCompression.UltimateForce(phiValue, material.Rb,
            concreteArea, profile.CompressionLayer.Rsc, profile.TotalRebarArea);
        var variables = new Dictionary<string, double>
        {
            ["N"] = n,
            ["e0"] = e0,
            ["h"] = h,
            ["l0OverH"] = l0OverH,
            ["phi"] = phiValue,
            ["phiLongTerm"] = longTerm ? 1.0 : 0.0,
            ["Rb"] = material.Rb,
            ["A"] = concreteArea,
            ["Rsc"] = profile.CompressionLayer.Rsc,
            ["AsTot"] = profile.TotalRebarArea,
            ["Nult"] = nUlt
        };
        if (concreteClass is { } classValue)
            variables["concreteClass"] = classValue;
        result.AlternativeChecks.Add(Detail("(8.17)", "Sp63Normal_AltCompressionCheck",
            reference, n, nUlt, variables));
    }

    static bool TryBuildProfile(CrossSection section, Sp63NormalAxis axis,
        CalcType calc, int tensionDirection, out Sp63NormalSectionProfile? profile,
        out IReadOnlyList<Sp63NormalMessage> messages)
    {
        var analysis = Sp63RebarLayoutAnalyzer.Analyze(section, axis, calc,
            tensionDirection);
        profile = analysis.Profile;
        messages = analysis.Messages;
        return profile is not null;
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
        Dictionary<string, double> variables, List<Sp63NormalMessage> informational,
        List<CheckDetail> constructiveChecks, List<Sp63NormalMessage> constructiveNotes)
    {
        var allInformational = new List<Sp63NormalMessage>(informational);
        allInformational.AddRange(constructiveNotes);
        return new()
        {
            Status = Sp63NormalStatus.Calculated,
            StrengthPassed = details.All(detail => detail.Passed),
            Branch = branch,
            StrengthDetails = details,
            ConstructiveChecks = constructiveChecks,
            Variables = variables,
            TraceSteps = Sp63NormalTraceBuilder.Build(branch, details, variables),
            InformationalMessages = allInformational
        };
    }

    /// <summary>Собирает конструктивные проверки прямоугольного профиля и вызывает основную сборку.</summary>
    static Sp63NormalResult Calculated(string branch, List<CheckDetail> details,
        Dictionary<string, double> variables, List<Sp63NormalMessage> informational,
        Sp63NormalSectionProfile profile, Sp63NormalOptions options)
    {
        var (constructiveChecks, constructiveNotes) =
            Sp63NormalConstructiveReinforcement.Check(branch, profile, options.MemberContext);
        var (coverChecks, coverNotes) =
            Sp63NormalConstructiveReinforcement.CheckCoverAndSpacing(profile,
                options.Axis, options.MemberContext.ElementKind,
                options.MemberContext.ExposureCondition, options.MemberContext.IsPrecast);
        var allConstructiveChecks = new List<CheckDetail>(constructiveChecks);
        allConstructiveChecks.AddRange(coverChecks);
        var allNotes = new List<Sp63NormalMessage>(constructiveNotes);
        allNotes.AddRange(coverNotes);
        return Calculated(branch, details, variables, informational,
            allConstructiveChecks, allNotes);
    }

    static Sp63NormalResult InvalidInput(string code, string text, string reference) =>
        new()
        {
            Status = Sp63NormalStatus.InvalidInput,
            Branch = "invalid_input",
            ApplicabilityMessages =
            [Message(code, Sp63NormalMessageKind.Applicability, reference, text)]
        };

    static Sp63NormalResult NotApplicable(string code, string text, string reference)
    {
        var messages = new List<Sp63NormalMessage>
            { Message(code, Sp63NormalMessageKind.Applicability, reference, text) };
        return new()
        {
            Status = Sp63NormalStatus.NotApplicable,
            Branch = "not_applicable",
            ApplicabilityMessages = messages,
            InformationalMessages = SuggestNdmIfNeeded(messages)
        };
    }

    static Sp63NormalResult NotApplicable(Sp63NormalMessage message) =>
        new()
        {
            Status = Sp63NormalStatus.NotApplicable,
            Branch = "not_applicable",
            ApplicabilityMessages = [message],
            InformationalMessages = SuggestNdmIfNeeded([message])
        };

    static Sp63NormalResult NotApplicable(IReadOnlyList<Sp63NormalMessage> messages) =>
        new()
        {
            Status = Sp63NormalStatus.NotApplicable,
            Branch = "not_applicable",
            ApplicabilityMessages = messages.ToList(),
            InformationalMessages = SuggestNdmIfNeeded(messages)
        };

    /// <summary>
    /// Явное предложение перейти к НДМ для случаев, где упрощённый режим заведомо
    /// не покрывает геометрию или арматуру (отверстия, несколько бетонных областей,
    /// двуосный изгиб, сложная арматура) — см. Этап 2 дорожной карты СП 63.
    /// </summary>
    internal static List<Sp63NormalMessage> SuggestNdmIfNeeded(IReadOnlyList<Sp63NormalMessage> messages) =>
        messages.Any(message => NdmSuggestionCodes.Contains(message.Code))
            ? [Message("suggest_ndm", Sp63NormalMessageKind.Information, "8.1",
                "Sp63Normal_SuggestNdm")]
            : [];

    static Sp63NormalMessage Message(string code, Sp63NormalMessageKind kind,
        string reference, string text) => new(code, kind, reference, text);

    static List<Sp63NormalMessage> XiMessage(double xi, double xiR) =>
    [
        Message("compression_zone_ratio", Sp63NormalMessageKind.Information,
            "8.1.8", "Sp63Normal_CompressionZoneRatio")
    ];

    static Dictionary<string, double> CommonVariables(double loadN, double moment,
        int tensionDirection, Sp63NormalSectionProfile profile,
        Sp63NormalMaterialResolver.MaterialValues material,
        double x, double xi, double xiR) => new()
        {
            ["N"] = loadN,
            ["M"] = moment,
            ["tensionDirection"] = tensionDirection,
            ["b"] = profile.B,
            ["h"] = profile.Height,
            ["h0"] = profile.H0,
            ["aPrime"] = profile.APrime,
            ["As"] = profile.TensionLayer.Area,
            ["AsPrime"] = profile.CompressionLayer.Area,
            ["Rs"] = material.Rs,
            ["Rsc"] = material.Rsc,
            ["Rb"] = material.Rb,
            ["Es"] = material.Es,
            ["epsilonB2"] = material.EpsilonB2,
            ["x"] = x,
            ["xi"] = xi,
            ["xiR"] = xiR
        };

    static bool AreFinite(params double[] values) => values.All(double.IsFinite);

    static bool IsFinitePositive(double value) => double.IsFinite(value) && value > 0;

}
