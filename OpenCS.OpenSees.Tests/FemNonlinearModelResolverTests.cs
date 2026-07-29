using System.Text.Json;
using CScore;
using CScore.Fem;
using OpenCS.OpenSees.CScore;
using OpenCS.OpenSees.Tests.Fixtures;
using Xunit;

namespace OpenCS.OpenSees.Tests;

public class FemNonlinearModelResolverTests
{
    static FemNonlinearAnalysisOptions Options() => new(
        GeomTransfKind: "Linear",
        RefinementDivisions: 10, Tolerance: 1e-6, MaxIterations: 50, IntegrationPoints: 5);

    // Конструктивная консоль: узел 1 (заделка, dofMask=63) — узел 2 (свободен), 1 стержень, сечение #5.
    static (List<FemMeshNode>, List<FemElement>, List<FemNode>, List<FemMember>, List<FemNodeLoad>) Console(double gj = 1e6)
    {
        var meshNodes = new List<FemMeshNode>
        {
            new() { Id = 10, NodeTag = "1", X = 0, Y = 0, Z = 0, SourceNodeTag = "1", SourceMemberTag = "1" },
            new() { Id = 11, NodeTag = "2", X = 3, Y = 0, Z = 0, SourceNodeTag = "2", SourceMemberTag = "1" },
        };
        var meshElems = new List<FemElement>
        {
            new() { Id = 20, ElemTag = "1", NodeIdsJson = "[1,2]", SourceMemberTag = "1",
                    CrossSectionId = 5, GjStrategy = "manual", GjManualValue = gj },
        };
        var srcNodes = new List<FemNode>
        {
            new() { Id = 1, NodeTag = "1", X = 0, Y = 0, Z = 0, DofMask = 63 },
            new() { Id = 2, NodeTag = "2", X = 3, Y = 0, Z = 0, DofMask = 0 },
        };
        var srcMembers = new List<FemMember>
        {
            new() { Id = 1, ElemTag = "1", ElemType = "beam", NodeIdsJson = "[1,2]",
                    CrossSectionId = 5, GjStrategy = "manual", GjManualValue = gj },
        };
        var loads = new List<FemNodeLoad>
        {
            new() { Id = 1, LoadCaseId = 1, NodeId = 2, Fz = -1000 },
        };
        return (meshNodes, meshElems, srcNodes, srcMembers, loads);
    }

    static Dictionary<int, CrossSection> Sections(CrossSection section) => new() { [5] = section };

    [Fact]
    public void Resolve_ValidConsole_BuildsModelWithFiberSection()
    {
        var (mn, me, sn, sm, ld) = Console();
        var (section, concrete, steel) = CrossSectionFixtures.RectangularSection();
        var r = new FemNonlinearModelResolver().Resolve(
            mn, me, sn, sm,
            [new FemNonlinearStageInput("Стадия 1", ld, LoadFactorStep: 0.1, MaxLoadFactor: 1.0)],
            Sections(section),
            CrossSectionFixtures.Materials(concrete, steel), customDiagramPool: null, CalcType.C, Options());

        Assert.True(r.Ok, string.Join("; ", r.Errors));
        Assert.Equal(2, r.Model!.Nodes.Count);
        Assert.Single(r.Model.Elements);
        Assert.Single(r.Model.Sections);

        var e = r.Model.Elements[0];
        Assert.Equal(1, e.SectionTag);
        Assert.Equal(5, e.NumIntegrationPoints);
        Assert.Equal((0d, -1d, 0d), e.Vecxz);
        Assert.Equal(1e6, r.Model.Sections[e.SectionTag].GJ, 3);

        var stage = Assert.Single(r.Model.Stages);
        var load = Assert.Single(stage.Loads);
        Assert.Equal(2, load.NodeTag);
        Assert.Equal(-1000, load.Fz, 6);
        Assert.Equal(0.1, stage.LoadFactorStep, 12);
        Assert.Equal(1.0, stage.MaxLoadFactor, 12);

        Assert.Equal("Linear", r.Model.GeomTransfKind);
    }

    [Fact]
    public void Resolve_TwoMembersSameSectionSameGj_ShareOneFiberSection()
    {
        var (mn, me, sn, sm, ld) = Console();
        // Второй стержень между теми же двумя узлами, тот же CrossSectionId и тот же GJ.
        me.Add(new FemElement { Id = 21, ElemTag = "2", NodeIdsJson = "[1,2]", SourceMemberTag = "2",
                                 CrossSectionId = 5, GjStrategy = "manual", GjManualValue = 1e6 });
        sm.Add(new FemMember { Id = 2, ElemTag = "2", ElemType = "beam", NodeIdsJson = "[1,2]",
                                CrossSectionId = 5, GjStrategy = "manual", GjManualValue = 1e6 });

        var (section, concrete, steel) = CrossSectionFixtures.RectangularSection();
        var r = new FemNonlinearModelResolver().Resolve(
            mn, me, sn, sm, [new FemNonlinearStageInput("Стадия 1", ld)], Sections(section),
            CrossSectionFixtures.Materials(concrete, steel), customDiagramPool: null, CalcType.C, Options());

        Assert.True(r.Ok, string.Join("; ", r.Errors));
        Assert.Equal(2, r.Model!.Elements.Count);
        Assert.Single(r.Model.Sections);   // одно сечение на оба стержня
        Assert.Equal(r.Model.Elements[0].SectionTag, r.Model.Elements[1].SectionTag);
    }

