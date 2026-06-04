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
   /// Выбор — ЛКМ / Shift+ЛКМ / Ctrl+ЛКМ на примитиве.
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

      /// <summary>
      /// Вызывается при изменении выделения. Передаёт текущий список выделенных примитивов.
      /// </summary>
      public Action<IReadOnlyList<DxfPrimitive>>? SelectionChanged { get; set; }

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
            shape.MouseLeftButtonDown += OnShapeClicked;
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

      // Возвращает текущий масштаб трансформации (для компенсации толщины линий)
      private double Scale => Math.Abs(_mt.Matrix.M11);

      // Обновляет StrokeThickness всех фигур так, чтобы на экране они были ~1.5px
      private void UpdateStrokes()
      {
         double s = Scale;
         if (s < 1e-10) return;
         foreach (Shape shape in InnerCanvas.Children.OfType<Shape>())
         {
            bool sel = shape.Tag is DxfPrimitive p && p.IsSelected;
            shape.StrokeThickness = sel ? 3.0 / s : 1.5 / s;
         }
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

      private void OnBorderLeftButtonDown(object sender, MouseButtonEventArgs e)
      {
         if (Keyboard.Modifiers != ModifierKeys.None) return;
         foreach (var p in _primitives) p.IsSelected = false;
         foreach (Shape s in InnerCanvas.Children.OfType<Shape>()) UpdateStyle(s);
         SelectionChanged?.Invoke([]);
      }

      private void OnShapeClicked(object sender, MouseButtonEventArgs e)
      {
         if (sender is not Shape shape || shape.Tag is not DxfPrimitive clicked) return;
         var mod = Keyboard.Modifiers;

         if (mod == ModifierKeys.None)
         {
            foreach (var p in _primitives) p.IsSelected = false;
            foreach (Shape s in InnerCanvas.Children.OfType<Shape>()) UpdateStyle(s);
            clicked.IsSelected = true;
         }
         else if (mod.HasFlag(ModifierKeys.Shift))
            clicked.IsSelected = true;
         else if (mod.HasFlag(ModifierKeys.Control))
            clicked.IsSelected = !clicked.IsSelected;

         UpdateStyle(shape);
         SelectionChanged?.Invoke(_primitives.Where(p => p.IsSelected).ToList());
         e.Handled = true;
      }

      private void UpdateStyle(Shape shape)
      {
         if (shape.Tag is not DxfPrimitive p) return;
         double s = Scale;
         if (p.IsSelected)
         {
            shape.Stroke = Brushes.Yellow;
            shape.StrokeThickness = s > 1e-10 ? 3.0 / s : 3.0;
         }
         else
         {
            string color = _colorMap.TryGetValue(p.LayerName, out var c) ? c : "#808080";
            shape.Stroke = ParseBrush(color);
            shape.StrokeThickness = s > 1e-10 ? 1.5 / s : 1.5;
         }
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
            StrokeThickness = 1.5,   // пересчитается в UpdateStrokes после FitToView
            Fill = Brushes.Transparent,
            Tag = p
         };
      }

      private static Path MakeCirclePath(DxfPrimitive p, string color)
      {
         return new Path
         {
            Data = new EllipseGeometry(new Point(p.CenterX, p.CenterY), p.Radius, p.Radius),
            Stroke = ParseBrush(color),
            StrokeThickness = 1.5,   // пересчитается в UpdateStrokes после FitToView
            Fill = null,
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
