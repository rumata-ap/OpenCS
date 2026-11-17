using CScore.CalculationTrace;

namespace CScore.Sp63Punching;

/// <summary>Данные одного варианта контура для трассировки.</summary>
internal sealed record PunchingTraceData(
    PunchingVariant Variant, ContourProperties G, double Wbx, double Wby, double H0, double Rbt,
    PunchingShearReinforcement? Reinforcement, double Qsw, double FswRaw, double Fsw, bool SwCounted,
    double Fb, double Mbx, double Mby, double Mswx, double Mswy,
    double Fult, double Mxult, double Myult, double F, double Fred, double Mx0, double My0,
    PlanPoint AreaCentroid, double Ex, double Ey, double SignLoad,
    EccentricityMode ModeX, EccentricityMode ModeY,
    double MxStar, double MyStar, double KF, double KMRaw, double KM, double K,
    PunchingSwDetail? Sw = null, PunchingRowCount? Rows = null, PunchingWall? Wall = null);

/// <summary>
/// Пошаговая трассировка расчёта на продавливание для отчёта. Каждый шаг — одна величина в форме
/// «обозначение = формула» (<see cref="CalculationTraceStep.FormulaLatex"/>), численная подстановка
/// (<see cref="CalculationTraceStep.SubstitutionLatexTemplate"/>, может быть пустой) и результат
/// (<see cref="CalculationTraceStep.ResultLatexTemplate"/> — один маркер значения). Маркеры {name}
/// ссылаются на <see cref="CalculationTraceStep.Values"/>. Отчёт склеивает их через «=».
/// </summary>
internal static class PunchingTrace
{
    const int MaxListedElements = 12;

