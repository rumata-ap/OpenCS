using CScore;
using CScore.Hysteresis;
using CSmath;

namespace OpenCS.OpenSees.Tests.Hysteresis;

/// <summary>Сверка моделей с состоянием с материалами OpenSees (внешняя программа, эталон).</summary>
[Trait("Category", "Slow")]
public sealed class HysteresisOpenSeesTests
{
    // OpenSees — в МПа, CScore — в кПа.
    const double E = 200000, Fy = 400, B = 0.01, Ey = Fy / E;
    const int N = 40;

    static List<double> Ours(IHysteresisModel m, IEnumerable<double> strains, bool ten = true)
    {
        var s = m.Initial; var r = new List<double>();
        foreach (var e in strains) { r.Add(m.Trial(s, e, ten, out _, out var n) / 1000.0); s = n; }
        return r;
    }

    static double[] SteelProgram => [Ey, -Ey, 2 * Ey, -2 * Ey, 4 * Ey, -4 * Ey, 4 * Ey];

    [Fact]
    public async Task Bilinear_MatchesSteel01()
    {
        var os = await UniaxialLoopRunner.RunAsync($"uniaxialMaterial Steel01 1 {Fy} {E} {B}", SteelProgram, N);
        var ours = Ours(new BilinearSteel(E * 1000, Fy * 1000, Fy * 1000, B), os.Select(p => p.Eps));
        for (int i = 0; i < os.Count; i++) Assert.True(Math.Abs(os[i].Sig - ours[i]) < 0.01 * Fy, $"i={i} ε={os[i].Eps} OS={os[i].Sig} наш={ours[i]}");
    }

    [Fact]
    public async Task MenegottoPinto_MatchesSteel02()
    {
        var os = await UniaxialLoopRunner.RunAsync($"uniaxialMaterial Steel02 1 {Fy} {E} {B} 20 0.925 0.15", SteelProgram, N);
        var ours = Ours(new MenegottoPintoSteel(E * 1000, Fy * 1000, Fy * 1000, B), os.Select(p => p.Eps));
        for (int i = 0; i < os.Count; i++) Assert.True(Math.Abs(os[i].Sig - ours[i]) < 0.01 * Fy, $"i={i} ε={os[i].Eps} OS={os[i].Sig} наш={ours[i]}");
    }

    [Fact]
    public async Task KarsanJirsa_MatchesConcrete01()
    {
        // εU отодвинута за программу: за εU Concrete01 замораживает εp на значении при εU (остаточная площадка fpcu),
        // а формула Taucer продолжается; у огибающих СП 63 остаточной площадки нет. На ниспадающем участке
        // (η = 2,5 у −0,005) Concrete01 следует второй ветви формулы — её и проверяем.
        const double fpc = -30, ec0 = -0.002, fpcu = -6, ecu = -0.006;
        double[] program = [-0.001, -0.0002, -0.002, 0.0, -0.003, -0.0005, -0.005, 0.0];
        var os = await UniaxialLoopRunner.RunAsync($"uniaxialMaterial Concrete01 1 {fpc} {ec0} {fpcu} {ecu}", program, N);
        var env = KentPark(fpc * 1000, ec0, fpcu * 1000, ecu);
        var ours = Ours(new KarsanJirsaConcrete(env), os.Select(p => p.Eps), ten: false);
        for (int i = 0; i < os.Count; i++) Assert.True(Math.Abs(os[i].Sig - ours[i]) < 0.02 * Math.Abs(fpc), $"i={i} ε={os[i].Eps} OS={os[i].Sig} наш={ours[i]}");
    }

    /// <summary>Огибающая Кента–Парка (как у Concrete01), кПа; узлы ровно в εc0 и εU.</summary>
    static Diagramm KentPark(double fpc, double ec0, double fpcu, double ecu)
    {
        double lo = 4 * ecu; int n = 2000;
        var xs = Enumerable.Range(0, n + 1).Select(i => lo * (1 - (double)i / n))
            .Where(e => Math.Abs(e - ec0) > 1e-7 && Math.Abs(e - ecu) > 1e-7)
            .Append(ec0).Append(ecu).Append(0.0).Distinct().OrderBy(e => e).ToArray();
        var ys = xs.Select(e => e >= ec0 ? fpc * (2 * e / ec0 - (e / ec0) * (e / ec0))
            : e >= ecu ? fpc + (fpcu - fpc) * (e - ec0) / (ecu - ec0) : fpcu).ToArray();
        ys[^1] = 0;
        return new Diagramm(new LSpline(xs, ys), new LSpline([0, 1e-9], [0, 0]), DiagrammType.Custom, MatType.Concrete, "KentPark");
    }
}
