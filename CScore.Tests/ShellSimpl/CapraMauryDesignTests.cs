using System.Diagnostics;
using CScore.ShellSimpl;
using Xunit;
using Xunit.Abstractions;
using static CScore.Tests.ShellSimpl.StripNdmFixture;

namespace CScore.Tests.ShellSimpl;

/// <summary>Подбор сеток Капра–Мори по ПС1 на НДМ полосы: <see cref="CapraMauryDesignSolver"/> (спека, раздел 10, п. 6).</summary>
public sealed class CapraMauryDesignTests(ITestOutputHelper output)
{
    static readonly Lazy<CapraMauryDesignSolver.Context> Ctx =
        new(() => CapraMauryDesignSolver.CreateUls(Plate(), Concrete(), Rebar(), CalcType.C));

    static CapraMauryDesignResult Design(double nx, double ny, double nxy, double mx, double my, double mxy, double step = 10.0) =>
        CapraMauryDesignSolver.DesignUls(Ctx.Value, nx, ny, nxy, mx, my, mxy, step);

    static PlateSection Mesh(CapraMauryDesignResult d, double k = 1.0, double kx = 1.0, double ky = 1.0, bool top = true) => new()
    {
        H = H, NLayers = 40, PlateModel = "layered", ConcreteDiagramType = DiagrammType.L3,
        RebarLayers =
        [
            Layer("низ", -Z, d.Bottom.Asx * k * (top ? 1.0 : kx), d.Bottom.Asy * k * (top ? 1.0 : ky)),
            Layer("верх", Z, d.Top.Asx * k * (top ? kx : 1.0), d.Top.Asy * k * (top ? ky : 1.0)),
        ],
    };

    static PlateRebarLayer Layer(string name, double z, double asx, double asy) => new()
    {
        Name = name, InputMode = "direct", Asx = asx, Asy = asy, Zsx = z, Zsy = z, DiameterX = Ds, DiameterY = Ds,
    };

