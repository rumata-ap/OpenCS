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

        Assert.Equal(-1200.0, result.Nominal.N, 10);
        Assert.Equal(-1200.0, result.Effective.N, 10);
        Assert.Equal(-120.0, result.Nominal.Mx, 10);
        Assert.Equal(-240.0, result.Nominal.My, 10);
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

        Assert.Equal(-1200.0, result.Nominal.N, 10);
        Assert.Equal(-1140.0, result.Effective.N, 10);
        Assert.Equal(-120.0, result.Nominal.Mx, 10);
        Assert.Equal(-114.0, result.Effective.Mx, 10);
        Assert.Equal(-240.0, result.Nominal.My, 10);
        Assert.Equal(-228.0, result.Effective.My, 10);
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

        Assert.Equal(-800.0, result.Nominal.N, 10);
        Assert.Equal(-80.0, result.Nominal.Mx, 10);
        Assert.Equal(-40.0, result.Nominal.My, 10);
        Assert.Equal(-800.0, result.Effective.N, 10);
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

        Assert.Equal(-400.0, result.Nominal.N, 10);
        Assert.Equal(-200.0, result.Nominal.Mx, 10);
        Assert.Equal(-200.0, result.Nominal.My, 10);
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
        Assert.Equal(-1200.0, root.GetProperty("nominal").GetProperty("N_kN").GetDouble(), 10);
        Assert.Equal(-1140.0, root.GetProperty("effective").GetProperty("N_kN").GetDouble(), 10);
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

        Assert.Equal(-1200.0, result.Nominal.N, 10);
        Assert.Equal(-1140.0, result.Effective.N, 10);
        Assert.Single(result.Groups);
    }

    [Fact]
    public void PrestressActions_ReportsPrestressAsCompressionOfSection()
    {
        var section = Section(
            Group(17, "strand", sigSp: 1000, gammaSp: 1,
                (0.0004, 0.0, -0.2)));

        var result = section.PrestressActions(new XY(0, 0));

        // Натянутая арматура обжимает сечение: по конвенции OpenCS (N = ∫σ·dA)
        // это отрицательная продольная сила.
        Assert.Equal(-400.0, result.Nominal.N, 10);
        // Обжатие ниже центра раскрывает сечение поверху → положительный Mx.
        Assert.Equal(80.0, result.Nominal.Mx, 10);
    }

    [Fact]
    public void PrestressActions_DefaultReferenceDoesNotDependOnCurrentStressState()
    {
        var section = TestSections.RectWithEccentricPrestressedRebar();
        var before = section.PrestressActions();

        // Продавливаем сечение далеко в нелинейность: секущие модули волокон меняются.
        section.SetEps(new Kurvature { e0 = -0.002, ky = 0.01, kz = 0.005 }, CalcType.C, ten: false);
        var after = section.PrestressActions();

        Assert.Equal(before.ReferencePoint.X, after.ReferencePoint.X, 12);
        Assert.Equal(before.ReferencePoint.Y, after.ReferencePoint.Y, 12);
        Assert.Equal(before.Nominal.Mx, after.Nominal.Mx, 9);
        Assert.Equal(before.Nominal.My, after.Nominal.My, 9);
    }

    [Fact]
    public void PrestressActions_ActualMatchesSectionResponseAtZeroStrainPlane()
    {
        var section = TestSections.RectWithEccentricPrestressedRebar();
        var atRest = section.Integral(new Kurvature(), CalcType.C, ten: false);

        var result = section.PrestressActions(new XY(0, 0), CalcType.C, ten: false);

        // Фактическое действие — это то, что сечение реально отдаёт от ε_p,
        // то есть отклик при нулевой плоскости деформаций, взятый с обратным знаком.
        Assert.Equal(-atRest.N, result.Actual.N, 6);
        Assert.Equal(-atRest.Mx, result.Actual.Mx, 6);
        Assert.Equal(-atRest.My, result.Actual.My, 6);
        Assert.True(Math.Abs(result.Actual.N) < Math.Abs(result.Nominal.N),
            "σ_sp выше Ft, значит диаграмма даёт меньшую силу, чем σ_sp·A");
    }

    [Fact]
    public void PrestressActions_FlagsGroupWhereSigSpExceedsMaterialStrength()
    {
        var section = TestSections.RectWithEccentricPrestressedRebar(sigSp: 900.0);

        var result = section.PrestressActions(calc: CalcType.C);
        var group = result.Groups.Single();

        Assert.True(group.ExceedsStrength);
        Assert.True(result.HasGroupsAboveStrength);
        Assert.Equal(870.0, group.SigLimit, 6);
        Assert.InRange(group.SigActual, 700.0, 870.0);
    }

    [Fact]
    public void PrestressActions_DoesNotFlagGroupWithinMaterialStrength()
    {
        var section = TestSections.RectWithEccentricPrestressedRebar(sigSp: 600.0);

        var result = section.PrestressActions(calc: CalcType.C);

        Assert.False(result.Groups.Single().ExceedsStrength);
        Assert.False(result.HasGroupsAboveStrength);
    }

    [Fact]
    public void PrestressActionsJsonModel_ExposesActualActionAndStrengthFlag()
    {
        var section = TestSections.RectWithEccentricPrestressedRebar(sigSp: 900.0);
        var result = section.PrestressActions(new XY(0, 0), CalcType.C, ten: false);

        var json = JsonSerializer.Serialize(PrestressActionsJsonModel.From(result));
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var group = root.GetProperty("groups")[0];

        Assert.Equal(result.Actual.N, root.GetProperty("actual").GetProperty("N_kN").GetDouble(), 6);
        Assert.Equal(result.Groups[0].SigActual, group.GetProperty("sigActual_MPa").GetDouble(), 6);
        Assert.Equal(870.0, group.GetProperty("sigLimit_MPa").GetDouble(), 6);
        Assert.True(group.GetProperty("exceedsStrength").GetBoolean());
        Assert.True(root.GetProperty("hasGroupsAboveStrength").GetBoolean());
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
