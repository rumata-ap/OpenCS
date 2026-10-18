using CScore.Fem;
using CScore.ShellSimpl;
using Xunit;
using Xunit.Abstractions;
using static CScore.Tests.ShellSimpl.StripNdmFixture;

namespace CScore.Tests.ShellSimpl;

/// <summary>Проверка Капра–Мори через ограничения r(θ)/As_n(θ) (спека, разделы 7 и 10, п. 8).</summary>
public sealed class CapraMauryConstraintsCheckTests(ITestOutputHelper output)
{
    static PlateSection Mesh(double asXTop, double asYTop, double asXBot, double asYBot) => new()
    {
        H = H, NLayers = 40, PlateModel = "layered", ConcreteDiagramType = DiagrammType.L3,
        RebarLayers =
        [
            Layer("низ", -Z, asXBot, asYBot),
            Layer("верх", Z, asXTop, asYTop),
        ],
    };

    static PlateRebarLayer Layer(string name, double z, double asx, double asy) => new()
    {
        Name = name, InputMode = "direct", Asx = asx, Asy = asy, Zsx = z, Zsy = z, DiameterX = Ds, DiameterY = Ds,
    };

    static ShellSimplSolver.SolveResult Solve(PlateSection plate, string kind, CapriCheckMode mode,
        double nx, double ny, double nxy, double mx, double my, double mxy)
    {
        var p = new ShellSimplSolver.SolveParams(nx, ny, nxy, mx, my, mxy, kind, AcrcLimMm: 0.3,
            Model: StripModel.Ndm, CapriMode: mode);
        return ShellSimplSolver.Solve(p, plate, Concrete(), Rebar(), kind.EndsWith("sls") ? CalcType.N : CalcType.C);
    }

    public static TheoryData<double, double, double, double, double, double> UlsCases => new()
    {
        { 0, 0, 0, 30, 10, 8 },
        { 0, 0, 0, -25, 15, -12 },
        { -300, -150, 50, 40, 20, 15 },  // внецентренное сжатие
        { 150, 80, 40, 20, -10, 6 },     // внецентренное растяжение
        { 400, 300, 100, 5, 3, 2 },      // сквозное растяжение
        { 0, 0, 0, 0, 0, 25 },           // чистое кручение
    };

    // Спека 10, п. 8: η − 1 одного знака в обоих режимах вне узкой окрестности 1 — сетки исходного
    // сечения, умноженные на k.
    [Theory]
    [MemberData(nameof(UlsCases))]
    public void Uls_ConstraintsAgreeWithDirect_OnSignOfEtaMinusOne(double nx, double ny, double nxy, double mx, double my, double mxy)
    {
        int compared = 0;
        foreach (double k in new[] { 0.3, 0.5, 0.7, 1.0, 1.5, 2.5 })
        {
            var plate = Mesh(k * AsPerMeter, 0.8 * k * AsPerMeter, 1.2 * k * AsPerMeter, k * AsPerMeter);
            double direct = Solve(plate, "shell_simpl_capri_uls", CapriCheckMode.Direct, nx, ny, nxy, mx, my, mxy).EtaMax!.Value;
            var cons = Solve(plate, "shell_simpl_capri_uls", CapriCheckMode.Constraints, nx, ny, nxy, mx, my, mxy);
            double eta = cons.EtaMax!.Value;
            output.WriteLine($"k = {k}: по фасеткам η = {direct:F3}, через ограничения η = {eta:F3}");
            Assert.NotNull(cons.CapriConstraints);
            Assert.Equal(18, cons.CapriConstraints!.Count);
            if (Math.Abs(direct - 1.0) < 0.05) continue;
            compared++;
            Assert.Equal(direct > 1.0, eta > 1.0);
        }
        Assert.True(compared >= 4);
    }

    public static TheoryData<double, double, double, double, double, double> SlsCases => new()
    {
        { 0, 0, 0, 30, 10, 8 },
        { 0, 0, 0, -25, 15, -12 },
        { -100, -50, 20, 40, 20, 15 },
        { 150, 80, 40, 20, -10, 6 },
        { 0, 0, 0, 0, 0, 25 },
    };

    // ПС2: знак a_crc/a_crc,ult − 1 по фасеткам и η через ограничения.
    [Theory]
    [MemberData(nameof(SlsCases))]
    public void Sls_ConstraintsAgreeWithDirect_OnSignOfEtaMinusOne(double nx, double ny, double nxy, double mx, double my, double mxy)
    {
        int compared = 0;
        foreach (double k in new[] { 0.3, 0.5, 0.8, 1.2, 2.0, 3.0 })
        {
            var plate = Mesh(k * AsPerMeter, 0.8 * k * AsPerMeter, 1.2 * k * AsPerMeter, k * AsPerMeter);
            var d = Solve(plate, "shell_simpl_capri_sls", CapriCheckMode.Direct, nx, ny, nxy, mx, my, mxy);
            Assert.Null(d.CapriConstraints);
            double direct = d.CapriDirs!.Max(x => x.Strip.Acrc_mm) / 0.3;
            double eta = Solve(plate, "shell_simpl_capri_sls", CapriCheckMode.Constraints, nx, ny, nxy, mx, my, mxy).EtaMax!.Value;
            output.WriteLine($"k = {k}: по фасеткам a_crc/a_crc,ult = {direct:F3}, через ограничения η = {eta:F3}");
            if (Math.Abs(direct - 1.0) < 0.05) continue;
            compared++;
            Assert.Equal(direct > 1.0, eta > 1.0);
        }
        Assert.True(compared >= 3);
    }

