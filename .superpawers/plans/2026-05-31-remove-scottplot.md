# Remove ScottPlot — Replace with Pure WPF Rendering

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpawers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Eliminate the external ScottPlot.WPF NuGet dependency and replace all plotting with pure WPF (Canvas-based FrameworkElement with DrawingContext).

**Architecture:** Keep the `IPlotService` abstraction intact. Replace `WpfPlotService` (ScottPlot-backed) with `WpfDrawingService` (pure WPF DrawingContext). Replace `<sp:WpfPlot>` XAML controls with a custom `PlotCanvas` FrameworkElement. Refactor DiagramPage and DxfPlot to use IPlotService instead of direct ScottPlot API calls. Title/axis labels/legend move to separate WPF TextBlock/ItemsControl in XAML.

**Tech Stack:** .NET 9.0 WPF, System.Windows.Media (DrawingContext, DrawingVisual, Pen, Brush), netDxf (untouched), no new packages.

---

## Files Affected

| Action | File |
|--------|------|
| **Modify** | `OpenCS/Services/IPlotService.cs` — add 2 methods |
| **Replace** | `OpenCS/Services/WpfPlotService.cs` → new implementation |
| **Create** | `OpenCS/Views/PlotCanvas.cs` — WPF FrameworkElement + DrawingVisual host |
| **Modify** | `OpenCS/Views/ContourPlot.xaml` — replace `<sp:WpfPlot>` |
| **Modify** | `OpenCS/Views/ContourPlot.xaml.cs` — wire new service |
| **Modify** | `OpenCS/Views/RCFiberRegionPage.xaml` — replace `<sp:WpfPlot>` |
| **Modify** | `OpenCS/Views/RCFiberRegionPage.xaml.cs` — wire new service |
| **Modify** | `OpenCS/Views/RCFiberRegionView.xaml` — replace `<sp:WpfPlot>` |
| **Modify** | `OpenCS/Views/RCFiberRegionView.xaml.cs` — wire new service |
| **Modify** | `OpenCS/Views/DiagramPage.xaml` — replace `<sp:WpfPlot>` + add TextBlock for title/labels |
| **Modify** | `OpenCS/Views/DiagramPage.xaml.cs` — refactor to use IPlotService |
| **Modify** | `OpenCS/Views/DxfPlot.xaml` — replace `<sp:WpfPlot>` |
| **Modify** | `OpenCS/Views/DxfPlot.xaml.cs` — refactor to use IPlotService |
| **Modify** | `OpenCS/Views/RegionPlot.xaml` — replace `<sp:WpfPlot>` |
| **Modify** | `OpenCS/OpenCS.csproj` — remove ScottPlot.WPF package |
| **No change** | `OpenCS/ViewModels/RCFiberRegionVM.cs` — uses IPlotService, no ScottPlot refs |
| **No change** | `OpenCS/ViewModels/ContourVM.cs` — uses IPlotService, no ScottPlot refs |

---

### Task 1: Extend IPlotService interface

**Files:**
- Modify: `OpenCS/Services/IPlotService.cs`

- [ ] **Step 1: Add missing methods to IPlotService**

Add two new method signatures to the existing interface:

```csharp
// IPlotService.cs — add after line 12 (after AddCircle):
void AddMarkers(double[] xs, double[] ys, float markerSize = 4, string color = null, string label = null);
void ShowLegend(bool show = true);
```

Full updated interface:

```csharp
namespace OpenCS.Services
{
   /// <summary>
   /// Сервис отрисовки графиков. Абстрагирует WPF-рендеринг от ViewModel.
   /// </summary>
   public interface IPlotService
   {
      void Clear();
      void AddScatter(double[] xs, double[] ys, double lineWidth = 1, string color = null, string label = null);
      void AddLine(double[] xs, double[] ys, string label = null);
      void AddPolygon(double[] xs, double[] ys, string fillColor = null, string lineColor = null);
      void AddCircle(double x, double y, double radius, string fillColor = null, string lineColor = null, float lineWidth = 1);
      void AddMarkers(double[] xs, double[] ys, float markerSize = 4, string color = null, string label = null);
      void ShowLegend(bool show = true);
      void EnableSquareAxes();
      void AutoScale();
      void SetAxisLimits(double xMin, double xMax, double yMin, double yMax);
      void SetTitle(string title);
      void SetXLabel(string label);
      void SetYLabel(string label);
      void Refresh();
   }
}
```

