using CScore.CalculationTrace;

namespace CScore.Sp63Punching;

/// <summary>Пошаговая трассировка расчёта на продавливание для отчёта.</summary>
internal static class PunchingTrace
{
    public static IReadOnlyList<CalculationTraceStep> Build(
        PunchingVariant v, ContourProperties g, double wbx, double wby, double h0, double rbt,
        double qsw, double fsw, bool swCounted, double fb, double mbx, double mby, double mswx, double mswy,
        double fult, double mxult, double myult, double f, double fred, double mx0, double my0,
        double ex, double ey, double mxStar, double myStar, double kF, double kMRaw, double kM, double k)
    {
        string p = $"sp63.punching.{v.Id}";
        bool hasM = mxStar > 0 || myStar > 0;
        var steps = new List<CalculationTraceStep>
        {
            Step($"{p}.contour", "п. 8.1.46, (8.99)", "Punching_Trace_Contour",
                @"u = \sum L_i;\quad x_c = \frac{\sum L_i x_i}{\sum L_i}",
                @"u = {u};\quad x_c = {xc};\quad y_c = {yc}",
                ("u", g.U, CalculationUnit.Meter), ("xc", g.Xc, CalculationUnit.Meter), ("yc", g.Yc, CalculationUnit.Meter)),
            Step($"{p}.inertia", "п. 8.1.51, (8.98)", "Punching_Trace_SectionModulus",
                @"W_{bx} = \frac{I_{bx}}{y_{max}};\quad W_{by} = \frac{I_{by}}{x_{max}}",
                @"W_{bx} = \frac{{Ibx}}{{ymax}} = {Wbx};\quad W_{by} = \frac{{Iby}}{{xmax}} = {Wby}",
                ("Ibx", g.Ibx, CalculationUnit.CubicMeter), ("Iby", g.Iby, CalculationUnit.CubicMeter),
                ("ymax", wbx > 0 ? g.Ibx / wbx : 0, CalculationUnit.Meter),
                ("xmax", wby > 0 ? g.Iby / wby : 0, CalculationUnit.Meter),
                ("Wbx", wbx, CalculationUnit.SquareMeter), ("Wby", wby, CalculationUnit.SquareMeter)),
            Step($"{p}.moments", "п. 8.1.46", "Punching_Trace_Eccentricity",
                @"M_x^* = M_x \pm F e_y;\quad M_y^* = M_y \pm F e_x",
                @"e_x = {ex};\ e_y = {ey};\ M_x^* = {Mx};\ M_y^* = {My}",
                ("ex", ex, CalculationUnit.Meter), ("ey", ey, CalculationUnit.Meter),
                ("Mx0", mx0, CalculationUnit.KilonewtonMeter), ("My0", my0, CalculationUnit.KilonewtonMeter),
                ("Mx", mxStar, CalculationUnit.KilonewtonMeter), ("My", myStar, CalculationUnit.KilonewtonMeter)),
            Step($"{p}.fb", "(8.88), (8.89)", "Punching_Trace_Fb",
                @"F_{b,ult} = R_{bt} u h_0",
                @"F_{b,ult} = {Rbt} \cdot {u} \cdot {h0} = {Fb}",
                ("Rbt", rbt, CalculationUnit.Kilopascal), ("u", g.U, CalculationUnit.Meter),
                ("h0", h0, CalculationUnit.Meter), ("Fb", fb, CalculationUnit.Kilonewton)),
            Step($"{p}.mb", "(8.94)", "Punching_Trace_Mb",
                @"M_{b,ult} = R_{bt} W_b h_0",
                @"M_{bx,ult} = {Mbx};\quad M_{by,ult} = {Mby}",
                ("Mbx", mbx, CalculationUnit.KilonewtonMeter), ("Mby", mby, CalculationUnit.KilonewtonMeter)),
        };

        if (qsw > 0)
            steps.Add(Step($"{p}.fsw", "(8.91), (8.92), (8.97), п. 8.1.48", "Punching_Trace_Fsw",
                @"F_{sw,ult} = 0{,}8 q_{sw} u \ge 0{,}25 F_{b,ult};\quad M_{sw,ult} = 0{,}8 q_{sw} W_{sw}",
                @"q_{sw} = {qsw};\ F_{sw,ult} = {Fsw};\ M_{swx,ult} = {Mswx};\ M_{swy,ult} = {Mswy}",
                swCounted ? CalculationTraceStatus.Passed : CalculationTraceStatus.NotApplicable,
                ("qsw", qsw, CalculationUnit.KilonewtonPerMeter), ("Fsw", fsw, CalculationUnit.Kilonewton),
                ("Mswx", mswx, CalculationUnit.KilonewtonMeter), ("Mswy", mswy, CalculationUnit.KilonewtonMeter)));

        steps.Add(Step($"{p}.ult", "п. 8.1.48, п. 8.1.50", "Punching_Trace_Limits",
            @"F_{ult} = \min(F_{b,ult} + F_{sw,ult};\ 2F_{b,ult})",
            @"F_{ult} = {Fult};\ M_{x,ult} = {Mxult};\ M_{y,ult} = {Myult}",
            ("Fult", fult, CalculationUnit.Kilonewton), ("Mxult", mxult, CalculationUnit.KilonewtonMeter),
            ("Myult", myult, CalculationUnit.KilonewtonMeter)));

        string formula = hasM
            ? (qsw > 0 ? "(8.96)" : "(8.95)")
            : (qsw > 0 ? "(8.90)" : "(8.87)");
        steps.Add(Step($"{p}.check", formula, "Punching_Trace_Check",
            @"\frac{F}{F_{ult}} + \min\left(\frac{M_x}{M_{x,ult}} + \frac{M_y}{M_{y,ult}};\ 0{,}5\frac{F}{F_{ult}}\right) \le 1",
            @"\frac{{F}}{{Fult}} + \min({kMRaw};\ 0{,}5 \cdot {kF}) = {K}",
            k <= 1.0 ? CalculationTraceStatus.Passed : CalculationTraceStatus.Failed,
            ("F", fred, CalculationUnit.Kilonewton), ("Fapplied", f, CalculationUnit.Kilonewton),
            ("Fult", fult, CalculationUnit.Kilonewton), ("kF", kF, CalculationUnit.Unitless),
            ("kMRaw", kMRaw, CalculationUnit.Unitless), ("kM", kM, CalculationUnit.Unitless),
            ("K", k, CalculationUnit.Unitless)));
        return steps;
    }

