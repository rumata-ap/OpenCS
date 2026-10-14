using System.Collections.ObjectModel;
using System.IO;
using CScore;
using CScore.Fem;
using CScore.Fire.Entities;
using CScore.Sp63.Deflection;
using OpenCS.Reporting;
using OpenCS.Services;
using OpenCS.Tasks;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>
/// Срез 0.1 Этапа 0: исполнители расчётов работают через <see cref="IProjectContext"/> — без AppViewModel и окон.
/// </summary>
public sealed class ProjectContextExecutorTests : IDisposable
{
    readonly string _path = Path.Combine(Path.GetTempPath(), $"opencs_ctx_{Guid.NewGuid():N}.db");
    readonly TestProjectContext _ctx;

    public ProjectContextExecutorTests() => _ctx = new TestProjectContext(new DatabaseService(_path));

    public void Dispose()
    {
        _ctx.Db.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_path); } catch (IOException) { }
    }

    // ---------- задачи сечений ----------

    static CrossSection Section(int id)
    {
        var concrete = new Material { Id = 1, Tag = "B25", Type = MatType.Concrete, E = 30_000_000 };
        concrete.N = new MaterialChars(CalcType.N)
        { Type = MatType.Concrete, Fc = -18_500, Ft = 1_550, E = 30_000_000, Class = 25 };
        var rebar = new Material { Id = 2, Tag = "A400", Type = MatType.ReSteelU, E = 200_000_000 };
        rebar.N = new MaterialChars(CalcType.N)
        { Type = MatType.ReSteelU, Fc = -500_000, Ft = 500_000, E = 200_000_000 };
        var section = new CrossSection { Id = id, Tag = "test" };
        var region = new MaterialArea { Category = AreaCategory.Region, Material = concrete, MaterialId = 1 };
        region.Contours.Add(new Contour([-0.25, 0.25, 0.25, -0.25, -0.25],
            [-0.15, -0.15, 0.15, 0.15, -0.15], "hull") { Type = ContourType.Hull });
        region.SetWKT();
        section.Areas.Add(region);
        foreach (double y in new[] { -0.11, 0.11 })
        {
            var group = new MaterialArea { Category = AreaCategory.RebarGroup, Material = rebar, MaterialId = 2 };
            group.Fibers.Add(new Fiber { TypeFiber = FiberType.point, X = 0, Y = y, Area = 20e-4, Diameter = 0.02 });
            section.Areas.Add(group);
        }
        return section;
    }

    CalcTask DeflectionTask(int sectionId)
    {
        var task = new CalcTask
        {
            Tag = "Прогиб", Kind = "sp63_deflection", CalcType = CalcType.N, SectionId = sectionId,
            ParamsJson = new Sp63DeflectionTaskParams
            {
                UseManualForces = true, N = 10, Mx = 80, My = 0,
                ForcesMode = "manual", NLongManual = 4, MxLongManual = 20,
                MyLongManual = 0, Scheme = "simply_supported_uniform", SpanM = 6,
                DeflectionLimitMm = 30
            }.ToJson()
        };
        _ctx.Db.SaveCalcTask(task);
        return task;
    }

    [Fact]
    public async Task CalcTask_MissingSection_ErrorNotice_NoBusy()
    {
        var task = DeflectionTask(sectionId: 999);
        var shown = new List<CalcResult>();

        await CalcTaskExecutor.RunAsync(_ctx, task, showResult: shown.Add);

        Assert.Equal(["CalcTaskSectionNotFound"], _ctx.Notices.Errors);
        Assert.Empty(_ctx.BusyHost.Calls);
        Assert.Empty(shown);
    }

    [Fact]
    public async Task CalcTask_Runs_SavesResult_ShowsItOnce_BeforeEndBusy()
    {
        _ctx.CrossSectionList.Add(Section(7));
        var task = DeflectionTask(sectionId: 7);
        var shown = new List<CalcResult>();
        bool changed = false;

        await CalcTaskExecutor.RunAsync(_ctx, task, () => changed = true,
            r => { shown.Add(r); _ctx.BusyHost.Calls.Add("show"); });

        var result = Assert.Single(shown);
        Assert.True(changed);
        Assert.NotEqual(0, result.Id);
        Assert.NotNull(_ctx.Db.GetCalcResultById(result.Id));
        Assert.Equal("begin", _ctx.BusyHost.Calls[0]);
        Assert.Equal(["show", "end"], _ctx.BusyHost.Calls.Skip(1).Select(c => c.StartsWith("end") ? "end" : c));
        Assert.False(_ctx.BusyHost.IsBusy);
        Assert.NotEmpty(_ctx.Log.LogEntries);
        Assert.Empty(_ctx.Notices.Errors);
    }

    [Fact]
    public async Task CalcTask_CancelledBeforeStart_WarnsAndSavesNothing()
    {
        _ctx.CrossSectionList.Add(Section(7));
        var task = DeflectionTask(sectionId: 7);
        _ctx.BusyHost.CancelImmediately = true;
        var shown = new List<CalcResult>();

        await CalcTaskExecutor.RunAsync(_ctx, task, showResult: shown.Add);

        Assert.Empty(shown);
        Assert.Contains(_ctx.Log.LogEntries, e => e.Level == LogLevel.Warning && e.Message == "CalcTaskCancelled");
        Assert.Equal("end:CalcTaskCancelled", _ctx.BusyHost.Calls[^1]);
        Assert.Empty(_ctx.Db.CalcResults.Where(r => r.TaskId == task.Id));
    }

    [Fact]
    public async Task CalcTask_WhenBusy_DoesNothing()
    {
        _ctx.CrossSectionList.Add(Section(7));
        var task = DeflectionTask(sectionId: 7);
        _ctx.BusyHost.IsBusy = true;

        await CalcTaskExecutor.RunAsync(_ctx, task);

        Assert.Empty(_ctx.BusyHost.Calls);
        Assert.Empty(_ctx.Db.CalcResults.Where(r => r.TaskId == task.Id));
    }

    // ---------- МКЭ-постановка ----------

    FemSchema Schema()
    {
        var schema = new FemSchema { Tag = "S" };
        _ctx.Db.SaveFemSchema(schema);
        _ctx.FemSchemaList.Add(schema);
        return schema;
    }

    [Fact]
    public async Task FemAnalysis_EmptyCsfeaSchema_SavesErrorResultAndStatus()
    {
        var schema = Schema();
        var analysis = new FemAnalysis { SchemaId = schema.Id, Tag = "A", Kind = FemCsfeaRunner.LinearAnalysisKind };
        _ctx.Db.SaveFemAnalysis(analysis);

        var result = await FemAnalysisExecutor.RunAndSaveAsync(_ctx, analysis);

        Assert.NotNull(result);
        Assert.Equal("error", result.Status);
        Assert.Equal(result.Id, analysis.ResultId);
        var stored = _ctx.Db.GetFemAnalysis(analysis.Id)!;
        Assert.Equal("error", stored.Status);
        Assert.Equal(result.Id, stored.ResultId);
        Assert.Contains(_ctx.Log.LogEntries, e => e.Level == LogLevel.Error && e.Message == "CalcResultError");
        Assert.Equal("begin", _ctx.BusyHost.Calls[0]);
        Assert.StartsWith("end", _ctx.BusyHost.Calls[^1]);
    }

    [Fact]
    public async Task FemAnalysis_UnknownSchema_ReturnsNullWithoutBusy()
    {
        var analysis = new FemAnalysis { SchemaId = 12345, Tag = "A", Kind = FemCsfeaRunner.LinearAnalysisKind };

        Assert.Null(await FemAnalysisExecutor.RunAndSaveAsync(_ctx, analysis));
        Assert.Empty(_ctx.BusyHost.Calls);
    }

    [Fact]
    public void FemAnalysisDiagnostics_ErrorsArrayAndArtifacts()
    {
        var (diag, dir) = FemAnalysisResultDiagnostics.Read("""{"errors":["e1","e2"]}""");
        Assert.Equal(["e1", "e2"], diag);
        Assert.Null(dir);

        (diag, dir) = FemAnalysisResultDiagnostics.Read(
            """{"Displacements":[],"Reactions":[],"ElementForces":[],"Diagnostics":["d1"],"ArtifactDirectory":"C:\\a"}""");
        Assert.Equal(["d1"], diag);
        Assert.Equal("C:\\a", dir);

        (diag, _) = FemAnalysisResultDiagnostics.Read("не JSON");
        Assert.Empty(diag);
    }

    // ---------- проверка по КЭ ----------

    /// <summary>
    /// Сетка из двух стержневых КЭ в группе; строки усилий есть только у КЭ 1 (плюс строка без номера) —
    /// проверка запускаема, но неполна: у КЭ 2 нет усилий.
    /// </summary>
    FemCheck IncompleteCheck()
    {
        var schema = Schema();
        _ctx.Db.SaveFemMeshSnapshot(schema.Id,
            [new FemMeshNode { NodeTag = "1" }, new FemMeshNode { NodeTag = "2" }, new FemMeshNode { NodeTag = "3" }],
            [new FemElement { ElemTag = "1", NodeIdsJson = "[1,2]" }, new FemElement { ElemTag = "2", NodeIdsJson = "[2,3]" }]);
        var group = new FemMemberGroup { SchemaId = schema.Id, Tag = "G", Kind = FemMemberGroup.KindMesh };
        group.SetTags(["1", "2"]);
        _ctx.Db.SaveFemMemberGroup(group);
        if (!schema.MemberGroups.Contains(group)) schema.MemberGroups.Add(group);

        var fs = new ForceSet { Tag = "РСУ", Kind = "bar", SourceSchemaId = schema.Id, SourceMemberId = group.Id };
        fs.Items.Add(new LoadItem { Num = 1, N = -10, SourceElementNum = 1 });
        fs.Items.Add(new LoadItem { Num = 2, N = -10 });
        _ctx.Db.SaveForceSet(fs);
        _ctx.ForceSetList.Add(fs);

        var check = new FemCheck { SchemaId = schema.Id, MemberId = group.Id, NormCode = "steel_check", Tag = "C" };
        _ctx.Db.SaveFemCheck(check);
        return check;
    }

    [Fact]
    public async Task FemCheck_Incomplete_Declined_NoBusyNoResult()
    {
        var check = IncompleteCheck();
        _ctx.Notices.ConfirmAnswer = false;

        var result = await FemCheckExecutor.RunAsync(_ctx, check);

        Assert.Null(result);
        Assert.Single(_ctx.Notices.Questions);
        Assert.Empty(_ctx.BusyHost.Calls);
        Assert.Null(check.ResultId);
    }

    [Fact]
    public async Task FemCheck_Incomplete_Confirmed_SavesResult()
    {
        var check = IncompleteCheck();
        _ctx.Notices.ConfirmAnswer = true;

        var result = await FemCheckExecutor.RunAsync(_ctx, check);

        Assert.NotNull(result);
        Assert.Single(_ctx.Notices.Questions);
        Assert.Equal(result.Id, check.ResultId);
        Assert.NotNull(_ctx.Db.GetCalcResultById(result.Id));
        Assert.Equal("begin", _ctx.BusyHost.Calls[0]);
        Assert.StartsWith("end", _ctx.BusyHost.Calls[^1]);
    }

    [Fact]
    public async Task FemCheck_TargetMissing_WarnsOnly()
    {
        var schema = Schema();
        var check = new FemCheck { SchemaId = schema.Id, MemberId = 4242, NormCode = "steel_check" };

        Assert.Null(await FemCheckExecutor.RunAsync(_ctx, check));
        Assert.Contains(_ctx.Log.LogEntries, e => e.Level == LogLevel.Warning);
        Assert.Empty(_ctx.BusyHost.Calls);
        Assert.Empty(_ctx.Notices.Questions);
    }
}

