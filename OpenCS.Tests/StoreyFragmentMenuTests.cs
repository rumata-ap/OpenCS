using System.Globalization;
using System.Windows;
using CScore.Fem;
using CScore.Fem.Fragment;
using Microsoft.Data.Sqlite;
using OpenCS.Utilites;
using OpenCS.ViewModels;
using OpenCS.Views;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Команда «Фрагмент по отметкам…»: сборка из меню и окна диалога и отчёта (без запуска программы).</summary>
public sealed class StoreyFragmentMenuTests : IDisposable
{
    readonly string _path = Path.Combine(Path.GetTempPath(), $"opencs-fragment-menu-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (File.Exists(_path)) File.Delete(_path); } catch (IOException) { }
    }

    [Fact]
    public async Task Run_Success_CreatesSchemaAndLog()
    {
        using var db = new DatabaseService(_path);
        var parent = StoreyFragmentDbFixture.CreateParent(db);

        var result = await StoreyFragmentMenuFlow.RunAsync(db, parent, new StoreyFragmentRequest(1.5, 4.5, "Этаж 2"));

        var schema = Assert.IsType<FemSchema>(result.Schema);
        Assert.Equal("Этаж 2", schema.Tag);
        Assert.Equal(parent.Id, schema.ParentSchemaId);
        Assert.False(result.IsError);
        Assert.DoesNotContain(result.Rows, r => r.IsError);
        Assert.Equal(Loc.S("StoreyFragmentBuilt"), result.LogMessage);
    }

    [Fact]
    public async Task Run_Refusal_NoSchema_ErrorRows()
    {
        using var db = new DatabaseService(_path);
        var parent = StoreyFragmentDbFixture.CreateParent(db);
        db.LoadAll();
        int before = db.FemSchemas.Count;

        // z₁ по плите S1 (z = 3) — граница прошла по перекрытию.
        var result = await StoreyFragmentMenuFlow.RunAsync(db, parent, new StoreyFragmentRequest(3, 6, "Этаж"));

        Assert.Null(result.Schema);
        Assert.True(result.IsError);
        Assert.Contains(result.Rows, r => r.IsError);
        Assert.True(result.Rows[0].IsError);
        Assert.Equal(Loc.S("StoreyFragmentFailed"), result.LogMessage);
        db.LoadAll();
        Assert.Equal(before, db.FemSchemas.Count);
    }

    [Fact]
    public void Dialogs_ParseXaml_NameFollowsLevels_ReportRows()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var dialog = new StoreyFragmentDialog("Каркас", (0, 9));
                ShowOffscreen(dialog);
                Assert.Equal(string.Format(Loc.S("StoreyFragmentMeshRange"), "0", "9"), dialog.rangeText.Text);
                dialog.z1Box.Text = "1,5";
                dialog.z2Box.Text = "4,5";
                Assert.Equal(StoreyFragmentForm.DefaultName("Каркас", 1.5, 4.5, CultureInfo.CurrentCulture), dialog.nameBox.Text);
                dialog.nameBox.Text = "Своё";
                dialog.z2Box.Text = "5";
                Assert.Equal("Своё", dialog.nameBox.Text);
                Assert.Equal((1.5, 5.0, "Своё"), dialog.ReadRequest(out _) is { } r ? (r.Z1, r.Z2, r.Name) : default);
                dialog.Close();

                var rows = StoreyFragmentReportRows.From(
                [
                    new FemValidationDiagnostic("w", "предупреждение", false),
                    new FemValidationDiagnostic("e", "ошибка", true),
                ]);
                var report = new StoreyFragmentReportDialog("Этаж", "заголовок", rows);
                ShowOffscreen(report);
                Assert.Equal(2, report.rowsList.Items.Count);
                report.Close();
            }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(error);
    }

    static void ShowOffscreen(Window window)
    {
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -20000;
        window.Top = -20000;
        window.ShowActivated = false;
        window.Show();
        window.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }
}
