using CScore.History;
using Xunit;

namespace CScore.Tests.History;

public class LoadHistorySolverLoadTests
{
    static LoadHistoryOptions Opt(double tol = 1e-6, bool ten = true) => new() { Calc = CalcType.C, Ten = ten, Tol = tol, MaxIter = 100 };

    // Тесты путей с переходом через трещинообразование идут без растяжения бетона: при ten = true
    // StrainSolver на этой балке не сходится и без истории (Mx = −30…−40 — провал момента после
    // трещины у L2), а здесь проверяется ход истории, а не решатель НДС.
    static Load L(double n, double mx, double my = 0) => new() { N = n, Mx = mx, My = my };

    /// <summary>Тест 2 спеки: одна часть, один этап = StrainSolver.Solve.</summary>
    [Fact]
    public void SinglePart_SingleStage_EqualsStrainSolver()
    {
        var s = StagedTestSections.SinglePart();
        var res = new LoadHistorySolver(s, Opt(0.01)).Run(
            [new HistoryStage("1", StageKind.Load, null, L(-300, -80), Steps: 4)], compareWithoutHistory: false);
        var last = res.WithHistory.Steps[^1];
        Assert.True(last.Converged);

        var plain = new CrossSection { Areas = s.Parts[0].Areas };
        var k = new StrainSolver(plain, CalcType.C, ten: true, tol: 0.01).Solve(-300, -80, 0);
        Assert.Equal(k.e0, last.K.e0, 7);
        Assert.Equal(k.ky, last.K.ky, 6);
        Assert.Equal(4, res.WithHistory.Steps.Count);
    }

    /// <summary>
    /// Тест 1 спеки: упругая работа двух бетонных частей (малые усилия, линейные участки L2).
    /// Плоскость = K1 (часть 0 под F1) + ΔK (составное сечение под F2 − F1).
    /// </summary>
    [Fact]
    public void ElasticTwoParts_MatchesSuperposition()
    {
        var b25 = TestMaterials.Concrete("B25");
        var p0 = StagedTestSections.ConcreteRect(1, -0.15, 0.15, -0.25, 0.25, b25);
        var p1 = StagedTestSections.ConcreteRect(2, -0.15, 0.15, 0.25, 0.35, b25);
        var s = new StagedSection
        {
            Parts = [new SectionPart { Index = 0, Areas = [p0] }, new SectionPart { Index = 1, Areas = [p1] }],
        };
        s.ResolveAndBuildDiagramms(0.85, null, rebarDifferentialDiagram: false);
        p0.Diagramms[CalcType.C].Sig(-1e-6, out double e, true, true); // касательный модуль линейного участка

        var f1 = L(-200, -5);
        var f2 = L(-400, -15);
        var res = new LoadHistorySolver(s, Opt()).Run(
            [
                new HistoryStage("1", StageKind.Load, null, f1, 1),
                new HistoryStage("2", StageKind.Load, 1, f2, 1),
            ], compareWithoutHistory: false);
        Assert.All(res.WithHistory.Steps, st => Assert.True(st.Converged));

        // одноосный изгиб вокруг x: e0, ky; жёсткости прямоугольников относительно y = 0
        static (double ea, double es, double ei) Rect(double e, double b, double y0, double y1)
        {
            double a = b * (y1 - y0), yc = (y0 + y1) / 2, h = y1 - y0;
            return (e * a, e * a * yc, e * (b * h * h * h / 12 + a * yc * yc));
        }
        static (double e0, double ky) SolveK((double ea, double es, double ei) s, double n, double m)
        {
            double det = s.ea * s.ei - s.es * s.es;
            return ((n * s.ei - m * s.es) / det, (s.ea * m - s.es * n) / det);
        }
        var s0 = Rect(e, 0.3, -0.25, 0.25);
        var s1 = Rect(e, 0.3, 0.25, 0.35);
        var sAll = (s0.ea + s1.ea, s0.es + s1.es, s0.ei + s1.ei);
        var k1 = SolveK(s0, f1.N, f1.Mx);
        var dk = SolveK(sAll, f2.N - f1.N, f2.Mx - f1.Mx);

        var kEnd = res.WithHistory.Steps[^1].K;
        Assert.Equal(k1.e0 + dk.e0, kEnd.e0, 10);
        Assert.Equal(k1.ky + dk.ky, kEnd.ky, 10);
        Assert.Equal(k1.e0, res.WithHistory.ActivationPlanes[1].e0, 10);
    }

    [Fact]
    public void WithoutHistory_AllPartsFromStart()
    {
        var s = StagedTestSections.TwoPartBeam();
        var res = new LoadHistorySolver(s, Opt(0.01, ten: false)).Run(
            [
                new HistoryStage("1", StageKind.Load, null, L(0, -60), 3),
                new HistoryStage("2", StageKind.Load, 1, L(0, -120), 3),
            ], compareWithoutHistory: true);
        var withH = res.WithHistory.Steps[^1];
        var noH = res.WithoutHistory!.Steps[^1];
        Assert.Equal(2, noH.ActivePartCount);
        Assert.Equal(-120, noH.Result.Mx, 1);
        Assert.True(Math.Abs(withH.K.ky) > Math.Abs(noH.K.ky)); // с историей кривизна больше
        Assert.Equal(2, res.WithoutHistory.Steps[0].ActivePartCount); // все части с первого шага
    }

    [Fact]
    public void Unloading_IsAPlainLoadStage()
    {
        var s = StagedTestSections.TwoPartBeam();
        var res = new LoadHistorySolver(s, Opt(0.01, ten: false)).Run(
            [
                new HistoryStage("нагрузка", StageKind.Load, null, L(0, -100), 4),
                new HistoryStage("разгрузка", StageKind.Load, null, L(0, -70), 2),
                new HistoryStage("усиление", StageKind.Load, 1, L(0, -130), 4),
            ], compareWithoutHistory: false);
        Assert.Equal("ok", res.WithHistory.Status);
        Assert.Equal(10, res.WithHistory.Steps.Count);
        Assert.Equal(-70, res.WithHistory.Steps[5].Result.Mx, 1);
    }

    [Fact]
    public void NotConvergedStep_StopsHistory()
    {
        var s = StagedTestSections.SinglePart();
        var res = new LoadHistorySolver(s, Opt(0.01)).Run(
            [new HistoryStage("1", StageKind.Load, null, L(0, -5000), 5)], compareWithoutHistory: false);
        Assert.Equal("partial", res.WithHistory.Status);
        Assert.Equal("load_not_converged", res.WithHistory.StopReason);
        Assert.False(res.WithHistory.Steps[^1].Converged);
        Assert.True(res.WithHistory.Steps.Count < 5);
    }

    [Theory]
    [InlineData(new[] { 2 })]        // нет части 2
    [InlineData(new[] { 1, 1 })]     // повторное включение
    public void Validate_RejectsBadActivation(int[] parts)
    {
        var stages = parts.Select(p => new HistoryStage("x", StageKind.Load, p, L(0, -1), 1)).ToList();
        Assert.Throws<ArgumentException>(() => LoadHistorySolver.Validate(stages, partCount: 2));
    }

    [Fact]
    public void Validate_FailureOnlyLast()
    {
        var stages = new List<HistoryStage>
        {
            new("f", StageKind.Failure, null, L(0, -1), 10),
            new("l", StageKind.Load, null, L(0, -1), 1),
        };
        Assert.Throws<ArgumentException>(() => LoadHistorySolver.Validate(stages, 1));
    }
}
