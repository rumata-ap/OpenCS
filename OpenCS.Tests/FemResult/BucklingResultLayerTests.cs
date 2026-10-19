using CScore.Fem;
using CSfea.CScoreBridge.Structural;
using CSfea.Core;
using OpenCS.Services;
using OpenCS.ViewModels.FemResult;
using Xunit;

namespace OpenCS.Tests.FemResult;

/// <summary>
/// Результат постановки «Расчётные длины — CSfea» в 3D-виде (спека 2026-10-18, срез 5б): шаги — формы, стержень из
/// одного КЭ — ломаной через точки дробления, величины мозаики и карточка по расчётному элементу.
/// </summary>
public sealed class BucklingResultLayerTests
{
    const double E = 2.0594e11, L = 4.0, Iz = 1e-4, P = 1e5;

    /// <summary>Шарнирная стойка из КЭ 101 (узлы 1, 2) — сводка и формы, как их пишет <see cref="FemBucklingRunner"/>.</summary>
    static (FemBucklingSummary Summary, Dictionary<int, byte[]> Records) Solve()
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
        var input = new FemSteelBucklingInput(new FemRcModelResult(m, [], []), [new FemBucklingUnit("1", ["101"])], [[101]],
            [new FemRcStage("С1", [(1, 1.0)])], []);
        var r = FemSteelBuckling.Run(input, new RcBucklingOptions(Modes: 3));
        var c = r.Cases[0];
        var summary = new FemBucklingSummary
        {
            Units = FemSteelBuckling.Summarize(input, r),
            Cases = [new FemBucklingCaseSummary { Tag = c.Tag, Lambda = c.Result.Lambda, Converged = true, Units = 1, FirstRecord = 1 }],
        };
        var records = c.Result.Modes.Select((mode, k) => (k + 1, FemBucklingModeCodec.Encode(FemBucklingRunner.Mode(input, c.Result, mode))))
            .ToDictionary(x => x.Item1, x => x.Item2);
        return (summary, records);
    }

    static FemResultLayer Layer()
    {
        var (summary, records) = Solve();
        var mesh = new FemResultMesh(
            [new FemMeshNode { NodeTag = "1" }, new FemMeshNode { NodeTag = "2", Z = L }],
            [new FemElement { ElemTag = "101", NodeIdsJson = "[1,2]", SourceMemberTag = "1" }]);
        var steps = new BucklingResultSteps(summary, n => records.GetValueOrDefault(n));
        return new FemResultLayer(new FemAnalysis { Kind = FemBucklingRunner.AnalysisKind, Tag = "Длины" }, "Длины", steps, mesh);
    }

    [Fact]
    public void Steps_AreModesOfCombinations()
    {
        var (summary, records) = Solve();
        var steps = new BucklingResultSteps(summary, n => records.GetValueOrDefault(n));
        Assert.True(steps.IsCaseList);
        Assert.Equal(summary.Cases[0].Lambda.Length, steps.StepCount);
        Assert.Equal(1.0, steps.MaxDisplacement(), 6);                    // нормировка формы — по точкам дробления
        Assert.Equal(3, steps.BarPoints(101)!.Count);
        int changed = 0;
        steps.StepChanged += () => changed++;
        steps.SelectedStepIndex = 1;
        Assert.Equal(1, changed);
        Assert.Equal(1.0, steps.MaxDisplacement(), 6);
    }

    [Fact]
    public void Layer_ShowsModeAsPolylineAndLengths()
    {
        var layer = Layer();
        Assert.True(layer.IsBuckling);
        Assert.True(layer.ShowModeList);                                    // формы — выпадающим списком нижней строки
        Assert.False(layer.ShowCaseList);                                   // селектор загружений CSfea — не для форм
        Assert.False(layer.ShowStepSlider);
        Assert.Equal(layer.StepNames.Count, layer.MaxStepIndex + 1);
        layer.SelectedStepIndex = 1;
        Assert.Equal(layer.StepNames[1], layer.StepLabel);
        layer.SelectedStepIndex = 0;
        Assert.True(layer.DeformedShown);                                   // форма — сразу деформированной схемой

        var curve = layer.BarCurve("101")!;
        Assert.Equal(5, curve.Count);                                       // I, 3 точки дробления, J
        Assert.Equal(L / 2, curve[2].Z, 6);
        Assert.Equal(layer.DeformScale, Math.Abs(curve[2].X), 6);           // середина полуволны — 1 × масштаб
        Assert.Equal(0, curve[0].X, 9);

        var unit = layer.BucklingUnit("101")!;
        Assert.Equal(1.0, layer.BucklingValue("101", FemBucklingQuantity.MuX)!.Value, 3);
        Assert.Equal(L * unit.X.Mu!.Value, layer.BucklingValue("101", FemBucklingQuantity.LefX)!.Value, 9);
        Assert.Equal(unit.Kzu(), layer.BucklingValue("101", FemBucklingQuantity.Kzu));
        Assert.Equal(0, layer.BucklingValue("101", FemBucklingQuantity.SourceX));   // общая форма
        Assert.Null(layer.BucklingValue("999", FemBucklingQuantity.MuX));

        layer.ShowDeformed = false;
        Assert.Null(layer.BarCurve("101"));
    }

    [Fact]
    public void Card_ShowsLengthsAndDerivationOfUnit()
    {
        var layer = Layer();
        layer.Selection.PickElement("101", FemResultPickMode.Elements, additive: false);
        var card = layer.Card!;
        Assert.Equal("FemBucklingCardTitle", card.Title);
        Assert.Contains(card.Rows, r => r.Label == "μx");
        Assert.Contains(card.Rows, r => r.Label == "μy");
        // Ход вычисления по обеим осям: пояснение, стойка по форме, справочная по окружению.
        Assert.Equal(6, card.Rows.Count(r => r.Label == "FemBucklingCardNotes"));
    }
}