/// <summary>Контекст проекта для тестов: БД плюс списки, записывающие журнал, занятость и уведомления.</summary>
sealed class TestProjectContext(DatabaseService db) : IProjectContext
{
    public DatabaseService Db { get; } = db;
    public CalcSettings CalcSettings { get; set; } = CalcSettings.Default;
    public GmshSettings GmshSettings { get; } = new();
    public TestLog Log { get; } = new();
    public ILogService LogService => Log;
    public RecordingNotifier Notices { get; } = new();
    public IUserNotifier Notifier => Notices;
    public RecordingBusyHost BusyHost { get; } = new();
    public IBusyHost Busy => BusyHost;
    public ReportProviderRegistry ReportProviders { get; } = new([]);

    public List<CrossSection> CrossSectionList { get; } = [];
    public List<ForceSet> ForceSetList { get; } = [];
    public List<FemSchema> FemSchemaList { get; } = [];

    public IReadOnlyList<Material> Materials => Db.Materials;
    public IReadOnlyList<Diagramm> Diagrams => Db.Diagrams;
    public IReadOnlyList<CrossSection> CrossSections => CrossSectionList;
    public IReadOnlyList<PlateSection> PlateSections => Db.PlateSections;
    public IReadOnlyList<FireSectionDef> FireSections => Db.FireSections;
    public IReadOnlyList<ForceSet> ForceSets => ForceSetList;
    public IReadOnlyList<ForceSet> BarForceSets => ForceSetList.Where(f => f.Kind == "bar").ToList();
    public IReadOnlyList<FemSchema> FemSchemas => FemSchemaList;
    public IReadOnlyList<CalcTask> CalcTasks => Db.CalcTasks;
}

