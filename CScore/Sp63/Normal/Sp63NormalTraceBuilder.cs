using CScore.CalculationTrace;

namespace CScore.Sp63.Normal;

/// <summary>Строит структурированную трассировку из уже рассчитанных результатов нормальной проверки.</summary>
internal static class Sp63NormalTraceBuilder
{
    /// <summary>Формирует шаги без повторного выполнения нормативных формул.</summary>
    public static List<CalculationTraceStep> Build(string branch,
        IReadOnlyList<CheckDetail> details, IReadOnlyDictionary<string, double> variables)
    {
        if (variables.ContainsKey("M0"))
            return BuildCircular(branch, details, variables);
        if (variables.ContainsKey("bw"))
            return BuildTee(details, variables);
        return branch switch
        {
            "bending" => BuildBending(details, variables),
            "central_tension" => BuildCentralTension(details, variables),
            "compression" => BuildCompression(details, variables),
            _ => BuildGeneric(branch, details)
        };
    }

    static List<CalculationTraceStep> BuildTee(IReadOnlyList<CheckDetail> details,
        IReadOnlyDictionary<string, double> variables)
    {
        var result = new List<CalculationTraceStep>();
        if (Has(variables, "x"))
            result.Add(new CalculationTraceStep
            {
                StepId = "sp63.normal.tee.bending.compression-zone",
                CodeReference = "8.1.11",
                TitleKey = "Sp63Normal_Trace_CompressionZone",
                FormulaLatex = @"x = \frac{R_s A_s - R_{sc} A'_s}{R_b b_f}",
                ResultLatexTemplate = "x = {x}",
                Values = Values(variables, ("Rs", CalculationUnit.Kilopascal),
                    ("As", CalculationUnit.SquareMeter), ("Rsc", CalculationUnit.Kilopascal),
                    ("AsPrime", CalculationUnit.SquareMeter), ("Rb", CalculationUnit.Kilopascal),
                    ("compressionFlangeActualWidth", CalculationUnit.Meter),
                    ("x", CalculationUnit.Meter))
            });
        if (Has(variables, "xi") && Has(variables, "xiR"))
            result.Add(new CalculationTraceStep
            {
                StepId = "sp63.normal.tee.bending.limit-comparison",
                CodeReference = "8.1.11",
                TitleKey = "Sp63Normal_Trace_LimitComparison",
                FormulaLatex = @"\xi \le \xi_R",
                SubstitutionLatexTemplate = @"{xi} \le {xiR}",
                ResultLatexTemplate = @"{xi} \le {xiR}",
                Status = ComparisonStatus(variables, "xi", "xiR"),
                Values = Values(variables, ("xi", CalculationUnit.Unitless),
                    ("xiR", CalculationUnit.Unitless))
            });
        var detail = details.FirstOrDefault();
        if (detail != null)
        {
            result.Add(new CalculationTraceStep
            {
                StepId = "sp63.normal.tee.bending.capacity",
                CodeReference = detail.NormReference,
                TitleKey = "Sp63Normal_Trace_Capacity",
                FormulaLatex = @"M_{ult} = f(R_b, b_f, b_w, x, h_0)",
                ResultLatexTemplate = "M_{ult} = {allowable}",
                Values = new Dictionary<string, TraceValue>(StringComparer.Ordinal)
                {
                    ["allowable"] = new(detail.Allowable, CalculationUnit.KilonewtonMeter)
                }
            });
            result.Add(StrengthStep("tee.bending", detail.NormReference, detail));
        }
        return result;
    }

    static List<CalculationTraceStep> BuildCircular(string branch,
        IReadOnlyList<CheckDetail> details, IReadOnlyDictionary<string, double> variables)
    {
        var result = new List<CalculationTraceStep>();
        if (Has(variables, "M0"))
            result.Add(new CalculationTraceStep
        {
            StepId = $"sp63.normal.{branch}.resultant-moment",
            CodeReference = "Д.1",
            TitleKey = "Sp63Normal_Trace_ResultantMoment",
            FormulaLatex = @"M_0 = \sqrt{M_x^2 + M_y^2}",
            ResultLatexTemplate = "M_0 = {M0}",
            Values = Values(variables, ("Mx", CalculationUnit.KilonewtonMeter),
                ("My", CalculationUnit.KilonewtonMeter),
                ("M0", CalculationUnit.KilonewtonMeter))
            });
        var detail = details.FirstOrDefault();
        if (detail != null)
        {
            result.Add(new CalculationTraceStep
            {
                StepId = $"sp63.normal.{branch}.capacity",
                CodeReference = detail.NormReference,
                TitleKey = "Sp63Normal_Trace_Capacity",
                FormulaLatex = @"M_{ult} = f(R_b, R_s, A, r)",
                ResultLatexTemplate = "M_{ult} = {allowable}",
                Values = new Dictionary<string, TraceValue>(StringComparer.Ordinal)
                {
                    ["allowable"] = new(detail.Allowable, CalculationUnit.KilonewtonMeter)
                }
            });
            result.Add(StrengthStep(branch, detail.NormReference, detail));
        }
        return result;
    }

