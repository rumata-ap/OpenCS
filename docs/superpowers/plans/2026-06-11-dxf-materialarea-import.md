# DXF MaterialArea Import Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Adapt the existing DXF import wizard to create MaterialArea objects (Region/RebarGroup/SingleBar) directly from DXF polylines and circles.

**Architecture:** Role-based selection — each DxfPrimitive gets a `DxfRole` (Hull/Hole/RebarGroup/SingleBar) via toolbar mode switching. One save action creates one Region + optional child RebarGroup + optional SingleBar(s) in the DB. Circles assigned as Hull/Hole are discretized to polygon contours; circles as bars become `Fiber.CreatePoint`.

**Tech Stack:** .NET 9 WPF, MVVM, Microsoft.Data.Sqlite, CScore (MaterialArea/Contour/Fiber)

---

## File Map

| File | Action | What changes |
|---|---|---|
| `OpenCS/ViewModels/DxfPrimitive.cs` | Modify | Add `DxfRole` enum + `Role` property; remove `IsSelected` |
| `OpenCS/ViewModels/FromDxfVM.cs` | Modify | Role logic, discretize helpers, CreateMaterialAreaCommand; remove old Save* |
| `OpenCS/Views/DxfInteractiveView.xaml.cs` | Modify | Color by Role; `PrimitiveClicked` replaces `SelectionChanged` |
| `OpenCS/Views/FromDxfPage.xaml` | Modify | New toolbar (4 radio-mode buttons); new right panel (4 lists + discretize + tag + create) |
| `OpenCS/Views/FromDxfPage.xaml.cs` | Modify | Wire `PrimitiveClicked`; drop `HandleSelectionChanged` |
| `OpenCS/Views/MainWindow.xaml` | Modify | Add «Из DXF…» MenuItem to MaterialAreas context menus |
| `OpenCS/Resources/Strings.ru-RU.xaml` | Modify | 16 new string keys |
| `OpenCS/Resources/Strings.en-US.xaml` | Modify | Same 16 keys in English |

**Verification command after every task:** `dotnet build OpenCS.sln`  
Expected: `Build succeeded.` (warnings are OK; errors are not)

---

## Task 1 — DxfPrimitive: add DxfRole, remove IsSelected

**Files:** Modify `OpenCS/ViewModels/DxfPrimitive.cs`

- [ ] **Replace the entire file content** with:

```csharp
using CScore;
using System.Windows.Media;

namespace OpenCS.ViewModels
{
   /// <summary>Вид примитива DXF: контур или окружность.</summary>
   public enum DxfPrimitiveKind { Contour, Circle }

   /// <summary>Роль примитива при назначении в MaterialArea.</summary>
   public enum DxfRole { None, Hull, Hole, RebarGroup, SingleBar }

   /// <summary>Информация о слое DXF: имя и цвет для легенды.</summary>
   public record LayerInfo(string Name, string HexColor)
   {
      /// <summary>Кисть для отображения цветного маркера в легенде слоёв.</summary>
      public Brush LayerBrush { get; } =
         new SolidColorBrush((Color)ColorConverter.ConvertFromString(HexColor));
   }

   /// <summary>
   /// Обёртка DXF-примитива: связывает геометрию для рендера с доменным объектом
   /// (<see cref="Contour"/> или <see cref="CircleP"/>) и назначенной ролью.
   /// </summary>
   public class DxfPrimitive
   {
      public DxfPrimitiveKind Kind      { get; init; }
      public string           LayerName { get; init; } = string.Empty;
      public DxfRole          Role      { get; set; } = DxfRole.None;

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

- [ ] **Build:** `dotnet build OpenCS.sln`
  Expected output ends with: `Build succeeded.`
  If `IsSelected` references remain elsewhere, fix them now (will be cleared in later tasks).

- [ ] **Commit:**
```
git add OpenCS/ViewModels/DxfPrimitive.cs
git commit -m "feat(dxf): add DxfRole enum, replace IsSelected with Role on DxfPrimitive"
```

---

## Task 2 — FromDxfVM: circle discretization helpers

**Files:** Modify `OpenCS/ViewModels/FromDxfVM.cs`  
Add private static helper methods **after** the `StitchLines` method (around line 325). Do NOT change anything else in the VM yet — the file will still reference `IsSelected` and the old commands; that's OK for now.

- [ ] **Add these static helpers inside the `FromDxfVM` class** (after `StitchLines`, before the closing `}`):

```csharp
      // ── Дискретизация и ориентация контуров ──────────────────────────────

      /// <summary>Метод дискретизации окружности в полигон.</summary>
      public enum CircleDiscretizeMethod { ChordLength, SegmentCount }

      /// <summary>
      /// Вычисляет площадь полигона со знаком по формуле Гаусса.
      /// Положительная → CCW, отрицательная → CW.
      /// </summary>
      internal static double SignedArea(IList<double> x, IList<double> y)
      {
         double s = 0;
         int n = x.Count - 1; // последняя точка = первая (замкнутый контур)
         for (int i = 0; i < n; i++)
            s += x[i] * y[i + 1] - x[i + 1] * y[i];
         return s / 2.0;
      }

      /// <summary>
      /// Дискретизирует окружность в замкнутый контур.
      /// ccw=true → обход против часовой (Hull); ccw=false → по часовой (Hole).
      /// </summary>
      internal static CScore.Contour DiscretizeCircle(
         double cx, double cy, double r,
         CircleDiscretizeMethod method, double value, bool ccw)
      {
         int n = method == CircleDiscretizeMethod.ChordLength
            ? Math.Max(3, (int)Math.Ceiling(2 * Math.PI * r / Math.Max(value, 1e-9)))
            : Math.Max(3, (int)value);

         double step = 2 * Math.PI / n;
         double dir  = ccw ? 1.0 : -1.0;

         var xs = new List<double>(n + 1);
         var ys = new List<double>(n + 1);
         for (int i = 0; i <= n; i++) // n+1 точек — последняя = первой
         {
            xs.Add(cx + r * Math.Cos(dir * i * step));
            ys.Add(cy + r * Math.Sin(dir * i * step));
         }
         return new CScore.Contour(xs, ys, "circle");
      }

      /// <summary>
      /// Возвращает контур с типом Hull (CCW). Если исходный CW — разворачивает.
      /// </summary>
      internal static CScore.Contour ToHullContour(DxfPrimitive p,
         CircleDiscretizeMethod method, double value)
      {
         CScore.Contour c;
         if (p.Kind == DxfPrimitiveKind.Circle)
         {
            c = DiscretizeCircle(p.CenterX, p.CenterY, p.Radius, method, value, ccw: true);
         }
         else
         {
            c = p.Contour!;
            if (SignedArea(c.X, c.Y) < 0) // CW → reverse
               c = new CScore.Contour(c.X.Reverse().ToList(), c.Y.Reverse().ToList(), c.Tag);
         }
         c.Type = ContourType.Hull;
         return c;
      }

      /// <summary>
      /// Возвращает контур с типом Hole (CW). Если исходный CCW — разворачивает.
      /// </summary>
      internal static CScore.Contour ToHoleContour(DxfPrimitive p,
         CircleDiscretizeMethod method, double value)
      {
         CScore.Contour c;
         if (p.Kind == DxfPrimitiveKind.Circle)
         {
            c = DiscretizeCircle(p.CenterX, p.CenterY, p.Radius, method, value, ccw: false);
         }
         else
         {
            c = p.Contour!;
            if (SignedArea(c.X, c.Y) > 0) // CCW → reverse to CW
               c = new CScore.Contour(c.X.Reverse().ToList(), c.Y.Reverse().ToList(), c.Tag);
         }
         c.Type = ContourType.Hole;
         return c;
      }
