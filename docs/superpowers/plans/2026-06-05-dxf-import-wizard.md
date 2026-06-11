# DXF Import Wizard Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the 3-tab blind-list DXF import with a single-panel interactive canvas where primitives are selected by clicking directly on the drawing (zoom/pan/Shift/Ctrl).

**Architecture:** New `DxfInteractiveView` UserControl wraps a WPF `Canvas` with `MatrixTransform` for zoom/pan; DXF primitives are WPF `Polyline`/`Path` shapes with native hit-testing. `DxfPrimitive` wrapper ties each shape to its domain object (`Contour`/`CircleP`). `FromDxfVM` gains Arc + Line parsing; drops all transfer commands. `FromDxfPage` becomes a 3-column layout: layer legend | canvas | selected items.

**Tech Stack:** .NET 9 WPF, netDxf, CScore (Contour, CircleP, StressPoint), existing AppViewModel/DatabaseService.

---

## File Map

| Action | File |
|---|---|
| Add key | `OpenCS/Resources/Strings.ru-RU.xaml` |
| Add key | `OpenCS/Resources/Strings.en-US.xaml` |
| **Create** | `OpenCS/ViewModels/DxfPrimitive.cs` |
| **Create** | `OpenCS/Views/DxfInteractiveView.xaml` |
| **Create** | `OpenCS/Views/DxfInteractiveView.xaml.cs` |
| **Rewrite** | `OpenCS/ViewModels/FromDxfVM.cs` |
| **Rewrite** | `OpenCS/Views/FromDxfPage.xaml` |
| **Rewrite** | `OpenCS/Views/FromDxfPage.xaml.cs` |
| Delete | `OpenCS/Views/DxfPlot.xaml` |
| Delete | `OpenCS/Views/DxfPlot.xaml.cs` |

---

## Task 1: Add `DxfLayers` localisation key

**Files:**
- Modify: `OpenCS/Resources/Strings.ru-RU.xaml`
- Modify: `OpenCS/Resources/Strings.en-US.xaml`

- [ ] **Step 1: Add key to ru-RU**

In `Strings.ru-RU.xaml`, after `<system:String x:Key="DXF">DXF</system:String>` add:

```xml
    <system:String x:Key="DxfLayers">Слои</system:String>
```

- [ ] **Step 2: Add key to en-US**

In `Strings.en-US.xaml`, after `<system:String x:Key="DXF">DXF</system:String>` add:

```xml
    <system:String x:Key="DxfLayers">Layers</system:String>
```

- [ ] **Step 3: Build**

```
dotnet build OpenCS.sln
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```
git add OpenCS/Resources/Strings.ru-RU.xaml OpenCS/Resources/Strings.en-US.xaml
git commit -m "feat: add DxfLayers localisation key"
```

---

## Task 2: Create `DxfPrimitive.cs`

**Files:**
- Create: `OpenCS/ViewModels/DxfPrimitive.cs`

- [ ] **Step 1: Create the file**

```csharp
using CScore;

using System.Windows.Media;

namespace OpenCS.ViewModels
{
   /// <summary>Вид примитива DXF: контур или окружность.</summary>
   public enum DxfPrimitiveKind { Contour, Circle }

   /// <summary>Информация о слое DXF: имя и цвет для легенды.</summary>
   public record LayerInfo(string Name, string HexColor)
   {
      /// <summary>Кисть для отображения цветного маркера в легенде слоёв.</summary>
      public Brush LayerBrush { get; } =
         new SolidColorBrush((Color)ColorConverter.ConvertFromString(HexColor));
   }

   /// <summary>
   /// Обёртка DXF-примитива: связывает геометрию для рендера с доменным объектом
   /// (<see cref="Contour"/> или <see cref="CircleP"/>) и состоянием выделения.
   /// </summary>
   public class DxfPrimitive
   {
      public DxfPrimitiveKind Kind      { get; init; }
      public string           LayerName { get; init; } = string.Empty;
      public bool             IsSelected { get; set; }

      // Заполнено когда Kind == Contour
      public double[]? Xs      { get; init; }
      public double[]? Ys      { get; init; }
      public Contour?  Contour { get; init; }

