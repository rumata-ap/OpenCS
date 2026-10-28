using CScore.Fem;
using CScore.Fem.Fragment.Masks;
using CScore.Planar;
using CScore.Submodel;
using Xunit;

namespace CScore.Tests.Fem.Fragment.Masks;

public sealed class FragmentBarCutSamplerTests
{
    static PlanarVector3 V(double x, double y, double z) => new(x, y, z);

    static void Near(Dof6 expected, Dof6 actual, double tol)
    {
        for (int i = 0; i < 6; i++) Assert.True(Math.Abs(expected[i] - actual[i]) <= tol, $"DOF {i}: {expected[i]} ≠ {actual[i]}");
    }

    /// <summary>Жёсткое смещение и поворот: u(p) = t + θ × (p − o) воспроизводится точно в любой точке.</summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(0.3)]
    [InlineData(1.0)]
    public void RigidMotion_ReproducedExactly(double xi)
    {
        var o = V(1, 2, 0);
        var t = V(1e-3, -2e-3, 5e-4);
        var th = V(2e-4, -1e-4, 3e-4);
        Dof6 At(PlanarVector3 p) => Dof6.FromParts(t + th.Cross(p - o), th);
        PlanarVector3 pi = V(0, 0, 0), pj = V(0.3, 0.4, 3);

        var u = FragmentBarCutSampler.Displacement(pi, pj, 25, At(pi), At(pj), xi);

        Near(At(pi + (pj - pi) * xi), u, 1e-12);
    }

    /// <summary>Консоль EI под силой на конце: прогиб кубический — Эрмит точен; поворот — производная.</summary>
    [Fact]
    public void CubicDeflection_Exact()
    {
        // Ось X, L = 2; w(x) = P x²(3L − x)/(6EI), θ = P x(2L − x)/(2EI) — направление местной оси Y (глобальная Z).
        const double L = 2, k = 1e-3; // k = P/EI
        double W(double x) => k * x * x * (3 * L - x) / 6;
        double Th(double x) => k * x * (2 * L - x) / 2;
        PlanarVector3 pi = V(0, 0, 0), pj = V(L, 0, 0);
        // Местные оси: x = X, y = Z, z = x × y = −Y; прогиб вдоль y = Z, поворот вокруг местной z = −Y:
        // θz_local = dw/dx → глобальный поворот вокруг Y = −θ.
        var uj = new Dof6(0, 0, W(L), 0, -Th(L), 0);

        var u = FragmentBarCutSampler.Displacement(pi, pj, 0, Dof6.Zero, uj, 0.4);

        Near(new Dof6(0, 0, W(0.8), 0, -Th(0.8), 0), u, 1e-15);
    }

    static Dof6 ToLocal(PlanarVector3 pi, PlanarVector3 pj, double rot, PlanarVector3 f, PlanarVector3 m)
    {
        var (ex, ey, ez) = BeamLocalAxisConvention.Frame(pi, pj, rot);
        return new Dof6(f.Dot(ex), f.Dot(ey), f.Dot(ez), m.Dot(ex), m.Dot(ey), m.Dot(ez));
    }

    /// <summary>
    /// Консоль L = 2 вдоль X, заделка в I, q = −1000 Н/м по Z. Сечение x = 0,5: часть к J (1,5 м) действует на часть к I
    /// силой (0, 0, −1500) и моментом (0, 1125, 0) относительно точки сечения.
    /// </summary>
    [Fact]
    public void SectionAction_CantileverUniformLoad()
    {
        PlanarVector3 pi = V(0, 0, 0), pj = V(2, 0, 0);
        var ends = new BeamEndForces(ToLocal(pi, pj, 0, V(0, 0, 2000), V(0, -2000, 0)), Dof6.Zero);
        FragmentBeamSpanLoad[] loads = [FragmentBeamSpanLoad.Distributed(0, 1, V(0, 0, -1000), V(0, 0, -1000))];

        var a = FragmentBarCutSampler.SectionAction(pi, pj, 0, ends, loads, 0.25);

        Near(new Dof6(0, 0, -1500, 0, 1125, 0), a, 1e-9);
    }