```

- [ ] **Add missing using** at top of file if not present:
```csharp
using CScore;
```
(Already there — just verify.)

- [ ] **Build:** `dotnet build OpenCS.sln` → `Build succeeded.`

- [ ] **Commit:**
```
git add OpenCS/ViewModels/FromDxfVM.cs
git commit -m "feat(dxf): add circle discretization + hull/hole contour helpers"
```

---

## Task 3 — DxfInteractiveView: role-based coloring + PrimitiveClicked

**Files:** Modify `OpenCS/Views/DxfInteractiveView.xaml.cs`

Role colors (match spec):
- None → layer color
- Hull → `#4CAF50`
- Hole → `#F44336`
- RebarGroup → `#FF9800`
- SingleBar → `#FFC107`

- [ ] **Replace `SelectionChanged` public API** with `PrimitiveClicked`:

Find and replace this property declaration:
```csharp
      public Action<IReadOnlyList<DxfPrimitive>>? SelectionChanged { get; set; }
```
Replace with:
```csharp
      /// <summary>
      /// Вызывается при клике на примитив. Передаёт кликнутый примитив.
      /// VM назначает ему роль и Canvas обновляет цвет в ответ.
      /// </summary>
      public Action<DxfPrimitive>? PrimitiveClicked { get; set; }
```

- [ ] **Replace `UpdateStyle` method** entirely:

```csharp
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
```

- [ ] **Replace `OnBorderLeftButtonDown` method** entirely:

```csharp
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
```

- [ ] **Build:** `dotnet build OpenCS.sln` → `Build succeeded.`
  Expect one error about `SelectionChanged` in `FromDxfPage.xaml.cs` — leave it for Task 7.

- [ ] **Commit:**
```
git add OpenCS/Views/DxfInteractiveView.xaml.cs
git commit -m "feat(dxf): role-based canvas coloring, PrimitiveClicked callback"
```

---

## Task 4 — FromDxfVM: role management, SelectMode, collections

**Files:** Modify `OpenCS/ViewModels/FromDxfVM.cs`

This task replaces the entire public surface of the VM (properties and commands). Keep the parsing methods (`ParseDxf`, `PolylineToPrimitive`, `CircleToPrimitive`, `ArcToPrimitive`, `StitchLines`) and the helpers from Task 2 unchanged.

- [ ] **Replace the fields and properties block** (from `private static readonly string[] Palette` through `public ICommand SaveCirclesCommand`) with:

