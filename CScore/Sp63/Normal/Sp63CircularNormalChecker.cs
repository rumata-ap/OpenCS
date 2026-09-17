using CScore.Sp63;

namespace CScore.Sp63.Normal;

/// <summary>
/// Проверяет круглое (п. Д.2) и кольцевое (п. Д.1) нормальное сечение по приложению Д
/// СП 63.13330.2018 при сжатии и изгибе с результирующим моментом √(Mx² + My²).
/// </summary>
public static class Sp63CircularNormalChecker
{
    /// <summary>Порог «класс не выше А400» по Rs характеристик C, кПа (табл. 6.14: А400 — 340; прежняя редакция — 350).</summary>
    public const double RebarClassA400RsLimit = 350_000.0;
    const double RebarClassRelativeTolerance = 1e-6;

    /// <summary>Выполняет проверку для форм Circular или Annular.</summary>
    public static Sp63NormalResult Check(CrossSection section, LoadItem load,
        CalcType calc, Sp63NormalOptions options)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(load);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.MemberContext);
        if (options.ShapeKind is not (Sp63NormalShapeKind.Circular or Sp63NormalShapeKind.Annular))
            throw new ArgumentException("Ожидается круглая или кольцевая форма.", nameof(options));

        bool annular = options.ShapeKind == Sp63NormalShapeKind.Annular;
        string reference = annular ? "Д.1" : "Д.2";
        var context = options.MemberContext;

        if (!double.IsFinite(load.N) || !double.IsFinite(load.Mx) || !double.IsFinite(load.My))
            return InvalidInput("non_finite_load", "Sp63Normal_NonFiniteLoad", "8.1");
        if (load.N > Sp63NormalTolerances.Force)
            return NotApplicable("circular_tension_not_supported",
                "Sp63Normal_CircularTensionNotSupported", "Д");

        bool compression = load.N < -Sp63NormalTolerances.Force;
        double m0 = Math.Sqrt(load.Mx * load.Mx + load.My * load.My);
        if (!compression && m0 <= Sp63NormalTolerances.Moment)
            return NotApplicable("zero_load", "Sp63Normal_ZeroLoad", "8.1");
        // Данные элемента проверяются до геометрии — тот же порядок, что у прямоугольника
        // (Sp63NormalChecker.CheckCompression): недостающий ввод задачи сообщается первым.
        if (compression && context.ElementLengthOrRestraintDistance is not > 0)
            return NotApplicable("missing_accidental_eccentricity_length",
                "Sp63Normal_MissingAccidentalEccentricityLength", "8.1.7");
        if (compression && context.StabilityMode == Sp63NormalStabilityMode.Member &&
            context.EffectiveLengthL0 is not > 0)
            return NotApplicable("missing_effective_length",
                "Sp63Normal_MissingEffectiveLength", "8.1.15");

        var geometryResult = Sp63CircularGeometryPolicy.Classify(section, options.ShapeKind);
        if (geometryResult.Geometry is null)
            return NotApplicable(geometryResult.Messages);
        var geometry = geometryResult.Geometry;

        var rebarResult = Sp63PolarRebarLayoutAnalyzer.Analyze(section, calc,
            geometry.CenterX, geometry.CenterY);
        if (rebarResult.Profile is null)
            return NotApplicable(rebarResult.Messages);
        var rebar = rebarResult.Profile;

        double rsClass = 0.0;
        if (!annular)
        {
            if (!TryResolveClassRs(section, out rsClass))
                return NotApplicable("missing_rebar_resistance",
                    "Sp63Normal_MissingRebarResistance", "Д.2");
            if (rsClass > RebarClassA400RsLimit * (1.0 + RebarClassRelativeTolerance))
                return NotApplicable("circular_rebar_class_above_a400",
                    "Sp63Normal_CircularRebarClassAboveA400", "Д.2");
        }

        // Общий резолвер требует также Es арматуры и εb2 бетона, хотя формулы Д.1–Д.9 их
        // не используют; оставлено ради единого контракта материалов с прямоугольником.
        if (!Sp63NormalMaterialResolver.TryResolve(section, calc, rebar.Rs, rebar.Rsc,
                out var material, out var materialMessage))
            return NotApplicable([materialMessage!]);

        double d = 2.0 * geometry.OuterRadius;
        string branch = (annular ? "annular" : "circular") +
                        (compression ? "_compression" : "_bending");
        var variables = BaseVariables(load, m0, d, geometry, rebar, material, annular, rsClass);
        var informational = new List<Sp63NormalMessage>
        {
            Info("appendix_d_recommended", "Д", "Sp63Normal_AppendixDRecommended")
        };
        if (Math.Abs(load.Mx) > Sp63NormalTolerances.Moment &&
            Math.Abs(load.My) > Sp63NormalTolerances.Moment)
            informational.Add(Info("resultant_moment_used", "Д", "Sp63Normal_ResultantMomentUsed"));
        if (!annular)
            informational.Add(Info("circular_rebar_class_by_rs", "Д.2",
                "Sp63Normal_CircularRebarClassByRs"));

        double n = 0.0;
        double moment = m0;
        EccentricityAmplifier.EtaResult? etaResult = null;
        if (compression)
        {
            n = Math.Abs(load.N);
            double ea = Sp63MemberContext.AccidentalEccentricity(
                context.ElementLengthOrRestraintDistance!.Value, d);
            double e0Static = m0 / n;
            double e0 = Sp63MemberContext.EffectiveEccentricity(e0Static, ea,
                context.StructuralScheme);
            variables["ea"] = ea;
            variables["e0Static"] = e0Static;
            variables["e0"] = e0;
            informational.Add(Info("accidental_eccentricity", "8.1.7",
                "Sp63Normal_AccidentalEccentricity"));

            double eta = 1.0;
            if (context.StabilityMode == Sp63NormalStabilityMode.Member)
            {
                var split = section.SplitStiffnessByMaterial();
                // Многоугольник даёт чуть разные EIx/EIy — берём меньшую жёсткость.
                etaResult = EccentricityAmplifier.AmplifyFormula(
                    n: -n,
                    m0: n * e0,
                    l0: context.EffectiveLengthL0!.Value,
                    h: d,
                    // Круг/кольцо брутто: i = √(r₁² + r₂²)/2 (п. 8.1.2 — условие l0/i).
                    i: RadiusOfGyration(geometry),
                    eiConcrete: Math.Min(split.EIxConcrete, split.EIyConcrete),
                    eiRebar: Math.Min(split.EIxRebar, split.EIyRebar),
                    psi: context.Psi,
                    slendernessThreshold: context.SlendernessThreshold);
                var etaValue = etaResult.Value;
                variables["etaMEff"] = etaValue.MEff;
                if (double.IsFinite(etaValue.Ncr))
                    variables["etaNcr"] = etaValue.Ncr;
                if (!etaValue.Stable)
                {
                    variables["eta"] = double.IsFinite(etaValue.Eta) ? etaValue.Eta : 0.0;
                    informational.Add(new Sp63NormalMessage("unstable_element",
                        Sp63NormalMessageKind.Warning, "8.1.15", "Sp63Normal_UnstableElement"));
                    var stability = Detail("(8.15)", "Sp63Normal_StabilityCheck", "8.1.15",
                        n, etaValue.Ncr, variables);
                    var unstable = Calculated(branch, stability, variables, informational, etaResult);
                    unstable.StrengthPassed = false;
                    return unstable;
                }
                eta = etaValue.Eta;
            }
            else
            {
                informational.Add(Info("stability_excluded_explicitly", "8.1.15",
                    "Sp63Normal_StabilityExcludedExplicitly"));
            }

            moment = eta * n * e0;
            variables["eta"] = eta;
        }
        else
        {
            informational.Add(Info("appendix_d_pure_bending_extension", "Д",
                "Sp63Normal_AppendixDPureBendingExtension"));
        }
        variables["M"] = moment;

        string formula;
        string description;
        double mult;
        bool overloaded;
        if (annular)
        {
            var capacity = Sp63CircularFormulas.Annular(n, material.Rb, rebar.Rs, rebar.Rsc,
                geometry.Area, rebar.TotalArea, geometry.InnerRadius, geometry.OuterRadius,
                rebar.RadiusRs);
            variables["rm"] = (geometry.InnerRadius + geometry.OuterRadius) / 2.0;
            variables["xiCir"] = capacity.XiCir;
            variables["annularBranch"] = (int)capacity.Branch;
            if (double.IsFinite(capacity.XiCir1)) variables["xiCir1"] = capacity.XiCir1;
            if (double.IsFinite(capacity.XiCir2)) variables["xiCir2"] = capacity.XiCir2;
            formula = capacity.Branch switch
            {
                Sp63AnnularBranch.A => "(Д.2)",
                Sp63AnnularBranch.B => "(Д.3)",
                _ => "(Д.4)"
            };
            description = "Sp63Normal_AnnularCheck";
            mult = capacity.Mult;
            overloaded = capacity.Overloaded;
        }
        else
        {
            var capacity = Sp63CircularFormulas.Circular(n, material.Rb, rebar.Rs,
                geometry.Area, rebar.TotalArea, geometry.OuterRadius, rebar.RadiusRs);
            variables["conditionD7"] = capacity.ConditionD7 ? 1.0 : 0.0;
            variables["circularEquation"] = capacity.ConditionD7 ? 8.0 : 9.0;
            variables["phi"] = capacity.Phi;
            if (double.IsFinite(capacity.XiCir)) variables["xiCir"] = capacity.XiCir;
            formula = "(Д.6)";
            description = "Sp63Normal_CircularCheck";
            mult = capacity.Mult;
            overloaded = capacity.Overloaded;
        }

        if (overloaded)
            informational.Add(new Sp63NormalMessage("compression_exceeds_section_capacity",
                Sp63NormalMessageKind.Warning, reference,
                "Sp63Normal_CompressionExceedsSectionCapacity"));
        else if (!(double.IsFinite(mult) && mult > 0.0))
            return NotApplicable("nonpositive_capacity", "Sp63Normal_NonpositiveCapacity",
                reference);

        var detail = Detail(formula, description, reference, moment, mult, variables);
        return Calculated(branch, detail, variables, informational, etaResult);
    }

    /// <summary>Радиус инерции круга (r₁ = 0) или кольца по эквивалентным радиусам, м.</summary>
    internal static double RadiusOfGyration(Sp63CircularGeometry geometry) =>
        Math.Sqrt(geometry.InnerRadius * geometry.InnerRadius +
                  geometry.OuterRadius * geometry.OuterRadius) / 2.0;

    /// <summary>Наибольшее |Ft| характеристик C среди материалов арматуры.</summary>
    static bool TryResolveClassRs(CrossSection section, out double rsClass)
    {
        rsClass = 0.0;
        foreach (var area in section.Areas.Where(a => a.Category == AreaCategory.RebarGroup))
        {
            var chars = area.Material?.GetChars(CalcType.C);
            if (chars is null || !double.IsFinite(chars.Ft) || chars.Ft == 0.0)
                return false;
            rsClass = Math.Max(rsClass, Math.Abs(chars.Ft));
        }
        return rsClass > 0.0;
    }

    static Dictionary<string, double> BaseVariables(LoadItem load, double m0, double d,
        Sp63CircularGeometry g, Sp63CircularRebarProfile rebar,
        Sp63NormalMaterialResolver.MaterialValues material, bool annular, double rsClass)
    {
        var v = new Dictionary<string, double>
        {
            ["N"] = load.N,
            ["Mx"] = load.Mx,
            ["My"] = load.My,
            ["M0"] = m0,
            ["A"] = g.Area,
            ["r2"] = g.OuterRadius,
            ["D"] = d,
            ["outerMeanVertexRadius"] = g.OuterMeanVertexRadius,
            ["outerRadialDeviation"] = g.OuterRadialDeviation,
            ["outerAreaDeviation"] = g.OuterAreaDeviation,
            ["nBars"] = rebar.BarCount,
            ["AsTot"] = rebar.TotalArea,
            ["rs"] = rebar.RadiusRs,
            ["rebarAreaDeviation"] = rebar.AreaDeviation,
            ["rebarRadiusDeviation"] = rebar.RadiusDeviation,
            ["rebarAngularStepDeviation"] = rebar.AngularStepDeviation,
            ["rebarCenterOffset"] = rebar.CenterOffset,
            ["Rb"] = material.Rb,
            ["Rs"] = material.Rs,
            ["Rsc"] = material.Rsc
        };
        if (annular)
        {
            v["r1"] = g.InnerRadius;
            v["radiusRatio"] = g.InnerRadius / g.OuterRadius;
            v["innerMeanVertexRadius"] = g.InnerMeanVertexRadius;
            v["innerRadialDeviation"] = g.InnerRadialDeviation;
            v["innerAreaDeviation"] = g.InnerAreaDeviation;
            v["centerOffset"] = g.CenterOffset;
        }
        else
        {
            v["RsClassCheck"] = rsClass;
        }
        return v;
    }

    static CheckDetail Detail(string formula, string description, string reference,
        double applied, double allowable, Dictionary<string, double> variables) =>
        new()
        {
            Formula = formula,
            Description = description,
            NormReference = reference,
            Applied = Math.Max(0.0, applied),
            Allowable = Math.Max(0.0, allowable),
            Variables = new Dictionary<string, double>(variables)
        };

    static Sp63NormalResult Calculated(string branch, CheckDetail detail,
        Dictionary<string, double> variables, List<Sp63NormalMessage> informational,
        EccentricityAmplifier.EtaResult? eta) =>
        new()
        {
            Status = Sp63NormalStatus.Calculated,
            StrengthPassed = detail.Passed,
            Branch = branch,
            StrengthDetails = [detail],
            ConstructiveChecks = [],
            Variables = variables,
            InformationalMessages = informational,
            Eta = eta
        };

    static Sp63NormalMessage Info(string code, string reference, string text) =>
        new(code, Sp63NormalMessageKind.Information, reference, text);

    static Sp63NormalResult InvalidInput(string code, string text, string reference) =>
        new()
        {
            Status = Sp63NormalStatus.InvalidInput,
            Branch = "invalid_input",
            ApplicabilityMessages =
            [new Sp63NormalMessage(code, Sp63NormalMessageKind.Applicability, reference, text)]
        };

    static Sp63NormalResult NotApplicable(string code, string text, string reference) =>
        NotApplicable([new Sp63NormalMessage(code, Sp63NormalMessageKind.Applicability,
            reference, text)]);

    static Sp63NormalResult NotApplicable(IReadOnlyList<Sp63NormalMessage> messages) =>
        new()
        {
            Status = Sp63NormalStatus.NotApplicable,
            Branch = "not_applicable",
            ApplicabilityMessages = messages.ToList(),
            InformationalMessages = Sp63NormalChecker.SuggestNdmIfNeeded(messages)
        };
}
