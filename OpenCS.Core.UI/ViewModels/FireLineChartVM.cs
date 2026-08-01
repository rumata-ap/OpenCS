using OpenCS.Utilites;

namespace OpenCS.ViewModels;

/// <summary>Одна линия для <see cref="FireChartView"/>.</summary>
public sealed record FireLineSeries(string Label, double[] X, double[] Y, string ColorHex);

/// <summary>Данные линейного графика (чистый WPF PlotCanvas).</summary>
public sealed class FireLineChartVM : ViewModelBase
{
    public string Title { get; }
    public string XLabel { get; }
    public string YLabel { get; }
    public IReadOnlyList<FireLineSeries> Series { get; }
    public bool LogY { get; }

    public event Action? RedrawRequested;

    public FireLineChartVM(string title, string xLabel, string yLabel, IReadOnlyList<FireLineSeries> series, bool logY = false)
    {
        Title = title;
        XLabel = xLabel;
        YLabel = yLabel;
        Series = series;
        LogY = logY;
    }

    public void RequestRedraw() => RedrawRequested?.Invoke();
}