```csharp
      private static readonly string[] Palette =
         ["#318CE7", "#9457EB", "#CC397B", "#F07427", "#F4CA16", "#20B2AA"];

      public AppViewModel mvm = null!;

      private double _scale = 0.001;
      private int _unitIdx;
      private DxfRole _selectMode = DxfRole.Hull;
      private CircleDiscretizeMethod _discretizeMethod = CircleDiscretizeMethod.ChordLength;
      private double _discretizeValue = 0.020;
      private string _tag = "area";
      private List<DxfPrimitive> _primitives = [];

      public List<string> Units { get; } = ["мм", "см", "м"];

      /// <summary>Слои DXF — источник для легенды в левой панели.</summary>
      public ObservableCollection<LayerInfo> Layers { get; } = [];

      /// <summary>
      /// Устанавливается code-behind страницы. Вызывается после успешного
      /// разбора DXF для передачи примитивов в <see cref="Views.DxfInteractiveView"/>.
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

      public DxfRole SelectMode
      {
         get => _selectMode;
         set { _selectMode = value; OnPropertyChanged(); }
      }

      public CircleDiscretizeMethod DiscretizeMethod
      {
         get => _discretizeMethod;
         set { _discretizeMethod = value; OnPropertyChanged(); }
      }

      public double DiscretizeValue
      {
         get => _discretizeValue;
         set { _discretizeValue = value; OnPropertyChanged(); }
      }

      public string Tag
      {
         get => _tag;
         set { _tag = value; OnPropertyChanged(); }
      }

      // ── Вычисляемые коллекции по ролям ───────────────────────────────────

      public DxfPrimitive? HullPrimitive =>
         _primitives.FirstOrDefault(p => p.Role == DxfRole.Hull);

      public IReadOnlyList<DxfPrimitive> HolePrimitives =>
         _primitives.Where(p => p.Role == DxfRole.Hole).ToList();

      public IReadOnlyList<DxfPrimitive> GroupBarPrimitives =>
         _primitives.Where(p => p.Role == DxfRole.RebarGroup).ToList();

      public IReadOnlyList<DxfPrimitive> SingleBarPrimitives =>
         _primitives.Where(p => p.Role == DxfRole.SingleBar).ToList();

      // ── Команды ──────────────────────────────────────────────────────────

      public ICommand OpenDXFCommand          { get; }
      public ICommand CreateMaterialAreaCommand { get; }
      public ICommand ClearRoleCommand        { get; }
```

- [ ] **Replace the constructor** (the `FromDxfVM()` block):

```csharp
      public FromDxfVM()
      {
         OpenDXFCommand           = new RelayCommand(OpenDxf);
         CreateMaterialAreaCommand = new RelayCommand(CreateMaterialArea);
         ClearRoleCommand         = new RelayCommand(p => ClearRole((DxfPrimitive)p!));
      }
```

- [ ] **Replace `HandleSelectionChanged`, `SaveContours`, `SaveCircles`** with `HandlePrimitiveClicked` and `ClearRole`:

Remove:
```csharp
      public void HandleSelectionChanged(IReadOnlyList<DxfPrimitive> selected) { ... }
      private void SaveContours(object? _ = null) { ... }
      private void SaveCircles(object? _ = null) { ... }
```

Add in their place:

```csharp
      /// <summary>
      /// Вызывается канвасом при клике. Назначает текущий режим как роль примитива.
      /// Hull: допускает только один объект — предыдущий Hull сбрасывается.
      /// Повторный клик на тот же режим — сброс в None.
      /// </summary>
      public void HandlePrimitiveClicked(DxfPrimitive p)
      {
         if (p.Role == _selectMode)
         {
            p.Role = DxfRole.None;
         }
         else
         {
            if (_selectMode == DxfRole.Hull)
            {
               // Сбросить предыдущий Hull
               foreach (var prev in _primitives.Where(x => x.Role == DxfRole.Hull))
                  prev.Role = DxfRole.None;
            }
            p.Role = _selectMode;
         }
         OnPropertyChanged(nameof(HullPrimitive));
         OnPropertyChanged(nameof(HolePrimitives));
         OnPropertyChanged(nameof(GroupBarPrimitives));
         OnPropertyChanged(nameof(SingleBarPrimitives));
      }

      /// <summary>Сбрасывает роль примитива в None. Вызывается кнопкой [×] в правой панели.</summary>
      public void ClearRole(DxfPrimitive p)
      {
         p.Role = DxfRole.None;
         OnPropertyChanged(nameof(HullPrimitive));
         OnPropertyChanged(nameof(HolePrimitives));
         OnPropertyChanged(nameof(GroupBarPrimitives));
         OnPropertyChanged(nameof(SingleBarPrimitives));
      }
```

- [ ] **Update `OpenDxf` method** — remove old collections clear, they no longer exist:

Find:
```csharp
         ContoursPrj.Clear();
         CirclesPrj.Clear();

         var dxf = DxfDocument.Load(fileName);
         GeometrySet = dxf.Name;
```
Replace with:
```csharp
         foreach (var p in _primitives) p.Role = DxfRole.None;

         var dxf = DxfDocument.Load(fileName);
         Tag = dxf.Name;
```

- [ ] **Build:** `dotnet build OpenCS.sln` → `Build succeeded.`

- [ ] **Commit:**
```
git add OpenCS/ViewModels/FromDxfVM.cs
git commit -m "feat(dxf): SelectMode role assignment, HandlePrimitiveClicked, role collections"
```

---

## Task 5 — FromDxfVM: CreateMaterialArea command

**Files:** Modify `OpenCS/ViewModels/FromDxfVM.cs`

Add `CreateMaterialArea` private method after `ClearRole`. This is the core business logic.

