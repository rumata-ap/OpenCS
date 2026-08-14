using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Проверки единиц отображения и независимого состояния масштабов результатов FEM.</summary>
public class FemResultDisplayTests
{
    [Theory]
    [InlineData(FemLengthUnit.Millimeters, 1000.0)]
    [InlineData(FemLengthUnit.Centimeters, 100.0)]
    [InlineData(FemLengthUnit.Meters, 1.0)]
    public void ConvertLength_UsesMetersAsSourceUnit(FemLengthUnit unit, double expected)
    {
        Assert.Equal(expected, FemResultDisplayConverter.ConvertLength(1.0, unit), 12);
    }

    [Theory]
    [InlineData(FemRotationScale.One, 0.25)]
    [InlineData(FemRotationScale.OneHundred, 25.0)]
    [InlineData(FemRotationScale.OneThousand, 250.0)]
    public void ConvertRotation_AppliesConfiguredDisplayScale(FemRotationScale scale, double expected)
    {
        Assert.Equal(expected, FemResultDisplayConverter.ConvertRotation(0.25, scale), 12);
    }

    [Fact]
    public void SuggestForceScale_UsesOnlyValuesOfOneComponent()
    {
        double scale = FemForceScaleCalculator.Suggest(10.0, [1000.0, -2000.0]);

        Assert.Equal(0.5, scale, 12);
    }

    [Fact]
    public void ManualForceScale_IsNotReplacedByAutomaticRefresh()
    {
        var state = new FemForceScaleState();
        state.SetManual(FemForceComponent.N, 7.0);

        state.RefreshAutomatic((FemForceComponent.N, 0.5), (FemForceComponent.Mz, 2.0));

        Assert.Equal(7.0, state.Get(FemForceComponent.N, () => 1.0), 12);
        Assert.Equal(2.0, state.Get(FemForceComponent.Mz, () => 1.0), 12);
        Assert.True(state.IsManual(FemForceComponent.N));
        Assert.False(state.IsManual(FemForceComponent.Mz));
    }

    [Fact]
    public void ResetForceScale_ClearsManualOverride()
    {
        var state = new FemForceScaleState();
        state.SetManual(FemForceComponent.N, 7.0);

        state.Reset(FemForceComponent.N, () => 0.25);

        Assert.Equal(0.25, state.Get(FemForceComponent.N, () => 1.0), 12);
        Assert.False(state.IsManual(FemForceComponent.N));
    }

    [Fact]
    public void BuildNodeResultLabels_UsesOneDisplayedRowPerNode()
    {
        var rows = new[]
        {
            new FemNodeDisplacementRow("M2", 2, 2, 0, 0, 0, 0, 0, []),
            new FemNodeDisplacementRow("M1", 2, 1, 0, 0, 0, 0, 0, []),
            new FemNodeDisplacementRow("M1", 1, 3, 0, 0, 0, 0, 0, [])
        };

        var labels = FemNodeResultLabelDataBuilder.Build(
            rows,
            FemNodalComponent.Ux,
            FemDisplacementDisplayMode.AllNodes);

        Assert.Equal(new[] { 1, 2 }, labels.Select(label => label.NodeTag));
        Assert.Equal("M1", labels[1].Row.MemberTag);
        Assert.Equal(FemNodalComponent.Ux, labels[1].Component);
        Assert.Equal(1.0, labels[1].Value, 12);
    }

    [Fact]
    public void BuildNodeResultLabels_ReturnsEmptyForEmptyTable()
    {
        Assert.Empty(FemNodeResultLabelDataBuilder.Build(
            [],
            FemNodalComponent.Ux,
            FemDisplacementDisplayMode.AllNodes));
    }

    [Fact]
    public void BuildNodeResultLabels_ExtremesModeKeepsOnlySelectedComponentExtremes()
    {
        var rows = new[]
        {
            new FemNodeDisplacementRow("M1", 1, 3, 0, 0, 0, 0, 0, [FemNodalComponent.Ux]),
            new FemNodeDisplacementRow("M1", 2, 2, 0, 0, 0, 0, 0, [FemNodalComponent.Uy]),
            new FemNodeDisplacementRow("M2", 2, 1, 0, 0, 0, 0, 0, [FemNodalComponent.Ux])
        };

        var labels = FemNodeResultLabelDataBuilder.Build(
            rows,
            FemNodalComponent.Ux,
            FemDisplacementDisplayMode.ExtremesOnly);

        Assert.Equal(new[] { 1, 2 }, labels.Select(label => label.NodeTag));
        Assert.Equal(new[] { 3.0, 1.0 }, labels.Select(label => label.Value));
    }
}
