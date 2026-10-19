using CScore.Fem;
using CSfea.CScoreBridge.Structural;
using CSfea.Core;
using OpenCS.Services;
using Xunit;

namespace OpenCS.Tests;

/// <summary>
/// Расчётные длины по расчёту устойчивости в осях СП 16 (<see cref="FemSteelBuckling.ElementLengths"/>): у КЭ своей схемы
/// ось x сечения — местная z (lef,x — изгиб вокруг z), у импортного — местная y.
/// </summary>
public sealed class FemSteelBucklingLengthsTests
{
    const double E = 2.0594e11;

    /// <summary>
    /// Три несвязанные стойки 4 м с заделкой внизу и связью по X вверху, Iy = 2·Iz: вокруг z (прогиб по X) μ = 0,699,
    /// вокруг y — консоль, μ = 2. 101 — сечение своей схемы, 102 — импортное, 103 растянута.
    /// </summary>
    [Fact]
    public void ElementLengths_MapLocalPlanesToSectionAxes()
    {
        const double l = 4.0, iz = 1e-4;
        var own = new RcBeamSection("own") { Elastic = new BeamSection(E, 1e-2, 2 * iz, iz), CrossYAlongLocalY = true };
        var imported = new RcBeamSection("imp") { Elastic = new BeamSection(E, 1e-2, 2 * iz, iz) };
        var m = new RcStructuralModel();
        for (int k = 0; k < 3; k++)
        {
            m.Nodes.Add(new RcNode(2 * k + 1, 5.0 * k, 0, 0));
            m.Nodes.Add(new RcNode(2 * k + 2, 5.0 * k, 0, l));
            m.Beams.Add(new RcBeam(101 + k, 2 * k + 1, 2 * k + 2, k == 0 ? own : imported, [1.0, 0.0, 0.0]));
            m.Supports.Add(new RcSupport(2 * k + 1, 0b111111));
            m.Supports.Add(new RcSupport(2 * k + 2, 0b000001));
        }
        // Прогиб по X (местная y, изгиб вокруг z) — заделка — шарнир, по Y — консоль; у 101 сила вдвое больше: λ (в долях
        // P_e,z/1000) — 101: 0,25 и 1,02; 102: 0,5 и 2,05 — все среди первых 6 форм.
        var lc = new RcLoadCase(1, "P");
        lc.Nodal.Add(new RcNodalLoad(2, [0.0, 0, -2000, 0, 0, 0]));
        lc.Nodal.Add(new RcNodalLoad(4, [0.0, 0, -1000, 0, 0, 0]));
        lc.Nodal.Add(new RcNodalLoad(6, [0.0, 0, 1000, 0, 0, 0]));
        m.LoadCases.Add(lc);

        var input = new FemSteelBucklingInput(new FemRcModelResult(m, [], []),
            [new FemBucklingUnit("1", ["101"]), new FemBucklingUnit(null, ["102"]), new FemBucklingUnit(null, ["103"])],
            [[101], [102], [103]], [new FemRcStage("С1", [(1, 1.0)])], []);
        var result = FemSteelBuckling.Run(input, new RcBucklingOptions(Modes: 6));
        var map = FemSteelBuckling.ElementLengths(input, result);

        Assert.Equal(2, map.Count);                                    // растянутая 103 — без длин
        var own101 = result.Lengths[0]!;
        Assert.Equal(own101.AboutZ.Mu, map["101"].MuX!.Value, 12);      // своя схема: x — местная z
        Assert.Equal(own101.AboutY.Mu, map["101"].MuY!.Value, 12);
        var imp102 = result.Lengths[1]!;
        Assert.Equal(imp102.AboutY.Mu, map["102"].MuX!.Value, 12);      // импорт: x — местная y
        Assert.Equal(imp102.AboutZ.Mu, map["102"].MuY!.Value, 12);
        Assert.Equal(0.699, map["101"].MuX!.Value, 2);
        Assert.Equal(2.0, map["101"].MuY!.Value, 2);
        Assert.Equal(2.0, map["102"].MuX!.Value, 2);
        Assert.Equal(0.699, map["102"].MuY!.Value, 2);
        Assert.Equal(l, map["102"].Length, 12);
        Assert.Equal("FemSteelAutoLefMode", map["101"].NoteX);          // без смещения — та же форма, не ниже λ·N
        Assert.True(own101.AboutZ.LocalBraced && own101.AboutZ.LocalNcr >= own101.AboutZ.Ncr * (1 - 1e-6));

        // Ход вычисления: стойка по форме (тот же μ) и справочная по окружению (только опоры — заделка — шарнир, консоль).
        Assert.InRange(own101.AboutZ.Equivalent!.Mu / own101.AboutZ.Mu, 1, 1.003);   // МКЭ из 4 КЭ жёстче точной стойки
        Assert.Equal(0.699, own101.AboutZ.Surround!.Mu, 3);
        Assert.Equal(2.0, own101.AboutY.Surround!.Mu, 3);
        Assert.Equal(["FemSteelAutoLefMode", "FemSteelAutoLefEquivalent", "FemSteelAutoLefSurround",
            "FemSteelAutoLefMode", "FemSteelAutoLefEquivalent", "FemSteelAutoLefSurround"], map["101"].Notes);
    }