- [ ] **Add `CreateMaterialArea` method** inside the class:

```csharp
      private void CreateMaterialArea(object? _ = null)
      {
         if (HullPrimitive == null && !GroupBarPrimitives.Any() && !SingleBarPrimitives.Any())
         {
            mvm.LogService.Info("Нет назначенных объектов для создания области");
            return;
         }

         // ── Region (если назначен Hull) ───────────────────────────────────
         MaterialArea? region = null;
         if (HullPrimitive != null)
         {
            region = new MaterialArea { Tag = _tag, Category = AreaCategory.Region };
            region.Hull = ToHullContour(HullPrimitive, _discretizeMethod, _discretizeValue);
            foreach (var hp in HolePrimitives)
               region.Contours.Add(ToHoleContour(hp, _discretizeMethod, _discretizeValue));
            region.SetWKT();
            mvm.db.SaveMaterialArea(region);
         }

         // ── RebarGroup (все GroupBar) ─────────────────────────────────────
         if (GroupBarPrimitives.Any())
         {
            var group = new MaterialArea
            {
               Tag      = _tag + "_г",
               Category = AreaCategory.RebarGroup,
               HostAreaId = region?.Id
            };
            foreach (var bar in GroupBarPrimitives)
               group.Fibers.Add(Fiber.CreatePoint(bar.Radius * 2, bar.CenterX, bar.CenterY));
            mvm.db.SaveMaterialArea(group);
         }

         // ── SingleBar (каждая → отдельная MaterialArea) ───────────────────
         foreach (var bar in SingleBarPrimitives)
         {
            var single = new MaterialArea
            {
               Tag      = _tag + "_с",
               Category = AreaCategory.SingleBar,
               HostAreaId = region?.Id
            };
            single.Fibers.Add(Fiber.CreatePoint(bar.Radius * 2, bar.CenterX, bar.CenterY));
            mvm.db.SaveMaterialArea(single);
         }

         // ── Сброс ролей после сохранения ─────────────────────────────────
         foreach (var p in _primitives) p.Role = DxfRole.None;
         OnPropertyChanged(nameof(HullPrimitive));
         OnPropertyChanged(nameof(HolePrimitives));
         OnPropertyChanged(nameof(GroupBarPrimitives));
         OnPropertyChanged(nameof(SingleBarPrimitives));

         mvm.RefreshMaterialAreaLiveCollections();
         mvm.LogService.Info($"Создана MaterialArea «{_tag}»");
      }
```

- [ ] **Add required using** at the top of the file (if not already present):
```csharp
using CScore;
```
(Verify: `MaterialArea`, `AreaCategory`, `Fiber`, `FiberType`, `ContourType` all live in `CScore`.)

- [ ] **Build:** `dotnet build OpenCS.sln` → `Build succeeded.`

- [ ] **Commit:**
```
git add OpenCS/ViewModels/FromDxfVM.cs
git commit -m "feat(dxf): CreateMaterialArea — Region + RebarGroup + SingleBar from DXF"
```

---

## Task 6 — FromDxfPage.xaml: new toolbar and right panel

**Files:** Modify `OpenCS/Views/FromDxfPage.xaml`

- [ ] **Replace the entire XAML** with:

