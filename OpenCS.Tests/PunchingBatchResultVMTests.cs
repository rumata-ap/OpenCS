using System.IO;
using CScore;
using OpenCS.Services;
using OpenCS.Tasks;
using OpenCS.Utilites;
using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Окно результата пакета продавливания: строки, столбцы вариантов, огибающая, рисунок строки и огибающей.</summary>
[Collection("Loc language")]
public sealed class PunchingBatchResultVMTests
{
    static readonly PunchingTaskParams P = new()
    {
        Area = new PunchingAreaParams { Shape = "rectangle", Bx = 0.25, By = 0.8 },
        Slab = new PunchingSlabParams { H = 0.2, Ax = 0.04, Ay = 0.03, RbtOverride = 1050 },
        Reinforcement = new PunchingReinforcementParams { Enabled = true, Diameter = 0.006, Count = 2, Step = 0.05, RswOverride = 170_000 }
    };

    static (CalcResult, CalcTask) Run()
    {
        var set = new ForceSet
        {
            Id = 4, Kind = "bar", Tag = "РСУ",
            Items =
            [
                new LoadItem { Id = 41, Num = 1, Label = "А", N = -300, Mx = 10, My = 5 },
                new LoadItem { Id = 42, Num = 2, Label = "Б", N = -600, Mx = 30, My = 20 },
                new LoadItem { Id = 43, Num = 3, Label = "В", N = -500, Mx = 0, My = 0 },
            ]
        };
        var task = new CalcTask { Id = 8, Tag = "Узел", Kind = PunchingBatchRunner.Kind, CalcType = CalcType.C, ForceSetId = 4, ParamsJson = P.ToJson() };
        string path = Path.Combine(Path.GetTempPath(), $"opencs-punch-vm-{Guid.NewGuid():N}.db");
        try
        {
            using var db = new DatabaseService(path);
            db.ForceSets.Add(set);
            return (PunchingBatchRunner.Run(task, CalcSettings.Default, new TaskRunContext { Database = db }), task);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { File.Delete(path); } catch (IOException) { }
        }
    }

    [Fact]
    public void Rows_Columns_Envelope_AndGoverningSelected()
    {
        var (result, task) = Run();
        var vm = new PunchingBatchResultVM(result, task);
        Assert.False(vm.HasError);
        Assert.Equal(3, vm.Rows.Count);
        Assert.Equal(["closed", "pylon_broken"], vm.VariantColumns.Select(c => c.Id).ToArray());
        Assert.All(vm.Rows, r => Assert.Equal(2, r.Ratios.Count));
        Assert.Equal(2, vm.SelectedRow!.Num);
        Assert.Equal(vm.SelectedRow.Ratios[1], vm.SelectedRow.KText);   // 1,019 — округление вверх, как у столбца контура
        Assert.Equal("pylon_broken", vm.SelectedVariantId);
        Assert.Equal(BatchVerdictTone.Fail, vm.Tone);
        Assert.Contains("1,019", vm.VerdictText);
        Assert.Equal(2, vm.Envelope.Count);
        Assert.Single(vm.Envelope, e => e.Governs && e.Id == "pylon_broken");
    }

    [Fact]
    public void Scene_FollowsSelectedRow_OrEnvelope()
    {
        var (result, task) = Run();
        var vm = new PunchingBatchResultVM(result, task);
        var rowA = vm.Rows.Single(r => r.Num == 1);
        vm.SelectedRow = rowA;
        var closedA = vm.Scene!.Variants.Single(v => v.Id == "closed");
        Assert.Equal(rowA.Source.Ratios["closed"]!.Value, closedA.Utilization, 12);
        Assert.Contains("№ 1", vm.SceneCaption);

        vm.ShowEnvelope = true;
        var closedEnv = vm.Scene!.Variants.Single(v => v.Id == "closed");
        Assert.True(closedEnv.Utilization > closedA.Utilization);
        Assert.False(vm.ShowRow);

        // Строка огибающей выбирает строку с наибольшим Кисп этого контура и показывает её.
        vm.ActivateEnvelope(vm.Envelope.Single(e => e.Id == "closed"));
        Assert.False(vm.ShowEnvelope);
        Assert.Equal(vm.Envelope.Single(e => e.Id == "closed").Num, vm.SelectedRow!.Num);
        Assert.Equal("closed", vm.SelectedVariantId);
        Assert.True(vm.Envelope.Single(e => e.Id == "closed").IsCurrent);
    }

