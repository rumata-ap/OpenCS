using CScore;
using OpenCS.Tasks;
using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Окно параметров продавливания: круговой проход, живой пересчёт, ошибки ввода, источники данных.</summary>
[Collection("Loc language")]
public sealed class PunchingParamsVMTests
{
    static PunchingTaskParams Gosha(params PunchingEdgeParams[] edges) => new()
    {
        Area = new PunchingAreaParams { Shape = "rectangle", Bx = 0.25, By = 0.8 },
        Slab = new PunchingSlabParams { H = 0.2, Ax = 0.04, Ay = 0.03, RbtOverride = 1050 },
        Edges = [.. edges],
        Reinforcement = new PunchingReinforcementParams { Enabled = true, Diameter = 0.006, Count = 2, Step = 0.05, RswOverride = 170_000 },
        Forces = new PunchingForceParams { F = 600, Mx = 30, My = 20 }
    };

    static CalcTask Task(string json) => new() { Id = 7, Tag = "Узел", Kind = PunchingRunner.Kind, CalcType = CalcType.C, ParamsJson = json };

    static PunchingParamsVM Vm(PunchingTaskParams p, LoadItem? row = null, IEnumerable<PlateSection>? plates = null)
        => new(Task(p.ToJson()), [], plates ?? [], row);

    /// <summary>Контур ЛИРЫ задачи (контуры пользователя + замена основного) в окне не правится, но сохраняется.</summary>
    [Fact]
    public void CustomReplacesMain_KeptThroughForm()
    {
        var p = Gosha() with
        {
            CustomContours = [new PunchingCustomContourParams { Name = "ЛИРА", Points = [[-0.2, -0.5], [0.2, -0.5], [0.2, 0.5], [-0.2, 0.5]] }],
            Options = new PunchingOptionParams { CustomReplacesMain = true },
        };
        var back = PunchingTaskParams.Parse(Vm(p).ToJson());
        Assert.True(back.Options.CustomReplacesMain);
        Assert.Single(back.CustomContours);
    }

    [Fact]
    public void RoundTrip_ParamsThroughForm()
    {
        var p = Gosha(new PunchingEdgeParams { Side = "-y", C = 0.05, AngleDeg = 10 }, new PunchingEdgeParams { Side = "+x", C = 0.12 }) with
        {
            Holes = [new PunchingHoleParams { Shape = "circle", Cx = 0.1, Cy = 1, D = 0.15 }],
            Reinforcement = Gosha().Reinforcement with { ZoneExtent = 0.3 },
            Forces = new PunchingForceParams { F = 600, Mx = 30, My = -20, MomentsAreMloc = true, LoadSide = "from_above", CounterPressure = 12 },
            Options = new PunchingOptionParams { EccentricityX = "auto", Wb = "stress_side", Pylon = "off", HoleInfluence = 5, PylonReduced = true, Mu = 0.008 },
            CustomContours = [new PunchingCustomContourParams { Name = "К1", Points = [[0, 0], [1, 0], [1, 1]] }]
        };
        var vm = Vm(p);
        Assert.Equal("corner", vm.Position);
        Assert.Equal(250, vm.Bx, 9);
        Assert.Equal(50, vm.Edge1C, 9);
        Assert.Equal(1.05, vm.RbtManualValue, 9);
        var back = vm.ToParams();
        Assert.Equal(p.ToJson(), back.ToJson());
    }

    [Fact]
    public void NewTask_DefaultsAndForcesFromRow()
    {
        var row = new LoadItem { Id = 3, Label = "РСУ 5", N = -812.5, Mx = 14, My = -9 };
        var vm = new PunchingParamsVM(Task("{}"), [], [], row);
        Assert.Equal(812.5, vm.F, 9);
        Assert.Equal(14, vm.Mx, 9);
        Assert.Equal(-9, vm.My, 9);
        Assert.True(vm.HasForceRow);
        Assert.Contains("РСУ 5", vm.ForceRowText);
        // Без бетона в проекте и без ручного Rbt — ошибка ввода, а не исключение.
        Assert.True(vm.HasErrors);
        Assert.Null(vm.Preview);

        vm.RbtManual = true;
        Assert.False(vm.HasErrors);
        Assert.NotNull(vm.Preview);

        vm.F = 1;
        vm.TakeForcesFromRow();
        Assert.Equal(812.5, vm.F, 9);

        // Сохранённые значения у существующей задачи — главные: строка набора их не перезаписывает.
        var saved = new PunchingParamsVM(Task(Gosha().ToJson()), [], [], row);
        Assert.Equal(600, saved.F, 9);
    }

    [Fact]
    public void RowTaskSource_SurvivesSave()
    {
        // Задача по строке проверки по МКЭ: окно не правит источник, но не должно его терять — по нему
        // «Создать задачу» находит прежнюю задачу строки (иначе повторный выбор строки плодит дубли).
        var src = new PunchingSourceParams { CheckId = 8, Schema = "Музей", Node = "47460", Key = "РСУ 1" };
        var vm = Vm(Gosha() with { Source = src });
        var back = PunchingTaskParams.Parse(vm.ToJson()).Source;
        Assert.NotNull(back);
        Assert.Equal((8, "47460", "РСУ 1"), (back.CheckId, back.Node, back.Key));
    }