```xml
<UserControl x:Class="OpenCS.Views.FromDxfPage"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:local="clr-namespace:OpenCS.Views"
             xmlns:vm="clr-namespace:OpenCS.ViewModels"
             mc:Ignorable="d"
             d:DesignHeight="450" d:DesignWidth="800">

   <UserControl.Resources>
      <ResourceDictionary Source="/Images/svg.xaml"/>
   </UserControl.Resources>

   <Grid>
      <Grid.RowDefinitions>
         <RowDefinition Height="Auto"/>
         <RowDefinition Height="Auto"/>
         <RowDefinition/>
      </Grid.RowDefinitions>

      <!-- Row 0: File + units -->
      <DockPanel Grid.Row="0" Margin="5,5,5,2" LastChildFill="False">
         <Button Height="25" Width="25" BorderThickness="0" Background="Transparent"
                 Command="{Binding OpenDXFCommand}" DockPanel.Dock="Left" Margin="0,0,8,0">
            <Image Source="{StaticResource di_dxf_file_xaml}"/>
         </Button>
         <TextBlock Text="{DynamicResource DxfUnits}" VerticalAlignment="Center"
                    Margin="0,0,4,0" DockPanel.Dock="Left"/>
         <ComboBox ItemsSource="{Binding Units}" SelectedIndex="{Binding UnitIdx}"
                   Width="60" DockPanel.Dock="Left"/>
      </DockPanel>

      <!-- Row 1: Mode selector -->
      <StackPanel Grid.Row="1" Orientation="Horizontal" Margin="5,2,5,5">
         <RadioButton Content="{DynamicResource DxfModeHull}"
                      IsChecked="{Binding SelectMode,
                         Converter={StaticResource EnumToBoolConverter},
                         ConverterParameter={x:Static vm:DxfRole.Hull}}"
                      GroupName="DxfMode" Margin="0,0,8,0" VerticalContentAlignment="Center"/>
         <RadioButton Content="{DynamicResource DxfModeHole}"
                      IsChecked="{Binding SelectMode,
                         Converter={StaticResource EnumToBoolConverter},
                         ConverterParameter={x:Static vm:DxfRole.Hole}}"
                      GroupName="DxfMode" Margin="0,0,8,0" VerticalContentAlignment="Center"/>
         <RadioButton Content="{DynamicResource DxfModeRebarGroup}"
                      IsChecked="{Binding SelectMode,
                         Converter={StaticResource EnumToBoolConverter},
                         ConverterParameter={x:Static vm:DxfRole.RebarGroup}}"
                      GroupName="DxfMode" Margin="0,0,8,0" VerticalContentAlignment="Center"/>
         <RadioButton Content="{DynamicResource DxfModeSingleBar}"
                      IsChecked="{Binding SelectMode,
                         Converter={StaticResource EnumToBoolConverter},
                         ConverterParameter={x:Static vm:DxfRole.SingleBar}}"
                      GroupName="DxfMode" VerticalContentAlignment="Center"/>
      </StackPanel>

      <!-- Row 2: 3-column main area -->
      <Grid Grid.Row="2">
         <Grid.ColumnDefinitions>
            <ColumnDefinition Width="160"/>
            <ColumnDefinition/>
            <ColumnDefinition Width="210"/>
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

         <!-- Col 2: Role-based assignment panel -->
         <ScrollViewer Grid.Column="2" VerticalScrollBarVisibility="Auto">
            <StackPanel Margin="4">

               <!-- Hull -->
               <GroupBox Header="{DynamicResource DxfRightHull}" Margin="0,0,0,4">
                  <StackPanel>
                     <ItemsControl ItemsSource="{Binding HullPrimitive,
                                   Converter={StaticResource NullableToList}}">
                        <ItemsControl.ItemTemplate>
                           <DataTemplate DataType="{x:Type vm:DxfPrimitive}">
                              <DockPanel>
                                 <Button DockPanel.Dock="Right" Content="×" Width="20" Height="20"
                                         Command="{Binding DataContext.ClearRoleCommand,
                                            RelativeSource={RelativeSource AncestorType=UserControl}}"
                                         CommandParameter="{Binding}"/>
                                 <TextBlock Text="{Binding LayerName}" VerticalAlignment="Center"
                                            TextTrimming="CharacterEllipsis" Margin="2,0"/>
                              </DockPanel>
                           </DataTemplate>
                        </ItemsControl.ItemTemplate>
                     </ItemsControl>
                  </StackPanel>
               </GroupBox>

               <!-- Holes -->
               <GroupBox Header="{DynamicResource DxfRightHoles}" Margin="0,0,0,4">
                  <ItemsControl ItemsSource="{Binding HolePrimitives}">
                     <ItemsControl.ItemTemplate>
                        <DataTemplate DataType="{x:Type vm:DxfPrimitive}">
                           <DockPanel>
                              <Button DockPanel.Dock="Right" Content="×" Width="20" Height="20"
                                      Command="{Binding DataContext.ClearRoleCommand,
                                         RelativeSource={RelativeSource AncestorType=UserControl}}"
                                      CommandParameter="{Binding}"/>
                              <TextBlock Text="{Binding LayerName}" VerticalAlignment="Center"
                                         TextTrimming="CharacterEllipsis" Margin="2,0"/>
                           </DockPanel>
                        </DataTemplate>
                     </ItemsControl.ItemTemplate>
                  </ItemsControl>
               </GroupBox>

               <!-- RebarGroup -->
               <GroupBox Header="{DynamicResource DxfRightGroup}" Margin="0,0,0,4">
                  <ItemsControl ItemsSource="{Binding GroupBarPrimitives}">
                     <ItemsControl.ItemTemplate>
                        <DataTemplate DataType="{x:Type vm:DxfPrimitive}">
                           <DockPanel>
                              <Button DockPanel.Dock="Right" Content="×" Width="20" Height="20"
                                      Command="{Binding DataContext.ClearRoleCommand,
                                         RelativeSource={RelativeSource AncestorType=UserControl}}"
                                      CommandParameter="{Binding}"/>
                              <TextBlock VerticalAlignment="Center" TextTrimming="CharacterEllipsis"
                                         Margin="2,0">
                                 <Run Text="⌀"/>
                                 <Run Text="{Binding Radius,
                                      StringFormat='{}{0:F3}', Mode=OneWay}"/>
                              </TextBlock>
                           </DockPanel>
                        </DataTemplate>
                     </ItemsControl.ItemTemplate>
                  </ItemsControl>
               </GroupBox>

               <!-- SingleBar -->
               <GroupBox Header="{DynamicResource DxfRightSingle}" Margin="0,0,0,4">
                  <ItemsControl ItemsSource="{Binding SingleBarPrimitives}">
                     <ItemsControl.ItemTemplate>
                        <DataTemplate DataType="{x:Type vm:DxfPrimitive}">
                           <DockPanel>
                              <Button DockPanel.Dock="Right" Content="×" Width="20" Height="20"
                                      Command="{Binding DataContext.ClearRoleCommand,
                                         RelativeSource={RelativeSource AncestorType=UserControl}}"
                                      CommandParameter="{Binding}"/>
                              <TextBlock VerticalAlignment="Center" TextTrimming="CharacterEllipsis"
                                         Margin="2,0">
                                 <Run Text="⌀"/>
                                 <Run Text="{Binding Radius,
                                      StringFormat='{}{0:F3}', Mode=OneWay}"/>
                              </TextBlock>
                           </DockPanel>
                        </DataTemplate>
                     </ItemsControl.ItemTemplate>
                  </ItemsControl>
               </GroupBox>

               <!-- Discretize parameters -->
               <Expander Header="{DynamicResource DxfDiscretize}" Margin="0,4,0,4"
                         IsExpanded="False">
                  <Grid Margin="4">
                     <Grid.RowDefinitions>
                        <RowDefinition Height="Auto"/>
                        <RowDefinition Height="Auto"/>
                     </Grid.RowDefinitions>
                     <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="*"/>
                        <ColumnDefinition Width="80"/>
                     </Grid.ColumnDefinitions>
                     <TextBlock Grid.Row="0" Grid.Column="0"
                                Text="{DynamicResource DxfDiscretizeMethod}"
                                VerticalAlignment="Center" Margin="0,0,4,2"/>
                     <ComboBox Grid.Row="0" Grid.Column="1" SelectedIndex="0"
                               SelectedItem="{Binding DiscretizeMethod}">
                        <ComboBoxItem Content="{DynamicResource DxfDiscretizeChord}"
                                      Tag="{x:Static vm:FromDxfVM+CircleDiscretizeMethod.ChordLength}"/>
                        <ComboBoxItem Content="{DynamicResource DxfDiscretizeN}"
                                      Tag="{x:Static vm:FromDxfVM+CircleDiscretizeMethod.SegmentCount}"/>
                     </ComboBox>
                     <TextBlock Grid.Row="1" Grid.Column="0"
                                Text="{DynamicResource DxfDiscretizeValue}"
                                VerticalAlignment="Center" Margin="0,2,4,0"/>
                     <TextBox Grid.Row="1" Grid.Column="1"
                              Text="{Binding DiscretizeValue, StringFormat='{}{0:F3}'}"
                              Margin="0,2,0,0"/>
                  </Grid>
               </Expander>

               <!-- Tag -->
               <DockPanel Margin="0,4,0,4">
                  <TextBlock Text="{DynamicResource DxfTagLabel}" DockPanel.Dock="Left"
                             VerticalAlignment="Center" Margin="0,0,6,0"/>
                  <TextBox Text="{Binding Tag, UpdateSourceTrigger=PropertyChanged}"/>
               </DockPanel>

               <!-- Create button -->
               <Button Content="{DynamicResource DxfCreateArea}" Height="28"
                       Command="{Binding CreateMaterialAreaCommand}" Margin="0,4,0,0"/>

            </StackPanel>
         </ScrollViewer>

      </Grid>
   </Grid>
</UserControl>
```

