using CScore.Fem;
using CScore.Fem.Fragment;
using CScore.Fem.Fragment.Masks;
using CScore.Planar;
using Xunit;

namespace CScore.Tests.Fem.Fragment;

/// <summary>Согласованность второго порядка фрагмента (спека 2026-11-06, срез 3): правило признака, торцы, сетка.</summary>
public class FragmentSecondOrderTests
{
    static FragmentSecondOrderFacts Full(bool? source = true, bool ends = true, bool allow = false, bool masks = true) => new()
    {
        Geometric = true, IsFragment = true, MasksUsed = masks, SourceSecondOrder = source, AllowFirstOrderMasks = allow,
        StaticEndMembers = ends ? [] : ["C1"], SourceLabel = "Линейный",
    };

    static string[] Codes(FragmentSecondOrderDecision d) => d.Diagnostics.Select(x => x.Code).ToArray();

    [Fact]
    public void Full_AllConditions_SecondOrderWithoutWarnings()
    {
        var d = FragmentSecondOrder.Decide(Full());
        Assert.True(d.SecondOrder);
        Assert.Empty(d.Diagnostics);
    }

    [Fact]
    public void Full_FirstOrderSource_FalseAndWarns()
    {
        var d = FragmentSecondOrder.Decide(Full(source: false));
        Assert.False(d.SecondOrder);
        Assert.Equal(["fragment_second_order_source_first_order", "fragment_second_order_eta"], Codes(d));
        Assert.Contains("«Линейный»", d.Diagnostics[0].Message);
        Assert.All(d.Diagnostics, x => Assert.False(x.IsError));
    }

    [Fact]
    public void Full_UnknownSource_IsFirstOrder()
    {
        var d = FragmentSecondOrder.Decide(Full(source: null));
        Assert.False(d.SecondOrder);
        Assert.Contains("не определён", d.Diagnostics[0].Message);
    }

    [Fact]
    public void Full_StaticEnds_FalseEvenWithSecondOrderSource()
    {
        var d = FragmentSecondOrder.Decide(Full(ends: false));
        Assert.False(d.SecondOrder);
        Assert.Equal(["fragment_second_order_static_ends", "fragment_second_order_eta"], Codes(d));
        Assert.Equal(["C1"], d.Diagnostics[0].SourceKeys!);
    }

    [Fact]
    public void Full_UserAllowsFirstOrderMasks_TrueButStillWarns()
    {
        var d = FragmentSecondOrder.Decide(Full(source: false, allow: true));
        Assert.True(d.SecondOrder);
        Assert.Equal(["fragment_second_order_source_first_order"], Codes(d));
        Assert.Contains("по выбору пользователя", d.Diagnostics[0].Message);
        // Флажок снимает только условие источника, не торцов.
        Assert.False(FragmentSecondOrder.Decide(Full(source: false, allow: true, ends: false)).SecondOrder);
    }

    [Fact]
    public void Full_NoMasksUsedAndKinematicEnds_True()
    {
        // Без масок (все торцы — по маскам не берутся) условие источника не действует; торцы решают отдельно.
        Assert.True(FragmentSecondOrder.Decide(Full(source: null, masks: false)).SecondOrder);
    }

    [Fact]
    public void NotGeometric_FalseWithoutDiagnostics()
    {
        var d = FragmentSecondOrder.Decide(Full() with { Geometric = false });
        Assert.False(d.SecondOrder);
        Assert.Empty(d.Diagnostics);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(5.0, false)]
    [InlineData(10.0, true)]
    [InlineData(12.0, true)]
    public void LocalOnly_ByLambda(double? lambda, bool expected)
    {
        var facts = Full(source: false, ends: false) with { LocalOnly = true, LambdaCr = lambda };
        var d = FragmentSecondOrder.Decide(facts);
        Assert.Equal(expected, d.SecondOrder);
        // В LocalOnly маски первого порядка и силовые торцы — штатны: их предупреждений нет.
        Assert.DoesNotContain("fragment_second_order_source_first_order", Codes(d));
        Assert.DoesNotContain("fragment_second_order_static_ends", Codes(d));
        Assert.Equal(!expected, Codes(d).Contains("fragment_second_order_eta"));
    }

