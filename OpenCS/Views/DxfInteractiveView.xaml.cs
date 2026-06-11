using CScore;
using OpenCS.ViewModels;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace OpenCS.Views
{
   /// <summary>
   /// Интерактивный канвас для отображения и выбора DXF-примитивов.
   /// Zoom — колесо мыши, pan — правая кнопка мыши.
   /// Выбор — ЛКМ / Shift+ЛКМ / Ctrl+ЛКМ. Для окружностей достаточно кликнуть
   /// внутри; для контуров — вблизи линии. При совпадении приоритет у окружностей.
   /// </summary>
   public partial class DxfInteractiveView : UserControl
   {
      private readonly MatrixTransform _mt = new();
      private List<DxfPrimitive> _primitives = [];
      private Dictionary<string, string> _colorMap = [];
      private bool _hasBounds;
      private double _xMin, _xMax, _yMin, _yMax;
      private bool _isPanning;
      private Point _panStart;

      // порог выбора в экранных пикселях
      private const double HitThresholdPx = 10.0;

      /// <summary>
      /// Вызывается при клике на примитив. Передаёт кликнутый примитив.
      /// VM назначает ему роль и Canvas обновляет цвет в ответ.
      /// </summary>
      public Action<DxfPrimitive>? PrimitiveClicked { get; set; }

      public DxfInteractiveView()
      {
         InitializeComponent();
         InnerCanvas.RenderTransform = _mt;
         RootBorder.Background = ParseBrush("#F5F5F5");
      }

      /// <summary>Устанавливает цвет фона канваса из hex-строки.</summary>
      public void SetBackground(string hex) => RootBorder.Background = ParseBrush(hex);

      /// <summary>
      /// Загружает примитивы и информацию о слоях, строит WPF-фигуры на канвасе
      /// и масштабирует вид по границам данных.
      /// </summary>
      public void Load(IReadOnlyList<DxfPrimitive> primitives, IReadOnlyList<LayerInfo> layers)
      {
         ClearAll();
         if (primitives.Count == 0) return;

         _colorMap = layers.ToDictionary(l => l.Name, l => l.HexColor);
         _primitives = [.. primitives];

         _xMin = double.MaxValue; _xMax = double.MinValue;
         _yMin = double.MaxValue; _yMax = double.MinValue;

         foreach (var p in primitives)
         {
            if (p.Kind == DxfPrimitiveKind.Contour && p.Xs is { Length: > 0 })
            {
               for (int i = 0; i < p.Xs.Length; i++)
               {
                  _xMin = Math.Min(_xMin, p.Xs[i]); _xMax = Math.Max(_xMax, p.Xs[i]);
                  _yMin = Math.Min(_yMin, p.Ys![i]); _yMax = Math.Max(_yMax, p.Ys![i]);
               }
            }
            else if (p.Kind == DxfPrimitiveKind.Circle)
            {
               _xMin = Math.Min(_xMin, p.CenterX - p.Radius);
               _xMax = Math.Max(_xMax, p.CenterX + p.Radius);
               _yMin = Math.Min(_yMin, p.CenterY - p.Radius);
               _yMax = Math.Max(_yMax, p.CenterY + p.Radius);
            }
         }
         _hasBounds = true;

         foreach (var p in primitives)
         {
            string color = _colorMap.TryGetValue(p.LayerName, out var c) ? c : "#808080";
            Shape shape = p.Kind == DxfPrimitiveKind.Contour
               ? MakePolyline(p, color)
               : MakeCirclePath(p, color);
            InnerCanvas.Children.Add(shape);
         }

         if (ActualWidth > 1)
            FitToView();
      }

      /// <summary>Очищает канвас и сбрасывает состояние.</summary>
      public void ClearAll()
      {
         InnerCanvas.Children.Clear();
         _primitives.Clear();
         _colorMap.Clear();
         _hasBounds = false;
      }

      protected override void OnRenderSizeChanged(SizeChangedInfo info)
      {
         base.OnRenderSizeChanged(info);
         if (_hasBounds && info.NewSize.Width > 1)
            FitToView();
      }

      private void FitToView()
      {
         double w = ActualWidth, h = ActualHeight;
         if (w < 1 || h < 1 || !_hasBounds) return;

         double dataW = _xMax - _xMin;
         double dataH = _yMax - _yMin;
         if (dataW < 1e-10) dataW = 1;
         if (dataH < 1e-10) dataH = 1;

         double s = Math.Min(w * 0.9 / dataW, h * 0.9 / dataH);
         double cx = (_xMin + _xMax) / 2;
         double cy = (_yMin + _yMax) / 2;

         // DXF: Y вверх; WPF: Y вниз — инвертируем через M22 = -s
         _mt.Matrix = new Matrix(s, 0, 0, -s, w / 2 - cx * s, h / 2 + cy * s);
         UpdateStrokes();
      }

      private double Scale => Math.Abs(_mt.Matrix.M11);

      private void UpdateStrokes()
      {
         for (int i = 0; i < _primitives.Count && i < InnerCanvas.Children.Count; i++)
            UpdateStyle((Shape)InnerCanvas.Children[i], _primitives[i]);
      }

      private void OnMouseWheel(object sender, MouseWheelEventArgs e)
      {
         double factor = e.Delta > 0 ? 1.2 : 1.0 / 1.2;
         var pos = e.GetPosition(RootBorder);
         var m = _mt.Matrix;
         m.ScaleAt(factor, factor, pos.X, pos.Y);
         _mt.Matrix = m;
         UpdateStrokes();
         e.Handled = true;
      }

      private void OnRightButtonDown(object sender, MouseButtonEventArgs e)
      {
         _isPanning = true;
         _panStart = e.GetPosition(RootBorder);
         RootBorder.CaptureMouse();
         e.Handled = true;
      }

      private void OnMouseMove(object sender, MouseEventArgs e)
      {
         if (!_isPanning) return;
         var pos = e.GetPosition(RootBorder);
         var m = _mt.Matrix;
         m.Translate(pos.X - _panStart.X, pos.Y - _panStart.Y);
         _mt.Matrix = m;
         _panStart = pos;
      }

      private void OnRightButtonUp(object sender, MouseButtonEventArgs e)
      {
         _isPanning = false;
         RootBorder.ReleaseMouseCapture();
      }

      /// <summary>
      /// Все клики обрабатываются здесь через ручной hit-test по геометрии.
      /// Это надёжнее WPF shape hit-test при матричном масштабировании.
      /// </summary>
      private void OnBorderLeftButtonDown(object sender, MouseButtonEventArgs e)
      {
         var click = e.GetPosition(RootBorder);
         var m = _mt.Matrix;
         if (!m.HasInverse) return;
         var mi = m;
         mi.Invert();
         double dx = mi.M11 * click.X + mi.M21 * click.Y + mi.OffsetX;
         double dy = mi.M12 * click.X + mi.M22 * click.Y + mi.OffsetY;

         double s = Scale;
         double thr = s > 1e-10 ? HitThresholdPx / s : double.MaxValue;

         DxfPrimitive? best = null;
         double bestDist = thr;
         foreach (var p in _primitives)
         {
            double d = HitDistance(p, dx, dy);
            if (d < bestDist) { bestDist = d; best = p; }
         }

         // Сначала уведомляем VM (она присваивает роль), затем обновляем цвета
         if (best != null)
            PrimitiveClicked?.Invoke(best);

         for (int i = 0; i < _primitives.Count && i < InnerCanvas.Children.Count; i++)
            UpdateStyle((Shape)InnerCanvas.Children[i], _primitives[i]);
      }

      // Расстояние до примитива в DXF-пространстве.
      // Для окружностей: 0 если клик внутри — окружность всегда побеждает контур.
      private static double HitDistance(DxfPrimitive p, double dx, double dy)
      {
         if (p.Kind == DxfPrimitiveKind.Circle)
         {
            double d = Math.Sqrt((dx - p.CenterX) * (dx - p.CenterX) +
                                 (dy - p.CenterY) * (dy - p.CenterY));
            return d <= p.Radius ? 0.0 : d - p.Radius;
         }

         // Контур: минимальное расстояние до сегментов
         var xs = p.Xs!; var ys = p.Ys!;
         double min = double.MaxValue;
         for (int i = 0; i < xs.Length - 1; i++)
         {
            double d = DistToSegment(dx, dy, xs[i], ys[i], xs[i + 1], ys[i + 1]);
            if (d < min) min = d;
         }
         return min;
      }

      private static double DistToSegment(double px, double py,
                                          double ax, double ay, double bx, double by)
      {
         double ddx = bx - ax, ddy = by - ay;
         double lenSq = ddx * ddx + ddy * ddy;
         if (lenSq < 1e-20)
            return Math.Sqrt((px - ax) * (px - ax) + (py - ay) * (py - ay));
         double t = Math.Max(0, Math.Min(1, ((px - ax) * ddx + (py - ay) * ddy) / lenSq));
         double projX = ax + t * ddx, projY = ay + t * ddy;
         return Math.Sqrt((px - projX) * (px - projX) + (py - projY) * (py - projY));
      }

      private void UpdateStyle(Shape shape, DxfPrimitive p)
      {
         string color = p.Role switch
         {
            DxfRole.Hull       => "#4CAF50",
            DxfRole.Hole       => "#F44336",
            DxfRole.RebarGroup => "#FF9800",
            DxfRole.SingleBar  => "#FFC107",
            _                  => _colorMap.TryGetValue(p.LayerName, out var c) ? c : "#808080"
         };
         double s = Scale;
         shape.Stroke = ParseBrush(color);
         shape.StrokeThickness = p.Role != DxfRole.None
            ? (s > 1e-10 ? 3.0 / s : 3.0)
            : (s > 1e-10 ? 1.5 / s : 1.5);
      }

      private static Polyline MakePolyline(DxfPrimitive p, string color)
      {
         var pts = new PointCollection(p.Xs!.Length);
         for (int i = 0; i < p.Xs.Length; i++)
            pts.Add(new Point(p.Xs[i], p.Ys![i]));
         return new Polyline
         {
            Points = pts,
            Stroke = ParseBrush(color),
            StrokeThickness = 1.5,
            Fill = null,
            IsHitTestVisible = false,   // hit-test только через OnBorderLeftButtonDown
            Tag = p
         };
      }

      private static Path MakeCirclePath(DxfPrimitive p, string color)
      {
         return new Path
         {
            Data = new EllipseGeometry(new Point(p.CenterX, p.CenterY), p.Radius, p.Radius),
            Stroke = ParseBrush(color),
            StrokeThickness = 1.5,
            Fill = null,
            IsHitTestVisible = false,   // hit-test только через OnBorderLeftButtonDown
            Tag = p
         };
      }

      private static SolidColorBrush ParseBrush(string hex)
      {
         try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
         catch { return Brushes.Gray; }
      }
   }
}