    static List<CalculationTraceStep> BuildBending(IReadOnlyList<CheckDetail> details,
        IReadOnlyDictionary<string, double> variables)
    {
        var result = new List<CalculationTraceStep>();
        result.Add(new CalculationTraceStep
        {
            StepId = "sp63.normal.bending.compression-zone",
            CodeReference = "8.1.8",
            TitleKey = "Sp63Normal_Trace_CompressionZone",
            FormulaLatex = @"x = \frac{R_s A_s - R_{sc} A'_s}{R_b b}",
            SubstitutionLatexTemplate = @"x = \frac{{Rs} \cdot {As} - {Rsc} \cdot {AsPrime}}{{Rb} \cdot {b}}",
            ResultLatexTemplate = "x = {x}",
            Values = Values(variables, ("Rs", CalculationUnit.Kilopascal),
                ("As", CalculationUnit.SquareMeter), ("Rsc", CalculationUnit.Kilopascal),
                ("AsPrime", CalculationUnit.SquareMeter), ("Rb", CalculationUnit.Kilopascal),
                ("b", CalculationUnit.Meter), ("x", CalculationUnit.Meter))
        });
        result.Add(new CalculationTraceStep
        {
            StepId = "sp63.normal.bending.relative-compression-zone",
            CodeReference = "8.1.8",
            TitleKey = "Sp63Normal_Trace_RelativeCompressionZone",
            FormulaLatex = @"\xi = \frac{x}{h_0}",
            SubstitutionLatexTemplate = @"\xi = \frac{{x}}{{h0}}",
            ResultLatexTemplate = "\\xi = {xi}",
            Values = Values(variables, ("x", CalculationUnit.Meter),
                ("h0", CalculationUnit.Meter), ("xi", CalculationUnit.Unitless))
        });
        result.Add(new CalculationTraceStep
        {
            StepId = "sp63.normal.bending.limit-comparison",
            CodeReference = "8.1.8",
            TitleKey = "Sp63Normal_Trace_LimitComparison",
            FormulaLatex = @"\xi \le \xi_R",
            SubstitutionLatexTemplate = @"{xi} \le {xiR}",
            ResultLatexTemplate = @"{xi} \le {xiR}",
            Status = ComparisonStatus(variables, "xi", "xiR"),
            Values = Values(variables, ("xi", CalculationUnit.Unitless),
                ("xiR", CalculationUnit.Unitless))
        });

        AddCapacityAndStrength(result, "bending", "8.1.8", details, variables);
        return result;
    }

    static List<CalculationTraceStep> BuildCentralTension(IReadOnlyList<CheckDetail> details,
        IReadOnlyDictionary<string, double> variables)
    {
        var result = new List<CalculationTraceStep>();
        var detail = details.FirstOrDefault();
        if (detail != null)
        {
            result.Add(new CalculationTraceStep
            {
                StepId = "sp63.normal.central-tension.capacity",
                CodeReference = "8.1.18",
                TitleKey = "Sp63Normal_Trace_CentralTensionCapacity",
                FormulaLatex = @"N_{ult} = R_s A_{s,tot}",
                SubstitutionLatexTemplate = @"N_{ult} = {Rs} \cdot {totalRebarArea}",
                ResultLatexTemplate = "N_{ult} = {allowable}",
                Values = Values(variables, ("Rs", CalculationUnit.Kilopascal),
                    ("totalRebarArea", CalculationUnit.SquareMeter),
                    ("allowable", CalculationUnit.Kilonewton))
                    .AppendValue("allowable", detail.Allowable, CalculationUnit.Kilonewton)
            });
            result.Add(StrengthStep("central-tension", "8.1.18", detail));
        }
        return result;
    }

