using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

using OpenCS.ViewModels;

namespace OpenCS.Views.Helpers;

/// <summary>Простое масштабируемое превью области-носителя и элементов хомутов.</summary>
public sealed class StirrupPreviewCanvas : Canvas
{
    StirrupGroupVM? _viewModel;

    /// <summary>Создаёт холст превью.</summary>
    public StirrupPreviewCanvas()
    {
        Background = Brushes.White;
        DataContextChanged += OnDataContextChanged;
        SizeChanged += (_, _) => InvalidateVisual();
    }

    void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs args)
    {
        if (_viewModel is not null)
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel = DataContext as StirrupGroupVM;
        if (_viewModel is not null)
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        InvalidateVisual();
    }

    void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args) =>
        InvalidateVisual();

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (_viewModel is null) return;

        var paths = new List<IReadOnlyList<(double X, double Y)>>();
        if (_viewModel.SelectedAnchorArea?.Hull is { } hull)
            paths.Add(Vertices(hull));
        paths.AddRange(_viewModel.Elements.Select(item => Vertices(item.Element.CenterlineContour)));
        var all = paths.SelectMany(path => path).ToList();
        if (all.Count == 0) return;

        double minX = all.Min(point => point.X);
        double maxX = all.Max(point => point.X);
        double minY = all.Min(point => point.Y);
        double maxY = all.Max(point => point.Y);
        double width = Math.Max(maxX - minX, 1e-6);
        double height = Math.Max(maxY - minY, 1e-6);
        double scale = Math.Min(Math.Max(ActualWidth - 20, 1) / width,
                                Math.Max(ActualHeight - 20, 1) / height);
        double left = (ActualWidth - width * scale) / 2.0;
        double top = (ActualHeight - height * scale) / 2.0;

        Point Map((double X, double Y) point) =>
            new(left + (point.X - minX) * scale,
                top + (maxY - point.Y) * scale);

        if (_viewModel.SelectedAnchorArea?.Hull is { } anchor)
            DrawPath(drawingContext, Vertices(anchor), Map,
                new Pen(Brushes.Gray, 1.5) { DashStyle = DashStyles.Dash });

        foreach (var element in _viewModel.Elements)
            DrawPath(drawingContext, Vertices(element.Element.CenterlineContour), Map,
                new Pen(Brushes.DarkRed, 2.0));
    }

    static void DrawPath(DrawingContext drawingContext,
                         IReadOnlyList<(double X, double Y)> path,
                         Func<(double X, double Y), Point> map,
                         Pen pen)
    {
        if (path.Count < 2) return;
        for (int i = 1; i < path.Count; i++)
            drawingContext.DrawLine(pen, map(path[i - 1]), map(path[i]));
    }

    static IReadOnlyList<(double X, double Y)> Vertices(CScore.Contour contour) =>
        contour.X.Zip(contour.Y, (x, y) => (x, y)).ToList();
}
