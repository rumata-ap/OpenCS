using CScore;
using CScore.LimitSurface;
using OpenCS.LimitSurface;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Сцена окна для поверхности образования трещин (ПС2): режим по умолчанию, легенды, раскраска.</summary>
public class LimitSurfaceCrackSceneTests
{
    static LimitSurfaceResultData Build(string criterionCode)
    {
        var section = ReportFixtures.BuildSurfaceBeam();
        var ch = section.Areas.First(a => a.Material?.Type == MatType.Concrete).Material!.GetChars(CalcType.N)!;
        ch.Ec0 = -0.002;
        ch.Et0 = 0.0001;
        section.ResolveAndBuildDiagramms(CalcSettings.Default.Sp63DescEtaMin);
        IStrainSurfaceCriterion criterion = criterionCode == LimitSurfaceResultData.CriterionCrack
            ? new CrackStrainCriterion()
            : new UlsStrainCriterion(CalcType.C, ten: false);
        var mesh = new StrainDomainMesher(section, criterion, new LimitSurfaceOptions { InitialMeridians = 12, ThetaIntervals = 12 }).Build();
        return new LimitSurfaceResultData
        {
            Criterion = criterionCode,
            Mesh = LimitSurfaceResultData.EncodeMesh(mesh),
            ControlPoints = criterion.BuildDomain(section).Points
                .Select(p => new LimitSurfaceControlPointRecord(p.Kind, p.X, p.Y, p.AreaIndex)).ToList(),
            VertexCount = mesh.Vertices.Count,
        };
    }

    static readonly Lazy<LimitSurfaceScene> Crack = new(() => new LimitSurfaceScene(Build(LimitSurfaceResultData.CriterionCrack)));

    [Fact]
    public void DefaultMode_CrackMoment_UlsAll()
    {
        Assert.Equal(UtilizationMode.Moment, Crack.Value.DefaultMode);
        Assert.Equal(UtilizationMode.All, new LimitSurfaceScene(Build(LimitSurfaceResultData.CriterionUls)).DefaultMode);
    }

    [Fact]
    public void ActiveGroups_OnlyConcrete_CrackLegend()
    {
        var groups = Crack.Value.SurfaceGroups(SurfaceColoring.Active);
        Assert.Equal(
            ["LimitSurfaceLegend_ConcreteCompression", "LimitSurfaceLegend_ConcreteTension_Crack"],
            groups.Select(g => g.LegendKey).OrderBy(k => k));
    }

    [Fact]
    public void SingleSignColoring_SupportedWithCrackLegend()
    {
        var scene = Crack.Value;
        Assert.True(scene.SupportsSingleSign);
        Assert.Equal("LimitSurfaceColoring_SingleSign_Crack", scene.ColoringKey(SurfaceColoring.SingleSign));
        Assert.Contains(scene.SurfaceGroups(SurfaceColoring.SingleSign), g => g.LegendKey == "LimitSurfaceLegend_SingleSign_Crack");
    }

    [Fact]
    public void Acrc_MomentDefault_ConditionCategories_NoSingleSign()
    {
        // Сетка тела ПС2 с кодом критерия acrc: проверяются правила сцены, а не построитель.
        var data = Build(LimitSurfaceResultData.CriterionCrack);
        data.Criterion = LimitSurfaceResultData.CriterionAcrc;
        var scene = new LimitSurfaceScene(data);
        Assert.True(scene.IsAcrc);
        Assert.Equal(UtilizationMode.Moment, scene.DefaultMode);
        Assert.False(scene.SupportsSingleSign);
        Assert.Equal(ActiveCategory.AcrcLong, scene.CategoryOf(new ActiveConstraint(-1, 1, false, ConstraintKind.AcrcLong)));
        Assert.Equal(ActiveCategory.AcrcShort, scene.CategoryOf(new ActiveConstraint(-1, 1, false, ConstraintKind.AcrcShort)));
        Assert.Equal(ActiveCategory.SteelStress, scene.CategoryOf(new ActiveConstraint(-1, 1, false, ConstraintKind.SteelStress)));
        Assert.Equal(ActiveCategory.ConcreteCompression, scene.CategoryOf(new ActiveConstraint(-1, -1, false, ConstraintKind.ConcreteCompression)));
        Assert.Equal(ActiveCategory.CrackBody, scene.CategoryOf(new ActiveConstraint(0, 1, false, ConstraintKind.CrackBody)));
        Assert.Equal("LimitSurfaceLegend_ConcreteCompression_Acrc", scene.ActiveLegendKey(ActiveCategory.ConcreteCompression));
        Assert.Equal("LimitSurfaceLegend_AcrcShort", scene.ActiveLegendKey(ActiveCategory.AcrcShort));
    }

    [Fact]
    public void ActiveNote_ByRule()
    {
        var scene = Crack.Value;
        Assert.Equal(" (8.54)", scene.SingleSignNote(ActiveCategory.ConcreteTension, singleSign: true));
        Assert.Equal(" (8.53)", scene.SingleSignNote(ActiveCategory.ConcreteCompression, singleSign: true));
        Assert.Equal("", scene.SingleSignNote(ActiveCategory.ConcreteTension, singleSign: false));
        Assert.Equal("LimitSurfaceLegend_ConcreteTension_Crack", scene.ActiveLegendKey(ActiveCategory.ConcreteTension));
    }
}
