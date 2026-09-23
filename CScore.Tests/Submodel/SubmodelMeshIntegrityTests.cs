using CScore.Fem;
using CScore.Submodel;
using Xunit;

namespace CScore.Tests.Submodel;

public sealed class SubmodelMeshIntegrityTests
{
    static (SubmodelExtraction Extraction, List<FemMeshNode> Nodes, List<FemElement> Elements) Beam()
    {
        var (extraction, nodes, elements) = PortalFrameReference.ExtractWithMesh(PortalFrameReference.Model(), ["12", "13", "14"]);
        return (extraction, nodes.ToList(), elements.ToList());
    }

    static void AssertStale(IReadOnlyList<FemValidationDiagnostic> diagnostics, string key)
    {
        Assert.NotEmpty(diagnostics);
        Assert.All(diagnostics, d => Assert.Equal(SubmodelMaterializationDiagnostics.MeshStale, d.Code));
        Assert.All(diagnostics, d => Assert.True(d.IsError));
        Assert.Contains(diagnostics, d => d.SourceKeys!.Contains(key));
    }

    [Fact]
    public void OriginalSnapshot_IsIntact()
    {
        var (extraction, nodes, elements) = Beam();

        Assert.Empty(SubmodelMeshIntegrity.Check(extraction, nodes, elements, checkIds: false));
    }

    [Fact]
    public void MissingElement_IsStale()
    {
        var (extraction, nodes, elements) = Beam();
        elements.RemoveAll(e => e.ElemTag == "13");

        AssertStale(SubmodelMeshIntegrity.Check(extraction, nodes, elements, false), "13");
    }

    [Fact]
    public void MissingNode_IsStale()
    {
        var (extraction, nodes, elements) = Beam();
        nodes.RemoveAll(n => n.NodeTag == "3");

        AssertStale(SubmodelMeshIntegrity.Check(extraction, nodes, elements, false), "3");
    }

    [Fact]
    public void DuplicateNodeTag_IsStale()
    {
        var (extraction, nodes, elements) = Beam();
        nodes.Add(new FemMeshNode { NodeTag = "3", X = 2, Z = 3 });

        AssertStale(SubmodelMeshIntegrity.Check(extraction, nodes, elements, false), "3");
    }

    [Fact]
    public void BrokenNodeIdsJson_IsStale()
    {
        var (extraction, nodes, elements) = Beam();
        elements.Single(e => e.ElemTag == "13").NodeIdsJson = "[3";

        AssertStale(SubmodelMeshIntegrity.Check(extraction, nodes, elements, false), "13");
    }

    [Fact]
    public void ElementWithForeignNode_IsStale()
    {
        var (extraction, nodes, elements) = Beam();
        elements.Single(e => e.ElemTag == "13").NodeIdsJson = "[3,99]";

        AssertStale(SubmodelMeshIntegrity.Check(extraction, nodes, elements, false), "13");
    }

    [Fact]
    public void MovedNode_IsStale()
    {
        var (extraction, nodes, elements) = Beam();
        nodes.Single(n => n.NodeTag == "4").X += 0.01;

        AssertStale(SubmodelMeshIntegrity.Check(extraction, nodes, elements, false), "4");
    }

    [Fact]
    public void ExtraNode_IsStale()
    {
        var (extraction, nodes, elements) = Beam();
        nodes.Add(new FemMeshNode { NodeTag = "77", X = 1, Z = 3 });

        AssertStale(SubmodelMeshIntegrity.Check(extraction, nodes, elements, false), "77");
    }

    [Fact]
    public void RecreatedRows_AreStaleOnlyWhenIdsChecked()
    {
        var (extraction, nodes, elements) = Beam();
        foreach (var node in nodes) node.Id = extraction.Nodes.Single(n => n.SubmodelNodeTag == node.NodeTag).SubmodelNodeId;
        foreach (var element in elements) element.Id = extraction.Segments.Single(s => s.SubmodelElementTag == element.ElemTag).SubmodelElementId;
        Assert.Empty(SubmodelMeshIntegrity.Check(extraction, nodes, elements, checkIds: true));

        // Пересоздание сетки тем же снимком: теги и координаты те же, id — новые.
        foreach (var node in nodes) node.Id += 1000;
        foreach (var element in elements) element.Id += 1000;

        Assert.Empty(SubmodelMeshIntegrity.Check(extraction, nodes, elements, checkIds: false));
        AssertStale(SubmodelMeshIntegrity.Check(extraction, nodes, elements, checkIds: true), "13");
    }
}
