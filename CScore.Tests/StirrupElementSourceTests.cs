using CScore;
using System.Text.Json;
using Xunit;

namespace CScore.Tests;

/// <summary>Параметры построения элемента поперечного армирования.</summary>
public sealed class StirrupElementSourceTests
{
    [Fact]
    public void Source_RoundTripsThroughJson()
    {
        var source = new StirrupElementSource
        {
            Kind = StirrupElementKind.Cut,
            AnchorAreaId = 3,
            OffsetM = 0.03,
            Direction = StirrupCutDirection.Vertical,
            Position = 0.05
        };

        var json = JsonSerializer.Serialize(source);
        var back = JsonSerializer.Deserialize<StirrupElementSource>(json)!;

        Assert.Contains("\"Cut\"", json);
        Assert.Equal(1, back.Version);
        Assert.Equal(StirrupElementKind.Cut, back.Kind);
        Assert.Equal(3, back.AnchorAreaId);
        Assert.Equal(0.03, back.OffsetM!.Value, 12);
        Assert.Equal(StirrupCutDirection.Vertical, back.Direction);
        Assert.Equal(0.05, back.Position!.Value, 12);
    }

    [Fact]
    public void NewElement_HasNullSource_MeaningManual()
    {
        var element = new StirrupElement();

        Assert.Null(element.Source);
    }

    [Fact]
    public void Clone_DeepCopiesSource()
    {
        var element = new StirrupElement
        {
            CenterlineContour = Contour.Polyline([0.0, 0.0], [-0.2, 0.2], "срез"),
            BarAreaM2 = 0.0000503,
            BarDiameterM = 0.008,
            Source = new StirrupElementSource
            {
                Kind = StirrupElementKind.Cut,
                Position = 0.05,
                EdgeOffsets = [0.03, 0.04]
            }
        };

        var clone = element.Clone(preserveId: true);
        clone.Source!.Position = 0.09;
        clone.Source.EdgeOffsets![0] = 0.99;

        Assert.Equal(0.05, element.Source!.Position!.Value, 12);
        Assert.Equal(0.03, element.Source.EdgeOffsets![0], 12);
    }
}
