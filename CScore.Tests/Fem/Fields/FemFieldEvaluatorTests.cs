using CScore.Fem;
using CScore.Fem.Fields;
using Xunit;

namespace CScore.Tests.Fem.Fields;

public class FemFieldEvaluatorTests
{
    /// <summary>
    /// Плита 3 × 1 КЭ (узлы 1…8, z = −16,3), КЭ 1–2 у КонЭ «P1», КЭ 3 — у «P2»; пружины в узлах 1 и 2. Узлы КЭ — в
    /// порядке хранения сетки «1 2 4 3» (как в ЛИРЕ/SCAD), не по контуру.
    /// </summary>
    static FemFieldMeshContext Context(double dz = 0)
    {
        var nodes = new List<FemMeshNode>();
        for (int i = 0; i <= 3; i++)
        {
            nodes.Add(new FemMeshNode { NodeTag = (i + 1).ToString(), X = i, Y = 0, Z = -16.3 + dz });
            nodes.Add(new FemMeshNode { NodeTag = (i + 5).ToString(), X = i, Y = 1, Z = -16.3 + dz });
        }
        FemElement Shell(string tag, string member, int a, int b) => new()
        {
            ElemTag = tag, ElemType = "shell", SourceMemberTag = member, NodeIdsJson = $"[{a},{b},{a + 4},{b + 4}]",
        };
        var elements = new List<FemElement> { Shell("1", "P1", 1, 2), Shell("2", "P1", 2, 3), Shell("3", "P2", 3, 4) };
        var springs = new List<FemSpring> { new() { NodeTag = "1", Kz = 1 }, new() { NodeTag = "2", Kz = 1 } };
        return new FemFieldMeshContext(nodes, elements, [], [], springs);
    }

    static FemField C1Field(params string[] memberTags) => new()
    {
        Id = 5, Name = "C1", Quantity = FemFieldQuantities.FoundationC1, HasZ = false,
        Definition = new FemFieldDefinition
        {
            Columns = [new FemFieldColumn("C1", "c1", "тс/м³", 9806.65)],
            Targets = new FemFieldTargets { MemberTags = [.. memberTags] },
            Settings = new FieldSettings(MaxDistance: 2, PlaneTolerance: 0.5),
        },
        Points = [new(new(-1, -1, 0), [100]), new(new(5, -1, 0), [100]), new(new(-1, 2, 0), [100]), new(new(5, 2, 0), [100])],
    };

    [Fact]
    public void Compute_C1ForMemberTargets_InSi()
    {
        var computed = FemFieldEvaluator.Compute(C1Field("P1"), Context());
        Assert.Equal(["1", "2"], computed.Values.Select(v => v.TargetTag));
        Assert.All(computed.Values, v =>
        {
            Assert.Equal(FemFieldTargetKind.Shell, v.TargetKind);
            Assert.Equal("c1", v.Component);
            Assert.Equal(980665, v.Value!.Value, 6);
            Assert.Equal(5, v.FieldId);
        });
    }

    [Fact]
    public void ResolveTargets_QuadStoredZigzag_ShellsGoAroundContour()
    {
        var shells = FemFieldEvaluator.ResolveTargets(C1Field(), Context()).Shells;
        Assert.Equal(3, shells.Count);
        Assert.All(shells, s =>
        {
            Assert.Equal(1, s.Area, 12);                // «бабочка» дала бы площадь 0
            Assert.Equal(1, Math.Abs(s.Normal.Z), 12);
        });
        Assert.Equal(["1", "2", "6", "5"], shells[0].NodeTags);
    }

    [Fact]
    public void EmptyTargets_AllShells_MissingTagsReported()
    {
        var all = FemFieldEvaluator.Compute(C1Field(), Context());
        Assert.Equal(3, all.Values.Count);
        var f = C1Field();
        f.Definition.Targets.ElemTags = ["1", "99"];
        var some = FemFieldEvaluator.Compute(f, Context());
        Assert.Single(some.Values);
        Assert.Contains(some.Evaluation.Diagnostics, d => d.Code == "field_target_missing" && d.Tags.Contains("99"));
    }

    [Fact]
    public void Signature_ChangesWithMesh_NotWithOtherTargets()
    {
        var f = C1Field("P1");
        string s0 = FemFieldEvaluator.Signature(f, Context());
        Assert.Equal(s0, FemFieldEvaluator.Signature(f, Context()));
        Assert.NotEqual(s0, FemFieldEvaluator.Signature(f, Context(dz: 0.01)));
        // Правка самого поля (способ, точки) тоже меняет подпись — кэш значений не переживает изменения поля.
        f.Definition.Settings = f.Definition.Settings with { Method = FieldMethod.Nearest };
        string s1 = FemFieldEvaluator.Signature(f, Context());
        Assert.NotEqual(s0, s1);
        f.Points[0] = f.Points[0] with { Values = [101] };
        Assert.NotEqual(s1, FemFieldEvaluator.Signature(f, Context()));
    }

    [Fact]
    public void SpringField_NodesWithSprings_Matched()
    {
        var f = new FemField
        {
            Id = 7, Quantity = FemFieldQuantities.Spring, HasZ = false,
            Definition = new FemFieldDefinition
            {
                Columns = [new FemFieldColumn("Rz", "kz", "тс/см", 980665)],
                Settings = new FieldSettings(FieldMethod.Match, MatchTolerance: 0.5),
            },
            Points = [new(new(0.2, 0.1, 0), [300]), new(new(1, 0, 0), [200])],
        };
        var computed = FemFieldEvaluator.Compute(f, Context());
        Assert.Equal(["1", "2"], computed.Values.Select(v => v.TargetTag));
        Assert.Equal(300 * 980665.0, computed.Values[0].Value!.Value, 3);
        Assert.Equal(FemFieldTargetKind.MeshNode, computed.Values[0].TargetKind);
    }

    [Fact]
    public void PointCodec_RoundTrips()
    {
        var pts = new List<FieldPoint> { new(new(1, 2, double.NaN), [3, 4]), new(new(-5, 6.5, 7), [8, 9]) };
        var back = FemFieldPointCodec.Decode(FemFieldPointCodec.Encode(pts, 2), 2);
        Assert.Equal(2, back.Count);
        Assert.Equal(pts[1].P, back[1].P);
        Assert.Equal([8.0, 9.0], back[1].Values);
        Assert.Throws<ArgumentException>(() => FemFieldPointCodec.Decode(FemFieldPointCodec.Encode(pts, 2), 1));
    }

    [Fact]
    public void Definition_JsonRoundTrips()
    {
        var d = C1Field("P1").Definition;
        d.Alignment = new FieldAlignment(0.01, true, false, 15, 63.6, 29.7, -148.9);
        d.Settings = new FieldSettings(FieldMethod.Idw, 3, 1.5, 4, 6, 2, 0.4);
        d.ZHeader = "Z";
        var back = FemFieldDefinition.FromJson(d.ToJson());
        Assert.Equal(d.Alignment, back.Alignment);
        Assert.Equal(d.Settings, back.Settings);
        Assert.Equal(["P1"], back.Targets.MemberTags);
        Assert.Equal(d.Columns, back.Columns);
        Assert.Equal("Z", back.ZHeader);
    }
}