      // Заполнено когда Kind == Circle
      public double   CenterX { get; init; }
      public double   CenterY { get; init; }
      public double   Radius  { get; init; }
      public CircleP? Circle  { get; init; }
   }
}
```

- [ ] **Step 2: Build**

```
dotnet build OpenCS.sln
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```
git add OpenCS/ViewModels/DxfPrimitive.cs
git commit -m "feat: add DxfPrimitive model and LayerInfo record"
```

---

## Task 3: Create `DxfInteractiveView`

**Files:**
- Create: `OpenCS/Views/DxfInteractiveView.xaml`
- Create: `OpenCS/Views/DxfInteractiveView.xaml.cs`

- [ ] **Step 1: Create XAML**

```xml
<UserControl x:Class="OpenCS.Views.DxfInteractiveView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             mc:Ignorable="d"
             d:DesignHeight="450" d:DesignWidth="800">
   <Border x:Name="RootBorder"
           Background="#1E1E1E"
           ClipToBounds="True"
           MouseWheel="OnMouseWheel"
           MouseRightButtonDown="OnRightButtonDown"
           MouseRightButtonUp="OnRightButtonUp"
           MouseMove="OnMouseMove"
           MouseLeftButtonDown="OnBorderLeftButtonDown">
      <Canvas x:Name="InnerCanvas"/>
   </Border>
</UserControl>
```

- [ ] **Step 2: Create code-behind**

```csharp
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
   /// Поддерживает zoom (колесо мыши), pan (правая кнопка) и выбор мышью
   /// с модификаторами Shift / Ctrl.
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
      /// Вызывается при изменении набора выделенных примитивов.
      /// Передаёт текущий список выделенных.
      /// </summary>
      public Action<IReadOnlyList<DxfPrimitive>>? SelectionChanged { get; set; }

      public DxfInteractiveView()
      {
         InitializeComponent();
         InnerCanvas.RenderTransform = _mt;
      }

      /// <summary>
      /// Загружает список примитивов и информацию о слоях, строит фигуры на канвасе
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

         // DXF: Y вверх; WPF: Y вниз — инвертируем ось Y через M22 = -s
         _mt.Matrix = new Matrix(s, 0, 0, -s, w / 2 - cx * s, h / 2 + cy * s);
      }

      private void OnMouseWheel(object sender, MouseWheelEventArgs e)
      {
         double factor = e.Delta > 0 ? 1.2 : 1.0 / 1.2;
         var pos = e.GetPosition(RootBorder);
         var m = _mt.Matrix;
         m.ScaleAt(factor, factor, pos.X, pos.Y);
         _mt.Matrix = m;
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
         if (p.IsSelected)
         {
            shape.Stroke = Brushes.Yellow;
            shape.StrokeThickness = 3.0;
         }
         else
         {
            string color = _colorMap.TryGetValue(p.LayerName, out var c) ? c : "#808080";
            shape.Stroke = ParseBrush(color);
            shape.StrokeThickness = 1.5;
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
            StrokeThickness = 1.5,
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
            StrokeThickness = 1.5,
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
```

- [ ] **Step 3: Build**

```
dotnet build OpenCS.sln
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```
git add OpenCS/Views/DxfInteractiveView.xaml OpenCS/Views/DxfInteractiveView.xaml.cs
git commit -m "feat: add DxfInteractiveView — WPF canvas with zoom/pan/selection"
```

---

## Task 4: Rewrite `FromDxfVM.cs`

**Files:**
- Rewrite: `OpenCS/ViewModels/FromDxfVM.cs`

- [ ] **Step 1: Replace the file contents**

```csharp
using CScore;

using OpenCS.Utilites;

using netDxf;
using netDxf.Entities;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;

namespace OpenCS.ViewModels
{
   /// <summary>
   /// ViewModel импорта геометрии из DXF. Выполняет парсинг Polyline2D, Circle,
   /// Arc (аппроксимация 32-точечной ломаной) и Line (сшивка в замкнутые цепи).
   /// Взаимодействие с канвасом — через колбэки <see cref="CanvasLoader"/>
   /// и <see cref="HandleSelectionChanged"/>.
   /// </summary>
   public class FromDxfVM : ViewModelBase
   {
      private static readonly string[] Palette =
         ["#318CE7", "#9457EB", "#CC397B", "#F07427", "#F4CA16", "#20B2AA"];

      public AppViewModel mvm = null!;

