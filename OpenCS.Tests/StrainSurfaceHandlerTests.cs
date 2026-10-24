using System.IO;
using CScore;
using OpenCS.LimitSurface;
using OpenCS.Tasks;
using OpenCS.Utilites;
using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Задача поверхности предельных усилий: сетка и снимок точек в результате, пометка η, ошибки.</summary>
public sealed class StrainSurfaceHandlerTests : IDisposable
{
    readonly string _path = Path.Combine(Path.GetTempPath(), $"opencs_surface_{Guid.NewGuid():N}.db");
    readonly DatabaseService _db;

    public StrainSurfaceHandlerTests()
    {
        _db = new DatabaseService(_path);
        _db.ForceSets.Add(new ForceSet
        {
            Id = 1, Kind = "bar", Tag = "РСУ",
            Items = [new LoadItem { Id = 1, Num = 1, Label = "1", N = -100, Mx = -40 },
                     new LoadItem { Id = 2, Num = 2, Label = "2", N = -400, Mx = -10, My = 5 }],
        });
        _db.ForceSets.Add(new ForceSet { Id = 2, Kind = "bar", Tag = "P-Δ", SecondOrder = true,
            Items = [new LoadItem { Id = 3, Num = 1, Label = "1", N = -100, Mx = -40 }] });
    }

    public void Dispose()
    {
        _db.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_path); } catch (IOException) { }
    }

    CalcResult Run(int forceSetId, string paramsJson = """{"Accuracy":"normal"}""") =>
        TaskRunner.Run(new CalcTask { Id = 1, Kind = "limit_surface", CalcType = CalcType.C, ForceSetId = forceSetId,
                                      ParamsJson = paramsJson },
            ReportFixtures.BuildSurfaceBeam(), new LoadItem(), CalcSettings.Default, new TaskRunContext { Database = _db });

    [Fact]
    public void WithForceSet_MeshAndPointSnapshot()
    {
        var r = Run(1);
        Assert.Equal("ok", r.Status);
        var data = LimitSurfaceResultData.Parse(r.DataJson);
        Assert.True(data.VertexCount > 100);
        Assert.Equal(data.VertexCount, LimitSurfaceResultData.DecodeMesh(data.Mesh).Vertices.Count);
        Assert.Equal([1, 2], data.Points.Select(p => p.Num));
        Assert.Equal(-400, data.Points[1].N);
        Assert.NotEmpty(data.ControlPoints);
        Assert.Contains(LimitSurfaceWarnings.EtaNotApplied, data.Warnings);
        Assert.Equal((24, 24), (data.InitialMeridians, data.ThetaIntervals));
    }

    [Fact]
    public void WithoutForceSet_OnlyMesh()
    {
        var data = LimitSurfaceResultData.Parse(Run(0).DataJson);
        Assert.Empty(data.Points);
        Assert.Empty(data.Warnings);
    }

    [Fact]
    public void HighAccuracy_NotFewerMeridians()
    {
        var normal = LimitSurfaceResultData.Parse(Run(0).DataJson);
        var high = LimitSurfaceResultData.Parse(Run(0, """{"Accuracy":"high"}""").DataJson);
        Assert.Equal("high", high.Accuracy);
        Assert.True(high.MeridianCount >= normal.MeridianCount, $"{high.MeridianCount} < {normal.MeridianCount}");
    }

    [Fact]
    public void SecondOrderSet_NoEtaWarning()
        => Assert.DoesNotContain(LimitSurfaceWarnings.EtaNotApplied, LimitSurfaceResultData.Parse(Run(2).DataJson).Warnings);

    [Fact]
    public void MissingForceSet_Error() => Assert.Equal("error", Run(99).Status);

    [Fact]
    public void Kind_IsUls() => Assert.Equal(CalcTaskGroups.Uls, CalcTaskGroups.Classify("limit_surface"));

    CalcResult RunCrack(CalcType calc, int forceSetId = 0, bool withEpsBt0 = true)
    {
        var section = ReportFixtures.BuildSurfaceBeam();
        if (withEpsBt0)
        {
            // В фикстуре только двухлинейная диаграмма; справочные εb0, εbt0 (DataSource, B25) — для (8.53)/(8.54).
            var ch = section.Areas.First(a => a.Material?.Type == MatType.Concrete).Material!.GetChars(calc)!;
            ch.Ec0 = -0.002;
            ch.Et0 = 0.0001;
        }
        return TaskRunner.Run(new CalcTask { Id = 2, Kind = "crack_surface", CalcType = calc, ForceSetId = forceSetId,
                                             ParamsJson = """{"Accuracy":"normal"}""" },
            section, new LoadItem(), CalcSettings.Default, new TaskRunContext { Database = _db });
    }

    [Fact]
    public void Crack_NoEpsBt0_ErrorNamesArea()
    {
        var r = RunCrack(CalcType.N, withEpsBt0: false);
        Assert.Equal("error", r.Status);
        using var doc = System.Text.Json.JsonDocument.Parse(r.DataJson);
        Assert.Contains("εbt0", doc.RootElement.GetProperty("error").GetString());
    }

    [Theory]
    [InlineData(CalcType.N)]
    [InlineData(CalcType.NL)]
    public void Crack_SecondGroup_MeshWithCrackCriterion(CalcType calc)
    {
        var r = RunCrack(calc, 1);
        Assert.True(r.Status == "ok", r.DataJson);
        var data = LimitSurfaceResultData.Parse(r.DataJson);
        Assert.Equal("crack", data.Criterion);
        Assert.True(data.Ten);
        Assert.Equal(calc, data.CalcType);
        Assert.All(data.ControlPoints, p => Assert.Equal(CScore.LimitSurface.StrainPointKind.Concrete, p.Kind));
        Assert.Equal(2, data.Points.Count);
    }

    [Theory]
    [InlineData(CalcType.C)]
    [InlineData(CalcType.CL)]
    public void Crack_FirstGroup_Error(CalcType calc)
    {
        var r = RunCrack(calc);
        Assert.Equal("error", r.Status);
        using var doc = System.Text.Json.JsonDocument.Parse(r.DataJson);
        Assert.Contains("2-й группы", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public void CrackKind_IsSls() => Assert.Equal(CalcTaskGroups.Sls, CalcTaskGroups.Classify("crack_surface"));
}
