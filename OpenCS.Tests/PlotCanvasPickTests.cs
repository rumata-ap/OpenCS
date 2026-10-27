using System.Windows;
using System.Windows.Media;
using OpenCS.Views;
using Xunit;

namespace OpenCS.Tests;

public sealed class PlotCanvasPickTests
{
    [Fact]
    public void FindNearest_ReturnsElementAndIndex()
    {
        Exception? error = null;
        var t = new Thread(() =>
        {
            try
            {
                var canvas = new PlotCanvas { Width = 400, Height = 300 };
                var scatter = new ScatterElement { Xs = [0, 1, 2], Ys = [0, 1, 0] };
                canvas.Draw([scatter], 0, 2, 0, 1);
                canvas.Measure(new Size(400, 300));
                canvas.Arrange(new Rect(0, 0, 400, 300));
                canvas.UpdateLayout();
                // экранная точка второй вершины
                var screen = canvas.ToScreenForTest(1, 1);
                var hit = canvas.FindNearest(screen);
                Assert.NotNull(hit);
                Assert.Same(scatter, hit!.Value.Element);
                Assert.Equal(1, hit.Value.Index);
            }
            catch (Exception ex) { error = ex; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start(); t.Join();
        if (error != null) throw error;
    }

    /// <summary>Легенда рисуется в правом нижнем углу области данных образцом линии своего цвета;
    /// без <see cref="PlotCanvas.Legend"/> угол пуст.</summary>
    [Fact]
    public void Legend_DrawnInLowerRightCorner_OnlyWhenSet()
    {
        Exception? error = null;
        var t = new Thread(() =>
        {
            try
            {
                static int CountColor(PlotCanvas canvas, Color color)
                {
                    canvas.Measure(new Size(400, 300));
                    canvas.Arrange(new Rect(0, 0, 400, 300));
                    canvas.UpdateLayout();
                    var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap(400, 300, 96, 96, PixelFormats.Pbgra32);
                    bmp.Render(canvas);
                    var px = new byte[400 * 300 * 4];
                    bmp.CopyPixels(px, 400 * 4, 0);
                    int n = 0;
                    for (int y = 200; y < 300; y++)       // нижняя правая четверть — там нет данных
                        for (int x = 250; x < 400; x++)
                        {
                            int i = (y * 400 + x) * 4;
                            if (px[i] == color.B && px[i + 1] == color.G && px[i + 2] == color.R) n++;
                        }
                    return n;
                }
                // кривая только в левой верхней части
                var scatter = new ScatterElement { Xs = [0, 0.5, 1], Ys = [0.5, 1, 1], Stroke = Brushes.Black };
                var plain = new PlotCanvas { Width = 400, Height = 300 };
                plain.Draw([scatter], 0, 4, 0, 1);
                Assert.Equal(0, CountColor(plain, Colors.Red));
                var withLegend = new PlotCanvas { Width = 400, Height = 300 };
                withLegend.Draw([scatter], 0, 4, 0, 1);
                withLegend.Legend = [new PlotLegendItem("кривая", Brushes.Red, 3)];
                Assert.True(CountColor(withLegend, Colors.Red) > 20);
            }
            catch (Exception ex) { error = ex; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start(); t.Join();
        if (error != null) throw error;
    }
}