      private double _scale = 0.001;
      private int _unitIdx;
      private string _geometrySet = "dxf";
      private ObservableCollection<Contour> _contoursPrj = [];
      private ObservableCollection<CircleP> _circlesPrj = [];
      private List<DxfPrimitive> _primitives = [];

      public List<string> Units { get; } = ["мм", "см", "м"];

      /// <summary>Коллекция слоёв DXF для отображения легенды в левой панели.</summary>
      public ObservableCollection<LayerInfo> Layers { get; } = [];

      /// <summary>
      /// Вызывается code-behind страницы для передачи примитивов в <see cref="DxfInteractiveView"/>.
      /// </summary>
      public Action<IReadOnlyList<DxfPrimitive>, IReadOnlyList<LayerInfo>>? CanvasLoader { get; set; }

      public int UnitIdx
      {
         get => _unitIdx;
         set
         {
            _unitIdx = value;
            _scale = value == 0 ? 0.001 : value == 1 ? 0.01 : 1.0;
            OnPropertyChanged();
         }
      }

      public string GeometrySet
      {
         get => _geometrySet;
         set { _geometrySet = value; OnPropertyChanged(); }
      }

      public ObservableCollection<Contour> ContoursPrj
      {
         get => _contoursPrj;
         set { _contoursPrj = value; OnPropertyChanged(); }
      }

      public ObservableCollection<CircleP> CirclesPrj
      {
         get => _circlesPrj;
         set { _circlesPrj = value; OnPropertyChanged(); }
      }

      public ICommand OpenDXFCommand   { get; }
      public ICommand SaveContoursCommand { get; }
      public ICommand SaveCirclesCommand  { get; }

      public FromDxfVM()
      {
         OpenDXFCommand      = new RelayCommand(OpenDxf);
         SaveContoursCommand = new RelayCommand(SaveContours);
         SaveCirclesCommand  = new RelayCommand(SaveCircles);
      }

      /// <summary>
      /// Обновляет <see cref="ContoursPrj"/> и <see cref="CirclesPrj"/> по текущему
      /// выделению канваса. Вызывается из code-behind через <see cref="DxfInteractiveView.SelectionChanged"/>.
      /// </summary>
      public void HandleSelectionChanged(IReadOnlyList<DxfPrimitive> selected)
      {
         ContoursPrj = new ObservableCollection<Contour>(
            selected.Where(p => p.Kind == DxfPrimitiveKind.Contour && p.Contour != null)
                    .Select(p => p.Contour!));
         CirclesPrj = new ObservableCollection<CircleP>(
            selected.Where(p => p.Kind == DxfPrimitiveKind.Circle && p.Circle != null)
                    .Select(p => p.Circle!));
      }

      private void SaveContours(object? _ = null)
      {
         if (_contoursPrj.Count == 0) return;
         mvm.db.AddRange(_contoursPrj);
         mvm.LogService.Info($"В проект добавлено {_contoursPrj.Count} контуров");
         ContoursPrj.Clear();
         mvm.ContoursRenumber();
      }

      private void SaveCircles(object? _ = null)
      {
         if (_circlesPrj.Count == 0) return;
         mvm.db.AddRange(_circlesPrj);
         mvm.LogService.Info($"В проект добавлено {_circlesPrj.Count} окружностей");
         CirclesPrj.Clear();
         mvm.CirclesRenumber();
      }

      private void OpenDxf(object? _ = null)
      {
         string fileName = mvm.FileDialogService.OpenFile(
            filter: "Файл обмена чертежами (*.dxf)|*.dxf",
            title: "Импорт данных из файла DXF");
         if (string.IsNullOrEmpty(fileName)) return;

         ContoursPrj.Clear();
         CirclesPrj.Clear();

         var dxf = DxfDocument.Load(fileName);
         GeometrySet = dxf.Name;

         _primitives = ParseDxf(dxf);

         Layers.Clear();
         var names = _primitives.Select(p => p.LayerName).Distinct().ToList();
         for (int i = 0; i < names.Count; i++)
            Layers.Add(new LayerInfo(names[i], Palette[i % Palette.Length]));

         CanvasLoader?.Invoke(_primitives, Layers);
      }

      // ── Парсинг ────────────────────────────────────────────────────────────

