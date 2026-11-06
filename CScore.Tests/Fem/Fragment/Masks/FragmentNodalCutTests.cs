using System.Globalization;
using CScore.Fem;
using CScore.Fem.Fragment;
using CScore.Fem.Fragment.Masks;
using CScore.Fem.Loads;
using CScore.Planar;
using CScore.Submodel;
using Xunit;
using static CScore.Tests.Fem.Fragment.Masks.FragmentMaskFixtures;

namespace CScore.Tests.Fem.Fragment.Masks;

/// <summary>
/// Силы кромок по узловым силам КЭ (спека 2026-11-05). Теги стены <c>Wall(3, 3, 6, 6)</c>: узел ряда r, столбца c —
/// <c>r·7 + c + 1</c> (x = 0, z = 1,0 — 15; x = 0, z = 0,5 — 8; x = 1,5, z = 0,5 — 11; x = 1,5, z = 1,0 — 18); КЭ — по
/// рядам снизу, по 6 в ряду.
/// </summary>
public sealed class FragmentNodalCutTests
{
    static readonly FragmentMaskKey K1 = FragmentMaskKey.OfLoadCase(1);

    static FragmentMaskCutInput WallBottom(double z, double w) => new(
        new FragmentCut("W", FragmentCutKind.Edge, FragmentCutSide.Bottom, z, null, [(V(0, 0, z), V(w, 0, z))]), null, V(0, 1, 0));

