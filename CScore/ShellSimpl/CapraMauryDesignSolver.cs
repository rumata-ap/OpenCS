namespace CScore.ShellSimpl;

/// <summary>Подобранная ортогональная сетка одной грани, м²/м.</summary>
/// <param name="ActiveThetas">Управляющие фасетки — активные ограничения ЛП, градусы.</param>
public sealed record CapraMauryMesh(double Asx, double Asy, IReadOnlyList<double> ActiveThetas);

/// <summary>Фасетка подбора: усилия, требования к рядам и обеспечиваемые площади, м²/м.</summary>
/// <param name="AsTop">Требование к верхнему ряду фасетки (r(θ) верхней сетки).</param>
/// <param name="AsBot">Требование к нижнему ряду.</param>
/// <param name="AsNTop">Обеспечиваемая верхней сеткой площадь Asx·cos²θ + Asy·sin²θ.</param>
/// <param name="Status">Исход подбора полосы фасетки.</param>
/// <param name="Eta">η полосы фасетки при требуемых площадях (ПС1; в подборе по ПС2 — 0).</param>
/// <param name="AcrcMm">a_crc полосы фасетки при требуемых площадях, мм (ПС2; в подборе по ПС1 — 0).</param>
/// <param name="AsCUls">ПС2: площадь ряда у грани, сжатой моментом, из ПС1 той же фасетки — отправная
/// для подбора по трещинам.</param>
public sealed record CapraMauryDesignFacet(double ThetaDeg, double N, double M, double AsTop, double AsBot,
    double AsNTop, double AsNBot, StripNdmDesignStatus Status, double Eta, double AcrcMm = 0.0, double AsCUls = 0.0);

/// <summary>Результат подбора сеток Капра–Мори.</summary>
/// <param name="Limit">"uls" — подбор по ПС1, "sls" — по ПС2.</param>
/// <param name="AcrcLimMm">ПС2: предельная ширина раскрытия трещин, мм.</param>
public sealed record CapraMauryDesignResult(CapraMauryMesh Top, CapraMauryMesh Bottom,
    IReadOnlyList<CapraMauryDesignFacet> Facets, IReadOnlyList<string> Diagnostics, double StepDeg,
    string Limit = "uls", double AcrcLimMm = 0.0)
{
    /// <summary>Подбор возможен на всех фасетках.</summary>
    public bool Feasible => Facets.All(f => f.Status != StripNdmDesignStatus.Impossible);
}

/// <summary>
/// Подбор арматуры пластины по Капра–Мори на НДМ полосы (спека, раздел 6): для каждой фасетки θ
/// (0…180°, шаг <c>StepDeg</c>) — N(θ), M(θ) как в проверке, требуемые площади рядов полосы
/// (<see cref="StripNdmDesign.RequiredUls(double, double, StripNdmUlsTable)"/>), затем для верхней и нижней
/// сеток — линейная задача <see cref="OrthogonalMeshLp"/>. Геометрия рядов — привязки сечения
/// (площади сечения не используются).
/// </summary>
public static class CapraMauryDesignSolver
{
    /// <summary>Геометрия и диаграммы подбора, общие для всех строк усилий одного сечения.</summary>
    public sealed class Context
    {
        internal Context(double h, double zTop, double zBot, StripNdmUlsTable table)
        {
            H = h; ZTop = zTop; ZBot = zBot; Table = table;
        }

        public double H { get; }
        public double ZTop { get; }
        public double ZBot { get; }
        internal StripNdmUlsTable Table { get; }
    }

    /// <summary>Подготовка подбора ПС1: привязки рядов сечения, диаграммы вида расчёта, таблица контура.</summary>
    public static Context CreateUls(PlateSection section, Material concreteMat, Material rebarMat, CalcType calcType)
    {
        double h = section.H;
        ShellSimplSolver.AggregateRebar(section, h, out double cb, out double ct,
            out _, out _, out _, out _, out _, out _);
        double zTop = h / 2.0 - ct, zBot = -(h / 2.0 - cb);
        var adapter = StripNdmAdapter.Create(section, concreteMat, rebarMat, calcType, false, 1.0, 0.5,
            SigmaSCrcMethod.ReleasedConcrete8137);
        return new Context(h, zTop, zBot, adapter.UlsTable(zTop, zBot));
    }

