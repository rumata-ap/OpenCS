using System.Diagnostics;
using CScore.ShellSimpl;
using Xunit;
using Xunit.Abstractions;
using static CScore.Tests.ShellSimpl.StripNdmFixture;

namespace CScore.Tests.ShellSimpl;

/// <summary>Подбор сеток Капра–Мори по ПС2 на НДМ полосы: <see cref="CapraMauryDesignSolver.DesignSls"/> (спека, раздел 6, п. 3).</summary>
public sealed class CapraMauryDesignSlsTests(ITestOutputHelper output)
{
    const double Es = 200_000_000.0;

    static readonly Lazy<CapraMauryDesignSolver.SlsContext> Ctx03 =
        new(() => CapraMauryDesignSolver.CreateSls(Plate(), Concrete(), Rebar(), 0.3));

    static CapraMauryDesignResult Design(double nx, double ny, double nxy, double mx, double my, double mxy,
        CapraMauryDesignSolver.SlsContext? ctx = null, double step = 10.0) =>
        CapraMauryDesignSolver.DesignSls(ctx ?? Ctx03.Value, nx, ny, nxy, mx, my, mxy, step);

    static double Sum(CapraMauryDesignResult d) => d.Top.Asx + d.Top.Asy + d.Bottom.Asx + d.Bottom.Asy;

    void Print(string title, CapraMauryDesignResult d)
    {
        output.WriteLine($"{title}: верх Asx = {d.Top.Asx * 1e4:F3}, Asy = {d.Top.Asy * 1e4:F3}; " +
                         $"низ Asx = {d.Bottom.Asx * 1e4:F3}, Asy = {d.Bottom.Asy * 1e4:F3} см²/м; " +
                         $"управляющие верх [{string.Join(", ", d.Top.ActiveThetas)}], низ [{string.Join(", ", d.Bottom.ActiveThetas)}]");
    }

    public static TheoryData<double, double, double, double, double, double> Cases => new()
    {
        { 0, 0, 0, 30, 10, 8 },          // изгиб с кручением — обе сетки
        { 0, 0, 0, -25, 15, -12 },
        { -100, -50, 20, 40, 20, 15 },   // внецентренное сжатие с трещиной
        { 150, 80, 40, 20, -10, 6 },     // внецентренное растяжение
        { 400, 300, 100, 5, 3, 2 },      // сквозное растяжение — оба ряда
        { 0, 0, 0, 0, 0, 25 },           // чистое кручение
    };

    // a_crc при найденной сетке ≤ предела на всех фасетках (обеспечиваемые площади обоих рядов),
    // сетки минимальны: уменьшение любой ненулевой компоненты на 1 % нарушает требование фасетки.
    [Theory]
    [MemberData(nameof(Cases))]
    public void DesignedMeshes_CrackWidthWithinLimit_AndMinimal(double nx, double ny, double nxy, double mx, double my, double mxy)
    {
        var d = Design(nx, ny, nxy, mx, my, mxy);
        Print($"N = ({nx}, {ny}, {nxy}), M = ({mx}, {my}, {mxy})", d);
        Assert.Equal("sls", d.Limit);
        Assert.Equal(0.3, d.AcrcLimMm);
        Assert.True(d.Feasible);
        Assert.Empty(d.Diagnostics);
        Assert.True(Sum(d) > 0.0);

        double worst = 0.0;
        foreach (var f in d.Facets)
        {
            Assert.True(f.AsNTop >= f.AsTop * (1 - 1e-9) - 1e-12, $"θ = {f.ThetaDeg}: верх");
            Assert.True(f.AsNBot >= f.AsBot * (1 - 1e-9) - 1e-12, $"θ = {f.ThetaDeg}: низ");
            Assert.True(f.AcrcMm <= 0.3 + 1e-9, $"θ = {f.ThetaDeg}: a_crc при требуемых площадях");
            var s = StripNdm.CrackWidth(Strip(CalcType.N, asTop: f.AsNTop, asBot: f.AsNBot), f.N, f.M, [Ds, Ds], Es, 1.0, 0.5);
            Assert.True(s.Converged || !s.Cracked, $"θ = {f.ThetaDeg}: {s.Reason}");
            worst = Math.Max(worst, s.AcrcMm);
            Assert.True(s.AcrcMm <= 0.3 * 1.005, $"θ = {f.ThetaDeg}: a_crc = {s.AcrcMm:F4} мм при сетке");
        }
        output.WriteLine($"  наибольшая a_crc при сетке по фасеткам: {worst:F4} мм");

        // Проверка Капра–Мори по трещинам на НДМ при подобранных сетках — тот же итог.
        var plate = Mesh(d);
        var p = new ShellSimplSolver.SolveParams(nx, ny, nxy, mx, my, mxy, "shell_simpl_capri_sls", AcrcLimMm: 0.3,
            Model: StripModel.Ndm);
        var check = ShellSimplSolver.Solve(p, plate, Concrete(), Rebar(), CalcType.N);
        double acrcCheck = check.CapriDirs!.Max(x => x.Strip.Acrc_mm);
        output.WriteLine($"  проверка ПС2 по фасеткам: a_crc,max = {acrcCheck:F4} мм");
        Assert.True(acrcCheck <= 0.3 * 1.005);

        static double Prov(double asx, double asy, double theta)
        {
            double a = theta * Math.PI / 180.0;
            return asx * Math.Cos(a) * Math.Cos(a) + asy * Math.Sin(a) * Math.Sin(a);
        }
        foreach (bool top in new[] { true, false })
        {
            var mesh = top ? d.Top : d.Bottom;
            Func<CapraMauryDesignFacet, double> req = top ? f => f.AsTop : f => f.AsBot;
            if (mesh.Asx > 1e-9)
                Assert.Contains(d.Facets, f => Prov(0.99 * mesh.Asx, mesh.Asy, f.ThetaDeg) < req(f) - 1e-12);
            if (mesh.Asy > 1e-9)
                Assert.Contains(d.Facets, f => Prov(mesh.Asx, 0.99 * mesh.Asy, f.ThetaDeg) < req(f) - 1e-12);
        }
    }

