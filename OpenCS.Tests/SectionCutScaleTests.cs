using OpenCS.Views.Helpers;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Вписывание эпюры разреза по оси σ/ε: постоянная эпюра (вся линия на площадке диаграммы,
/// типично для предельных усилий) не должна схлопываться в линию на базе.</summary>
public class SectionCutScaleTests
{
    [Theory]
    [InlineData(-19.5, -19.5)]
    [InlineData(-19.5, -19.4999999)]
    [InlineData(12.0, 12.0)]
    [InlineData(-3.0, 5.0)]
    public void MaxScaleV_FillsUsableSpanFromBase(double vMin, double vMax)
    {
        const double vUsable = 700, rebarPx = 20;
        double scale = CutCanvas.MaxScaleVForRebarAndCurve(vUsable, vMin, vMax, rebarPx, rebarPx);

        double left = Math.Max(rebarPx, Math.Max(0, -vMin) * scale);
        double right = Math.Max(rebarPx, Math.Max(0, vMax) * scale);
        Assert.True(left + right <= vUsable + 1e-6);
        Assert.True(left + right > 0.95 * vUsable, $"эпюра с арматурой занимает {left + right:F1} px из {vUsable}");
    }
}
