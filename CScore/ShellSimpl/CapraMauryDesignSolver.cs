namespace CScore.ShellSimpl;

/// <summary>Подобранная ортогональная сетка одной грани, м²/м.</summary>
/// <param name="ActiveThetas">Управляющие фасетки — активные ограничения ЛП, градусы.</param>
public sealed record CapraMauryMesh(double Asx, double Asy, IReadOnlyList<double> ActiveThetas);

/// <summary>Фасетка подбора: усилия, требования к рядам и обеспечиваемые площади, м²/м.</summary>
/// <param name="AsTop">Требование к верхнему ряду фасетки (r(θ) верхней сетки).</param>
/// <param name="AsBot">Требование к нижнему ряду.</param>
/// <param name="AsNTop">Обеспечиваемая верхней сеткой площадь Asx·cos²θ + Asy·sin²θ.</param>
/// <param name="Status">Исход подбора полосы фасетки.</param>
/// <param name="Eta">η полосы фасетки при требуемых площадях.</param>
public sealed record CapraMauryDesignFacet(double ThetaDeg, double N, double M, double AsTop, double AsBot,
    double AsNTop, double AsNBot, StripNdmDesignStatus Status, double Eta);

/// <summary>Результат подбора сеток Капра–Мори.</summary>
public sealed record CapraMauryDesignResult(CapraMauryMesh Top, CapraMauryMesh Bottom,
    IReadOnlyList<CapraMauryDesignFacet> Facets, IReadOnlyList<string> Diagnostics, double StepDeg)
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

    /// <summary>Подбор по ПС1 для усилий (Nx, Ny, Nxy — кН/м; Mx, My, Mxy — кН·м/м, конвенция пластины).</summary>
    public static CapraMauryDesignResult DesignUls(Context ctx, double nx, double ny, double nxy,
        double mx, double my, double mxy, double stepDeg = 10.0)
    {
        int count = Math.Max(1, (int)Math.Round(180.0 / stepDeg));
        var raw = new (double Theta, double N, double M, StripNdmDesignResult R)[count];
        var diagnostics = new List<string>();
        for (int i = 0; i < count; i++)
        {
            double theta = i * stepDeg, a = theta * Math.PI / 180.0;
            double c = Math.Cos(a), s = Math.Sin(a), c2 = c * c, s2 = s * s, cs = c * s;
            double n = nx * c2 + ny * s2 + 2.0 * nxy * cs;
            double m = mx * c2 + my * s2 + 2.0 * mxy * cs;
            var r = StripNdmDesign.RequiredUls(n, m, ctx.Table);
            raw[i] = (theta, n, m, r);
            if (r.Status == StripNdmDesignStatus.Impossible)
                diagnostics.Add($"θ = {theta:0.##}°: {r.Reason} (N = {n:F1} кН/м, M = {m:F2} кН·м/м)");
        }

        static double Top(StripNdmDesignResult r) => r.TensionTop ? r.AsTension : r.AsCompression;
        static double Bot(StripNdmDesignResult r) => r.TensionTop ? r.AsCompression : r.AsTension;

        var top = OrthogonalMeshLp.Solve(raw.Select(f => (f.Theta, Top(f.R))).ToArray());
        var bot = OrthogonalMeshLp.Solve(raw.Select(f => (f.Theta, Bot(f.R))).ToArray());

        var facets = raw.Select(f =>
        {
            double a = f.Theta * Math.PI / 180.0, c2 = Math.Cos(a) * Math.Cos(a), s2 = Math.Sin(a) * Math.Sin(a);
            return new CapraMauryDesignFacet(f.Theta, f.N, f.M, Top(f.R), Bot(f.R),
                top.Asx * c2 + top.Asy * s2, bot.Asx * c2 + bot.Asy * s2, f.R.Status, f.R.Value);
        }).ToList();

        return new CapraMauryDesignResult(
            new CapraMauryMesh(top.Asx, top.Asy, top.ActiveThetas),
            new CapraMauryMesh(bot.Asx, bot.Asy, bot.ActiveThetas),
            facets, diagnostics, stepDeg);
    }
}
