using System.Threading;
using System.Windows;
using System.Windows.Controls;
using CScore;
using OpenCS.ViewModels;
using OpenCS.Tests;
using OpenCS.Views;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Проверки создания WPF preview-окна.</summary>
public class FemMemberForceSetPreviewDialogTests
{
    [Fact]
    public void Ctor_CreatesDialogOnStaAndLeavesResultUnconfirmed()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var dialog = new FemMemberForceSetPreviewDialog(
                    FemMemberForceSetPreviewVMTests.PreviewWithTwoInternalCandidates(), []);

                Assert.Null(dialog.Result);
                Assert.NotNull(dialog.FindName("RowsGrid"));
                dialog.Close();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(error);
    }

    [Fact]
    public void TakenTag_ShowsWarningUnderName_FreeDefaultHidesIt()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var preview = FemMemberForceSetPreviewVMTests.PreviewWithTwoInternalCandidates();
                string baseTag = string.Format(OpenCS.Utilites.Loc.S("FemForceSetDefaultTag"), preview.MemberTag, preview.StepLabel);
                ForceSet[] existing = [new() { Tag = baseTag }];
                var dialog = new FemMemberForceSetPreviewDialog(preview, existing);
                var vm = (FemMemberForceSetPreviewVM)dialog.DataContext;
                var warning = (TextBlock)dialog.FindName("TagWarningText");
                // Привязки неоткрытого окна вычисляются в очереди диспетчера — сбросить её перед проверкой.
                void Flush() => dialog.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
                Flush();

                Assert.Equal(baseTag + " (2)", vm.Tag);  // имя по умолчанию — свободное
                Assert.Equal(Visibility.Collapsed, warning.Visibility);

                vm.Tag = baseTag;
                Flush();
                Assert.Equal(Visibility.Visible, warning.Visibility);
                Assert.False(string.IsNullOrEmpty(warning.Text));
                dialog.Close();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(error);
    }
}
