using CScore.ParametricRc;
using Xunit;

namespace CScore.Tests;

/// <summary>
/// Центральное растяжение: сечение 1000×1000, 2×10Ø22, бетон с диаграммой L2 (плато растяжения
/// Rbt,ser до εbt2, дальше σ = 0). При трещинообразовании бетон выключается весь разом, и
/// кривая N(ε) проваливается от Ncrc до усилия одной арматуры.
/// </summary>
public sealed class CentralTensionCrackingTests
{
    const double Rbt = 1_800;      // кПа, ветвь растяжения для N и NL
    const double Es = 200_000_000; // кПа
    const double EtN = 0.00015;    // εbt2 для N
    const double EtNL = 0.00031;   // εbt2 для NL
    static readonly double As = 20 * Math.PI * 0.022 * 0.022 / 4;

    static Material Concrete()
    {
        MaterialChars Ch(CalcType ct, double e, double ft, double et1Red, double et2) => new(ct)
        {
            E = e, Fc = -22_000, Ft = ft, Ec2 = -0.0035, Ec1Red = -0.0015,
            Et1Red = et1Red, Et2 = et2, Type = MatType.Concrete
        };
        return new Material
        {
            Id = 1, Tag = "B30", Type = MatType.Concrete, E = 32_500_000,
            MaterialChars =
            [
                Ch(CalcType.C, 32_500_000, 1_100, 8e-5, EtN),
                Ch(CalcType.CL, 32_500_000, 1_100, 8e-5, EtN),
                Ch(CalcType.N, 32_500_000, Rbt, 8e-5, EtN),
                Ch(CalcType.NL, 9_848_484.85, Rbt, 0.00022, EtNL)
            ]
        };
    }

    static CrossSection Section()
    {
        var def = ParametricRcSectionDefinition.Rectangle(1.0, 1.0) with
        {
            LowerRebar = ParametricLongitudinalLayer.Physical(10, 0.022, -0.439),
            UpperRebar = ParametricLongitudinalLayer.Physical(10, 0.022, 0.439)
        };
        var section = ParametricRcSectionGenerator.Generate(def).Section;
        var concrete = Concrete();
        var steel = SectionCutFixtures.BuildSteelMaterial();
        foreach (var area in section.Areas)
            area.SetMaterial(area.Category == AreaCategory.Region ? concrete : steel, DiagrammType.L2);
        section.ResolveAndBuildDiagramms(rebarDifferentialDiagram: false);
        return section;
    }

    /// <summary>Ncrc по НДМ для плато L2: бетон на Rbt по всей площади + арматура при εbt2.</summary>
    static double NcrcByDiagram(double epsBt2) => Rbt * 1.0 + Es * As * epsBt2;

    [Theory]
    [InlineData(2_100.0)]
    [InlineData(2_266.0)]
    [InlineData(3_000.0)]
    public void StrainSolverWithCrackedRestart_FindsPlaneBeyondCracking(double n)
    {
        var section = Section();
        var solver = new StrainSolver(section, CalcType.N, ten: true, tol: 0.1, maxIter: 25,
            crackedRestart: true);

        var k = solver.Solve(n, 0, 0);

        Assert.True(solver.Converged, $"residual = {solver.Residual}");
        Assert.Equal(n / (Es * As), k.e0, 1e-6);
    }

    [Fact]
    public void StrainSolverWithCrackedRestart_KeepsUncrackedBranchBelowNcrc()
    {
        var section = Section();
        var solver = new StrainSolver(section, CalcType.N, ten: true, tol: 0.1, maxIter: 25,
            crackedRestart: true);

        var k = solver.Solve(1_962, 0, 0);

        Assert.True(solver.Converged);
        Assert.True(k.e0 < EtN, $"e0 = {k.e0}");
    }

    [Theory]
    [InlineData(1_000.0)]
    [InlineData(1_962.0)]
    [InlineData(3_000.0)]
    public void CrackingAxialForce_MatchesDiagramPlateau(double nDirection)
    {
        var section = Section();
        var solver = new CrackingSolver(section, CalcType.N,
            tensionZone: CrackingSolver.LoadedTensionZone(section, nDirection, 0, 0, nAtZeroMoment: 0));

        var res = solver.CrackingAxialForce(nDirection);

        Assert.True(res.Converged);
        Assert.Equal(NcrcByDiagram(EtN), res.N, NcrcByDiagram(EtN) * 0.01);
        Assert.Equal(0.0, res.Mx, 1e-6);
    }

    [Fact]
    public void CrackingMoment_AboveNcrc_ReportsCrackedByAxialForce()
    {
        var section = Section();
        var solver = new CrackingSolver(section, CalcType.N,
            tensionZone: CrackingSolver.LoadedTensionZone(section, 2_266, 1, 0, nAtZeroMoment: 2_266));

        var res = solver.CrackingMoment(2_266, 1, 0);

        Assert.True(res.Converged);
        Assert.Equal(0.0, res.Mx);
        Assert.NotNull(res.StrainPlane);
    }

    [Fact]
    public void CrackWidth_CentralTensionAboveNcrc_IsCracked()
    {
        var section = Section();
        var solver = new CrackWidthSolver(section, calcCrc: CalcType.NL, calcService: CalcType.N,
            acrcUltLong: 0.3, acrcUltShort: 0.3);

        var res = solver.Compute(2_400, 0, 0, 0, 0);

        Assert.True(res.Cracked);
        Assert.True(res.OnsetByAxialForce);
        Assert.Equal(NcrcByDiagram(EtNL), res.NCrc, NcrcByDiagram(EtNL) * 0.01);
        Assert.True(res.AcrcShort > 0);
    }

    [Fact]
    public void CrackWidth_CentralTensionBelowNcrc_IsNotCracked()
    {
        var section = Section();
        var solver = new CrackWidthSolver(section, calcCrc: CalcType.N, calcService: CalcType.N);

        var res = solver.Compute(1_962, 0, 0, 0, 0);

        Assert.False(res.Cracked);
    }

    [Theory]
    [InlineData(100.0, 0.0, 0.0, true)]
    [InlineData(100.0, 1e-3, 0.0, false)]
    [InlineData(-100.0, 0.0, 0.0, false)]
    [InlineData(0.0, 0.0, 0.0, false)]
    public void IsCentralTension(double n, double mx, double my, bool expected) =>
        Assert.Equal(expected, CrackingSolver.IsCentralTension(n, mx, my));
}
