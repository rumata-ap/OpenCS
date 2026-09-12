using CScore.Sp63;

namespace CScore.Sp63.Normal;

/// <summary>Проверяет прямоугольное нормальное сечение по формулам СП 63.</summary>
public static class Sp63NormalChecker
{
    const double ForceTolerance = 1e-9;
    const double MomentTolerance = 1e-9;
    const double RebarAreaTolerance = 1e-9;

    /// <summary>
    /// Выполняет одноосную проверку прямоугольного железобетонного сечения.
    /// </summary>
    /// <param name="section">Расчётное сечение.</param>
    /// <param name="load">Нормальная сила и моменты в конвенции OpenCS.</param>
    /// <param name="calc">Вид расчёта для разрешения характеристик материалов.</param>
    /// <param name="options">Типизированные настройки формульного режима.</param>
    public static Sp63NormalResult Check(
        CrossSection section,
        LoadItem load,
        CalcType calc,
        Sp63NormalOptions options)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(load);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.MemberContext);

        if (options.ShapeKind != Sp63NormalShapeKind.Rectangular)
            return NotApplicable("unsupported_shape", "Sp63Normal_ShapeNotSupported", "8.1");
        if (!Enum.IsDefined(options.Axis))
            return InvalidInput("invalid_axis", "Sp63Normal_InvalidAxis", "8.1");
        if (!AreFinite(load.N, load.Mx, load.My))
            return InvalidInput("non_finite_load", "Sp63Normal_NonFiniteLoad", "8.1");

        double moment = options.Axis == Sp63NormalAxis.Mx ? load.Mx : load.My;
        double otherMoment = options.Axis == Sp63NormalAxis.Mx ? load.My : load.Mx;
        if (Math.Abs(otherMoment) > MomentTolerance)
            return NotApplicable("biaxial_load", "Sp63Normal_BiaxialLoad", "8.1");

        if (load.N < -ForceTolerance)
            return CheckCompression(section, load.N, moment, calc, options);
        if (load.N > ForceTolerance)
        {
            if (Math.Abs(moment) <= MomentTolerance)
                return CheckCentralTension(section, load.N, calc, options);
            return CheckEccentricTension(section, load.N, moment, calc, options);
        }
        if (Math.Abs(moment) > MomentTolerance)
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
        if (!TryResolveMaterialValues(section, calc, profile!, out var material,
                out var materialMessage))
            return NotApplicable(materialMessage!);

        double x = Sp63NormalFormulas.BendingX(
            profile!.TensionLayer.Rs,
            profile.TensionLayer.Area,
            profile.CompressionLayer.Rsc,
            profile.CompressionLayer.Area,
            material.Rb,
            profile.B);
        if (!double.IsFinite(x) || x < -ForceTolerance)
            return NotApplicable("invalid_compression_zone", "Sp63Normal_InvalidCompressionZone", "8.1.8");

        double xiR = Sp63NormalFormulas.XiR(material.Rs, material.Es, material.EpsilonB2);
        double xi = Math.Max(0.0, x) / profile.H0;
        bool symmetricBranch = x <= 2.0 * profile.APrime + ForceTolerance;
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
            profile.CompressionLayer.Area <= RebarAreaTolerance ? 1.0 : 0.0;
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
        return Calculated("bending", [detail], variables, informational);
    }

    static Sp63NormalResult CheckCentralTension(CrossSection section, double n,
        CalcType calc, Sp63NormalOptions options)
    {
        if (!TryBuildProfile(section, options.Axis, calc, -1,
                out var profile, out var messages))
            return NotApplicable(messages);
        if (!TryResolveMaterialValues(section, calc, profile!, out var material,
                out var materialMessage))
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
        return Calculated("central_tension", [detail], variables, []);
    }

    static Sp63NormalResult CheckEccentricTension(CrossSection section, double n,
        double moment, CalcType calc, Sp63NormalOptions options)
    {
        int tensionDirection = Math.Sign(moment);
        if (!TryBuildProfile(section, options.Axis, calc, tensionDirection,
                out var profile, out var messages))
            return NotApplicable(messages);
        if (!TryResolveMaterialValues(section, calc, profile!, out var material,
                out var materialMessage))
            return NotApplicable(materialMessage!);

        double lineCoordinate = profile!.Height / 2.0 + Math.Abs(moment) / n;
        bool between = lineCoordinate >= profile.APrime - MomentTolerance &&
                       lineCoordinate <= profile.H0 + MomentTolerance;
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
                XiMessage(0.0, xiR));
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
            XiMessage(limited.UsedX / profile.H0, xiR));
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

        var directions = Math.Abs(moment) > MomentTolerance
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
        if (!TryResolveMaterialValues(section, calc, profile!, out var material,
                out var materialMessage))
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

        double xi = x / profile.H0;
        double allowable = Sp63NormalFormulas.BendingMoment(
            material.Rb,
            profile.B,
            x,
            profile.H0,
            profile.CompressionLayer.Rsc,
            profile.CompressionLayer.Area,
            profile.APrime);
        double e = Sp63NormalFormulas.CompressionE(1.0, e0,
            profile.H0, profile.APrime);
        var variables = CommonVariables(-n, moment, tensionDirection, profile,
            material, x, xi, xiR);
        variables["ea"] = ea;
        variables["e0Static"] = e0Static;
        variables["e0"] = e0;
        variables["e"] = e;
        variables["compressionXLow"] = xLow;
        variables["compressionXBranchHighXi"] = xiLow > xiR ? 1.0 : 0.0;
        variables["eta"] = 1.0;
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
        return Calculated("compression", [detail], variables, informational);
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

    static bool TryResolveMaterialValues(CrossSection section, CalcType calc,
        Sp63NormalSectionProfile profile, out MaterialValues values,
        out Sp63NormalMessage? message)
    {
        var concrete = section.Areas.FirstOrDefault(area =>
            area.Category == AreaCategory.Region &&
            area.Material?.Type == MatType.Concrete);
        var rebar = section.Areas.FirstOrDefault(area =>
            area.Category == AreaCategory.RebarGroup && area.Material != null);
        var concreteChars = concrete?.Material?.GetChars(calc);
        var rebarChars = rebar?.Material?.GetChars(calc);
        double rb = Math.Abs(concreteChars?.Fc ?? 0.0);
        double es = rebarChars?.E ?? 0.0;
        double epsilonB2 = Math.Abs(concreteChars?.Ec2 ?? 0.0);
        if (!IsFinitePositive(rb))
            return FailureValues("missing_concrete_resistance",
                "Sp63Normal_MissingConcreteResistance", "8.1.8", out values, out message);
        if (!IsFinitePositive(es))
            return FailureValues("missing_rebar_modulus",
                "Sp63Normal_MissingRebarModulus", "8.1.8", out values, out message);
        if (!IsFinitePositive(epsilonB2))
            return FailureValues("missing_concrete_strain",
                "Sp63Normal_MissingConcreteStrain", "8.1.8", out values, out message);

        values = new MaterialValues(rb, profile.TensionLayer.Rs,
            profile.CompressionLayer.Rsc, es, epsilonB2);
        message = null;
        return true;
    }

    static bool FailureValues(string code, string text, string reference,
        out MaterialValues values, out Sp63NormalMessage? message)
    {
        values = default;
        message = Message(code, Sp63NormalMessageKind.Applicability, reference, text);
        return false;
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
        Dictionary<string, double> variables, List<Sp63NormalMessage> informational) =>
        new()
        {
            Status = Sp63NormalStatus.Calculated,
            StrengthPassed = details.All(detail => detail.Passed),
            Branch = branch,
            StrengthDetails = details,
            Variables = variables,
            InformationalMessages = informational
        };

    static Sp63NormalResult InvalidInput(string code, string text, string reference) =>
        new()
        {
            Status = Sp63NormalStatus.InvalidInput,
            Branch = "invalid_input",
            ApplicabilityMessages =
            [Message(code, Sp63NormalMessageKind.Applicability, reference, text)]
        };

    static Sp63NormalResult NotApplicable(string code, string text, string reference) =>
        new()
        {
            Status = Sp63NormalStatus.NotApplicable,
            Branch = "not_applicable",
            ApplicabilityMessages =
            [Message(code, Sp63NormalMessageKind.Applicability, reference, text)]
        };

    static Sp63NormalResult NotApplicable(Sp63NormalMessage message) =>
        new()
        {
            Status = Sp63NormalStatus.NotApplicable,
            Branch = "not_applicable",
            ApplicabilityMessages = [message]
        };

    static Sp63NormalResult NotApplicable(IReadOnlyList<Sp63NormalMessage> messages) =>
        new()
        {
            Status = Sp63NormalStatus.NotApplicable,
            Branch = "not_applicable",
            ApplicabilityMessages = messages.ToList()
        };

    static Sp63NormalMessage Message(string code, Sp63NormalMessageKind kind,
        string reference, string text) => new(code, kind, reference, text);

    static List<Sp63NormalMessage> XiMessage(double xi, double xiR) =>
    [
        Message("compression_zone_ratio", Sp63NormalMessageKind.Information,
            "8.1.8", "Sp63Normal_CompressionZoneRatio")
    ];

    static Dictionary<string, double> CommonVariables(double loadN, double moment,
        int tensionDirection, Sp63NormalSectionProfile profile, MaterialValues material,
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

    readonly record struct MaterialValues(double Rb, double Rs, double Rsc,
        double Es, double EpsilonB2);
}