    // Подобранные по ПС1 сетки проходят проверку через ограничения: r при заданной второй сетке не
    // больше требования подбора (с точностью бисекции).
    [Fact]
    public void Uls_DesignedMeshes_PassConstraints()
    {
        var ctx = CapraMauryDesignSolver.CreateUls(Plate(), Concrete(), Rebar(), CalcType.C);
        var d = CapraMauryDesignSolver.DesignUls(ctx, 50, -20, 30, 30, 10, 8);
        var plate = Mesh(d.Top.Asx, d.Top.Asy, d.Bottom.Asx, d.Bottom.Asy);
        var r = Solve(plate, "shell_simpl_capri_uls", CapriCheckMode.Constraints, 50, -20, 30, 30, 10, 8);
        output.WriteLine($"η = {r.EtaMax:F4}");
        Assert.InRange(r.EtaMax!.Value, 0.5, 1.01);
        foreach (var f in r.CapriConstraints!)
            Assert.True(f.Eta <= 1.01, $"θ = {f.ThetaDeg}: η = {f.Eta:F4}");
        Assert.Equal(d.Top.Asx, r.CapriConstraints![0].AsNTop, 12);
        Assert.Equal(d.Bottom.Asy, r.CapriConstraints![9].AsNBot, 12);
    }

    // Слабая сетка: сечение с трещиной не уравновешивается — a_crc по фасеткам не ограничена (не 0).
    [Fact]
    public void Sls_Direct_NoCrackedEquilibrium_AcrcUnbounded()
    {
        double k = 0.3;
        var plate = Mesh(k * AsPerMeter, 0.8 * k * AsPerMeter, 1.2 * k * AsPerMeter, k * AsPerMeter);
        var d = Solve(plate, "shell_simpl_capri_sls", CapriCheckMode.Direct, 0, 0, 0, 30, 10, 8);
        var worst = d.CapriDirs!.MaxBy(x => x.Strip.Acrc_mm)!;
        Assert.Equal(StripNdmAdapter.UnboundedAcrcMm, worst.Strip.Acrc_mm);
        Assert.Contains("не найдено", worst.Strip.Case);
        Assert.Equal(StripNdmAdapter.UnboundedAcrcMm, d.CriticalTop!.Strip.Acrc_mm);

        var wa = Solve(plate, "shell_simpl_wa_sls", CapriCheckMode.Direct, 0, 0, 0, 30, 10, 8);
        Assert.Contains(wa.WaStrips!, s => s.Acrc_mm == StripNdmAdapter.UnboundedAcrcMm);
    }

    [Fact]
    public void ZeroLoad_EtaZero()
    {
        var r = Solve(Plate(), "shell_simpl_capri_uls", CapriCheckMode.Constraints, 0, 0, 0, 0, 0, 0);
        Assert.Equal(0.0, r.EtaMax);
        Assert.All(r.CapriConstraints!, f => Assert.Equal(0.0, f.RTop + f.RBot));
    }

    // Требование к пустой сетке — η = 1e9 (вид показывает ∞).
    [Fact]
    public void MissingMesh_WithDemand_EtaHuge()
    {
        var plate = Mesh(AsPerMeter, AsPerMeter, 0.0, 0.0);
        var r = Solve(plate, "shell_simpl_capri_uls", CapriCheckMode.Constraints, 0, 0, 0, -30, -20, 0);
        Assert.Equal(1e9, r.EtaMax);
        var f0 = r.CapriConstraints![0];
        Assert.True(f0.RBot > 0.0);
        Assert.Equal(1e9, f0.EtaBot);
    }

    // Через ограничения — только Капра–Мори; Вуд–Армер режим не замечает.
    [Fact]
    public void WoodArmer_IgnoresMode()
    {
        var r = Solve(Plate(), "shell_simpl_wa_uls", CapriCheckMode.Constraints, 0, 0, 0, 30, 10, 8);
        Assert.Null(r.CapriConstraints);
        Assert.NotNull(r.WaStrips);
    }

    [Fact]
    public void PlateCheckParams_WithoutField_IsDirect_AndRoundTrip()
    {
        Assert.Equal(CapriCheckMode.Direct, PlateCheckParams.Parse("{\"Kind\":\"shell_simpl_capri_uls\"}").CapriCheckMode);
        var json = new PlateCheckParams { Kind = "shell_simpl_capri_sls", CapriCheckMode = CapriCheckMode.Constraints }.ToJson();
        Assert.Equal(CapriCheckMode.Constraints, PlateCheckParams.Parse(json).CapriCheckMode);
    }
}