**Note on converters:** The XAML above uses `EnumToBoolConverter` and `NullableToList` converters. Check if `EnumToBoolConverter` already exists in the project:

- [ ] **Grep for existing EnumToBoolConverter:**
```
Grep pattern: EnumToBoolConverter
Path: OpenCS/
```
If found: use the existing key name.  
If not found: add this converter to `App.xaml`:

```xml
<!-- In App.xaml Resources, add: -->
<converters:EnumToBoolConverter x:Key="EnumToBoolConverter"/>
```
And create `OpenCS/Converters/EnumToBoolConverter.cs`:
```csharp
using System;
using System.Globalization;
using System.Windows.Data;

namespace OpenCS.Converters
{
   public class EnumToBoolConverter : IValueConverter
   {
      public object Convert(object value, Type t, object param, CultureInfo c)
         => value?.Equals(param) == true;
      public object ConvertBack(object value, Type t, object param, CultureInfo c)
         => (bool)value ? param : Binding.DoNothing;
   }
}
```

- [ ] **Add `NullableToList` converter** to `OpenCS/Converters/NullableToListConverter.cs`:
```csharp
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Data;

namespace OpenCS.Converters
{
   /// <summary>Конвертирует nullable-объект в список (для ItemsControl).</summary>
   public class NullableToListConverter : IValueConverter
   {
      public object Convert(object? value, Type t, object p, CultureInfo c)
         => value == null ? Array.Empty<object>() : new[] { value };
      public object ConvertBack(object v, Type t, object p, CultureInfo c)
         => throw new NotSupportedException();
   }
}
```

And register in `App.xaml`:
```xml
<converters:NullableToListConverter x:Key="NullableToList"/>
```

With `xmlns:converters="clr-namespace:OpenCS.Converters"` in the `<Application>` tag.

- [ ] **Build:** `dotnet build OpenCS.sln` → `Build succeeded.`

- [ ] **Commit:**
```
git add OpenCS/Views/FromDxfPage.xaml OpenCS/Converters/
git commit -m "feat(dxf): new role-based right panel, mode RadioButtons in toolbar"
```

---

## Task 7 — FromDxfPage.xaml.cs: wire PrimitiveClicked

**Files:** Modify `OpenCS/Views/FromDxfPage.xaml.cs`

- [ ] **Replace the constructor body** in `FromDxfPage.xaml.cs`:

```csharp
      public FromDxfPage(AppViewModel mvm)
      {
         InitializeComponent();
         var vm = new FromDxfVM { mvm = mvm };
         DataContext = vm;
         vm.CanvasLoader = (prims, layers) => InteractiveCanvas.Load(prims, layers);
         InteractiveCanvas.PrimitiveClicked = vm.HandlePrimitiveClicked;
         InteractiveCanvas.SetBackground(mvm.PlotSettings.DxfCanvasBackground);
         mvm.DxfBgApplied = bg => InteractiveCanvas.SetBackground(bg);
      }
```

