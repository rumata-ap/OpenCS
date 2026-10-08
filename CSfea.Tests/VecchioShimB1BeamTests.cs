using System.Globalization;
using CScore;
using CSfea.CScoreBridge.Structural;
using Xunit.Abstractions;

namespace CSfea.Tests;

/// <summary>
/// Балка B1 Vecchio, Shim (2004) — повтор опыта Bresler, Scordelis (1963): пролёт 3660 мм, сечение 229 × 552 мм, сила в
/// середине пролёта, сдвигово-изгибное разрушение (наклонные трещины с ~60 % предела, дробление сжатой зоны). Проверка
/// закона сдвига стержня (КЭ Тимошенко, <see cref="RcSecantOptions.BeamShear"/>) на второй балке — чтобы он не оказался
/// подогнан под C3 (<see cref="VecchioShimBeamTests"/>): здесь короче пролёт (a/d = 4,0 против 7,0) и меньше хомутов
/// (ρw = 0,15 % против 0,20 %). Данные — по DIANA Verification Report 10.6, гл. 6.2 (табл. 6.2, рис. 6.15): бетон
/// E = 36 500 МПа, fcm = 22,6 МПа, ftm = 2,37 МПа; низ — 2M30 (fy = 436, fu = 700 МПа) в 64 мм от низа и 2M25 (fy = 445,
/// fu = 680 МПа) в 128 мм, εsu = 0,2; верх — 3M10 (fy = 315, fu = 460 МПа, εsu = 0,025, как у C3) в 50 мм от верха; хомуты
/// D5 (2 × 32,2 мм², fy = 600 МПа) с шагом 190 мм. Модуль всей арматуры — 200 000 МПа (у M25 в табл. 6.2 — 210 000).
/// Собственный вес не учитывается. Опыт: пик 434 кН при прогибе 22,0 мм.
/// </summary>
[Trait("Category", "Verification")]
public class VecchioShimB1BeamTests(ITestOutputHelper output)
{
    const double Span = 3.66, B = 0.229, H = 0.552;
    const int N = 24;                       // КЭ на пролёт (152,5 мм)
    const double PMax = 500e3;              // Н

    /// <summary>Опыт (DIANA 10.6, рис. 6.22, «Experimental»; оцифровка 07.10.2026): прогиб середины, мм → сила, кН.</summary>
    static readonly (double W, double P)[] Experiment =
    [
        (1, 86), (2, 118), (3, 152), (4, 170), (5, 207), (6, 235), (7, 252), (8, 273), (9, 290), (10, 315), (11, 318),
        (12, 351), (13, 352), (14, 379), (15, 396), (16, 397), (17, 416), (18, 408), (19, 424), (20, 434), (22, 436),
        (24, 440), (26, 434), (28, 432), (30, 423),
    ];

    /// <summary>DIANA, total strain rotate crack model (рис. 6.22; оцифровка 07.10.2026): прогиб, мм → сила, кН.</summary>
    static readonly (double W, double P)[] Diana =
    [
        (1, 89), (2, 134), (3, 170), (4, 207), (5, 243), (6, 273), (7, 300), (8, 325), (9, 344), (10, 364), (11, 382),
        (12, 403), (13, 425), (14, 433), (15, 438), (16, 446), (17, 442), (18, 439), (19, 427),
    ];

    const double Ec = 36_500_000, Fcm = 22_600, Ftm = 2_370;   // кПа

    static MaterialChars ConcreteChars(CalcType ct) => new()
    {
        Type = MatType.Concrete, TypeCalc = ct, Fc = -Fcm, Ft = Ftm, E = Ec,
        Ec0 = -0.002, Ec1 = -0.6 * Fcm / Ec, Ec1Red = -0.0015, Ec2 = -0.0035,
        Et0 = 0.0001, Et1 = 0.6 * Ftm / Ec, Et1Red = 0.00008, Et2 = 0.00015,
    };

