using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace OpenCS.Views.Helpers;

/// <summary>График «коэффициент нагрузки по шагам» результата нелинейного расчёта: точка
/// зелёная (сошёлся) или красная (не сошёлся), выбранный шаг обведён.</summary>
public sealed class FemLoadFactorCanvas : Canvas
{
    IReadOnlyList<(int Step, double LoadFactor, bool Converged)> _points = [];
    int _selectedStep;

    public FemLoadFactorCanvas()
    {
        Background = Brushes.White;
        SizeChanged += (_, _) => Redraw();
        MouseLeftButtonDown += OnClick;
    }

    /// <summary>Поднимается при клике по точке графика — индекс шага (в списке, переданном в SetData).</summary>
    public event Action<int>? StepClicked;

    /// <summary>Задаёт точки графика и выбранный шаг, перерисовывает.</summary>
    public void SetData(IReadOnlyList<(int Step, double LoadFactor, bool Converged)> points, int selectedStep)
    {
        _points = points ?? [];
        _selectedStep = selectedStep;
        Redraw();
    }

    const double AxisMargin = 30;

    void Redraw()
    {
        Children.Clear();
        if (ActualWidth < 20 || ActualHeight < 20 || _points.Count == 0) return;

        double x0 = AxisMargin, x1 = ActualWidth - AxisMargin, y0 = ActualHeight - AxisMargin, y1 = AxisMargin;
        double maxLf = System.Math.Max(1.0, _points.Max(p => p.LoadFactor));

        double MapX(int i) => _points.Count == 1 ? (x0 + x1) / 2 : x0 + (double)i / (_points.Count - 1) * (x1 - x0);
        double MapY(double lf) => y0 - lf / maxLf * (y0 - y1);

        Children.Add(new Line { X1 = x0, Y1 = y0, X2 = x1, Y2 = y0, Stroke = Brushes.Black, StrokeThickness = 1 });
        Children.Add(new Line { X1 = x0, Y1 = y0, X2 = x0, Y2 = y1, Stroke = Brushes.Black, StrokeThickness = 1 });

        var poly = new Polyline { Stroke = Brushes.SteelBlue, StrokeThickness = 1.5 };
        for (int i = 0; i < _points.Count; i++)
            poly.Points.Add(new Point(MapX(i), MapY(_points[i].LoadFactor)));
        Children.Add(poly);

        for (int i = 0; i < _points.Count; i++)
        {
            var p = _points[i];
            var dot = new Ellipse
            {
                Width = 8, Height = 8,
                Fill = p.Converged ? Brushes.SeaGreen : Brushes.Crimson,
                Stroke = i == _selectedStep ? Brushes.Black : null,
                StrokeThickness = 2,
            };
            SetLeft(dot, MapX(i) - 4);
            SetTop(dot, MapY(p.LoadFactor) - 4);
            Children.Add(dot);
        }
    }

    void OnClick(object sender, MouseButtonEventArgs e)
    {
        if (_points.Count == 0) return;
        var pos = e.GetPosition(this);
        double x0 = AxisMargin, x1 = ActualWidth - AxisMargin;
        double t = _points.Count == 1 ? 0 : (pos.X - x0) / (x1 - x0) * (_points.Count - 1);
        int idx = System.Math.Clamp((int)System.Math.Round(t), 0, _points.Count - 1);
        StepClicked?.Invoke(idx);
    }
}
