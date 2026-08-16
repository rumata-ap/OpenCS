using System;
using System.Collections.Generic;
using CScore;
using Xunit;

namespace CScore.Tests;

public class TotalCurvatureSolverTests
{
    const double Height = 0.300;
    const double Width = 1.150;
    const double RebarDepth = 0.042;
    const double RebarDiameter = 0.014;
    const double RebarArea = 923e-6;

    static CrossSection BuildSection(
        DiagrammType concreteDiagram = DiagrammType.L3,
        MatType rebarType = MatType.ReSteelF)
    {
        var concreteMaterial = new Material
        {
            Id = 1,
            Tag = "B15",
            Type = MatType.Concrete,
            E = 24_000_000.0,
            MaterialChars =
            [
                ConcreteChars(CalcType.C, false),
                ConcreteChars(CalcType.CL, true),
                ConcreteChars(CalcType.N, false),
                ConcreteChars(CalcType.NL, true)
            ]
        };

        var x = new[] { -Width / 2, Width / 2, Width / 2, -Width / 2, -Width / 2 };
        var y = new[] { -Height / 2, -Height / 2, Height / 2, Height / 2, -Height / 2 };
        var concrete = new MaterialArea
        {
            Id = 1,
            Tag = "Бетон B15",
            Category = AreaCategory.Region,
            Material = concreteMaterial,
            MaterialId = concreteMaterial.Id,
            DiagrammType = concreteDiagram,
            Hull = new Contour(x, y, "hull")
        };
        concrete.SetWKT();
        concrete.SliceXY(nx: 48, ny: 24);

        var steelMaterial = new Material
        {
            Id = 2,
            Tag = "A400",
            Type = rebarType,
            E = 200_000_000.0,
            MaterialChars =
            [
                RebarChars(CalcType.C, rebarType),
                RebarChars(CalcType.CL, rebarType),
                RebarChars(CalcType.N, rebarType),
                RebarChars(CalcType.NL, rebarType)
            ]
        };

        var rebar = new MaterialArea
        {
            Id = 2,
            Tag = "6Ø14 снизу",
            Category = AreaCategory.RebarGroup,
            Material = steelMaterial,
            MaterialId = steelMaterial.Id,
            DiagrammType = rebarType == MatType.ReSteelU ? DiagrammType.L3 : DiagrammType.L2,
            HostArea = concrete,
            HostAreaId = concrete.Id
        };

        double yBar = -Height / 2 + RebarDepth;
        double barArea = RebarArea / 6.0;
        for (int i = 0; i < 6; i++)
        {
            var bar = Fiber.CreatePoint(RebarDiameter, -0.45 + i * 0.18, yBar);
            bar.Area = barArea;
            rebar.Fibers.Add(bar);
        }

        var section = new CrossSection
        {
            Id = 1,
            Tag = "Пример 47, плита фундамента",
            Areas = [concrete, rebar]
        };
        section.ResolveAndBuildDiagramms(rebarDifferentialDiagram: false);
        return section;
    }

    static MaterialChars ConcreteChars(CalcType calc, bool longTerm) => longTerm
        ? new MaterialChars
        {
            Type = MatType.Concrete,
            TypeCalc = calc,
            Fc = -11_000,
            Ft = 1_100,
            E = 9_090_909.091,
            Ec0 = -0.0034,
            Ec1 = -0.00121,
            Ec2 = -0.0048,
            Ec1Red = -0.0028,
            Et1Red = 0.00022,
            Et0 = 0.00024,
            Et1 = 0.000121,
            Et2 = 0.00031
        }
        : new MaterialChars
        {
            Type = MatType.Concrete,
            TypeCalc = calc,
            Fc = -11_000,
            Ft = 1_100,
            E = 24_000_000,
            Ec0 = -0.002,
            Ec1 = -0.000275,
            Ec2 = -0.0035,
            Ec1Red = -0.0015,
            Et1Red = 0.00008,
            Et0 = 0.0001,
            Et1 = 0.0000275,
            Et2 = 0.00015
        };

    static MaterialChars RebarChars(CalcType calc, MatType type) => new()
    {
        Type = type,
        TypeCalc = calc,
        Fc = -400_000,
        Ft = 400_000,
        E = 200_000_000,
        Ec2 = -0.0035,
        Et2 = 0.025
    };

