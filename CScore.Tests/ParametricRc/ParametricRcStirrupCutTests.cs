using CScore.Sp63Shear;
using CScore.ParametricRc;
using Xunit;

namespace CScore.Tests.ParametricRc;

public sealed class ParametricRcStirrupCutTests
{
    [Theory]
    [InlineData(ParametricRcShape.Rectangle, ParametricStirrupZone.Body)]
    [InlineData(ParametricRcShape.Tee, ParametricStirrupZone.Flange)]
    [InlineData(ParametricRcShape.Tee, ParametricStirrupZone.Web)]
    [InlineData(ParametricRcShape.IBeam, ParametricStirrupZone.BottomFlange)]
    [InlineData(ParametricRcShape.IBeam, ParametricStirrupZone.Web)]
    [InlineData(ParametricRcShape.IBeam, ParametricStirrupZone.TopFlange)]
    public void VerticalAndHorizontalCutsStayInsideTheirExactZone(
        ParametricRcShape shape, ParametricStirrupZone zone)
    {
        var definition = shape switch
        {
            ParametricRcShape.Rectangle => ParametricRcSectionDefinition.Rectangle(0.60, 0.80),
            ParametricRcShape.Tee => ParametricRcSectionDefinition.Tee(0.60, 0.80, 0.20, 0.16),
            _ => ParametricRcSectionDefinition.IBeam(0.60, 0.80, 0.20, 0.16)
        } with
        {
            StirrupCuts =
            [
                new(zone, ParametricStirrupDirection.Vertical, 2, 0.008, 0.20, 0.03, 101),
                new(zone, ParametricStirrupDirection.Horizontal, 2, 0.008, 0.25, 0.03, 102)
            ]
        };

        var result = ParametricRcSectionGenerator.Generate(definition);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(2, result.Section.Areas.Count(a => a.Category == AreaCategory.Stirrups));
        var exactZone = result.ZoneMap[zone];
        var elements = result.Section.Areas
            .Where(a => a.Category == AreaCategory.Stirrups)
            .SelectMany(a => a.Stirrups)
            .SelectMany(g => g.Elements)
            .ToArray();
        Assert.Equal(4, elements.Length);
        Assert.All(elements, element =>
        {
            Assert.Equal(StirrupElementKind.Cut, element.Source!.Kind);
            Assert.False(string.IsNullOrWhiteSpace(element.CenterlineContour.WKT));
            Assert.All(element.CenterlineContour.X, x => Assert.InRange(x, exactZone.MinX + 0.03 - 1e-12, exactZone.MaxX - 0.03 + 1e-12));
            Assert.All(element.CenterlineContour.Y, y => Assert.InRange(y, exactZone.MinY + 0.03 - 1e-12, exactZone.MaxY - 0.03 + 1e-12));
        });

        double barArea = Math.PI * 0.008 * 0.008 / 4.0;
        double expectedAsw = 4 * barArea;
        Assert.Equal(expectedAsw,
            elements.Sum(element => StirrupResolver.BranchAreas(element).Vy + StirrupResolver.BranchAreas(element).Vx), 12);
    }

    [Fact]
    public void ZeroCountDisablesCutAndInvalidClearRectangleNamesZone()
    {
        var disabled = ParametricRcSectionGenerator.Generate(
            ParametricRcSectionDefinition.Rectangle(0.30, 0.50) with
            {
                StirrupCuts = [new(ParametricStirrupZone.Body,
                    ParametricStirrupDirection.Vertical, 0, 0, 0, 0, 0)]
            });
        Assert.Empty(disabled.Diagnostics);
        Assert.DoesNotContain(disabled.Section.Areas, a => a.Category == AreaCategory.Stirrups);

        var invalid = ParametricRcSectionGenerator.Generate(
            ParametricRcSectionDefinition.Tee(0.60, 0.80, 0.20, 0.16) with
            {
                StirrupCuts = [new(ParametricStirrupZone.Web,
                    ParametricStirrupDirection.Horizontal, 1, 0.008, 0.20, 0.11, 1)]
            });
        Assert.Contains(invalid.Diagnostics, message => message.Contains("Web") && message.Contains("Horizontal"));
    }
}