      private List<DxfPrimitive> ParseDxf(DxfDocument dxf)
      {
         var result = new List<DxfPrimitive>();
         int num = 1;

         foreach (var p in dxf.Entities.Polylines2D)
            result.Add(PolylineToPrimitive(p, num++));

         foreach (var c in dxf.Entities.Circles)
            result.Add(CircleToPrimitive(c, num++));

         foreach (var a in dxf.Entities.Arcs)
            result.Add(ArcToPrimitive(a, num++));

         foreach (var group in dxf.Entities.Lines.GroupBy(l => l.Layer.Name))
         {
            var stitched = StitchLines(group, group.Key, num);
            result.AddRange(stitched);
            num += stitched.Count;
         }

         return result;
      }

      private DxfPrimitive PolylineToPrimitive(Polyline2D pline, int num)
      {
         var verts = pline.Vertexes;
         bool needClose = pline.IsClosed &&
            !verts.First().Position.Equals(verts.Last().Position, 1e-4);
         int total = verts.Count + (needClose ? 1 : 0);

         var xs = new double[total];
         var ys = new double[total];
         var pts = new List<StressPoint>(total);

         int j = 0;
         foreach (var v in verts)
         {
            xs[j] = v.Position.X * _scale;
            ys[j] = v.Position.Y * _scale;
            pts.Add(new StressPoint(xs[j], ys[j]) { Num = j + 1 });
            j++;
         }
         if (needClose)
         {
            xs[j] = verts.First().Position.X * _scale;
            ys[j] = verts.First().Position.Y * _scale;
            pts.Add(new StressPoint(xs[j], ys[j]) { Num = j + 1 });
         }

         var contour = new Contour(pts, pline.Layer.Name) { Num = num, GeometrySet = _geometrySet };
         contour.SetWKT();

         return new DxfPrimitive
         {
            Kind = DxfPrimitiveKind.Contour,
            LayerName = pline.Layer.Name,
            Xs = xs, Ys = ys,
            Contour = contour
         };
      }

      private DxfPrimitive CircleToPrimitive(Circle circle, int num)
      {
         var cp = new CircleP(circle.Center.X * _scale, circle.Center.Y * _scale, circle.Radius * _scale)
         {
            Num = num,
            Tag = circle.Layer.Name,
            GeometrySet = _geometrySet
         };
         return new DxfPrimitive
         {
            Kind = DxfPrimitiveKind.Circle,
            LayerName = circle.Layer.Name,
            CenterX = cp.X, CenterY = cp.Y, Radius = cp.Radius,
            Circle = cp
         };
      }

      private DxfPrimitive ArcToPrimitive(Arc arc, int num)
      {
         // netDxf углы — в градусах, Math.Cos/Sin — в радианах
         double startRad = arc.StartAngle * Math.PI / 180;
         double endRad   = arc.EndAngle   * Math.PI / 180;
         if (endRad < startRad) endRad += 2 * Math.PI;

         const int N = 32;
         var xs = new double[N + 1];
         var ys = new double[N + 1];
         var pts = new List<StressPoint>(N + 1);

         for (int i = 0; i <= N; i++)
         {
            double angle = startRad + i * (endRad - startRad) / N;
            xs[i] = (arc.Center.X + arc.Radius * Math.Cos(angle)) * _scale;
            ys[i] = (arc.Center.Y + arc.Radius * Math.Sin(angle)) * _scale;
            pts.Add(new StressPoint(xs[i], ys[i]) { Num = i + 1 });
         }

         var contour = new Contour(pts, arc.Layer.Name) { Num = num, GeometrySet = _geometrySet };
         contour.SetWKT();

         return new DxfPrimitive
         {
            Kind = DxfPrimitiveKind.Contour,
            LayerName = arc.Layer.Name,
            Xs = xs, Ys = ys,
            Contour = contour
         };
      }

