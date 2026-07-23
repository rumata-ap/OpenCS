using CScore.Fem;
using CScore.Fem.Editing;
using OpenCS.Utilites;
using OpenCS.ViewModels;
using System.Windows.Media.Media3D;

namespace OpenCS.OpenSees.Tests;

public sealed class FemDiagramGlyphTests
{
    [Fact]
    public void ViewModel_SelectDiagramLoadCase_RendersAppliedNodeLoad()
    {
        string path = Path.Combine(Path.GetTempPath(), $"opencs-fem-{Guid.NewGuid():N}.db");
        try
        {
            using var db = new DatabaseService(path);
            var schema = new FemSchema { Id = 1 };
            var loadCase = new FemLoadCase { Id = 2, SchemaId = schema.Id, Tag = "G" };
            var session = new FemSchemaEditSession(schema);
            session.Nodes.Add(new FemNode { Id = 1, SchemaId = schema.Id, NodeTag = "1" });
            session.LoadCases.Add(loadCase);
            session.NodeLoads.Add(new FemNodeLoad { NodeId = 1, LoadCaseId = loadCase.Id, Fz = -10 });
            var viewModel = new Fem3DVM(schema, db) { EditMode = true };

            viewModel.LoadFromSession(session);
            viewModel.SelectDiagramLoadCase(loadCase);

            Assert.Contains(viewModel.DiagramGlyphs, glyph =>
                glyph.Kind == FemDiagramGlyphKind.Force && glyph.NodeId == 1 && glyph.Component == "Fz");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Factory_EmitsSeparateTranslationAndRotationSupportGlyphs()
    {
        var glyphs = FemDiagramGlyphFactory.Create(
            [new FemNode { Id = 1, DofMask = 1 | 32 }], [], showSupports: true, showLoads: false);

        Assert.Contains(glyphs, glyph => glyph.Kind == FemDiagramGlyphKind.TranslationSupport && glyph.Axis == new Vector3D(1, 0, 0));
        Assert.Contains(glyphs, glyph => glyph.Kind == FemDiagramGlyphKind.RotationSupport && glyph.Axis == new Vector3D(0, 0, 1));
        Assert.DoesNotContain(glyphs, glyph => !glyph.IsSupport);
    }

    [Fact]
    public void ViewModel_SelectDiagramLoadCase_RendersMemberLoadGlyph()
    {
        string path = Path.Combine(Path.GetTempPath(), $"opencs-fem-member-glyph-{Guid.NewGuid():N}.db");
        try
        {
            using var db = new DatabaseService(path);
            var schema = new FemSchema { Id = 1 };
            var loadCase = new FemLoadCase { Id = 2, SchemaId = schema.Id, Tag = "G" };
            var session = new FemSchemaEditSession(schema);
            session.Nodes.Add(new FemNode { Id = 1, SchemaId = schema.Id, NodeTag = "1", X = 0 });
            session.Nodes.Add(new FemNode { Id = 2, SchemaId = schema.Id, NodeTag = "2", X = 10 });
            session.Members.Add(new FemMember { Id = 10, SchemaId = schema.Id, ElemTag = "10", NodeIdsJson = "[1,2]" });
            session.LoadCases.Add(loadCase);
            session.MemberLoads.Add(new FemMemberLoad
            {
                MemberId = 10, LoadCaseId = loadCase.Id, CoordinateSystem = "global",
                DistributionType = "uniform", QzStart = -100
            });
            var viewModel = new Fem3DVM(schema, db) { EditMode = true };

            viewModel.LoadFromSession(session);
            viewModel.SelectDiagramLoadCase(loadCase);

            var glyph = Assert.Single(viewModel.MemberLoadGlyphs);
            Assert.Equal("10", glyph.MemberTag);
            Assert.Equal(0, glyph.Start.X, 8);
            Assert.Equal(10, glyph.End.X, 8);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Factory_UsesNegativeSignForNegativeForceAndHidesLayersIndependently()
    {
        var load = new FemResolvedNodeLoad(1, 0, 0, -4, 0, 2, 0);

        var loadsOnly = FemDiagramGlyphFactory.Create([], [load], showSupports: false, showLoads: true);
        var fz = Assert.Single(loadsOnly, glyph => glyph.Kind == FemDiagramGlyphKind.Force);
        Assert.Equal(new Vector3D(0, 0, 1), fz.Axis);
        Assert.Equal(-1, fz.Sign);
        Assert.Contains("Fz", fz.Label);

        var hidden = FemDiagramGlyphFactory.Create([], [load], showSupports: false, showLoads: false);
        Assert.Empty(hidden);
    }

    [Fact]
    public void MemberLoadFactory_UsesPartialSpanAndLocalAxisDirection()
    {
        var members = new[]
        {
            new FemMember { Id = 10, ElemTag = "10", ElemType = "beam", NodeIdsJson = "[1,2]" }
        };
        var nodes = new[]
        {
            new FemNode { Id = 1, NodeTag = "1", X = 0 },
            new FemNode { Id = 2, NodeTag = "2", X = 10 }
        };
        var loads = new[]
        {
            new FemMemberLoad
            {
                MemberId = 10, CoordinateSystem = "local", DistributionType = "trapezoidal",
                StartOffsetM = 2, EndOffsetM = 1, QyStart = -100, QyEnd = -300
            }
        };

        var glyph = Assert.Single(FemMemberLoadGlyphFactory.Create(members, nodes, loads));

        Assert.Equal(2, glyph.Start.X, 8);
        Assert.Equal(9, glyph.End.X, 8);
        Assert.Equal(0, glyph.LoadAtStart.X, 8);
        Assert.Equal(-100, glyph.LoadAtStart.Z, 8);
        Assert.Equal(-300, glyph.LoadAtEnd.Z, 8);
    }

    [Fact]
    public void Create_PointLoadProducesArrowGlyphAtPosition()
    {
        var members = new List<FemMember> { new() { Id = 1, ElemTag = "10", ElemType = "beam", NodeIdsJson = "[1,2]" } };
        var nodes = new List<FemNode>
        {
            new() { NodeTag = "1", X = 0, Y = 0, Z = 0 },
            new() { NodeTag = "2", X = 10, Y = 0, Z = 0 }
        };
        var loads = new List<FemMemberLoad>
        {
            new() { MemberId = 1, DistributionType = "point", CoordinateSystem = "global",
                    StartOffsetM = 4, QyStart = -1000 }
        };

        var glyphs = FemMemberLoadGlyphFactory.Create(members, nodes, loads);

        var glyph = Assert.Single(glyphs);
        Assert.Equal(glyph.Start, glyph.End);
        Assert.Equal(4, glyph.Start.X, 8);
        Assert.Equal(glyph.LoadAtStart, glyph.LoadAtEnd);
        Assert.Equal(-1000, glyph.LoadAtStart.Y, 8);
    }
}