    static CrossSection BuildSectionWithCustomConcreteDiagram()
    {
        var section = BuildSection();
        var concrete = section.Areas.Find(a => a.Material?.Type == MatType.Concrete)!;
        var material = concrete.Material!;
        var pool = new List<Diagramm>();
        material.Type = MatType.Custom;
        material.BaseType = MatType.Concrete;
        material.CustomDiagramIds.Clear();
        concrete.DiagrammType = DiagrammType.Custom;

        foreach (var calc in new[] { CalcType.C, CalcType.CL, CalcType.N, CalcType.NL })
        {
            var source = concrete.Diagramms[calc];
            var custom = new Diagramm(source.Ic, source.It, DiagrammType.Custom,
                MatType.Concrete, $"custom-{calc}")
            {
                Id = 100 + (int)calc,
                MaterialId = material.Id,
                CalcType = calc
            };
            pool.Add(custom);
            material.CustomDiagramIds[calc] = custom.Id;
        }

        section.ResolveAndBuildDiagramms(pool: pool, rebarDifferentialDiagram: false);
        return section;
    }

    [Fact]
    public void Compute_SmallMoment_UsesUncrackedFormula()
    {
        var result = new TotalCurvatureSolver(BuildSection())
            .Compute(N: 0.0, mxLong: -8.0, myLong: 0.0, mxTotal: -10.0, myTotal: 0.0);

        Assert.True(result.CrcConverged);
        Assert.False(result.Cracked);
        Assert.NotNull(result.Stage1);
        Assert.NotNull(result.Stage2);
        Assert.Null(result.Stage3);
        Assert.True(result.Stage1!.Converged);
        Assert.True(result.Stage2!.Converged);
        Assert.True(result.AllConverged);
        Assert.Equal(CalcType.N, result.Stage1.CalcType);
        Assert.Equal(CalcType.NL, result.Stage2.CalcType);
        Assert.True(result.Stage1.ConcreteTension);
        Assert.True(result.Stage2.ConcreteTension);
        Assert.Empty(result.Stage1.PsiSByRebar);
        Assert.Empty(result.Stage2.PsiSByRebar);
        Assert.Equal(result.Stage1.Plane.ky + result.Stage2.Plane.ky, result.KyFull, 10);
        Assert.True(result.KyFull < 0.0);
    }

    [Fact]
    public void Compute_LargeMoment_UsesCrackedFormulaAndPsiReducesCurvature()
    {
        var section = BuildSection();
        var solver = new TotalCurvatureSolver(section, solverTol: 0.05, solverMaxIter: 80, solverH: 1e-7);
        var result = solver.Compute(N: 0.0, mxLong: -50.0, myLong: 0.0, mxTotal: -60.0, myTotal: 0.0);

        Assert.True(result.CrcConverged);
        Assert.True(result.Cracked);
        Assert.NotNull(result.Stage1);
        Assert.NotNull(result.Stage2);
        Assert.NotNull(result.Stage3);
        Assert.True(result.Stage1!.Converged && result.Stage2!.Converged && result.Stage3!.Converged);
        Assert.True(result.AllConverged);
        Assert.Equal(CalcType.N, result.Stage1.CalcType);
        Assert.Equal(CalcType.N, result.Stage2.CalcType);
        Assert.Equal(CalcType.NL, result.Stage3.CalcType);
        Assert.False(result.Stage1.ConcreteTension);
        Assert.False(result.Stage2.ConcreteTension);
        Assert.False(result.Stage3.ConcreteTension);
        Assert.NotEmpty(result.Stage1.PsiSByRebar);
        Assert.NotEmpty(result.Stage2.PsiSByRebar);
        Assert.NotEmpty(result.Stage3.PsiSByRebar);
        Assert.All(result.Stage1.PsiSByRebar, psi =>
            Assert.True(psi.PsiS > 0.0 && psi.PsiS <= 1.0));
        Assert.True(result.KyFull < 0.0);
        Assert.Equal(0.0, result.KzFull, 6);

        var noPsiSolver = new StrainSolver(section, CalcType.N, ten: false, ca: true,
            tol: 0.05, maxIter: 80, h: 1e-7);
        var noPsiPlane = noPsiSolver.Solve(0.0, -60.0, 0.0, result.Stage1.Plane);
        Assert.True(noPsiSolver.Converged);
        Assert.True(Math.Abs(result.Stage1.Plane.ky) < Math.Abs(noPsiPlane.ky));
    }

