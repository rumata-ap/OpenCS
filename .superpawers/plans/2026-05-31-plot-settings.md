# Plot Settings Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpawers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Global configurable plot colors (background, grid, curves, fills) and display toggles (grid, point labels, tooltips) persisted in SQLite, controlled via main menu Settings window.

**Architecture:** `PlotSettings` class serialized as JSON in `settings` SQLite table. `DatabaseService` loads/saves settings. `SettingsWindow` (WPF Window) edits colors via hex textboxes with preview swatches. `IPlotService.ApplySettings()` propagates to `PlotCanvas`, which uses settings in `OnRender`. Point labels and tooltips rendered directly on canvas.

**Tech Stack:** .NET 9.0 WPF, Newtonsoft.Json, Microsoft.Data.Sqlite. No new packages.

---

### Task 1: Create `PlotSettings` data class

**Files:**
- Create: `OpenCS/Utilites/PlotSettings.cs`

- [ ] **Step 1: Write PlotSettings.cs**

```csharp
using Newtonsoft.Json;

namespace OpenCS.Utilites
{
   /// <summary>
   /// Глобальные настройки отображения графиков. Сериализуются в JSON.
   /// </summary>
   public class PlotSettings
   {
      [JsonProperty("bg")]
      public string Background { get; set; } = "#FFFFFF";

      [JsonProperty("grid")]
      public string Grid { get; set; } = "#D3D3D3";

      [JsonProperty("curve")]
      public string Curve { get; set; } = "#003A6C";

      [JsonProperty("fill")]
      public string Fill { get; set; } = "#F0EACD50";

      [JsonProperty("marker")]
      public string MarkerFill { get; set; } = "#003A6C";

      [JsonProperty("text")]
      public string Text { get; set; } = "#333333";

      [JsonProperty("showGrid")]
      public bool ShowGrid { get; set; } = true;

      [JsonProperty("showLabels")]
      public bool ShowPointLabels { get; set; }

      [JsonProperty("showTooltips")]
      public bool ShowTooltips { get; set; } = true;

      public static PlotSettings Default => new();

      public PlotSettings Clone() => new()
      {
         Background = Background, Grid = Grid, Curve = Curve,
         Fill = Fill, MarkerFill = MarkerFill, Text = Text,
         ShowGrid = ShowGrid, ShowPointLabels = ShowPointLabels,
         ShowTooltips = ShowTooltips
      };
   }
}
```

- [ ] **Step 2: Build**

```bash
dotnet build OpenCS.sln
```

Expected: 0 errors.

---

### Task 2: Add settings storage to `DatabaseService`

**Files:**
- Modify: `OpenCS/Utilites/DatabaseService.cs`

- [ ] **Step 1: Add `settings` table to `EnsureCreated`**

Find method `EnsureCreated()` (~line 55). Add before `cmd.ExecuteNonQuery()`:

```csharp
cmd.CommandText += @"
    CREATE TABLE IF NOT EXISTS settings (
        key TEXT PRIMARY KEY,
        value_json TEXT NOT NULL DEFAULT '{}'
    );";
```

- [ ] **Step 2: Add `LoadPlotSettings` method**

Insert after existing `LoadDiagrams()` method (~line 393):

```csharp
public PlotSettings LoadPlotSettings()
{
    var cmd = _connection.CreateCommand();
    cmd.CommandText = "SELECT value_json FROM settings WHERE key='plot'";
    var json = cmd.ExecuteScalar() as string;
    if (json == null) return PlotSettings.Default;
    return JsonConvert.DeserializeObject<PlotSettings>(json) ?? PlotSettings.Default;
}
```

- [ ] **Step 3: Add `SavePlotSettings` method**

```csharp
public void SavePlotSettings(PlotSettings s)
{
    var json = JsonConvert.SerializeObject(s);
    var cmd = _connection.CreateCommand();
    cmd.CommandText = @"INSERT OR REPLACE INTO settings (key, value_json)
                        VALUES ('plot', $json)";
    cmd.Parameters.AddWithValue("$json", json);
    cmd.ExecuteNonQuery();
}
```

