using CScore;
using CScore.History;
using OpenCS.Tasks;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Сквозной прогон load_history через TaskRunner.</summary>
public sealed class LoadHistoryHandlerTests : IDisposable
{
    readonly string _path = Path.Combine(Path.GetTempPath(), $"opencs_lh_{Guid.NewGuid():N}.db");
    readonly DatabaseService _db;

    public LoadHistoryHandlerTests()
    {
        _db = new DatabaseService(_path);
        _db.ForceSets.Add(new ForceSet { Id = 1, Kind = "bar", Tag = "РСУ",
            Items = [new LoadItem { Id = 1, Num = 1, Label = "1", Mx = -60 }] });
    }

    public void Dispose()
    {
        _db.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_path); } catch (IOException) { }
    }

    /// <summary>Балка ReportFixtures.BuildBeam — часть 0, её копия бетона, сдвинутая вверх, — часть 1.</summary>
    static StagedSection Section()
    {
        var beam = ReportFixtures.BuildBeam();
        var top = ReportFixtures.BuildBeam().Areas[0];
        double h = top.Hull!.Y.Max() - top.Hull.Y.Min();
        top.Hull = new Contour(top.Hull.X.ToArray(), top.Hull.Y.Select(y => y + h).ToArray(), "outer");
        top.SetWKT();
        var s = new StagedSection { Id = 5, Tag = "усиление",
            Parts = [new SectionPart { Index = 0, Tag = "балка", Areas = beam.Areas },
                     new SectionPart { Index = 1, Tag = "наращивание", Areas = [top] }] };
        s.SyncAreas();
        return s;
    }

    CalcResult Run(LoadHistoryParams p, CalcSettings? settings = null) =>
        TaskRunner.Run(new CalcTask { Id = 1, Kind = "load_history", CalcType = CalcType.C, SectionId = 5, ParamsJson = p.ToJson() },
            Section(), new LoadItem(), settings ?? CalcSettings.Default, new TaskRunContext { Database = _db });

    static LoadHistoryParams Params() => new()
    {
        Stages =
        [
            new() { Tag = "1", Steps = 3, Force = new StageForce { Mode = "item", ForceSetId = 1, ForceItemId = 1 } },
            new() { Tag = "2", ActivatePart = 1, Steps = 3, Force = new StageForce { Mode = "manual", Mx = -90 } },
            new() { Tag = "р", Kind = StageKind.Failure, Force = new StageForce { Mode = "manual", Mx = -1 } },
        ],
        Failure = new FailureOptions { Steps = 30 },
    };

    [Fact]
    public void Runs_WithAndWithoutHistory()
    {
        var r = Run(Params());
        Assert.Contains(r.Status, new[] { "ok", "partial" });
        var d = LoadHistoryResultData.Parse(r.DataJson);
        Assert.Null(d.Error);
        Assert.Equal(-60, d.Steps[2].Mx, 0);           // строка набора
        Assert.NotNull(d.StepsNoHistory);
        Assert.NotNull(d.With.MPeak);
        Assert.Equal(["балка", "наращивание"], d.PartTags);
    }

    [Fact]
    public void InvalidParams_Error()
    {
        var p = Params();
        p.Stages[1].ActivatePart = 3;
        Assert.Equal("error", Run(p).Status);
    }

    [Fact]
    public void NotStagedSection_Error() =>
        Assert.Equal("error", TaskRunner.Run(new CalcTask { Kind = "load_history", ParamsJson = Params().ToJson() },
            ReportFixtures.BuildBeam(), new LoadItem(), CalcSettings.Default, new TaskRunContext { Database = _db }).Status);

    [Fact]
    public void FibersMode_FromSettings()
    {
        var settings = CalcSettings.Default.Clone();
        settings.StageStrainMode = "fibers";
        Assert.Equal("fibers", LoadHistoryResultData.Parse(Run(Params(), settings).DataJson).StrainMode);
    }

    [Fact]
    public void Runs_WithHysteresis_InFibersMode()
    {
        var p = Params();
        p.Hysteresis = new CScore.Hysteresis.HysteresisOptions();
        var settings = CalcSettings.Default.Clone();
        settings.StageStrainMode = "plane";
        var r = Run(p, settings);
        Assert.Contains(r.Status, new[] { "ok", "partial" });
        var d = LoadHistoryResultData.Parse(r.DataJson);
        Assert.Null(d.Error);
        Assert.NotNull(d.Hysteresis);
        Assert.Equal("fibers", d.StrainMode);
    }

    [Fact]
    public void OriginalSectionNotMutated()
    {
        var s = Section();
        TaskRunner.Run(new CalcTask { Kind = "load_history", ParamsJson = Params().ToJson() },
            s, new LoadItem(), CalcSettings.Default, new TaskRunContext { Database = _db });
        Assert.Equal(1, s.ActivePartCount);
    }
}