sealed class TestLog : ILogService
{
    public ObservableCollection<LogEntry> LogEntries { get; } = [];
    public void Info(string message) => LogEntries.Add(new(message, LogLevel.Info, DateTime.Now));
    public void Warning(string message) => LogEntries.Add(new(message, LogLevel.Warning, DateTime.Now));
    public void Error(string message) => LogEntries.Add(new(message, LogLevel.Error, DateTime.Now));
}

sealed class RecordingNotifier : IUserNotifier
{
    public List<string> Errors { get; } = [];
    public List<string> Warnings { get; } = [];
    public List<string> Infos { get; } = [];
    public List<string> Questions { get; } = [];
    public bool ConfirmAnswer { get; set; }

    public void Info(string message, string? title = null) => Infos.Add(message);
    public void Warning(string message, string? title = null) => Warnings.Add(message);
    public void Error(string message, string? title = null) => Errors.Add(message);
    public bool Confirm(string message, string? title = null) { Questions.Add(message); return ConfirmAnswer; }
}

sealed class RecordingBusyHost : IBusyHost
{
    /// <summary>«begin», «end[:сообщение]» — по порядку вызовов (прогресс без контекста синхронизации приходит
    /// из пула потоков и может опоздать за «end» — он только считается).</summary>
    public List<string> Calls { get; } = [];
    public bool IsBusy { get; set; }
    /// <summary>Выдавать уже отменённый токен — отмена до старта расчёта.</summary>
    public bool CancelImmediately { get; set; }

    public CancellationTokenSource BeginBusyWithCancellation(string message, bool indeterminate = true)
    {
        Calls.Add("begin");
        IsBusy = true;
        var cts = new CancellationTokenSource();
        if (CancelImmediately) cts.Cancel();
        return cts;
    }

    int _progressReports;
    public int ProgressReports => Volatile.Read(ref _progressReports);

    public void ReportBusyProgress(double fraction, string? message = null) => Interlocked.Increment(ref _progressReports);

    public void EndBusy(string? message = null)
    {
        Calls.Add(string.IsNullOrEmpty(message) ? "end" : "end:" + message);
        IsBusy = false;
    }
}
