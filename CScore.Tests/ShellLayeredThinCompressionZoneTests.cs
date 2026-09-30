using System;
using Xunit;
using CScore;

namespace CScore.Tests;

// Слоистая модель плиты при тонкой сжатой зоне.
//
// Бетон слоя работает по деформации его середины. У слабоармированной плиты (h = 200 мм,
// 2,6 см²/м — минимальный процент армирования) высота сжатой зоны x = Rs·As/Rb ≈ 8 мм, а при
// десяти слоях середина крайнего слоя лежит в 10 мм от грани: сжатого бетона в модели нет,
// момент воспринимает только пара «сжатая — растянутая арматура», и для момента больше
// Rsc·A's·(h0 − a') равновесия не существует — итерации расходятся. Проверка в таком случае
// повторяет поиск НДС на сечении, измельчённом до ShellLayeredCheck.RefinedLayers слоёв.
//
// Усилия — КЭ 1230 плиты схемы 1-lin (ЛИРА-САПР), подбор ЛИРЫ 2,6 см²/м во всех четырёх рядах.
public class ShellLayeredThinCompressionZoneTests
{
    const double H = 0.2, Cover = 0.04, As = 2.6e-4;

    static MaterialChars ConcreteChars(CalcType ct, double rb, double rbt) => new(ct)
    {
        Type = MatType.Concrete, E = 30_000_000.0, Fc = -rb, Ft = rbt,
        Ec0 = -0.002, Ec1 = -0.6 * rb / 30_000_000.0, Ec2 = -0.0035, Ec1Red = -0.0015,
        Et0 = 0.0001, Et1 = 0.6 * rbt / 30_000_000.0, Et2 = 0.00015, Et1Red = 0.00008,
    };

    static MaterialChars RebarChars(CalcType ct, double rs) => new(ct)
    {
        Type = MatType.ReSteelF, E = 200_000_000.0, Fc = -rs, Ft = rs, Ec2 = -0.025, Et2 = 0.025,
    };

    static Material Concrete()
    {
        var m = new Material { Id = 1, Tag = "B25", Type = MatType.Concrete, E = 30_000_000.0 };
        m.C = ConcreteChars(CalcType.C, 14_500.0, 1_050.0);
        m.CL = ConcreteChars(CalcType.CL, 14_500.0, 1_050.0);
        m.N = ConcreteChars(CalcType.N, 18_500.0, 1_550.0);
        m.NL = ConcreteChars(CalcType.NL, 18_500.0, 1_550.0);
        return m;
    }

    static Material Rebar()
    {
        var m = new Material { Id = 2, Tag = "A500", Type = MatType.ReSteelF, E = 200_000_000.0 };
        m.C = RebarChars(CalcType.C, 435_000.0);
        m.CL = RebarChars(CalcType.CL, 435_000.0);
        m.N = RebarChars(CalcType.N, 500_000.0);
        m.NL = RebarChars(CalcType.NL, 500_000.0);
        return m;
    }

    static PlateSection Section(int layers) => new()
    {
        H = H, NLayers = layers, PlateModel = "layered", ConcreteDiagramType = DiagrammType.L3,
        TensionConcrete = false,
        RebarLayers =
        [
            new PlateRebarLayer { Name = "низ", InputMode = "direct", Asx = As, Asy = As,
                                  Zsx = -(H / 2 - Cover), Zsy = -(H / 2 - Cover), DiameterX = 0.01, DiameterY = 0.01 },
            new PlateRebarLayer { Name = "верх", InputMode = "direct", Asx = As, Asy = As,
                                  Zsx = H / 2 - Cover, Zsy = H / 2 - Cover, DiameterX = 0.01, DiameterY = 0.01 },
        ],
    };

    static readonly ShellLoadItem Forces = new()
    {
        Label = "КЭ 1230", Nx = -1.4, Ny = -7.2, Nxy = 5.2, Mx = -8.81, My = -10.61, Mxy = 0.77,
    };

    static ShellLayeredCheck.Result Check(int layers) =>
        ShellLayeredCheck.CheckUls(Section(layers), Forces, Concrete(), Rebar(), CalcType.C,
            DiagrammType.L3, out _, out _, out _);

    // На десяти слоях НДС не находится ни обычным, ни робастным поиском.
    [Fact]
    public void TenLayers_PlainSolverDoesNotConverge()
    {
        var concrete = Concrete();
        var rebar = Rebar();
        var solver = new ShellStrainSolver(Section(10),
            concrete.GetDiagramms(DiagrammType.L3)![CalcType.C],
            rebar.GetDiagramms(DiagrammType.L2)![CalcType.C]);
        double[] target = [Forces.Nx, Forces.Ny, Forces.Nxy, Forces.Mx, Forces.My, Forces.Mxy];

        Assert.False(solver.Solve(target).Converged);
        Assert.False(solver.SolveRobust(target, concrete, rebar, CalcType.C).Converged);
    }

    // Проверка при этом даёт результат — на измельчённом сечении, с пометкой в описании.
    // Несущая способность полосы y: Rs·As·(h0 − x/2) = 113,1·(0,160 − 0,004) = 17,6 > 10,6 кН·м/м.
    [Fact]
    public void TenLayers_CheckFallsBackToRefinedSection()
    {
        var r = Check(10);

        Assert.True(r.Converged);
        Assert.True(r.Passed);
        Assert.Contains($"слоёв по толщине {ShellLayeredCheck.RefinedLayers}", r.Description);
    }

    // Если разбиения хватает, сечение не подменяется и результат тот же.
    [Fact]
    public void FortyLayers_NoFallback_SameUtilisation()
    {
        var coarse = Check(10);
        var fine = Check(ShellLayeredCheck.RefinedLayers);

        Assert.True(fine.Converged);
        Assert.DoesNotContain("слоёв по толщине", fine.Description);
        Assert.Equal(fine.Utilization, coarse.Utilization, 6);
    }
}