    /// <summary>
    /// Номограммы ASCE на тех же стойках: низ — заделка (G = 1 по AISC), верх без ригелей (G = ∞). По общей форме:
    /// прогиб по X (верх держит связь) — без смещения, K(1; ∞) = 0,875; по Y — консоль со смещением, K(1; ∞) по ур. (2.9).
    /// Вручную по осям СП 16 (x — без смещения, y — со смещением): у своей схемы x — изгиб вокруг z, у импортной — вокруг y.
    /// </summary>
    [Fact]
    public void Nomogram_AsceByModeAndManualAxes()
    {
        const double l = 4.0, iz = 1e-4;
        var own = new RcBeamSection("own") { Elastic = new BeamSection(E, 1e-2, 2 * iz, iz), CrossYAlongLocalY = true };
        var imported = new RcBeamSection("imp") { Elastic = new BeamSection(E, 1e-2, 2 * iz, iz) };
        var m = new RcStructuralModel();
        for (int k = 0; k < 2; k++)
        {
            m.Nodes.Add(new RcNode(2 * k + 1, 5.0 * k, 0, 0));
            m.Nodes.Add(new RcNode(2 * k + 2, 5.0 * k, 0, l));
            m.Beams.Add(new RcBeam(101 + k, 2 * k + 1, 2 * k + 2, k == 0 ? own : imported, [1.0, 0.0, 0.0]));
            m.Supports.Add(new RcSupport(2 * k + 1, 0b111111));
            m.Supports.Add(new RcSupport(2 * k + 2, 0b000001));
        }
        var lc = new RcLoadCase(1, "P");
        lc.Nodal.Add(new RcNodalLoad(2, [0.0, 0, -2000, 0, 0, 0]));
        lc.Nodal.Add(new RcNodalLoad(4, [0.0, 0, -1000, 0, 0, 0]));
        m.LoadCases.Add(lc);
        var input = new FemSteelBucklingInput(new FemRcModelResult(m, [], []),
            [new FemBucklingUnit("1", ["101"]), new FemBucklingUnit(null, ["102"])],
            [[101], [102]], [new FemRcStage("С1", [(1, 1.0)])], []);
        double sway = RcAlignmentChart.Sway(1, double.PositiveInfinity), braced = RcAlignmentChart.Braced(1, double.PositiveInfinity);

        var byMode = FemSteelBuckling.Run(input, new RcBucklingOptions(Modes: 6), method: new OpenCS.Tasks.FemBucklingParams { Method = "asce" });
        foreach (var x in byMode.Lengths)
        {
            Assert.Equal(RcBucklingLengthSource.Nomogram, x!.AboutZ.Source);
            Assert.False(x.AboutZ.Nomogram!.Sway);
            Assert.Equal(braced, x.AboutZ.Mu, 9);
            Assert.True(x.AboutY.Nomogram!.Sway);
            Assert.Equal(sway, x.AboutY.Mu, 9);
            Assert.Equal(RcBucklingLengthSource.Mode, x.AboutY.Form!.Source);
        }
        var map = FemSteelBuckling.ElementLengths(input, byMode);
        Assert.Equal(braced, map["101"].MuX!.Value, 9);
        Assert.Equal("FemSteelAutoLefNomogramAsce", map["101"].NoteX[..27]);
        Assert.Contains("FemSteelAutoLefNomogramEq213", map["101"].DetailsX!);
        Assert.Contains("FemSteelAutoLefNomogramEq29", map["101"].DetailsY!);

        var manual = FemSteelBuckling.Run(input, new RcBucklingOptions(Modes: 6),
            method: new OpenCS.Tasks.FemBucklingParams { Method = "asce", Sway = "manual", SwayX = false, SwayY = true });
        Assert.False(manual.Lengths[0]!.AboutZ.Nomogram!.Sway);        // своя: x — вокруг z
        Assert.True(manual.Lengths[0]!.AboutY.Nomogram!.Sway);
        Assert.False(manual.Lengths[1]!.AboutY.Nomogram!.Sway);        // импорт: x — вокруг y
        Assert.True(manual.Lengths[1]!.AboutZ.Nomogram!.Sway);
    }