    /// <summary>
    /// Подготовка подбора ПС2: диаграммы вида N (п. 6.1.26), диаметры рядов — наибольший из x/y сечения
    /// (в запас: l_s и a_crc растут с d_s). <paramref name="withUls"/> — отправная As_c из ПС1 той же
    /// фасетки по расчётным диаграммам (вид C) при тех же усилиях; иначе As_c = 0.
    /// </summary>
    public static SlsContext CreateSls(PlateSection section, Material concreteMat, Material rebarMat,
        double acrcLimMm, double phi1 = 1.0, double phi2 = 0.5,
        SigmaSCrcMethod sigmaSCrc = SigmaSCrcMethod.ReleasedConcrete8137, bool withUls = true)
    {
        double h = section.H;
        ShellSimplSolver.AggregateRebar(section, h, out double cb, out double ct,
            out _, out _, out _, out _, out double dsX, out double dsY);
        double zTop = h / 2.0 - ct, zBot = -(h / 2.0 - cb);
        var sls = StripNdmAdapter.Create(section, concreteMat, rebarMat, CalcType.N, true, phi1, phi2, sigmaSCrc);
        var uls = withUls
            ? StripNdmAdapter.Create(section, concreteMat, rebarMat, CalcType.C, false, 1.0, 0.5, sigmaSCrc).UlsTable(zTop, zBot)
            : null;
        return new SlsContext(h, zTop, zBot, Math.Max(dsX, dsY), acrcLimMm, sls, uls);
    }

    /// <summary>Геометрия, диаграммы и предел трещин подбора ПС2, общие для всех строк одного сечения.</summary>
    public sealed class SlsContext
    {
        internal SlsContext(double h, double zTop, double zBot, double ds, double acrcLimMm,
            StripNdmAdapter adapter, StripNdmUlsTable? ulsTable)
        {
            H = h; ZTop = zTop; ZBot = zBot; Ds = ds; AcrcLimMm = acrcLimMm; Adapter = adapter; UlsTable = ulsTable;
        }

        public double H { get; }
        public double ZTop { get; }
        public double ZBot { get; }
        /// <summary>Диаметр стержней обоих рядов в расчёте a_crc, м.</summary>
        public double Ds { get; }
        public double AcrcLimMm { get; }
        /// <summary>Отправная As_c берётся из ПС1 той же фасетки.</summary>
        public bool WithUls => UlsTable != null;
        internal StripNdmAdapter Adapter { get; }
        internal StripNdmUlsTable? UlsTable { get; }
    }

    static double Top(StripNdmDesignResult r) => r.TensionTop ? r.AsTension : r.AsCompression;
    static double Bot(StripNdmDesignResult r) => r.TensionTop ? r.AsCompression : r.AsTension;

    /// <summary>Фасетки 0…180° с шагом <paramref name="stepDeg"/>: (θ, N(θ), M(θ)) как в проверке.</summary>
    static IEnumerable<(double Theta, double N, double M)> Facets(double nx, double ny, double nxy,
        double mx, double my, double mxy, double stepDeg)
    {
        int count = Math.Max(1, (int)Math.Round(180.0 / stepDeg));
        for (int i = 0; i < count; i++)
        {
            double theta = i * stepDeg, a = theta * Math.PI / 180.0;
            double c = Math.Cos(a), s = Math.Sin(a), c2 = c * c, s2 = s * s, cs = c * s;
            yield return (theta, nx * c2 + ny * s2 + 2.0 * nxy * cs, mx * c2 + my * s2 + 2.0 * mxy * cs);
        }
    }

