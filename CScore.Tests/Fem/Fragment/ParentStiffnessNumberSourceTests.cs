using CScore.Fem;
using CScore.Fem.Fragment;
using CScore.Planar;
using Xunit;

namespace CScore.Tests.Fem.Fragment;

public sealed class ParentStiffnessNumberSourceTests
{
    static PlanarVector3 V(double x, double y, double z) => new(x, y, z);

    /// <summary>Вложенный источник: запоминает номер жёсткости, с которым его спросили.</summary>
    sealed class RecordingSource : IFemElementStiffnessSource
    {
        public List<int?> Seen { get; } = [];

        public FemShellStiffness? Shell(FemElement element) { Seen.Add(element.StiffnessNum); return null; }

        public FemBarStiffness? Bar(FemElement element)
        {
            Seen.Add(element.StiffnessNum);
            return new FemBarStiffness(1, 1, 1, 1, 1, 1);
        }

        public double? UnitWeight(FemElement element) { Seen.Add(element.StiffnessNum); return null; }

        public double? BarArea(FemElement element) => null;
    }

    [Fact]
    public void FragmentBar_TakesStiffnessNumberOfParentReference_OriginalUntouched()
    {
        // Опорный КЭ родителя — стержень на той же оси с номером жёсткости 7.
        FragmentRefElement[] refs = [new("201", "beam", [V(5, 0, 0), V(5, 0, 3)], stiffnessNum: 7)];
        var nodes = new Dictionary<string, FemMeshNode>(StringComparer.Ordinal)
        {
            ["10"] = new() { NodeTag = "10", X = 5, Y = 0, Z = 0 },
            ["11"] = new() { NodeTag = "11", X = 5, Y = 0, Z = 1 },
        };
        var bar = new FemElement { ElemTag = "3", ElemType = "beam", NodeIdsJson = "[10,11]" };
        var map = FragmentParentMap.Build([bar], nodes, new ParentMeshMapper(refs));
        var inner = new RecordingSource();
        var source = new ParentStiffnessNumberSource(inner, map);

        Assert.NotNull(source.Bar(bar));
        Assert.Equal(7, Assert.Single(inner.Seen));
        Assert.Null(bar.StiffnessNum);
    }

    [Fact]
    public void ElementWithOwnNumberOrWithoutPair_PassedAsIs()
    {
        FragmentRefElement[] refs = [new("201", "beam", [V(5, 0, 0), V(5, 0, 3)], stiffnessNum: 7)];
        var nodes = new Dictionary<string, FemMeshNode>(StringComparer.Ordinal)
        {
            ["10"] = new() { NodeTag = "10", X = 5, Y = 0, Z = 0 },
            ["11"] = new() { NodeTag = "11", X = 5, Y = 0, Z = 1 },
            ["20"] = new() { NodeTag = "20", X = -9, Y = -9, Z = 0 },
            ["21"] = new() { NodeTag = "21", X = -9, Y = -9, Z = 1 },
        };
        var own = new FemElement { ElemTag = "4", ElemType = "beam", NodeIdsJson = "[10,11]", StiffnessNum = 5 };
        var orphan = new FemElement { ElemTag = "5", ElemType = "beam", NodeIdsJson = "[20,21]" };
        var map = FragmentParentMap.Build([own, orphan], nodes, new ParentMeshMapper(refs));
        var inner = new RecordingSource();
        var source = new ParentStiffnessNumberSource(inner, map);

        source.Bar(own);
        source.UnitWeight(orphan);
        Assert.Equal([5, null], inner.Seen);
    }
}