    public static IReadOnlyList<CalculationTraceStep> Build(PunchingTraceData d)
    {
        var v = d.Variant;
        var g = d.G;
        string p = $"sp63.punching.{v.Id}";
        var steps = new List<CalculationTraceStep>();
        bool hasM = d.MxStar > 0 || d.MyStar > 0 || d.Mx0 != 0 || d.My0 != 0;

        // u = ΣLi
        var lengths = v.Contour.Elements.Select(e => e.Length).ToList();
        var uValues = new List<(string, double, CalculationUnit)> { ("u", g.U, CalculationUnit.Meter) };
        string uSubst = "";
        if (lengths.Count > 1 && lengths.Count <= MaxListedElements)
        {
            for (int i = 0; i < lengths.Count; i++) uValues.Add(($"L{i + 1}", lengths[i], CalculationUnit.Meter));
            uSubst = string.Join(" + ", Enumerable.Range(1, lengths.Count).Select(i => $"{{L{i}}}"));
        }
        steps.Add(Step($"{p}.u", "п. 8.1.46", "Punching_Trace_U", @"u = \sum L_i", uSubst, "{u}", uValues.ToArray()));

        // Центр тяжести (8.99)
        steps.Add(Step($"{p}.xc", "п. 8.1.51, (8.99)", "Punching_Trace_Xc",
            @"x_c = \frac{\sum L_i x_i}{\sum L_i}", @"\frac{{SLx}}{{u}}", "{xc}",
            ("SLx", g.Xc * g.U, CalculationUnit.SquareMeter), ("u", g.U, CalculationUnit.Meter), ("xc", g.Xc, CalculationUnit.Meter)));
        steps.Add(Step($"{p}.yc", "п. 8.1.51, (8.99)", "Punching_Trace_Yc",
            @"y_c = \frac{\sum L_i y_i}{\sum L_i}", @"\frac{{SLy}}{{u}}", "{yc}",
            ("SLy", g.Yc * g.U, CalculationUnit.SquareMeter), ("u", g.U, CalculationUnit.Meter), ("yc", g.Yc, CalculationUnit.Meter)));

        // Моменты инерции и сопротивления (8.98)
        double ymax = d.Wbx > 0 ? g.Ibx / d.Wbx : 0, xmax = d.Wby > 0 ? g.Iby / d.Wby : 0;
        steps.Add(Step($"{p}.ibx", "п. 8.1.51", "Punching_Trace_Ibx",
            @"I_{bx} = \int_u (y - y_c)^2\,dl", "", "{Ibx}", ("Ibx", g.Ibx, CalculationUnit.CubicMeter)));
        steps.Add(Step($"{p}.iby", "п. 8.1.51", "Punching_Trace_Iby",
            @"I_{by} = \int_u (x - x_c)^2\,dl", "", "{Iby}", ("Iby", g.Iby, CalculationUnit.CubicMeter)));
        steps.Add(Step($"{p}.wbx", "п. 8.1.51, (8.98)", "Punching_Trace_Wbx",
            @"W_{bx} = \frac{I_{bx}}{y_{max}}", @"\frac{{Ibx}}{{ymax}}", "{Wbx}",
            ("Ibx", g.Ibx, CalculationUnit.CubicMeter), ("ymax", ymax, CalculationUnit.Meter), ("Wbx", d.Wbx, CalculationUnit.SquareMeter)));
        steps.Add(Step($"{p}.wby", "п. 8.1.51, (8.98)", "Punching_Trace_Wby",
            @"W_{by} = \frac{I_{by}}{x_{max}}", @"\frac{{Iby}}{{xmax}}", "{Wby}",
            ("Iby", g.Iby, CalculationUnit.CubicMeter), ("xmax", xmax, CalculationUnit.Meter), ("Wby", d.Wby, CalculationUnit.SquareMeter)));

        // Узел стены (СП 430 п. 6.3.8): какие моменты учитываются.
        bool wallNode = d.Wall is not null && v.Kind is PunchingVariantKind.WallEnd or PunchingVariantKind.WallEndEdge or PunchingVariantKind.WallCorner;
        bool corner = wallNode && d.Wall!.Kind == PunchingWallKind.Corner;
        if (wallNode)
            steps.Add(corner
                ? Step($"{p}.wall", "СП 430 п. 6.3.8, рис. 6.2 " + PunchingWallGeometry.FigureEdition(d.Wall!), "Punching_Trace_WallCorner", @"M_x = M_y = 0", "", "", ("F", d.F, CalculationUnit.Kilonewton))
                : Step($"{p}.wall", "СП 430 п. 6.3.8, рис. 6.1", "Punching_Trace_WallEnd",
                    @"M_x = \frac{\Delta M}{2},\ M_y = 0,\ e_x = 0", @"{dM} / 2", "{Mx}",
                    ("dM", 2 * d.Mx0, CalculationUnit.KilonewtonMeter), ("Mx", d.Mx0, CalculationUnit.KilonewtonMeter)));

        // Эксцентриситет и моменты относительно центра тяжести контура (п. 8.1.46)
        if (!wallNode)
        steps.Add(Step($"{p}.ex", "п. 8.1.46", "Punching_Trace_Ex",
            @"e_x = x_c - x_0", @"{xc} - {x0}", "{ex}",
            ("xc", g.Xc, CalculationUnit.Meter), ("x0", d.AreaCentroid.X, CalculationUnit.Meter), ("ex", d.Ex, CalculationUnit.Meter)));
        if (!corner)
        steps.Add(Step($"{p}.ey", "п. 8.1.46", "Punching_Trace_Ey",
            @"e_y = y_c - y_0", @"{yc} - {y0}", "{ey}",
            ("yc", g.Yc, CalculationUnit.Meter), ("y0", d.AreaCentroid.Y, CalculationUnit.Meter), ("ey", d.Ey, CalculationUnit.Meter)));
        if (!corner) steps.Add(MomentStep($"{p}.mx", "x", d.ModeX, d.Mx0, d.F, d.Ey, +d.SignLoad, d.MxStar));
        if (!wallNode) steps.Add(MomentStep($"{p}.my", "y", d.ModeY, d.My0, d.F, d.Ex, -d.SignLoad, d.MyStar));

        // Бетон (8.88), (8.89), (8.94)
        steps.Add(Step($"{p}.fb", "(8.88), (8.89)", "Punching_Trace_Fb",
            @"F_{b,ult} = R_{bt}\,u\,h_0", @"{Rbt} \cdot {u} \cdot {h0}", "{Fb}",
            ("Rbt", d.Rbt, CalculationUnit.Kilopascal), ("u", g.U, CalculationUnit.Meter),
            ("h0", d.H0, CalculationUnit.Meter), ("Fb", d.Fb, CalculationUnit.Kilonewton)));
        steps.Add(Step($"{p}.mbx", "(8.94)", "Punching_Trace_Mbx",
            @"M_{bx,ult} = R_{bt}\,W_{bx}\,h_0", @"{Rbt} \cdot {Wbx} \cdot {h0}", "{Mbx}",
            ("Rbt", d.Rbt, CalculationUnit.Kilopascal), ("Wbx", d.Wbx, CalculationUnit.SquareMeter),
            ("h0", d.H0, CalculationUnit.Meter), ("Mbx", d.Mbx, CalculationUnit.KilonewtonMeter)));
        steps.Add(Step($"{p}.mby", "(8.94)", "Punching_Trace_Mby",
            @"M_{by,ult} = R_{bt}\,W_{by}\,h_0", @"{Rbt} \cdot {Wby} \cdot {h0}", "{Mby}",
            ("Rbt", d.Rbt, CalculationUnit.Kilopascal), ("Wby", d.Wby, CalculationUnit.SquareMeter),
            ("h0", d.H0, CalculationUnit.Meter), ("Mby", d.Mby, CalculationUnit.KilonewtonMeter)));

        // Поперечная арматура (8.91), (8.92), (8.97)
        if (d.Sw is { } sw && d.Reinforcement is { } r)
            AddSwSteps(steps, p, d, sw, r);
        // Предельные усилия (п. 8.1.48, п. 8.1.50)
        if (d.SwCounted)
        {
            steps.Add(Step($"{p}.fult", "п. 8.1.48", "Punching_Trace_Fult",
                @"F_{ult} = \min(F_{b,ult} + F_{sw,ult};\ 2F_{b,ult})", @"\min({Fb} + {Fsw};\ 2 \cdot {Fb})", "{Fult}",
                ("Fb", d.Fb, CalculationUnit.Kilonewton), ("Fsw", d.Fsw, CalculationUnit.Kilonewton), ("Fult", d.Fult, CalculationUnit.Kilonewton)));
            steps.Add(Step($"{p}.mxult", "п. 8.1.50", "Punching_Trace_Mxult",
                @"M_{x,ult} = \min(M_{bx,ult} + M_{swx,ult};\ 2M_{bx,ult})", @"\min({Mbx} + {Mswx};\ 2 \cdot {Mbx})", "{Mxult}",
                ("Mbx", d.Mbx, CalculationUnit.KilonewtonMeter), ("Mswx", d.Mswx, CalculationUnit.KilonewtonMeter), ("Mxult", d.Mxult, CalculationUnit.KilonewtonMeter)));
            steps.Add(Step($"{p}.myult", "п. 8.1.50", "Punching_Trace_Myult",
                @"M_{y,ult} = \min(M_{by,ult} + M_{swy,ult};\ 2M_{by,ult})", @"\min({Mby} + {Mswy};\ 2 \cdot {Mby})", "{Myult}",
                ("Mby", d.Mby, CalculationUnit.KilonewtonMeter), ("Mswy", d.Mswy, CalculationUnit.KilonewtonMeter), ("Myult", d.Myult, CalculationUnit.KilonewtonMeter)));
        }

        // Условие прочности (8.87)/(8.90)/(8.93)/(8.95)/(8.96)
        steps.Add(Step($"{p}.kf", d.SwCounted ? "(8.90)" : "(8.87)", "Punching_Trace_KF",
            @"k_F = \frac{F}{F_{ult}}", @"\frac{{F}}{{Fult}}", "{kF}",
            ("F", d.Fred, CalculationUnit.Kilonewton), ("Fult", d.Fult, CalculationUnit.Kilonewton), ("kF", d.KF, CalculationUnit.Unitless)));
        if (hasM)
            steps.Add(Step($"{p}.km", "(8.93)", "Punching_Trace_KM",
                @"k_M = \frac{M_x^*}{M_{x,ult}} + \frac{M_y^*}{M_{y,ult}}", @"\frac{{Mx}}{{Mxult}} + \frac{{My}}{{Myult}}", "{kMRaw}",
                ("Mx", d.MxStar, CalculationUnit.KilonewtonMeter), ("Mxult", d.Mxult, CalculationUnit.KilonewtonMeter),
                ("My", d.MyStar, CalculationUnit.KilonewtonMeter), ("Myult", d.Myult, CalculationUnit.KilonewtonMeter),
                ("kMRaw", d.KMRaw, CalculationUnit.Unitless)));

        string formula = hasM ? (d.SwCounted ? "(8.96)" : "(8.95)") : (d.SwCounted ? "(8.90)" : "(8.87)");
        steps.Add(Step($"{p}.check", formula, "Punching_Trace_Check",
            hasM ? @"K = k_F + \min(k_M;\ 0{,}5\,k_F)" : @"K = k_F",
            hasM ? @"{kF} + \min({kMRaw};\ 0{,}5 \cdot {kF})" : "", "{K}",
            d.K <= 1.0 ? CalculationTraceStatus.Passed : CalculationTraceStatus.Failed,
            ("F", d.Fred, CalculationUnit.Kilonewton), ("Fapplied", d.F, CalculationUnit.Kilonewton),
            ("Fult", d.Fult, CalculationUnit.Kilonewton), ("kF", d.KF, CalculationUnit.Unitless),
            ("kMRaw", d.KMRaw, CalculationUnit.Unitless), ("kM", d.KM, CalculationUnit.Unitless),
            ("K", d.K, CalculationUnit.Unitless)) with { FormulaCode = formula });
        return steps;
    }