    /// <summary>
    /// Шесть консолей 4 м с шарнирами вверху, верх — шарнирные тяги; С1 — P на все, С2 — 1,2P на две крайние. По
    /// умолчанию — одна задача по огибающей: крайняя μx = 1,886 (в С1 сама по себе — 2), определяющее сочетание в сводке —
    /// С2. По определяющим сочетаниям (п. 10.3.2) — две задачи, у крайней μx = 1,176: незагруженные в С2 колонны её
    /// раскрепляют — для С1 не в запас (CSfea.Tests RcBucklingLengthsTests.RunEnvelope — вывод значений).
    /// </summary>
    [Fact]
    public void Run_EnvelopeByDefault_GoverningOnRequest()
    {
        const int My = 1 << 4, Mz = 1 << 5;
        var col = new RcBeamSection("col") { Elastic = new BeamSection(E, 1e-2, 1e-4, 1e-4), CrossYAlongLocalY = true };
        var link = new RcBeamSection("link") { Elastic = new BeamSection(E, 1e-1, 1e-3, 1e-3) };
        var m = new RcStructuralModel();
        for (int i = 0; i < 6; i++)
        {
            m.Nodes.Add(new RcNode(10 + i, 5.0 * i, 0, 0));
            m.Nodes.Add(new RcNode(20 + i, 5.0 * i, 0, 4));
            m.Beams.Add(new RcBeam(100 + i, 10 + i, 20 + i, col, [1.0, 0.0, 0.0], 0, My | Mz));
            m.Supports.Add(new RcSupport(10 + i, 0b111111));
            m.Supports.Add(new RcSupport(20 + i, 0b111010));
            if (i > 0) m.Beams.Add(new RcBeam(200 + i, 19 + i, 20 + i, link, [0.0, 1.0, 0.0], My | Mz, My | Mz));
        }
        var all = new RcLoadCase(1, "все");
        for (int i = 0; i < 6; i++) all.Nodal.Add(new RcNodalLoad(20 + i, [0.0, 0, -1000, 0, 0, 0]));
        var two = new RcLoadCase(2, "две");
        for (int i = 0; i < 2; i++) two.Nodal.Add(new RcNodalLoad(20 + i, [0.0, 0, -1200, 0, 0, 0]));
        m.LoadCases.Add(all);
        m.LoadCases.Add(two);
        var input = new FemSteelBucklingInput(new FemRcModelResult(m, [], []),
            Enumerable.Range(0, 6).Select(i => new FemBucklingUnit(null, [$"{100 + i}"])).ToList(),
            Enumerable.Range(0, 6).Select(i => (IReadOnlyList<int>)[100 + i]).ToList(),
            [new FemRcStage("С1", [(1, 1.0)]), new FemRcStage("С2", [(2, 1.0)])], []);
        var options = new RcBucklingOptions(Modes: 4);

        var envelope = FemSteelBuckling.Run(input, options);
        var c = Assert.Single(envelope.Cases);
        Assert.True(c.Envelope);
        Assert.Equal(6, c.Units.Count);
        Assert.Same(c, envelope.CaseOf(0));
        Assert.Equal(1.886, FemSteelBuckling.ElementLengths(input, envelope)["100"].MuX!.Value, 0.01);
        var summary = FemSteelBuckling.Summarize(input, envelope);
        Assert.Equal("С2", summary[0].Combination);
        Assert.Equal("С1", summary[3].Combination);
        Assert.Equal(-1200, summary[0].N, 6);

        var governing = FemSteelBuckling.Run(input, options,
            method: new OpenCS.Tasks.FemBucklingParams { Forces = OpenCS.Tasks.FemBucklingParams.ForcesGoverning });
        Assert.Equal(2, governing.Cases.Count);
        Assert.DoesNotContain(governing.Cases, x => x.Envelope);
        Assert.Equal(1.176, FemSteelBuckling.ElementLengths(input, governing)["100"].MuX!.Value, 0.01);
    }
}