    [Fact]
    public void LocalOnly_NotFragment_SameLambdaRule()
    {
        var facts = new FragmentSecondOrderFacts { Geometric = true, LocalOnly = true, LambdaCr = 4 };
        Assert.False(FragmentSecondOrder.Decide(facts).SecondOrder);
        Assert.True(FragmentSecondOrder.Decide(facts with { LambdaCr = 15 }).SecondOrder);
    }

    [Fact]
    public void Coarse_WarnsButDoesNotChangeFlag()
    {
        var d = FragmentSecondOrder.Decide(Full() with { CoarseMembers = [new("C1", 3, 2, 0), new("W1", 3, 0, 1.5)] });
        Assert.True(d.SecondOrder);
        var w = Assert.Single(d.Diagnostics);
        Assert.Equal("fragment_second_order_coarse", w.Code);
        Assert.Equal(["C1", "W1"], w.SourceKeys!);
        Assert.Contains("«C1» — 2 КЭ", w.Message);
    }

    // ---------------------------------------------------------------- торцы

    static FragmentCut End(string member, FragmentCutSide side) =>
        new(member, FragmentCutKind.End, side, side == FragmentCutSide.Bottom ? 0 : 3, member + side, []);

    static FragmentCut Lateral(string member) =>
        new(member, FragmentCutKind.Edge, FragmentCutSide.Lateral, 1.5, null,
            [(new PlanarVector3(1, 0, 0), new PlanarVector3(1, 0, 3))], new PlanarVector3(1, 0, 0));

    static readonly IReadOnlyList<(int, FragmentCut)> ColumnCuts =
        [(1, End("C1", FragmentCutSide.Bottom)), (2, End("C1", FragmentCutSide.Top)), (3, Lateral("S1"))];

    [Theory]
    [InlineData(FragmentBcPreset.Kinematic, 0)]
    [InlineData(FragmentBcPreset.KinematicTranslations, 0)]
    [InlineData(FragmentBcPreset.Static, 1)]
    [InlineData(FragmentBcPreset.Isolation, 1)]
    public void StaticEnds_ByPreset(FragmentBcPreset preset, int staticCount)
    {
        var ends = FragmentSecondOrder.StaticEnds(ColumnCuts, preset, FragmentBcPreset.Static, new Dictionary<int, byte[]>(), out bool used);
        Assert.Equal(staticCount, ends.Count);
        // Боковая грань — «Усилия из родителя»: маска берётся при любом пресете торцов.
        Assert.True(used);
    }

    [Fact]
    public void StaticEnds_LateralPresetDoesNotMatter()
    {
        var ends = FragmentSecondOrder.StaticEnds(ColumnCuts, FragmentBcPreset.Kinematic, FragmentBcPreset.Isolation,
            new Dictionary<int, byte[]>(), out bool used);
        Assert.Empty(ends);
        Assert.True(used);
    }

    [Fact]
    public void StaticEnds_OverrideUyFree_Static()
    {
        var overrides = new Dictionary<int, byte[]>
        {
            [2] = FragmentCutBc.Encode([null, FragmentDofMode.Free, null, null, null, null]),
        };
        var ends = FragmentSecondOrder.StaticEnds(ColumnCuts, FragmentBcPreset.Kinematic, FragmentBcPreset.Static, overrides, out _);
        Assert.Equal(["C1"], ends);
        // Правка поворота на Ux/Uy не влияет.
        overrides[2] = FragmentCutBc.Encode([null, null, null, FragmentDofMode.Free, null, null]);
        Assert.Empty(FragmentSecondOrder.StaticEnds(ColumnCuts, FragmentBcPreset.Kinematic, FragmentBcPreset.Static, overrides, out _));
    }

    [Fact]
    public void StaticEnds_IsolationOnly_NoMasks()
    {
        FragmentSecondOrder.StaticEnds(ColumnCuts.Take(2).ToList(), FragmentBcPreset.Isolation, FragmentBcPreset.Static,
            new Dictionary<int, byte[]>(), out bool used);
        Assert.False(used);
    }

