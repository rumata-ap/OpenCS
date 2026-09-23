using CScore.Sp63.CrackWidth;
using CScore.Sp63.Normal;
using CScore.ParametricRc;

namespace CScore.Sp63.Deflection;

/// <summary>Вычисляет кривизну и прогиб прямоугольного сечения по формульному пути СП 63.</summary>
public static class Sp63DeflectionChecker
{
    const double ForceTolerance = 1e-9;
    const double MomentTolerance = 1e-9;

    /// <summary>Выполняет расчёт с полной и длительной нагрузками.</summary>
    public static Sp63DeflectionResult Check(CrossSection section, LoadItem totalLoad,
        LoadItem longLoad, CalcType calc, Sp63DeflectionOptions options)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(totalLoad);
        ArgumentNullException.ThrowIfNull(longLoad);
        ArgumentNullException.ThrowIfNull(options);

        if (options.ShapeKind != Sp63NormalShapeKind.Rectangular)
            return NotApplicable("unsupported_shape", "Sp63Deflection_ShapeNotSupported", "8.2.21");
        if (!Enum.IsDefined(options.Axis) || !Enum.IsDefined(options.Scheme) ||
            !Enum.IsDefined(options.Humidity))
            return InvalidInput("invalid_options", "Sp63Deflection_InvalidOptions", "8.2.21");
        if (!double.IsFinite(options.SpanM) || options.SpanM <= 0)
            return InvalidInput("invalid_span", "Sp63Deflection_InvalidSpan", "8.2.21");
        if (!double.IsFinite(options.DeflectionLimitMm) || options.DeflectionLimitMm <= 0)
            return InvalidInput("invalid_deflection_limit", "Sp63Deflection_InvalidLimit", "8.2.21");
        if (!double.IsFinite(options.LongTermShare) || options.LongTermShare is < 0 or > 1)
            return InvalidInput("invalid_long_term_share", "Sp63Deflection_InvalidLongTermShare", "8.2.31");
        if (!Finite(totalLoad.N, totalLoad.Mx, totalLoad.My, longLoad.N, longLoad.Mx, longLoad.My))
            return InvalidInput("non_finite_load", "Sp63Deflection_NonFiniteLoad", "8.2.21");

        double moment = options.Axis == Sp63NormalAxis.Mx ? totalLoad.Mx : totalLoad.My;
        double otherMoment = options.Axis == Sp63NormalAxis.Mx ? totalLoad.My : totalLoad.Mx;
        double longMoment = options.Axis == Sp63NormalAxis.Mx ? longLoad.Mx : longLoad.My;
        double otherLongMoment = options.Axis == Sp63NormalAxis.Mx ? longLoad.My : longLoad.Mx;
        if (Math.Abs(otherMoment) > MomentTolerance || Math.Abs(otherLongMoment) > MomentTolerance)
            return NotApplicable("biaxial_load", "Sp63Deflection_BiaxialLoad", "8.2.21");
        if (Math.Abs(moment) <= MomentTolerance)
            return NotApplicable("zero_moment", "Sp63Deflection_ZeroMoment", "8.2.23");
        if (Math.Abs(longMoment) > MomentTolerance && Math.Sign(longMoment) != Math.Sign(moment))
            return NotApplicable("long_moment_reversed", "Sp63Deflection_LongMomentReversed", "8.2.31");
        if (Math.Abs(longMoment) > Math.Abs(moment) + MomentTolerance)
            return InvalidInput("long_moment_exceeds_total", "Sp63Deflection_LongMomentExceedsTotal", "8.2.31");

        var parametric = ParametricRebarApplicability.Evaluate(section, "sp63_deflection", totalLoad, options.Axis);
        if (!parametric.IsApplicable)
            return NotApplicable(parametric.ReasonCode ?? "idealized_rebar_not_applicable",
                ParametricText(parametric.Reason), "8.1.8");

        var analysis = Sp63RebarLayoutAnalyzer.Analyze(section, options.Axis, calc,
            Math.Sign(moment), requireAtLeastTwoLayers: true);
        if (analysis.Profile is null)
            return NotApplicable(analysis.Messages.Select(m => new Sp63DeflectionMessage(
                m.Code, Sp63DeflectionMessageKind.Applicability, m.NormReference, m.Text)).ToList());
        var profile = analysis.Profile;

        var concreteArea = section.Areas.FirstOrDefault(area =>
            area.Category == AreaCategory.Region && area.Material?.Type == MatType.Concrete);
        var concreteChars = concreteArea?.Material?.GetChars(calc);
        if (concreteChars is null || !Positive(concreteChars.E) ||
            !Positive(Math.Abs(concreteChars.Fc)) || !Positive(concreteChars.Ft))
            return NotApplicable("missing_concrete_chars", "Sp63Deflection_MissingConcreteChars", "8.2.23");

        var rebarArea = section.Areas.FirstOrDefault(area =>
            area.Category == AreaCategory.RebarGroup && area.Material != null);
        var rebarChars = rebarArea?.Material?.GetChars(calc);
        if (rebarChars is null || !Positive(rebarChars.E) || !Positive(Math.Abs(rebarChars.Ft)))
            return NotApplicable("missing_rebar_chars", "Sp63Deflection_MissingRebarChars", "8.2.23");

