using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CScore;
using OpenCS.LoadHistory;
using OpenCS.Utilites;
using OpenCS.ViewModels;

namespace OpenCS.Views;

/// <summary>Окно результата истории нагружения: шаги, «момент–кривизна» (щелчок выбирает шаг), поля шага.</summary>
public partial class LoadHistoryResultView : UserControl
{
    readonly LoadHistoryResultVM _vm;
    readonly Dictionary<PlotElement, (bool NoHistory, IReadOnlyList<CurvePoint> Points)> _pickable =
        new(ReferenceEqualityComparer.Instance);

    public LoadHistoryResultView(CalcResult result, StagedSection? section, CalcType calc, CalcSettings settings)
    {
        InitializeComponent();
        _vm = new LoadHistoryResultVM(result, section, calc, settings);
        DataContext = _vm;
        NoSectionText.Visibility = _vm.HasSection ? Visibility.Collapsed : Visibility.Visible;
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LoadHistoryResultVM.SelectedRow) && _vm.SelectedRow != null)
                StepsGrid.ScrollIntoView(_vm.SelectedRow);
        };
        curve.PointPicked += OnPointPicked;
        DrawCurves();
    }

    void OnPointPicked(PlotElement element, int index)
    {
        if (!_pickable.TryGetValue(element, out var path) || index < 0 || index >= path.Points.Count) return;
        int step = path.Points[index].StepIndex;
        if (step < 0) return; // начало (0, 0)
        _vm.SelectStep(path.NoHistory, step);
    }

    void DrawCurves()
    {
        var c = _vm.Curves;
        var elements = new List<PlotElement>();
        _pickable.Clear();

        void Path(IReadOnlyList<CurvePoint> pts, Brush brush, double thickness, bool noHistory, string label)
        {
            if (pts.Count == 0) return;
            double[] xs = pts.Select(p => p.C).ToArray(), ys = pts.Select(p => p.M).ToArray();
            var line = new ScatterElement { Xs = xs, Ys = ys, Stroke = brush, StrokeThickness = thickness, Label = label };
            var marks = new MarkerElement { Xs = xs, Ys = ys, Fill = brush, MarkerSize = 4 };
            elements.Add(line);
            elements.Add(marks);
            _pickable[line] = (noHistory, pts);
            _pickable[marks] = (noHistory, pts);
        }

        void Mark(CurvePoint? p, Brush brush, double size, string text)
        {
            if (p is null) return;
            elements.Add(new MarkerElement { Xs = [p.C], Ys = [p.M], Fill = brush, MarkerSize = size });
            elements.Add(new TextElement { X = p.C, Y = p.M, Text = text, Foreground = brush });
        }

        Path(c.WithoutHistory, Brushes.DarkOrange, 1.5, true, Loc.S("LoadHistory_WithoutHistory"));
        Path(c.WithHistory, Brushes.SteelBlue, 2, false, Loc.S("LoadHistory_WithHistory"));
        foreach (var a in c.Activations) Mark(a, Brushes.Green, 7, Loc.S("LoadHistory_Activation"));
        Mark(c.LimitWith, Brushes.Red, 8, "M_lim");
        Mark(c.LimitWithout, Brushes.Red, 8, "M_lim");
        Mark(c.PeakWith, Brushes.Black, 6, "M_peak");
        Mark(c.PeakWithout, Brushes.Black, 6, "M_peak");

        var all = c.WithHistory.Concat(c.WithoutHistory).ToList();
        if (all.Count < 2) { curve.Clear(); return; }
        double xMin = all.Min(p => p.C), xMax = all.Max(p => p.C);
        double yMin = all.Min(p => p.M), yMax = all.Max(p => p.M);
        double dx = Math.Max((xMax - xMin) * 0.05, 1e-6), dy = Math.Max((yMax - yMin) * 0.05, 1e-3);
        curve.Draw(elements, xMin - dx, xMax + dx, yMin - dy, yMax + dy,
            xLabel: Loc.S("LoadHistory_AxisC"), yLabel: Loc.S("LoadHistory_AxisM"));
    }
}
