using CScore.PlateStrip;
using Xunit;

namespace CScore.Tests.PlateStrip;

public sealed class EquivalentSectionKinematicsTests
{
    [Fact]
    public void Map_UsesBeamSignConventionAndActivePlateComponents()
    {
        var embedding = new StripKinematicEmbedding(2.0);

        var state = embedding.Map(new BeamStrainState(0.001, 0.002, 0.003), 0.25);

        Assert.Equal(0.001 - 0.003 * 0.25, state.Eps0x, 12);
        Assert.Equal(0.0, state.Eps0y, 12);
        Assert.Equal(0.0, state.Gamma0xy, 12);
        Assert.Equal(0.002, state.Kx, 12);
        Assert.Equal(0.0, state.Ky, 12);
        Assert.Equal(0.0, state.Kxy, 12);
    }

    [Fact]
    public void Matrix_ContainsOnlyTheThreeKinematicTerms()
    {
        var matrix = new StripKinematicEmbedding(2.0).Matrix(0.25);

        Assert.Equal(6, matrix.GetLength(0));
        Assert.Equal(3, matrix.GetLength(1));
        Assert.Equal(1.0, matrix[0, 0], 12);
        Assert.Equal(-0.25, matrix[0, 2], 12);
        Assert.Equal(1.0, matrix[3, 1], 12);

        for (int row = 0; row < matrix.GetLength(0); row++)
        for (int col = 0; col < matrix.GetLength(1); col++)
        {
            bool active = (row == 0 && (col == 0 || col == 2)) || (row == 3 && col == 1);
            if (!active) Assert.Equal(0.0, matrix[row, col], 12);
        }
    }

    [Fact]
    public void Matrix_ChangesOnlyKappaZColumnWhenWidthCoordinateChangesSign()
    {
        var embedding = new StripKinematicEmbedding(2.0);
        var plus = embedding.Matrix(0.4);
        var minus = embedding.Matrix(-0.4);

        Assert.Equal(-plus[0, 2], minus[0, 2], 12);
        Assert.Equal(plus[0, 0], minus[0, 0], 12);
        Assert.Equal(plus[3, 1], minus[3, 1], 12);
    }
}
