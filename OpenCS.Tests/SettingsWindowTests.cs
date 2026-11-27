using System.IO;
using System.Windows.Controls;
using System.Windows.Media;
using OpenCS.Services;
using OpenCS.Utilites;
using OpenCS.ViewModels;
using OpenCS.Views;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Окно настроек: неверное поле не сохраняется и видно, верное — пишется через службу, прочее не теряется.</summary>
[Collection("AppViewModel pages")]
public sealed class SettingsWindowTests
{
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

    [Fact]
    public void InvalidField_NotSaved_TabMarked_ThenValidSaved_KeepsOtherSettings() => RunSta(() =>
    {
        string path = Path.Combine(Path.GetTempPath(), $"opencs-settingswnd-{Guid.NewGuid():N}.db");
        var app = new AppViewModel(new LogService(), new NullFileDialogService(), path, new RecordingNotifier());
        Loc.SetLanguage("ru-RU");
        try
        {
            app.CalcSettings.ImportedBarRebarTolerancePercent = 12;
            app.db.SaveCalcSettings(app.CalcSettings);

            var w = new SettingsWindow(app);
            var grid = (TextBox)w.FindName("GridDensityBox");
            var gamma = (TextBox)w.FindName("Sp20GammaGBox");
            var status = (TextBlock)w.FindName("StatusText");
            var tabs = (TabControl)w.FindName("Tabs");

            grid.Text = "abc";
            Assert.Single(w.FieldErrors);
            Assert.NotEqual("", status.Text);
            Assert.Equal(Brushes.Crimson, ((TabItem)tabs.Items[0]).Foreground);
            Assert.False(w.TrySaveAndApply());
            Assert.Equal(40, app.db.LoadCalcSettings().GridDensity);

            grid.Text = "50";
            gamma.Text = "1,3";
            Assert.Empty(w.FieldErrors);
            Assert.True(w.TrySaveAndApply());
            var saved = app.db.LoadCalcSettings();
            Assert.Equal(50, saved.GridDensity);
            Assert.Equal(1.3, saved.Sp20GammaFPermanent);
            Assert.Equal(12, saved.ImportedBarRebarTolerancePercent);
            Assert.Equal(50, app.CalcSettings.GridDensity);
            w.Close();
        }
        finally
        {
            Loc.SetLanguage(null);
            app.db.Dispose();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (var suffix in new[] { "", "-wal", "-shm" })
                try { File.Delete(path + suffix); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    });
}
