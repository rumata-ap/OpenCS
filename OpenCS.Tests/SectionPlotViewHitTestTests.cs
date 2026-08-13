using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CScore;
using OpenCS.ViewModels;
using OpenCS.Views;
using OpenCS.Views.Helpers;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Проверки интерактивности SectionPlotView: без CutVM оверлей разреза не должен
/// перехватывать клики по карте сечения (иначе FiberCanvas не получает события).</summary>
public class SectionPlotViewHitTestTests
{
    [Fact]
    public void SectionPlotView_WithoutCutVm_OverlayIsHitTestTransparent()
    {
        Exception? error = null;
        bool? overlayHitTestVisible = null;

        var thread = new System.Threading.Thread(() =>
        {
            try
            {
                if (Application.Current == null)
                {
                    var app = new Application();
                    var res = app.Resources;
                    if (!res.Contains("BoolToVisibility"))
                        res["BoolToVisibility"] = new BooleanToVisibilityConverter();
                    if (!res.Contains("EnumToBoolConverter"))
                        res["EnumToBoolConverter"] = new OpenCS.Converters.EnumToBoolConverter();
                }

                var vm = new SectionPlotVM(new CrossSection(), new Kurvature(), CalcType.C, SectionPlotMode.Stress);
                var view = new SectionPlotView { DataContext = vm };
                view.Measure(new Size(600, 400));
                view.Arrange(new Rect(0, 0, 600, 400));
                view.UpdateLayout();

                var overlay = FindVisualChild<SectionCutOverlay>(view);
                overlayHitTestVisible = overlay?.IsHitTestVisible;
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(System.Threading.ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(error);
        Assert.NotNull(overlayHitTestVisible);
        Assert.False(overlayHitTestVisible);
    }

    static T? FindVisualChild<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) return match;
            if (FindVisualChild<T>(child) is { } nested) return nested;
        }
        return null;
    }
}
