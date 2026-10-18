namespace CScore.ShellSimpl;

/// <summary>
/// Полоса упрощённых проверок пластин (<see cref="ShellSimplSolver"/>) на одноосной НДМ:
/// диаграммы и пределы задачи, расчёт полосы и заполнение <see cref="ShellSimplStripResult"/>.
/// Полоса — оба ряда направления (верх z &gt; 0 — индекс 0, низ — 1), момент в конвенции
/// <see cref="PlateSection"/> («+» растягивает верх).
/// </summary>
internal sealed class StripNdmAdapter
{
    readonly Diagramm _concrete;
    readonly Diagramm _rebar;
    readonly StripNdmLimits _lim;
    readonly double _es;
    readonly double _h;
    readonly double _phi1, _phi2;
    readonly SigmaSCrcMethod _sigmaSCrc;

    StripNdmAdapter(Diagramm concrete, Diagramm rebar, StripNdmLimits lim, double es, double h,
        double phi1, double phi2, SigmaSCrcMethod sigmaSCrc)
    {
        _concrete = concrete; _rebar = rebar; _lim = lim; _es = es; _h = h;
        _phi1 = phi1; _phi2 = phi2; _sigmaSCrc = sigmaSCrc;
    }

    /// <summary>
    /// Диаграммы — как в слоистой проверке: бетон по <see cref="PlateSection.ConcreteDiagramType"/>
    /// (резерв L3), арматура — L2, приведённая к допустимому типу; ПС1 — вид расчёта задачи,
    /// ПС2 — диаграммы вида N (п. 6.1.26).
    /// </summary>
    public static StripNdmAdapter Create(PlateSection section, Material concreteMat, Material rebarMat,
        CalcType calcType, bool isSls, double phi1, double phi2, SigmaSCrcMethod sigmaSCrc)
    {
        var diagCalc = isSls ? CalcType.N : calcType;
        var c = concreteMat.GetDiagramms(section.ConcreteDiagramType)?[diagCalc]
            ?? concreteMat.GetDiagramms(DiagrammType.L3)?[diagCalc]
            ?? throw new InvalidOperationException("Диаграмма бетона не построена");
        var r = rebarMat.GetDiagramms(DiagrammCompatibility.Coerce(rebarMat.Type, DiagrammType.L2))?[diagCalc]
            ?? throw new InvalidOperationException("Диаграмма арматуры не построена");
        var lim = StripNdmLimits.From(concreteMat.GetChars(calcType), rebarMat.Type);
        return new StripNdmAdapter(c, r, lim, rebarMat.chars[calcType].E, section.H, phi1, phi2, sigmaSCrc);
    }

    /// <summary>Таблица предельного контура для подбора (ряды [верх, низ]).</summary>
    public StripNdmUlsTable UlsTable(double zTop, double zBot) =>
        StripNdmDesign.UlsTable(_h, zTop, zBot, _concrete, _rebar, _lim);

    /// <summary>Подбор ПС2 полосы (<see cref="StripNdmDesign.RequiredSls"/>) на диаграммах и параметрах трещин задачи.</summary>
    public StripNdmDesignResult RequiredSls(double n, double m, double zTop, double zBot, double asCompression,
        double acrcLimMm, double ds) =>
        StripNdmDesign.RequiredSls(n, m, _h, zTop, zBot, asCompression, acrcLimMm, _concrete, _rebar,
            ds, ds, _es, _phi1, _phi2, _sigmaSCrc);

    StripNdmSection Section(double zTop, double asTop, double zBot, double asBot) =>
        new(_h, [new StripNdmRow(zTop, asTop), new StripNdmRow(zBot, asBot)], _concrete, _rebar);

    static string PivotText(StripNdmPivot p) => p switch
    {
        StripNdmPivot.A => "A — растянутый ряд на εs,ult",
        StripNdmPivot.B => "B — сжатая грань на εb2",
        StripNdmPivot.C => "C — однозначное сжатие, εb,ult по п. 8.1.30",
        _ => "—",
    };