    static List<CalculationTraceStep> BuildCompression(IReadOnlyList<CheckDetail> details,
        IReadOnlyDictionary<string, double> variables)
    {
        var result = new List<CalculationTraceStep>();
        if (Has(variables, "x"))
        {
            result.Add(new CalculationTraceStep
            {
                StepId = "sp63.normal.compression.compression-zone",
                CodeReference = "8.1.10",
                TitleKey = "Sp63Normal_Trace_CompressionZone",
                FormulaLatex = @"x = \frac{N + R_s A_s - R_{sc} A'_s}{R_b b}",
                ResultLatexTemplate = "x = {x}",
                Values = Values(variables, ("N", CalculationUnit.Kilonewton),
                    ("Rs", CalculationUnit.Kilopascal), ("As", CalculationUnit.SquareMeter),
                    ("Rsc", CalculationUnit.Kilopascal), ("AsPrime", CalculationUnit.SquareMeter),
                    ("Rb", CalculationUnit.Kilopascal), ("b", CalculationUnit.Meter),
                    ("x", CalculationUnit.Meter))
            });
        }
        if (Has(variables, "xi") && Has(variables, "xiR"))
        {
            result.Add(new CalculationTraceStep
            {
                StepId = "sp63.normal.compression.limit-comparison",
                CodeReference = "8.1.10",
                TitleKey = "Sp63Normal_Trace_LimitComparison",
                FormulaLatex = @"\xi \le \xi_R",
                SubstitutionLatexTemplate = @"{xi} \le {xiR}",
                ResultLatexTemplate = @"{xi} \le {xiR}",
                Status = ComparisonStatus(variables, "xi", "xiR"),
                Values = Values(variables, ("xi", CalculationUnit.Unitless),
                    ("xiR", CalculationUnit.Unitless))
            });
        }
        AddCapacityAndStrength(result, "compression", "8.1.10", details, variables);
        return result;
    }

    static List<CalculationTraceStep> BuildGeneric(string branch,
        IReadOnlyList<CheckDetail> details)
    {
        var result = new List<CalculationTraceStep>();
        for (int i = 0; i < details.Count; i++)
            result.Add(StrengthStep($"{branch}-{i + 1}", details[i].NormReference, details[i]));
        return result;
    }

    static void AddCapacityAndStrength(List<CalculationTraceStep> steps, string branch,
        string reference, IReadOnlyList<CheckDetail> details,
        IReadOnlyDictionary<string, double> variables)
    {
        var detail = details.FirstOrDefault();
        if (detail == null)
            return;

        steps.Add(new CalculationTraceStep
        {
            StepId = $"sp63.normal.{branch}.capacity",
            CodeReference = reference,
            TitleKey = "Sp63Normal_Trace_Capacity",
            FormulaLatex = @"M_{ult} = R_b b x (h_0 - x/2) + R_{sc} A'_s (h_0 - a')",
            ResultLatexTemplate = "M_{ult} = {allowable}",
            Values = Values(variables, ("allowable", CalculationUnit.KilonewtonMeter))
                .AppendValue("allowable", detail.Allowable, CalculationUnit.KilonewtonMeter)
        });
        steps.Add(StrengthStep(branch, reference, detail));
    }

    static CalculationTraceStep StrengthStep(string branch, string reference, CheckDetail detail)
        => new()
        {
            StepId = $"sp63.normal.{branch}.strength",
            CodeReference = reference,
            TitleKey = "Sp63Normal_Trace_Strength",
            FormulaLatex = @"M_{Ed} \le M_{ult}",
            SubstitutionLatexTemplate = @"{applied} \le {allowable}",
            ResultLatexTemplate = "Кисп = {ratio}",
            Status = detail.Passed ? CalculationTraceStatus.Passed : CalculationTraceStatus.Failed,
            Values = new Dictionary<string, TraceValue>(StringComparer.Ordinal)
            {
                ["applied"] = new(detail.Applied, CalculationUnit.KilonewtonMeter),
                ["allowable"] = new(detail.Allowable, CalculationUnit.KilonewtonMeter),
                ["ratio"] = new(detail.Ratio, CalculationUnit.Unitless)
            }
        };

    static CalculationTraceStatus ComparisonStatus(IReadOnlyDictionary<string, double> values,
        string left, string right)
        => Has(values, left) && Has(values, right) && values[left] <= values[right]
            ? CalculationTraceStatus.Passed
            : CalculationTraceStatus.Failed;

    static bool Has(IReadOnlyDictionary<string, double> values, string key)
        => values.TryGetValue(key, out var value) && double.IsFinite(value);

    static Dictionary<string, TraceValue> Values(
        IReadOnlyDictionary<string, double> source,
        params (string Key, CalculationUnit Unit)[] keys)
    {
        var result = new Dictionary<string, TraceValue>(StringComparer.Ordinal);
        foreach (var (key, unit) in keys)
            if (source.TryGetValue(key, out var value))
                result[key] = new TraceValue(value, unit);
        return result;
    }
}

file static class TraceValueDictionaryExtensions
{
    public static Dictionary<string, TraceValue> AppendValue(
        this Dictionary<string, TraceValue> values, string key, double value,
        CalculationUnit unit)
    {
        values[key] = new TraceValue(value, unit);
        return values;
    }
}
