using System.Threading;
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
                    FemMemberForceSetPreviewVMTests.PreviewWithTwoInternalCandidates());

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
}
