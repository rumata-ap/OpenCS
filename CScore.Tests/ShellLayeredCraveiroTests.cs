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

    // Бетон в характеристиках OpenCS: вершина диаграммы Rb = 0,85·fcd (как у NBR 6118),
    // модуль Eci = 5600·√fck = 25 043 МПа (NBR 6118, п. 8.2.8), εb0 = 2‰, εb2 = 3,5‰.
    const double Rb = 0.85 * Fcd;             // кПа
    const double Eb = 25_043_000.0;           // кПа

    internal static Material ConcreteMaterial()
    {
        MaterialChars Ch(CalcType ct) => new(ct)
        {
            Type = MatType.Concrete, E = Eb, Fc = -Rb, Ft = 1_000.0,
            Ec0 = -0.002, Ec1 = -0.6 * Rb / Eb, Ec2 = -0.0035, Ec1Red = -0.0015,
            Et0 = 0.0001, Et1 = 0.6 * 1_000.0 / Eb, Et2 = 0.00015, Et1Red = 0.00008,
        };
        var m = new Material { Id = 1, Tag = "fck20", Type = MatType.Concrete, E = Eb };
        m.C = Ch(CalcType.C); m.CL = Ch(CalcType.CL); m.N = Ch(CalcType.N); m.NL = Ch(CalcType.NL);
        return m;
    }

    internal static Diagramm OpenCsConcrete(DiagrammType type)
        => ConcreteMaterial().GetDiagramms(type)![CalcType.C];

    internal static Material RebarMaterial()
    {
        MaterialChars Ch(CalcType ct) => new(ct)
        {
            Type = MatType.ReSteelF, E = Es, Fc = -Fyd, Ft = Fyd, Ec2 = -0.025, Et2 = 0.025,
        };
        var m = new Material { Id = 2, Tag = "fyk500", Type = MatType.ReSteelF, E = Es };
        m.C = Ch(CalcType.C); m.CL = Ch(CalcType.CL); m.N = Ch(CalcType.N); m.NL = Ch(CalcType.NL);
        return m;
    }

    internal static Diagramm OpenCsSteel() => RebarMaterial().GetDiagramms(DiagrammType.L2)![CalcType.C];

    internal static Diagramm PaperConcrete() => new NbrConcrete();
    internal static Diagramm PaperSteel() => new PrandtlSteel();

    internal static PlateSection Element1() => new()
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
    internal static readonly double[] Loads =
        [202.46 * Tf, -9.31 * Tf, 27.26 * Tf, -28.79 * Tf, -36.28 * Tf, -16.66 * Tf];

    // НДС равновесия элемента 1 (табл. 16): ‰ и ‰/м.
    internal static readonly ShellStrainState PaperState =
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

    // Те же нагрузки со штатными диаграммами бетона OpenCS (Rb = 0,85·fcd, Eb = 25 043 МПа)
    // и L2 арматуры. Бетон сжат слабо (|ε₂| < 1‰), где диаграммы сильно различаются по
    // начальной жёсткости, поэтому деформации расходятся со статьёй в разы (κx: L3 0,53,
    // ЕКБ 0,95, L2 2,96 против 2,28 ‰/м). Но арматура x у обеих граней течёт, и разложение
    // усилий между бетоном и арматурой почти не зависит от диаграммы — оно и сверяется
    // с табл. 15 статьи, вместе с поворотом главных осей у граней (табл. 14).
    [Theory]
    [InlineData(DiagrammType.L2)]
    [InlineData(DiagrammType.L3)]
    [InlineData(DiagrammType.EKB)]
    public void Element1_OpenCsDiagrams_MatchPaperForceSplit(DiagrammType type)
    {
        var sec = Element1();
        var res = new ShellStrainSolver(sec, OpenCsConcrete(type), OpenCsSteel()).Solve(Loads);
        Assert.True(res.Converged, $"{type}: нет сходимости, невязка {res.Residual:G3}");

        var f = res.Forces;
        double[] got = [f.Nx, f.Ny, f.Nxy, f.Mx, f.My, f.Mxy];
        for (int i = 0; i < 6; i++)
            Assert.True(Math.Abs(got[i] - Loads[i]) <= (i < 3 ? 0.005 * 2024.6 : 0.005 * 362.8),
                $"{type}, равновесие, компонента {i}: {got[i]:F2} против {Loads[i]:F2}");

        // Табл. 15: ΣNsx = 232,93, ΣNsy = 52,92, ΣMsx = −48,59, ΣMsy = −25,24 тс(·м)/м.
        (string, double, double)[] rebar =
        [
            ("Nsx", f.NxRebar, 232.93 * Tf), ("Nsy", f.NyRebar, 52.92 * Tf),
            ("Msx", f.MxRebar, -48.59 * Tf), ("Msy", f.MyRebar, -25.24 * Tf),
        ];
        foreach (var (name, v, exp) in rebar)
            Assert.True(Math.Abs(v - exp) <= 0.025 * Math.Abs(exp),
                $"{type}, {name}: OpenCS {v:F1}, статья {exp:F1}");

        // Табл. 14: θ в крайних слоях 0,86° (z = +0,675) и 49,63° (z = −0,675).
        var st = res.StrainState;
        foreach (var (z, exp) in new[] { (0.675, 0.8639), (-0.675, 49.6282) })
        {
            PlateSection.PrincipalStrains2D(st.EpsX(z), st.EpsY(z), st.GammaXY(z),
                out _, out _, out double theta);
            double deg = theta * 180.0 / Math.PI;
            Assert.True(Math.Abs(deg - exp) <= 2.5, $"{type}, θ(z={z}): {deg:F2}°, статья {exp:F2}°");
        }

        // Предельные деформации (п. 8.1.30 СП 63): бетон не дальше εb2, арматура не дальше εs,ult.
        foreach (double z in new[] { 0.75, -0.75 })
        {
            PlateSection.PrincipalStrains2D(st.EpsX(z), st.EpsY(z), st.GammaXY(z),
                out _, out double e2, out _);
            Assert.True(e2 > -0.0035, $"{type}: ε2 на грани z={z} = {e2 * 1e3:F3}‰");
        }
        Assert.True(st.EpsX(0.55) < 0.025, $"{type}: εs верхней арматуры x = {st.EpsX(0.55) * 1e3:F2}‰");
    }
}
