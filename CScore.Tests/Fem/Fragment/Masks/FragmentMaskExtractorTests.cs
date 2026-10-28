using CScore.Fem;
using CScore.Fem.Fragment;
using CScore.Fem.Fragment.Masks;
using CScore.Planar;
using CScore.Submodel;
using Xunit;
using static CScore.Tests.Fem.Fragment.Masks.FragmentMaskFixtures;

namespace CScore.Tests.Fem.Fragment.Masks;

public sealed class FragmentMaskExtractorTests
{
    static readonly FragmentMaskKey K1 = FragmentMaskKey.OfLoadCase(1);

    static FragmentMaskCutInput WallCut(FragmentCutSide side, double z, params (double X0, double X1)[] spans) => new(
        new FragmentCut("W", FragmentCutKind.Edge, side, z, null,
            spans.Select(s => (V(s.X0, 0, z), V(s.X1, 0, z))).ToList()),
        null, V(0, 1, 0));

    /// <summary>
    /// Стена 3 × 3 (сетка 6 × 6), всюду Ny = −2000 Н/м (сжатие), перемещения линейны. Рассечение низа z = 0,8 с проёмом
    /// x ∈ [1, 2]: два отрезка; ΣFz = +2000 × 2 = 4000 Н (низ толкает фрагмент вверх); s сквозная.
    /// </summary>
    [Fact]
    public void Wall_ResultantAndContinuousS()
    {
        var (nodes, elements) = Wall(3, 3, 6, 6, flip: true);
        var t = new FragmentParentResultTable();
        t.AddKey(K1);
        foreach (var (tag, n) in nodes) t.SetDisplacement(K1, tag, new Dof6(0, 0, -1e-4 * n.Z, 0, 0, 0));
        foreach (var e in elements)
        {
            var g = CScore.Fem.Loads.FemElementGeometry.Of(e, nodes)!;
            var (ax, ay, _) = g.ShellFrame();
            t.SetShellForces(K1, e.ElemTag, new FragmentShellForces(ax, ay, [0, -2000, 0, 0, 0, 0, 0, 0]));
        }
        var cut = WallCut(FragmentCutSide.Bottom, 0.8, (0, 1), (2, 3));

        var x = FragmentMaskExtractor.Extract(new FragmentMaskInput
        {
            Cuts = [cut], ParentNodes = nodes, ParentElements = elements, Result = t,
        });

        var mask = Assert.Single(x.Masks);
        Assert.Equal(K1, mask.Key);
        Assert.Equal(0, mask.Samples[0].S);
        Assert.Equal(2, mask.Samples[^1].S, 9);
        Assert.All(mask.Samples, s => Assert.Equal(-8e-5, s.U!.Z, 12));
        var (f, _) = FragmentMaskBalance.Resultant(cut, mask.Samples, V(0, 0, 0))!.Value;
        Assert.Equal(4000, f.Z, 6);
        Assert.DoesNotContain(x.Diagnostics, d => d.Code == "fragment_mask_cut_resultant");
    }

    /// <summary>Колонна: маска в точке, s = 0; сечение без КЭ родителя — предупреждение, маски нет.</summary>
    [Fact]
    public void Column_SampleAndUnmatched()
    {
        var nodes = new[] { ("1", 0.0), ("2", 3.0) }.ToDictionary(p => p.Item1,
            p => new FemMeshNode { NodeTag = p.Item1, X = 0, Y = 0, Z = p.Item2 }, StringComparer.Ordinal);
        var elements = new List<FemElement> { new() { ElemTag = "c", ElemType = "beam", NodeIdsJson = "[1,2]" } };
        var t = new FragmentParentResultTable();
        t.AddKey(K1);
        t.SetDisplacement(K1, "1", Dof6.Zero);
        t.SetDisplacement(K1, "2", new Dof6(0, 0, -3e-3, 0, 0, 0));
        FragmentMaskCutInput col = new(new FragmentCut("C", FragmentCutKind.End, FragmentCutSide.Bottom, 1, "n1", []),
            V(0, 0, 1), null);
        FragmentMaskCutInput lost = new(new FragmentCut("C2", FragmentCutKind.End, FragmentCutSide.Bottom, 1, "n2", []),
            V(5, 0, 1), null);

        var x = FragmentMaskExtractor.Extract(new FragmentMaskInput
        {
            Cuts = [col, lost], ParentNodes = nodes, ParentElements = elements, Result = t,
        });

        var mask = Assert.Single(x.Masks);
        Assert.Equal(0, mask.CutIndex);
        var s = Assert.Single(mask.Samples);
        Assert.Equal(-1e-3, s.U!.Z, 15);
        Assert.Null(s.F);
        Assert.Contains(x.Diagnostics, d => d.Code == "fragment_mask_cut_unmatched" && !d.IsError);
        Assert.Contains(x.Diagnostics, d => d.Code == "fragment_mask_no_forces");
    }

    /// <summary>Перемещений у источника нет → сэмплы без U и одно предупреждение на сечение и ключ.</summary>
    [Fact]
    public void Wall_NoDisplacements_Warned()
    {
        var (nodes, elements) = Wall(1, 1, 2, 2);
        var t = new FragmentParentResultTable();
        t.AddKey(K1);
        var x = FragmentMaskExtractor.Extract(new FragmentMaskInput
        {
            Cuts = [WallCut(FragmentCutSide.Top, 0.6, (0, 1))], ParentNodes = nodes, ParentElements = elements, Result = t,
        });
        Assert.All(Assert.Single(x.Masks).Samples, s => Assert.Null(s.U));
        Assert.Single(x.Diagnostics, d => d.Code == "fragment_mask_no_displacement");
    }
}