    static void AddSwSteps(List<CalculationTraceStep> steps, string p, PunchingTraceData d, PunchingSwDetail sw, PunchingShearReinforcement r)
    {
        var g = d.G;
        var counted = d.SwCounted ? CalculationTraceStatus.Passed : CalculationTraceStatus.NotApplicable;
        if (d.Rows is { } rc && r.Rows is { } rows)
            steps.Add(Step($"{p}.asw", "п. 8.1.48, п. 10.3.17", "Punching_Trace_AswRows",
                @"A_{sw} = n_{sw}\,A_{s1}", @"{n} \cdot {As1}", "{Asw}",
                ("n", rc.Counted, CalculationUnit.Count), ("nTotal", rc.Total, CalculationUnit.Count),
                ("As1", rows.BarArea, CalculationUnit.SquareMeter), ("Asw", sw.AswLine, CalculationUnit.SquareMeter)));

        if (!sw.Cross)
        {
            steps.Add(Step($"{p}.qsw", "(8.92)", "Punching_Trace_Qsw",
                @"q_{sw} = \frac{R_{sw} A_{sw}}{s_w}", @"\frac{{Rsw} \cdot {Asw}}{{sw}}", "{qsw}",
                ("Rsw", r.Rsw, CalculationUnit.Kilopascal), ("Asw", sw.AswLine, CalculationUnit.SquareMeter),
                ("sw", r.Sw, CalculationUnit.Meter), ("qsw", sw.Qsw, CalculationUnit.KilonewtonPerMeter)));
            steps.Add(Step($"{p}.fsw", "(8.91), п. 8.1.48", "Punching_Trace_Fsw",
                @"F_{sw,ult} = 0{,}8\,q_{sw}\,u", @"0{,}8 \cdot {qsw} \cdot {u}", "{FswRaw}", counted,
                ("qsw", sw.Qsw, CalculationUnit.KilonewtonPerMeter), ("u", g.U, CalculationUnit.Meter),
                ("FswRaw", sw.FswRaw, CalculationUnit.Kilonewton), ("Fb025", 0.25 * d.Fb, CalculationUnit.Kilonewton)));
            if (d.SwCounted)
            {
                steps.Add(Step($"{p}.mswx", "(8.97), п. 8.1.52", "Punching_Trace_Mswx",
                    @"M_{swx,ult} = 0{,}8\,q_{sw}\,W_{bx}", @"0{,}8 \cdot {qsw} \cdot {Wbx}", "{Mswx}",
                    ("qsw", sw.Qsw, CalculationUnit.KilonewtonPerMeter), ("Wbx", d.Wbx, CalculationUnit.SquareMeter),
                    ("Mswx", d.Mswx, CalculationUnit.KilonewtonMeter)));
                steps.Add(Step($"{p}.mswy", "(8.97), п. 8.1.52", "Punching_Trace_Mswy",
                    @"M_{swy,ult} = 0{,}8\,q_{sw}\,W_{by}", @"0{,}8 \cdot {qsw} \cdot {Wby}", "{Mswy}",
                    ("qsw", sw.Qsw, CalculationUnit.KilonewtonPerMeter), ("Wby", d.Wby, CalculationUnit.SquareMeter),
                    ("Mswy", d.Mswy, CalculationUnit.KilonewtonMeter)));
            }
            return;
        }

        // Крест (п. 8.1.48, рис. 8.11г): лучи вдоль X дают участки Lsw,y у граней ±x, лучи вдоль Y — Lsw,x у граней ±y.
        var a = sw.Armed ?? PunchingArmedSegments.Empty;
        double sX = r.Cross?.AlongY?.Sw ?? 0, sY = r.Cross?.AlongX?.Sw ?? 0;
        bool hasX = sw.QY > 0 && a.LY > 0, hasY = sw.QX > 0 && a.LX > 0;   // индекс — по рис. 8.11г
        if (hasX)
        {
            steps.Add(Step($"{p}.lswx", "п. 8.1.48, рис. 8.11г", "Punching_Trace_LswX",
                @"L_{sw,x} = \sum L_i", "", "{L}", ("L", a.LY, CalculationUnit.Meter)));
            steps.Add(Step($"{p}.qswx", "(8.92)", "Punching_Trace_QswX",
                @"q_{sw,x} = \frac{R_{sw} A_{sw}}{s_{w,x}}", @"\frac{{Rsw} \cdot {Asw}}{{sw}}", "{qsw}",
                ("Rsw", r.Rsw, CalculationUnit.Kilopascal), ("Asw", sw.AswLine, CalculationUnit.SquareMeter),
                ("sw", sX, CalculationUnit.Meter), ("qsw", sw.QY, CalculationUnit.KilonewtonPerMeter)));
        }
        if (hasY)
        {
            steps.Add(Step($"{p}.lswy", "п. 8.1.48, рис. 8.11г", "Punching_Trace_LswY",
                @"L_{sw,y} = \sum L_i", "", "{L}", ("L", a.LX, CalculationUnit.Meter)));
            steps.Add(Step($"{p}.qswy", "(8.92)", "Punching_Trace_QswY",
                @"q_{sw,y} = \frac{R_{sw} A_{sw}}{s_{w,y}}", @"\frac{{Rsw} \cdot {Asw}}{{sw}}", "{qsw}",
                ("Rsw", r.Rsw, CalculationUnit.Kilopascal), ("Asw", sw.AswLine, CalculationUnit.SquareMeter),
                ("sw", sY, CalculationUnit.Meter), ("qsw", sw.QX, CalculationUnit.KilonewtonPerMeter)));
        }
        if (!hasX && !hasY)
        {
            steps.Add(Step($"{p}.fsw", "п. 8.1.48, рис. 8.11г", "Punching_Trace_FswCross",
                @"F_{sw,ult} = 0", "", "{FswRaw}", CalculationTraceStatus.NotApplicable, ("FswRaw", 0, CalculationUnit.Kilonewton)));
            return;
        }

        // Fsw = 0,8·(qsw,x·Lsw,x + qsw,y·Lsw,y) — сумма по направлениям, у которых есть участки.
        var fTerms = new List<string>();
        if (hasX) fTerms.Add(@"q_{sw,x} L_{sw,x}");
        if (hasY) fTerms.Add(@"q_{sw,y} L_{sw,y}");
        var fSubst = new List<string>();
        if (hasX) fSubst.Add(@"{qx} \cdot {Lx}");
        if (hasY) fSubst.Add(@"{qy} \cdot {Ly}");
        steps.Add(Step($"{p}.fsw", "(8.91), п. 8.1.48", "Punching_Trace_FswCross",
            @"F_{sw,ult} = 0{,}8\,(" + string.Join(" + ", fTerms) + ")",
            @"0{,}8 \cdot (" + string.Join(" + ", fSubst) + ")", "{FswRaw}", counted,
            ("qx", sw.QY, CalculationUnit.KilonewtonPerMeter), ("Lx", a.LY, CalculationUnit.Meter),
            ("qy", sw.QX, CalculationUnit.KilonewtonPerMeter), ("Ly", a.LX, CalculationUnit.Meter),
            ("FswRaw", sw.FswRaw, CalculationUnit.Kilonewton), ("Fb025", 0.25 * d.Fb, CalculationUnit.Kilonewton)));
        if (!d.SwCounted) return;

        // Wsw по участкам (п. 8.1.52): I относительно центра тяжести контура бетона, тот же делитель, что у Wb.
        steps.Add(Step($"{p}.iswx", "п. 8.1.52", "Punching_Trace_Iswx",
            @"I_{swx} = \int_{L_{sw}} (y - y_c)^2\,dl", "", "{I}", ("I", a.IbxX + a.IbxY, CalculationUnit.CubicMeter)));
        steps.Add(Step($"{p}.iswy", "п. 8.1.52", "Punching_Trace_Iswy",
            @"I_{swy} = \int_{L_{sw}} (x - x_c)^2\,dl", "", "{I}", ("I", a.IbyX + a.IbyY, CalculationUnit.CubicMeter)));
        double wswx = sw.YMax > 0 ? (a.IbxX + a.IbxY) / sw.YMax : 0, wswy = sw.XMax > 0 ? (a.IbyX + a.IbyY) / sw.XMax : 0;
        steps.Add(Step($"{p}.wswx", "п. 8.1.52, (8.98)", "Punching_Trace_Wswx",
            @"W_{swx} = \frac{I_{swx}}{y_{max}}", @"\frac{{I}}{{ymax}}", "{W}",
            ("I", a.IbxX + a.IbxY, CalculationUnit.CubicMeter), ("ymax", sw.YMax, CalculationUnit.Meter), ("W", wswx, CalculationUnit.SquareMeter)));
        steps.Add(Step($"{p}.wswy", "п. 8.1.52, (8.98)", "Punching_Trace_Wswy",
            @"W_{swy} = \frac{I_{swy}}{x_{max}}", @"\frac{{I}}{{xmax}}", "{W}",
            ("I", a.IbyX + a.IbyY, CalculationUnit.CubicMeter), ("xmax", sw.XMax, CalculationUnit.Meter), ("W", wswy, CalculationUnit.SquareMeter)));
        bool sameQ = !hasX || !hasY || Math.Abs(sw.QX - sw.QY) <= 1e-9 * Math.Max(sw.QX, sw.QY);
        double q = Math.Max(sw.QX, sw.QY);
        if (sameQ)
        {
            steps.Add(Step($"{p}.mswx", "(8.97), п. 8.1.52", "Punching_Trace_Mswx",
                @"M_{swx,ult} = 0{,}8\,q_{sw}\,W_{swx}", @"0{,}8 \cdot {qsw} \cdot {W}", "{Mswx}",
                ("qsw", q, CalculationUnit.KilonewtonPerMeter), ("W", wswx, CalculationUnit.SquareMeter), ("Mswx", d.Mswx, CalculationUnit.KilonewtonMeter)));
            steps.Add(Step($"{p}.mswy", "(8.97), п. 8.1.52", "Punching_Trace_Mswy",
                @"M_{swy,ult} = 0{,}8\,q_{sw}\,W_{swy}", @"0{,}8 \cdot {qsw} \cdot {W}", "{Mswy}",
                ("qsw", q, CalculationUnit.KilonewtonPerMeter), ("W", wswy, CalculationUnit.SquareMeter), ("Mswy", d.Mswy, CalculationUnit.KilonewtonMeter)));
        }
        else
        {
            steps.Add(Step($"{p}.mswx", "(8.97), п. 8.1.52", "Punching_Trace_Mswx",
                @"M_{swx,ult} = 0{,}8\,\frac{q_{sw,x} I_{swx,x} + q_{sw,y} I_{swx,y}}{y_{max}}",
                @"0{,}8 \cdot \frac{{qx} \cdot {Ix} + {qy} \cdot {Iy}}{{ymax}}", "{Mswx}",
                ("qx", sw.QY, CalculationUnit.KilonewtonPerMeter), ("Ix", a.IbxY, CalculationUnit.CubicMeter),
                ("qy", sw.QX, CalculationUnit.KilonewtonPerMeter), ("Iy", a.IbxX, CalculationUnit.CubicMeter),
                ("ymax", sw.YMax, CalculationUnit.Meter), ("Mswx", d.Mswx, CalculationUnit.KilonewtonMeter)));
            steps.Add(Step($"{p}.mswy", "(8.97), п. 8.1.52", "Punching_Trace_Mswy",
                @"M_{swy,ult} = 0{,}8\,\frac{q_{sw,x} I_{swy,x} + q_{sw,y} I_{swy,y}}{x_{max}}",
                @"0{,}8 \cdot \frac{{qx} \cdot {Ix} + {qy} \cdot {Iy}}{{xmax}}", "{Mswy}",
                ("qx", sw.QY, CalculationUnit.KilonewtonPerMeter), ("Ix", a.IbyY, CalculationUnit.CubicMeter),
                ("qy", sw.QX, CalculationUnit.KilonewtonPerMeter), ("Iy", a.IbyX, CalculationUnit.CubicMeter),
                ("xmax", sw.XMax, CalculationUnit.Meter), ("Mswy", d.Mswy, CalculationUnit.KilonewtonMeter)));
        }
    }

