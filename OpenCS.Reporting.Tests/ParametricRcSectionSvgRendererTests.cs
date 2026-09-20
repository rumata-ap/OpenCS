using System.Globalization;
using System.Xml.Linq;
using CScore.ParametricRc;
using OpenCS.Reporting;
using Xunit;

namespace OpenCS.Reporting.Tests;

/// <summary>Проверяет portable-схему параметрического ЖБ-сечения.</summary>
public sealed class ParametricRcSectionSvgRendererTests
{
    [Fact]
    public void Rectangle_renders_dimensions_rebar_axes_and_calculation_annotations()
    {
        var definition = ParametricRcSectionDefinition.Rectangle(0.30, 0.60) with
        {
            LowerRebar = ParametricLongitudinalLayer.Physical(3, 0.016, -0.25),
            UpperRebar = ParametricLongitudinalLayer.Physical(2, 0.012, 0.25),
            StirrupCuts =
            [new(ParametricStirrupZone.Body, ParametricStirrupDirection.Vertical,
                2, 0.008, 0.15, 0.03, 2)]
        };

        string svg = new ParametricRcSectionSvgRenderer().Render(definition,
            new ReportSectionDiagramOptions
            {
                Axis = "Mx",
                TensionSide = ReportTensionSide.Positive,
                A = 0.05,
                APrime = 0.05,
                H0 = 0.55
            });

        Assert.Contains("<svg", svg);
        Assert.Contains("300", svg);
        Assert.Contains("600", svg);
        Assert.Contains("a =", svg);
        Assert.Contains("a′ =", svg);
        Assert.Contains("h₀ =", svg);
        Assert.Contains("+Y", svg);
        Assert.Contains("stirrup-cut", svg);
        Assert.Equal(3 + 2, Count(svg, "class=\"rebar\""));
    }

    [Fact]
    public void Annulus_renders_inner_diameter_and_not_applicable_tension_side()
    {
        string svg = new ParametricRcSectionSvgRenderer().Render(
            ParametricRcSectionDefinition.Annulus(0.60, 0.30),
            new ReportSectionDiagramOptions { TensionSide = ReportTensionSide.NotApplicable });

        Assert.Contains("300", svg);
        Assert.Contains("сторона растяжения: не применяется", svg);
        Assert.Contains("class=\"inner-contour\"", svg);
    }

    [Fact]
    public void GeometryNumbers_areInvariantCultureAndDoNotUseLowPrecisionScientificNotation()
    {
        var definition = ParametricRcSectionDefinition.Rectangle(0.30, 0.60) with
        {
            LowerRebar = ParametricLongitudinalLayer.Physical(3, 0.016, -0.25),
            UpperRebar = ParametricLongitudinalLayer.Physical(2, 0.012, 0.25)
        };

        var root = XDocument.Parse(new ParametricRcSectionSvgRenderer().Render(definition));
        string[] numericAttributes = ["x", "y", "x1", "y1", "x2", "y2", "cx", "cy", "r", "width", "height"];
        foreach (var attribute in root.Descendants().Attributes()
                     .Where(attribute => numericAttributes.Contains(attribute.Name.LocalName) &&
                                         !attribute.Value.EndsWith('%')))
            Assert.True(double.TryParse(attribute.Value, NumberStyles.Float,
                CultureInfo.InvariantCulture, out _),
                $"SVG attribute is not invariant numeric: {attribute.Name}={attribute.Value}");
    }

    static int Count(string text, string value)
        => text.Split(value, StringSplitOptions.None).Length - 1;
}
