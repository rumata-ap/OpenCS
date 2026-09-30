using System;
using System.Linq;
using Xunit;
using CScore;

namespace CScore.Tests;

// Вуд-Армер, прочность, растягивающая мембранная сила и грань, которую момент сжимает.
//
// СП 63.13330.2018, п. 8.1.54: плита без разделения на слои проверяется по условиям
//     (Mx,ult − Mx)·(My,ult − My) − Mxy² ≥ 0   (8.100),   Mx,ult ≥ Mx, My,ult ≥ My,
// а при действии продольной силы предельные моменты берутся с её учётом (п. 8.1.57). Расчётные
// моменты Вуда Mx* = Mx + k·|Mxy|, My* = My + |Mxy|/k — частный выбор в (8.100); условие
// достаточно выполнить при одном k > 0.
//
// Когда Mx + |Mxy| < 0 (момент сжимает грань), правило Вуда берёт k = |Mx/Mxy| и Mx* = 0. Без
// продольной силы это точно: арматура грани не нужна. При растягивающей N полоса с M* = 0
// считалась как центрально растянутая и отдавала половину N арматуре этой грани, хотя момент
// уводит равнодействующую к другой грани. Теперь для такой грани проверяется и пара k = 1 без
// обнуления (момент остаётся со знаком), берётся пара с меньшим η.
//
// Пример — КЭ 4333 плиты схемы 1-lin (ЛИРА-САПР): h = 200, привязка 40 мм, B25, A500;
// по X верх 11,5 см²/м, низ 2,6 см²/м (подбор ЛИРЫ), по Y 2,6 см²/м у обеих граней.
// Nx = +363, Nxy = 22, Mx = +37,3 (растянут верх).
public class ShellSimplWoodArmerTensionTests
{
    const double H = 0.2, Cover = 0.04;
    const string WaUls = "shell_simpl_wa_uls";

    static MaterialChars ConcreteChars(CalcType ct) => new(ct)
    {
        Type = MatType.Concrete, E = 30_000_000.0, Fc = -14_500.0, Ft = 1_050.0,
    };

    static MaterialChars RebarChars(CalcType ct) => new(ct)
    {
        Type = MatType.ReSteelF, E = 200_000_000.0, Fc = -400_000.0, Ft = 435_000.0,
    };

    static Material Concrete()
    {
        var m = new Material { Id = 1, Tag = "B25", Type = MatType.Concrete, E = 30_000_000.0 };
        m.C = ConcreteChars(CalcType.C);
        return m;
    }

    static Material Rebar()
    {
        var m = new Material { Id = 2, Tag = "A500", Type = MatType.ReSteelF, E = 200_000_000.0 };
        m.C = RebarChars(CalcType.C);
        return m;
    }

    static PlateSection Section() => new()
    {
        H = H, NLayers = 40, PlateModel = "layered", ConcreteDiagramType = DiagrammType.L3,
        TensionConcrete = false,
        RebarLayers =
        [
            new PlateRebarLayer { Name = "низ", InputMode = "direct", Asx = 2.6e-4, Asy = 2.6e-4,
                                  Zsx = -(H / 2 - Cover), Zsy = -(H / 2 - Cover), DiameterX = 0.01, DiameterY = 0.01 },
            new PlateRebarLayer { Name = "верх", InputMode = "direct", Asx = 11.5e-4, Asy = 2.6e-4,
                                  Zsx = H / 2 - Cover, Zsy = H / 2 - Cover, DiameterX = 0.012, DiameterY = 0.01 },
        ],
    };

    static ShellSimplSolver.SolveResult Wa(double nx, double ny, double nxy, double mx, double my, double mxy) =>
        ShellSimplSolver.Solve(new ShellSimplSolver.SolveParams(nx, ny, nxy, mx, my, mxy, WaUls),
            Section(), Concrete(), Rebar(), CalcType.C);

    static ShellSimplStripResult Strip(ShellSimplSolver.SolveResult r, string name) =>
        r.WaStrips!.Single(s => s.Name == name);

    // По правилу Вуда полоса «x, низ» получала M* = 0 и N* = 363 + 22 = 385 → центральное
    // растяжение, на нижний ряд N*/2: η = 385·0,06/(435·10³·2,6·10⁻⁴·0,12) = 1,70.
    // С моментом, уводящим силу к верхней грани (k = 1: M = −(37,3 − 2) = −35,3), то же сечение
    // проходит: e0 = 35,3/385 = 0,092 > (h0−a')/2, x без A's = (500,25 − 385)/14500 = 7,9 мм,
    // M_ult = 115,25·(0,160 − 0,004) = 17,98, N·e = 385·(0,0917 − 0,06) = 12,2 → η = 0,68.
    [Fact]
    public void ZeroedMomentStrip_UnderTension_IsCheckedWithRelievingMoment()
    {
        var r = Wa(363.0, 0.0, 22.0, 37.3, 0.0, 2.0);
        var bottom = Strip(r, "x, низ");

        Assert.Equal(-35.3, bottom.M_des, 9);
        Assert.Equal(385.0, bottom.N_des, 9);
        Assert.Contains("сжимает грань", bottom.Case);
        Assert.InRange(bottom.Eta, 0.66, 0.70);

        // Решает полоса верхней грани с полным моментом Вуда: M = 39,3, N = 385.
        var top = Strip(r, "x, верх");
        Assert.Equal(39.3, top.M_des, 9);
        Assert.InRange(r.EtaMax!.Value, 0.88, 0.93);
        Assert.Equal(top.Eta, r.EtaMax!.Value, 9);
    }

    // Пара k = 1 — одна на грань: вместе с моментом x без обнуления в полосу y идёт
    // My + |Mxy|, а не меньший момент Вуда My + Mxy²/|Mx|.
    [Fact]
    public void RelievingPair_TakesFullTwistInOtherDirection()
    {
        var r = Wa(363.0, 0.0, 22.0, 37.3, -3.0, 2.0);   // у нижней грани My = +3

        Assert.Equal(3.0 + 2.0, Strip(r, "y, низ").M_des, 9);
        Assert.True(Strip(r, "x, низ").M_des < 0.0);
    }

    // Без растяжения правило Вуда не меняется: обнулённая полоса остаётся с M* = 0.
    [Theory]
    [InlineData(0.0)]
    [InlineData(-100.0)]
    public void WithoutTension_WoodRuleIsUnchanged(double nx)
    {
        var r = Wa(nx, 0.0, 0.0, 37.3, 0.0, 2.0);
        var bottomX = Strip(r, "x, низ");
        var bottomY = Strip(r, "y, низ");

        Assert.Equal(0.0, bottomX.M_des, 12);
        Assert.Equal(2.0 * 2.0 / 37.3, bottomY.M_des, 9);      // My + Mxy²/|Mx|
        Assert.DoesNotContain("сжимает грань", bottomX.Case);
    }

    // Пара k = 1 берётся, только если она лучше: при малом моменте, сжимающем грань, разгрузки
    // мало, а полоса y с полным |Mxy| тяжелее — остаётся правило Вуда.
    [Fact]
    public void RelievingPair_IsNotTaken_WhenWoodPairIsBetter()
    {
        // N мала, кручение велико: у нижней грани Mx + |Mxy| = −1 < 0, но полоса y по k = 1
        // получает 0 + 9 против 9²/10 = 8,1 по Вуду.
        var r = Wa(5.0, 0.0, 0.0, 10.0, 0.0, 9.0);

        Assert.Equal(0.0, Strip(r, "x, низ").M_des, 12);
        Assert.Equal(8.1, Strip(r, "y, низ").M_des, 9);
    }
}
