using CScore.Fem;
using CScore.Fem.Fragment;
using CScore.Fem.Fragment.Masks;
using CScore.Planar;
using CScore.Submodel;
using Xunit;
using static CScore.Tests.Fem.Fragment.Masks.FragmentMaskFixtures;

namespace CScore.Tests.Fem.Fragment.Masks;

public sealed class FragmentMaskDistributionTests
{
    /// <summary>Кромка y = 0, z = 1,5: отрезки 0…1,5 и 2,5…4 (проём), s второго — 1,5…3.</summary>
    static readonly FragmentCut Cut = new("W1", FragmentCutKind.Edge, FragmentCutSide.Bottom, 1.5, null,
        [(V(0, 0, 1.5), V(1.5, 0, 1.5)), (V(2.5, 0, 1.5), V(4, 0, 1.5))]);

    static FragmentMaskCutInput Input => new(Cut, null, V(0, 1, 0));

    /// <summary>Неравномерная сетка узлов (точки сэмплов маски на узлы не попадают).</summary>
    static FragmentCutMeshNodes Nodes()
    {
        double[] xs = [0, 0.2, 0.9, 1.5, 2.5, 2.6, 3.3, 4];
        var nodes = xs.Select((x, i) => new FemMeshNode { NodeTag = $"{i + 1}", X = x, Y = 0, Z = 1.5 }).ToList();
        var r = FragmentCutMeshNodes.Find(Input, nodes, 1e-3);
        Assert.Null(r.Error);
        return r;
    }

    /// <summary>
    /// Маска: на первом отрезке сэмплы s = 0; 0,5; 1,5 (излом на 0,5), на втором — 1,5; 2,25; 3 со скачком на стыке;
    /// компоненты силы и погонного момента разные по знаку и величине.
    /// </summary>
    static List<FragmentMaskSample> Mask() =>
    [
        S(0, 10, -40, 3), S(0.5, 25, -55, 1), S(1.5, -5, -20, -2),
        S(1.5, 7, -80, 4), S(2.25, 0, -60, 0), S(3, 12, -10, 6),
    ];

    static FragmentMaskSample S(double s, double fx, double fz, double my) =>
        new(s, new Dof6(s * 1e-3, 0, -s * 2e-3, 0, s * 1e-4, 0), new Dof6(fx * 1e3, 3e3, fz * 1e3, 0.5e3, my * 1e3, -1e3));

    [Fact]
    public void EdgeNodalForces_PreserveResultantOnNonUniformMesh()
    {
        var nodes = Nodes();
        var mask = Mask();
        var forces = FragmentMaskDistribution.EdgeNodalForces(Cut, nodes, mask)!;
        Assert.NotNull(forces);

        var origin = V(1, 2, 0);
        var position = nodes.Segments.SelectMany(s => s).GroupBy(n => n.Tag).ToDictionary(g => g.Key, g => g.First().X);
        var sumF = PlanarVector3.Zero;
        var sumM = PlanarVector3.Zero;
        foreach (var (tag, f) in forces)
        {
            sumF += f.Force;
            sumM += f.Moment + (position[tag] - origin).Cross(f.Force);
        }
        var (rf, rm) = FragmentMaskBalance.Resultant(Input, mask, origin)!.Value;

        Assert.Equal(8, forces.Count);
        AssertVector(rf, sumF);
        AssertVector(rm, sumM);
        // Узлы на краях проёма получают силу только своего отрезка: ни один узел не пропал и нет «перетекания» через проём.
        Assert.True(Math.Abs(forces["4"].Z) > 0 && Math.Abs(forces["5"].Z) > 0);
    }

    /// <summary>
    /// Равномерная погонная сила q на отрезке длины L с двумя узлами — по q·L/2 в узел; на третьем узле в середине —
    /// L/4, L/2, L/4 (известные согласованные силы).
    /// </summary>
    [Fact]
    public void EdgeNodalForces_UniformLoad_KnownConsistentForces()
    {
        var cut = new FragmentCut("W", FragmentCutKind.Edge, FragmentCutSide.Top, 0, null, [(V(0, 0, 0), V(2, 0, 0))]);
        var q = new Dof6(0, 0, -10e3, 0, 0, 0);
        List<FragmentMaskSample> mask = [new(0, null, q), new(1.3, null, q), new(2, null, q)];
        var nodes = FragmentCutMeshNodes.Find(new(cut, null, null),
            [new FemMeshNode { NodeTag = "a", X = 0 }, new FemMeshNode { NodeTag = "m", X = 1 }, new FemMeshNode { NodeTag = "b", X = 2 }], 1e-3);

        var forces = FragmentMaskDistribution.EdgeNodalForces(cut, nodes, mask)!;

        Assert.Equal(-5e3, forces["a"].Z, 6);
        Assert.Equal(-10e3, forces["m"].Z, 6);
        Assert.Equal(-5e3, forces["b"].Z, 6);
    }

    [Fact]
    public void DisplacementAt_InterpolatesWithinSegment()
    {
        var parts = FragmentMaskDistribution.SplitBySegments(Cut.Segments, Mask());
        Assert.Equal([3, 3], parts.Select(p => p.Count));
        var u = FragmentMaskDistribution.DisplacementAt(parts[1], 1.875)!;
        Assert.Equal(1.875e-3, u.X, 12);
        Assert.Equal(-3.75e-3, u.Z, 12);
        // Без перемещений в источнике — нет данных, а не ноль.
        Assert.Null(FragmentMaskDistribution.DisplacementAt([new(0, null, Dof6.Zero)], 0));
    }

    /// <summary>Сочетание «A + 0,5·B» по сэмплам; у B нет перемещений — в сумме их тоже нет.</summary>
    [Fact]
    public void Combine_LinearCombinationOfLoadCaseMasks()
    {
        var a = Mask();
        var b = Mask().Select(x => x with { U = null, F = x.F!.Scale(2) }).ToList();

        var sum = FragmentMaskDistribution.Combine([(a, 1.0), (b, 0.5)])!;
        var onlyA = FragmentMaskDistribution.Combine([(a, 2.0)])!;

        Assert.Equal(a[1].F!.Scale(2), sum[1].F);
        Assert.All(sum, x => Assert.Null(x.U));
        Assert.Equal(a[4].U!.Scale(2), onlyA[4].U);
        Assert.Null(FragmentMaskDistribution.Combine([(a, 1.0), (a.Take(5).ToList(), 1.0)]));
    }

    static void AssertVector(PlanarVector3 expected, PlanarVector3 actual)
    {
        double scale = Math.Max(1, expected.Length);
        Assert.True((expected - actual).Length <= 1e-9 * scale, $"ожидалось {expected}, получено {actual}");
    }
}