    /// <summary>M* по режиму учёта эксцентриситета; <paramref name="sign"/> — знак F·e в статике.</summary>
    static CalculationTraceStep MomentStep(string id, string axis, EccentricityMode mode, double m0, double f, double e,
        double sign, double star)
    {
        string a = axis, ea = axis == "x" ? "y" : "x";
        var values = new (string, double, CalculationUnit)[]
        {
            ("M0", m0, CalculationUnit.KilonewtonMeter), ("F", f, CalculationUnit.Kilonewton),
            ("e", e, CalculationUnit.Meter), ("eAbs", Math.Abs(e), CalculationUnit.Meter),
            ("M0Abs", Math.Abs(m0), CalculationUnit.KilonewtonMeter),
            ("sF", sign * f, CalculationUnit.Kilonewton), ("M", star, CalculationUnit.KilonewtonMeter)
        };
        var (formula, subst) = mode switch
        {
            EccentricityMode.Loading => ($@"M_{a}^* = |M_{a}| + F\,|e_{ea}|", @"{M0Abs} + {F} \cdot {eAbs}"),
            EccentricityMode.Unloading => ($@"M_{a}^* = \big||M_{a}| - F\,|e_{ea}|\big|", @"\big|{M0Abs} - {F} \cdot {eAbs}\big|"),
            EccentricityMode.Auto => ($@"M_{a}^* = |M_{a} \pm F\,e_{ea}|", @"|{M0} + ({sF}) \cdot ({e})|"),
            _ => ($@"M_{a}^* = |M_{a}|", @"|{M0}|")
        };
        return Step(id, "п. 8.1.46", axis == "x" ? "Punching_Trace_MxStar" : "Punching_Trace_MyStar",
            formula, subst, "{M}", values);
    }

