using System.Globalization;
using CScore;
using CSmath;
using CSfea.CScoreBridge.Structural;
using Xunit.Abstractions;

namespace CSfea.Tests;

/// <summary>
/// Балка C3 Vecchio, Shim (2004) — повтор опыта Bresler, Scordelis (1963): пролёт 6400 мм, сечение 152 × 552 мм, сила в
/// середине пролёта, изгибное разрушение (дробление сжатой зоны). Данные — по DIANA Verification Report, гл. 5 «Beams
/// Failing in Bending» (табл. 5.1, рис. 5.1): бетон E = 34 300 МПа, fcm = 43,5 МПа, ftm = 3,13 МПа; низ — 2M30
/// (fy = 436, fu = 700 МПа, εsu = 0,05) в 64 мм от низа и 2M25 (fy = 445, fu = 680 МПа) в 128 мм, верх — 3M10 (fy = 315,
/// fu = 460 МПа, εsu = 0,025) в 50 мм от верха; хомуты D4 (2 × 25,7 мм², fy = 600 МПа) с шагом 168 мм — для сдвиговой
/// податливости стержня (КЭ Тимошенко, <see cref="RcSecantOptions.BeamShear"/>). Собственный вес не учитывается (как у
/// DIANA). Опыт: пик 265 кН при прогибе 44,3 мм.
/// </summary>
[Trait("Category", "Verification")]
public class VecchioShimBeamTests(ITestOutputHelper output)
{
    const double Span = 6.4, B = 0.152, H = 0.552;
    const int N = 32;                       // КЭ на пролёт (200 мм)
    const double PMax = 300e3;              // Н

    /// <summary>Опыт (DIANA, рис. 5.9, «Experimental»; оцифровка 07.10.2026): прогиб середины, мм → сила, кН.</summary>
    static readonly (double W, double P)[] Experiment =
    [
        (2, 32), (4, 39), (6, 58), (10, 82), (12, 100), (14, 115), (16, 129), (18, 139), (20, 159), (26, 197), (30, 221),
        (32, 230), (34, 234), (36, 242), (38, 256), (40, 251), (42, 259), (44, 264), (45, 266),
    ];

    /// <summary>DIANA, total strain rotate crack model (рис. 5.9; оцифровка 07.10.2026): прогиб, мм → сила, кН.</summary>
    static readonly (double W, double P)[] Diana =
    [
        (2, 33), (4, 52), (6, 70), (10, 93), (12, 107), (14, 122), (16, 136), (18, 148), (20, 160), (22, 173), (24, 184),
        (26, 198), (28, 204), (30, 213), (32, 220), (34, 230), (36, 237), (38, 244), (40, 249), (42, 254), (44, 258), (48, 257),
    ];

    const double Ec = 34_300_000, Fcm = 43_500, Ftm = 3_130;   // кПа

    static MaterialChars ConcreteChars(CalcType ct) => new()
    {
        Type = MatType.Concrete, TypeCalc = ct, Fc = -Fcm, Ft = Ftm, E = Ec,
        Ec0 = -0.002, Ec1 = -0.6 * Fcm / Ec, Ec1Red = -0.0015, Ec2 = -0.0035,
        Et0 = 0.0001, Et1 = 0.6 * Ftm / Ec, Et1Red = 0.00008, Et2 = 0.00015,
    };

    static Material Concrete() => new()
    {
        Id = 1, Tag = "fcm 43,5 (C3)", Type = MatType.Concrete, E = Ec,
        MaterialChars = [ConcreteChars(CalcType.C), ConcreteChars(CalcType.CL), ConcreteChars(CalcType.N), ConcreteChars(CalcType.NL)],
    };

    /// <summary>Арматура: материал для типа области; диаграмма заменяется своей (см. <see cref="Hardening"/>).</summary>
    internal static Material Rebar(int id, double fy) => new()
    {
        Id = id, Tag = $"fy {fy / 1e3:0}", Type = MatType.ReSteelF, E = 200_000_000,
        MaterialChars = new[] { CalcType.C, CalcType.CL, CalcType.N, CalcType.NL }.Select(ct => new MaterialChars
        {
            Type = MatType.ReSteelF, TypeCalc = ct, Fc = -fy, Ft = fy, E = 200_000_000, Ec2 = -0.0035, Et2 = 0.025,
        }).ToList(),
    };

    /// <summary>
    /// Двухлинейная диаграмма с упрочнением, симметричная: упругость до fy, далее прямая до fu при εsu (DIANA: «hardening
    /// plasticity»); <paramref name="hardening"/> = false — площадка fy до εsu.
    /// </summary>
    internal static Diagramm Hardening(double fy, double fu, double esu, bool hardening)
    {
        double ey = fy / 200_000_000, top = hardening ? fu : fy;
        return new Diagramm(new LSpline([-esu, -ey, 0.0], [-top, -fy, 0.0]), new LSpline([0.0, ey, esu], [0.0, fy, top]),
            DiagrammType.Custom, MatType.ReSteelF, "упрочнение");
    }

