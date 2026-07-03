using CScore;
using CSfea.Core;
using CSfea.CScoreBridge;

namespace CSfea.Tests;

/// <summary>Тесты моста CScore ↔ CSfea: единицы, оси, упругая жёсткость.</summary>
public static class CScoreBridgeTests
{
    public static void RunAll()
    {
        TestHarness.Section("Мост CScore: масштаб единиц N");
        RunUnitScaleForce();

        TestHarness.Section("Мост CScore: упругая жёсткость прямоугольника");
        RunElasticStiffness();

        TestHarness.Section("Мост CScore: плита — мембранный Nx");
        RunPlateMembraneNx();
    }

    static void RunUnitScaleForce()
    {
        var section = BridgeTestSections.RectSteel(0.2, 0.4, 210_000);
        var area = section.Areas[0];
        var d = area.Diagramms[CalcType.C];
        double sig = d.Sig(0.001, out _);
        TestHarness.Check("σ(ε=0.001) > 0", sig > 1.0, $"σ={sig:F1} МПа");
        var k = new Kurvature { e0 = 0.001 };
        var direct = section.Integral(k, CalcType.C);
        var resp = SectionBridgeFactory.BeamFromPrepared(section, CalcType.C);
        var f = resp.Forces(0.001, 0.0, 0.0);

        TestHarness.Check("N > 0 (CScore)", direct.N > 1e-6, $"N={direct.N:e3} кН");
        TestHarness.CheckRel("N (кН→Н)", f.N, direct.N * 1000.0, 1e-4);
    }

    static void RunElasticStiffness()
    {
        double w = 0.25, h = 0.50;
        var section = BridgeTestSections.RectSteel(w, h, 210_000);
        var gp = new GeoProps(section);
        var resp = SectionBridgeFactory.BeamFromPrepared(section, CalcType.C);
        var j = resp.Tangent(0.0, 0.0, 0.0);

        TestHarness.CheckRel("EA", j[0, 0], gp.EA * 1000.0, 0.05);
        TestHarness.CheckRel("EIy (κ_y)", j[1, 1], gp.EIx * 1000.0, 0.05);
        TestHarness.CheckRel("EIz (κ_z)", j[2, 2], gp.EIy * 1000.0, 0.05);
    }

    static void RunPlateMembraneNx()
    {
        var steel = BridgeTestSections.LinearSteel(30_000);
        var cDiag = steel.GetDiagramms(DiagrammType.L2)![CalcType.C];
        var plate = new PlateSection { H = 0.2, NLayers = 8, TensionConcrete = true };
        var mats = new PlateSectionMaterials
        {
            ConcreteDiagram = cDiag,
            RebarDiagram = cDiag,
            ConcreteE_MPa = 30_000,
        };
        var resp = SectionBridgeFactory.ShellFromPrepared(plate, mats);
        double eps = 1e-4;
        var forces = resp.Forces(new[] { eps, 0.0, 0.0 }, new double[3], new double[2]);
        // σ≈E·ε [МПа], Nx≈σ·h·1000 кН/м → ×1000 → Н/м
        double nxRef = eps * 30_000 * 0.2 * 1000.0 * 1000.0;
        TestHarness.CheckRel("Nx плиты (ε₀x)", forces.N[0], nxRef, 0.08);
    }
}

/// <summary>Вспомогательные сечения для тестов моста.</summary>
static class BridgeTestSections
{
    public static Material LinearSteel(double e_MPa)
    {
        MaterialChars Ch(CalcType ct) => new(ct)
        {
            E = e_MPa,
            Ry = 500,
            Ru = 600,
            Ft = 500,
            Fc = 35,
            Ec2 = -0.002,
            Et2 = 0.05,
            Type = MatType.ReSteelF,
        };
        var m = new Material { Id = 1, E = e_MPa, Type = MatType.ReSteelF, Tag = "test" };
        m.MaterialChars =
        [
            Ch(CalcType.C), Ch(CalcType.CL), Ch(CalcType.N), Ch(CalcType.NL)
        ];
        return m;
    }

    public static CrossSection RectSteel(double width, double height, double e_MPa)
    {
        var steel = LinearSteel(e_MPa);
        var hull = new Contour(
            new[] { 0.0, width, width, 0.0, 0.0 },
            new[] { 0.0, 0.0, height, height, 0.0 },
            "rect")
        { Type = ContourType.Hull };

        var area = new MaterialArea
        {
            Tag = "rect",
            MaterialId = 1,
            DiagrammType = DiagrammType.L2,
            Contours = [hull],
            NX = 10,
            NY = 10,
        };
        area.Hull = hull;
        area.SetMaterial(steel, DiagrammType.L2);
        // Одно эквивалентное волокно — надёжный упругий тест без триангуляции.
        area.Fibers =
        [
            new Fiber
            {
                X = width / 2.0,
                Y = height / 2.0,
                Area = width * height,
                TypeFiber = FiberType.poly,
                Tag = "equiv",
            }
        ];

        return new CrossSection { Tag = "test-rect", Areas = [area] };
    }
}