    static PlateSection Mesh(CapraMauryDesignResult d) => new()
    {
        H = H, NLayers = 40, PlateModel = "layered", ConcreteDiagramType = DiagrammType.L3,
        RebarLayers =
        [
            Layer("низ", -Z, d.Bottom.Asx, d.Bottom.Asy),
            Layer("верх", Z, d.Top.Asx, d.Top.Asy),
        ],
    };

    static PlateRebarLayer Layer(string name, double z, double asx, double asy) => new()
    {
        Name = name, InputMode = "direct", Asx = asx, Asy = asy, Zsx = z, Zsy = z, DiameterX = Ds, DiameterY = Ds,
    };

    // Ряд у сжатой моментом грани не меньше As_c из ПС1 той же фасетки; без ПС1 — As_c = 0.
    [Fact]
    public void CompressionRow_StartsFromUlsOfSameFacet()
    {
        var with = Design(400, 300, 100, 5, 3, 2);   // ПС1 требует оба ряда
        foreach (var f in with.Facets)
        {
            double opposite = f.M >= 0 ? f.AsBot : f.AsTop;
            Assert.True(opposite >= f.AsCUls * (1 - 1e-9) - 1e-12, $"θ = {f.ThetaDeg}");
        }
        Assert.Contains(with.Facets, f => f.AsCUls > 1e-6);

        var ctxNoUls = CapraMauryDesignSolver.CreateSls(Plate(), Concrete(), Rebar(), 0.3, withUls: false);
        Assert.False(ctxNoUls.WithUls);
        var without = Design(400, 300, 100, 5, 3, 2, ctxNoUls);
        Assert.All(without.Facets, f => Assert.Equal(0.0, f.AsCUls));
        Print("с ПС1", with);
        Print("без ПС1", without);
    }

    // Строже предел — больше арматуры.
    [Fact]
    public void StricterLimit_NeedsMoreSteel()
    {
        var ctx01 = CapraMauryDesignSolver.CreateSls(Plate(), Concrete(), Rebar(), 0.1);
        var d03 = Design(0, 0, 0, 30, 10, 8);
        var d01 = Design(0, 0, 0, 30, 10, 8, ctx01);
        Print("a_crc,ult = 0,3", d03);
        Print("a_crc,ult = 0,1", d01);
        Assert.True(Sum(d01) > Sum(d03));
    }

    // Без кручения и сдвига сетки равны подбору одиночных полос x и y.
    [Fact]
    public void NoTwist_MatchesSingleStrips()
    {
        var ctx = CapraMauryDesignSolver.CreateSls(Plate(), Concrete(), Rebar(), 0.3, withUls: false);
        var d = Design(0, 0, 0, 30, -20, 0, ctx);
        var rx = StripNdmDesign.RequiredSls(0, 30, H, Z, -Z, 0.0, 0.3, ConcreteDiagram(CalcType.N), RebarDiagram(CalcType.N), Ds, Ds, Es, 1.0, 0.5);
        var ry = StripNdmDesign.RequiredSls(0, -20, H, Z, -Z, 0.0, 0.3, ConcreteDiagram(CalcType.N), RebarDiagram(CalcType.N), Ds, Ds, Es, 1.0, 0.5);
        Assert.Equal(rx.AsTension, d.Top.Asx, 1e-8);
        Assert.Equal(ry.AsTension, d.Bottom.Asy, 1e-8);
        Assert.Equal(0.0, d.Top.Asy, 1e-12);
        Assert.Equal(0.0, d.Bottom.Asx, 1e-12);
    }

    [Fact]
    public void ZeroForces_NoReinforcement()
    {
        var d = Design(0, 0, 0, 0, 0, 0);
        Assert.Equal(0.0, Sum(d));
        Assert.True(d.Feasible);
        Assert.All(d.Facets, f => Assert.Equal(StripNdmDesignStatus.NotRequired, f.Status));
    }

    [Fact]
    public void Impossible_ReportsDiagnostics()
    {
        var ctx = CapraMauryDesignSolver.CreateSls(Plate(), Concrete(), Rebar(), 0.001);
        var d = Design(0, 0, 0, 120, 0, 0, ctx);
        Assert.False(d.Feasible);
        Assert.NotEmpty(d.Diagnostics);
    }

    // Замер, не утверждение.
    [Fact]
    public void Timing_100Rows()
    {
        var ctx = Ctx03.Value;
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 100; i++)
            CapraMauryDesignSolver.DesignSls(ctx, -100 + 2 * i, 50 - i, 20, 10 + 0.3 * i, -5 + 0.2 * i, 8);
        output.WriteLine($"100 строк × 18 фасеток: {sw.ElapsedMilliseconds} мс");
    }
}