    [Fact]
    public void Compute_ExtremeAxialForce_ReturnsNotConvergedWithoutThrowing()
    {
        var result = new TotalCurvatureSolver(BuildSection())
            .Compute(N: 1.0e9, mxLong: 0.0, myLong: 0.0, mxTotal: 0.0, myTotal: 0.0);

        Assert.False(result.CrcConverged);
        Assert.False(result.AllConverged);
    }

    [Fact]
    public void Compute_L2ConcreteDiagram_Converges()
    {
        var result = new TotalCurvatureSolver(BuildSection(DiagrammType.L2),
                solverTol: 0.05, solverMaxIter: 80, solverH: 1e-7)
            .Compute(0.0, -50.0, 0.0, -60.0, 0.0);

        Assert.True(result.AllConverged);
        Assert.True(result.KyFull < 0.0);
    }

    [Fact]
    public void Compute_CustomConcreteDiagram_Converges()
    {
        var result = new TotalCurvatureSolver(BuildSectionWithCustomConcreteDiagram(),
                solverTol: 0.05, solverMaxIter: 80, solverH: 1e-7)
            .Compute(0.0, -50.0, 0.0, -60.0, 0.0);

        Assert.True(result.CrcConverged);
        Assert.True(result.AllConverged);
        Assert.True(result.KyFull < 0.0);
    }

    [Fact]
    public void Compute_ReSteelURebar_Converges()
    {
        var result = new TotalCurvatureSolver(BuildSection(rebarType: MatType.ReSteelU),
                solverTol: 0.05, solverMaxIter: 80, solverH: 1e-7)
            .Compute(0.0, -50.0, 0.0, -60.0, 0.0);

        Assert.True(result.Cracked);
        Assert.True(result.AllConverged);
        Assert.True(result.KyFull < 0.0);
    }

    [Fact]
    public void Compute_WithAxialCompression_ConvergesAndShiftsCrackingMoment()
    {
        var zeroN = new TotalCurvatureSolver(BuildSection(),
                solverTol: 0.05, solverMaxIter: 80, solverH: 1e-7)
            .Compute(0.0, -20.0, 0.0, -25.0, 0.0);
        var compression = new TotalCurvatureSolver(BuildSection(),
                solverTol: 0.05, solverMaxIter: 80, solverH: 1e-7)
            .Compute(-500.0, -20.0, 0.0, -25.0, 0.0);

        Assert.True(zeroN.AllConverged);
        Assert.True(compression.AllConverged);
        Assert.NotEqual(zeroN.Mcrc, compression.Mcrc, 3);
    }

    [Fact]
    public void Compute_BiaxialBending_ConvergesWithNonZeroKyAndKz()
    {
        var result = new TotalCurvatureSolver(BuildSection(),
                solverTol: 0.05, solverMaxIter: 80, solverH: 1e-7)
            .Compute(0.0, -50.0, -10.0, -60.0, -12.0);

        Assert.True(result.AllConverged);
        Assert.NotEqual(0.0, result.KyFull);
        Assert.NotEqual(0.0, result.KzFull);
    }

    [Fact]
    public void Compute_WithPrestrainOnRebar_ProducesDifferentCurvature()
    {
        var plain = new TotalCurvatureSolver(BuildSection(),
                solverTol: 0.05, solverMaxIter: 80, solverH: 1e-7)
            .Compute(0.0, -50.0, 0.0, -60.0, 0.0);
        var prestressedSection = BuildSection();
        foreach (var area in prestressedSection.Areas)
            if (area.Material?.Type == MatType.ReSteelF)
                foreach (var fiber in area.Fibers)
                    fiber.Eps_p = 0.0005;
        var prestressed = new TotalCurvatureSolver(prestressedSection,
                solverTol: 0.05, solverMaxIter: 80, solverH: 1e-7)
            .Compute(0.0, -50.0, 0.0, -60.0, 0.0);

        Assert.True(plain.AllConverged);
        Assert.True(prestressed.AllConverged);
        Assert.NotEqual(plain.KyFull, prestressed.KyFull, 6);
    }

    [Fact]
    public void Compute_WithoutRebar_ReturnsNotConvergedWithoutThrowing()
    {
        var section = BuildSection();
        section.Areas.RemoveAll(a => a.Material?.Type is MatType.ReSteelF or MatType.ReSteelU);

        var result = new TotalCurvatureSolver(section,
                solverTol: 0.05, solverMaxIter: 80, solverH: 1e-7)
            .Compute(0.0, -50.0, 0.0, -60.0, 0.0);

        Assert.False(result.AllConverged);
    }
}