    static Material Concrete() => new()
    {
        Id = 1, Tag = "fcm 22,6 (B1)", Type = MatType.Concrete, E = Ec,
        MaterialChars = [ConcreteChars(CalcType.C), ConcreteChars(CalcType.CL), ConcreteChars(CalcType.N), ConcreteChars(CalcType.NL)],
    };

    /// <summary>Сечение CScore: X — ширина, Y — высота, начало в центре; бетон — 4 × 60 фибр.</summary>
    static CrossSection Section()
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

        var bars = new (int Id, double D, double Fy, double Fu, double Esu, int Count, double HalfSpread, double Y)[]
        {
            (2, 0.0299, 436_000, 700_000, 0.2, 2, 0.064, -H / 2 + 0.064),     // 2M30
            (3, 0.0252, 445_000, 680_000, 0.2, 2, 0.066, -H / 2 + 0.128),     // 2M25
            (4, 0.0113, 315_000, 460_000, 0.025, 3, 0.070, H / 2 - 0.050),    // 3M10
        };
        var areas = new List<MaterialArea> { concrete };
        foreach (var b in bars)
        {
            var mat = VecchioShimBeamTests.Rebar(b.Id, b.Fy);
            var r = new MaterialArea
            {
                Id = b.Id, Tag = $"арматура {b.Id}", Category = AreaCategory.RebarGroup,
                Material = mat, MaterialId = mat.Id, DiagrammType = DiagrammType.L2,
                HostArea = concrete, HostAreaId = concrete.Id,
            };
            for (int i = 0; i < b.Count; i++)
                r.Fibers.Add(Fiber.CreatePoint(b.D, -b.HalfSpread + i * 2 * b.HalfSpread / (b.Count - 1), b.Y));
            areas.Add(r);
        }
        // Хомуты D5: замкнутый контур по осям ветвей, две вертикальные ветви в плоскости сдвига.
        var stirrupMat = VecchioShimBeamTests.Rebar(5, 600_000);
        double sx = B / 2 - 0.030, sy = H / 2 - 0.030;
        areas.Add(new MaterialArea
        {
            Id = 5, Tag = "хомуты D5", Category = AreaCategory.Stirrups, Material = stirrupMat, MaterialId = stirrupMat.Id,
            Stirrups =
            [
                new StirrupGroup
                {
                    MaterialId = stirrupMat.Id, SpacingM = 0.190,
                    Elements = [new StirrupElement
                    {
                        CenterlineContour = new Contour([-sx, sx, sx, -sx, -sx], [-sy, -sy, sy, sy, -sy], "хомут"),
                        BarAreaM2 = 32.2e-6, BarDiameterM = 0.0064,
                    }],
                },
            ],
        });
        var section = new CrossSection { Id = 1, Tag = "B1", Areas = areas };
        section.ResolveAndBuildDiagramms(rebarDifferentialDiagram: false);
        foreach (var (a, b) in areas.Skip(1).Zip(bars))
            a.Diagramms = new[] { CalcType.C, CalcType.CL, CalcType.N, CalcType.NL }
                .ToDictionary(ct => ct, _ => VecchioShimBeamTests.Hardening(b.Fy, b.Fu, b.Esu, hardening: true));
        return section;
    }

    /// <summary>Свободно опёртая балка: <paramref name="n"/> КЭ, сила в середине до <see cref="PMax"/> за <paramref name="steps"/> шагов.</summary>
    static RcStructuralModel Model(int n, int steps)
    {
        var rs = new RcBeamSection("B1") { Cross = Section(), Calc = CalcType.N, TorsionGJ = 1e7 };
        var m = new RcStructuralModel();
        for (int i = 0; i <= n; i++) m.Nodes.Add(new RcNode(i, Span * i / n, 0, 0));
        // Локальная ось y — глобальная Y, ось z КЭ = высота сечения (Y сечения CScore).
        for (int i = 0; i < n; i++) m.Beams.Add(new RcBeam(i, i, i + 1, rs, [0.0, 1.0, 0.0]));
        m.Supports.Add(new RcSupport(0, 0b001111));
        m.Supports.Add(new RcSupport(n, 0b000110));
        var lc = new RcLoadCase(1, "P");
        lc.Nodal.Add(new RcNodalLoad(n / 2, [0, 0, -PMax, 0, 0, 0]));
        m.LoadCases.Add(lc);
        m.Stages.Add(new RcStage("P", [(1, 1.0)], steps));
        return m;
    }

    List<(double P, double W, int Cracked, int Yielded, int Failed)> Run(string name, string tag, RcSecantOptions options,
        int n = N, int steps = 100)
    {
        var m = Model(n, steps);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var run = RcSecantAnalysis.Run(m, options);
        var r = run.Result;
        var curve = r.Steps.Where(s => s.Converged).Select(s => (P: s.LoadFactor * PMax / 1e3,
            W: -s.U[run.Build.Dof(n / 2, 2)] * 1e3, Cracked: s.Beams.Count(x => x.Cracked), Yielded: s.Beams.Count(x => x.Yielded),
            Failed: s.Beams.Count(x => x.Failed))).ToList();
        output.WriteLine($"=== {name}: {(r.Completed ? "до конца" : r.Message)}; время {sw.Elapsed:mm\\:ss}");
        output.WriteLine("P, кН; w, мм; опыт при w, кН; DIANA при w, кН; трещины; текучесть; отказ");
        foreach (var c in curve)
            output.WriteLine(string.Join("; ", F(c.P), F(c.W), F(At(Experiment, c.W)), F(At(Diana, c.W)), c.Cracked, c.Yielded, c.Failed));
        if (Environment.GetEnvironmentVariable("OPENCS_CSFEA_OUT") is { Length: > 0 } dir)
        {
            Directory.CreateDirectory(dir);
            static string R(double v) => v.ToString("R", CultureInfo.InvariantCulture);
            File.WriteAllText(Path.Combine(dir, $"vs-b1-{tag}-curve.csv"), "P_kN,w_mm,cracked,yielded,failed\n" +
                string.Concat(curve.Select(c => $"{R(c.P)},{R(c.W)},{c.Cracked},{c.Yielded},{c.Failed}\n")));
        }
        return curve;
    }

    static double At((double W, double P)[] data, double w) => VecchioShimBeamTests.At(data, w);

    static string F(double v) => VecchioShimBeamTests.F(v);

    [Fact]
    public void Psi() => Run("ψs, сдвиг", "psi", new RcSecantOptions { Psi = true });

    [Fact]
    public void NoPsi() => Run("без ψs, сдвиг", "nopsi", new RcSecantOptions { Psi = false });

    [Fact]
    public void PsiBernoulli() => Run("ψs, Бернулли", "psi-bernoulli", new RcSecantOptions { Psi = true, BeamShear = false });

    [Fact]
    public void PsiFineMesh() => Run("ψs, сдвиг, 48 КЭ", "psi-48", new RcSecantOptions { Psi = true }, n: 48);

    /// <summary>Сдвиговые характеристики сечения B1 — сверка с ручным расчётом по формулам мини-спеки.</summary>
    [Fact]
    public void ShearSection()
    {
        var s = BeamShearSection.From(Section(), CalcType.N)!;
        double ga0 = 0.4 * Ec * 5.0 / 6.0 * B * H * 1e3;
        Assert.Equal(ga0, s.Initial.GAvZ, ga0 * 1e-9);
        var law = s.CrackedZ(tensionPositive: false)!;   // растянут низ
        double as1 = 2 * Math.PI * 0.0299 * 0.0299 / 4, as2 = 2 * Math.PI * 0.0252 * 0.0252 / 4;
        double h0 = H - (as1 * 0.064 + as2 * 0.128) / (as1 + as2);
        double rho = 2 * 32.2e-6 / (B * 0.190), n = 200_000_000 / Ec;
        double kv = rho / (1 + 4 * n * rho) * 200e9 * B * h0;
        output.WriteLine($"GA0 = {s.Initial.GAvZ / 1e6:0} МН, Kv = {law.Kv / 1e6:0.0} МН, h0 = {h0:0.000} м, ρw = {rho:0.00000}");
        Assert.Equal(kv, law.Kv, kv * 1e-9);
    }
}
