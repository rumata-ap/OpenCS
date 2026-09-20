using CScore.CalculationTrace;

namespace CScore.Sp63Shear;

/// <summary>Строит трассировку выбранных проверок наклонного сечения.</summary>
internal static class ShearInclinedTraceBuilder
{
    /// <summary>Формирует шаги по агрегированным критическим деталям и стоянкам.</summary>
    public static IReadOnlyList<CalculationTraceStep> Build(
        ShearPlane plane, IReadOnlyList<CheckDetail> details,
        IReadOnlyList<StationResult> stations)
    {
        var result = new List<CalculationTraceStep>();
        string planeKey = plane == ShearPlane.Vy ? "vy" : "vx";
        for (int detailIndex = 0; detailIndex < details.Count; detailIndex++)
        {
            var detail = details[detailIndex];
            int stationIndex = FindStationIndex(detail, stations);
            string id = $"sp63.shear.{planeKey}.{detail.Formula}.{stationIndex}.strength";
            bool normative = detail.Formula is "8.55" or "8.56" or "8.63";
            var values = new Dictionary<string, TraceValue>(StringComparer.Ordinal)
            {
                ["applied"] = new(detail.Applied, UnitFor(detail.Formula, "applied")),
                ["allowable"] = new(detail.Allowable, UnitFor(detail.Formula, "allowable")),
                ["ratio"] = new(detail.Ratio, CalculationUnit.Unitless)
            };
            foreach (var pair in detail.Variables)
                values[pair.Key] = new TraceValue(pair.Value, UnitFor(detail.Formula, pair.Key));

            double? station = TryValue(detail, "s");
            double? criticalC = TryValue(detail, "C");
            result.Add(new CalculationTraceStep
            {
                StepId = id,
                CodeReference = detail.NormReference,
                TitleKey = "Sp63Shear_Trace_Check",
                ExplanationKey = normative
                    ? "Sp63Shear_Trace_NormativeCheck"
                    : "Sp63Shear_Trace_ReferenceCheck",
                FormulaCode = detail.Formula,
                Plane = planeKey,
                StationIndex = stationIndex >= 0 ? stationIndex : null,
                StationS = station is double s ? new TraceValue(s, CalculationUnit.Meter) : null,
                CriticalC = criticalC is double c ? new TraceValue(c, CalculationUnit.Meter) : null,
                FormulaLatex = Formula(detail.Formula),
                SubstitutionLatexTemplate = @"{applied} \le {allowable}",
                ResultLatexTemplate = @"Кисп = {ratio}",
                Values = values,
                Status = normative
                    ? (detail.Passed ? CalculationTraceStatus.Passed : CalculationTraceStatus.Failed)
                    : CalculationTraceStatus.Informational
            });
        }
        return result;
    }

    static string Formula(string code) => code switch
    {
        "8.55" => @"Q \le Q_{b,max}",
        "8.56" => @"Q \le Q_b + Q_{sw}",
        "8.60" => @"Q \le Q_{b,min} + Q_{sw,min}",
        "8.63" => @"M \le M_s + M_{sw}",
        "8.63s" => @"M \le M_s + M_{sw}\;(C = 2h_0)",
        _ => @"A_{Ed} \le A_{Rd}"
    };

    static int FindStationIndex(CheckDetail detail, IReadOnlyList<StationResult> stations)
    {
        if (!double.IsFinite(TryValue(detail, "s") ?? double.NaN) || stations.Count == 0)
            return -1;
        double s = TryValue(detail, "s")!.Value;
        int index = 0;
        double distance = double.PositiveInfinity;
        for (int i = 0; i < stations.Count; i++)
        {
            double candidate = Math.Abs(stations[i].S - s);
            if (candidate < distance)
            {
                distance = candidate;
                index = i;
            }
        }
        return index;
    }

    static double? TryValue(CheckDetail detail, string key)
        => detail.Variables.TryGetValue(key, out var value) && double.IsFinite(value)
            ? value : null;

    static CalculationUnit UnitFor(string formula, string key)
    {
        if (key is "s" or "C" or "d" or "b" or "h0")
            return CalculationUnit.Meter;
        if (key is "Ms" or "Msw" or "mApplied" ||
            ((key is "applied" or "allowable") &&
             (formula is "8.63" or "8.63s")))
            return CalculationUnit.KilonewtonMeter;
        if (key is "Q" or "Qb" or "Qsw" or "Qb,min" or "Qsw,min" or "applied" or "allowable")
            return CalculationUnit.Kilonewton;
        if (key is "Rb")
            return CalculationUnit.Kilopascal;
        return CalculationUnit.Unitless;
    }
}