- [ ] **Step 4: Add `using` for PlotSettings namespace**

`DatabaseService.cs` already imports `OpenCS.Utilites` (same namespace), so no new imports needed.

- [ ] **Step 5: Build**

```bash
dotnet build OpenCS.sln
```

Expected: 0 errors.

---

### Task 3: Extend `IPlotService` with `ApplySettings`

**Files:**
- Modify: `OpenCS/Services/IPlotService.cs`

- [ ] **Step 1: Add method to interface**

Insert after `ShowLegend` line:

```csharp
void ApplySettings(Utilites.PlotSettings settings);
```

(Add `using OpenCS.Utilites;` at top if not present — likely not needed since `PlotSettings` is referenced via `Utilites.PlotSettings`.)

- [ ] **Step 2: Implement in `WpfPlotService`**

Modify `OpenCS/Services/WpfPlotService.cs`. Add field and method:

At top of class, add field:
```csharp
private PlotSettings _settings = OpenCS.Utilites.PlotSettings.Default;
```

Add method:
```csharp
public void ApplySettings(OpenCS.Utilites.PlotSettings settings)
{
    _settings = settings;
    _canvas.ApplySettings(settings);
}
```

Update `AddScatter` default color — replace `"#000000"` with `_settings.Curve`:
```csharp
// In AddScatter:
Stroke = ParseColor(color ?? _settings.Curve),
```

Update `AddPolygon` default:
```csharp
// In AddPolygon, lineColor default:
Stroke = ParseColor(lineColor ?? _settings.Curve),
```

Update `AddMarkers` default:
```csharp
// In AddMarkers:
Fill = ParseColor(color ?? _settings.MarkerFill),
```

- [ ] **Step 3: Build**

```bash
dotnet build OpenCS.sln
```

Expected: 0 errors.

---

### Task 4: Update `PlotCanvas` for settings, grid, labels, tooltip

**Files:**
- Modify: `OpenCS/Views/PlotCanvas.cs`

- [ ] **Step 1: Add settings field and `ApplySettings` method**

Add field at top:
```csharp
private PlotSettings _settings = new();
```

Add method:
```csharp
public void ApplySettings(PlotSettings s)
{
    _settings = s;
    InvalidateVisual();
}
```

(Import `OpenCS.Utilites` namespace.)

- [ ] **Step 2: Use settings in `OnRender`**

Replace background fill line:
```csharp
// Replace: dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, w, h));
dc.DrawRectangle(ParseBrush(_settings.Background), null, new Rect(0, 0, w, h));
```

Wrap grid drawing in condition:
```csharp
// Replace: DrawGrid(dc, w, h);
if (_settings.ShowGrid) DrawGrid(dc, w, h);
```

Update `DrawGrid` to use settings color:
```csharp
private void DrawGrid(DrawingContext dc, double w, double h)
{
    var pen = new Pen(ParseBrush(_settings.Grid), 0.3);
    pen.DashStyle = DashStyles.Dot;
    for (double x = 30; x < w - 30; x += 40)
        dc.DrawLine(pen, new Point(x, 30), new Point(x, h - 30));
    for (double y = 30; y < h - 30; y += 40)
        dc.DrawLine(pen, new Point(30, y), new Point(w - 30, y));
}
```

Update `DrawTitle` to use settings text color:
```csharp
private void DrawTitle(DrawingContext dc, double w)
{
    var ft = new FormattedText(_title, CultureInfo.CurrentCulture,
        FlowDirection.LeftToRight, new Typeface("Segoe UI"), 13,
        ParseBrush(_settings.Text), 1.0);
    dc.DrawText(ft, new Point((w - ft.Width) / 2, 4));
}
```

Update `DrawAxes` to use settings text color:
```csharp
// Replace Brushes.Gray with ParseBrush(_settings.Text) for labels
```

- [ ] **Step 3: Store transform params for tooltip reverse-transform**

Add fields:
```csharp
private double _sx, _sy, _ox, _oy;
```

In `OnRender`, after computing transform parameters, store them:
```csharp
_sx = sx; _sy = sy; _ox = ox; _oy = oy;
```