    // ---------------------------------------------------------------- сетка

    sealed class Mesh
    {
        public List<FemMeshNode> Nodes { get; } = [];
        public List<FemElement> Elements { get; } = [];

        int Node(double x, double y, double z)
        {
            var n = Nodes.FirstOrDefault(p => Math.Abs(p.X - x) + Math.Abs(p.Y - y) + Math.Abs(p.Z - z) < 1e-9);
            if (n != null) return int.Parse(n.NodeTag);
            int id = Nodes.Count + 1;
            Nodes.Add(new FemMeshNode { NodeTag = id.ToString(), X = x, Y = y, Z = z });
            return id;
        }

        void Add(string member, string type, params int[] ids) => Elements.Add(new FemElement
        {
            ElemTag = (Elements.Count + 1).ToString(), ElemType = type, SourceMemberTag = member,
            NodeIdsJson = "[" + string.Join(",", ids) + "]",
        });

        /// <summary>Стержень от a до b, разбитый на n КЭ.</summary>
        public void Bar(string member, PlanarVector3 a, PlanarVector3 b, int n)
        {
            for (int i = 0; i < n; i++)
            {
                var p = a + (b - a) * ((double)i / n);
                var q = a + (b - a) * ((double)(i + 1) / n);
                Add(member, "beam", Node(p.X, p.Y, p.Z), Node(q.X, q.Y, q.Z));
            }
        }

        /// <summary>Стена в плоскости y = 0 шириной w и высотой h, сетка nx × nz.</summary>
        public void Wall(string member, double w, double h, int nx, int nz)
        {
            for (int i = 0; i < nx; i++)
                for (int k = 0; k < nz; k++)
                {
                    double x0 = w * i / nx, x1 = w * (i + 1) / nx, z0 = h * k / nz, z1 = h * (k + 1) / nz;
                    Add(member, "shell", Node(x0, 0, z0), Node(x1, 0, z0), Node(x1, 0, z1), Node(x0, 0, z1));
                }
        }
    }

    [Fact]
    public void Coarse_ColumnBeamCount()
    {
        var m = new Mesh();
        m.Bar("C3", new(0, 0, 0), new(0, 0, 3), 3);
        m.Bar("C4", new(2, 0, 0), new(2, 0, 3), 4);
        var c = Assert.Single(FragmentSecondOrder.Coarse(m.Elements, m.Nodes));
        Assert.Equal("C3", c.MemberTag);
        Assert.Equal(3, c.Beams);
        Assert.Equal(3, c.HeightM, 9);
    }

    [Fact]
    public void Coarse_ColumnOfTwoHalfStoreyMembers_IsOneChain()
    {
        // Колонна фрагмента — два КонЭ по полэтажа с узлом перекрытия: 3 + 3 КЭ на 3 м — не грубо.
        var fine = new Mesh();
        fine.Bar("C0a", new(0, 0, 1.5), new(0, 0, 3), 3);
        fine.Bar("C0b", new(0, 0, 3), new(0, 0, 4.5), 3);
        Assert.Empty(FragmentSecondOrder.Coarse(fine.Elements, fine.Nodes));

        // По одному КЭ на половину — 2 КЭ на цепочку 3 м.
        var coarse = new Mesh();
        coarse.Bar("C0a", new(0, 0, 1.5), new(0, 0, 3), 1);
        coarse.Bar("C0b", new(0, 0, 3), new(0, 0, 4.5), 1);
        coarse.Bar("B1", new(0, 0, 3), new(6, 0, 3), 1);  // ригель в узле перекрытия цепочку не продолжает
        var c = Assert.Single(FragmentSecondOrder.Coarse(coarse.Elements, coarse.Nodes));
        Assert.Equal("C0a+C0b", c.MemberTag);
        Assert.Equal(2, c.Beams);
        Assert.Equal(3, c.HeightM, 9);
    }