        double b = profile.B;
        if (!Positive(b))
            return InvalidInput("invalid_section_width", "Sp63Deflection_InvalidGeometry", "8.2.23");
        double asT = profile.TensionLayer.Area / b;
        double asC = profile.CompressionLayer.Area / b;
        const double phi1 = 1.0;
        const double phi2 = 0.5;
        const double acrcLimMm = 0.3;
        const double effectiveDiameterM = 0.012;
        ShellSimplStripResult Strip(double m, double n) => ShellSimplSolver.ComputeStripSls(
            m / b, n / b, profile.Height, profile.H0, profile.APrime, asT, asC,
            effectiveDiameterM, concreteChars, rebarChars, phi1, phi2, acrcLimMm,
            SigmaSCrcMethod.ReleasedConcrete8137, WplGammaMethod.Sp63);

        var fullStrip = Strip(Math.Abs(moment), totalLoad.N);
        var longStrip = Strip(Math.Abs(longMoment), longLoad.N);
        if (!Finite(fullStrip.Mcrc, longStrip.Mcrc))
            return InvalidInput("non_finite_result", "Sp63Deflection_NonFiniteResult", "8.2.23");
        double mcrcFull = fullStrip.Mcrc * b;
        double mcrcLong = longStrip.Mcrc * b;
        var curvature = Sp63Curvature.Compute(
            new Sp63CurvatureInput(b, profile.Height, profile.H0, profile.APrime,
                profile.TensionLayer.Area, profile.CompressionLayer.Area,
                concreteChars.E, Math.Abs(concreteChars.Fc), rebarChars.E,
                concreteChars.Class, options.Humidity),
            new Sp63CurvatureLoad(Math.Abs(moment), totalLoad.N),
            new Sp63CurvatureLoad(Math.Abs(longMoment), longLoad.N),
            fullStrip.Cracked, mcrcFull, mcrcLong);
        if (curvature is null)
        {
            string code = concreteChars.Class > 0 ? "curvature_through_tension" : "missing_concrete_class";
            string text = concreteChars.Class > 0
                ? "Sp63Deflection_CurvatureThroughTension"
                : "Sp63Deflection_MissingConcreteClass";
            return NotApplicable(code, text, "8.2.23");
        }

        double coefficient = Sp63DeflectionScheme.Coefficient(options.Scheme);
        double deflection = 1000.0 * coefficient * options.SpanM * options.SpanM * Math.Abs(curvature.Total);
        if (!double.IsFinite(deflection))
            return InvalidInput("non_finite_result", "Sp63Deflection_NonFiniteResult", "8.2.21");
        bool passed = deflection <= options.DeflectionLimitMm + 1e-9;
        var variables = new Dictionary<string, double>
        {
            ["N"] = totalLoad.N, ["M"] = moment, ["Nl"] = longLoad.N, ["Ml"] = longMoment,
            ["axis"] = options.Axis == Sp63NormalAxis.Mx ? 0.0 : 1.0,
            ["S"] = coefficient, ["l"] = options.SpanM, ["f"] = deflection,
            ["fult"] = options.DeflectionLimitMm, ["utilization"] = deflection / options.DeflectionLimitMm,
            ["McrcFull"] = mcrcFull, ["McrcLong"] = mcrcLong, ["b"] = b
        };
        return new Sp63DeflectionResult
        {
            Status = Sp63DeflectionStatus.Calculated,
            Curvature = curvature,
            Scheme = options.Scheme,
            CoefficientS = coefficient,
            SpanM = options.SpanM,
            DeflectionMm = deflection,
            DeflectionLimitMm = options.DeflectionLimitMm,
            Utilization = deflection / options.DeflectionLimitMm,
            DeflectionPassed = passed,
            Cracked = fullStrip.Cracked,
            Branch = fullStrip.Cracked ? "cracked" : "not_cracked",
            Variables = variables,
            InformationalMessages = [new("constant_stiffness_scheme", Sp63DeflectionMessageKind.Information,
                "8.2.21", "Sp63Deflection_ConstantStiffnessNote")]
        };
    }

    static string ParametricText(ParametricRebarApplicabilityReason reason) => reason switch
    {
        ParametricRebarApplicabilityReason.AxisMismatch => "Sp63Deflection_IdealizedAxisMismatch",
        ParametricRebarApplicabilityReason.BiaxialLoad => "Sp63Deflection_BiaxialLoad",
        ParametricRebarApplicabilityReason.InconsistentAxis => "Sp63Deflection_IdealizedAxisInconsistent",
        _ => "Sp63Deflection_IdealizedRebarNotSupported"
    };

    static Sp63DeflectionResult InvalidInput(string code, string text, string reference) => new()
    {
        Status = Sp63DeflectionStatus.InvalidInput,
        Branch = "invalid_input",
        ApplicabilityMessages = [Message(code, text, reference)]
    };

    static Sp63DeflectionResult NotApplicable(string code, string text, string reference) =>
        NotApplicable([Message(code, text, reference)]);

    static Sp63DeflectionResult NotApplicable(List<Sp63DeflectionMessage> messages) => new()
    {
        Status = Sp63DeflectionStatus.NotApplicable,
        Branch = "not_applicable",
        ApplicabilityMessages = messages
    };

    static Sp63DeflectionMessage Message(string code, string text, string reference) =>
        new(code, Sp63DeflectionMessageKind.Applicability, reference, text);
    static bool Positive(double value) => double.IsFinite(value) && value > 0;
    static bool Finite(params double[] values) => values.All(double.IsFinite);
}
