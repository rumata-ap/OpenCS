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
}