    public static IReadOnlyList<CalculationTraceStep> BuildReduced(
        PunchingVariant v, GalyautdinovPerimeter gp, double cmin, double factor, double h0, double rbt,
        double fb, double fsw, double fult, double fred, double kF)
    {
        string p = $"sp63.punching.{v.Id}";
        return new List<CalculationTraceStep>
        {
            Step($"{p}.ured", "Галяутдинов З. Ш., 2022, (5.9), (5.33)", "Punching_Trace_ReducedPerimeter",
                @"u_{red} = 2C_{min}(\chi K_{ls} n + K_{shs}) + 4h_0",
                @"2 \cdot {cmin} \cdot ({chi} \cdot {Kls} \cdot {n} + {Kshs}) + 4 \cdot {h0}", "{ured}",
                ("cmin", cmin, CalculationUnit.Meter), ("n", gp.N, CalculationUnit.Unitless),
                ("chi", gp.Chi, CalculationUnit.Unitless), ("Kls", gp.Kls, CalculationUnit.Unitless),
                ("Kshs", gp.Kshs, CalculationUnit.Unitless), ("h0", h0, CalculationUnit.Meter),
                ("ured", gp.URed, CalculationUnit.Meter)),
            Step($"{p}.fb", "Галяутдинов З. Ш., 2022, (5.34)", "Punching_Trace_ReducedFb",
                @"F_{b,ult} = R_{bt}\,u_{red}\,h_0\,(100\mu)^{1/3}",
                @"{Rbt} \cdot {ured} \cdot {h0} \cdot {factor}", "{Fb}",
                ("Rbt", rbt, CalculationUnit.Kilopascal), ("ured", gp.URed, CalculationUnit.Meter),
                ("h0", h0, CalculationUnit.Meter), ("factor", factor, CalculationUnit.Unitless),
                ("Fb", fb, CalculationUnit.Kilonewton)),
            Step($"{p}.check", "Галяутдинов З. Ш., 2022, (5.35); справочно, моменты не рассматриваются",
                "Punching_Trace_ReducedCheck",
                @"K = \frac{F}{F_{ult}}", @"\frac{{F}}{{Fult}}", "{K}",
                kF <= 1.0 ? CalculationTraceStatus.Passed : CalculationTraceStatus.Failed,
                ("Fb", fb, CalculationUnit.Kilonewton), ("Fsw", fsw, CalculationUnit.Kilonewton),
                ("Fult", fult, CalculationUnit.Kilonewton), ("F", fred, CalculationUnit.Kilonewton),
                ("K", kF, CalculationUnit.Unitless))
        };
    }