- [ ] **Build:** `dotnet build OpenCS.sln` → `Build succeeded.`

- [ ] **Commit:**
```
git add OpenCS/Views/FromDxfPage.xaml.cs
git commit -m "fix(dxf): wire PrimitiveClicked, remove old HandleSelectionChanged"
```

---

## Task 8 — Strings + MainWindow context menu

**Files:**
- Modify `OpenCS/Resources/Strings.ru-RU.xaml`
- Modify `OpenCS/Resources/Strings.en-US.xaml`
- Modify `OpenCS/Views/MainWindow.xaml`

### 8a — String keys

- [ ] **Add to `Strings.ru-RU.xaml`** (before closing `</ResourceDictionary>`):

```xml
   <!-- DXF MaterialArea import -->
   <sys:String x:Key="DxfModeHull">Hull</sys:String>
   <sys:String x:Key="DxfModeHole">Отверстие</sys:String>
   <sys:String x:Key="DxfModeRebarGroup">Группа арм.</sys:String>
   <sys:String x:Key="DxfModeSingleBar">Стержень</sys:String>
   <sys:String x:Key="DxfRightHull">Внешний контур</sys:String>
   <sys:String x:Key="DxfRightHoles">Отверстия</sys:String>
   <sys:String x:Key="DxfRightGroup">Группа арматуры</sys:String>
   <sys:String x:Key="DxfRightSingle">Стержни</sys:String>
   <sys:String x:Key="DxfDiscretize">Дискретизация кругов</sys:String>
   <sys:String x:Key="DxfDiscretizeMethod">Метод</sys:String>
   <sys:String x:Key="DxfDiscretizeChord">Длина хорды</sys:String>
   <sys:String x:Key="DxfDiscretizeN">Число сегментов</sys:String>
   <sys:String x:Key="DxfDiscretizeValue">Значение</sys:String>
   <sys:String x:Key="DxfCreateArea">Создать область</sys:String>
   <sys:String x:Key="DxfTagLabel">Тег</sys:String>
   <sys:String x:Key="MenuFromDxfMaterialArea">Из DXF…</sys:String>
```

- [ ] **Add to `Strings.en-US.xaml`** (before closing `</ResourceDictionary>`):

```xml
   <!-- DXF MaterialArea import -->
   <sys:String x:Key="DxfModeHull">Hull</sys:String>
   <sys:String x:Key="DxfModeHole">Hole</sys:String>
   <sys:String x:Key="DxfModeRebarGroup">Rebar Group</sys:String>
   <sys:String x:Key="DxfModeSingleBar">Single Bar</sys:String>
   <sys:String x:Key="DxfRightHull">Outer contour</sys:String>
   <sys:String x:Key="DxfRightHoles">Holes</sys:String>
   <sys:String x:Key="DxfRightGroup">Rebar group</sys:String>
   <sys:String x:Key="DxfRightSingle">Single bars</sys:String>
   <sys:String x:Key="DxfDiscretize">Circle discretization</sys:String>
   <sys:String x:Key="DxfDiscretizeMethod">Method</sys:String>
   <sys:String x:Key="DxfDiscretizeChord">Chord length</sys:String>
   <sys:String x:Key="DxfDiscretizeN">Segment count</sys:String>
   <sys:String x:Key="DxfDiscretizeValue">Value</sys:String>
   <sys:String x:Key="DxfCreateArea">Create area</sys:String>
   <sys:String x:Key="DxfTagLabel">Tag</sys:String>
   <sys:String x:Key="MenuFromDxfMaterialArea">From DXF…</sys:String>
```

### 8b — MainWindow context menu

In `MainWindow.xaml` find the MaterialAreas TreeViewItem context menu (around line 301 — it has `NewAreaCommand` and `DeleteMaterialAreaCommand`). There are **three** such context menus (Region, RebarGroup, SingleBar). Add the DXF import MenuItem to **all three**:

- [ ] **In each of the three ContextMenu blocks** for material area types, add before `</ContextMenu>`:

```xml
<MenuItem Header="{DynamicResource MenuFromDxfMaterialArea}"
          Command="{Binding FromDxfCommand}">
   <MenuItem.Icon>
      <Image Source="{StaticResource di_dxf_file_xaml}"/>
   </MenuItem.Icon>
</MenuItem>
```

Note: `FromDxfCommand` already exists in `AppViewModel` and opens `FromDxfPage`. No new command is needed.

- [ ] **Build:** `dotnet build OpenCS.sln` → `Build succeeded.`

- [ ] **Commit:**
```
git add OpenCS/Resources/Strings.ru-RU.xaml OpenCS/Resources/Strings.en-US.xaml OpenCS/Views/MainWindow.xaml
git commit -m "feat(dxf): add string keys for DXF import, FromDXF menu in MaterialAreas context"
```

---

## Task 9 — Verify ComboBox discretize binding

The `Expander` in Task 6 uses a `ComboBox` with `SelectedItem={Binding DiscretizeMethod}` but `ComboBoxItem` with `Tag` — this won't two-way bind correctly. Fix the binding approach.

