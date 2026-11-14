using System.IO;
using CScore.ParametricSteel;
using OpenCS.Services;
using OpenCS.Tasks;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Срез 0.4: проект без UI — проверки файла, контекст из файла, расчёт задачи через него.</summary>
public sealed class HeadlessProjectContextTests : IDisposable
{
    readonly string _path = Path.Combine(Path.GetTempPath(), $"opencs_headless_{Guid.NewGuid():N}.db");

    public void Dispose() => TestProjectDb.Delete(_path);

    HeadlessProjectContext Open() =>
        HeadlessProjectContext.Open(_path, new TestLog(), new RecordingNotifier(), new RecordingBusyHost());

    [Fact]
    public void ProjectFile_RecognizesSqliteAndReadsVersion_WithoutWriting()
    {
        TestProjectDb.Create(_path);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        byte[] before = File.ReadAllBytes(_path);

        Assert.True(ProjectFile.IsSqliteDatabase(_path));
        Assert.Equal(DatabaseService.SchemaVersion, ProjectFile.ReadSchemaVersion(_path));
        Assert.False(ProjectFile.IsInUse(_path));

        Assert.Equal(before, File.ReadAllBytes(_path));
    }

    [Fact]
    public void ProjectFile_RejectsNonSqlite_AndMissingFile()
    {
        File.WriteAllText(_path, "not a database at all");
        Assert.False(ProjectFile.IsSqliteDatabase(_path));
        Assert.False(ProjectFile.IsSqliteDatabase(_path + ".missing"));
        Assert.Null(ProjectFile.ReadSchemaVersion(_path + ".missing"));
    }

    [Fact]
    public void Open_AppliesParametricSteelBindings()
    {
        using (var db = new DatabaseService(_path))
            new ParametricSteelSectionProjectService(db).GenerateAndSave(new CScore.CrossSection(),
                CScore.ParametricSteel.ParametricSteelSectionDefinition.RolledIBeam(0.298, 0.299, 0.009, 0.014, 0.018));
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        using var ctx = Open();

        // Без привязки проверки СП 16 в консоли распознавали бы профиль по контуру, а не брали его из параметрики.
        var profile = Assert.Single(ctx.CrossSections).TryGetParametricSteelProfile();
        Assert.NotNull(profile);
        Assert.Equal(0.018, profile.R, 12);
    }

    [Fact]
    public void ProjectFile_IsInUse_WhileDatabaseServiceHoldsIt()
    {
        TestProjectDb.Create(_path);
        using (Open())
            Assert.True(ProjectFile.IsInUse(_path));
        Assert.False(ProjectFile.IsInUse(_path));
    }

    [Fact]
    public void Open_LoadsCollectionsAndSettings()
    {
        var (sectionId, taskId) = TestProjectDb.Create(_path);

        using var ctx = Open();

        Assert.Equal(2, ctx.Materials.Count);
        Assert.Equal(sectionId, Assert.Single(ctx.CrossSections).Id);
        Assert.Equal(3, ctx.CrossSections[0].Areas.Count);
        Assert.Equal(taskId, Assert.Single(ctx.CalcTasks).Id);
        Assert.Empty(ctx.BarForceSets);
        Assert.NotNull(ctx.CalcSettings);
        Assert.Contains("sp63_deflection", ctx.ReportProviders.SupportedKinds);
    }

    [Fact]
    public async Task CalcTask_RunsThroughHeadlessContext_ReturnsSavedResult()
    {
        TestProjectDb.Create(_path);
        using var ctx = Open();

        var result = await CalcTaskExecutor.RunAsync(ctx, ctx.CalcTasks[0]);

        Assert.NotNull(result);
        Assert.NotEqual(0, result.Id);
        Assert.Equal(result.Status, ctx.Db.GetCalcResultById(result.Id)!.Status);
        Assert.Empty(((RecordingNotifier)ctx.Notifier).Errors);
    }

    [Fact]
    public async Task CalcTask_MissingSection_ReturnsNull()
    {
        TestProjectDb.Create(_path);
        using var ctx = Open();
        var task = ctx.CalcTasks[0];
        task.SectionId = 999;

        Assert.Null(await CalcTaskExecutor.RunAsync(ctx, task));
    }
}
