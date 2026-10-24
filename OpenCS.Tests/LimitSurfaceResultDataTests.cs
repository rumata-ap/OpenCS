using CScore;
using CScore.LimitSurface;
using OpenCS.LimitSurface;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Формат результата поверхности: сетка переживает сохранение без потерь, чужая версия — понятная ошибка.</summary>
public class LimitSurfaceResultDataTests
{
    static LimitSurfaceMesh BuildMesh()
    {
        var section = ReportFixtures.BuildSurfaceBeam();
        section.ResolveAndBuildDiagramms(CalcSettings.Default.Sp63DescEtaMin);
        return new StrainDomainMesher(section, new UlsStrainCriterion(CalcType.C, ten: false),
            new LimitSurfaceOptions { InitialMeridians = 12, ThetaIntervals = 12 }).Build();
    }

    [Fact]
    public void Mesh_RoundTrip_IsExact()
    {
        var mesh = BuildMesh();
        var back = LimitSurfaceResultData.DecodeMesh(LimitSurfaceResultData.EncodeMesh(mesh));

        Assert.Equal(mesh.Vertices, back.Vertices);
        Assert.Equal(mesh.Triangles, back.Triangles);
        Assert.Equal(mesh.Active, back.Active);
        Assert.Equal(mesh.MeridianOf, back.MeridianOf);
        Assert.Equal(mesh.StrainPlanes.Select(k => (k.e0, k.ky, k.kz)), back.StrainPlanes.Select(k => (k.e0, k.ky, k.kz)));
        Assert.Equal(mesh.Angles, back.Angles);
    }

    [Fact]
    public void Mesh_WithoutAngles_StillDecodes()
    {
        var mesh = BuildMesh();
        var bare = new LimitSurfaceMesh(mesh.Vertices, mesh.Triangles, mesh.StrainPlanes, mesh.Active, meridianOf: mesh.MeridianOf);
        var back = LimitSurfaceResultData.DecodeMesh(LimitSurfaceResultData.EncodeMesh(bare));
        Assert.Equal(mesh.Vertices, back.Vertices);
        Assert.Empty(back.Angles);
    }

    [Theory]
    [InlineData(UtilizationMode.All)]
    [InlineData(UtilizationMode.Moment)]
    [InlineData(UtilizationMode.Axial)]
    public void Ratio_SameOnRestoredMesh(UtilizationMode mode)
    {
        var mesh = BuildMesh();
        var back = LimitSurfaceResultData.DecodeMesh(LimitSurfaceResultData.EncodeMesh(mesh));
        foreach (var (n, mx, my) in new[] { (-300.0, -40.0, 5.0), (-800.0, -10.0, -20.0), (-50.0, -60.0, 0.0) })
            Assert.Equal(mesh.Utilization(n, mx, my, mode).Ratio, back.Utilization(n, mx, my, mode).Ratio);
    }

    [Fact]
    public void Json_RoundTrip_KeepsPointsAndWarnings()
    {
        var data = new LimitSurfaceResultData
        {
            CalcType = CalcType.C, Accuracy = "high", Mesh = LimitSurfaceResultData.EncodeMesh(BuildMesh()),
            Points = [new LimitSurfacePointRecord(3, "РСУ 3", -100, -20, 1.5)],
            ControlPoints = [new LimitSurfaceControlPointRecord(StrainPointKind.Rebar, 0.1, -0.2, 2)],
            ForceSetId = 7, ForceSetName = "РСУ", Warnings = [LimitSurfaceWarnings.EtaNotApplied],
        };
        var back = LimitSurfaceResultData.Parse(data.ToJson());
        Assert.Equal(data.Points, back.Points);
        Assert.Equal(data.ControlPoints, back.ControlPoints);
        Assert.Equal("high", back.Accuracy);
        Assert.Equal([LimitSurfaceWarnings.EtaNotApplied], back.Warnings);
    }

    [Fact]
    public void Parse_UnknownVersion_Throws()
        => Assert.Throws<LimitSurfaceFormatException>(() => LimitSurfaceResultData.Parse("""{"FormatVersion":99}"""));

    [Fact]
    public void Params_HighAccuracy_FinerOptions()
    {
        var normal = LimitSurfaceTaskParams.Parse("""{"Accuracy":"normal"}""").ToOptions();
        var high = LimitSurfaceTaskParams.Parse("""{"Accuracy":"high"}""").ToOptions();
        Assert.Equal(0.002, normal.RefineTol);
        Assert.Equal(0.001, high.RefineTol);
        Assert.Equal(1152, high.MaxMeridians);
        Assert.Equal("normal", LimitSurfaceTaskParams.Parse(null).Accuracy);
    }
}
