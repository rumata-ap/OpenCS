using System.Windows;
using OpenCS.Services;
using OpenCS.Tasks;
using OpenCS.ViewModels;
using OpenCS.Views;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Диалог «Наборы усилий по вариантам пакета» (спека 2026-10-17 «наборы по вариантам», срез 2).</summary>
[Collection(nameof(FemCsfeaBatchForceSetsVMTests))]
public sealed class FemCsfeaBatchForceSetsVMTests
{
    public FemCsfeaBatchForceSetsVMTests() => FemCsfeaBatchForceSetsVM.ResetSession();

    static FemCsfeaBatchSummary Variant(string tag, string type, bool completed = true,
        string kind = FemCsfeaBatchItemKind.Combination) => new()
    {
        Tag = tag, Type = type, Kind = kind, RecordN = 1, Done = true, Completed = completed, LoadFactor = completed ? 1 : 0.5,
    };

    /// <summary>C: 2 дошедших из 3; N: 0 из 1; загружения: 1 из 1.</summary>
    static FemCsfeaResultSummary Summary() => new()
    {
        Batch =
        [
            Variant("C1", "C"), Variant("C2", "C", completed: false), Variant("C3", "C"),
            Variant("N1", "N", completed: false), Variant("q", "Q", kind: FemCsfeaBatchItemKind.LoadCase),
        ],
    };

    static FemCsfeaBatchForceSetsVM Vm(FemCsfeaForceSetScope? selection = null) =>
        new(Summary(), "Пакет", shells: 100, beams: 10, selection, selectedShells: 3, selectedBeams: 1);

    [Fact]
    public void Groups_CountsAndDefaults_EstimateWholeSchema()
    {
        var vm = Vm();

        Assert.Equal(["C", "N", FemCsfeaBatchGroup.LoadCases], vm.Groups.Select(g => g.Key));
        Assert.Equal([(2, 3), (0, 1), (1, 1)], vm.Groups.Select(g => (g.Included, g.Total)));
        Assert.False(vm.Groups[1].IsAvailable);
        // По умолчанию — доступные группы комбинаций; загружения — по выбору.
        Assert.Equal(["C"], vm.CheckedGroups);
        Assert.False(vm.HasSelection);
        Assert.True(vm.UseWholeSchema);
        Assert.Null(vm.Scope);
        Assert.Equal(2, vm.SetCount);
        Assert.Equal(2 * 100 + 2 * 2 * 10, vm.RowCount);
        Assert.True(vm.ShowPairWarning);
        Assert.True(vm.CanCreate);

        vm.Groups[1].IsChecked = true;
        Assert.False(vm.Groups[1].IsChecked);
    }

    [Fact]
    public void LoadCaseTypes_SetPerType_AndRememberedInSession()
    {
        var vm = Vm();
        vm.Groups[0].IsChecked = false;
        vm.Groups[2].IsChecked = true;
        Assert.Equal(["C"], vm.CheckedTypes);
        Assert.False(vm.ShowPairWarning);

        vm.LoadCaseTypes.Single(t => t.Type == "N").IsChecked = true;
        vm.LoadCaseTypes.Single(t => t.Type == "NL").IsChecked = true;
        Assert.Equal(3 * 2, vm.SetCount);
        Assert.Equal(3 * (100 + 2 * 10), vm.RowCount);

        foreach (var t in vm.LoadCaseTypes) t.IsChecked = false;
        Assert.False(vm.CanCreate);
        vm.LoadCaseTypes[0].IsChecked = true;
        vm.Remember();

        var again = Vm();
        Assert.Equal([FemCsfeaBatchGroup.LoadCases], again.CheckedGroups);
        Assert.Equal(["C"], again.CheckedTypes);
    }

    [Fact]
    public void Selection_PresetScope_LargeWarning()
    {
        var selection = new FemCsfeaForceSetScope([1, 2, 3, 50], ["Пл-1", "Ст-2"]);
        var vm = Vm(selection);

        Assert.True(vm.UseSelection);
        Assert.Same(selection, vm.Scope);
        Assert.Equal(2 * 3 + 2 * 2 * 1, vm.RowCount);

        vm.UseWholeSchema = true;
        Assert.Null(vm.Scope);
        vm.Remember();
        Assert.False(Vm(selection).UseSelection);

        var big = new FemCsfeaBatchForceSetsVM(Summary(), "Пакет", shells: 300_000, beams: 0);
        Assert.True(big.IsLarge);
        Assert.False(vm.IsLarge);
    }

    [Fact]
    public void Dialog_ParsesXaml()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var dialog = new FemCsfeaBatchForceSetsDialog(Vm(new FemCsfeaForceSetScope([1], ["Пл-1"])));
                dialog.WindowStartupLocation = WindowStartupLocation.Manual;
                dialog.Left = dialog.Top = -20000;
                dialog.ShowActivated = false;
                dialog.Show();
                dialog.UpdateLayout();
                Assert.Equal(3, dialog.GroupsList.Items.Count);
                Assert.True(dialog.SelectionRadio.IsChecked);
                Assert.False(dialog.WholeRadio.IsChecked);
                dialog.Close();
            }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(error);
    }
}
