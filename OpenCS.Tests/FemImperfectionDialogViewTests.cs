using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CScore.Fem.Imperfections;
using OpenCS.ViewModels;
using OpenCS.Views;
using Xunit;

namespace OpenCS.Tests;

/// <summary>
/// Окно диалога несовершенства: переключатели сгруппированы по панели (без GroupName — глобальный реестр групп WPF
/// терял отметки при первом открытии), при правке существующего элемента отмечены по модели, щелчок по второму
/// варианту снимает первый и меняет модель.
/// </summary>
public sealed class FemImperfectionDialogViewTests
{
    [Fact]
    public void Radios_ReflectModel_AndStayExclusive()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var sway = new FemImperfection
                {
                    Id = 5, Kind = FemImperfectionKinds.Sway, Tag = "Крен X",
                    ParamsJson = new FemSwayParams { PhiMode = FemSwayParams.PhiEn1993, M = 2 }.ToJson(),
                };
                var (dialog, vm) = Open(sway);
                var radios = Radios(dialog);
                Assert.Equal(8, radios.Count);                       // вид ×4, охват ×2, φ ×2
                Assert.True(radios[0].IsChecked);                    // крен
                Assert.True(radios[4].IsChecked);                    // вся схема
                Assert.False(radios[5].IsChecked);
                Assert.True(radios[6].IsChecked);                    // φ по (5.5)
                Assert.False(radios[7].IsChecked);
                radios[7].IsChecked = true;                          // 1/φ вручную
                Assert.False(radios[6].IsChecked);
                Assert.True(vm.PhiManual);
                Assert.True(radios[4].IsChecked);                    // соседняя группа не затронута
                dialog.Close();

                var bow = new FemImperfection
                {
                    Id = 6, Kind = FemImperfectionKinds.Bow, Tag = "Погиби стоек",
                    ParamsJson = new FemBowParams { MemberTags = ["К1"], E0Mode = FemBowParams.E0Table }.ToJson(),
                };
                (dialog, vm) = Open(bow);
                radios = Radios(dialog);
                Assert.Equal(6, radios.Count);                       // вид ×4, упругий/пластический
                Assert.True(radios[1].IsChecked);                    // погибь
                Assert.True(radios[4].IsChecked);                    // упругий
                Assert.False(radios[5].IsChecked);
                radios[5].IsChecked = true;
                Assert.False(radios[4].IsChecked);
                Assert.True(vm.Plastic);
                dialog.Close();
            }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        Assert.Null(error);
    }

    static (FemImperfectionDialog, FemImperfectionDialogVM) Open(FemImperfection item)
    {
        var vm = new FemImperfectionDialogVM(item);
        var dialog = new FemImperfectionDialog(vm)
        {
            WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000, ShowActivated = false,
        };
        dialog.Show();
        dialog.UpdateLayout();
        dialog.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        return (dialog, vm);
    }

    static List<RadioButton> Radios(DependencyObject root)
    {
        var list = new List<RadioButton>();
        void Walk(DependencyObject d)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
            {
                var child = VisualTreeHelper.GetChild(d, i);
                if (child is RadioButton rb && rb.IsVisible) list.Add(rb);
                Walk(child);
            }
        }
        Walk(root);
        return list;
    }
}
