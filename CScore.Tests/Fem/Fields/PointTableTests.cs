using CScore.Fem.Fields;
using Xunit;

namespace CScore.Tests.Fem.Fields;

public class PointTableTests
{
    [Fact]
    public void Parse_SemicolonBomDecimalComma_WithHeaderUnits()
    {
        var t = PointTable.Parse("﻿X [см];Y [см];Z;Rz [тс/см]\r\n-6013,3;150;;360,8263\r\n-99.9;-99.9;;337.9861\r\n\r\n");
        Assert.Equal(';', t.Separator);
        Assert.Equal(["X [см]", "Y [см]", "Z", "Rz [тс/см]"], t.Headers);
        Assert.Equal(2, t.Rows.Count);
        Assert.Equal(-6013.3, t.Rows[0][0]);
        Assert.Null(t.Rows[0][2]);
        Assert.Equal(360.8263, t.Rows[0][3]);
        Assert.False(t.ColumnHasNumbers(2));
        Assert.Equal(0.01, PointTable.LengthScaleFromHeader(t.Headers[0]));
        Assert.Equal(1, PointTable.LengthScaleFromHeader("X"));
        Assert.Equal(0.001, PointTable.LengthScaleFromHeader("Y (mm)"));
    }

    [Fact]
    public void Parse_TabAndComma_NoHeader()
    {
        var tab = PointTable.Parse("1\t2,5\t3\n4\t5\t6");
        Assert.Equal('\t', tab.Separator);
        Assert.Equal(["1", "2", "3"], tab.Headers);
        Assert.Equal(2.5, tab.Rows[0][1]);
        var comma = PointTable.Parse("x,y,v\n1.5,2,3");
        Assert.Equal(',', comma.Separator);
        Assert.Equal(1.5, comma.Rows[0][0]);
    }

    [Fact]
    public void Select_SkipsRowsWithEmptySelectedCells()
    {
        var t = PointTable.Parse("X;Y;Z;V\n1;2;3;10\n4;5;;20\n7;x;9;30");
        var (withZ, skippedZ) = t.Select(0, 1, 2, [3]);
        Assert.Single(withZ);
        Assert.Equal(2, skippedZ);
        var (noZ, skipped) = t.Select(0, 1, null, [3]);
        Assert.Equal(2, noZ.Count);
        Assert.Equal(1, skipped);
        Assert.Equal(new FieldVec3(4, 5, 0), noZ[1].P);
        Assert.Equal([20.0], noZ[1].Values);
    }

    [Fact]
    public void MergeDuplicates_AveragesValues()
    {
        var pts = new List<FieldPoint>
        {
            new(new(0, 0, 0), [10]), new(new(0.0005, 0, 0), [20]), new(new(1, 0, 0), [5]),
        };
        var (merged, count) = FieldPoints.MergeDuplicates(pts);
        Assert.Equal(1, count);
        Assert.Equal(2, merged.Count);
        Assert.Equal(15, merged[0].Values[0], 12);
        Assert.Equal(5, merged[1].Values[0]);
    }
}
