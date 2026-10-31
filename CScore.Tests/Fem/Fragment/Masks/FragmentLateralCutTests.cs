using CScore.Fem;
using CScore.Fem.Fragment;
using CScore.Fem.Fragment.Masks;
using CScore.Planar;
using CScore.Submodel;
using Xunit;
using static CScore.Tests.Fem.Fragment.Masks.FragmentMaskFixtures;

namespace CScore.Tests.Fem.Fragment.Masks;

/// <summary>Маски на боковых гранях призмы (спека призмы 3.1): сторона и нормаль сечения — по <see cref="FragmentCut.Outward"/>.</summary>
public sealed class FragmentLateralCutTests
{
    static readonly FragmentMaskKey K1 = FragmentMaskKey.OfLoadCase(1);

    /// <summary>Плита на z = 3 из сетки стены <see cref="FragmentMaskFixtures.Wall"/> (Y ↔ Z): x ∈ [0, w], y ∈ [0, h].</summary>
    static (Dictionary<string, FemMeshNode> Nodes, List<FemElement> Elements) Slab(double w, double h, int nx, int ny)
    {
        var (nodes, elements) = Wall(w, h, nx, ny);
        foreach (var n in nodes.Values) (n.Y, n.Z) = (n.Z, 3);
        return (nodes, elements);
    }

    static FragmentParentResultTable Uniform(Dictionary<string, FemMeshNode> nodes, List<FemElement> elements, double nx, double ny)
    {
        var t = new FragmentParentResultTable();
        t.AddKey(K1);
        foreach (var (tag, n) in nodes) t.SetDisplacement(K1, tag, new Dof6(1e-4 * n.X, 0, 0, 0, 0, 0));
        foreach (var e in elements)
        {
            var g = CScore.Fem.Loads.FemElementGeometry.Of(e, nodes)!;
            var (ax, ay, _) = g.ShellFrame();
            // Усилия заданы в осях пластины: у обеих сеток ось X1 — глобальная X.
            t.SetShellForces(K1, e.ElemTag, new FragmentShellForces(ax, ay, [nx, ny, 0, 0, 0, 0, 0, 0]));
        }
        return t;
    }

    /// <summary>Горизонтальная балка 0 → 3 → 6 по X через грань x = 4,5 (+X): КЭ уходит в +X; в узле 3 у грани −X — КЭ 1.</summary>
    [Fact]
    public void Locate_LateralNormal()
    {
        FragmentBarElement[] bars =
        [
            new("b1", "1", "2", V(0, 0, 3), V(3, 0, 3), 0),
            new("b2", "2", "3", V(3, 0, 3), V(6, 0, 3), 0),
        ];
        var hit = FragmentBarCutSampler.Locate(V(4.5, 0, 3), V(1, 0, 0), bars, 1e-3)!;
        Assert.Equal(("b2", 0.5, true), (hit.ElemTag, hit.Xi, hit.DiscardedTowardJ));
        var atNode = FragmentBarCutSampler.Locate(V(3, 0, 3), V(-1, 0, 0), bars, 1e-3)!;
        Assert.Equal(("b1", false), (atNode.ElemTag, atNode.DiscardedTowardJ));
        var atNodePlus = FragmentBarCutSampler.Locate(V(3, 0, 3), V(1, 0, 0), bars, 1e-3)!;
        Assert.Equal(("b2", true), (atNodePlus.ElemTag, atNodePlus.DiscardedTowardJ));
    }

    /// <summary>
    /// Плита 3 × 3 (сетка 6 × 6), Nx = −2000 Н/м; кромка на грани x = 1,5 (+X) по всей ширине: нормаль сечения +X,
    /// действие отброшенной части ΣFx = −2000 × 3 = −6000 Н (сжатие толкает фрагмент в −X).
    /// </summary>
    [Fact]
    public void SlabEdgeOnLateralFace_NormalAndResultant()
    {
        var (nodes, elements) = Slab(3, 3, 6, 6);
        var g = FragmentWallCutSampler.Prepare(V(1.5, 0, 3), V(1.5, 3, 3), V(0, 0, 1), V(1, 0, 0), Shells(nodes, elements), 1e-3);
        Assert.Equal(V(1, 0, 0), g.CutNormal);
        Assert.Equal(36, g.Plane.Count);

        var cut = new FragmentMaskCutInput(new FragmentCut("S1", FragmentCutKind.Edge, FragmentCutSide.Lateral, 3, null,
            [(V(1.5, 0, 3), V(1.5, 3, 3))], V(1, 0, 0)), null, V(0, 0, 1));
        var x = FragmentMaskExtractor.Extract(new FragmentMaskInput
        {
            Cuts = [cut], ParentNodes = nodes, ParentElements = elements, Result = Uniform(nodes, elements, -2000, 0),
        });

        var mask = Assert.Single(x.Masks);
        Assert.All(mask.Samples, s => Assert.Equal(1.5e-4, s.U!.X, 12));
        var (f, _) = FragmentMaskBalance.Resultant(cut, mask.Samples, V(0, 0, 0))!.Value;
        Assert.Equal(-6000, f.X, 6);
        Assert.Equal(0, f.Y, 6);
        Assert.DoesNotContain(x.Diagnostics, d => d.Code == "fragment_mask_cut_resultant");
    }

    /// <summary>Вертикальная кромка стены y = 0 на грани x = 1,5 (−X, фрагмент справа): нормаль сечения −X, ΣFx = +2000 × 3.</summary>
    [Fact]
    public void VerticalWallEdgeOnLateralFace()
    {
        var (nodes, elements) = Wall(3, 3, 6, 6, flip: true);
        var cut = new FragmentMaskCutInput(new FragmentCut("W", FragmentCutKind.Edge, FragmentCutSide.Lateral, 1.5, null,
            [(V(1.5, 0, 0), V(1.5, 0, 3))], V(-1, 0, 0)), null, V(0, 1, 0));
        var g = FragmentWallCutSampler.Prepare(cut.Cut.Segments[0].P, cut.Cut.Segments[0].Q, V(0, 1, 0), V(-1, 0, 0),
            Shells(nodes, elements), 1e-3);
        Assert.Equal(V(-1, 0, 0), g.CutNormal);

        var x = FragmentMaskExtractor.Extract(new FragmentMaskInput
        {
            Cuts = [cut], ParentNodes = nodes, ParentElements = elements, Result = Uniform(nodes, elements, -2000, 0),
        });
        var (f, _) = FragmentMaskBalance.Resultant(cut, Assert.Single(x.Masks).Samples, V(0, 0, 0))!.Value;
        Assert.Equal(6000, f.X, 6);
        Assert.Equal(0, f.Z, 6);
    }

    /// <summary>Нормаль разреза ⟂ плоскости пластины (горизонтальный разрез по плите) — предупреждение, маски нет.</summary>
    [Fact]
    public void NormalAcrossShellPlane_Unmatched()
    {
        var (nodes, elements) = Slab(1, 1, 2, 2);
        var cut = new FragmentMaskCutInput(new FragmentCut("S1", FragmentCutKind.Edge, FragmentCutSide.Top, 3, null,
            [(V(0, 0.5, 3), V(1, 0.5, 3))]), null, V(0, 0, 1));
        var x = FragmentMaskExtractor.Extract(new FragmentMaskInput
        {
            Cuts = [cut], ParentNodes = nodes, ParentElements = elements, Result = Uniform(nodes, elements, 0, 0),
        });
        Assert.Empty(x.Masks);
        Assert.Contains(x.Diagnostics, d => d.Code == "fragment_mask_cut_unmatched");
    }
}