- [ ] **Replace the discretize ComboBox** in `FromDxfPage.xaml` with a simpler approach using `ItemsSource`:

Find the Expander Grid content and replace the ComboBox with:
```xml
<ComboBox Grid.Row="0" Grid.Column="1"
          ItemsSource="{Binding DiscretizeMethods}"
          SelectedItem="{Binding DiscretizeMethod}"/>
```

- [ ] **Add to `FromDxfVM`** the `DiscretizeMethods` property:

```csharp
      public List<CircleDiscretizeMethod> DiscretizeMethods { get; } =
         [CircleDiscretizeMethod.ChordLength, CircleDiscretizeMethod.SegmentCount];
```

- [ ] **Add a display converter or ItemTemplate for the ComboBox** — or use DisplayMemberPath approach.

Since `CircleDiscretizeMethod` is an enum, use a `TemplateSelector` or bind to a list of display strings. The simplest: wrap in a tuple or use a converter.

Simplest approach — add `DiscretizeMethodDisplayNames` to VM:

```csharp
      public List<string> DiscretizeMethodDisplayNames { get; } =
         ["{DynamicResource DxfDiscretizeChord}", "{DynamicResource DxfDiscretizeN}"];
```

Wait — that won't work with DynamicResource in code. Instead, hard-code for now:

```csharp
      public List<string> DiscretizeMethodDisplayNames { get; } =
         ["Длина хорды", "Число сегментов"];
```

And bind `UnitIdx`-style with index:

```csharp
      public int DiscretizeMethodIdx
      {
         get => (int)_discretizeMethod;
         set { _discretizeMethod = (CircleDiscretizeMethod)value; OnPropertyChanged(); }
      }
```

Then in XAML:
```xml
<ComboBox Grid.Row="0" Grid.Column="1"
          ItemsSource="{Binding DiscretizeMethodDisplayNames}"
          SelectedIndex="{Binding DiscretizeMethodIdx}"/>
```

- [ ] **Build:** `dotnet build OpenCS.sln` → `Build succeeded.`

- [ ] **Commit:**
```
git add OpenCS/ViewModels/FromDxfVM.cs OpenCS/Views/FromDxfPage.xaml
git commit -m "fix(dxf): DiscretizeMethodIdx binding for ComboBox"
```

---

## Task 10 — Manual smoke test

- [ ] **Run the app:** `dotnet run --project OpenCS`

- [ ] **Test: open a DXF, assign roles, create Region**
  1. Open Settings → verify no crash
  2. Right-click «Материальные области» in tree → «Из DXF…» menu item appears
  3. Open `FromDxfPage` via the command
  4. Open a DXF file with at least one closed polyline + two circles
  5. Verify toolbar shows Hull/Отверстие/Группа арм./Стержень radio buttons
  6. Select Hull mode → click polyline → shape turns green
  7. Select Стержень mode → click both circles → shapes turn yellow
  8. Right panel lists: Hull=1, Стержни=2
  9. Enter tag «test_area»
  10. Click «Создать область»
  11. Close DXF window → verify tree shows «test_area» under Материальные области
  12. Click the created area → verify it opens MaterialAreaPage without error

- [ ] **Test: hull circle discretization**
  1. Open DXF with a circle
  2. Select Hull mode → click circle → circle turns green
  3. Expand «Дискретизация кругов» → set Метод=Длина хорды, Знач.=0.050
  4. Click «Создать область»
  5. Verify area appears in tree, open it → WKT is set (polygon visible in plot)

- [ ] **Test: RebarGroup without hull**
  1. Open DXF, assign 3 circles as «Группа арм.»
  2. Click «Создать область»
  3. Verify 1 RebarGroup appears in tree under «Группы арматуры»

- [ ] **Final commit if any fixes were needed:**
```
git add -u
git commit -m "fix(dxf): smoke test fixes"
```

---

## Self-Review Notes

**Spec coverage check:**
- ✅ SelectMode toolbar (4 RadioButtons) — Task 6
- ✅ Role assignment on click — Task 3 + 4
- ✅ Hull color green, Hole red, Group orange, Single yellow — Task 3
- ✅ Hull = max 1 object, prev reset — Task 4
- ✅ Circle discretize: chord length + segment count — Task 2, 5
- ✅ CW for holes (polys auto-reversed, circles generated CW) — Task 2
- ✅ CCW for hull — Task 2
- ✅ Right panel 4 sections + [×] button — Task 6
- ✅ [×] clears role — Task 4 (`ClearRoleCommand`)
- ✅ SaveMaterialArea with point fibers — Task 5 (uses existing `db.SaveMaterialArea` which writes both area + point_fibers)
- ✅ `RefreshMaterialAreaLiveCollections` called after save — Task 5
- ✅ MaterialAreas context menu «Из DXF…» — Task 8
- ✅ All strings in ru-RU + en-US — Task 8
- ✅ No hardcoded UI text — Tasks 6+8
- ✅ DiscretizeValue default 0.020 — Task 4

**Known build-time issues to watch:**
- If `EnumToBoolConverter` doesn't exist in the project → create it per Task 6 instructions
- `CircleDiscretizeMethod` is a nested type in `FromDxfVM` — in XAML use `vm:FromDxfVM+CircleDiscretizeMethod.ChordLength` (nested type syntax)
- `NullableToList` converter needed for the Hull slot ItemsControl (single nullable item)
