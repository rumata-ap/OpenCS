using System.Text.Json.Nodes;
using System.Windows;
using CScore;
using OpenCS.Services;
using OpenCS.Utilites;
using OpenCS.ViewModels;
using OpenCS.Views;
using Xunit;

namespace OpenCS.Tests;

/// <summary>
/// Диалог задачи: числа полей — с запятой или точкой; нераспознанное — ошибка и задача не создаётся (раньше молча 0);
/// имя по умолчанию — по номеру; итоговая задача проходит проверку службы.
/// </summary>
[Collection("AppViewModel pages")]
public class CalcTaskDialogVMTests
{
    static void WithDialog(Action<AppViewModel, CalcTaskPropsDlgVM, RecordingNotifier, Func<bool>> test) => RunSta(() =>
    {
        string path = Path.Combine(Path.GetTempPath(), $"opencs-taskdlg-{Guid.NewGuid():N}.db");
        var notifier = new RecordingNotifier();
        var app = new AppViewModel(new LogService(), new NullFileDialogService(), path, notifier);
        Loc.SetLanguage("ru-RU");
        try
        {
            var section = new CrossSection { Tag = "К1" };
            app.db.SaveCrossSection(section);
            app.CrossSections.Add(section);
            var plate = new PlateSection { Tag = "Плита", H = 0.2 };
            app.db.SavePlateSection(plate);
            if (!app.PlateSections.Contains(plate)) app.PlateSections.Add(plate);
            var vm = new CalcTaskPropsDlgVM(app, null, new Window());
            bool accepted = false;
            vm.Accept = () => accepted = true;
            test(app, vm, notifier, () => accepted);
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

    static double Number(string json, string name) => JsonNode.Parse(json)![name]!.GetValue<double>();

    [Fact]
    public void StrainState_CommaDecimal_Saved() => WithDialog((app, vm, notifier, accepted) =>
    {
        vm.Kind = "strain_state";
        vm.SelectedSection = app.CrossSections[0];
        vm.ManualN = "-12,5";
        vm.ManualMx = "3.25";
        vm.ManualMy = "0";
        vm.OkCommand.Execute(null);
        Assert.True(accepted(), string.Join("; ", notifier.Warnings));
        Assert.Equal(-12.5, Number(vm.Result!.ParamsJson, "N"));
        Assert.Equal(3.25, Number(vm.Result.ParamsJson, "Mx"));
        Assert.Equal("Задача 1", vm.Result.Tag);
    });

    [Fact]
    public void StrainState_NotANumber_Refused() => WithDialog((app, vm, notifier, accepted) =>
    {
        vm.Kind = "strain_state";
        vm.SelectedSection = app.CrossSections[0];
        vm.ManualN = "abc";
        vm.OkCommand.Execute(null);
        Assert.False(accepted());
        Assert.Null(vm.Result);
        Assert.Contains(notifier.Warnings, w => w.Contains("abc"));
    });

    [Fact]
    public void Eta_CommaDecimal_And_BadMu() => WithDialog((app, vm, notifier, accepted) =>
    {
        vm.Kind = "strain_state";
        vm.SelectedSection = app.CrossSections[0];
        vm.ManualN = "-100";
        vm.ManualMx = "10";
        vm.ManualMy = "0";
        vm.EtaEnabled = true;
        vm.EtaL = "3,6";
        vm.OkCommand.Execute(null);
        Assert.True(accepted(), string.Join("; ", notifier.Warnings));
        Assert.Equal(3.6, Number(vm.Result!.ParamsJson, "etaL"), 9);

        vm.EtaMuX = "x";
        vm.OkCommand.Execute(null);
        Assert.Null(vm.Result);
    });

    [Fact]
    public void Plate_CommaDecimal_Saved() => WithDialog((app, vm, notifier, accepted) =>
    {
        vm.Kind = "shell_simpl_wa_uls";
        vm.SelectedShellSimplSection = app.PlateSections[0];
        vm.ShellSimplMx = "0,5";
        vm.ShellSimplNx = "-10,25";
        vm.OkCommand.Execute(null);
        Assert.True(accepted(), string.Join("; ", notifier.Warnings));
        Assert.Equal(0.5, Number(vm.Result!.ParamsJson, "mx"));
        Assert.Equal(-10.25, Number(vm.Result.ParamsJson, "nx"));

        vm.ShellSimplPhi1 = "один";
        vm.OkCommand.Execute(null);
        Assert.Null(vm.Result);
        Assert.Contains(notifier.Warnings, w => w.Contains("φ1"));
    });
}
