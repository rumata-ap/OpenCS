using OpenCS.OpenSees.Structural;
using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Проверки полной и экстремальной таблиц глобальных перемещений и поворотов.</summary>
public class FemDisplacementTableBuilderTests
{
    [Fact]
    public void AllNodes_ContainsAllSixConvertedDisplayedValues()
    {
        var value = new FemNodeDisplacement(7, 0.001, 0.002, 0.003, 0.01, 0.02, 0.03);

        var rows = FemDisplacementTableBuilder.Build(
            [value],
            new Dictionary<string, IReadOnlyList<int>>(),
            FemDisplacementDisplayMode.AllNodes,
            FemLengthUnit.Millimeters,
            FemRotationScale.OneHundred);

        var row = Assert.Single(rows);
        Assert.Null(row.MemberTag);
        Assert.Equal(1.0, row.Ux, 12);
        Assert.Equal(2.0, row.Uy, 12);
        Assert.Equal(3.0, row.Uz, 12);
        Assert.Equal(1.0, row.Rx, 12);
        Assert.Equal(2.0, row.Ry, 12);
        Assert.Equal(3.0, row.Rz, 12);
    }

    [Fact]
    public void Extremes_ExcludesResultWithoutMemberAndDuplicatesSharedNodePerMember()
    {
        var values = new[]
        {
            new FemNodeDisplacement(1, 1, 0, 0, 0, 0, 0),
            new FemNodeDisplacement(2, 2, 0, 0, 0, 0, 0),
            new FemNodeDisplacement(3, 3, 0, 0, 0, 0, 0),
            new FemNodeDisplacement(99, 999, 0, 0, 0, 0, 0)
        };
        var members = new Dictionary<string, IReadOnlyList<int>>
        {
            ["A"] = [1, 2],
            ["B"] = [2, 3]
        };

        var rows = FemDisplacementTableBuilder.Build(
            values, members, FemDisplacementDisplayMode.ExtremesOnly,
            FemLengthUnit.Meters, FemRotationScale.One);

        Assert.DoesNotContain(rows, row => row.NodeTag == 99);
        Assert.Contains(rows, row => row.MemberTag == "A" && row.NodeTag == 2);
        Assert.Contains(rows, row => row.MemberTag == "B" && row.NodeTag == 2);
        Assert.Equal(4, rows.Count);
    }

    [Fact]
    public void Extremes_SelectsOnRawValuesAndSmallestTagWhenMinEqualsMax()
    {
        var values = new[]
        {
            new FemNodeDisplacement(10, 0.001, 0, 0, 0.2, 0, 0),
            new FemNodeDisplacement(2, 0.001, 0, 0, 0.2, 0, 0),
            new FemNodeDisplacement(5, 0.0005, 0, 0, 0.1, 0, 0)
        };
        var members = new Dictionary<string, IReadOnlyList<int>> { ["M"] = [10, 2] };

        var rows = FemDisplacementTableBuilder.Build(
            values, members, FemDisplacementDisplayMode.ExtremesOnly,
            FemLengthUnit.Millimeters, FemRotationScale.OneThousand);

        var row = Assert.Single(rows);
        Assert.Equal(2, row.NodeTag);
        Assert.Equal(1.0, row.Ux, 12);
        Assert.Equal(200.0, row.Rx, 12);
        Assert.Contains(FemNodalComponent.Ux, row.ExtremeComponents);
        Assert.Contains(FemNodalComponent.Rx, row.ExtremeComponents);
    }
}