    /// <summary>
    /// Равномерное вертикальное сжатие n (Н/м) стены: сила узла на КЭ шириной b — у верхних узлов (0, 0, n·b/2), у
    /// нижних (0, 0, −n·b/2); узлы верха стены <paramref name="zTop"/> уравновешены узловой нагрузкой.
    /// </summary>
    static FragmentNodalForcesTable Compression(Dictionary<string, FemMeshNode> nodes, IEnumerable<FemElement> elements,
        double n, double zTop)
    {
        var t = new FragmentNodalForcesTable();
        var top = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var e in elements)
        {
            var g = FemElementGeometry.Of(e, nodes)!;
            double zMid = g.Centroid.Z, b = g.Points.Max(p => p.X) - g.Points.Min(p => p.X);
            for (int k = 0; k < g.NodeTags.Count; k++)
            {
                double fz = (g.Points[k].Z > zMid ? 1 : -1) * n * b / 2;
                t.SetElementNodeForce(K1, e.ElemTag, g.NodeTags[k], new Dof6(0, 0, fz, 0, 0, 0));
                if (Math.Abs(g.Points[k].Z - zTop) < 1e-9) top[g.NodeTags[k]] = top.GetValueOrDefault(g.NodeTags[k]) + fz;
            }
        }
        foreach (var (tag, fz) in top) t.AddNodeLoad(K1, tag, new Dof6(0, 0, fz, 0, 0, 0));
        return t;
    }

    static PlanarVector3 Sum(FragmentNodalCutResult r, int cut) =>
        r.Terms[K1].TryGetValue(cut, out var list) ? list.Aggregate(PlanarVector3.Zero, (s, x) => s + x.F) : PlanarVector3.Zero;

    /// <summary>Стена 3 × 3 (КЭ 0,5 м), сжатие 2000 Н/м, разрез z = 0,8: ступенька по рёбрам z = 1,0; ΣS = +6000 Н.</summary>
    [Fact]
    public void Wall_StepForces_EqualMinusDiscardedElementForces()
    {
        var (nodes, elements) = Wall(3, 3, 6, 6);
        var t = Compression(nodes, elements, -2000, 3);
        var r = FragmentNodalCut.Compute([WallBottom(0.8, 3)], new FragmentBounds(0.8, 3, null, 1e-3), nodes, elements,
            new HashSet<string>(), [K1], t);

        Assert.True(r.Ok, string.Join("; ", r.Diagnostics.Select(d => d.Message)));
        var f = Sum(r, 0);
        Assert.Equal(6000, f.Z, 6);
        Assert.Equal(0, f.X, 9);
        Assert.All(r.Terms[K1][0], x => Assert.Equal(1.0, x.At.Z, 12));
    }

    /// <summary>
    /// Разрез z = 0,9 пересекает ряд КЭ 0,5…1,0 (они отброшены, доля внутри фрагмента 0,2): поправка на полоску −0,2·ΣL
    /// нагрузок ряда. ΣL = 6 × 4 × (−100) = −2400 Н → маска 6000 + 480 Н; с нагрузками Ω_f (−480) и верха (−6000) — ноль.
    /// </summary>
    [Fact]
    public void Wall_ElementCrossingCut_CorrectionClosesBalance()
    {
        var (nodes, elements) = Wall(3, 3, 6, 6);
        var t = Compression(nodes, elements, -2000, 3);
        foreach (var e in elements)
        {
            var g = FemElementGeometry.Of(e, nodes)!;
            if (Math.Abs(g.Centroid.Z - 0.75) > 1e-9) continue;
            foreach (var tag in g.NodeTags) t.SetElementLoad(K1, e.ElemTag, tag, new Dof6(0, 0, -100, 0, 0, 0));
        }
        var r = FragmentNodalCut.Compute([WallBottom(0.9, 3)], new FragmentBounds(0.9, 3, null, 1e-3), nodes, elements,
            new HashSet<string>(), [K1], t);

        Assert.True(r.Ok);
        Assert.Equal(6480, Sum(r, 0).Z, 6);
    }

    /// <summary>
    /// Разрез z = 0,9; КЭ 7 (x 0…0,5, z 0,5…1,0) отброшен, доля внутри 0,2. Сосредоточенная −1000 Н (z) по точке:
    /// внутри (z = 0,95) — поправка +1000, маска 7000; снаружи (z = 0,6) — 0, маска 6000 (долей было бы 6200 в обоих).
    /// </summary>
    [Theory]
    [InlineData(0.95, 7000)]
    [InlineData(0.6, 6000)]
    public void Wall_PointLoadInCrossingElement_ByPoint(double z, double expected)
    {
        var (nodes, elements) = Wall(3, 3, 6, 6);
        var t = Compression(nodes, elements, -2000, 3);
        var g = FemElementGeometry.Of(elements.Single(e => e.ElemTag == "7"), nodes)!;
        Assert.Equal(0.75, g.Centroid.Z, 12);
        foreach (var tag in g.NodeTags) t.SetElementLoad(K1, "7", tag, new Dof6(0, 0, -250, 0, 0, 0));
        t.AddElementLoadPiece(K1, "7", new FemPointPiece(V(0.25, 0, z), V(0, 0, -1000), V(0, 0, 0)));
        var r = FragmentNodalCut.Compute([WallBottom(0.9, 3)], new FragmentBounds(0.9, 3, null, 1e-3), nodes, elements,
            new HashSet<string>(), [K1], t);

        Assert.True(r.Ok, string.Join("; ", r.Diagnostics.Select(d => d.Message)));
        Assert.Equal(expected, Sum(r, 0).Z, 6);
    }

    /// <summary>Нагрузка на верхнее ребро КЭ 7 (z = 1,0; −2000 Н/м на 0,5 м) — целиком внутри: поправка +1000, маска 7000.</summary>
    [Fact]
    public void Wall_EdgeLoadInCrossingElement_ByClip()
    {
        var (nodes, elements) = Wall(3, 3, 6, 6);
        var t = Compression(nodes, elements, -2000, 3);
        var g = FemElementGeometry.Of(elements.Single(e => e.ElemTag == "7"), nodes)!;
        foreach (var tag in g.NodeTags.Where(n => nodes[n].Z > 0.9))
            t.SetElementLoad(K1, "7", tag, new Dof6(0, 0, -500, 0, 0, 0));
        t.AddElementLoadPiece(K1, "7", new FemLinePiece(V(0, 0, 1), V(0.5, 0, 1), V(0, 0, -2000), V(0, 0, -2000)));
        var r = FragmentNodalCut.Compute([WallBottom(0.9, 3)], new FragmentBounds(0.9, 3, null, 1e-3), nodes, elements,
            new HashSet<string>(), [K1], t);

        Assert.True(r.Ok, string.Join("; ", r.Diagnostics.Select(d => d.Message)));
        Assert.Equal(7000, Sum(r, 0).Z, 6);
    }

    static (Dictionary<string, FemMeshNode> Nodes, List<FemElement> Elements) Plate(int n, double h, double z)
    {
        var nodes = new Dictionary<string, FemMeshNode>(StringComparer.Ordinal);
        string T(int i, int j) => (j * (n + 1) + i + 1).ToString(CultureInfo.InvariantCulture);
        for (int j = 0; j <= n; j++)
            for (int i = 0; i <= n; i++)
                nodes[T(i, j)] = new FemMeshNode { NodeTag = T(i, j), X = h * i, Y = h * j, Z = z };
        var elements = new List<FemElement>();
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
                elements.Add(new FemElement
                {
                    ElemTag = (j * n + i + 1).ToString(CultureInfo.InvariantCulture), ElemType = "shell",
                    NodeIdsJson = $"[{T(i, j)},{T(i + 1, j)},{T(i, j + 1)},{T(i + 1, j + 1)}]",
                });
        return (nodes, elements);
    }

    /// <summary>Равномерное растяжение nx = ny = n плиты: сила узла на КЭ ширины b — (±n·b/2, ±n·b/2, 0) наружу от центра.</summary>
    static FragmentNodalForcesTable Tension(Dictionary<string, FemMeshNode> nodes, IEnumerable<FemElement> elements, double n)
    {
        var t = new FragmentNodalForcesTable();
        foreach (var e in elements)
        {
            var g = FemElementGeometry.Of(e, nodes)!;
            double b = g.Points.Max(p => p.X) - g.Points.Min(p => p.X);
            for (int k = 0; k < g.NodeTags.Count; k++)
                t.SetElementNodeForce(K1, e.ElemTag, g.NodeTags[k], new Dof6(
                    Math.Sign(g.Points[k].X - g.Centroid.X) * n * b / 2, Math.Sign(g.Points[k].Y - g.Centroid.Y) * n * b / 2, 0, 0, 0, 0));
        }
        return t;
    }

    static FragmentMaskCutInput Lateral(string tag, PlanarVector3 normal, PlanarVector3 p, PlanarVector3 q) => new(
        new FragmentCut(tag, FragmentCutKind.Edge, FragmentCutSide.Lateral, 1, null, [(p, q)], normal), null, V(0, 0, 1));

    static PlanarVector3 Round(PlanarVector3 v) => new(Math.Round(v.X, 6) + 0, Math.Round(v.Y, 6) + 0, Math.Round(v.Z, 6) + 0);

    /// <summary>
    /// Плита 4 × 4 (КЭ 1 м), призма (2, 2) с полуразмерами 1,2 × 1,1: тело — 2 × 2 КЭ, грани x = 0,8 / 3,2, y = 0,9 / 3,1.
    /// Угловые КЭ (Eval по ±Y 0,4 больше, чем по ±X 0,3) уходят в разрезы ±Y: Fx(+X) = 2000, Fy(+Y) = 3000, ΣF = 0.
    /// </summary>
    [Fact]
    public void PrismCorner_TermsGoToPlaneOfLargestEval()
    {
        var (nodes, elements) = Plate(4, 1, 1);
        var t = Tension(nodes, elements, 1000);
        var bounds = new FragmentBounds(0.5, 1.5, FragmentPrism.Create(2, 2, 1, 0, 1.2, 1.1), 1e-3);
        FragmentMaskCutInput[] cuts =
        [
            Lateral("+X", V(1, 0, 0), V(3.2, 0.9, 1), V(3.2, 3.1, 1)),
            Lateral("-X", V(-1, 0, 0), V(0.8, 0.9, 1), V(0.8, 3.1, 1)),
            Lateral("+Y", V(0, 1, 0), V(0.8, 3.1, 1), V(3.2, 3.1, 1)),
            Lateral("-Y", V(0, -1, 0), V(0.8, 0.9, 1), V(3.2, 0.9, 1)),
        ];
        var r = FragmentNodalCut.Compute(cuts, bounds, nodes, elements, new HashSet<string>(), [K1], t);

        Assert.True(r.Ok, string.Join("; ", r.Diagnostics.Select(d => d.Message)));
        Assert.Equal(V(2000, 0, 0), Round(Sum(r, 0)));
        Assert.Equal(V(-2000, 0, 0), Round(Sum(r, 1)));
        Assert.Equal(V(0, 3000, 0), Round(Sum(r, 2)));
        Assert.Equal(V(0, -3000, 0), Round(Sum(r, 3)));
        Assert.DoesNotContain(r.Diagnostics, d => d.Code == "fragment_mask_nodal_unassigned");

        // Без разреза −Y его силы некуда отнести — предупреждение с суммой.
        var partial = FragmentNodalCut.Compute(cuts[..3], bounds, nodes, elements, new HashSet<string>(), [K1], t);
        Assert.Contains(partial.Diagnostics, d => d.Code == "fragment_mask_nodal_unassigned");
    }

    [Fact]
    public void SelfCheck_WrongElementForces_NotOk()
    {
        var (nodes, elements) = Wall(3, 3, 6, 6);
        var t = Compression(nodes, elements, -2000, 3);
        // КЭ тела (центр z = 1,75) с силами ×1,1 — «не та K_e».
        var e = elements.First(x => Math.Abs(FemElementGeometry.Of(x, nodes)!.Centroid.Z - 1.75) < 1e-9);
        t.TryElementNodeForces(K1, e.ElemTag, out var f);
        foreach (var (tag, v) in f.ToList()) t.SetElementNodeForce(K1, e.ElemTag, tag, v.Scale(1.1));

        var r = FragmentNodalCut.Compute([WallBottom(0.8, 3)], new FragmentBounds(0.8, 3, null, 1e-3), nodes, elements,
            new HashSet<string>(), [K1], t);

        Assert.False(r.Ok);
        Assert.Contains(r.Diagnostics, d => d.Code == "fragment_mask_nodal_selfcheck" && !d.IsError);
    }

    static FragmentNodalCutResult Run(FragmentNodalForcesTable t, Dictionary<string, FemMeshNode> nodes, List<FemElement> elements,
        double z1 = 0.8) =>
        FragmentNodalCut.Compute([WallBottom(z1, 3)], new FragmentBounds(z1, 3, null, 1e-3), nodes, elements,
            new HashSet<string>(), [K1], t);

    static string Text(FragmentNodalCutResult r) => string.Join("; ", r.Diagnostics.Select(d => $"{d.Code}: {d.Message}"));

    /// <summary>Связь узла тела 15 (x = 0, z = 1,0) с узлом отброшенной части 8 (z = 0,5): L_15 = 0 — учтена, предупреждения нет.</summary>
    [Fact]
    public void LinkAcrossStep_CountedByNodeEquilibrium()
    {
        var (nodes, elements) = Wall(3, 3, 6, 6);
        var t = Compression(nodes, elements, -2000, 3);
        t.Links.Add(("15", "8"));
        var r = Run(t, nodes, elements);

        Assert.True(r.Ok, Text(r));
        Assert.Equal(6000, Sum(r, 0).Z, 6);
        Assert.Contains(r.Diagnostics, d => d.Code == "fragment_mask_link_forces" && !d.IsError);
        Assert.DoesNotContain(r.Diagnostics, d => d.Code == "fragment_mask_link_crosses_cut");
    }

    /// <summary>
    /// Ведущий без КЭ снаружи (1,5; 0,3; 0,5), ведомый — узел ступеньки 18 с узловой нагрузкой −500 по Z: связь держит
    /// узел силой L_18 = +500 — член маски в точке узла, Σ = 6500 (уравновешивает верх −6000 и P_18).
    /// </summary>
    [Fact]
    public void RigidLinkFromOutside_AddsNodeEquilibriumForce()
    {
        var (nodes, elements) = Wall(3, 3, 6, 6);
        nodes["m"] = new FemMeshNode { NodeTag = "m", X = 1.5, Y = 0.3, Z = 0.5 };
        var t = Compression(nodes, elements, -2000, 3);
        t.Links.Add(("m", "18"));
        t.AddNodeLoad(K1, "18", new Dof6(0, 0, -500, 0, 0, 0));
        var r = Run(t, nodes, elements);

        Assert.True(r.Ok, Text(r));
        Assert.Equal(6500, Sum(r, 0).Z, 6);
        Assert.Contains(r.Terms[K1][0], x => (x.At - V(1.5, 0, 1)).Length < 1e-12 && Math.Abs(x.F.Z - 500) < 1e-9);
        Assert.Contains(r.Diagnostics, d => d.Code == "fragment_mask_link_forces");
        Assert.DoesNotContain(r.Diagnostics, d => d.Code == "fragment_mask_link_crosses_cut");
    }

    /// <summary>АЖТ «m → 17, 18»: члены −P в обоих ведомых, с моментом у 17; Σ по Z = 6800, по X = −100.</summary>
    [Fact]
    public void RigidBodyTwoSlaves_ForceAndMomentPerSlave()
    {
        var (nodes, elements) = Wall(3, 3, 6, 6);
        nodes["m"] = new FemMeshNode { NodeTag = "m", X = 1.5, Y = 0.3, Z = 0.5 };
        var t = Compression(nodes, elements, -2000, 3);
        t.Links.Add(("m", "17"));
        t.Links.Add(("m", "18"));
        t.AddNodeLoad(K1, "17", new Dof6(100, 0, -300, 0, 50, 0));
        t.AddNodeLoad(K1, "18", new Dof6(0, 0, -500, 0, 0, 0));
        var r = Run(t, nodes, elements);

        Assert.True(r.Ok, Text(r));
        Assert.Equal(V(-100, 0, 6800), Round(Sum(r, 0)));
        var at17 = Assert.Single(r.Terms[K1][0], x => (x.At - V(1, 0, 1)).Length < 1e-12 && x.M.Length > 0);
        Assert.Equal(V(0, -50, 0), Round(at17.M));
        Assert.Equal(V(-100, 0, 300), Round(at17.F));
    }

    /// <summary>Связь двух узлов тела — внутренняя: членов и диагностик связей нет.</summary>
    [Fact]
    public void LinkInsideBody_Ignored()
    {
        var (nodes, elements) = Wall(3, 3, 6, 6);
        var t = Compression(nodes, elements, -2000, 3);
        t.Links.Add(("25", "18"));
        t.AddNodeLoad(K1, "18", new Dof6(0, 0, -500, 0, 0, 0));
        t.AddNodeLoad(K1, "25", new Dof6(0, 0, 500, 0, 0, 0));
        var r = Run(t, nodes, elements);

        Assert.True(r.Ok, Text(r));
        Assert.Equal(6000, Sum(r, 0).Z, 6);
        Assert.DoesNotContain(r.Diagnostics, d => d.Code.StartsWith("fragment_mask_link", StringComparison.Ordinal));
    }

    /// <summary>
    /// Пружина в узле ступеньки 18 при z₁ = 1,1 (узел в Ω_s, вне Ω_f): узловая нагрузка −500 (k = 1) и сила пружины +500
    /// (k = 1) гасятся — Σ = 6000 (без учёта пружины было бы 5500). При z₁ = 0,8 (k = 0) — ничего.
    /// </summary>
    [Fact]
    public void SpringAtStepNode_LikeReaction()
    {
        var (nodes, elements) = Wall(3, 3, 6, 6);
        var t = Compression(nodes, elements, -2000, 3);
        t.Links.Add(("18", null));
        t.AddNodeLoad(K1, "18", new Dof6(0, 0, -500, 0, 0, 0));

        var r = Run(t, nodes, elements, z1: 1.1);
        Assert.True(r.Ok, Text(r));
        Assert.Equal(6000, Sum(r, 0).Z, 6);
        Assert.Contains(r.Diagnostics, d => d.Code == "fragment_mask_link_forces");

        var inside = Run(t, nodes, elements);
        Assert.True(inside.Ok, Text(inside));
        Assert.Equal(6000, Sum(inside, 0).Z, 6);
        Assert.DoesNotContain(inside.Diagnostics, d => d.Code.StartsWith("fragment_mask_link", StringComparison.Ordinal));
    }

    /// <summary>АЖТ через ступеньку и пружина в той же группе — не разделить: не учтена, предупреждение.</summary>
    [Fact]
    public void MixedLinkGroup_Warned()
    {
        var (nodes, elements) = Wall(3, 3, 6, 6);
        nodes["m"] = new FemMeshNode { NodeTag = "m", X = 1.5, Y = 0.3, Z = 0.5 };
        var t = Compression(nodes, elements, -2000, 3);
        t.Links.Add(("m", "18"));
        t.Links.Add(("18", null));
        t.AddNodeLoad(K1, "18", new Dof6(0, 0, -500, 0, 0, 0));
        var r = Run(t, nodes, elements);

        Assert.True(r.Ok, Text(r));
        Assert.Equal(6000, Sum(r, 0).Z, 6);
        Assert.Contains(r.Diagnostics, d => d.Code == "fragment_mask_link_crosses_cut" && !d.IsError);
        Assert.DoesNotContain(r.Diagnostics, d => d.Code == "fragment_mask_link_forces");
    }

    /// <summary>Экстрактор: член связи не теряется при переводе в плотность — равнодействующая маски 6500 Н.</summary>
    [Fact]
    public void Extractor_RigidLinkForce_InMaskResultant()
    {
        var (nodes, elements) = Wall(3, 3, 6, 6);
        nodes["m"] = new FemMeshNode { NodeTag = "m", X = 1.5, Y = 0.3, Z = 0.5 };
        var result = new FragmentParentResultTable();
        result.AddKey(K1);
        foreach (var (tag, n) in nodes) result.SetDisplacement(K1, tag, new Dof6(0, 0, -1e-4 * n.Z, 0, 0, 0));
        var t = Compression(nodes, elements, -2000, 3);
        t.Links.Add(("m", "18"));
        t.AddNodeLoad(K1, "18", new Dof6(0, 0, -500, 0, 0, 0));
        var cut = WallBottom(0.8, 3);

        var x = FragmentMaskExtractor.Extract(new FragmentMaskInput
        {
            Cuts = [cut], ParentNodes = nodes, ParentElements = elements, Result = result,
            NodalForces = t, Bounds = new FragmentBounds(0.8, 3, null, 1e-3),
        });

        Assert.Equal(FragmentEdgeForceMethod.NodalForces, x.EdgeMethod);
        var mask = Assert.Single(x.Masks);
        Assert.Equal(6500, FragmentMaskBalance.Resultant(cut, mask.Samples, V(0, 0, 0))!.Value.F.Z, 3);
        Assert.DoesNotContain(x.Diagnostics, d => d.Code == "fragment_mask_cut_resultant");
    }

    /// <summary>
    /// Стержень отброшенной части x = 1,5, z 0,5…1,0 (узлы стены 11 и 18) с силой (0, 0, −300) в узле тела 18: без
    /// разреза «конец стержня» он входит в ступеньку (+300), с разрезом в z = 0,8 — нет (это маска конца стержня).
    /// </summary>
    [Fact]
    public void EndCutBar_ExcludedFromStepForces()
    {
        var (nodes, elements) = Wall(3, 3, 6, 6);
        var bar = new FemElement { ElemTag = "b", ElemType = "beam", NodeIdsJson = "[11,18]" };
        var all = elements.Append(bar).ToList();
        var t = Compression(nodes, elements, -2000, 3);
        t.SetElementNodeForce(K1, "b", "18", new Dof6(0, 0, -300, 0, 0, 0));
        t.SetElementNodeForce(K1, "b", "11", new Dof6(0, 0, 300, 0, 0, 0));
        t.AddNodeLoad(K1, "18", new Dof6(0, 0, -300, 0, 0, 0));
        var bounds = new FragmentBounds(0.8, 3, null, 1e-3);
        FragmentMaskCutInput end = new(new FragmentCut("B", FragmentCutKind.End, FragmentCutSide.Bottom, 0.8, "n", []), V(1.5, 0, 0.8), null);
        FragmentMaskCutInput[] cuts = [WallBottom(0.8, 3), end];

        var without = FragmentNodalCut.Compute(cuts, bounds, nodes, all, new HashSet<string>(), [K1], t);
        Assert.True(without.Ok, string.Join("; ", without.Diagnostics.Select(d => d.Message)));
        Assert.Equal(6300, Sum(without, 0).Z, 6);

        var endBars = FragmentNodalCut.EndCutBars(cuts, nodes, all, 1e-3);
        Assert.Equal("b", Assert.Single(endBars));
        var with = FragmentNodalCut.Compute(cuts, bounds, nodes, all, endBars, [K1], t);
        Assert.True(with.Ok);
        Assert.Equal(6000, Sum(with, 0).Z, 6);
    }

    /// <summary>
    /// Опорный узел без КЭ внутри фрагмента (как опора АЖТ колонны на музее), связанный АЖТ с узлом тела 25 (x = 1,5,
    /// z = 1,5): он часть тела по положению — реакция в поправку не идёт, связь не «через ступеньку».
    /// </summary>
    [Fact]
    public void ElementlessSupportNodeInside_BelongsToBody()
    {
        var (nodes, elements) = Wall(3, 3, 6, 6);
        nodes["s"] = new FemMeshNode { NodeTag = "s", X = 1.5, Y = 0.3, Z = 1.5 };
        var t = Compression(nodes, elements, -2000, 3);
        t.Links.Add(("25", "s"));
        t.AddReaction(K1, "s", new Dof6(0, 0, 300, 0, 0, 0));

        var r = FragmentNodalCut.Compute([WallBottom(0.8, 3)], new FragmentBounds(0.8, 3, null, 1e-3), nodes, elements,
            new HashSet<string>(), [K1], t);

        Assert.True(r.Ok);
        Assert.Equal(6000, Sum(r, 0).Z, 6);
        Assert.DoesNotContain(r.Diagnostics, d => d.Code == "fragment_mask_link_crosses_cut");
    }

    /// <summary>Экстрактор с узловыми силами: путь «узловые силы», маска стены — 2000 Н/м во всех сэмплах, без сверки МНК.</summary>
    [Fact]
    public void Extractor_NodalPath_UniformDensity()
    {
        var (nodes, elements) = Wall(3, 3, 6, 6);
        var result = new FragmentParentResultTable();
        result.AddKey(K1);
        foreach (var (tag, n) in nodes) result.SetDisplacement(K1, tag, new Dof6(0, 0, -1e-4 * n.Z, 0, 0, 0));
        var cut = WallBottom(0.8, 3);

        var x = FragmentMaskExtractor.Extract(new FragmentMaskInput
        {
            Cuts = [cut], ParentNodes = nodes, ParentElements = elements, Result = result,
            NodalForces = Compression(nodes, elements, -2000, 3), Bounds = new FragmentBounds(0.8, 3, null, 1e-3),
        });

        Assert.Equal(FragmentEdgeForceMethod.NodalForces, x.EdgeMethod);
        var mask = Assert.Single(x.Masks);
        Assert.All(mask.Samples, s => Assert.Equal(2000, s.F!.Z, 6));
        Assert.All(mask.Samples, s => Assert.Equal(-8e-5, s.U!.Z, 12));
        Assert.Equal(6000, FragmentMaskBalance.Resultant(cut, mask.Samples, V(0, 0, 0))!.Value.F.Z, 6);
        Assert.DoesNotContain(x.Diagnostics, d => d.Code == "fragment_mask_cut_resultant");
    }

    /// <summary>Самопроверка не прошла — МНК по центрам КЭ (усилия есть) и предупреждение.</summary>
    [Fact]
    public void Extractor_SelfCheckFails_FallsBackToLeastSquares()
    {
        var (nodes, elements) = Wall(3, 3, 6, 6);
        var result = new FragmentParentResultTable();
        result.AddKey(K1);
        foreach (var (tag, _) in nodes) result.SetDisplacement(K1, tag, Dof6.Zero);
        foreach (var e in elements)
        {
            var (ax, ay, _) = FemElementGeometry.Of(e, nodes)!.ShellFrame();
            result.SetShellForces(K1, e.ElemTag, new FragmentShellForces(ax, ay, [0, -2000, 0, 0, 0, 0, 0, 0]));
        }
        var nodal = Compression(nodes, elements, -2000, 3);
        // КЭ ряда 4 (z 1,5…2,0) — в теле.
        nodal.SetElementNodeForce(K1, elements[20].ElemTag, FemElementGeometry.Of(elements[20], nodes)!.NodeTags[0], new Dof6(0, 0, 1e4, 0, 0, 0));

        var x = FragmentMaskExtractor.Extract(new FragmentMaskInput
        {
            Cuts = [WallBottom(0.8, 3)], ParentNodes = nodes, ParentElements = elements, Result = result,
            NodalForces = nodal, Bounds = new FragmentBounds(0.8, 3, null, 1e-3),
        });

        Assert.Equal(FragmentEdgeForceMethod.LeastSquares, x.EdgeMethod);
        Assert.Contains(x.Diagnostics, d => d.Code == "fragment_mask_nodal_selfcheck");
        Assert.Single(x.Masks);
    }
}