    public static IReadOnlyList<CalculationTraceStep> BuildReduced(
        PunchingVariant v, GalyautdinovPerimeter gp, double factor, double h0, double rbt,
        double fb, double fsw, double fult, double fred, double kF)
    {
        string p = $"sp63.punching.{v.Id}";
        return new List<CalculationTraceStep>
        {
            Step($"{p}.ured", "Галяутдинов З. Ш., 2022, (5.9), (5.33)", "Punching_Trace_ReducedPerimeter",
                @"u_{red} = 2C_{min}(\chi K_{ls} n + K_{shs}) + 4h_0",
                @"n = {n};\ \chi = {chi};\ K_{ls} = {Kls};\ K_{shs} = {Kshs};\ u_{red} = {ured}",
                ("n", gp.N, CalculationUnit.Unitless), ("chi", gp.Chi, CalculationUnit.Unitless),
                ("Kls", gp.Kls, CalculationUnit.Unitless), ("Kshs", gp.Kshs, CalculationUnit.Unitless),
                ("ured", gp.URed, CalculationUnit.Meter)),
            Step($"{p}.check", "Галяутдинов З. Ш., 2022, (5.34), (5.35); справочно, моменты не рассматриваются",
                "Punching_Trace_ReducedCheck",
                @"F \le R_{bt} u_{red} h_0 (100\mu)^{1/3}",
                @"F_{b,ult} = {Rbt} \cdot {ured} \cdot {h0} \cdot {factor} = {Fb};\ \frac{{F}}{{Fult}} = {K}",
                kF <= 1.0 ? CalculationTraceStatus.Passed : CalculationTraceStatus.Failed,
                ("Rbt", rbt, CalculationUnit.Kilopascal), ("ured", gp.URed, CalculationUnit.Meter),
                ("h0", h0, CalculationUnit.Meter), ("factor", factor, CalculationUnit.Unitless),
                ("Fb", fb, CalculationUnit.Kilonewton), ("Fsw", fsw, CalculationUnit.Kilonewton),
                ("Fult", fult, CalculationUnit.Kilonewton), ("F", fred, CalculationUnit.Kilonewton),
                ("K", kF, CalculationUnit.Unitless))
        };
    }

    static CalculationTraceStep Step(string id, string reference, string titleKey, string formula, string subst,
        params (string Key, double Value, CalculationUnit Unit)[] values)
        => Step(id, reference, titleKey, formula, subst, CalculationTraceStatus.Informational, values);

    static CalculationTraceStep Step(string id, string reference, string titleKey, string formula, string subst,
        CalculationTraceStatus status, params (string Key, double Value, CalculationUnit Unit)[] values)
        => new()
        {
            StepId = id,
            CodeReference = reference,
            TitleKey = titleKey,
            FormulaLatex = formula,
            SubstitutionLatexTemplate = subst,
            Status = status,
            Values = values.ToDictionary(x => x.Key, x => new TraceValue(x.Value, x.Unit), StringComparer.Ordinal)
        };
}