    /// <summary>Шаги капители в начале варианта: у колонны — h0к = h0 + hc; за капителью — h0 плиты и своё Fred.</summary>
    public static IReadOnlyList<CalculationTraceStep> Capital(PunchingVariantResult v, PunchingInput input, double hc)
    {
        string p = $"sp63.punching.{v.Variant.Id}";
        var steps = new List<CalculationTraceStep>();
        if (v.Variant.Zone == PunchingZone.BeyondCapital)
        {
            steps.Add(Step($"{p}.capital", "п. 8.1.46", "Punching_Trace_CapitalBeyond", @"h_0", "", "{h0}",
                ("h0", v.H0, CalculationUnit.Meter)));
            if (input.Forces.CounterPressure != 0)
                steps.Add(Step($"{p}.fred", "п. 8.1.46", "Punching_Trace_Fred", @"F = F_0 - q\,A_b", @"{F0} - {q} \cdot {Ab}", "{F}",
                    ("F0", input.Forces.F, CalculationUnit.Kilonewton), ("q", input.Forces.CounterPressure, CalculationUnit.Kilopascal),
                    ("Ab", v.BaseArea, CalculationUnit.SquareMeter), ("F", v.Fred, CalculationUnit.Kilonewton)));
        }
        else if (v.H0 > input.H0)
            steps.Add(Step($"{p}.capital", "п. 8.1.46", "Punching_Trace_CapitalColumn", @"h_{0c} = h_0 + h_c", @"{h0} + {hc}", "{h0c}",
                ("h0", input.H0, CalculationUnit.Meter), ("hc", hc, CalculationUnit.Meter), ("h0c", v.H0, CalculationUnit.Meter)));
        return steps;
    }

    static CalculationTraceStep Step(string id, string reference, string titleKey, string formula, string subst,
        string result, params (string Key, double Value, CalculationUnit Unit)[] values)
        => Step(id, reference, titleKey, formula, subst, result, CalculationTraceStatus.Informational, values);

    static CalculationTraceStep Step(string id, string reference, string titleKey, string formula, string subst,
        string result, CalculationTraceStatus status, params (string Key, double Value, CalculationUnit Unit)[] values)
        => new()
        {
            StepId = id,
            CodeReference = reference,
            TitleKey = titleKey,
            FormulaLatex = formula,
            SubstitutionLatexTemplate = subst,
            ResultLatexTemplate = result,
            Status = status,
            Values = values.ToDictionary(x => x.Key, x => new TraceValue(x.Value, x.Unit), StringComparer.Ordinal)
        };
}