- [ ] **Step 4: Add point labels rendering**

After the `foreach (var el in elements) el.Render(dc, ToPixel);` loop, add:

```csharp
if (_settings.ShowPointLabels)
{
    foreach (var el in _elements)
    {
        if (el is MarkerElement m)
        {
            var ftBrush = ParseBrush(_settings.Text);
            var typeface = new Typeface("Segoe UI");
            int n = Math.Min(m.Xs.Length, m.Ys.Length);
            for (int i = 0; i < n; i++)
            {
                var pt = ToPixel(m.Xs[i], m.Ys[i]);
                var ft = new FormattedText($"({m.Xs[i]:F4}; {m.Ys[i]:F2})",
                    CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                    typeface, 9, ftBrush, 1.0);
                dc.DrawText(ft, new Point(pt.X + 5, pt.Y - ft.Height - 3));
            }
        }
        else if (el is ScatterElement s)
        {
            int n = Math.Min(s.Xs.Length, s.Ys.Length);
            if (n > 0)
            {
                var pt = ToPixel(s.Xs[0], s.Ys[0]);
                var ftBrush = ParseBrush(_settings.Text);
                var typeface = new Typeface("Segoe UI");
                var ft = new FormattedText($"({s.Xs[0]:F4}; {s.Ys[0]:F2})",
                    CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                    typeface, 9, ftBrush, 1.0);
                dc.DrawText(ft, new Point(pt.X + 5, pt.Y - ft.Height - 3));
            }
        }
    }
}
```

- [ ] **Step 5: Add tooltip support**

Override `OnMouseMove`:
```csharp
protected override void OnMouseMove(System.Windows.Input.MouseEventArgs e)
{
    base.OnMouseMove(e);
    if (!_settings.ShowTooltips) return;

    var pos = e.GetPosition(this);
    double x = (pos.X - _ox) / _sx;
    double y = (pos.Y - _oy) / _sy;

    if (_sx == 0 || _sy == 0) return;
    ToolTip = $"X={x:F4}  Y={y:F4}";
}
```

Override `OnMouseLeave`:
```csharp
protected override void OnMouseLeave(System.Windows.Input.MouseEventArgs e)
{
    base.OnMouseLeave(e);
    ToolTip = null;
}
```

- [ ] **Step 6: Add `ParseBrush` helper**

```csharp
private static Brush ParseBrush(string hex)
{
    try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
    catch { return Brushes.White; }
}
```

- [ ] **Step 7: Build**

```bash
dotnet build OpenCS.sln
```

Expected: 0 errors.

---

### Task 5: Create `SettingsWindow`

**Files:**
- Create: `OpenCS/Views/SettingsWindow.xaml`
- Create: `OpenCS/Views/SettingsWindow.xaml.cs`

- [ ] **Step 1: Write `SettingsWindow.xaml`**

