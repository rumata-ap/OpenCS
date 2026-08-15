using System.Text.Json;
using Xunit;

namespace CScore.Tests;

/// <summary>Тесты результата действий преднапряжения по группам точечных фибр.</summary>
public sealed class PrestressActionsTests
{
    [Fact]
    public void PrestressActions_UsesConcretePropertiesForceAndOpenCsTwoAxisMoments()
    {
        var section = Section(
            Group(17, "strand", sigSp: 1500, gammaSp: 1,
                (0.0004, 0.2, 0.1),
                (0.0004, 0.2, 0.1)));

        var result = section.PrestressActions(new XY(0, 0));

        Assert.Equal(1200.0, result.Nominal.N, 10);
        Assert.Equal(1200.0, result.Effective.N, 10);
        Assert.Equal(120.0, result.Nominal.Mx, 10);
        Assert.Equal(240.0, result.Nominal.My, 10);
        Assert.Single(result.Groups);
    }

    [Fact]
    public void PrestressActions_SeparatesNominalAndEffectiveByGammaSp()
    {
        var section = Section(
            Group(17, "strand", sigSp: 1500, gammaSp: 0.95,
                (0.0004, 0.2, 0.1),
                (0.0004, 0.2, 0.1)));

        var result = section.PrestressActions(new XY(0, 0));

        Assert.Equal(1200.0, result.Nominal.N, 10);
        Assert.Equal(1140.0, result.Effective.N, 10);
        Assert.Equal(120.0, result.Nominal.Mx, 10);
        Assert.Equal(114.0, result.Effective.Mx, 10);
        Assert.Equal(240.0, result.Nominal.My, 10);
        Assert.Equal(228.0, result.Effective.My, 10);
    }

    [Fact]
    public void PrestressActions_SumsGroupsAndPreservesMomentSigns()
    {
        var section = Section(
            Group(1, "upper", sigSp: 1000, gammaSp: 1,
                (0.0004, 0.2, -0.1)),
            Group(2, "lower", sigSp: 2000, gammaSp: 1,
                (0.0002, -0.1, 0.3)));

        var result = section.PrestressActions(new XY(0, 0));

        Assert.Equal(800.0, result.Nominal.N, 10);
        Assert.Equal(80.0, result.Nominal.Mx, 10);
        Assert.Equal(40.0, result.Nominal.My, 10);
        Assert.Equal(800.0, result.Effective.N, 10);
        Assert.Equal(2, result.Groups.Count);
    }

    [Fact]
    public void PrestressActions_DefaultReferenceIsTheSectionCentroid()
    {
        var section = Section(
            Group(17, "strand", sigSp: 1500, gammaSp: 1,
                (0.0004, 0.2, 0.1),
                (0.0004, 0.2, 0.1)));

        var expected = new GeoProps(section).Centroid!;
        var result = section.PrestressActions();

        Assert.Equal(expected.X, result.ReferencePoint.X, 12);
        Assert.Equal(expected.Y, result.ReferencePoint.Y, 12);
        Assert.Equal(0.0, result.Nominal.Mx, 12);
        Assert.Equal(0.0, result.Nominal.My, 12);
    }

    [Fact]
    public void PrestressActions_UsesExplicitReferencePoint()
    {
        var section = Section(
            Group(17, "strand", sigSp: 1000, gammaSp: 1,
                (0.0004, 0.2, 0.1)));

        var result = section.PrestressActions(new XY(-0.3, -0.4));

        Assert.Equal(400.0, result.Nominal.N, 10);
        Assert.Equal(200.0, result.Nominal.Mx, 10);
        Assert.Equal(200.0, result.Nominal.My, 10);
        Assert.Equal(-0.3, result.ReferencePoint.X, 12);
        Assert.Equal(-0.4, result.ReferencePoint.Y, 12);
    }

    [Fact]
    public void PrestressActions_FiltersNonRebarGroupsAndReturnsZeroWhenNoGroupsRemain()
    {
        var region = Group(17, "region", sigSp: 1500, gammaSp: 1,
            (0.0004, 0.2, 0.1));
        region.Category = AreaCategory.Region;
        var section = Section(region);

        var result = section.PrestressActions();

        Assert.False(result.HasPrestressedGroups);
        Assert.Empty(result.Groups);
        Assert.Equal(0.0, result.Nominal.N, 12);
        Assert.Equal(0.0, result.Nominal.Mx, 12);
        Assert.Equal(0.0, result.Nominal.My, 12);
    }

