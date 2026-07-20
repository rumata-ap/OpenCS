using System.Windows.Media.Media3D;
using OpenCS.Views.Helpers;

namespace OpenCS.OpenSees.Tests;

public sealed class FemGridVisualsTests
{
    [Fact]
    public void Apply_enabled_adds_all_available_grid_layers_once()
    {
        var unrelated = new ModelVisual3D();
        var shellEdges = new ModelVisual3D();
        var mesh = new ModelVisual3D();
        var meshNodes = new ModelVisual3D();
        var children = new List<Visual3D> { unrelated, mesh };

        FemGridVisuals.Apply(children, true, shellEdges, mesh, meshNodes);

        Assert.Equal(new Visual3D[] { unrelated, shellEdges, mesh, meshNodes }, children);
    }

    [Fact]
    public void Apply_disabled_removes_grid_layers_but_keeps_unrelated_visuals()
    {
        var unrelated = new ModelVisual3D();
        var shellEdges = new ModelVisual3D();
        var mesh = new ModelVisual3D();
        var meshNodes = new ModelVisual3D();
        var children = new List<Visual3D> { unrelated, shellEdges, mesh, meshNodes };

        FemGridVisuals.Apply(children, false, shellEdges, mesh, meshNodes);

        Assert.Equal(new Visual3D[] { unrelated }, children);
    }

    [Fact]
    public void Apply_enabled_ignores_missing_layers_and_does_not_duplicate_existing_layers()
    {
        var mesh = new ModelVisual3D();
        var children = new List<Visual3D> { mesh };

        FemGridVisuals.Apply(children, true, null, mesh, null);

        Assert.Single(children);
        Assert.Same(mesh, children[0]);
    }
}