    // Проверка Капра–Мори на НДМ полосы при подобранных сетках.
    static double CheckEta(PlateSection plate, double nx, double ny, double nxy, double mx, double my, double mxy, double step = 10.0)
    {
        var p = new ShellSimplSolver.SolveParams(nx, ny, nxy, mx, my, mxy, "shell_simpl_capri_uls", StepDeg: step,
            Model: StripModel.Ndm);
        return ShellSimplSolver.Solve(p, plate, Concrete(), Rebar(), CalcType.C).EtaMax ?? 0.0;
    }

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
        { -300, -200, 50, 40, 20, 15 },  // внецентренное сжатие
        { 150, 80, 40, 20, -10, 6 },     // внецентренное растяжение
        { 400, 300, 100, 5, 3, 2 },      // сквозное растяжение — оба ряда
        { 0, 0, 0, 0, 0, 25 },           // чистое кручение
    };

    // (а) найденные сетки удовлетворяют всем фасеткам: проверка Капра–Мори на НДМ даёт η ≤ 1;
    // (б) уменьшение любой ненулевой компоненты на 1 % нарушает хотя бы одно требование фасетки.
    [Theory]
    [MemberData(nameof(Cases))]
    public void DesignedMeshes_PassCheck_AndAreMinimal(double nx, double ny, double nxy, double mx, double my, double mxy)
    {
        var d = Design(nx, ny, nxy, mx, my, mxy);
        Print($"N = ({nx}, {ny}, {nxy}), M = ({mx}, {my}, {mxy})", d);
        Assert.True(d.Feasible);
        Assert.Empty(d.Diagnostics);

        foreach (var f in d.Facets)
        {
            Assert.True(f.AsNTop >= f.AsTop * (1 - 1e-9) - 1e-12, $"θ = {f.ThetaDeg}: верх");
            Assert.True(f.AsNBot >= f.AsBot * (1 - 1e-9) - 1e-12, $"θ = {f.ThetaDeg}: низ");
        }

        double eta = CheckEta(Mesh(d), nx, ny, nxy, mx, my, mxy);
        output.WriteLine($"  проверка по фасеткам: η = {eta:F4}");
        Assert.InRange(eta, 0.0, 1.005);

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

    // (в) смена знака Nxy и Mxy зеркалит фасетки θ ↔ 180° − θ, сетки не меняются.
    [Fact]
    public void Symmetry_SignOfTwistAndShear()
    {
        var a = Design(-100, 50, 40, 20, -8, 12);
        var b = Design(-100, 50, -40, 20, -8, -12);
        Assert.Equal(a.Top.Asx, b.Top.Asx, 1e-9);
        Assert.Equal(a.Top.Asy, b.Top.Asy, 1e-9);
        Assert.Equal(a.Bottom.Asx, b.Bottom.Asx, 1e-9);
        Assert.Equal(a.Bottom.Asy, b.Bottom.Asy, 1e-9);
    }

    // (г) сходимость по шагу: 10° → 5° → 2,5°, суммы сеток сходятся.
    [Fact]
    public void Convergence_WithStep()
    {
        double Sum(CapraMauryDesignResult d) => d.Top.Asx + d.Top.Asy + d.Bottom.Asx + d.Bottom.Asy;
        var s10 = Sum(Design(-50, 20, 30, 25, -5, 14, 10.0));
        var s5 = Sum(Design(-50, 20, 30, 25, -5, 14, 5.0));
        var s25 = Sum(Design(-50, 20, 30, 25, -5, 14, 2.5));
        output.WriteLine($"Σ: 10° — {s10 * 1e4:F4}, 5° — {s5 * 1e4:F4}, 2,5° — {s25 * 1e4:F4} см²/м");
        // Более частая сетка фасеток добавляет ограничения — сумма не убывает.
        Assert.True(s5 >= s10 * (1 - 1e-9));
        Assert.True(s25 >= s5 * (1 - 1e-9));
        Assert.True(Math.Abs(s25 - s5) <= Math.Max(Math.Abs(s5 - s10), 0.002 * s25) + 1e-12);
        Assert.True(Math.Abs(s25 - s10) <= 0.03 * s25);
    }

    // (д) без кручения и сдвига подбор совпадает с подбором одиночных полос x и y.
    [Theory]
    [InlineData(0, 0, 30, 12)]
    [InlineData(0, 0, -20, 35)]
    [InlineData(-200, -100, 25, -15)]
    public void NoTwist_MatchesSingleStrips(double nx, double ny, double mx, double my)
    {
        var d = Design(nx, ny, 0, mx, my, 0);
        var rx = StripNdmDesign.RequiredUls(nx, mx, H, Z, -Z, ConcreteDiagram(CalcType.C), RebarDiagram(CalcType.C), Limits());
        var ry = StripNdmDesign.RequiredUls(ny, my, H, Z, -Z, ConcreteDiagram(CalcType.C), RebarDiagram(CalcType.C), Limits());
        static double T(StripNdmDesignResult r) => r.TensionTop ? r.AsTension : r.AsCompression;
        static double B(StripNdmDesignResult r) => r.TensionTop ? r.AsCompression : r.AsTension;
        Print($"N = ({nx}, {ny}), M = ({mx}, {my})", d);
        Assert.Equal(T(rx), d.Top.Asx, 1e-8);
        Assert.Equal(T(ry), d.Top.Asy, 1e-8);
        Assert.Equal(B(rx), d.Bottom.Asx, 1e-8);
        Assert.Equal(B(ry), d.Bottom.Asy, 1e-8);
    }

    // (е) знак момента выбирает сетку: положительные моменты — верх, отрицательные — низ.
    [Fact]
    public void TopAndBottom_FollowMomentSign()
    {
        var up = Design(0, 0, 0, 30, 20, 0);
        Assert.True(up.Top.Asx > 0 && up.Top.Asy > 0);
        Assert.Equal(0.0, up.Bottom.Asx);
        Assert.Equal(0.0, up.Bottom.Asy);

        var down = Design(0, 0, 0, -30, -20, 0);
        Assert.Equal(up.Top.Asx, down.Bottom.Asx, 1e-9);
        Assert.Equal(up.Top.Asy, down.Bottom.Asy, 1e-9);
        Assert.Equal(0.0, down.Top.Asx);
    }

    [Fact]
    public void Impossible_ReportsDiagnostics()
    {
        var d = Design(-20_000, -20_000, 0, 0, 0, 0);
        Assert.False(d.Feasible);
        Assert.NotEmpty(d.Diagnostics);
    }

    [Fact]
    public void ZeroForces_NoReinforcement()
    {
        var d = Design(0, 0, 0, 0, 0, 0);
        Assert.Equal(0.0, d.Top.Asx + d.Top.Asy + d.Bottom.Asx + d.Bottom.Asy);
        Assert.True(d.Feasible);
    }

    // Замер, не утверждение (спека, раздел 12: ориентир ≤ 20 мс на строку).
    [Fact]
    public void Timing_1000Rows()
    {
        var ctx = Ctx.Value;
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 1000; i++)
            CapraMauryDesignSolver.DesignUls(ctx, -100 + 0.2 * i, 50 - 0.1 * i, 20, 10 + 0.03 * i, -5 + 0.02 * i, 8);
        output.WriteLine($"1000 строк × 18 фасеток: {sw.ElapsedMilliseconds} мс");
    }
}
