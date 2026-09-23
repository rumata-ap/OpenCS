using CScore.Fem;
using CScore.Planar;
using CScore.Submodel;
using Xunit;
using static CScore.Tests.Submodel.BoundaryTestModels;

namespace CScore.Tests.Submodel;

public sealed class BoundaryActionProvidersTests
{
    static readonly BeamEndForces Sample = new(new Dof6(10, 2, 3, 4, 5, 6), new Dof6(-10, -2, -3, -4, -5, -6));

    static void AssertDof(Dof6 expected, Dof6 actual)
    {
        for (int k = 0; k < 6; k++) Assert.Equal(expected[k], actual[k], 12);
    }

    [Fact]
    public void ToGlobal_HorizontalBar_LocalYIsGlobalZ()
    {
        // x = (1,0,0), y = (0,0,1), z = x × y = (0,−1,0).
        var global = EndForceTransform.ToGlobal(Sample, false, new PlanarVector3(0, 0, 0), new PlanarVector3(2, 0, 0), 0);
        AssertDof(new Dof6(10, -3, 2, 4, -6, 5), global);
    }

    [Fact]
    public void ToGlobal_VerticalBar_LocalYIsGlobalX()
    {
        // x = (0,0,1), y = (1,0,0), z = (0,1,0).
        var global = EndForceTransform.ToGlobal(Sample, false, new PlanarVector3(0, 0, 0), new PlanarVector3(0, 0, 3), 0);
        AssertDof(new Dof6(2, 3, 10, 5, 6, 4), global);
    }

    [Fact]
    public void ToGlobal_BetaRotatesSectionAxes_AndPicksEndJ()
    {
        // β = 90° вокруг x = (1,0,0): y = (0,−1,0), z = (0,0,−1). Конец j: значения со знаком минус.
        var global = EndForceTransform.ToGlobal(Sample, true, new PlanarVector3(0, 0, 0), new PlanarVector3(2, 0, 0), 90);
        AssertDof(new Dof6(-10, 2, 3, -4, 5, 6), global);
    }

    static (EndActions Actions, List<FemValidationDiagnostic> Diagnostics) CollectStart(
        Dictionary<string, BeamEndForces> forces, IReadOnlyList<BoundaryNodalLoad>? nodal = null,
        Action<ParentModel>? edit = null)
    {
        var parent = Beam();
        edit?.Invoke(parent);
        var diagnostics = new List<FemValidationDiagnostic>();
        var chain = SubmodelChainTopology.Build(Extraction(parent, MiddleChain), parent.MeshElements, diagnostics)!;
        var result = new DictionaryParentLinearResult(true,
            new Dictionary<string, Dof6> { ["102"] = new(0, 0, -0.01, 0, 0.002, 0) },
            new Dictionary<string, Dof6>(), forces);
        var actions = BoundaryActionProviders.Collect(true, chain, parent.Members, parent.MeshNodes,
            parent.MeshElements, nodal ?? [], result, diagnostics);
        return (actions, diagnostics);
    }

    [Fact]
    public void Collect_DiscardedNeighbourGivesMinusItsResistance_PlusBoundaryNodalLoad()
    {
        var neighbour = new BeamEndForces(Dof6.Zero, new Dof6(1, 2, 3, 4, 5, 6));
        var selected = new BeamEndForces(new Dof6(7, 8, 9, 10, 11, 12), Dof6.Zero);
        var nodal = new BoundaryNodalLoad("102", new Dof6(0, 0, -100, 0, 0, 0), new NodalLoadSource("node_load", 3, "3"));

        var (actions, diagnostics) = CollectStart(new() { ["201"] = neighbour, ["202"] = selected }, [nodal]);

        // Стержни вдоль X: глобальный вектор = (N, −Qz, Qy, Mx, −Mz, My).
        var neighbourGlobal = new Dof6(1, -3, 2, 4, -6, 5);
        AssertDof(-neighbourGlobal + nodal.Load, actions.BoundaryVector!);
        AssertDof(new Dof6(7, -9, 8, 10, -12, 11), actions.RetainedResistance!);
        Assert.Equal("102", actions.ParentNodeTag);
        Assert.Equal(2, actions.Contributions.Count);
        Assert.NotNull(actions.Displacement);
        Assert.Null(actions.Reaction);
        Assert.DoesNotContain(diagnostics, d => d.IsError);
    }

    [Fact]
    public void Collect_MissingNeighbourForces_MakesForceUndefined()
    {
        var (actions, diagnostics) = CollectStart(new() { ["202"] = Sample });

        Assert.Null(actions.BoundaryVector);
        Assert.Equal(new[] { "201" }, actions.MissingEndForceTags);
        Assert.Contains(diagnostics, d => d.Code == BoundaryScenarioDiagnostics.MissingEndForces && !d.IsError);
    }

    [Fact]
    public void Collect_ShellAtEnd_IsUnsupportedJunction()
    {
        var (actions, diagnostics) = CollectStart(
            new() { ["201"] = Sample, ["202"] = Sample },
            edit: p => p.MeshElements.Add(new FemElement { ElemTag = "900", ElemType = "shell", NodeIdsJson = "[102,103,104]" }));

        Assert.Null(actions.BoundaryVector);
        Assert.Equal(new[] { "900" }, actions.UnsupportedJunctionTags);
        Assert.Contains(diagnostics, d => d.Code == BoundaryScenarioDiagnostics.UnsupportedJunction);
    }
}