    /// <summary>ЛП верхней и нижней сеток по требованиям фасеток и обеспечиваемые площади.</summary>
    static CapraMauryDesignResult Meshes(IReadOnlyList<(double Theta, double N, double M, StripNdmDesignResult R, double AsCUls)> raw,
        List<string> diagnostics, double stepDeg, bool sls, double acrcLimMm)
    {
        var top = OrthogonalMeshLp.Solve(raw.Select(f => (f.Theta, Top(f.R))).ToArray());
        var bot = OrthogonalMeshLp.Solve(raw.Select(f => (f.Theta, Bot(f.R))).ToArray());

        var facets = raw.Select(f =>
        {
            double a = f.Theta * Math.PI / 180.0, c2 = Math.Cos(a) * Math.Cos(a), s2 = Math.Sin(a) * Math.Sin(a);
            return new CapraMauryDesignFacet(f.Theta, f.N, f.M, Top(f.R), Bot(f.R),
                top.Asx * c2 + top.Asy * s2, bot.Asx * c2 + bot.Asy * s2, f.R.Status,
                sls ? 0.0 : f.R.Value, sls ? f.R.Value : 0.0, f.AsCUls);
        }).ToList();

        return new CapraMauryDesignResult(
            new CapraMauryMesh(top.Asx, top.Asy, top.ActiveThetas),
            new CapraMauryMesh(bot.Asx, bot.Asy, bot.ActiveThetas),
            facets, diagnostics, stepDeg, sls ? "sls" : "uls", sls ? acrcLimMm : 0.0);
    }

    /// <summary>Подбор по ПС1 для усилий (Nx, Ny, Nxy — кН/м; Mx, My, Mxy — кН·м/м, конвенция пластины).</summary>
    public static CapraMauryDesignResult DesignUls(Context ctx, double nx, double ny, double nxy,
        double mx, double my, double mxy, double stepDeg = 10.0)
    {
        var raw = new List<(double, double, double, StripNdmDesignResult, double)>();
        var diagnostics = new List<string>();
        foreach (var (theta, n, m) in Facets(nx, ny, nxy, mx, my, mxy, stepDeg))
        {
            var r = StripNdmDesign.RequiredUls(n, m, ctx.Table);
            raw.Add((theta, n, m, r, 0.0));
            if (r.Status == StripNdmDesignStatus.Impossible)
                diagnostics.Add($"θ = {theta:0.##}°: {r.Reason} (N = {n:F1} кН/м, M = {m:F2} кН·м/м)");
        }
        return Meshes(raw, diagnostics, stepDeg, false, 0.0);
    }

    /// <summary>
    /// Подбор по ПС2 (спека, раздел 6, п. 3): для каждой фасетки — As ряда у растянутой моментом грани
    /// по <see cref="StripNdmDesign.RequiredSls"/> до a_crc ≤ a_crc,ult; ряд у второй грани — As_c из ПС1
    /// той же фасетки (если <see cref="SlsContext.WithUls"/>), при сквозном растяжении наращивается
    /// пропорционально доле усилия. Требования обоих рядов идут в ЛП сеток.
    /// </summary>
    public static CapraMauryDesignResult DesignSls(SlsContext ctx, double nx, double ny, double nxy,
        double mx, double my, double mxy, double stepDeg = 10.0)
    {
        var raw = new List<(double, double, double, StripNdmDesignResult, double)>();
        var diagnostics = new List<string>();
        foreach (var (theta, n, m) in Facets(nx, ny, nxy, mx, my, mxy, stepDeg))
        {
            double asC = 0.0;
            if (ctx.UlsTable != null)
            {
                var u = StripNdmDesign.RequiredUls(n, m, ctx.UlsTable);
                if (u.Status != StripNdmDesignStatus.Impossible) asC = u.AsCompression;
            }
            var r = ctx.Adapter.RequiredSls(n, m, ctx.ZTop, ctx.ZBot, asC, ctx.AcrcLimMm, ctx.Ds);
            raw.Add((theta, n, m, r, asC));
            if (r.Status == StripNdmDesignStatus.Impossible)
                diagnostics.Add($"θ = {theta:0.##}°: {r.Reason} (N = {n:F1} кН/м, M = {m:F2} кН·м/м)");
        }
        return Meshes(raw, diagnostics, stepDeg, true, ctx.AcrcLimMm);
    }
}