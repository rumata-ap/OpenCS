using System.Linq;
using CScore.PlateStrip;
using Xunit;

namespace CScore.Tests.PlateStrip;

/// <summary>Срез 7, Task 3: решётка источников по элементам производной балки.</summary>
public sealed class StripSectionSourceGridTests
{
    [Fact]
    public void Uniform_GivesIdenticalRowsAndCorrectShape()
    {
        var source = Source();
        var grid = StripSectionSourceGrid.Uniform([source, source], elementCount: 4);

        Assert.Equal(4, grid.ElementCount);
        Assert.Equal(2, grid.WidthPointCount);
        Assert.Empty(grid.Validate());
        for (int e = 0; e < grid.ElementCount; e++)
            Assert.Same(source, grid[e][0]);
    }

    [Fact]
    public void Indexer_AddressesElementThenWidthPoint()
    {
        var stiff = Source(a00: 2000.0);
        var soft = Source(a00: 200.0);
        var grid = new StripSectionSourceGrid([[stiff, soft], [soft, stiff]]);

        Assert.Same(stiff, grid[0][0]);
        Assert.Same(soft, grid[0][1]);
        Assert.Same(soft, grid[1][0]);
        Assert.Same(stiff, grid[1][1]);
    }

    [Fact]
    public void RaggedGrid_IsRejected()
    {
        var source = Source();
        var grid = new StripSectionSourceGrid([[source, source], [source]]);

        Assert.Contains(grid.Validate(), d => d.Code == "plate_strip_source_grid_shape_mismatch");
    }

    [Fact]
    public void NullSource_IsRejected()
    {
        var source = Source();
        var grid = new StripSectionSourceGrid([[source, null!]]);

        Assert.Contains(grid.Validate(), d => d.Code == "plate_strip_source_grid_shape_mismatch");
    }

    [Fact]
    public void EmptyGrid_IsRejected()
    {
        Assert.Contains(new StripSectionSourceGrid([]).Validate(),
            d => d.Code == "plate_strip_source_grid_shape_mismatch");
        Assert.Contains(new StripSectionSourceGrid([[]]).Validate(),
            d => d.Code == "plate_strip_source_grid_shape_mismatch");
    }

    [Fact]
    public void ValidateAgainstStations_RequiresOneElementFewerThanStations()
    {
        var grid = StripSectionSourceGrid.Uniform([Source(), Source()], elementCount: 3);

        Assert.Empty(grid.ValidateAgainstStations(4));
        Assert.Contains(grid.ValidateAgainstStations(3),
            d => d.Code == "plate_strip_source_grid_shape_mismatch");
        Assert.Contains(grid.ValidateAgainstStations(6),
            d => d.Code == "plate_strip_source_grid_shape_mismatch");
    }

    [Fact]
    public void UniformSections_ProduceNoGradientWarning()
    {
        var grid = StripSectionSourceGrid.Uniform([Source(), Source()], elementCount: 4);

        Assert.Empty(grid.SectionGradientDiagnostics(2.0));
    }

    [Fact]
    public void SharplyVaryingSections_ProduceWarningNotError()
    {
        var stiff = Source(a00: 2000.0, d00: 300.0);
        var soft = Source(a00: 200.0, d00: 30.0);
        var grid = new StripSectionSourceGrid([[stiff, stiff], [soft, soft]]);

        var diagnostics = grid.SectionGradientDiagnostics(2.0);

        Assert.Contains(diagnostics, d => d.Code == "plate_strip_section_gradient_along_span");
        Assert.All(diagnostics, d => Assert.False(d.IsError));
    }

    [Fact]
    public void SingleElement_HasNoGradientToReport()
    {
        var grid = StripSectionSourceGrid.Uniform([Source(), Source()], elementCount: 1);

        Assert.Empty(grid.SectionGradientDiagnostics(2.0));
    }

    [Fact]
    public void InvalidArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => new StripSectionSourceGrid(null!));
        Assert.Throws<ArgumentNullException>(() => StripSectionSourceGrid.Uniform(null!, 2));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => StripSectionSourceGrid.Uniform([Source()], 0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => StripSectionSourceGrid.Uniform([Source(), Source()], 2).SectionGradientDiagnostics(2.0, 0.0));
    }

    static ConstantLinearPlateSectionResponse Source(double a00 = 1000.0, double d00 = 300.0)
    {
        var a = new double[3, 3];
        var b = new double[3, 3];
        var d = new double[3, 3];
        var ass = new double[2, 2];
        a[0, 0] = a00;
        d[0, 0] = d00;
        a[1, 1] = 500.0;
        d[1, 1] = 100.0;
        ass[0, 0] = ass[1, 1] = 400.0;
        return new ConstantLinearPlateSectionResponse(a, b, d, ass, $"src-{a00}-{d00}");
    }
}