      /// <summary>
      /// Сшивает набор отрезков одного слоя в замкнутые/незамкнутые цепи.
      /// Алгоритм: граф смежности + жадный DFS.
      /// </summary>
      private List<DxfPrimitive> StitchLines(IEnumerable<Line> lines, string layerName, int startNum)
      {
         const double tol = 1e-6;
         (double x, double y) Snap(double x, double y) =>
            (Math.Round(x / tol) * tol, Math.Round(y / tol) * tol);

         var segs = lines.Select(l => (
            A: Snap(l.StartPoint.X * _scale, l.StartPoint.Y * _scale),
            B: Snap(l.EndPoint.X * _scale, l.EndPoint.Y * _scale)
         )).ToList();

         if (segs.Count == 0) return [];

         var adj = new Dictionary<(double, double), List<int>>();
         for (int i = 0; i < segs.Count; i++)
         {
            if (!adj.TryGetValue(segs[i].A, out var la)) adj[segs[i].A] = la = [];
            la.Add(i);
            if (!adj.TryGetValue(segs[i].B, out var lb)) adj[segs[i].B] = lb = [];
            lb.Add(i);
         }

         var used = new bool[segs.Count];
         var result = new List<DxfPrimitive>();
         int num = startNum;

         for (int start = 0; start < segs.Count; start++)
         {
            if (used[start]) continue;
            used[start] = true;

            var chain = new List<(double x, double y)> { segs[start].A, segs[start].B };
            var startPt = segs[start].A;
            var curPt = segs[start].B;

            while (true)
            {
               int next = adj[curPt].FirstOrDefault(i => !used[i], -1);
               if (next == -1) break;
               used[next] = true;
               curPt = segs[next].A == curPt ? segs[next].B : segs[next].A;
               chain.Add(curPt);
               if (curPt == startPt) break;
            }

            if (chain.Count < 2) continue;

            var xs = chain.Select(p => p.x).ToArray();
            var ys = chain.Select(p => p.y).ToArray();
            var pts = chain.Select((p, i) => new StressPoint(p.x, p.y) { Num = i + 1 }).ToList();

            var contour = new Contour(pts, layerName) { Num = num, GeometrySet = _geometrySet };
            contour.SetWKT();

            result.Add(new DxfPrimitive
            {
               Kind = DxfPrimitiveKind.Contour,
               LayerName = layerName,
               Xs = xs, Ys = ys,
               Contour = contour
            });
            num++;
         }

         return result;
      }
   }
}
```

- [ ] **Step 2: Build**

```
dotnet build OpenCS.sln
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```
git add OpenCS/ViewModels/FromDxfVM.cs
git commit -m "feat: rewrite FromDxfVM — Arc/Line parsing, selection callback, remove transfer commands"
```

---

## Task 5: Redesign `FromDxfPage`

**Files:**
- Rewrite: `OpenCS/Views/FromDxfPage.xaml`
- Rewrite: `OpenCS/Views/FromDxfPage.xaml.cs`

- [ ] **Step 1: Replace XAML**

