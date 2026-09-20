using CScore.Sp63;

namespace CScore.Sp63.Normal;

/// <summary>Проверяет тавровое/двутавровое нормальное сечение при чистом изгибе.</summary>
public static class Sp63TeeNormalChecker
{
    /// <summary>
    /// Выполняет одноосную проверку таврового/двутаврового железобетонного сечения
    /// при чистом изгибе (п. 8.1.8, 8.1.9, 8.1.11 СП 63).
    /// </summary>
    /// <param name="section">Расчётное сечение.</param>
    /// <param name="load">Нормальная сила и моменты в конвенции OpenCS.</param>
    /// <param name="calc">Вид расчёта для разрешения характеристик материалов.</param>
    /// <param name="options">Типизированные настройки формульного режима.</param>
    /// <param name="spanLength">Пролёт l по п. 8.1.11; обязателен только при наличии сжатой полки.</param>
    public static Sp63NormalResult Check(CrossSection section, LoadItem load,
        CalcType calc, Sp63NormalOptions options, double? spanLength)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(load);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.MemberContext);

        if (!Enum.IsDefined(options.Axis))
            return InvalidInput("invalid_axis", "Sp63Normal_InvalidAxis", "8.1");
        if (!AreFinite(load.N, load.Mx, load.My))
            return InvalidInput("non_finite_load", "Sp63Normal_NonFiniteLoad", "8.1");

        double moment = options.Axis == Sp63NormalAxis.Mx ? load.Mx : load.My;
        double otherMoment = options.Axis == Sp63NormalAxis.Mx ? load.My : load.Mx;
        if (Math.Abs(otherMoment) > Sp63NormalTolerances.Moment)
            return NotApplicable("biaxial_load", "Sp63Normal_BiaxialLoad", "8.1");
        if (Math.Abs(load.N) > Sp63NormalTolerances.Force)
            return NotApplicable("unsupported_load_case_for_tee",
                "Sp63Normal_UnsupportedLoadCaseForTee", "8.1.11");
        if (Math.Abs(moment) <= Sp63NormalTolerances.Moment)
            return NotApplicable("zero_load", "Sp63Normal_ZeroLoad", "8.1");

        int tensionDirection = Math.Sign(moment);
        var analysis = Sp63TeeRebarLayoutAnalyzer.Analyze(section, options.Axis,
            calc, tensionDirection);
        if (analysis.Profile is null)
            return NotApplicable(analysis.Messages);
        var profile = analysis.Profile;

        if (!Sp63NormalMaterialResolver.TryResolve(section, calc,
                profile.TensionLayer.Rs, profile.CompressionLayer.Rsc,
                out var material, out var materialMessage))
            return NotApplicable(materialMessage!);

        bool hasCompressionFlange = profile.CompressionFlangeThickness > 0;
        double bfEff = 0.0;
        if (hasCompressionFlange)
        {
            if (spanLength is not > 0)
                return NotApplicable("missing_span_length",
                    "Sp63Normal_MissingSpanLength", "8.1.11");
            bfEff = Sp63TeeFormulas.EffectiveFlangeWidth(profile.Bw,
                profile.CompressionFlangeActualWidth,
                profile.CompressionFlangeThickness, profile.H, spanLength.Value);
        }

        bool neutralAxisInFlange = false;
        double x;
        if (!hasCompressionFlange)
        {
            x = Sp63NormalFormulas.BendingX(profile.TensionLayer.Rs,
                profile.TensionLayer.Area, profile.CompressionLayer.Rsc,
                profile.CompressionLayer.Area, material.Rb, profile.Bw);
        }
        else
        {
            neutralAxisInFlange = Sp63TeeFormulas.IsNeutralAxisInFlange(
                profile.TensionLayer.Rs, profile.TensionLayer.Area,
                profile.CompressionLayer.Rsc, profile.CompressionLayer.Area,
                material.Rb, bfEff, profile.CompressionFlangeThickness);
            x = neutralAxisInFlange
                ? Sp63NormalFormulas.BendingX(profile.TensionLayer.Rs,
                    profile.TensionLayer.Area, profile.CompressionLayer.Rsc,
                    profile.CompressionLayer.Area, material.Rb, bfEff)
                : Sp63TeeFormulas.BendingX_CaseB(profile.TensionLayer.Rs,
                    profile.TensionLayer.Area, profile.CompressionLayer.Rsc,
                    profile.CompressionLayer.Area, material.Rb, profile.Bw,
                    bfEff, profile.CompressionFlangeThickness);
        }
        if (!double.IsFinite(x) || x < -Sp63NormalTolerances.Force)
            return NotApplicable("invalid_compression_zone",
                "Sp63Normal_InvalidCompressionZone", "8.1.8");

        double xiR = Sp63NormalFormulas.XiR(material.Rs, material.Es,
            material.EpsilonB2);
        double xi = Math.Max(0.0, x) / profile.H0;
        bool symmetricBranch = x <= 2.0 * profile.APrime + Sp63NormalTolerances.Force;
        double x0 = 0.0;
        double allowable;
        string formula;
        string description;
        string reference;
        if (symmetricBranch)
        {
            x0 = BendingXWithoutCompression(profile, material, hasCompressionFlange,
                bfEff);
            allowable = Sp63NormalFormulas.SymmetricMoment(material.Rs,
                profile.TensionLayer.Area, profile.H0, profile.APrime, x0,
                compressionRebarWasExcluded: true);
            formula = "(8.9)";
            description = "Sp63Normal_TeeSymmetricBendingCheck";
            reference = "8.1.9";
        }
        else if (!hasCompressionFlange)
        {
            allowable = Sp63NormalFormulas.BendingMoment(material.Rb, profile.Bw, x,
                profile.H0, profile.CompressionLayer.Rsc,
                profile.CompressionLayer.Area, profile.APrime);
            formula = "(8.4)";
            description = "Sp63Normal_TeeBendingCheck";
            reference = "8.1.8";
        }
        else if (neutralAxisInFlange)
        {
            allowable = Sp63NormalFormulas.BendingMoment(material.Rb, bfEff, x,
                profile.H0, profile.CompressionLayer.Rsc,
                profile.CompressionLayer.Area, profile.APrime);
            formula = "(8.4)";
            description = "Sp63Normal_TeeBendingCheck";
            reference = "8.1.11";
        }
        else
        {
            allowable = Sp63TeeFormulas.BendingMoment_CaseB(material.Rb, profile.Bw,
                bfEff, profile.CompressionFlangeThickness, x, profile.H0,
                profile.CompressionLayer.Rsc, profile.CompressionLayer.Area,
                profile.APrime);
            formula = "(8.8)";
            description = "Sp63Normal_TeeBendingCheck";
            reference = "8.1.11";
        }
        if (!IsFinitePositive(allowable))
            return NotApplicable("nonpositive_capacity",
                "Sp63Normal_NonpositiveCapacity", "8.1.8");

        var variables = Variables(load.N, moment, tensionDirection, profile, material,
            x, xi, xiR, hasCompressionFlange, bfEff, neutralAxisInFlange,
            symmetricBranch, x0);
        var informational = new List<Sp63NormalMessage>
        {
            new("compression_zone_ratio", Sp63NormalMessageKind.Information, "8.1.8",
                "Sp63Normal_CompressionZoneRatio")
        };
        if (symmetricBranch)
            informational.Add(new Sp63NormalMessage("symmetric_branch",
                Sp63NormalMessageKind.Information, "8.1.9",
                "Sp63Normal_SymmetricBranch"));

        var detail = new CheckDetail
        {
            Formula = formula,
            Description = description,
            NormReference = reference,
            Applied = Math.Max(0.0, Math.Abs(moment)),
            Allowable = allowable,
            Variables = new Dictionary<string, double>(variables)
        };
        return new Sp63NormalResult
        {
            Status = Sp63NormalStatus.Calculated,
            StrengthPassed = detail.Passed,
            Branch = "bending",
            StrengthDetails = [detail],
            ConstructiveChecks = [],
            Variables = variables,
            TraceSteps = Sp63NormalTraceBuilder.Build("bending", [detail], variables),
            InformationalMessages = informational
        };
    }

    static double BendingXWithoutCompression(Sp63TeeSectionProfile profile,
        Sp63NormalMaterialResolver.MaterialValues material, bool hasCompressionFlange,
        double bfEff)
    {
        double rs = profile.TensionLayer.Rs;
        double asT = profile.TensionLayer.Area;
        if (!hasCompressionFlange)
            return Sp63NormalFormulas.BendingX(rs, asT, 0.0, 0.0, material.Rb,
                profile.Bw);
        return Sp63TeeFormulas.IsNeutralAxisInFlange(rs, asT, 0.0, 0.0, material.Rb,
            bfEff, profile.CompressionFlangeThickness)
            ? Sp63NormalFormulas.BendingX(rs, asT, 0.0, 0.0, material.Rb, bfEff)
            : Sp63TeeFormulas.BendingX_CaseB(rs, asT, 0.0, 0.0, material.Rb,
                profile.Bw, bfEff, profile.CompressionFlangeThickness);
    }

    static Dictionary<string, double> Variables(double loadN, double moment,
        int tensionDirection, Sp63TeeSectionProfile profile,
        Sp63NormalMaterialResolver.MaterialValues material, double x, double xi,
        double xiR, bool hasCompressionFlange, double bfEff,
        bool neutralAxisInFlange, bool symmetricBranch, double x0)
    {
        var variables = new Dictionary<string, double>
        {
            ["N"] = loadN,
            ["M"] = moment,
            ["tensionDirection"] = tensionDirection,
            ["bw"] = profile.Bw,
            ["h"] = profile.H,
            ["h0"] = profile.H0,
            ["aPrime"] = profile.APrime,
            ["hasCompressionFlange"] = hasCompressionFlange ? 1.0 : 0.0,
            ["compressionFlangeActualWidth"] = profile.CompressionFlangeActualWidth,
            ["compressionFlangeThickness"] = profile.CompressionFlangeThickness,
            ["compressionFlangeEffectiveWidth"] =
                hasCompressionFlange ? bfEff : profile.Bw,
            ["As"] = profile.TensionLayer.Area,
            ["AsPrime"] = profile.CompressionLayer.Area,
            ["Rs"] = material.Rs,
            ["Rsc"] = material.Rsc,
            ["Rb"] = material.Rb,
            ["Es"] = material.Es,
            ["epsilonB2"] = material.EpsilonB2,
            ["x"] = x,
            ["xi"] = xi,
            ["xiR"] = xiR,
            ["symmetricBranchApplied"] = symmetricBranch ? 1.0 : 0.0
        };
        if (hasCompressionFlange)
            variables["neutralAxisInFlange"] = neutralAxisInFlange ? 1.0 : 0.0;
        if (symmetricBranch)
            variables["symmetricXWithoutCompressionRebar"] = x0;
        return variables;
    }

    static Sp63NormalResult InvalidInput(string code, string text, string reference) =>
        new()
        {
            Status = Sp63NormalStatus.InvalidInput,
            Branch = "invalid_input",
            ApplicabilityMessages =
            [new(code, Sp63NormalMessageKind.Applicability, reference, text)]
        };

    static Sp63NormalResult NotApplicable(string code, string text, string reference) =>
        NotApplicable([new(code, Sp63NormalMessageKind.Applicability, reference, text)]);

    static Sp63NormalResult NotApplicable(Sp63NormalMessage message) =>
        NotApplicable([message]);

    static Sp63NormalResult NotApplicable(IReadOnlyList<Sp63NormalMessage> messages) =>
        new()
        {
            Status = Sp63NormalStatus.NotApplicable,
            Branch = "not_applicable",
            ApplicabilityMessages = messages.ToList(),
            InformationalMessages = Sp63NormalChecker.SuggestNdmIfNeeded(messages)
        };

    static bool AreFinite(params double[] values) => values.All(double.IsFinite);

    static bool IsFinitePositive(double value) => double.IsFinite(value) && value > 0;
}