    [Fact]
    public void LiveRecalc_EdgeDistanceRebuildsVariants()
    {
        var vm = Vm(Gosha(new PunchingEdgeParams { Side = "-y", C = 0 }));
        var first = vm.Preview!;
        Assert.Contains(first.Rows, r => r.Id == "open_edge_0");
        Assert.Equal(1.2622792060118404, first.Utilization, 9);
        int before = vm.RecalcCount;

        vm.Edge1C = 100;
        Assert.True(vm.RecalcCount > before);
        Assert.NotSame(first, vm.Preview);
        Assert.NotEqual(first.Rows.Single(r => r.Id == "open_edge_0").K, vm.Preview!.Rows.Single(r => r.Id == "open_edge_0").K);

        vm.Position = "inside";
        Assert.DoesNotContain(vm.Preview!.Rows, r => r.Id.StartsWith("open_edge", StringComparison.Ordinal));
        Assert.Equal(1.0181060000790736, vm.Preview.Utilization, 9);
    }

    [Fact]
    public void InputError_KeepsLastSceneAndSelection()
    {
        var vm = Vm(Gosha());
        var preview = vm.Preview!;
        vm.Preview!.SelectedVariantId = "closed";

        vm.H = 0;
        Assert.True(vm.HasErrors);
        Assert.Contains("h", vm.ErrorText);
        Assert.Same(preview, vm.Preview);
        Assert.NotNull(vm.Preview!.Scene);

        vm.H = 220;
        Assert.False(vm.HasErrors);
        Assert.NotSame(preview, vm.Preview);
        Assert.Equal("closed", vm.Preview!.SelectedVariantId);
    }

    [Fact]
    public void Holes_AddRemoveAndEdit_Recalculate()
    {
        var vm = Vm(Gosha());
        int n = vm.RecalcCount;
        vm.AddHoleCommand.Execute(null);
        Assert.Single(vm.Holes);
        Assert.True(vm.RecalcCount > n);
        Assert.Single(vm.ToParams().Holes);

        n = vm.RecalcCount;
        vm.Holes[0].Bx = 300;
        Assert.True(vm.RecalcCount > n);
        Assert.Equal(0.3, vm.ToParams().Holes[0].Bx, 12);

        vm.SelectedHole = vm.Holes[0];
        vm.RemoveHoleCommand.Execute(null);
        Assert.Empty(vm.ToParams().Holes);
    }

    /// <summary>Разметка окна параметров и панели результата разбирается, рисунок рисуется (без запуска программы).</summary>
    [Fact]
    public void Dialog_ParsesXaml_AndRendersPreview()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var app = System.Windows.Application.Current ?? new System.Windows.Application();
                var res = app.Resources;
                if (!res.Contains("BoolToVisibility")) res["BoolToVisibility"] = new System.Windows.Controls.BooleanToVisibilityConverter();
                if (!res.Contains("InverseBool")) res["InverseBool"] = new OpenCS.Converters.InverseBoolConverter();
                if (!res.Contains("BatchVerdictRowStyle")) res["BatchVerdictRowStyle"] = new System.Windows.Style(typeof(System.Windows.Controls.DataGridRow));

                var task = Task(Gosha(new PunchingEdgeParams { Side = "-y", C = 0.05 }).ToJson());
                var vm = new PunchingParamsVM(task, [], [], null);
                var dlg = new OpenCS.Views.PunchingParamsDialog(task, vm)
                {
                    WindowStartupLocation = System.Windows.WindowStartupLocation.Manual, Left = -20000, Top = -20000, ShowActivated = false
                };
                dlg.Show();
                dlg.UpdateLayout();
                dlg.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

                var canvas = FindChild<OpenCS.Views.Helpers.PunchingPlanCanvas>(dlg);
                Assert.NotNull(canvas);
                Assert.Same(vm.Preview!.Scene, canvas!.Scene);
                Assert.True(canvas.ActualWidth > 100 && canvas.ActualHeight > 100);
                var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap((int)canvas.ActualWidth, (int)canvas.ActualHeight, 96, 96,
                    System.Windows.Media.PixelFormats.Pbgra32);
                bmp.Render(canvas);

                vm.Edge1C = 120;
                dlg.UpdateLayout();
                Assert.Same(vm.Preview!.Scene, canvas.Scene);
                dlg.Close();
            }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        Assert.Null(error);
    }

    static T? FindChild<T>(System.Windows.DependencyObject root) where T : System.Windows.DependencyObject
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var c = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (c is T t) return t;
            if (FindChild<T>(c) is { } found) return found;
        }
        return null;
    }

    [Fact]
    public void TakeFromPlateSection_CoverAtTensionFace()
    {
        var plate = new PlateSection
        {
            Tag = "П250", H = 0.25,
            RebarLayers =
            [
                new PlateRebarLayer { Asx = 5e-4, Asy = 5e-4, Zsx = 0.09, Zsy = 0.078 },
                new PlateRebarLayer { Asx = 5e-4, Asy = 5e-4, Zsx = -0.095, Zsy = -0.083 }
            ]
        };
        var vm = Vm(Gosha(), plates: [plate]);
        vm.SelectedPlateSection = plate;
        vm.TakeFromPlateSection();
        Assert.Equal(250, vm.H, 9);
        Assert.Equal(35, vm.Ax, 9);
        Assert.Equal(47, vm.Ay, 9);

        vm.LoadSide = "from_above";
        vm.TakeFromPlateSection();
        Assert.Equal(30, vm.Ax, 9);
        Assert.Equal(42, vm.Ay, 9);
    }
}