    [Fact]
    public void CanvasClick_MarksEnvelopeRow_WithoutSwitchingRow()
    {
        var (result, task) = Run();
        var vm = new PunchingBatchResultVM(result, task);
        Assert.True(vm.Envelope.Single(e => e.Id == "pylon_broken").IsCurrent);   // определяющий контур при открытии
        var row = vm.Rows.Single(r => r.Num == 1);
        vm.SelectedRow = row;

        vm.SelectedVariantId = "closed";   // щелчок по контуру на рисунке

        Assert.Equal(["closed"], vm.Envelope.Where(e => e.IsCurrent).Select(e => e.Id).ToArray());
        Assert.Same(row, vm.SelectedRow);
        Assert.False(vm.ShowEnvelope);
    }

    [Fact]
    public void EnvelopeRow_ClickedAgain_AfterLeavingIt_SwitchesBack()
    {
        var (result, task) = Run();
        var vm = new PunchingBatchResultVM(result, task);
        var closed = vm.Envelope.Single(e => e.Id == "closed");

        vm.ActivateEnvelope(closed);
        Assert.False(vm.ShowEnvelope);

        // Ушли в огибающую — повторный щелчок по той же строке снова показывает её строку набора.
        vm.ShowEnvelope = true;
        vm.ActivateEnvelope(closed);
        Assert.False(vm.ShowEnvelope);
        Assert.Equal(closed.Num, vm.SelectedRow!.Num);

        // Выбрали другую строку набора и другой контур — то же самое.
        vm.SelectedRow = vm.Rows.First(r => r.Num != closed.Num);
        Assert.NotEqual(closed.Num, vm.SelectedRow!.Num);
        vm.ActivateEnvelope(closed);
        Assert.Equal(closed.Num, vm.SelectedRow!.Num);
        Assert.Equal("closed", vm.SelectedVariantId);
        Assert.Equal(["closed"], vm.Envelope.Where(e => e.IsCurrent).Select(e => e.Id).ToArray());
    }

    [Fact]
    public void View_MarkupLoads_WithVariantColumns()
    {
        var (result, task) = Run();
        Exception? error = null;
        int columns = 0;
        var thread = new System.Threading.Thread(() =>
        {
            try
            {
                var resources = new System.Windows.ResourceDictionary
                {
                    ["BoolToVisibility"] = new System.Windows.Controls.BooleanToVisibilityConverter(),
                    ["BatchVerdictRowStyle"] = new System.Windows.Style(typeof(System.Windows.Controls.DataGridRow))
                };
                var view = new OpenCS.Views.PunchingBatchResultView(new PunchingBatchResultVM(result, task), resources);
                columns = view.RowColumnCount;
            }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(System.Threading.ApartmentState.STA);
        thread.Start();
        thread.Join();
        Assert.Null(error);
        Assert.Equal(9 + 2, columns);   // 9 постоянных + Кисп замкнутого и пилона
    }

    [Fact]
    public void ParamsVM_BatchMode_PreviewByGoverningRow()
    {
        var rows = new List<LoadItem>
        {
            new() { Id = 1, Num = 1, Label = "А", N = -300, Mx = 10, My = 5 },
            new() { Id = 2, Num = 2, Label = "Б", N = -600, Mx = 30, My = 20 },
        };
        var task = new CalcTask { Id = 8, Tag = "Узел", Kind = PunchingBatchRunner.Kind, CalcType = CalcType.C, ForceSetId = 4, ParamsJson = P.ToJson() };
        var vm = new PunchingParamsVM(task, [], [], null, rows, "РСУ");
        Assert.True(vm.IsBatch);
        Assert.False(vm.IsSingle);
        Assert.False(vm.ShowForceRow);
        Assert.False(vm.HasErrors);
        Assert.Contains("№ 2", vm.PreviewNote);
        Assert.Contains("2", vm.BatchForcesText);
        Assert.Equal(1.0181060000790736, vm.Preview!.Utilization, 9);

        // Квадратная колонна — пилона нет, рисунок и таблица по-прежнему по определяющей строке.
        vm.By = 250;
        Assert.DoesNotContain(vm.Preview!.Rows, r => r.Id == "pylon_broken");
        Assert.Contains("№ 2", vm.PreviewNote);

        var empty = new PunchingParamsVM(task, [], [], null, [], "РСУ");
        Assert.True(empty.HasErrors);
    }

    [Fact]
    public void ErrorResult_ShowsError()
    {
        var vm = new PunchingBatchResultVM(new CalcResult { Status = "error", DataJson = """{"error":"нет набора"}""" }, null);
        Assert.True(vm.HasError);
        Assert.Empty(vm.Rows);
        Assert.Null(vm.Scene);
    }
}
