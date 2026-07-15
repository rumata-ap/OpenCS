using CScore;
using CSmath;
using OpenCS.OpenSees.CScore;
using OpenCS.OpenSees.Model;

namespace OpenCS.OpenSees.Tests;

public sealed class MaterialDiagramMapperTests
{
    [Theory]
    [InlineData(DiagrammType.L2)]
    [InlineData(DiagrammType.L3)]
    [InlineData(DiagrammType.SP63)]
    public void Standard_diagram_contains_zero_critical_strains_and_deterministic_samples(DiagrammType type)
    {
        Diagramm diagram = CreateDiagram(type);

        OpenSeesMaterialDefinition result = MaterialDiagramMapper.Map(
            diagram,
            tag: 7,
            sourceId: "concrete-1",
            sourceType: MatType.Concrete);

        double[] critical = diagram.GetCriticalStrains();
        double[] strains = result.PositiveEnvelope
            .Concat(result.NegativeEnvelope)
            .Select(point => point.Strain)
            .ToArray();

        Assert.Contains(0, strains);
        foreach (double strain in critical)
            Assert.Contains(strain, strains);

        Assert.Contains(result.PositiveEnvelope, point =>
            Math.Abs(point.Strain - 0.00005) < 1e-12);
        Assert.Equal(result.PositiveEnvelope.Select(point => point.Strain).OrderBy(x => x),
            result.PositiveEnvelope.Select(point => point.Strain));
        Assert.Contains(result.Warnings, warning => warning.Contains("монотон", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Custom_diagram_retains_source_compression_and_tension_points_in_SI()
    {
        Diagramm diagram = CreateDiagram(DiagrammType.Custom);

        OpenSeesMaterialDefinition result = MaterialDiagramMapper.Map(
            diagram,
            tag: 3,
            sourceId: "custom-1",
            sourceType: MatType.Custom);

        Assert.Contains(result.NegativeEnvelope, point =>
            point.Strain == -0.002 && point.StressPa == -2_000_000);
        Assert.Contains(result.PositiveEnvelope, point =>
            point.Strain == 0.001 && point.StressPa == 1_500_000);
    }

    private static Diagramm CreateDiagram(DiagrammType type)
    {
        if (type == DiagrammType.Custom)
        {
            return new Diagramm(
                new LSpline([-0.002, 0], [-2_000, 0]),
                new LSpline([0, 0.001], [0, 1_500]),
                type,
                MatType.ReSteelF,
                type.ToString());
        }

        double[] compressionStrains = [-0.003, -0.0015, 0];
        double[] compressionStress = [-3_000, -2_000, 0];
        double[] tensionStrains = [0, 0.0001, 0.0002];
        double[] tensionStress = [0, 500, 1_000];

        return new Diagramm(
            new LSpline(compressionStrains, compressionStress),
            new LSpline(tensionStrains, tensionStress),
            type,
            MatType.Concrete,
            type.ToString())
        { };
    }
}