    [Fact]
    public void PrestressActions_ThrowsWhenDefaultReferenceIsUnavailableForPrestressedGroup()
    {
        var group = new MaterialArea
        {
            Id = 17,
            Tag = "strand",
            Category = AreaCategory.RebarGroup,
            SigSp = 1500,
            GammaSp = 1,
            Fibers =
            [
                new Fiber(0.2, 0.1)
                {
                    Area = 0.0008,
                    TypeFiber = FiberType.point,
                },
            ],
        };

        var section = Section(group);

        Assert.Throws<InvalidOperationException>(() => section.PrestressActions());
    }

    [Fact]
    public void PrestressActionsJsonModel_UsesStableNamesAndUnits()
    {
        var section = Section(
            Group(17, "strand", sigSp: 1500, gammaSp: 0.95,
                (0.0004, 0.2, 0.1),
                (0.0004, 0.2, 0.1)));
        var result = section.PrestressActions(new XY(0, 0));

        var json = JsonSerializer.Serialize(PrestressActionsJsonModel.From(result));
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal(0.0, root.GetProperty("reference").GetProperty("x_m").GetDouble(), 12);
        Assert.Equal(1200.0, root.GetProperty("nominal").GetProperty("N_kN").GetDouble(), 10);
        Assert.Equal(1140.0, root.GetProperty("effective").GetProperty("N_kN").GetDouble(), 10);
        Assert.Equal(17, root.GetProperty("groups")[0].GetProperty("areaId").GetInt32());
        Assert.Equal(1500.0, root.GetProperty("groups")[0].GetProperty("sigSp_MPa").GetDouble(), 10);
    }

    [Fact]
    public void CrossSectionCompute_ExposesPrestressActionsWithoutChangingSectionResponse()
    {
        var section = TestSections.RectWithBottomRebar();
        var group = section.Areas.Single(area => area.Category == AreaCategory.RebarGroup);
        group.SigSp = 1500;
        group.GammaSp = 0.95;
        section.ResolveAndBuildDiagramms(0.85, pool: null, rebarDifferentialDiagram: false);

        var result = section.Compute(new Kurvature(), CalcType.N, computeStiffness: false);
        var direct = section.PrestressActions();
        var expectedResponse = section.Integral(new Kurvature(), CalcType.N);

        Assert.NotNull(result.Prestress);
        Assert.Equal(direct.Effective.N, result.Prestress!.Effective.N, 10);
        Assert.Equal(direct.Effective.Mx, result.Prestress.Effective.Mx, 10);
        Assert.Equal(direct.Effective.My, result.Prestress.Effective.My, 10);
        Assert.Equal(expectedResponse.N, result.N, 10);
        Assert.Equal(expectedResponse.Mx, result.Mx, 10);
        Assert.Equal(expectedResponse.My, result.My, 10);
    }

    [Fact]
    public void PrestressActions_PreservePrestressParametersWhenSectionIsClonedForCalculation()
    {
        var section = Section(
            Group(17, "strand", sigSp: 1500, gammaSp: 0.95,
                (0.0004, 0.2, 0.1),
                (0.0004, 0.2, 0.1)));

        var clone = section.CloneForCalc();
        var result = clone.PrestressActions(new XY(0, 0));

        Assert.Equal(1200.0, result.Nominal.N, 10);
        Assert.Equal(1140.0, result.Effective.N, 10);
        Assert.Single(result.Groups);
    }

    static CrossSection Section(params MaterialArea[] areas) => new() { Areas = [.. areas] };

    static MaterialArea Group(
        int id,
        string tag,
        double sigSp,
        double gammaSp,
        params (double Area, double X, double Y)[] fibers)
    {
        var material = TestMaterials.Rebar("A500");
        return new MaterialArea
        {
            Id = id,
            Tag = tag,
            Category = AreaCategory.RebarGroup,
            Material = material,
            MaterialId = material.Id,
            SigSp = sigSp,
            GammaSp = gammaSp,
            Fibers = [.. fibers.Select(item => new Fiber(item.X, item.Y)
            {
                Area = item.Area,
                TypeFiber = FiberType.point,
            })],
        };
    }
}
