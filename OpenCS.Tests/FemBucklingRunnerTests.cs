using CScore.Fem;
using CSfea.CScoreBridge.Structural;
using CSfea.Core;
using OpenCS.Services;
using Xunit;

namespace OpenCS.Tests;

/// <summary>
/// Постановка «Расчётные длины — CSfea» (спека 2026-10-18, срез 5а): сводка элементов с КЗУ, форма на сетке схемы с
/// точками дробления стержней, кодек формы и отпечаток входа.
/// </summary>
public sealed class FemBucklingRunnerTests
{
    const double E = 2.0594e11, L = 4.0, Iz = 1e-4, P = 1e5;

    /// <summary>Шарнирная стойка 4 м из одного КЭ (101), сжатая силой P; прогиб по X — вокруг местной z (Iz &lt; Iy).</summary>
    static FemSteelBucklingInput Column(double factor = 1.0)
    {
        var section = new RcBeamSection("own") { Elastic = new BeamSection(E, 1e-2, 2 * Iz, Iz), CrossYAlongLocalY = true };
        var m = new RcStructuralModel();
        m.Nodes.Add(new RcNode(1, 0, 0, 0));
        m.Nodes.Add(new RcNode(2, 0, 0, L));
        m.Beams.Add(new RcBeam(101, 1, 2, section, [1.0, 0.0, 0.0]));
        m.Supports.Add(new RcSupport(1, 0b001111));
        m.Supports.Add(new RcSupport(2, 0b000011));
        var lc = new RcLoadCase(1, "P");
        lc.Nodal.Add(new RcNodalLoad(2, [0.0, 0, -P, 0, 0, 0]));
        m.LoadCases.Add(lc);
        return new FemSteelBucklingInput(new FemRcModelResult(m, [], []), [new FemBucklingUnit("1", ["101"])], [[101]],
            [new FemRcStage("С1", [(1, factor)])], []);
    }

    [Fact]
    public void Summarize_KzuIsLambdaOfOwnCombination()
    {
        var input = Column();
        var r = FemSteelBuckling.Run(input, new RcBucklingOptions(Modes: 4));
        var unit = Assert.Single(FemSteelBuckling.Summarize(input, r));

        double lambda = r.Cases[0].Result.Lambda[0];
        Assert.Equal(Math.PI * Math.PI * E * Iz / (L * L * P), lambda, 2e-3 * lambda);
        Assert.Equal(-P, unit.N, 1e-6 * P);
        Assert.Equal(1.0, unit.X.Mu!.Value, 3);
        Assert.Equal("Mode", unit.X.Source);
        Assert.Equal(1, unit.X.Mode);
        Assert.Equal(lambda, unit.Kzu(unit.X)!.Value, 1e-9 * lambda);
        Assert.Equal(lambda, unit.Kzu()!.Value, 1e-9 * lambda);          // вокруг y жёстче — КЗУ элемента по x
        Assert.Equal(["101"], unit.ElemTags);

        // Сводка переживает JSON: расчётные длины для проверки — те же.
        var summary = new FemBucklingSummary { Units = [unit] };
        var back = FemBucklingSummary.Parse(summary.ToJson())!;
        var map = FemSteelBuckling.ElementLengths(back.Units);
        Assert.Equal(FemSteelBuckling.ElementLengths(input, r)["101"].Notes, map["101"].Notes);
        Assert.Equal(unit.X.Mu, map["101"].MuX);
    }

    [Fact]
    public void Mode_HasSubdivisionPointsAndUnitAmplitude()
    {
        var input = Column();
        var r = FemSteelBuckling.Run(input, new RcBucklingOptions(Modes: 4));
        var c = r.Cases[0].Result;
        var mode = FemBucklingRunner.Mode(input, c, c.Modes[0]);

        Assert.Equal([1, 2], mode.Nodes.Keys.Order());
        Assert.Equal(0, mode.Nodes[1].X, 9);                               // шарниры — без поперечных смещений
        Assert.Equal(0, mode.Nodes[2].X, 9);
        var points = mode.BarPoints[101];
        Assert.Equal(3, points.Length);                                     // 4 подэлемента — 3 промежуточные точки
        Assert.Equal(1.0, Math.Abs(points[1].X), 9);                        // полуволна: наибольшее — посередине, 1
        Assert.Equal(Math.Sqrt(0.5), Math.Abs(points[0].X), 2);

        var decoded = FemBucklingModeCodec.Decode(FemBucklingModeCodec.Encode(mode));
        Assert.Equal(mode.Nodes.Count, decoded.Nodes.Count);
        Assert.Equal(points[1].X, decoded.BarPoints[101][1].X, 6);
    }

    [Fact]
    public void Fingerprint_ChangesWithLoadsAndModes()
    {
        var options = new RcBucklingOptions(Modes: 4);
        string a = FemBucklingFingerprint.Compute(Column(), options);
        Assert.Equal(a, FemBucklingFingerprint.Compute(Column(), options));
        Assert.NotEqual(a, FemBucklingFingerprint.Compute(Column(1.5), options));
        Assert.NotEqual(a, FemBucklingFingerprint.Compute(Column(), new RcBucklingOptions(Modes: 5)));
    }
}
