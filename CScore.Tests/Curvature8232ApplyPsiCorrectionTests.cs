using System;
using System.Collections.Generic;
using System.Linq;
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
            section, new Kurvature(), new Load { N = 10.0, Mx = 1.0 }, epsCrc, CalcType.N);

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
            new Load { N = 5.6, Mx = -0.28 }, new Dictionary<Fiber, double>(), CalcType.N);

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
            new Dictionary<Fiber, double> { [fiber] = 0.0003 }, CalcType.N);

        Assert.Equal(-4.5, corrected.N, 6);
        Assert.Equal(-0.54, corrected.Mx, 6);
        Assert.Equal(-50_000.0, fiber.Sig, 3);
    }

    [Fact]
    public void ApplyPsiCorrection_CompressedPlaneWithPrestress_IsLeftUntouched()
    {
        var fiber = MakeFiber(0.08, -0.0002, 0.0004, 90_000, 70_000_000, 112_500_000, 0.0001);
        var corrected = Curvature8232.ApplyPsiCorrection(
            SectionWithFibers(fiber), new Kurvature(),
            new Load { N = 9.0, Mx = 0.72 },
            new Dictionary<Fiber, double> { [fiber] = 0.0010 }, CalcType.N,
            requireCurrentPlaneStrain: true);

        Assert.Equal(9.0, corrected.N, 6);
        Assert.Equal(0.72, corrected.Mx, 6);
        Assert.Equal(90_000.0, fiber.Sig, 3);
    }

    [Fact]
    public void ApplyPsiCorrection_TensionedPlaneWithNegativeCrackStrain_UsesPlaneStrainMagnitude()
    {
        var fiber = MakeFiber(0.08, 0.0004, 0.0045, 90_000, 70_000_000, 112_500_000, 0.0001);
        var corrected = Curvature8232.ApplyPsiCorrection(
            SectionWithFibers(fiber), new Kurvature(),
            new Load { N = 9.0, Mx = 0.72 },
            new Dictionary<Fiber, double> { [fiber] = -0.0001 }, CalcType.N,
            requireCurrentPlaneStrain: true);

        // ψs = 1 / (1 + 0.8 · |εs,crc| / εs) = 1/1.2.
        Assert.Equal(10.8, corrected.N, 6);
        Assert.Equal(0.864, corrected.Mx, 6);
        Assert.Equal(108_000.0, fiber.Sig, 3);
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
            }, CalcType.N);

        Assert.Equal(539.1, corrected.N, 6);
        Assert.Equal(12.62, corrected.Mx, 6);
    }

    [Fact]
    public void ApplyPsiCorrection_YieldedRebar_KeepsStressOnMaterialDiagram()
    {
        // Арматура за площадкой текучести: σ в трещине не может превысить Rs = 400 МПа.
        // ψs-поправка обязана браться с диаграммы при εs,crc = ε + 0,8·εcrc, а не делением σ/ψs.
        var section = TestSections.Example47();
        var k = new Kurvature { e0 = 0.0, ky = -0.05, kz = 0.0 };
        var raw = section.Integral(k, CalcType.N, ten: false, ca: true);

        var rebars = section.Areas
            .Where(a => a.Material?.Type is MatType.ReSteelF or MatType.ReSteelU)
            .SelectMany(a => a.Fibers)
            .Where(f => f.TypeFiber == FiberType.point)
            .ToList();
        var epsCrc = rebars.ToDictionary(f => f, _ => 0.00082, ReferenceEqualityComparer.Instance as IEqualityComparer<Fiber>);

        Curvature8232.ApplyPsiCorrection(section, k, raw, epsCrc, CalcType.N);

        var fiber = rebars[0];
        Assert.True(fiber.Eps + fiber.Eps_p > 0.002, "фикстура должна быть за текучестью");
        Assert.True(fiber.Sig <= 400_000.0 + 1.0,
            $"σ в трещине = {fiber.Sig / 1000.0:F1} МПа превышает Rs = 400 МПа");
    }
}
