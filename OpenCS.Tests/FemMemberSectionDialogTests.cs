using CScore;
using OpenCS.Utilites;
using OpenCS.Views;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Проверки создания read-only окна preview поперечного сечения.</summary>
public class FemMemberSectionDialogTests
{
    [Fact]
    public void Ctor_WithSectionGeometry_CreatesPlotCanvasOnStaThread()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var section = new CrossSection
                {
                    Areas =
                    [
                        new MaterialArea
                        {
                            Hull = new Contour(
                                [0d, 1d, 1d, 0d, 0d],
                                [0d, 0d, 1d, 1d, 0d],
                                "hull")
                        }
                    ]
                };

                var dialog = new FemMemberSectionDialog(section, PlotSettings.Default);
                Assert.NotNull(dialog.PreviewCanvas);
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
