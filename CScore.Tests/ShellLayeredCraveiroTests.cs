using System;
using Xunit;
using CScore;

namespace CScore.Tests;

// Сверка слоистой модели пластины с многослойным методом Kollegger в изложении
// Craveiro, Bittencourt, Della Bella «Design and verification of reinforced concrete
// shell elements», Rev. IBRACON Estrut. Mater. 14(3), e14305, 2021, раздел 3.
// Их схема совпадает с PlateSection "layered": слои по толщине, главные деформации
// ε₁/ε₂ в слое, снижение β(ε₁) сжатой ветви, поворот σ₁/σ₂ в x–y, Ньютон по 6
// обобщённым деформациям. β — ур. (50): 1/(0,8 − 0,34·ε₁/εc2), 0,60/0,85 ≤ β ≤ 1.
//
// Пример — элемент 1 (табл. 1, 13–16): h = 1,5 м, 10 слоев, fck = 20 МПа, γc = 1,4,
// бетон — парабола-прямоугольник NBR 6118 (0,85·β·fcd, n = 2, εc2 = 2‰, εcu = 3,5‰),
// растяжение бетона не учитывается; сталь fyk = 500 МПа, γs = 1,15, Es = 210 ГПа,
// диаграмма Прандтля. Единицы статьи — тс (пик 857,14 тс/м² = 0,6·fcd ⇒ 1 тс = 10 кН).
// У всех сжатых слоев примера ε₁ > 3,6‰, т.е. β стоит на нижней границе 0,706 —
// без неё (β = 1/(0,8 + 170·ε₁)) бетон у граней был бы на ~11% слабее.
public class ShellLayeredCraveiroTests
{
    const double Tf = 10.0;                   // кН в 1 тс (см. выше)
    const double Fcd = 20_000.0 / 1.4;        // кПа
    const double Fyd = 500_000.0 / 1.15;      // кПа
    const double Es = 210e6;                  // кПа

    /// <summary>Парабола-прямоугольник NBR 6118 для сжатия, без растяжения; σ в кПа.</summary>
    sealed class NbrConcrete : Diagramm
    {
        public NbrConcrete() { MaterialType = MatType.Concrete; }

        public override double Sig(double eps, out double E2, bool tenB = true, bool comprA = true)
        {
            const double ec2 = -0.002, fc = 0.85 * Fcd;
            E2 = 0;
            if (eps >= 0) return 0;
            if (eps <= ec2) return -fc;
            double r = 1 - eps / ec2;
            E2 = 2 * fc * r / -ec2;
            return -fc * (1 - r * r);
        }
    }

    /// <summary>Упругопластическая сталь (Прандтль), σ в кПа.</summary>
    sealed class PrandtlSteel : Diagramm
    {
        public PrandtlSteel() { MaterialType = MatType.ReSteelF; }

        public override double Sig(double eps, out double E2, bool tenB = true, bool comprA = true)
        {
            double s = Es * eps;
            if (Math.Abs(s) <= Fyd) { E2 = Es; return s; }
            E2 = 0;
            return Math.Sign(s) * Fyd;
        }
    }

    static PlateSection Element1() => new()
    {
        H = 1.5, NLayers = 10, PlateModel = "layered", TensionConcrete = false,
        SofteningModel = "vecchio_collins", SofteningEpsC2 = 0.002,
        RebarLayers =
        {
            new PlateRebarLayer { InputMode = "direct", Asx = 14.00e-4, Zsx = 0.55,   Asy = 0.0,      Zsy = 0.55 },
            new PlateRebarLayer { InputMode = "direct", Asx = 39.70e-4, Zsx = -0.477, Asy = 12.70e-4, Zsy = -0.477 },
        },
    };

    // Нагрузки элемента 1 (табл. 1), кН/м и кН·м/м.
    static readonly double[] Loads =
        [202.46 * Tf, -9.31 * Tf, 27.26 * Tf, -28.79 * Tf, -36.28 * Tf, -16.66 * Tf];

    // НДС равновесия элемента 1 (табл. 16): ‰ и ‰/м.
    static readonly ShellStrainState PaperState =
        new(3.1494e-3, 1.0386e-3, 2.4179e-3, 2.2759e-3, -1.9826e-3, -3.3593e-3);

    [Theory]
    [InlineData(0.0,    0.002,  1.0)]
    [InlineData(-0.001, 0.002,  1.0)]
    [InlineData(0.001,  0.002,  1.0)]                        // 1/(0,8+0,17) > 1 → 1
    [InlineData(0.002,  0.002,  1.0 / (0.8 + 0.34))]
    [InlineData(0.002,  0.0025, 1.0 / (0.8 + 0.34 * 0.8))]   // εc2 входит в формулу
    [InlineData(0.002,  -0.002, 1.0 / (0.8 + 0.34))]         // знак εc2 не важен
    [InlineData(0.005,  0.002,  0.6 / 0.85)]                 // нижняя граница
    public void VecchioCollinsBeta_MatchesEquation50(double eps1, double epsC2, double expected)
    {
        Assert.Equal(expected, PlateSection.VecchioCollinsBeta(eps1, epsC2), 12);
    }

    // Прямая задача: по НДС статьи внутренние усилия должны равняться нагрузкам
    // (НДС в таблице дан с 5 значащими цифрами — допуск 1% от наибольшего усилия).
    [Fact]
    public void Element1_PaperStrainState_GivesAppliedLoads()
    {
        var f = Element1().Compute(PaperState, new NbrConcrete(), new PrandtlSteel(),
            computeStiffness: false);
        double[] got = [f.Nx, f.Ny, f.Nxy, f.Mx, f.My, f.Mxy];
        double tolN = 0.01 * 2024.6, tolM = 0.01 * 362.8;
        for (int i = 0; i < 6; i++)
            Assert.True(Math.Abs(got[i] - Loads[i]) <= (i < 3 ? tolN : tolM),
                $"компонента {i}: OpenCS {got[i]:F2}, статья {Loads[i]:F2}");
    }

    // Обратная задача: решатель OpenCS по нагрузкам статьи приходит к её НДС.
    [Fact]
    public void Element1_Solver_ReproducesPaperStrainState()
    {
        var res = new ShellStrainSolver(Element1(), new NbrConcrete(), new PrandtlSteel()).Solve(Loads);
        Assert.True(res.Converged, $"нет сходимости, невязка {res.Residual:G3}");

        var st = res.StrainState;
        double[] got = [st.Eps0x, st.Eps0y, st.Gamma0xy, st.Kx, st.Ky, st.Kxy];
        double[] exp = [PaperState.Eps0x, PaperState.Eps0y, PaperState.Gamma0xy,
                        PaperState.Kx, PaperState.Ky, PaperState.Kxy];
        for (int i = 0; i < 6; i++)
            Assert.True(Math.Abs(got[i] - exp[i]) <= 0.02 * Math.Abs(exp[i]) + 2e-5,
                $"компонента {i}: OpenCS {got[i] * 1e3:F4}‰, статья {exp[i] * 1e3:F4}‰");
    }
}