    /// <summary>ПС1 полосы. <paramref name="mPlate"/> — момент в конвенции пластины.</summary>
    /// <param name="mDes">Момент для отчёта (как в формульной полосе).</param>
    /// <param name="tensionTop">Ряд, растянутый моментом, — верхний (при нулевом моменте — ряд грани
    /// полосы, как в формульной полосе).</param>
    public ShellSimplStripResult Uls(string name, double mPlate, double nDes, double mDes, bool tensionTop,
        double zTop, double asTop, double zBot, double asBot)
    {
        double asT = tensionTop ? asTop : asBot, asC = tensionTop ? asBot : asTop;
        double zT = tensionTop ? zTop : zBot, zC = tensionTop ? zBot : zTop;
        var r = new ShellSimplStripResult
        {
            Name = name, Model = "ndm", M_des = mDes, N_des = nDes,
            H0 = _h / 2 + Math.Abs(zT), A_prime = _h / 2 - Math.Abs(zC), As_t = asT, As_c = asC,
            NoRebar = asT < 1e-12,
        };
        if (r.NoRebar) return r;

        var sec = Section(zTop, asTop, zBot, asBot);
        var u = StripNdm.UltimateFactor(sec, nDes, mPlate, _lim);
        r.Eta = u.Eta;
        r.Lambda = double.IsFinite(u.Lambda) ? u.Lambda : 0.0;   // 0 — нагрузки нет (JSON не принимает ∞)
        r.Pivot = u.Pivot == StripNdmPivot.None ? "" : u.Pivot.ToString();
        r.Demand = Math.Abs(mPlate);
        r.M_ult = double.IsFinite(u.Lambda) ? u.Lambda * Math.Abs(mPlate) : 0.0;

        // Для отчёта — фактическое НДС, если прочность обеспечена, иначе предельная плоскость.
        StripNdmState? st = null;
        if (u.Eta <= 1.0)
        {
            var actual = StripNdm.Solve(sec, nDes, mPlate, false);
            if (actual.Converged) st = actual;
        }
        st ??= u.Ultimate;
        string where = u.Eta <= 1.0 && st != u.Ultimate ? "НДС при заданных усилиях" : "предельная плоскость";
        if (st != null) Fill(r, st, zT, zC);
        r.Case = u.Pivot == StripNdmPivot.None
            ? $"НДМ полосы: {(string.IsNullOrEmpty(u.Reason) ? "предел не найден" : u.Reason)}"
            : $"НДМ полосы (п. 8.1.30), шарнир {PivotText(u.Pivot)}, λ = {u.Lambda:F3}; εb, εs — {where}";
        return r;
    }

    void Fill(ShellSimplStripResult r, StripNdmState st, double zT, double zC)
    {
        r.EpsB = Math.Min(st.EpsAt(_h / 2), st.EpsAt(-_h / 2));
        r.EpsS_t = st.EpsAt(zT);
        r.EpsS_c = st.EpsAt(zC);
        r.Xm = st.CompressionHeight(_h);
        r.Xi = r.H0 > 0 ? r.Xm / r.H0 : 0;
        r.Sigma_s_MPa = _rebar.Sig(r.EpsS_t, out _, true, true) / 1000.0;
    }

    /// <summary>ПС2 полосы: трещины по НДМ, оба ряда.</summary>
    public StripNdmSls Sls(double mPlate, double nDes, double zTop, double asTop, double dsTop,
        double zBot, double asBot, double dsBot) =>
        StripNdm.CrackWidth(Section(zTop, asTop, zBot, asBot), nDes, mPlate, [dsTop, dsBot], _es, _phi1, _phi2, _sigmaSCrc);

    /// <summary>a_crc, мм, когда сечение с трещиной не уравновешивается (JSON не принимает ∞).</summary>
    public const double UnboundedAcrcMm = 1e9;

    /// <summary>Запись полосы ПС2 по ряду <paramref name="row"/> (0 — верх, 1 — низ).</summary>
    public ShellSimplStripResult FromSls(string name, StripNdmSls s, int row, double mDes, double nDes, double ds)
    {
        var own = s.Rows[row];
        var other = s.Rows[1 - row];
        var r = new ShellSimplStripResult
        {
            Name = name, Model = "ndm", M_des = mDes, N_des = nDes,
            H0 = _h / 2 + Math.Abs(own.Z), A_prime = _h / 2 - Math.Abs(other.Z),
            As_t = own.As, As_c = other.As, Ds = ds,
            NoRebar = own.As < 1e-12,
            Mcrc = s.Mcrc,
            Cracked = s.Cracked && own.SigmaS > 0.0,
            Sigma_s_MPa = own.SigmaS / 1000.0,
            Sigma_s_crc_MPa = own.SigmaSCrc / 1000.0,
            Psi_s = own.SigmaS > 0.0 ? own.PsiS : 1.0,
            Ls_m = own.LsM,
            Acrc_mm = own.AcrcMm,
        };
        if (s.State != null)
        {
            Fill(r, s.State, own.Z, other.Z);
            r.Sigma_s_MPa = own.SigmaS / 1000.0;
            double k = Math.Abs(s.State.Kappa);
            r.B_kNm2 = k > 1e-12 ? Math.Abs(mDes) / k : 0.0;
        }
        // Трещина есть, а равновесия сечения с трещиной нет (момент сверх несущей способности полосы):
        // ширина не ограничена — 1e9 (вид показывает ∞), а не 0, иначе проверка проходит ложно.
        if (s.Cracked && !s.Converged && !r.NoRebar)
            r.Acrc_mm = UnboundedAcrcMm;
        r.Case = "НДМ полосы (пп. 8.2.14–8.2.18)" + (string.IsNullOrEmpty(s.Reason) ? "" : "; " + s.Reason);
        return r;
    }

    /// <summary>Индекс ряда с наибольшей a_crc; при равенстве — ряд, растянутый моментом.</summary>
    public static int WorstRow(StripNdmSls s, double mPlate)
    {
        int byMoment = mPlate >= 0 ? 0 : 1;
        return s.Rows[1 - byMoment].AcrcMm > s.Rows[byMoment].AcrcMm ? 1 - byMoment : byMoment;
    }
}
