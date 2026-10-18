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
}
