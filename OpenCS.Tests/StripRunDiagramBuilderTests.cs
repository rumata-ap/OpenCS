using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Ряды эпюр нелинейного прогона полосы: координаты, значения, знак и пустые случаи.</summary>
public class StripRunDiagramBuilderTests
{
    static readonly double[] Fractions = [0.0, 0.5, 1.0];

    // Однопролётная балка с нагрузкой вниз: момент My < 0 (растянута нижняя грань), прогиб вниз.
    static readonly double[][] Resultants = [[0, 0, 0], [5, -12.5, 1], [0, 0, 0]];
    static readonly double[] Deflections = [0.0, -0.004, 0.0];

    [Fact]
    public void My_UsesStationsAndLength()
    {
        var series = StripRunDiagramBuilder.Build(Fractions, 4.0, Resultants, Deflections, StripRunDiagramQuantity.My);

        Assert.Equal(2, series.Segments.Count);
        Assert.Equal(0.0, series.Segments[0].S0, 12);
        Assert.Equal(2.0, series.Segments[0].S1, 12);
        Assert.Equal(-12.5, series.Segments[0].Value1, 12);
        Assert.Equal(2.0, series.Segments[1].S0, 12);
        Assert.Equal(4.0, series.Segments[1].S1, 12);
    }

    [Fact]
    public void My_TensionAtBottom_IsPlottedDownward()
    {
        // Нормаль вверх: отрицательный My — растянут низ — ордината вниз (отрицательная).
        var up = StripRunDiagramBuilder.Build(Fractions, 4.0, Resultants, Deflections, StripRunDiagramQuantity.My);
        Assert.Equal(-12.5, StripRunDiagramBuilder.Peak(up)!.Value, 12);

        // Нормаль вниз: тот же результат — растянута грань +Z, то есть низ проекта, — снова вниз.
        var down = StripRunDiagramBuilder.Build(
            Fractions, 4.0, [[0, 0, 0], [5, 12.5, 1], [0, 0, 0]], Deflections,
            StripRunDiagramQuantity.My, flip: true);
        Assert.Equal(-12.5, StripRunDiagramBuilder.Peak(down)!.Value, 12);
    }

    [Fact]
    public void W_IsInMillimetres_AndFollowsDirection()
    {
        var series = StripRunDiagramBuilder.Build(Fractions, 4.0, Resultants, Deflections, StripRunDiagramQuantity.W);
        Assert.Equal(-4.0, series.Segments[0].Value1, 12);

        var flipped = StripRunDiagramBuilder.Build(
            Fractions, 4.0, Resultants, [0.0, 0.004, 0.0], StripRunDiagramQuantity.W, flip: true);
        Assert.Equal(-4.0, flipped.Segments[0].Value1, 12);
    }

    [Fact]
    public void N_AndMz_AreNotFlipped()
    {
        var n = StripRunDiagramBuilder.Build(Fractions, 4.0, Resultants, Deflections, StripRunDiagramQuantity.N, flip: true);
        Assert.Equal(5.0, n.Segments[0].Value1, 12);
        var mz = StripRunDiagramBuilder.Build(Fractions, 4.0, Resultants, Deflections, StripRunDiagramQuantity.Mz, flip: true);
        Assert.Equal(1.0, mz.Segments[0].Value1, 12);
    }

    [Fact]
    public void MissingOrMismatchedData_GivesEmptySeries()
    {
        Assert.Empty(StripRunDiagramBuilder.Build(Fractions, 4.0, null, null, StripRunDiagramQuantity.My).Segments);
        Assert.Empty(StripRunDiagramBuilder.Build(
            Fractions, 4.0, [[0, 0, 0]], Deflections, StripRunDiagramQuantity.My).Segments);
        Assert.Empty(StripRunDiagramBuilder.Build(
            Fractions, 0.0, Resultants, Deflections, StripRunDiagramQuantity.My).Segments);
        Assert.Empty(StripRunDiagramBuilder.Build(
            [0.0], 4.0, [[0, 0, 0]], [0.0], StripRunDiagramQuantity.My).Segments);
    }
}
