using CScore.Fem;
using CScore.Fem.Fragment;
using Microsoft.Data.Sqlite;
using OpenCS.Services;
using OpenCS.Utilites;
using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Шапка фрагмента на странице схемы: сверка родителя при открытии, отчёт сборки.</summary>
public sealed class StoreyFragmentBannerVMTests : IDisposable
{
    readonly string _path = Path.Combine(Path.GetTempPath(), $"opencs-fragment-banner-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (File.Exists(_path)) File.Delete(_path); } catch (IOException) { }
    }

    [Fact]
    public async Task NotFragment_Hidden_NoCheck()
    {
        using var db = new DatabaseService(_path);
        var parent = StoreyFragmentDbFixture.CreateParent(db);

        var vm = new StoreyFragmentBannerVM(db, parent);
        Assert.False(vm.IsVisible);
        Assert.Equal(FragmentParentStatus.NotFragment, await vm.LoadAsync());
        Assert.Null(vm.StatusText);
    }

    [Fact]
    public async Task Fragment_Unchanged_Changed_Deleted()
    {
        using var db = new DatabaseService(_path);
        var parent = StoreyFragmentDbFixture.CreateParent(db);
        var fragment = StoreyFragmentService.Build(db, parent.Id, new StoreyFragmentRequest(1.5, 4.5, "Этаж 2")).Schema!;

        var vm = new StoreyFragmentBannerVM(db, fragment);
        Assert.True(vm.IsVisible);
        Assert.Equal(Loc.S("StoreyFragmentBannerTitle"), vm.Title);
        Assert.True(vm.IsChecking);
        Assert.Equal(FragmentParentStatus.Unchanged, await vm.LoadAsync());
        Assert.False(vm.IsChecking);
        Assert.False(vm.IsWarning);
        Assert.Equal(Loc.S("StoreyFragmentParentUnchanged"), vm.StatusText);

        Exec($"UPDATE fem_element_loads SET values_json='[-6000]' WHERE schema_id={parent.Id} AND load_kind='uniform'");
        vm = new StoreyFragmentBannerVM(db, fragment);
        Assert.Equal(FragmentParentStatus.Changed, await vm.LoadAsync());
        Assert.True(vm.IsWarning);
        Assert.Equal(Loc.S("StoreyFragmentParentChanged"), vm.StatusText);

        db.DeleteFemSchema(parent);
        vm = new StoreyFragmentBannerVM(db, fragment);
        Assert.Equal(FragmentParentStatus.ParentDeleted, await vm.LoadAsync());
        Assert.True(vm.IsWarning);
        Assert.Equal(Loc.S("StoreyFragmentParentDeleted"), vm.StatusText);
    }

    [Fact]
    public void ReportRows_FromStoredReport()
    {
        var schema = new FemSchema
        {
            Id = 5, Tag = "Этаж", ParentSchemaId = 1, FragmentZ1 = 1.5, FragmentZ2 = 4.5,
            FragmentBuildReportJson = StoreyFragmentBuildReport.ToJson(
                [new FemValidationDiagnostic("w", "не перенесено: шарниры", false)]),
        };
        using var db = new DatabaseService(_path);
        var vm = new StoreyFragmentBannerVM(db, schema);
        var row = Assert.Single(vm.ReportRows);
        Assert.Equal("не перенесено: шарниры", row.Message);
        Assert.True(vm.HasReport);
    }

    void Exec(string sql)
    {
        using var c = new SqliteConnection($"Data Source={_path};Pooling=False");
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
