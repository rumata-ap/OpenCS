using CScore.Fem;
using CScore.Fem.Fragment;
using CScore.Planar;
using Xunit;

namespace CScore.Tests.Fem.Fragment;

public sealed class ParentMappedRebarSourcesTests
{
    static PlanarVector3 V(double x, double y, double z) => new(x, y, z);

    /// <summary>Пластина фрагмента «7» над КЭ родителя «101» (ТЗА «1 2», толщина 0,25), стержень «8» — часть КЭ «201»,
    /// пластина «9» — вне опорной сетки.</summary>
    static (FragmentParentMap Map, FemCheckScopeElement Shell, FemCheckScopeElement Bar, FemCheckScopeElement Lost) Setup()
    {
        FragmentRefElement[] refs =
        [
            new("101", "shell", [V(0, 0, 0), V(1, 0, 0), V(0, 1, 0), V(1, 1, 0)], reinforcementTypeIds: "1 2", thicknessM: 0.25),
            new("201", "beam", [V(5, 0, 0), V(5, 0, 3)], stiffnessNum: 4),
        ];
        var nodes = new[]
        {
            new FemMeshNode { NodeTag = "1", X = 0 }, new FemMeshNode { NodeTag = "2", X = 1 }, new FemMeshNode { NodeTag = "3", Y = 1 },
            new FemMeshNode { NodeTag = "4", X = 5 }, new FemMeshNode { NodeTag = "5", X = 5, Z = 1 },
            new FemMeshNode { NodeTag = "6", X = 9 }, new FemMeshNode { NodeTag = "7", X = 10 }, new FemMeshNode { NodeTag = "8", X = 9, Y = 1 },
        }.ToDictionary(n => n.NodeTag, StringComparer.Ordinal);
        var shell = new FemElement { ElemTag = "7", ElemType = "shell", NodeIdsJson = "[1,2,3]" };
        var bar = new FemElement { ElemTag = "8", ElemType = "beam", NodeIdsJson = "[4,5]", CrossSectionId = 12 };
        var lost = new FemElement { ElemTag = "9", ElemType = "shell", NodeIdsJson = "[6,7,8]", ThicknessM = 0.3 };
        var map = FragmentParentMap.Build([shell, bar, lost], nodes, new ParentMeshMapper(refs));
        var member = new FemMember { ElemTag = "S1" };
        return (map, new(7, shell, member), new(8, bar, null), new(9, lost, member));
    }

    sealed class PlateProbe : IPlateElementSectionSource
    {
        public List<FemCheckScopeElement> Seen { get; } = [];
        public string Key => FemCheckRebarSource.Selected;
        public PlateElementSection Resolve(FemCheckScopeElement element)
        {
            Seen.Add(element);
            return new PlateElementSection(null, "ASP", "", "проба");
        }
        public IReadOnlyList<string> Warnings(IReadOnlyList<FemCheckScopeElement> elements, Material? concrete, Material? rebar) =>
            elements.Select(e => e.ElemNum?.ToString() ?? "?").ToList();
    }

    sealed class BarProbe : IBarElementSectionSource
    {
        public List<(FemCheckScopeElement Element, int? Section)> Seen { get; } = [];
        public string Key => FemCheckRebarSource.Assigned;
        public bool PerSection => true;
        public string? MissingReason(FemCheckScopeElement element) => element.ElemNum == 201 ? null : "чужой";
        public BarElementSection Resolve(FemCheckScopeElement element, int? sectionNum)
        {
            Seen.Add((element, sectionNum));
            return BarElementSection.Missing("проба");
        }
    }

    [Fact]
    public void Plate_ParentNumberRebarTypesAndThickness()
    {
        var (map, shell, _, lost) = Setup();
        var probe = new PlateProbe();
        var source = new ParentMappedPlateSectionSource(probe, map);

        Assert.Equal(FemCheckRebarSource.Selected, source.Key);
        source.Resolve(shell);
        var seen = Assert.Single(probe.Seen);
        Assert.Equal((101, "101", "1 2", 0.25, "S1"),
            (seen.ElemNum!.Value, seen.Element.ElemTag, seen.Element.ReinforcementTypeIds, seen.Element.ThicknessM!.Value, seen.Member!.ElemTag));
        Assert.Equal("7", shell.Element.ElemTag);
        Assert.Null(shell.Element.ReinforcementTypeIds);

        var missing = source.Resolve(lost);
        Assert.Null(missing.Section);
        Assert.Equal(ParentMappedElement.UnmappedReason, missing.Reason);
        Assert.Single(probe.Seen);

        Assert.Equal(["101"], source.Warnings([shell, lost], null, null));
    }

    [Fact]
    public void Plate_OwnThicknessWins()
    {
        var (map, shell, _, _) = Setup();
        shell.Element.ThicknessM = 0.18;
        var probe = new PlateProbe();
        new ParentMappedPlateSectionSource(probe, map).Resolve(shell);
        Assert.Equal(0.18, probe.Seen[0].Element.ThicknessM);
    }

    [Fact]
    public void Bar_ParentElementWholeLength()
    {
        var (map, _, bar, lost) = Setup();
        var probe = new BarProbe();
        var source = new ParentMappedBarSectionSource(probe, map);

        Assert.False(source.PerSection);
        Assert.Null(source.MissingReason(bar));
        Assert.Equal(ParentMappedElement.UnmappedReason, source.MissingReason(lost));
        source.Resolve(bar, 3);
        var (seen, section) = Assert.Single(probe.Seen);
        Assert.Equal((201, 4, 12), (seen.ElemNum!.Value, seen.Element.StiffnessNum!.Value, seen.Element.CrossSectionId!.Value));
        Assert.Null(section);
        Assert.Equal(ParentMappedElement.UnmappedReason, source.Resolve(lost, 1).Reason);
    }
}
