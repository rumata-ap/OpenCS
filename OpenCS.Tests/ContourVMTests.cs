using CScore;
using OpenCS.Services;
using OpenCS.Utilites;
using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests;

/// <summary>
/// Страница контура: правки пишутся только по «Сохранить» (раньше перенумерация, импорт CSV, сдвиг и масштаб сразу
/// писали в базу), «*» и откат, CSV вершин с десятичной запятой.
/// </summary>
[Collection("AppViewModel pages")]
public class ContourVMTests
{
    static void WithApp(Action<AppViewModel> test) => RunSta(() =>
    {
        string path = Path.Combine(Path.GetTempPath(), $"opencs-contourvm-{Guid.NewGuid():N}.db");
        _dbPath = path;
        var app = new AppViewModel(new LogService(), new NullFileDialogService(), path);
        try { test(app); }
        finally
        {
            app.db.Dispose();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (var suffix in new[] { "", "-wal", "-shm" })
                try { File.Delete(path + suffix); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    });

    static string _dbPath = "";

    static ContourVM Square(AppViewModel app, string tag = "K1")
    {
        var vm = new ContourVM { mvm = app };
        vm.Tag = tag;
        foreach (var (x, y) in new[] { (0.0, 0.0), (0.4, 0.0), (0.4, 0.6), (0.0, 0.6) })
            vm.Contour.Points.Add(new StressPoint(x, y));
        return vm;
    }

    /// <summary>Строки контуров в файле базы — отдельным соединением (мимо пула в памяти).</summary>
    static int RowsInDb(AppViewModel app)
    {
        using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_dbPath};Pooling=False");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM contours";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    [Fact]
    public void NewContour_RenumberDoesNotWrite_SaveAddsToPool() => WithApp(app =>
    {
        var vm = Square(app);
        Assert.True(vm.IsDirty);
        vm.RenumPointCommand.Execute(null);
        Assert.Equal(0, RowsInDb(app));                 // раньше — «призрак» в базе
        Assert.Empty(app.db.Contours);

        Assert.Empty(vm.Save());
        Assert.False(vm.IsDirty);
        Assert.Equal(1, RowsInDb(app));
        Assert.Single(app.db.Contours);
        Assert.True(vm.Contour.IsClosed);
    });

    [Fact]
    public void InvalidContour_NotSaved_StaysDirty() => WithApp(app =>
    {
        var vm = new ContourVM { mvm = app };
        vm.Tag = "Бабочка";
        foreach (var (x, y) in new[] { (0.0, 0.0), (1.0, 1.0), (1.0, 0.0), (0.0, 1.0) })
            vm.Contour.Points.Add(new StressPoint(x, y));
        Assert.NotEmpty(vm.Save());
        Assert.True(vm.IsDirty);
        Assert.Equal(0, RowsInDb(app));
    });

    [Fact]
    public void SavedContour_EditThenDiscard_RestoresPoolObject() => WithApp(app =>
    {
        var vm = Square(app);
        Assert.Empty(vm.Save());
        var pool = Assert.Single(app.db.Contours);

        var page = new ContourVM(pool) { mvm = app };
        Assert.False(page.IsDirty);
        page.Tag = "K1-изм";
        page.Contour.Points[1].X = 0.5;
        page.CommitPointMove();
        Assert.True(page.IsDirty);

        ((IUnsavedChanges)page).DiscardChanges();
        Assert.False(page.IsDirty);
        Assert.Equal("K1", pool.Tag);
        Assert.Equal(0.4, pool.Points[1].X, 12);
    });

    [Fact]
    public void ImportCsv_CommaDecimals_SemicolonFields_Dirty_NotWritten() => WithApp(app =>
    {
        string csv = Path.Combine(Path.GetTempPath(), $"opencs-contour-{Guid.NewGuid():N}.csv");
        File.WriteAllLines(csv, ["X;Y", "-0,2;-0,3", "0,2;-0,3", "0,2;0,3", "abc;1", "-0,2;0,3"]);
        try
        {
            var vm = new ContourVM { mvm = app };
            vm.Tag = "CSV";
            int skipped = vm.ImportCsv(csv);
            Assert.Equal(1, skipped);
            Assert.True(vm.Contour.IsClosed);
            Assert.Equal(5, vm.Contour.Points.Count);   // 4 + замыкающая
            Assert.Equal(-0.2, vm.Contour.Points[0].X, 12);
            Assert.True(vm.IsDirty);
            Assert.Equal(0, RowsInDb(app));
            Assert.Empty(vm.Save());
            Assert.Equal(0.24, Math.Abs(new GeoProps(vm.Contour).A), 9);
        }
        finally { File.Delete(csv); }
    });

    [Fact]
    public void CircleCsv_RoundTrip_CommaAndBadRows() => RunSta(() =>
    {
        var parsed = CircleCsv.Parse(["Tag;X;Y;Radius", "Ø25;0,1;-0,2;0,0125", "bad;x;0;1", "zero;0;0;0"], out int skipped);
        var c = Assert.Single(parsed);
        Assert.Equal(2, skipped);
        Assert.Equal(0.0125, c.Radius, 12);
        Assert.Equal(0.025, c.Diameter, 12);
        var back = CircleCsv.Parse(CircleCsv.Format(parsed), out skipped);
        Assert.Equal(0, skipped);
        Assert.Equal(c.X, Assert.Single(back).X, 12);
    });

    static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null) throw new Xunit.Sdk.XunitException(error.ToString());
    }
}