    /// <summary>Сечение CScore: X — ширина, Y — высота, начало в центре; бетон — 4 × 60 фибр.</summary>
    static CrossSection Section(bool hardening)
    {
        var concreteMat = Concrete();
        var concrete = new MaterialArea
        {
            Id = 1, Tag = "бетон", Category = AreaCategory.Region,
            Material = concreteMat, MaterialId = concreteMat.Id, DiagrammType = DiagrammType.L3,
            Hull = new Contour([-B / 2, B / 2, B / 2, -B / 2, -B / 2], [-H / 2, -H / 2, H / 2, H / 2, -H / 2], "hull"),
        };
        concrete.SetWKT();
        concrete.SliceXY(nx: 4, ny: 60);

        var bars = new (int Id, double D, double Fy, double Fu, double Esu, int Count, double Y)[]
        {
            (2, 0.0299, 436_000, 700_000, 0.05, 2, -H / 2 + 0.064),     // 2M30
            (3, 0.0252, 445_000, 680_000, 0.05, 2, -H / 2 + 0.128),     // 2M25
            (4, 0.0113, 315_000, 460_000, 0.025, 3, H / 2 - 0.050),     // 3M10
        };
        var areas = new List<MaterialArea> { concrete };
        foreach (var b in bars)
        {
            var mat = Rebar(b.Id, b.Fy);
            var r = new MaterialArea
            {
                Id = b.Id, Tag = $"арматура {b.Id}", Category = AreaCategory.RebarGroup,
                Material = mat, MaterialId = mat.Id, DiagrammType = DiagrammType.L2,
                HostArea = concrete, HostAreaId = concrete.Id,
            };
            for (int i = 0; i < b.Count; i++)
                r.Fibers.Add(Fiber.CreatePoint(b.D, b.Count == 1 ? 0.0 : -0.045 + i * 0.09 / (b.Count - 1), b.Y));
            areas.Add(r);
        }
        // Хомуты D4: замкнутый контур по осям ветвей, две вертикальные ветви в плоскости сдвига.
        var stirrupMat = Rebar(5, 600_000);
        double sx = B / 2 - 0.025, sy = H / 2 - 0.025;
        areas.Add(new MaterialArea
        {
            Id = 5, Tag = "хомуты D4", Category = AreaCategory.Stirrups, Material = stirrupMat, MaterialId = stirrupMat.Id,
            Stirrups =
            [
                new StirrupGroup
                {
                    MaterialId = stirrupMat.Id, SpacingM = 0.168,
                    Elements = [new StirrupElement
                    {
                        CenterlineContour = new Contour([-sx, sx, sx, -sx, -sx], [-sy, -sy, sy, sy, -sy], "хомут"),
                        BarAreaM2 = 25.7e-6, BarDiameterM = 0.0057,
                    }],
                },
            ],
        });
        var section = new CrossSection { Id = 1, Tag = "C3", Areas = areas };
        section.ResolveAndBuildDiagramms(rebarDifferentialDiagram: false);
        foreach (var (a, b) in areas.Skip(1).Zip(bars))
            a.Diagramms = new[] { CalcType.C, CalcType.CL, CalcType.N, CalcType.NL }
                .ToDictionary(ct => ct, _ => Hardening(b.Fy, b.Fu, b.Esu, hardening));
        return section;
    }

    /// <summary>Свободно опёртая балка: N КЭ, сила в середине до <see cref="PMax"/> за <paramref name="steps"/> шагов.</summary>
    static RcStructuralModel Model(int steps, bool hardening)
    {
        var rs = new RcBeamSection("C3") { Cross = Section(hardening), Calc = CalcType.N, TorsionGJ = 1e7 };
        var m = new RcStructuralModel();
        for (int i = 0; i <= N; i++) m.Nodes.Add(new RcNode(i, Span * i / N, 0, 0));
        // Локальная ось y — глобальная Y, ось z КЭ = высота сечения (Y сечения CScore).
        for (int i = 0; i < N; i++) m.Beams.Add(new RcBeam(i, i, i + 1, rs, [0.0, 1.0, 0.0]));
        m.Supports.Add(new RcSupport(0, 0b001111));
        m.Supports.Add(new RcSupport(N, 0b000110));
        var lc = new RcLoadCase(1, "P");
        lc.Nodal.Add(new RcNodalLoad(N / 2, [0, 0, -PMax, 0, 0, 0]));
        m.LoadCases.Add(lc);
        m.Stages.Add(new RcStage("P", [(1, 1.0)], steps));
        return m;
    }

