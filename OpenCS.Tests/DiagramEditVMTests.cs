using CScore;
using CSmath;
using OpenCS.Services;
using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Страница диаграммы: черновик до «Сохранить», σ в МПа на странице и кПа в базе, CSV с запятой.</summary>
[Collection("AppViewModel pages")]
public class DiagramEditVMTests
{
    static void WithApp(Action<AppViewModel> test)
    {
        string path = Path.Combine(Path.GetTempPath(), $"opencs-diagram-vm-{Guid.NewGuid():N}.db");
        var app = new AppViewModel(new LogService(), new NullFileDialogService(), path);
        try { test(app); }
        finally
        {
            app.db.Dispose();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (var suffix in new[] { "", "-wal", "-shm" })
                try { File.Delete(path + suffix); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    static Diagramm Pool(AppViewModel app)
    {
        var d = new Diagramm
        {
            Tag = "D", Type = DiagrammType.Custom, CalcType = CalcType.C, MaterialType = MatType.Concrete,
            Ic = new LSpline([-0.0035, 0.0], [-14500.0, 0.0]),
            It = new LSpline([0.0, 0.0001], [0.0, 1050.0])
        };
        Assert.Empty(DiagramAuthoring.Save(app.db, d));
        app.DiagramsLive.Add(d);
        return d;
    }

    [Fact]
    public void Load_ShowsMPa_Save_WritesKPa() => WithApp(app =>
    {
        var d = Pool(app);
        var vm = new DiagramEditVM(d, app);
        Assert.Equal(-14.5, vm.Points[0].Sig, 9);
        Assert.False(vm.IsDirty);

        vm.Points[0].Sig = -13.2;
        Assert.True(vm.IsDirty);
        Assert.EndsWith("*", vm.Title);
        Assert.Empty(vm.Save());
        Assert.Equal(-13200, d.Ic.Y[0], 6);
        Assert.False(vm.IsDirty);
        Assert.DoesNotContain("*", vm.Title);
    });

    [Fact]
    public void EditWithoutSave_PoolUntouched_DiscardRestores() => WithApp(app =>
    {
        var d = Pool(app);
        var vm = new DiagramEditVM(d, app);
        vm.Tag = "Другая";
        vm.MaterialType = MatType.Steel;
        vm.Points.RemoveAt(0);
        Assert.Equal("D", d.Tag);
        Assert.Equal(MatType.Concrete, d.MaterialType);
        Assert.Equal(2, d.Ic.X.Length);

        ((OpenCS.Utilites.IUnsavedChanges)vm).DiscardChanges();
        Assert.Equal("D", vm.Tag);
        Assert.Equal(3, vm.Points.Count);
        Assert.False(vm.IsDirty);
    });

    [Fact]
    public void Save_Invalid_NothingChanged() => WithApp(app =>
    {
        var d = Pool(app);
        var vm = new DiagramEditVM(d, app);
        vm.Tag = "";
        vm.Points.Add(new DiagramPoint { Eps = -0.0035, Sig = -20 });   // повтор ε
        Assert.NotEmpty(vm.Save());
        Assert.Equal("D", d.Tag);
        Assert.Equal(-14500, d.Ic.Y[0], 6);
        Assert.True(vm.IsDirty);
    });

    [Fact]
    public void NewDiagram_SavedOnce_AddedToPoolAndTree() => WithApp(app =>
    {
        var d = new Diagramm
        {
            Tag = "Новая", Type = DiagrammType.Custom, CalcType = CalcType.C, MaterialType = MatType.Concrete,
            Ic = new LSpline([-0.0035, 0.0], [-14500.0, 0.0]),
            It = new LSpline([0.0, 0.0001], [0.0, 1050.0])
        };
        var vm = new DiagramEditVM(d, app, isNew: true);
        Assert.Empty(vm.Save());
        Assert.Empty(vm.Save());
        Assert.Single(app.db.Diagrams);
        Assert.Single(app.DiagramsLive);
        Assert.False(vm.IsNew);
    });

    /// <summary>Ручная проверка B4: пустое обозначение → «Сохранить» на странице → предупреждение в журнале.</summary>
    [Fact]
    public void Page_SaveButton_EmptyTag_WarnsInLog()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                WithApp(app =>
                {
                    var page = new OpenCS.Views.DiagramPage(Pool(app), app);
                    var vm = (DiagramEditVM)page.DataContext;
                    vm.Tag = "";
                    var save = (System.Windows.Controls.Button)page.FindName("saveButton");
                    save.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                    var log = ((LogService)app.LogService).LogEntries.Select(e => e.Message).ToList();
                    Assert.Contains(OpenCS.Utilites.Loc.S("DiagramErrTagEmpty"), log);
                    Assert.True(vm.IsDirty);
                });
            }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        Assert.Null(error);
    }

    [Fact]
    public void ImportCsv_SemicolonWithDecimalComma() => WithApp(app =>
    {
        var vm = new DiagramEditVM(Pool(app), app);
        string csv = Path.Combine(Path.GetTempPath(), $"opencs-diag-{Guid.NewGuid():N}.csv");
        File.WriteAllLines(csv, ["ε;σ, МПа", "-0,0035;-14,5", "0;0", "0,0001;1,05", "мусор;1"]);
        try
        {
            Assert.Equal(1, vm.ImportCsv(csv));
            Assert.Equal(3, vm.Points.Count);
            Assert.Equal(-14.5, vm.Points[0].Sig, 9);
            Assert.Equal(0.0001, vm.Points[2].Eps, 12);
            Assert.Contains("-14.5", vm.ExportCsv(";"));
        }
        finally { File.Delete(csv); }
    });
}
