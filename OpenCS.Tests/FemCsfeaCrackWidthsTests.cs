using CScore;
using CSfea.CScoreBridge.Structural;
using OpenCS.Services;
using Xunit;

namespace OpenCS.Tests;

/// <summary>
/// Ширина раскрытия трещин пластин по усилиям шага секущего расчёта (<see cref="FemCsfeaCrackWidths"/>): сверка с прямым
/// вызовом упрощённой проверки по направлениям, п. 8.2.7, поворот осей выдачи в оси сечения, параллельный счёт.
/// </summary>
public class FemCsfeaCrackWidthsTests
{
    const string Sls = "shell_simpl_capri_sls";

    // Плита h = 200 мм, B25, A500; вдоль x — ⌀12 шаг 200, вдоль y — ⌀12 шаг 100 у каждой грани, центр арматуры в 30 мм от грани.
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
        m.N = ConcreteChars(CalcType.N, 18_500.0, 1_550.0);
        m.NL = ConcreteChars(CalcType.NL, 18_500.0, 1_550.0);
        return m;
    }

    static Material Rebar()
    {
        var m = new Material { Id = 2, Tag = "A500", Type = MatType.ReSteelF, E = 200_000_000.0 };
        m.N = RebarChars(CalcType.N, 500_000.0);
        m.NL = RebarChars(CalcType.NL, 500_000.0);
        return m;
    }

    static PlateSection Section()
    {
        double bar = Math.PI * 0.012 * 0.012 / 4.0;
        PlateRebarLayer Layer(string name, double z) => new()
        {
            Name = name, InputMode = "direct", Asx = bar / 0.2, Asy = bar / 0.1,
            Zsx = z, Zsy = z, DiameterX = 0.012, DiameterY = 0.012,
        };
        return new PlateSection
        {
            H = 0.2, NLayers = 40, PlateModel = "layered", ConcreteDiagramType = DiagrammType.L3, TensionConcrete = false,
            RebarLayers = [Layer("низ", -0.07), Layer("верх", 0.07)],
        };
    }

    static FemCsfeaCrackElement Element(int id = 1, double angle = 0) =>
        new(id, Section(), Concrete(), Rebar(), angle, [1, 0, 0], [0, 0, 1]);

    // Усилия в СИ: Nx, Ny, Nxy, Н/м; Mx, My, Mxy, Н·м/м; Qx, Qy. Положительный момент растягивает верх.
    static double[] Forces(double mx, double my, double mxy) => [0, 0, 0, mx * 1e3, my * 1e3, mxy * 1e3, 0, 0];

    static List<ShellSimplDirectionResult> Direct(double k, double mx, double my, double mxy, double phi1) => ShellSimplSolver.Solve(
        new ShellSimplSolver.SolveParams(0, 0, 0, k * mx, k * my, k * mxy, Sls, Phi1: phi1), Section(), Concrete(), Rebar(), CalcType.N).CapriDirs!;

    [Fact]
    public void AllLongTerm_IsLongWidthOfDirectSolve()
    {
        var w = FemCsfeaCrackWidths.Compute(Element(), Forces(30, 12, 6), 1.0);
        var dirs = Direct(1, 30, 12, 6, 1.4);
        double top = dirs.Where(d => d.Top && !d.Strip.NoRebar).Max(d => d.Strip.Acrc_mm);

        Assert.True(top > 0.01, $"ожидалась трещина у верхней грани, a = {top}");
        Assert.Equal(top, w.LongTop, 12);
        Assert.Equal(w.LongTop, w.ShortTop, 12);
        Assert.Equal(0, w.LongBottom);
        Assert.Equal(0, w.ShortBottom);
    }

    [Fact]
    public void PartLongTerm_FollowsClause827()
    {
        const double share = 0.6;
        var w = FemCsfeaCrackWidths.Compute(Element(), Forces(30, 12, 6), share);
        var (full10, long10, long14) = (Direct(1, 30, 12, 6, 1.0), Direct(share, 30, 12, 6, 1.0), Direct(share, 30, 12, 6, 1.4));
        var top = Enumerable.Range(0, full10.Count).Where(i => full10[i].Top && !full10[i].Strip.NoRebar).ToList();
        double aLong = top.Max(i => long14[i].Strip.Acrc_mm);
        int worst = top.MaxBy(i => full10[i].Strip.Acrc_mm - long10[i].Strip.Acrc_mm + long14[i].Strip.Acrc_mm);
        double aShort = full10[worst].Strip.Acrc_mm - long10[worst].Strip.Acrc_mm + long14[worst].Strip.Acrc_mm;

        Assert.Equal(aLong, w.LongTop, 12);
        Assert.Equal(aShort, w.ShortTop, 12);
        Assert.True(w.ShortTop > w.LongTop, $"a_crc = {w.ShortTop} должно быть больше a_crc,1 = {w.LongTop}");
        // Линия трещины перпендикулярна направлению наибольшего раскрытия.
        double line = full10[worst].Alpha_deg + 90;
        while (line > 90) line -= 180;
        Assert.Equal(line, w.AngleTop, 9);
    }

    [Fact]
    public void NegativeMoment_CracksBottomFace()
    {
        var w = FemCsfeaCrackWidths.Compute(Element(), Forces(-30, -12, 0), 1.0);
        Assert.True(w.LongBottom > 0.01);
        Assert.Equal(0, w.LongTop);
    }

    /// <summary>Ось x выдачи повёрнута на 90° в осях сечения: усилия выдачи (Mx, My, Mxy) — это (My, Mx, −Mxy) сечения.</summary>
    [Fact]
    public void ForceAngle_RotatesForcesToSectionAxes()
    {
        var rotated = FemCsfeaCrackWidths.Compute(Element(angle: 90), Forces(30, 12, 6), 0.8);
        var section = FemCsfeaCrackWidths.Compute(Element(), Forces(12, 30, -6), 0.8);
        var unrotated = FemCsfeaCrackWidths.Compute(Element(), Forces(30, 12, 6), 0.8);

        Assert.Equal(section.ShortTop, rotated.ShortTop, 9);
        Assert.Equal(section.LongTop, rotated.LongTop, 9);
        Assert.Equal(section.AngleTop, rotated.AngleTop, 6);
        // Армирование по x и y разное — без поворота результат другой.
        Assert.True(Math.Abs(unrotated.ShortTop - rotated.ShortTop) > 1e-3);
    }

    [Fact]
    public void Batch_MatchesSingleElementAndSkipsMissing()
    {
        const int n = 200;
        var elements = Enumerable.Range(1, n).Select(i => Element(i, i % 3 == 0 ? 30 : 0)).ToList();
        elements.Add(Element(9999));   // нет в полях шага
        var forces = new double[n * RcSecantStepFields.ShellForceComponents];
        for (int i = 0; i < n; i++)
            Forces(10 + 0.2 * i, 25 - 0.1 * i, i % 7 - 3).CopyTo(forces, i * RcSecantStepFields.ShellForceComponents);
        var fields = new RcSecantStepFields
        {
            NodeIds = [], Displacements = [], ShellIds = Enumerable.Range(1, n).ToArray(), ShellForces = forces,
            ShellStates = new double[n * RcSecantStepFields.ShellStateComponents], ShellFlags = new byte[n],
            BeamIds = [], BeamForces = [], BeamFlags = [],
        };

        var batch = FemCsfeaCrackWidths.Compute(elements, fields, 0.9);

        Assert.Equal(n, batch.Count);
        Assert.False(batch.ContainsKey(9999));
        for (int i = 0; i < n; i++)
        {
            var single = FemCsfeaCrackWidths.Compute(elements[i],
                forces.AsSpan(i * RcSecantStepFields.ShellForceComponents, RcSecantStepFields.ShellForceComponents), 0.9);
            Assert.Equal(single, batch[i + 1]);
        }
    }
}