```xml
<Window x:Class="OpenCS.Views.SettingsWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Настройка графиков" Width="420" Height="520"
        WindowStartupLocation="CenterOwner" ResizeMode="NoResize">
    <Grid Margin="15">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <GroupBox Grid.Row="0" Header="Цвета" Margin="0,0,0,10">
            <Grid>
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="140"/>
                    <ColumnDefinition Width="*"/>
                    <ColumnDefinition Width="30"/>
                </Grid.ColumnDefinitions>
                <Grid.RowDefinitions>
                    <RowDefinition Height="28"/>
                    <RowDefinition Height="28"/>
                    <RowDefinition Height="28"/>
                    <RowDefinition Height="28"/>
                    <RowDefinition Height="28"/>
                    <RowDefinition Height="28"/>
                </Grid.RowDefinitions>

                <TextBlock Grid.Row="0" Grid.Column="0" Text="Фон" VerticalAlignment="Center"/>
                <TextBox Grid.Row="0" Grid.Column="1" x:Name="BgBox" Margin="2" Height="22"
                         VerticalContentAlignment="Center" HorizontalContentAlignment="Center"/>
                <Rectangle Grid.Row="0" Grid.Column="2" x:Name="BgSwatch" Margin="3" RadiusX="3" RadiusY="3"/>

                <TextBlock Grid.Row="1" Grid.Column="0" Text="Сетка" VerticalAlignment="Center"/>
                <TextBox Grid.Row="1" Grid.Column="1" x:Name="GridBox" Margin="2" Height="22"
                         VerticalContentAlignment="Center" HorizontalContentAlignment="Center"/>
                <Rectangle Grid.Row="1" Grid.Column="2" x:Name="GridSwatch" Margin="3" RadiusX="3" RadiusY="3"/>

                <TextBlock Grid.Row="2" Grid.Column="0" Text="Кривые" VerticalAlignment="Center"/>
                <TextBox Grid.Row="2" Grid.Column="1" x:Name="CurveBox" Margin="2" Height="22"
                         VerticalContentAlignment="Center" HorizontalContentAlignment="Center"/>
                <Rectangle Grid.Row="2" Grid.Column="2" x:Name="CurveSwatch" Margin="3" RadiusX="3" RadiusY="3"/>

                <TextBlock Grid.Row="3" Grid.Column="0" Text="Заливка" VerticalAlignment="Center"/>
                <TextBox Grid.Row="3" Grid.Column="1" x:Name="FillBox" Margin="2" Height="22"
                         VerticalContentAlignment="Center" HorizontalContentAlignment="Center"/>
                <Rectangle Grid.Row="3" Grid.Column="2" x:Name="FillSwatch" Margin="3" RadiusX="3" RadiusY="3"/>

                <TextBlock Grid.Row="4" Grid.Column="0" Text="Маркеры" VerticalAlignment="Center"/>
                <TextBox Grid.Row="4" Grid.Column="1" x:Name="MarkerBox" Margin="2" Height="22"
                         VerticalContentAlignment="Center" HorizontalContentAlignment="Center"/>
                <Rectangle Grid.Row="4" Grid.Column="2" x:Name="MarkerSwatch" Margin="3" RadiusX="3" RadiusY="3"/>

                <TextBlock Grid.Row="5" Grid.Column="0" Text="Текст" VerticalAlignment="Center"/>
                <TextBox Grid.Row="5" Grid.Column="1" x:Name="TextBoxField" Margin="2" Height="22"
                         VerticalContentAlignment="Center" HorizontalContentAlignment="Center"/>
                <Rectangle Grid.Row="5" Grid.Column="2" x:Name="TextSwatch" Margin="3" RadiusX="3" RadiusY="3"/>
            </Grid>
        </GroupBox>

        <GroupBox Grid.Row="1" Header="Отображение" Margin="0,0,0,10">
            <StackPanel>
                <CheckBox x:Name="ShowGridCb" Content="Сетка" Margin="0,3,0,0"/>
                <CheckBox x:Name="ShowLabelsCb" Content="Подписи точек" Margin="0,3,0,0"/>
                <CheckBox x:Name="ShowTooltipsCb" Content="Всплывающие подсказки" Margin="0,3,0,0"/>
            </StackPanel>
        </GroupBox>

        <GroupBox Grid.Row="2" Header="Сброс" Margin="0,0,0,10">
            <Button Content="Вернуть значения по умолчанию" Click="Reset_Click" Height="28"/>
        </GroupBox>

        <StackPanel Grid.Row="4" Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,10,0,0">
            <Button Content="Применить" Click="Apply_Click" Width="90" Height="28" Margin="0,0,8,0"/>
            <Button Content="OK" Click="Ok_Click" Width="70" Height="28" Margin="0,0,8,0"/>
            <Button Content="Отмена" Click="Cancel_Click" Width="70" Height="28"/>
        </StackPanel>
    </Grid>
</Window>
```

- [ ] **Step 2: Write `SettingsWindow.xaml.cs`**

