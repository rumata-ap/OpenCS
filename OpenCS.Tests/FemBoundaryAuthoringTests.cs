using CScore.Fem;
using CScore.Fem.Editing;
using OpenCS.Services;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Служба ГУ схемы: общие значения набора, «разные» поля поверх прежних, проверка сессии, каскад пружин.</summary>
[Collection("Loc language")]
public sealed class FemBoundaryAuthoringTests : IDisposable
{
    public FemBoundaryAuthoringTests() => Loc.SetLanguage("ru-RU");
    public void Dispose() => Loc.SetLanguage(null);

    [Fact]
    public void StiffnessError_NegativeAndSpringOnFixed()
    {
        Assert.Null(FemBoundaryAuthoring.StiffnessError(false, 0));
        Assert.Null(FemBoundaryAuthoring.StiffnessError(false, 5e3));
        Assert.Null(FemBoundaryAuthoring.StiffnessError(true, 0));
        Assert.NotNull(FemBoundaryAuthoring.StiffnessError(false, -1));
        Assert.NotNull(FemBoundaryAuthoring.StiffnessError(false, double.NaN));
        Assert.NotNull(FemBoundaryAuthoring.StiffnessError(true, 1));
    }

    [Fact]
    public void Common_And_Apply_MixedFieldsKeepOwnValues()
    {
        // Узел A: закреплён X, пружина Z = 5; узел B: закреплены X и Y, пружина Z = 7.
        double[] ka = [0, 0, 5, 0, 0, 0], kb = [0, 0, 7, 0, 0, 0];
        var (fix, k) = FemBoundaryAuthoring.Common([(0b001, ka), (0b011, kb)]);
        Assert.Equal(true, fix[0]);
        Assert.Null(fix[1]);
        Assert.Equal(false, fix[2]);
        Assert.Null(k[2]);
        Assert.Equal(0, k[0]);

        // Пользователь добавил пружину UZ = 3, остальное не трогал: у каждого узла его закрепления и Z остаются.
        k[5] = 3;
        var a = FemBoundaryAuthoring.Apply(0b001, ka, fix, k);
        var b = FemBoundaryAuthoring.Apply(0b011, kb, fix, k);
        Assert.Equal(0b001, a.Mask);
        Assert.Equal(0b011, b.Mask);
        Assert.Equal(5, a.K[2]);
        Assert.Equal(7, b.K[2]);
        Assert.Equal(3, a.K[5]);
        Assert.Equal(3, b.K[5]);

        // Закрепили Z у всех — пружина Z обнуляется.
        fix[2] = true;
        Assert.Equal(0, FemBoundaryAuthoring.Apply(0b001, ka, fix, k).K[2]);
    }

    [Fact]
    public void Validate_SpringOnFixedDirection_ErrorWithNodeTag_BlocksSave()
    {
        var session = new FemSchemaEditSession(new FemSchema { Id = 1, Tag = "С", SourceType = "internal" });
        var node = new FemNode { Id = -1, SchemaId = 1, NodeTag = "7", DofMask = 0b100 };
        session.Nodes.Add(node);
        var spring = new FemSpring { SchemaId = 1, TargetKind = FemSpringTargetKinds.Node, NodeTag = "7" };
        spring.SetStiffnesses([0, 0, 1e3, 0, 0, 0]);
        session.NodeSprings.Add(spring);
        var d = Assert.Single(FemBoundaryAuthoring.Validate(session));
        Assert.True(d.IsError);
        Assert.Equal(["7"], d.SourceKeys);
        Assert.Contains("Z", d.Message);

        node.DofMask = 0;
        Assert.Empty(FemBoundaryAuthoring.Validate(session));
    }

    [Fact]
    public void DeleteNodes_TakesSprings_UndoReturnsThem()
    {
        var session = new FemSchemaEditSession(new FemSchema { Id = 1, Tag = "С", SourceType = "internal" });
        var node = new FemNode { Id = -1, SchemaId = 1, NodeTag = "3" };
        session.Nodes.Add(node);
        session.Execute(new SetNodeBoundaryCommand([node], 0, [0, 0, 2e3, 0, 0, 0]));
        Assert.Single(session.NodeSprings);

        session.Execute(new DeleteNodesCommand([node]));
        Assert.Empty(session.NodeSprings);
        session.Undo();
        Assert.Equal(2e3, Assert.Single(session.NodeSprings).Kz);
    }
}
