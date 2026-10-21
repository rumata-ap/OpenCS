using System.Windows;
using CScore.Fem;
using CScore.Fem.Fields;
using OpenCS.ViewModels;
using OpenCS.Views;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Разбор XAML окна поля из таблицы точек и отрисовка предпросмотра (без запуска программы).</summary>
public sealed class FemFieldDialogWindowTests
{
    [Fact]
    public void Window_Constructs_AndPreviewRenders()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                // Без Application: глобальный экземпляр из потока теста ломает соседние STA-тесты окон.
                var ctx = new FemFieldMeshContext(
                    [new FemMeshNode { NodeTag = "1" }, new FemMeshNode { NodeTag = "2", X = 1 },
                     new FemMeshNode { NodeTag = "3", X = 1, Y = 1 }, new FemMeshNode { NodeTag = "4", Y = 1 }],
                    [new FemElement { ElemTag = "1", ElemType = "shell", NodeIdsJson = "[1,2,4,3]" }], [], [], []);
                var vm = new FemFieldDialogVM(ctx, new FemFieldTargets(), 9.80665);
                vm.LoadText("X;Y;C1 [тс/м3]\n-1;-1;100\n2;-1;100\n-1;2;100\n2;2;100", "c.csv");
                vm.Preview();
                var w = new FemFieldDialog(vm) { Left = -20000, Top = -20000, ShowActivated = false };
                w.Show();
                w.UpdateLayout();
                Assert.Equal(FieldHow.Interpolated, Assert.Single(vm.Evaluation!.Targets).How);
                Assert.Single(vm.PatchLabels);
                w.Close();

                var nodes = new FemFieldDialogVM(ctx with { Springs = [new FemSpring { NodeTag = "3", Kz = 1 }] }, new FemFieldTargets(), 9.80665);
                nodes.LoadText("X;Y;Rz [тс/м]\n1;1;50", "rz.csv");
                nodes.Preview();
                var wn = new FemFieldDialog(nodes) { Left = -20000, Top = -20000, ShowActivated = false };
                wn.Show();
                wn.UpdateLayout();
                Assert.Single(nodes.NodeTargets);
                wn.Close();
            }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        Assert.Null(error);
    }
}
