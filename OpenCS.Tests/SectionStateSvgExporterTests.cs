using CScore;
using OpenCS.Services;
using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Проверяет, что автономная карта НДС содержит координатную шкалу и подписи осей.</summary>
public sealed class SectionStateSvgExporterTests
{
    [Fact]
    public void Render_EmitsCoordinateAxesAndNumericTicks()
    {
        var plot = new SectionPlotVM(new CrossSection(), new Kurvature(),
            CalcType.C, SectionPlotMode.Stress);

        string svg = new SectionStateSvgExporter().Render(plot, "Карта напряжений σ");

        Assert.Contains("data-axis=\"x\"", svg);
        Assert.Contains("data-axis=\"y\"", svg);
        Assert.Contains("x, мм", svg);
        Assert.Contains("y, мм", svg);
        Assert.Contains("data-tick-label", svg);
        Assert.Contains(">0</text>", svg);
    }
}