```csharp
using OpenCS.Utilites;

using System.Windows;
using System.Windows.Media;

namespace OpenCS.Views
{
   public partial class SettingsWindow : Window
   {
      readonly AppViewModel _mvm;
      readonly PlotSettings _settings;

      public SettingsWindow(AppViewModel mvm)
      {
         InitializeComponent();
         _mvm = mvm;
         _settings = mvm.PlotSettings.Clone();
         Owner = Application.Current.MainWindow;

         LoadToUi();
         HookTextBoxes();
      }

      void LoadToUi()
      {
         BgBox.Text = _settings.Background;
         GridBox.Text = _settings.Grid;
         CurveBox.Text = _settings.Curve;
         FillBox.Text = _settings.Fill;
         MarkerBox.Text = _settings.MarkerFill;
         TextBoxField.Text = _settings.Text;
         ShowGridCb.IsChecked = _settings.ShowGrid;
         ShowLabelsCb.IsChecked = _settings.ShowPointLabels;
         ShowTooltipsCb.IsChecked = _settings.ShowTooltips;
         UpdateSwatches();
      }

      void HookTextBoxes()
      {
         BgBox.TextChanged += (_, _) => { _settings.Background = BgBox.Text; UpdateSwatch(BgSwatch, BgBox.Text); };
         GridBox.TextChanged += (_, _) => { _settings.Grid = GridBox.Text; UpdateSwatch(GridSwatch, GridBox.Text); };
         CurveBox.TextChanged += (_, _) => { _settings.Curve = CurveBox.Text; UpdateSwatch(CurveSwatch, CurveBox.Text); };
         FillBox.TextChanged += (_, _) => { _settings.Fill = FillBox.Text; UpdateSwatch(FillSwatch, FillBox.Text); };
         MarkerBox.TextChanged += (_, _) => { _settings.MarkerFill = MarkerBox.Text; UpdateSwatch(MarkerSwatch, MarkerBox.Text); };
         TextBoxField.TextChanged += (_, _) => { _settings.Text = TextBoxField.Text; UpdateSwatch(TextSwatch, TextBoxField.Text); };
         ShowGridCb.Checked += (_, _) => _settings.ShowGrid = true;
         ShowGridCb.Unchecked += (_, _) => _settings.ShowGrid = false;
         ShowLabelsCb.Checked += (_, _) => _settings.ShowPointLabels = true;
         ShowLabelsCb.Unchecked += (_, _) => _settings.ShowPointLabels = false;
         ShowTooltipsCb.Checked += (_, _) => _settings.ShowTooltips = true;
         ShowTooltipsCb.Unchecked += (_, _) => _settings.ShowTooltips = false;
      }

      void UpdateSwatches()
      {
         UpdateSwatch(BgSwatch, BgBox.Text);
         UpdateSwatch(GridSwatch, GridBox.Text);
         UpdateSwatch(CurveSwatch, CurveBox.Text);
         UpdateSwatch(FillSwatch, FillBox.Text);
         UpdateSwatch(MarkerSwatch, MarkerBox.Text);
         UpdateSwatch(TextSwatch, TextBoxField.Text);
      }

      static void UpdateSwatch(System.Windows.Shapes.Rectangle rect, string hex)
      {
         try { rect.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
         catch { rect.Fill = Brushes.Gray; }
      }

      void Apply_Click(object sender, RoutedEventArgs e)
      {
         _mvm.PlotSettings = _settings.Clone();
         _mvm.ApplyPlotSettings();
      }

      void Ok_Click(object sender, RoutedEventArgs e)
      {
         _mvm.PlotSettings = _settings.Clone();
         _mvm.ApplyPlotSettings();
         _mvm.db.SavePlotSettings(_mvm.PlotSettings);
         Close();
      }

      void Cancel_Click(object sender, RoutedEventArgs e)
      {
         Close();
      }

      void Reset_Click(object sender, RoutedEventArgs e)
      {
         var def = PlotSettings.Default;
         _settings.Background = def.Background;
         _settings.Grid = def.Grid;
         _settings.Curve = def.Curve;
         _settings.Fill = def.Fill;
         _settings.MarkerFill = def.MarkerFill;
         _settings.Text = def.Text;
         _settings.ShowGrid = def.ShowGrid;
         _settings.ShowPointLabels = def.ShowPointLabels;
         _settings.ShowTooltips = def.ShowTooltips;
         LoadToUi();
      }
   }
}
```

- [ ] **Step 3: Build**

