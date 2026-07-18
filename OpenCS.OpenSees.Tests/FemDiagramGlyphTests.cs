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
}