    List<(double P, double W, int Cracked, int Yielded, int Failed)> Run(string name, string tag, RcSecantOptions options,
        bool hardening = true, int steps = 60)
    {
        var m = Model(steps, hardening);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var run = RcSecantAnalysis.Run(m, options);
        var r = run.Result;
        var curve = r.Steps.Where(s => s.Converged).Select(s => (P: s.LoadFactor * PMax / 1e3,
            W: -s.U[run.Build.Dof(N / 2, 2)] * 1e3, Cracked: s.Beams.Count(x => x.Cracked), Yielded: s.Beams.Count(x => x.Yielded),
            Failed: s.Beams.Count(x => x.Failed))).ToList();
        output.WriteLine($"=== {name}: {(r.Completed ? "до конца" : r.Message)}; время {sw.Elapsed:mm\\:ss}");
        output.WriteLine("P, кН; w, мм; опыт при w, кН; DIANA при w, кН; трещины; текучесть; отказ");
        foreach (var c in curve)
            output.WriteLine(string.Join("; ", F(c.P), F(c.W), F(At(Experiment, c.W)), F(At(Diana, c.W)), c.Cracked, c.Yielded, c.Failed));
        if (Environment.GetEnvironmentVariable("OPENCS_CSFEA_OUT") is { Length: > 0 } dir)
        {
            Directory.CreateDirectory(dir);
            static string R(double v) => v.ToString("R", CultureInfo.InvariantCulture);
            File.WriteAllText(Path.Combine(dir, $"vs-c3-{tag}-curve.csv"), "P_kN,w_mm,cracked,yielded,failed\n" +
                string.Concat(curve.Select(c => $"{R(c.P)},{R(c.W)},{c.Cracked},{c.Yielded},{c.Failed}\n")));
        }
        return curve;
    }

    /// <summary>Сила кривой при прогибе w (линейная интерполяция, NaN вне кривой).</summary>
    internal static double At((double W, double P)[] data, double w)
    {
        var pts = new List<(double W, double P)> { (0, 0) };
        pts.AddRange(data);
        for (int i = 1; i < pts.Count; i++)
            if (w <= pts[i].W) return pts[i - 1].P + (pts[i].P - pts[i - 1].P) * (w - pts[i - 1].W) / (pts[i].W - pts[i - 1].W);
        return double.NaN;
    }

    internal static string F(double v) => double.IsNaN(v) ? "—" : v.ToString("0.00", CultureInfo.InvariantCulture);

    [Fact]
    public void Psi() => Run("ψs, упрочнение, сдвиг", "psi", new RcSecantOptions { Psi = true });

    [Fact]
    public void NoPsi() => Run("без ψs, упрочнение, сдвиг", "nopsi", new RcSecantOptions { Psi = false });

    [Fact]
    public void PsiPlateau() => Run("ψs, площадка текучести, сдвиг", "psi-plateau", new RcSecantOptions { Psi = true }, hardening: false);

    [Fact]
    public void PsiBernoulli() => Run("ψs, упрочнение, Бернулли", "psi-bernoulli", new RcSecantOptions { Psi = true, BeamShear = false });

    [Fact]
    public void NoPsiBernoulli() => Run("без ψs, упрочнение, Бернулли", "nopsi-bernoulli",
        new RcSecantOptions { Psi = false, BeamShear = false });

    /// <summary>Сдвиговые характеристики сечения C3 — сверка с ручным расчётом мини-спеки (07.10.2026).</summary>
    [Fact]
    public void ShearSection()
    {
        var s = BeamShearSection.From(Section(true), CalcType.N)!;
        double ga0 = 0.4 * Ec * 5.0 / 6.0 * B * H * 1e3;
        Assert.Equal(ga0, s.Initial.GAvZ, ga0 * 1e-9);
        var law = s.CrackedZ(tensionPositive: false)!;   // растянут низ
        double as1 = 2 * Math.PI * 0.0299 * 0.0299 / 4, as2 = 2 * Math.PI * 0.0252 * 0.0252 / 4;
        double h0 = H - (as1 * 0.064 + as2 * 0.128) / (as1 + as2);
        double rho = 2 * 25.7e-6 / (B * 0.168), n = 200_000_000 / Ec;
        double kv = rho / (1 + 4 * n * rho) * 200e9 * B * h0;
        output.WriteLine($"GA0 = {s.Initial.GAvZ / 1e6:0} МН, Kv = {law.Kv / 1e6:0.0} МН, h0 = {h0:0.000} м");
        Assert.Equal(kv, law.Kv, kv * 1e-9);
    }
}