    [Fact]
    public void Coarse_WallOfTwoStackedMembers_IsOneGroup()
    {
        // Стена 3 м с шагом 0,5 м, верхняя половина — другой КонЭ (полэтажа над перекрытием): по КонЭ было бы
        // 0,5 > 1,5/4, по группе — 0,5 ≤ 3/4.
        var m = new Mesh();
        m.Wall("W1", 2, 3, 2, 6);
        var nodes = m.Nodes.ToDictionary(n => n.NodeTag, StringComparer.Ordinal);
        foreach (var e in m.Elements)
            if (CScore.Fem.Loads.FemElementGeometry.Of(e, nodes)!.Centroid.Z > 1.5) e.SourceMemberTag = "W2";
        Assert.Contains(m.Elements, e => e.SourceMemberTag == "W2");
        Assert.Empty(FragmentSecondOrder.Coarse(m.Elements, m.Nodes));

        // Параллельная стена в другой плоскости (y = 1) с общей кромкой не делит группу: её шаг 1,5 м — своя группа.
        var two = new Mesh();
        two.Wall("W1", 2, 3, 2, 6);
        var near = new Mesh();
        near.Wall("W3", 2, 3, 1, 2);
        int offset = two.Nodes.Count;
        foreach (var n in near.Nodes) two.Nodes.Add(new FemMeshNode { NodeTag = (int.Parse(n.NodeTag) + offset).ToString(), X = n.X, Y = 1, Z = n.Z });
        foreach (var e in near.Elements)
            two.Elements.Add(new FemElement
            {
                ElemTag = "n" + e.ElemTag, ElemType = "shell", SourceMemberTag = "W3",
                NodeIdsJson = "[" + string.Join(",", System.Text.Json.JsonSerializer.Deserialize<int[]>(e.NodeIdsJson)!.Select(i => i + offset)) + "]",
            });
        var c = Assert.Single(FragmentSecondOrder.Coarse(two.Elements, two.Nodes));
        Assert.Equal("W3", c.MemberTag);
        Assert.Equal(1.5, c.ShellStepM, 9);
    }

    [Fact]
    public void Coarse_HorizontalAndInclinedBars_Ignored()
    {
        var m = new Mesh();
        m.Bar("B1", new(0, 0, 3), new(6, 0, 3), 1);       // ригель
        m.Bar("D1", new(0, 0, 0), new(3, 0, 3), 1);       // раскос 45°
        m.Bar("V1", new(5, 0, 0), new(5.2, 0, 3), 4);     // 3,8° — вертикальный, 4 КЭ
        Assert.Empty(FragmentSecondOrder.Coarse(m.Elements, m.Nodes));
    }

    [Fact]
    public void Coarse_WallStep()
    {
        var coarse = new Mesh();
        coarse.Wall("W1", 2, 3, 2, 3);                    // шаг 1 м > 3/4
        var c = Assert.Single(FragmentSecondOrder.Coarse(coarse.Elements, coarse.Nodes));
        Assert.Equal(1, c.ShellStepM, 9);
        Assert.Equal(0, c.Beams);

        var fine = new Mesh();
        fine.Wall("W1", 2, 3, 2, 6);                      // шаг 0,5 м ≤ 3/4
        Assert.Empty(FragmentSecondOrder.Coarse(fine.Elements, fine.Nodes));
    }

    [Fact]
    public void Coarse_SlabIgnored()
    {
        var m = new Mesh();
        m.Elements.Add(new FemElement { ElemTag = "s", ElemType = "shell", SourceMemberTag = "P1", NodeIdsJson = "[1,2,3,4]" });
        m.Nodes.AddRange([
            new FemMeshNode { NodeTag = "1", X = 0, Y = 0, Z = 3 }, new FemMeshNode { NodeTag = "2", X = 6, Y = 0, Z = 3 },
            new FemMeshNode { NodeTag = "3", X = 6, Y = 6, Z = 3 }, new FemMeshNode { NodeTag = "4", X = 0, Y = 6, Z = 3 }]);
        Assert.Empty(FragmentSecondOrder.Coarse(m.Elements, m.Nodes));
    }
}
