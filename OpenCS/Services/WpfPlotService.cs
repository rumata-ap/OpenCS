using OpenCS.Utilites;
using OpenCS.Views;

using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace OpenCS.Services
{
   public class WpfPlotService : IPlotService
   {
      private readonly PlotCanvas _canvas;
      private readonly List<PlotElement> _elements = [];

      private double _xMin = double.MaxValue, _xMax = double.MinValue;
      private double _yMin = double.MaxValue, _yMax = double.MinValue;
      private bool _squareAxes;
      private bool _autoScale = true;

      private PlotSettings _settings = PlotSettings.Default;
      private string? _title, _xLabel, _yLabel;

      public WpfPlotService(PlotCanvas canvas)
      {
         _canvas = canvas;
      }

      public void Clear()
      {
         _elements.Clear();
         _canvas.Clear();
         _xMin = double.MaxValue; _xMax = double.MinValue;
         _yMin = double.MaxValue; _yMax = double.MinValue;
         _squareAxes = false;
         _autoScale = true;
         _title = _xLabel = _yLabel = null;
      }

      public void AddScatter(double[] xs, double[] ys, double lineWidth = 1, string? color = null, string? label = null)
      {
         if (xs == null || ys == null || xs.Length < 2) return;
         UpdateBounds(xs, ys);
         _elements.Add(new ScatterElement
         {
            Xs = xs, Ys = ys,
            Stroke = ParseColor(color ?? _settings.Curve),
            StrokeThickness = _settings.CurveThickness,
            Label = label
         });
      }

      public void AddLine(double[] xs, double[] ys, string? label = null)
      {
         AddScatter(xs, ys, 1, null, label);
      }

      public void AddPolygon(double[] xs, double[] ys, string? fillColor = null, string? lineColor = null)
      {
         if (xs == null || ys == null || xs.Length < 3) return;
         UpdateBounds(xs, ys);
         _elements.Add(new PolygonElement
         {
            Xs = xs, Ys = ys,
            Fill = fillColor != null ? ParseColor(fillColor) : null,
            Stroke = ParseColor(lineColor ?? _settings.Curve),
            StrokeThickness = _settings.CurveThickness
         });
      }

      public void AddCircle(double x, double y, double radius, string? fillColor = null, string? lineColor = null, float lineWidth = 1)
      {
         UpdateBounds(new[] { x - radius, x + radius }, new[] { y - radius, y + radius });
         _elements.Add(new CircleElement
         {
            X = x, Y = y, Radius = radius,
            Fill = fillColor != null ? ParseColor(fillColor) : null,
            Stroke = ParseColor(lineColor ?? _settings.Curve),
            StrokeThickness = _settings.CurveThickness
         });
      }

      public void AddMarkers(double[] xs, double[] ys, float markerSize = 4, string? color = null, string? label = null)
      {
         if (xs == null || ys == null || xs.Length == 0) return;
         UpdateBounds(xs, ys);
         _elements.Add(new MarkerElement
         {
            Xs = xs, Ys = ys,
            Fill = ParseColor(color ?? _settings.MarkerFill),
            MarkerSize = _settings.MarkerSize,
            Label = label
         });
      }

      public void ApplySettings(PlotSettings settings)
      {
         _settings = settings;
         _canvas.ApplySettings(settings);
      }

      public void ShowLegend(bool show = true) { }
      public void EnableSquareAxes() { _squareAxes = true; }
      public void AutoScale() { _autoScale = true; }
      public void SetAxisLimits(double xMin, double xMax, double yMin, double yMax)
      {
         _xMin = xMin; _xMax = xMax;
         _yMin = yMin; _yMax = yMax;
         _autoScale = false;
      }
      public void SetTitle(string title) => _title = title;
      public void SetXLabel(string label) => _xLabel = label;
      public void SetYLabel(string label) => _yLabel = label;

      public void Refresh()
      {
         if (_elements.Count == 0) return;

         double xMin = _xMin, xMax = _xMax;
         double yMin = _yMin, yMax = _yMax;

         if (double.IsInfinity(xMin)) { xMin = 0; xMax = 1; }
         if (double.IsInfinity(yMin)) { yMin = 0; yMax = 1; }

         _canvas.Draw(_elements, xMin, xMax, yMin, yMax,
            squareAxes: _squareAxes,
            xLabel: _xLabel, yLabel: _yLabel, title: _title);
      }

      private void UpdateBounds(double[] xs, double[] ys)
      {
         if (!_autoScale) return;
         foreach (var x in xs) { if (x < _xMin) _xMin = x; if (x > _xMax) _xMax = x; }
         foreach (var y in ys) { if (y < _yMin) _yMin = y; if (y > _yMax) _yMax = y; }
      }

      private static Brush ParseColor(string? hex)
      {
         try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex!)); }
         catch { return Brushes.Black; }
      }
   }
}
