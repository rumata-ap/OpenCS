namespace CScore.Tests.LimitSurface;

/// <summary>Сечения для сверки поверхности: симметричное и несимметричное армирование (B25 L2, A500).</summary>
internal static class LimitSurfaceTestSections
{
    public static CrossSection Rect((double X, double Y, double D)[] bars, double h = 0.5, double b = 0.3)
    {
        var concreteMaterial = TestMaterials.Concrete("B25");
        var rebarMaterial = TestMaterials.Rebar("A500");
        double y0 = -h / 2, y1 = h / 2, x0 = -b / 2, x1 = b / 2;
        var concrete = new MaterialArea
        {
            Id = 1, Tag = "concrete", Category = AreaCategory.Region,
            Material = concreteMaterial, MaterialId = concreteMaterial.Id, DiagrammType = DiagrammType.L2,
            Hull = new Contour([x0, x1, x1, x0, x0], [y0, y0, y1, y1, y0], "outer"),
        };
        concrete.SetWKT();
        concrete.SliceXY(nx: 12, ny: 20);
        var rebar = new MaterialArea
        {
            Id = 2, Tag = "rebar", Category = AreaCategory.RebarGroup,
            Material = rebarMaterial, MaterialId = rebarMaterial.Id, DiagrammType = DiagrammType.L2,
            Fibers = bars.Select(p => Fiber.CreatePoint(p.D, p.X, p.Y)).ToList(),
        };
        var section = new CrossSection { Id = 1, Tag = "rect", Areas = [concrete, rebar] };
        section.ResolveAndBuildDiagramms(0.85, pool: null, rebarDifferentialDiagram: false);
        return section;
    }

    public static CrossSection Symmetric() =>
        Rect([(-0.1, -0.2, 0.016), (0.1, -0.2, 0.016), (-0.1, 0.2, 0.016), (0.1, 0.2, 0.016)]);

    /// <summary>Сечение, на котором 23.10 в прототипе выявлялись ошибки, скрытые симметрией.</summary>
    public static CrossSection Asymmetric() =>
        Rect([(-0.1, -0.2, 0.025), (0.0, -0.2, 0.025), (0.1, -0.2, 0.025), (-0.1, 0.2, 0.012), (0.1, 0.2, 0.012)]);
}