```bash
dotnet build OpenCS.sln
```

Expected: 0 errors.

---

### Task 6: Add menu item and AppViewModel wiring

**Files:**
- Modify: `OpenCS/AppViewModel.cs`
- Modify: `OpenCS/MainWindow.xaml` (or App.xaml if menu is there)

- [ ] **Step 1: Check where the main menu is defined**

Read `MainWindow.xaml` to see existing menu structure. Add menu item.

- [ ] **Step 2: Add `PlotSettings` property and command to `AppViewModel`**

In `AppViewModel.cs`, add:

```csharp
public PlotSettings PlotSettings { get; set; } = PlotSettings.Default;

public RelayCommand OpenPlotSettingsCommand { get; }

// In constructor, add:
OpenPlotSettingsCommand = new RelayCommand(_ => new Views.SettingsWindow(this).ShowDialog());
```

Add `using OpenCS.Utilites;` if not already present.

- [ ] **Step 3: Add `LoadPlotSettings` and `ApplyPlotSettings` to AppViewModel**

```csharp
public void LoadPlotSettings()
{
    PlotSettings = db.LoadPlotSettings() ?? PlotSettings.Default;
}

public void ApplyPlotSettings()
{
    // Apply to any active plot service
    // Since PlotService is per-view, we broadcast via a static event or iterate
    // For simplicity: store on AppViewModel, views read it when activated
}
```

- [ ] **Step 4: Update `MainWindow.xaml`**

Add menu item (find existing menu structure and insert):

```xml
<MenuItem Header="Вид">
    <MenuItem Header="Настройка графиков..." Command="{Binding OpenPlotSettingsCommand}" />
</MenuItem>
```

- [ ] **Step 5: Build**

```bash
dotnet build OpenCS.sln
```

Expected: 0 errors.

---

### Task 7: Integration — wire settings to existing views

**Files:**
- Modify: `OpenCS/Views/ContourPlot.xaml.cs`
- Modify: `OpenCS/Views/RCFiberRegionPage.xaml.cs`
- Modify: `OpenCS/Views/RCFiberRegionView.xaml.cs`
- Modify: `OpenCS/Views/DiagramPage.xaml.cs`
- Modify: `OpenCS/Views/DxfPlot.xaml.cs`

- [ ] **Step 1: Apply settings after creating `WpfPlotService` in each view code-behind**

In each `*.xaml.cs` constructor, after `var plotService = new WpfPlotService(plot);`, add:

```csharp
plotService.ApplySettings(mvm.PlotSettings);
```

For `RCFiberRegionPage` and `RCFiberRegionView`, settings object is accessible via `mvm.PlotSettings`.

For `ContourPlot`, settings via `mvm.PlotSettings`.

For `DiagramPage`:
```csharp
_plotService?.ApplySettings(mvm.PlotSettings);
```

For `DxfPlot` (no mvm reference), pass `AppViewModel` as constructor param or skip. DxfPlot takes `DxfDocument` only — need to add `AppViewModel` param too, or skip for now.

- [ ] **Step 2: Call `LoadPlotSettings` in `AppViewModel` constructor**

After `db.LoadAll()` call:
```csharp
PlotSettings = db.LoadPlotSettings() ?? PlotSettings.Default;
```

- [ ] **Step 3: Build**

```bash
dotnet build OpenCS.sln
```

Expected: 0 errors.

---

### Task 8: Final build and verification

- [ ] **Step 1: Clean build**

```bash
dotnet clean OpenCS.sln; dotnet build OpenCS.sln
```

Expected: 0 errors, 0 new warnings.

- [ ] **Step 2: Launch and test**

```bash
Start-Process -FilePath "OpenCS\bin\Debug\net9.0-windows\OpenCS.exe"
```

Verify:
1. Open «Вид → Настройка графиков...» — window appears
2. Change background color → Apply → plot updates
3. Toggle grid → grid appears/disappears
4. Enable point labels → labels visible on diagrams/contours
5. Hover over plot → tooltip shows data coordinates
6. OK → close window, reopen app → settings persist
7. Reset → defaults restored