```xml
<UserControl x:Class="OpenCS.Views.FromDxfPage"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:local="clr-namespace:OpenCS.Views"
             mc:Ignorable="d"
             d:DesignHeight="450" d:DesignWidth="800">

   <UserControl.Resources>
      <ResourceDictionary Source="/Images/svg.xaml"/>
   </UserControl.Resources>

   <Grid>
      <Grid.RowDefinitions>
         <RowDefinition Height="Auto"/>
         <RowDefinition/>
      </Grid.RowDefinitions>

      <!-- Toolbar -->
      <DockPanel Grid.Row="0" Margin="5" LastChildFill="False">
         <Button Height="25" Width="25" BorderThickness="0" Background="Transparent"
                 Command="{Binding OpenDXFCommand}" DockPanel.Dock="Left" Margin="0,0,8,0">
            <Image Source="{StaticResource di_dxf_file_xaml}"/>
         </Button>
         <TextBlock Text="{DynamicResource DesignationSet}" VerticalAlignment="Center"
                    Margin="0,0,4,0" DockPanel.Dock="Left"/>
         <TextBox Width="120" VerticalContentAlignment="Center"
                  Text="{Binding GeometrySet}" DockPanel.Dock="Left" Margin="0,0,16,0"/>
         <TextBlock Text="{DynamicResource DxfUnits}" VerticalAlignment="Center"
                    Margin="0,0,4,0" DockPanel.Dock="Left"/>
         <ComboBox ItemsSource="{Binding Units}" SelectedIndex="{Binding UnitIdx}"
                   Width="60" DockPanel.Dock="Left"/>
      </DockPanel>

      <!-- 3-column main area -->
      <Grid Grid.Row="1">
         <Grid.ColumnDefinitions>
            <ColumnDefinition Width="160"/>
            <ColumnDefinition/>
            <ColumnDefinition Width="200"/>
         </Grid.ColumnDefinitions>

         <!-- Col 0: Layer legend -->
         <GroupBox Grid.Column="0" Header="{DynamicResource DxfLayers}" Margin="4">
            <ScrollViewer VerticalScrollBarVisibility="Auto">
               <ItemsControl ItemsSource="{Binding Layers}" Margin="4">
                  <ItemsControl.ItemTemplate>
                     <DataTemplate>
                        <StackPanel Orientation="Horizontal" Margin="2">
                           <Rectangle Width="14" Height="14" Margin="0,0,6,0"
                                      Fill="{Binding LayerBrush}"/>
                           <TextBlock Text="{Binding Name}" VerticalAlignment="Center"/>
                        </StackPanel>
                     </DataTemplate>
                  </ItemsControl.ItemTemplate>
               </ItemsControl>
            </ScrollViewer>
         </GroupBox>

         <!-- Col 1: Interactive DXF canvas -->
         <local:DxfInteractiveView x:Name="InteractiveCanvas" Grid.Column="1" Margin="4"/>

         <!-- Col 2: Selected primitives -->
         <Grid Grid.Column="2">
            <Grid.RowDefinitions>
               <RowDefinition/>
               <RowDefinition/>
            </Grid.RowDefinitions>

            <GroupBox Grid.Row="0" Header="{DynamicResource Contours}" Margin="4">
               <Grid>
                  <Grid.RowDefinitions>
                     <RowDefinition/>
                     <RowDefinition Height="Auto"/>
                  </Grid.RowDefinitions>
                  <ListBox Grid.Row="0" ItemsSource="{Binding ContoursPrj}"
                           DisplayMemberPath="Tag" BorderThickness="0" Margin="4"/>
                  <Button Grid.Row="1" Height="25" Margin="4"
                          Command="{Binding SaveContoursCommand}"
                          Content="{DynamicResource AddCaps}"/>
               </Grid>
            </GroupBox>

            <GroupBox Grid.Row="1" Header="{DynamicResource Circles}" Margin="4">
               <Grid>
                  <Grid.RowDefinitions>
                     <RowDefinition/>
                     <RowDefinition Height="Auto"/>
                  </Grid.RowDefinitions>
                  <ListBox Grid.Row="0" ItemsSource="{Binding CirclesPrj}"
                           DisplayMemberPath="Tag" BorderThickness="0" Margin="4"/>
                  <Button Grid.Row="1" Height="25" Margin="4"
                          Command="{Binding SaveCirclesCommand}"
                          Content="{DynamicResource AddCaps}"/>
               </Grid>
            </GroupBox>
         </Grid>
      </Grid>
   </Grid>
</UserControl>
```

- [ ] **Step 2: Replace code-behind**

```csharp
using OpenCS.ViewModels;

using System.Windows.Controls;

namespace OpenCS.Views
{
   /// <summary>
   /// Страница импорта DXF. Связывает <see cref="FromDxfVM"/> с
   /// <see cref="DxfInteractiveView"/> через колбэки (не через команды).
   /// </summary>
   public partial class FromDxfPage : UserControl
   {
      public FromDxfPage(AppViewModel mvm)
      {
         InitializeComponent();
         var vm = new FromDxfVM { mvm = mvm };
         DataContext = vm;
         vm.CanvasLoader = (prims, layers) => InteractiveCanvas.Load(prims, layers);
         InteractiveCanvas.SelectionChanged = vm.HandleSelectionChanged;
      }
   }
}
```

- [ ] **Step 3: Build**

```
dotnet build OpenCS.sln
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```
git add OpenCS/Views/FromDxfPage.xaml OpenCS/Views/FromDxfPage.xaml.cs
git commit -m "feat: redesign FromDxfPage — single panel with layer legend, interactive canvas, selected list"
```

---

## Task 6: Delete `DxfPlot` and final verification

**Files:**
- Delete: `OpenCS/Views/DxfPlot.xaml`
- Delete: `OpenCS/Views/DxfPlot.xaml.cs`

- [ ] **Step 1: Verify no remaining references to DxfPlot**

```
grep -r "DxfPlot" OpenCS/ --include="*.cs" --include="*.xaml"
```

Expected: no output (zero matches).

- [ ] **Step 2: Delete files**

```
git rm OpenCS/Views/DxfPlot.xaml OpenCS/Views/DxfPlot.xaml.cs
```

- [ ] **Step 3: Final build**

```
dotnet build OpenCS.sln
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```
git commit -m "chore: remove obsolete DxfPlot view"
```
