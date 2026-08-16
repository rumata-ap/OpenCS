using System.Collections.Generic;
using CScore;
using Xunit;

namespace CScore.Tests;

public class Curvature8232ApplyPsiCorrectionTests
{
    static CrossSection SectionWithFibers(params Fiber[] fibers)
    {
        var material = new Material { Id = 1, Type = MatType.ReSteelF };
        var area = new MaterialArea
        {
            Id = 1,
            Category = AreaCategory.RebarGroup,
            Material = material,
            MaterialId = material.Id
        };
        foreach (var fiber in fibers) area.Fibers.Add(fiber);
        return new CrossSection { Id = 1, Areas = [area] };
    }

    static Fiber MakeFiber(double y, double eps, double epsP, double sig,
        double e2, double e, double area)
    {
        var fiber = new Fiber(x: 0, y: y)
        {
            TypeFiber = FiberType.point,
            Area = area,
            Eps = eps,
            Eps_p = epsP,
            Sig = sig,
            E2 = e2,
            E = e,
            N = sig * area
        };
        fiber.Mx = fiber.N * y;
        return fiber;
    }

    [Fact]
    public void ApplyPsiCorrection_TensionedFiberWithCrackMap_DividesStressAndForcesByPsi()
    {
        var fiber = MakeFiber(0.1, 0.0008, 0.0, 100_000, 80_000_000, 125_000_000, 0.0001);
        var section = SectionWithFibers(fiber);
        var epsCrc = new Dictionary<Fiber, double> { [fiber] = 0.0010 };

        var corrected = Curvature8232.ApplyPsiCorrection(
            section, new Kurvature(), new Load { N = 10.0, Mx = 1.0 }, epsCrc);

        Assert.Equal(20.0, corrected.N, 6);
        Assert.Equal(2.0, corrected.Mx, 6);
        Assert.Equal(0.0, corrected.My, 6);
        Assert.Equal(200_000.0, fiber.Sig, 3);
        Assert.Equal(160_000_000.0, fiber.E2, 1);
        Assert.Equal(250_000_000.0, fiber.E, 1);
        Assert.Equal(20.0, fiber.N, 6);
        Assert.Equal(2.0, fiber.Mx, 6);
    }

    [Fact]
    public void ApplyPsiCorrection_TensionedFiberWithoutCrackMapEntry_IsLeftUntouched()
    {
        var fiber = MakeFiber(-0.05, 0.0006, 0.0, 70_000, 60_000_000, 116_666_667, 0.00008);
        var corrected = Curvature8232.ApplyPsiCorrection(
            SectionWithFibers(fiber), new Kurvature(),
            new Load { N = 5.6, Mx = -0.28 }, new Dictionary<Fiber, double>());

        Assert.Equal(5.6, corrected.N, 6);
        Assert.Equal(-0.28, corrected.Mx, 6);
        Assert.Equal(70_000.0, fiber.Sig, 3);
    }

    [Fact]
    public void ApplyPsiCorrection_CompressedFiberWithCrackMapEntry_IsLeftUntouched()
    {
        var fiber = MakeFiber(0.12, -0.0002, 0.0, -50_000, 200_000_000, 250_000_000, 0.00009);
        var corrected = Curvature8232.ApplyPsiCorrection(
            SectionWithFibers(fiber), new Kurvature(),
            new Load { N = -4.5, Mx = -0.54 },
            new Dictionary<Fiber, double> { [fiber] = 0.0003 });

        Assert.Equal(-4.5, corrected.N, 6);
        Assert.Equal(-0.54, corrected.Mx, 6);
        Assert.Equal(-50_000.0, fiber.Sig, 3);
    }

    [Fact]
    public void ApplyPsiCorrection_UsesFullStrainIncludingEpsP()
    {
        var fiber = MakeFiber(0.08, 0.0004, 0.0004, 90_000, 70_000_000, 112_500_000, 0.0001);
        var corrected = Curvature8232.ApplyPsiCorrection(
            SectionWithFibers(fiber), new Kurvature(),
            new Load { N = 9.0, Mx = 0.72 },
            new Dictionary<Fiber, double> { [fiber] = 0.0010 });

        Assert.Equal(18.0, corrected.N, 6);
        Assert.Equal(1.44, corrected.Mx, 6);
    }

    [Fact]
    public void ApplyPsiCorrection_CombinesMultipleFibersCorrectly()
    {
        var fiber1 = MakeFiber(0.1, 0.0008, 0.0, 100_000, 80_000_000, 125_000_000, 0.0001);
        var fiber2 = MakeFiber(-0.05, 0.0006, 0.0, 70_000, 60_000_000, 116_666_667, 0.00008);
        var fiber3 = MakeFiber(0.12, -0.0002, 0.0, -50_000, 200_000_000, 250_000_000, 0.00009);
        var fiber4 = MakeFiber(0.08, 0.0004, 0.0004, 90_000, 70_000_000, 112_500_000, 0.0001);
        var corrected = Curvature8232.ApplyPsiCorrection(
            SectionWithFibers(fiber1, fiber2, fiber3, fiber4), new Kurvature(),
            new Load { N = 520.1, Mx = 10.9 },
            new Dictionary<Fiber, double>
            {
                [fiber1] = 0.0010,
                [fiber3] = 0.0003,
                [fiber4] = 0.0010
            });

        Assert.Equal(539.1, corrected.N, 6);
        Assert.Equal(12.62, corrected.Mx, 6);
    }
}