Add optional `label` to `AddScatter` and `lineWidth` to `AddCircle`.

- [ ] **Step 2: Build to verify interface compiles**

```bash
dotnet build OpenCS.sln
```

Expected: compilation errors in `WpfPlotService.cs` (doesn't implement new methods) and `RCFiberRegionVM.cs`, `ContourVM.cs` (their calls to AddScatter/AddCircle with existing args are still fine since new params are optional). Fix WpfPlotService next.

---

### Task 2: Create PlotCanvas WPF control

**Files:**
- Create: `OpenCS/Views/PlotCanvas.cs`

- [ ] **Step 1: Create `PlotCanvas.cs`**

This is a `FrameworkElement` that hosts `DrawingVisual` children. It provides methods to add visual elements and triggers redraw. The coordinate system is managed externally (by WpfDrawingService), this control just draws what it's told.

```csharp
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace OpenCS.Views
{
   /// <summary>
   /// WPF-элемент для отрисовки графиков без внешних зависимостей.
   /// Использует DrawingVisual для рендеринга геометрических примитивов.
   /// </summary>
   public class PlotCanvas : FrameworkElement
   {
      private readonly VisualCollection _visuals;
      private readonly DrawingVisual _plotVisual;
      private readonly DrawingVisual _axesVisual;

      public PlotCanvas()
      {
         _plotVisual = new DrawingVisual();
         _axesVisual = new DrawingVisual();
         _visuals = new VisualCollection(this) { _plotVisual, _axesVisual };
         ClipToBounds = true;
         Background = Brushes.White;
      }

      protected override int VisualChildrenCount => _visuals.Count;
      protected override Visual GetVisualChild(int index) => _visuals[index];

      protected override Size MeasureOverride(Size availableSize)
      {
         return new Size(
            double.IsInfinity(availableSize.Width) ? 400 : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? 300 : availableSize.Height);
      }

      /// <summary>
      /// Отрисовывает элементы графика с заданным преобразованием координат.
      /// </summary>
      /// <param name="elements">Список графических примитивов.</param>
      /// <param name="transform">Матрица преобразования данных в пиксели (Affine 3x2).</param>
      /// <param name="showAxes">Отображать ли координатные оси.</param>
      /// <param name="xLabel">Метка оси X (null — не показывать).</param>
      /// <param name="yLabel">Метка оси Y (null — не показывать).</param>
      /// <param name="title">Заголовок графика (null — не показывать).</param>
      public void Draw(
          IReadOnlyList<PlotElement> elements,
          MatrixTransform transform,
          bool showAxes = true,
          string? xLabel = null,
          string? yLabel = null,
          string? title = null)
      {
         using var dc = _plotVisual.RenderOpen();
         dc.PushTransform(transform);

         foreach (var el in elements)
            el.Render(dc);

         dc.Close();

         using var adc = _axesVisual.RenderOpen();

         if (showAxes)
            DrawAxes(adc, transform);

         if (title != null)
            DrawTitle(adc, title);

         adc.Close();
      }

      private void DrawAxes(DrawingContext dc, MatrixTransform transform)
      {
         var pen = new Pen(Brushes.LightGray, 0.5);
         double w = ActualWidth;
         double h = ActualHeight;
         if (w < 1 || h < 1) { w = 400; h = 300; }

         dc.DrawLine(pen, new Point(0, 0), new Point(w, 0));
         dc.DrawLine(pen, new Point(0, 0), new Point(0, h));
      }

      private void DrawTitle(DrawingContext dc, string title)
      {
         var ft = new FormattedText(title, CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), 13, Brushes.Black, 1.0);
         double w = ActualWidth;
         if (w < 1) w = 400;
         dc.DrawText(ft, new Point((w - ft.Width) / 2, 4));
      }

      /// <summary>
      /// Очищает все визуальные элементы.
      /// </summary>
      public void Clear()
      {
         using var dc = _plotVisual.RenderOpen();
         dc.Close();
         using var adc = _axesVisual.RenderOpen();
         adc.Close();
      }
   }
}
```

---

### Task 3: Create PlotElement model classes

**Files:**
- Create: `OpenCS/Views/PlotElement.cs`

- [ ] **Step 1: Create `PlotElement.cs`**

These are immutable records representing plot primitives. The `Render` method draws directly to a `DrawingContext`.

```csharp
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace OpenCS.Views
{
   /// <summary>
   /// Абстрактный элемент графика для WPF-рендеринга.
   /// </summary>
   public abstract record PlotElement
   {
      public abstract void Render(DrawingContext dc);
   }

   /// <summary>
   /// Ломаная линия (scatter-график).
   /// </summary>
   public record ScatterElement : PlotElement
   {
      public Point[] Points { get; init; } = [];
      public Brush Stroke { get; init; } = Brushes.Black;
      public double StrokeThickness { get; init; } = 1;
      public string? Label { get; init; }

      public override void Render(DrawingContext dc)
      {
         if (Points.Length < 2) return;
         var pen = new Pen(Stroke, StrokeThickness);
         for (int i = 0; i < Points.Length - 1; i++)
            dc.DrawLine(pen, Points[i], Points[i + 1]);
      }
   }

   /// <summary>
   /// Замкнутый многоугольник с заливкой.
   /// </summary>
   public record PolygonElement : PlotElement
   {
      public Point[] Points { get; init; } = [];
      public Brush? Fill { get; init; }
      public Brush Stroke { get; init; } = Brushes.Black;
      public double StrokeThickness { get; init; } = 1;

      public override void Render(DrawingContext dc)
      {
         if (Points.Length < 3) return;
         var stream = new StreamGeometry();
         using var ctx = stream.Open();
         ctx.BeginFigure(Points[0], true, true);
         var tail = new Point[Points.Length - 1];
         Array.Copy(Points, 1, tail, 0, tail.Length);
         ctx.PolyLineTo(tail, true, true);
         stream.Freeze();
         if (Fill != null)
            dc.DrawGeometry(Fill, new Pen(Stroke, StrokeThickness), stream);
         else
            dc.DrawGeometry(null, new Pen(Stroke, StrokeThickness), stream);
      }
   }

   /// <summary>
   /// Окружность.
   /// </summary>
   public record CircleElement : PlotElement
   {
      public Point Center { get; init; }
      public double Radius { get; init; }
      public Brush? Fill { get; init; }
      public Brush Stroke { get; init; } = Brushes.Black;
      public double StrokeThickness { get; init; } = 1;

      public override void Render(DrawingContext dc)
      {
         if (Radius <= 0) return;
         var pen = new Pen(Stroke, StrokeThickness);
         double d = Radius * 2;
         dc.DrawEllipse(Fill, pen, Center, Radius, Radius);
      }
   }

   /// <summary>
   /// Маркеры (точки-круги на графике).
   /// </summary>
   public record MarkerElement : PlotElement
   {
      public Point[] Points { get; init; } = [];
      public Brush Fill { get; init; } = Brushes.Black;
      public double MarkerSize { get; init; } = 4;
      public string? Label { get; init; }

      public override void Render(DrawingContext dc)
      {
         double half = MarkerSize / 2.0;
         foreach (var pt in Points)
            dc.DrawEllipse(Fill, null, pt, half, half);
      }
   }
}
```

---

### Task 4: Create WpfDrawingService (replaces WpfPlotService)

**Files:**
- Modify: `OpenCS/Services/WpfPlotService.cs` — rewrite completely

- [ ] **Step 1: Rewrite `WpfPlotService.cs`**

Replace all ScottPlot references with pure WPF rendering. The service:
- Stores plot elements in a list
- Manages axis limits and auto-scale
- Computes the coordinate transform (data → pixels)
- Delegates drawing to `PlotCanvas`

```csharp
using OpenCS.Views;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace OpenCS.Services
{
   /// <summary>
   /// WPF-реализация сервиса отрисовки графиков без внешних зависимостей.
   /// </summary>
   public class WpfPlotService : IPlotService
   {
      private readonly PlotCanvas _canvas;
      private readonly List<PlotElement> _elements = [];
      private readonly List<(PlotElement el, string label)> _labeledElements = [];

      private double _xMin = double.MaxValue, _xMax = double.MinValue;
      private double _yMin = double.MaxValue, _yMax = double.MinValue;
      private bool _squareAxes;
      private bool _autoScale = true;
      private bool _showLegend;

      private string? _title, _xLabel, _yLabel;

      public WpfPlotService(PlotCanvas canvas)
      {
         _canvas = canvas;
      }

      public void Clear()
      {
         _elements.Clear();
         _labeledElements.Clear();
         _canvas.Clear();
         _xMin = double.MaxValue; _xMax = double.MinValue;
         _yMin = double.MaxValue; _yMax = double.MinValue;
         _squareAxes = false;
         _autoScale = true;
         _showLegend = false;
         _title = _xLabel = _yLabel = null;
      }

      public void AddScatter(double[] xs, double[] ys, double lineWidth = 1, string color = null, string label = null)
      {
         if (xs == null || ys == null || xs.Length < 2) return;
         var points = ToPoints(xs, ys);
         UpdateBounds(xs, ys);
         var element = new ScatterElement
         {
            Points = points,
            Stroke = ParseColor(color ?? "#000000"),
            StrokeThickness = lineWidth,
            Label = label
         };
         _elements.Add(element);
         if (label != null)
            _labeledElements.Add((element, label));
      }

      public void AddLine(double[] xs, double[] ys, string label = null)
      {
         AddScatter(xs, ys, 1, null, label);
      }

      public void AddPolygon(double[] xs, double[] ys, string fillColor = null, string lineColor = null)
      {
         if (xs == null || ys == null || xs.Length < 3) return;
         var points = ToPoints(xs, ys);
         UpdateBounds(xs, ys);
         _elements.Add(new PolygonElement
         {
            Points = points,
            Fill = fillColor != null ? ParseColor(fillColor) : null,
            Stroke = ParseColor(lineColor ?? "#000000"),
            StrokeThickness = 1
         });
      }

      public void AddCircle(double x, double y, double radius, string fillColor = null, string lineColor = null, float lineWidth = 1)
      {
         UpdateBounds(new[] { x - radius, x + radius }, new[] { y - radius, y + radius });
         _elements.Add(new CircleElement
         {
            Center = new Point(x, y),
            Radius = radius,
            Fill = fillColor != null ? ParseColor(fillColor) : null,
            Stroke = ParseColor(lineColor ?? "#000000"),
            StrokeThickness = lineWidth
         });
      }

      public void AddMarkers(double[] xs, double[] ys, float markerSize = 4, string color = null, string label = null)
      {
         if (xs == null || ys == null || xs.Length == 0) return;
         var points = ToPoints(xs, ys);
         UpdateBounds(xs, ys);
         var element = new MarkerElement
         {
            Points = points,
            Fill = ParseColor(color ?? "#000000"),
            MarkerSize = markerSize,
            Label = label
         };
         _elements.Add(element);
         if (label != null)
            _labeledElements.Add((element, label));
      }

      public void ShowLegend(bool show = true)
      {
         _showLegend = show;
      }

      public void EnableSquareAxes()
      {
         _squareAxes = true;
      }

      public void AutoScale()
      {
         _autoScale = true;
      }

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
         if (_elements.Count == 0)
            return;

         double w = _canvas.ActualWidth;
         double h = _canvas.ActualHeight;
         if (w < 2 || h < 2) { w = 400; h = 300; }

         double xMin = _xMin, xMax = _xMax;
         double yMin = _yMin, yMax = _yMax;

         if (double.IsInfinity(xMin)) { xMin = 0; xMax = 1; }
         if (double.IsInfinity(yMin)) { yMin = 0; yMax = 1; }

         double padX = (xMax - xMin) * 0.05;
         double padY = (yMax - yMin) * 0.05;
         xMin -= padX; xMax += padX;
         yMin -= padY; yMax += padY;

         if (xMax == xMin) { xMax = xMin + 1; }
         if (yMax == yMin) { yMax = yMin + 1; }

         double margin = 30;
         double pw = w - 2 * margin;
         double ph = h - 2 * margin;

         double dataW = xMax - xMin;
         double dataH = yMax - yMin;

         if (_squareAxes)
         {
            double aspect = pw / ph;
            double dataAspect = dataW / dataH;
            if (dataAspect > aspect)
            {
               double newH = dataW / aspect;
               double center = (yMin + yMax) / 2;
               yMin = center - newH / 2;
               yMax = center + newH / 2;
               dataH = yMax - yMin;
            }
            else
            {
               double newW = dataH * aspect;
               double center = (xMin + xMax) / 2;
               xMin = center - newW / 2;
               xMax = center + newW / 2;
               dataW = xMax - xMin;
            }
         }

         double sx = pw / dataW;
         double sy = -ph / dataH;

         var transform = new MatrixTransform(
            sx, 0, 0, sy,
            margin - xMin * sx,
            margin + ph + yMin * sy);

         _canvas.Draw(_elements, transform, true, _xLabel, _yLabel, _title);
      }

      private static Point[] ToPoints(double[] xs, double[] ys)
      {
         int n = Math.Min(xs.Length, ys.Length);
         var points = new Point[n];
         for (int i = 0; i < n; i++)
            points[i] = new Point(xs[i], ys[i]);
         return points;
      }

      private void UpdateBounds(double[] xs, double[] ys)
      {
         if (!_autoScale) return;
         foreach (var x in xs)
         {
            if (x < _xMin) _xMin = x;
            if (x > _xMax) _xMax = x;
         }
         foreach (var y in ys)
         {
            if (y < _yMin) _yMin = y;
            if (y > _yMax) _yMax = y;
         }
      }

      private static Brush ParseColor(string hex)
      {
         try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
         catch { return Brushes.Black; }
      }
   }
}
```

---

### Task 5: Replace `<sp:WpfPlot>` in ContourPlot

**Files:**
- Modify: `OpenCS/Views/ContourPlot.xaml`
- Modify: `OpenCS/Views/ContourPlot.xaml.cs`

- [ ] **Step 1: Update ContourPlot.xaml**

Replace line 9 (ScottPlot namespace) with:
```xml
xmlns:local="clr-namespace:OpenCS.Views"
```

Replace line 136 (`<sp:WpfPlot .../>`) with:
```xml
<local:PlotCanvas x:Name="ViewPl" Margin="5,10,5,5" Grid.Column="1"/>
```

- [ ] **Step 2: Update ContourPlot.xaml.cs**

Replace line 19 `var plotService = new WpfPlotService(ViewPl);` — no change needed since `WpfPlotService` keeps the same constructor interface internally, but now takes `PlotCanvas` instead of `ScottPlot.WPF.WpfPlot`. Actually, we need to change from `WpfPlot` to `PlotCanvas`.

```csharp
// In constructor, replace line 19:
var plotService = new WpfPlotService(ViewPl);
// ViewPl is now PlotCanvas — constructor compatible, no change needed to this line.
// But we must remove the direct plotService calls on lines 23-27 since they're
// done in ContourVM now:
```

Actually wait — lines 23-27 use `plotService` directly in the code-behind. Those calls should either remain (they use IPlotService) or be moved to ContourVM. Looking at it:

```csharp
if (isSaved)
{
   mvm.CurrentContour.Contour.PointsToXYs();
   plotService.AddScatter(mvm.CurrentContour.Contour.X.ToArray(), mvm.CurrentContour.Contour.Y.ToArray(), lineWidth: 2);
   plotService.EnableSquareAxes();
   plotService.AutoScale();
   plotService.Refresh();
}
```

This draws the contour immediately. The IPlotService interface is still the same, so these calls work. But the constructor parameter type changes. The key change:

```csharp
// ContourPlot.xaml.cs — update constructor body:
var plotService = new WpfPlotService(ViewPl); // ViewPl is now PlotCanvas, still works

if (isSaved)
{
   mvm.CurrentContour.Contour.PointsToXYs();
   plotService.AddScatter(mvm.CurrentContour.Contour.X.ToArray(), mvm.CurrentContour.Contour.Y.ToArray(), lineWidth: 2);
   plotService.EnableSquareAxes();
   plotService.AutoScale();
   plotService.Refresh();
}
```

No code changes needed in .xaml.cs beyond removing the ScottPlot/WpfPlot import (not even imported currently — no ScottPlot import in ContourPlot.xaml.cs). The `WpfPlotService` name stays, but the constructor now takes `PlotCanvas`.

Wait, `WpfPlotService` currently takes `ScottPlot.WPF.WpfPlot` — after Task 4, it will take `PlotCanvas`. So in the code-behind, `var plotService = new WpfPlotService(ViewPl);` where `ViewPl` is now a `PlotCanvas`, not a `WpfPlot`. The type changes automatically because we changed the XAML element. No C# code change needed in ContourPlot.xaml.cs.

---

### Task 6: Replace `<sp:WpfPlot>` in RCFiberRegionPage and RCFiberRegionView

**Files:**
- Modify: `OpenCS/Views/RCFiberRegionPage.xaml`
- Modify: `OpenCS/Views/RCFiberRegionPage.xaml.cs`
- Modify: `OpenCS/Views/RCFiberRegionView.xaml`
- Modify: `OpenCS/Views/RCFiberRegionView.xaml.cs`

- [ ] **Step 1: Update RCFiberRegionPage.xaml**

Replace line 8 (`xmlns:sp="clr-namespace:ScottPlot.WPF;assembly=ScottPlot.WPF"`) with:
```xml
xmlns:local="clr-namespace:OpenCS.Views"
```

Replace line 299 (`<sp:WpfPlot x:Name="plot" Margin="5" />`) with:
```xml
<local:PlotCanvas x:Name="plot" Margin="5" />
```

- [ ] **Step 2: Update RCFiberRegionPage.xaml.cs**

Remove `using OpenCS.Services;` if no longer needed. The `WpfPlotService` is still in `OpenCS.Services` namespace, so no import change needed. The constructor `new WpfPlotService(plot)` still works because `plot` is now `PlotCanvas`.

No code changes needed in .xaml.cs.

- [ ] **Step 3: Update RCFiberRegionView.xaml**

Same changes as Step 1:
Replace line 8 namespace with `xmlns:local="clr-namespace:OpenCS.Views"`.
Replace line 76 `<sp:WpfPlot>` with `<local:PlotCanvas>`.

- [ ] **Step 4: Update RCFiberRegionView.xaml.cs**

No code changes needed — same pattern as above.

---

### Task 7: Replace `<sp:WpfPlot>` in DiagramPage and refactor to IPlotService

**Files:**
- Modify: `OpenCS/Views/DiagramPage.xaml`
- Modify: `OpenCS/Views/DiagramPage.xaml.cs`

- [ ] **Step 1: Update DiagramPage.xaml**

Replace `<sp:WpfPlot>` with `<local:PlotCanvas>`. Add title/label TextBlocks outside the plot area (they're already in XAML at lines 19-26, so we just need to wire them differently).

Replace line 6 namespace:
```xml
xmlns:local="clr-namespace:OpenCS.Views"
```

Replace line 28:
```xml
<local:PlotCanvas x:Name="plot" Grid.Row="2" Margin="5"/>
```

The titleText and infoText TextBlocks already exist in the XAML at lines 20 and 25.

- [ ] **Step 2: Rewrite DiagramPage.xaml.cs**

Replace all direct ScottPlot calls with IPlotService calls:

```csharp
using CScore;
using OpenCS.Services;

using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace OpenCS.Views
{
   public partial class DiagramPage : UserControl
   {
      readonly AppViewModel mvm;
      readonly Diagramm diagram;
      IPlotService? _plotService;

      public DiagramPage(Diagramm diagram, AppViewModel mvm)
      {
         InitializeComponent();
         this.mvm = mvm;
         this.diagram = diagram;

         titleText.Text = diagram.Tag;
         infoText.Text = $"Материал: {(diagram.MaterialId > 0 ? $"id={diagram.MaterialId}" : "—")}  |  Тип расчёта: {diagram.CalcType}";

         _plotService = new WpfPlotService(plot);
         DrawPlot();
      }

      void DrawPlot()
      {
         if (_plotService == null) return;
         _plotService.Clear();
         _plotService.SetTitle($"Диаграмма работы материала — {diagram.Tag}");
         _plotService.SetXLabel("ε");
         _plotService.SetYLabel("σ, МПа");

         PlotBranch(diagram.Ic, "Сжатие", "#0000C8");
         PlotBranch(diagram.It, "Растяжение", "#C80000");

         _plotService.ShowLegend();
         _plotService.AutoScale();
         _plotService.Refresh();
      }

      void PlotBranch(CSmath.ISpline? spline, string label, string colorHex)
      {
         if (spline?.X == null || spline.X.Length < 2) return;

         bool hasInvalidNodes = spline.X.Any(double.IsNaN) || spline.Y.Any(double.IsNaN);
         if (hasInvalidNodes)
         {
            _plotService?.AddMarkers(spline.X, spline.Y, 5, colorHex, label + " (узлы)");
            return;
         }

         int sampleCount = Math.Max(spline.X.Length * 20, 200);
         double xMin = spline.X.Min();
         double xMax = spline.X.Max();
         double step = (xMax - xMin) / (sampleCount - 1);

         var xs = new double[sampleCount];
         var ys = new double[sampleCount];
         int realCount = 0;
         for (int i = 0; i < sampleCount; i++)
         {
            double xi = xMin + step * i;
            double yi = spline.Interpolate(xi);
            if (double.IsFinite(yi))
            {
               xs[realCount] = xi;
               ys[realCount] = yi;
               realCount++;
            }
         }

         if (realCount < 2)
         {
            _plotService?.AddMarkers(spline.X, spline.Y, 4, colorHex, label + " (узлы)");
            return;
         }

         var trimXs = new double[realCount];
         var trimYs = new double[realCount];
         Array.Copy(xs, trimXs, realCount);
         Array.Copy(ys, trimYs, realCount);

         _plotService?.AddScatter(trimXs, trimYs, 2, colorHex, label);
         _plotService?.AddMarkers(spline.X, spline.Y, 4, colorHex, label + " (узлы)");
      }

      void Delete_Click(object sender, RoutedEventArgs e)
      {
         var res = MessageBox.Show("Удалить диаграмму?", "Подтверждение",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
         if (res != MessageBoxResult.Yes) return;

         mvm.db.DeleteDiagram(diagram);
         mvm.db.Diagrams.Remove(diagram);
         mvm.CurrentPage = null;
         mvm.LogService.Info($"Диаграмма '{diagram.Tag}' удалена");
      }

      void Close_Click(object sender, RoutedEventArgs e)
      {
         mvm.CurrentPage = null;
      }
   }
}
```

---

### Task 8: Replace `<sp:WpfPlot>` in DxfPlot and refactor to IPlotService

**Files:**
- Modify: `OpenCS/Views/DxfPlot.xaml`
- Modify: `OpenCS/Views/DxfPlot.xaml.cs`

- [ ] **Step 1: Update DxfPlot.xaml**

Replace line 7 namespace with `xmlns:local="clr-namespace:OpenCS.Views"`.
Replace line 11 `<sp:WpfPlot>` with `<local:PlotCanvas>`.

- [ ] **Step 2: Rewrite DxfPlot.xaml.cs**

```csharp
using CScore;
using OpenCS.Services;

using netDxf;
using netDxf.Entities;

using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;

namespace OpenCS.Views
{
   public partial class DxfPlot : UserControl
   {
      private readonly List<string> hcolors = ["#318CE7", "#9457EB", "#CC397B", "#F07427", "#F4CA16", "#20B2AA"];
      public DxfDocument? Dxf { get; set; }

      public DxfPlot(DxfDocument dxfDocument)
      {
         InitializeComponent();
         if (dxfDocument == null) return;

         Dxf = dxfDocument;
         var plotService = new WpfPlotService(ViewPl);

         List<Polyline2D> plines = Dxf.Entities.Polylines2D.ToList();
         List<Circle> circles = Dxf.Entities.Circles.ToList();
         List<string> layers = [];

         foreach (var item in plines)
         {
            if (!layers.Contains(item.Layer.Name))
               layers.Add(item.Layer.Name);
         }
         foreach (var item in circles)
         {
            if (!layers.Contains(item.Layer.Name))
               layers.Add(item.Layer.Name);
         }

         int j = 0;
         foreach (var item in layers)
         {
            var plines_lay = from p in plines where p.Layer.Name == item select p;
            foreach (var p in plines_lay)
            {
               var pts = PolylineToPoints(p);
               var xs = pts.Select(pt => pt.X).ToArray();
               var ys = pts.Select(pt => pt.Y).ToArray();
               plotService.AddScatter(xs, ys, lineWidth: 2, color: hcolors[j % hcolors.Count]);
            }
            j++;
         }
         j = 0;
         foreach (var item in layers)
         {
            var circles_lay = from c in circles where c.Layer.Name == item select c;
            foreach (var c in circles_lay)
            {
               plotService.AddCircle(c.Center.X, c.Center.Y, c.Radius,
                  lineColor: hcolors[j % hcolors.Count], lineWidth: 4);
            }
            j++;
         }

         plotService.EnableSquareAxes();
         plotService.AutoScale();
         plotService.Refresh();
      }

      private static List<System.Windows.Point> PolylineToPoints(Polyline2D pline)
      {
         List<System.Windows.Point> points = new(pline.Vertexes.Count + 1);
         foreach (var item in pline.Vertexes)
         {
            points.Add(new System.Windows.Point(item.Position.X, item.Position.Y));
         }
         var first = pline.Vertexes.First().Position;
         var last = pline.Vertexes.Last().Position;
         if (pline.IsClosed && !first.Equals(last, 1e-4))
            points.Add(new System.Windows.Point(first.X, first.Y));

         return points;
      }
   }
}
```

---

### Task 9: Replace `<sp:WpfPlot>` in RegionPlot

**Files:**
- Modify: `OpenCS/Views/RegionPlot.xaml`

- [ ] **Step 1: Update RegionPlot.xaml**

Replace line 7 namespace with `xmlns:local="clr-namespace:OpenCS.Views"`.
Replace line 12 `<sp:WpfPlot>` with `<local:PlotCanvas>`.

No changes needed to RegionPlot.xaml.cs (it doesn't use ScottPlot at all).

---

### Task 10: Remove ScottPlot.WPF NuGet package

**Files:**
- Modify: `OpenCS/OpenCS.csproj`

- [ ] **Step 1: Remove package reference**

Delete line 377:
```xml
<PackageReference Include="ScottPlot.WPF" Version="5.0.53" />
```

---

### Task 11: Build and verify

- [ ] **Step 1: Clean build**

```bash
dotnet clean OpenCS.sln
dotnet build OpenCS.sln
```

Expected: 0 errors, 0 warnings.

- [ ] **Step 2: Verify no ScottPlot references remain**

```bash
rg "ScottPlot" --include="*.cs" --include="*.xaml" --include="*.csproj" -l
```

Expected: only `obj/` generated files (these are stale build artifacts, cleaned by `dotnet clean`). If any source files remain, fix them.

- [ ] **Step 3: Commit**

```bash
git add .
git commit -m "refactor: remove ScottPlot.WPF, replace with pure WPF DrawingContext rendering"
```
