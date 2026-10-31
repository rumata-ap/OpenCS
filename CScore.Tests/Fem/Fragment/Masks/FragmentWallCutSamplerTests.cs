using CScore.Fem.Fragment.Masks;
using CScore.Planar;
using CScore.Submodel;
using Xunit;
using static CScore.Tests.Fem.Fragment.Masks.FragmentMaskFixtures;

namespace CScore.Tests.Fem.Fragment.Masks;

public sealed class FragmentWallCutSamplerTests
{
    /// <summary>Стена 2 × 2 сеткой 4 × 4; рассечение z = 0,8 по всей ширине: пересечения x = 0; 0,5; …; 2 и середины.</summary>
    [Fact]
    public void Points_AtEdgeCrossingsAndMidpoints()
    {
        var (nodes, elements) = Wall(2, 2, 4, 4);

        var g = FragmentWallCutSampler.Prepare(V(0, 0, 0.8), V(2, 0, 0.8), V(0, 1, 0), V(0, 0, -1),
            Shells(nodes, elements), 1e-3);

        Assert.Equal(new[] { 0, 0.25, 0.5, 0.75, 1, 1.25, 1.5, 1.75, 2 }, g.Points.Select(p => Math.Round(p.S, 9)));
        Assert.Equal(4, g.Intervals.Count);
        Assert.All(g.Points, p => Assert.NotEmpty(p.Containing));
        Assert.Equal(V(0, 0, -1), g.CutNormal);
        Assert.Equal(16, g.Plane.Count);
    }

    /// <summary>КЭ другой плоскости (плита z = 2) и параллельная стена со сдвигом не входят в плоскость стены.</summary>
    [Fact]
    public void Plane_ExcludesOtherPlanes()
    {
        var (nodes, elements) = Wall(2, 2, 2, 2);
        var (n2, e2) = Wall(2, 2, 2, 2, y: 0.5, tagBase: 100);
        foreach (var (k, v) in n2) nodes[k] = v;
        elements.AddRange(e2);

        var g = FragmentWallCutSampler.Prepare(V(0, 0, 0.8), V(2, 0, 0.8), V(0, 1, 0), V(0, 0, -1), Shells(nodes, elements), 1e-3);

        Assert.Equal(4, g.Plane.Count);
        Assert.All(g.Plane, e => Assert.True(int.Parse(e.Tag) < 100));
    }

    /// <summary>Линейное поле перемещений u = (a + b·z, c·x, d + e·x·0 + f·z) воспроизводится точно в точках сэмплов.</summary>
    [Fact]
    public void Displacement_LinearFieldExact()
    {
        var (nodes, elements) = Wall(2, 2, 3, 3, flip: true);
        Dof6 Field(double x, double z) => new(1e-3 + 2e-4 * z, 3e-4 * x, -1e-3 + 5e-4 * z, 1e-5, 2e-5 * x, 0);
        var g = FragmentWallCutSampler.Prepare(V(0, 0, 0.9), V(2, 0, 0.9), V(0, 1, 0), V(0, 0, 1), Shells(nodes, elements), 1e-3);

        foreach (var p in g.Points)
        {
            var u = FragmentWallCutSampler.Displacement(p, tag => Field(nodes[tag].X, nodes[tag].Z))!;
            var e = Field(p.X.X, p.X.Z);
            for (int i = 0; i < 6; i++) Assert.Equal(e[i], u[i], 12);
        }
        Assert.Equal(V(0, 0, 1), g.CutNormal);
    }

    /// <summary>Нет перемещения хотя бы одного узла КЭ → null.</summary>
    [Fact]
    public void Displacement_MissingNode_Null()
    {
        var (nodes, elements) = Wall(1, 1, 1, 1);
        var g = FragmentWallCutSampler.Prepare(V(0, 0, 0.5), V(1, 0, 0.5), V(0, 1, 0), V(0, 0, -1), Shells(nodes, elements), 1e-3);
        Assert.Null(FragmentWallCutSampler.Displacement(g.Points[1], tag => tag == "1" ? null : Dof6.Zero));
    }
}