    /// <summary>Сосредоточенная сила до сечения учитывается, после — нет; трапеция интегрируется точно.</summary>
    [Fact]
    public void SectionAction_PointAndTrapezoid()
    {
        PlanarVector3 pi = V(0, 0, 0), pj = V(4, 0, 0);
        // Свободный кусок: силы узла I уравновешивают нагрузки целиком; проверяем равновесие части [0, x].
        var p = V(0, 0, -100);
        FragmentBeamSpanLoad[] loads =
        [
            FragmentBeamSpanLoad.Point(0.25, p, PlanarVector3.Zero),          // x = 1
            FragmentBeamSpanLoad.Distributed(0, 0.75, V(0, 0, 0), V(0, 0, -30)), // 0…3 м, 0 → −30
        ];
        var ends = new BeamEndForces(ToLocal(pi, pj, 0, V(0, 0, 50), V(0, 10, 0)), Dof6.Zero);

        var a = FragmentBarCutSampler.SectionAction(pi, pj, 0, ends, loads, 0.5); // x = 2

        // Часть [0, 2]: узел I (0,0,50), момент (0,10,0); точечная −100 в x = 1; трапеция на [0,2]: q(x) = −10x,
        // сила −20, момент силы относительно x=0: ∫x·q = −80/3 → центр 4/3.
        double fz = 50 - 100 - 20;
        // Моменты относительно точки x = 2 (вклад силы Fz в точке x: (x − 2)·ex × Fz·ez = −(x − 2)·Fz·ey):
        double my = 10 - (0 - 2) * 50 - (1 - 2) * -100 - (4.0 / 3 - 2) * -20;
        Near(new Dof6(0, 0, -fz, 0, -my, 0), a, 1e-9);
    }

    /// <summary>
    /// Колонна из двух КЭ 0→1,5→3 (по Z), рассечение z = 1 (низ — отброшено ниже): КЭ 1, часть к I отброшена.
    /// Рассечение ровно в узле 1,5: берётся КЭ на отброшенной стороне.
    /// </summary>
    [Fact]
    public void Locate_PicksElementAndDiscardedSide()
    {
        FragmentBarElement[] bars =
        [
            new("e1", "1", "2", V(0, 0, 0), V(0, 0, 1.5), 0),
            new("e2", "2", "3", V(0, 0, 1.5), V(0, 0, 3), 0),
        ];

        var mid = FragmentBarCutSampler.Locate(V(0, 0, 1), discardedBelow: true, bars, 1e-3)!;
        Assert.Equal("e1", mid.ElemTag);
        Assert.Equal(2.0 / 3, mid.Xi, 12);
        Assert.False(mid.DiscardedTowardJ);

        var atNodeTop = FragmentBarCutSampler.Locate(V(0, 0, 1.5), discardedBelow: false, bars, 1e-3)!;
        Assert.Equal("e2", atNodeTop.ElemTag);
        Assert.True(atNodeTop.DiscardedTowardJ);

        var atNodeBottom = FragmentBarCutSampler.Locate(V(0, 0, 1.5), discardedBelow: true, bars, 1e-3)!;
        Assert.Equal("e1", atNodeBottom.ElemTag);
        Assert.False(atNodeBottom.DiscardedTowardJ);

        Assert.Null(FragmentBarCutSampler.Locate(V(1, 0, 1), true, bars, 1e-3));
    }

    /// <summary>
    /// Колонна под сжатием N = −1000 (сила по −Z на верх). Рассечение снизу z = 1: отброшенная нижняя часть давит на
    /// фрагмент вверх (+1000 по Z). Рассечение сверху z = 2: отброшенная верхняя часть давит вниз (−1000).
    /// </summary>
    [Fact]
    public void Sample_SignByDiscardedSide()
    {
        var key = FragmentMaskKey.OfLoadCase(1);
        PlanarVector3 pi = V(0, 0, 0), pj = V(0, 0, 3);
        var t = new FragmentParentResultTable();
        t.AddKey(key);
        // Сила узла I на КЭ: опора толкает вверх (0,0,+1000); узла J: нагрузка сверху (0,0,−1000).
        t.SetBeamEndForces(key, "e", new BeamEndForces(ToLocal(pi, pj, 0, V(0, 0, 1000), PlanarVector3.Zero),
            ToLocal(pi, pj, 0, V(0, 0, -1000), PlanarVector3.Zero)));
        t.SetDisplacement(key, "1", Dof6.Zero);
        t.SetDisplacement(key, "2", new Dof6(0, 0, -3e-3, 0, 0, 0));
        FragmentBarElement[] bars = [new("e", "1", "2", pi, pj, 0)];

        var low = FragmentBarCutSampler.Sample(FragmentBarCutSampler.Locate(V(0, 0, 1), true, bars, 1e-3)!, t, key, out _);
        var high = FragmentBarCutSampler.Sample(FragmentBarCutSampler.Locate(V(0, 0, 2), false, bars, 1e-3)!, t, key, out _);

        Near(new Dof6(0, 0, 1000, 0, 0, 0), low.F!, 1e-9);
        Near(new Dof6(0, 0, -1000, 0, 0, 0), high.F!, 1e-9);
        Assert.Equal(-1e-3, low.U!.Z, 15);
        Assert.Equal(-2e-3, high.U!.Z, 15);
        Assert.Equal(0, low.S);
    }
}
