using CScore.PlateRebar;
using CScore.PlateStrip;
using Xunit;

namespace CScore.Tests.PlateStrip;

/// <summary>Срез 8c: фибровое сечение полосы обязано давать те же усилия [N, My, Mz], что и
/// состояние-зависимая редукция PlateSection (NonlinearStripSection).</summary>
public sealed class PlateStripFiberSectionBuilderTests
{
    const double H = 0.3;
    const double Width = 2.0;

    static Material Bilinear(int id, double e, double ry, double eu)
    {
        MaterialChars Ch(CalcType ct) => new(ct)
        {
            E = e, Ry = ry, Ru = ry * 1.1, Ft = ry, Fc = -ry,
            Ec2 = -eu, Et2 = eu, Type = MatType.ReSteelF,
        };
        var m = new Material { Id = id, E = e, Type = MatType.ReSteelF, Tag = $"m{id}" };
        m.MaterialChars = [Ch(CalcType.C), Ch(CalcType.CL), Ch(CalcType.N), Ch(CalcType.NL)];
        return m;
    }

    static PlateSection Section() => new()
    {
        H = H, NLayers = 12, TensionConcrete = true, PlateModel = "layered",
        ConcreteMaterialId = 1, RebarMaterialId = 2
    };

    static PlateRebarLayer Layer(double asx, double zsx, int materialId = 0) =>
        new() { InputMode = "direct", Asx = asx, Zsx = zsx, MaterialId = materialId };

    /// <summary>Усилия фибрового сечения по диаграммам материалов: N = ΣσA, My = ΣσA·z, Mz = −ΣσA·v.</summary>
    static double[] FiberForces(CrossSection section, IReadOnlyDictionary<int, Material> materials, BeamStrainState s)
    {
        double n = 0, my = 0, mz = 0;
        foreach (var area in section.Areas)
        {
            var material = area.Material!;
            var diagram = material.GetDiagramms(area.DiagrammType)![CalcType.C];
            foreach (var f in area.Fibers)
            {
                double eps = s.Eps0 + s.KappaY * f.X - s.KappaZ * f.Y;
                double force = diagram.Sig(eps, out _) * f.Area;
                n += force;
                my += force * f.X;
                mz += -force * f.Y;
            }
        }
        return [n, my, mz];
    }

    [Theory]
    [InlineData(0.0001, 0.0005, 0.0)]      // упруго
    [InlineData(0.0004, 0.03, 0.0)]        // изгиб за изломом
    [InlineData(-0.0005, 0.02, -0.004)]    // с горизонтальным изгибом
    public void FiberForces_EqualPlateSectionForces_WithDifferentRebarAcrossWidth(
        double eps0, double kappaY, double kappaZ)
    {
        var concrete = Bilinear(1, 30_000.0, 30.0, 0.05);
        var steel = Bilinear(2, 200_000.0, 400.0, 0.05);
        var materials = new Dictionary<int, Material> { [1] = concrete, [2] = steel };

        var plateA = Section();
        plateA.RebarLayers = [Layer(0.002, -0.12), Layer(0.001, 0.12)];
        var plateB = Section();
        plateB.RebarLayers = [Layer(0.004, -0.12), Layer(0.0005, 0.12)];

        var cDiagram = concrete.GetDiagramms(DiagrammType.L2)![CalcType.C];
        var sDiagram = steel.GetDiagramms(DiagrammType.L2)![CalcType.C];
        IPlateSectionResponse[] sources =
        [
            new PlateSectionLiveResponse(plateA, cDiagram, sDiagram),
            new PlateSectionLiveResponse(plateB, cDiagram, sDiagram),
        ];
        var state = new BeamStrainState(eps0, kappaY, kappaZ);
        var expected = NonlinearStripSection.Evaluate(Width, sources, state).Q;

        var built = PlateStripFiberSectionBuilder.Build(
            Section(), [plateA.RebarLayers, plateB.RebarLayers], Width, materials, CalcType.C);

        Assert.True(built.IsCalculable);
        var actual = FiberForces(built.Section!, materials, state);
        for (int i = 0; i < 3; i++)
        {
            double scale = Math.Max(1.0, Math.Abs(expected[i]));
            Assert.True(Math.Abs(expected[i] - actual[i]) < 1e-9 * scale,
                $"компонента {i}: ожидалось {expected[i]}, получено {actual[i]}");
        }
    }

    [Fact]
    public void Geometry_LayersAreMidpoints_AndAreaSumsToWidthTimesThickness()
    {
        var concrete = Bilinear(1, 30_000.0, 30.0, 0.05);
        var built = PlateStripFiberSectionBuilder.Build(
            Section(), [[], []], Width, new Dictionary<int, Material> { [1] = concrete }, CalcType.C);

        Assert.True(built.IsCalculable);
        var area = Assert.Single(built.Section!.Areas);
        Assert.Equal(2 * 12, area.Fibers.Count);
        Assert.Equal(Width * H, area.Fibers.Sum(f => f.Area), 12);
        Assert.Equal(-H / 2 + H / 24, area.Fibers.Min(f => f.X), 12);
        Assert.Equal(H / 2 - H / 24, area.Fibers.Max(f => f.X), 12);
    }

    [Fact]
    public void RebarWithOwnMaterial_GetsSeparateArea_AndAsyIsIgnored()
    {
        var concrete = Bilinear(1, 30_000.0, 30.0, 0.05);
        var steelA = Bilinear(2, 200_000.0, 400.0, 0.05);
        var steelB = Bilinear(3, 190_000.0, 300.0, 0.05);
        var materials = new Dictionary<int, Material> { [1] = concrete, [2] = steelA, [3] = steelB };
        var layers = new List<PlateRebarLayer>
        {
            Layer(0.002, -0.12),
            Layer(0.001, 0.12, materialId: 3),
            new() { InputMode = "direct", Asx = 0.0, Asy = 0.005, Zsy = 0.1 },
        };

        var built = PlateStripFiberSectionBuilder.Build(Section(), [layers], Width, materials, CalcType.C);

        Assert.True(built.IsCalculable);
        Assert.Equal(3, built.Section!.Areas.Count);
        var totalRebar = built.Section.Areas.Skip(1).Sum(a => a.Fibers.Sum(f => f.Area));
        Assert.Equal((0.002 + 0.001) * Width, totalRebar, 12);
    }

    [Fact]
    public void Diagnostics_ReportMissingMaterialAndUnsupportedModel()
    {
        var concrete = Bilinear(1, 30_000.0, 30.0, 0.05);
        var empty = new Dictionary<int, Material>();
        var missing = PlateStripFiberSectionBuilder.Build(Section(), [[]], Width, empty, CalcType.C);
        Assert.False(missing.IsCalculable);
        Assert.Contains(missing.Diagnostics, d => d.Code == "plate_strip_fiber_concrete_not_found");

        var poisson = Section();
        poisson.PoissonUncracked = 0.2;
        var warned = PlateStripFiberSectionBuilder.Build(
            poisson, [[]], Width, new Dictionary<int, Material> { [1] = concrete }, CalcType.C);
        Assert.True(warned.IsCalculable);
        Assert.Contains(warned.Diagnostics, d => d.Code == "plate_strip_fiber_poisson_ignored" && !d.IsError);
    }
}
