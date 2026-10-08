using CScore.Fem;
using CScore.Import;
using Xunit;

namespace CScore.Tests.Import;

public sealed class ScadBoundaryTransferTests
{
    static readonly HashSet<string> Nodes = ["1", "2", "3", "4", "5", "6"];

    static readonly Dictionary<string, string> Types = new()
    {
        ["10"] = "beam", ["11"] = "beam", ["20"] = "shell",
    };

    [Fact]
    public void MapsSupportsSpringsBodiesAndJoints()
    {
        var model = new ScadAnalysisModel
        {
            HasBoundaryV2 = true, SchemaType = 5,
            Bounds = { [1] = 0b000011, [2] = 0x3F, [99] = 0x3F },
            Springs =
            {
                new ScadSpring(30, 26, 3, [0, 0, 9.81e6, 0, 0, 0]),
                new ScadSpring(31, 27, 3, [0, 0, 1e6, 0, 0, 0]),
                new ScadSpring(32, 26, 4, [1e5, 0, 0, 0, 0, 2e3]),
                new ScadSpring(33, 26, 77, [0, 0, 1, 0, 0, 0]),
            },
            RigidBodies =
            {
                new ScadRigidBody(40, 1, 5, [6, 98], 0x3F),
                new ScadRigidBody(41, 1, 97, [6], 0x3F),
            },
            Joints = { new ScadJoint(10, 0, 0b110000), new ScadJoint(11, 0b010000, 0b010000), new ScadJoint(20, 1, 0) },
            NotTransferred = { [ScadNotTransferredKinds.Fe55] = 58 },
        };

        var r = ScadBoundaryTransfer.Transfer(model, Nodes, Types);

        Assert.Equal([("1", 0b000011), ("2", 0x3F)], r.Supports.Select(s => (s.NodeTag, s.Mask)));
        Assert.All(r.Supports, s => Assert.Equal(ScadBoundaryTransfer.Origin, s.Origin));

        Assert.Equal(2, r.Springs.Count);
        var s3 = r.Springs.Single(s => s.NodeTag == "3");
        Assert.Equal(10.81e6, s3.Kz, 6);
        Assert.Equal("30,31", s3.SourceElemTag);
        Assert.Equal(FemSpringTargetKinds.MeshNode, s3.TargetKind);
        var s4 = r.Springs.Single(s => s.NodeTag == "4");
        Assert.Equal((1e5, 2e3), (s4.Kx, s4.Kuz));

        var body = Assert.Single(r.RigidBodies);
        Assert.Equal("5", body.MasterNodeTag);
        Assert.Equal(["6"], body.SlaveNodeTags);
        Assert.Equal("40", body.SourceElemTag);

        Assert.Equal(2, r.ElementProps.Count);
        Assert.Equal(new FemElementBoundaryProps(null, 0b110000, null), r.ElementProps["10"]);
        Assert.Equal(new FemElementBoundaryProps(0b010000, 0b010000, null), r.ElementProps["11"]);

        Assert.Contains("закреплений 2, пружин 2, жёстких тел 1, стержней с шарнирами 2 (концов 3)", r.Report[0]);
        Assert.Contains(r.Report, l => l.Contains("КЭ 55") && l.Contains("58"));
        Assert.Contains(r.Report, l => l.StartsWith("Закрепления: узлов нет в сетке — 1"));
        Assert.Contains(r.Report, l => l.StartsWith("Шарниры: 1 КЭ"));

        Assert.Equal(
            [("Закрепления", 2, 1), ("Связи конечной жёсткости (КЭ 51)", 3, 1), ("Жёсткие тела (КЭ 100)", 1, 1),
             ("Шарниры стержней", 2, 1), ("Упругое основание пластин (C1)", 0, 0), ("Упругие связи двух узлов (КЭ 55)", 0, 58)],
            r.Summary.Select(x => (x.Kind, x.Transferred, x.NotTransferred)));
        Assert.Equal("сложены на общих узлах: 2", r.Summary[1].Note);
        Assert.Equal("освобождённых концов: 3", r.Summary[3].Note);
    }

    [Fact]
    public void OldAttachment_TransfersSupportsAndBodies_AndAsksToReread()
    {
        var model = new ScadAnalysisModel { Bounds = { [1] = 0x3F } };

        var r = ScadBoundaryTransfer.Transfer(model, Nodes, Types);

        Assert.Single(r.Supports);
        Assert.Empty(r.ElementProps);
        Assert.Contains(r.Report, l => l.Contains("дочитайте граничные условия"));
    }

    [Fact]
    public void Beds_ShellsGetC1_BeamsAndC2GoToReport()
    {
        var model = new ScadAnalysisModel
        {
            HasBoundaryV2 = true, HasBeds = true, SchemaType = 5,
            Joints = { new ScadJoint(10, 0, 0b110000) },
            Beds =
            {
                new ScadBed(73, [8.494e6, 0, 5.946e6], [20, 10]),
                new ScadBed(0, [4.1e6, 5.1e7], [21, 99]),
            },
        };
        var types = new Dictionary<string, string>(Types) { ["21"] = "shell" };

        var r = ScadBoundaryTransfer.Transfer(model, Nodes, types);

        Assert.Equal(new FemElementBoundaryProps(null, null, 8.494e6), r.ElementProps["20"]);
        Assert.Equal(new FemElementBoundaryProps(null, null, 4.1e6), r.ElementProps["21"]);
        Assert.Equal(new FemElementBoundaryProps(null, 0b110000, null), r.ElementProps["10"]);
        Assert.Contains("стержней с шарнирами 1 (концов 1), пластин на упругом основании 2", r.Report[0]);
        Assert.Contains(r.Report, l => l.StartsWith("Упругое основание: у 2 пластин заданы C2"));
        Assert.Contains(r.Report, l => l.Contains("упругое основание стержней, КЭ — 1"));
        Assert.Contains(r.Report, l => l.StartsWith("Упругое основание: 1 КЭ нет в сетке"));
        Assert.DoesNotContain(r.Report, l => l.Contains("дочитайте"));
    }

    [Fact]
    public void Joints_SeveralRecordsOfOneElement_MasksMerged()
    {
        var model = new ScadAnalysisModel
        {
            HasBoundaryV2 = true, HasBeds = true, SchemaType = 5,
            Joints = { new ScadJoint(10, 0b010000, 0), new ScadJoint(10, 0b100000, 0b010000) },
        };

        var r = ScadBoundaryTransfer.Transfer(model, Nodes, Types);

        Assert.Equal(new FemElementBoundaryProps(0b110000, 0b010000, null), Assert.Single(r.ElementProps).Value);
        Assert.Contains("стержней с шарнирами 1 (концов 2)", r.Report[0]);
    }

    [Fact]
    public void AttachmentWithoutBeds_AsksToReread()
    {
        var model = new ScadAnalysisModel { HasBoundaryV2 = true, Bounds = { [1] = 0x3F } };

        var r = ScadBoundaryTransfer.Transfer(model, Nodes, Types);

        Assert.Contains(r.Report, l => l.Contains("упругого основания") && l.Contains("дочитайте"));
    }
}
