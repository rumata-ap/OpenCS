using CScore;
using CScore.Planar;
using CScore.PlateRebar;
using OpenCS.OpenSees.CScore;
using OpenCS.OpenSees.Model;
using OpenCS.OpenSees.Structural;
using Xunit;

namespace OpenCS.OpenSees.Tests;

public class PlanarMeshSnapshotShellModelAdapterTests
{
    [Fact]
    public void Build_TagsIndicesAndFrame_AreConsistentAcrossElements()
    {
        var snapshot = new PlanarMeshSnapshot
        {
            Id = 1,
            RegionId = 1,
            IsCalculable = true,
            Nodes =
            [
                new(0, 0, 0, 0, 0, 0),
                new(1, 1, 0, 1, 0, 0),
                new(2, 1, 1, 1, 1, 0),
                new(3, 0, 1, 0, 1, 0),
                new(4, 2, 0, 2, 0, 0),
                new(5, 2, 1, 2, 1, 0),
            ],
            Elements =
            [
                new(0, PlanarMeshElementKind.Triangle3, [0, 1, 2]),
                new(1, PlanarMeshElementKind.Quadrangle4, [1, 4, 5, 2]),
            ],
        };
        var section = new PlateSection { H = 0.2, NLayers = 4 };
        var field = new PlateRebarField([], []);

        PlanarMeshShellModelResult result = PlanarMeshSnapshotShellModelAdapter.Build(
            snapshot, Frame3D.Identity, section, field, new ConcreteOnlyResolver());

        Assert.Equal(6, result.Model.Nodes.Count);
        Assert.Equal(2, result.Model.Elements.Count);
        Assert.Equal(1, result.NodeIndexToTag[0]);
        Assert.Equal(1, result.ElementIndexToTag[0]);
        Assert.Equal(2, result.ElementIndexToTag[1]);

        NormalizedShellElement t3 = result.Model.Elements.Single(e => e.Tag == 1);
        Assert.Equal(ShellElementKind.ASDShellT3, t3.Kind);
        Assert.Equal(new[] { 1, 2, 3 }, t3.NodeTags);

        NormalizedShellElement q4 = result.Model.Elements.Single(e => e.Tag == 2);
        Assert.Equal(ShellElementKind.ASDShellQ4, q4.Kind);
        Assert.Equal(new[] { 2, 5, 6, 3 }, q4.NodeTags);

        Assert.All(result.Model.Elements, e => Assert.Equal(ShellFrame.Identity, e.Frame));
        Assert.Single(result.Model.Sections);
        Assert.All(result.Model.Sections, s => Assert.Equal(ShellFrame.Identity, s.Frame));

        // Модель без Stages не самодостаточна для Validate() — добавляется вызывающим.
        var validated = result.Model with
        {
            Stages = [new() { Tag = "s", Loads = [new(1, 0, 0, 0, 0, 0, 0)] }]
        };
        validated.Validate();
    }

    private sealed class ConcreteOnlyResolver : IPlateSectionShellMaterialResolver
    {
        public IReadOnlyList<NativeShellMaterialDefinition> ResolveConcrete(int sourceMaterialId) =>
            [new(1, $"concrete:{sourceMaterialId}", new ElasticIsotropicShellMaterialSpec(30e9, 0.2))];

        public IReadOnlyList<NativeShellMaterialDefinition> ResolveRebar(int sourceMaterialId) =>
            throw new NotSupportedException("Тест не использует армирование.");
    }
}