    [Fact]
    public void Resolve_TwoMembersSameSectionDifferentGj_BuildTwoFiberSections()
    {
        var (mn, me, sn, sm, ld) = Console(gj: 1e6);
        me.Add(new FemElement { Id = 21, ElemTag = "2", NodeIdsJson = "[1,2]", SourceMemberTag = "2",
                                 CrossSectionId = 5, GjStrategy = "manual", GjManualValue = 2e6 });   // другой GJ
        sm.Add(new FemMember { Id = 2, ElemTag = "2", ElemType = "beam", NodeIdsJson = "[1,2]",
                                CrossSectionId = 5, GjStrategy = "manual", GjManualValue = 2e6 });

        var (section, concrete, steel) = CrossSectionFixtures.RectangularSection();
        var r = new FemNonlinearModelResolver().Resolve(
            mn, me, sn, sm, [new FemNonlinearStageInput("Стадия 1", ld)], Sections(section),
            CrossSectionFixtures.Materials(concrete, steel), customDiagramPool: null, CalcType.C, Options());

        Assert.True(r.Ok, string.Join("; ", r.Errors));
        Assert.Equal(2, r.Model!.Sections.Count);   // разные GJ → разные fiber-секции
        Assert.NotEqual(r.Model.Elements[0].SectionTag, r.Model.Elements[1].SectionTag);

        // Материалы обеих секций должны иметь непересекающиеся теги (глобальная уникальность).
        var tags1 = r.Model.Sections[r.Model.Elements[0].SectionTag].Materials.Select(m => m.Tag).ToHashSet();
        var tags2 = r.Model.Sections[r.Model.Elements[1].SectionTag].Materials.Select(m => m.Tag).ToHashSet();
        Assert.Empty(tags1.Intersect(tags2));
    }

    [Fact]
    public void Resolve_MissingSection_ReportsError()
    {
        var (mn, me, sn, sm, ld) = Console();
        var r = new FemNonlinearModelResolver().Resolve(
            mn, me, sn, sm, [new FemNonlinearStageInput("Стадия 1", ld)], new Dictionary<int, CrossSection>(),
            new Dictionary<int, Material>(), customDiagramPool: null, CalcType.C, Options());
        Assert.False(r.Ok);
        Assert.Contains(r.Errors, x => x.Contains("готов", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Resolve_ManualGjMissingValue_ReportsError()
    {
        var (mn, me, sn, sm, ld) = Console();
        sm[0].GjManualValue = null;
        var (section, concrete, steel) = CrossSectionFixtures.RectangularSection();
        var r = new FemNonlinearModelResolver().Resolve(
            mn, me, sn, sm, [new FemNonlinearStageInput("Стадия 1", ld)], Sections(section),
            CrossSectionFixtures.Materials(concrete, steel), customDiagramPool: null, CalcType.C, Options());
        Assert.False(r.Ok);
        Assert.Contains(r.Errors, x => x.Contains("GJ", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Resolve_SaintVenantGj_ReportsDeferredError()
    {
        var (mn, me, sn, sm, ld) = Console();
        sm[0].GjStrategy = "saint_venant";
        var (section, concrete, steel) = CrossSectionFixtures.RectangularSection();
        var r = new FemNonlinearModelResolver().Resolve(
            mn, me, sn, sm, [new FemNonlinearStageInput("Стадия 1", ld)], Sections(section),
            CrossSectionFixtures.Materials(concrete, steel), customDiagramPool: null, CalcType.C, Options());
        Assert.False(r.Ok);
        Assert.Contains(r.Errors, x => x.Contains("отложен", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Resolve_SectionWithoutFibers_ReportsError()
    {
        var (mn, me, sn, sm, ld) = Console();
        var (section, concrete, steel) = CrossSectionFixtures.RectangularSection();
        section.Areas[0].Fibers = [];
        var r = new FemNonlinearModelResolver().Resolve(
            mn, me, sn, sm, [new FemNonlinearStageInput("Стадия 1", ld)], Sections(section),
            CrossSectionFixtures.Materials(concrete, steel), customDiagramPool: null, CalcType.C, Options());
        Assert.False(r.Ok);
    }

    [Fact]
    public void Resolve_TwoStages_BuildsModelWithTwoStagesInOrder()
    {
        var (mn, me, sn, sm, ld) = Console();
        var secondStageLoads = new List<FemNodeLoad> { new() { Id = 2, LoadCaseId = 2, NodeId = 2, Fx = 500 } };
        var (section, concrete, steel) = CrossSectionFixtures.RectangularSection();

        var r = new FemNonlinearModelResolver().Resolve(
            mn, me, sn, sm,
            [
                new FemNonlinearStageInput("Сжатие", ld, LoadFactorStep: 0.2, MaxLoadFactor: 2.0),
                new FemNonlinearStageInput("Изгиб", secondStageLoads, LoadFactorStep: 0.05, MaxLoadFactor: 5.0)
            ],
            Sections(section), CrossSectionFixtures.Materials(concrete, steel),
            customDiagramPool: null, CalcType.C, Options());

        Assert.True(r.Ok, string.Join("; ", r.Errors));
        Assert.Equal(2, r.Model!.Stages.Count);
        Assert.Equal("Сжатие", r.Model.Stages[0].Tag);
        Assert.Equal("Изгиб", r.Model.Stages[1].Tag);
        Assert.Equal(-1000, r.Model.Stages[0].Loads.Single().Fz, 6);
        Assert.Equal(500, r.Model.Stages[1].Loads.Single().Fx, 6);

        // Разные Шаг/Предел λ по стадиям — резолвер должен пробросить их независимо, а не
        // разделить общие значения из options.
        Assert.Equal(0.2, r.Model.Stages[0].LoadFactorStep, 12);
        Assert.Equal(2.0, r.Model.Stages[0].MaxLoadFactor, 12);
        Assert.Equal(0.05, r.Model.Stages[1].LoadFactorStep, 12);
        Assert.Equal(5.0, r.Model.Stages[1].MaxLoadFactor, 12);
    }
}
