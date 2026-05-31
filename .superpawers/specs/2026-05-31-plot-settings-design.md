# Plot Settings — Design Spec

**Goal:** Global configurable plot appearance (colors, grid, point labels, tooltips) persisted in SQLite and controlled via main menu.

## 1. `PlotSettings` data class

Location: `OpenCS/Utilites/PlotSettings.cs`

Newtonsoft.Json serializable, stored as JSON in SQLite.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Background` | string | `"#FFFFFF"` | Plot background fill (hex) |
| `Grid` | string | `"#D3D3D3"` | Grid line color |
| `Curve` | string | `"#003A6C"` | Default curve/scatter color |
| `Fill` | string | `"#F0EACD50"` | Polygon fill color (with alpha) |
| `MarkerFill` | string | `"#003A6C"` | Marker dot fill color |
| `Text` | string | `"#333333"` | Label/title text color |
| `ShowGrid` | bool | `true` | Render dotted grid lines |
| `ShowPointLabels` | bool | `false` | Render (X;Y) text near markers |
| `ShowTooltips` | bool | `true` | Show coordinate tooltip on hover |

A static `PlotSettings.Default` property returns the hardcoded defaults.

## 2. Database storage

New table in `dbapp.db` (managed in `DatabaseService`):

```sql
CREATE TABLE IF NOT EXISTS settings (
    key TEXT PRIMARY KEY,
    value_json TEXT NOT NULL DEFAULT '{}'
);
```

Two methods on `DatabaseService`:
- `PlotSettings LoadPlotSettings()` — reads row `key='plot'`, deserializes `value_json`
- `void SavePlotSettings(PlotSettings s)` — serializes, upserts row `key='plot'`

Called from `AppViewModel` on startup and on settings save.

## 3. `SettingsWindow`

Location: `OpenCS/Views/SettingsWindow.xaml` + `.xaml.cs`

A modal WPF `Window` (not UserControl).

**Layout:**
- GroupBox «Цвета» with 6 rows: label + `TextBox` (hex) + colored `Rectangle` preview + 6 preset swatch buttons
- GroupBox «Отображение» with 3 `CheckBox`es: Сетка, Подписи точек, Tooltip
- Bottom row: «Применить» (applies without closing), «OK» (applies + closes), «Отмена»

**Behavior:**
- Constructor takes current `PlotSettings` (clone)
- «Применить» → writes settings back to `AppViewModel`, triggers `PlotService.ApplySettings()`
- «OK» → same as Apply + saves to DB via `AppViewModel.db.SavePlotSettings()`
- «Отмена» → discards changes and closes

## 4. `IPlotService` extension

New method:

```csharp
void ApplySettings(PlotSettings settings);
```

`WpfPlotService.ApplySettings()` passes settings to `PlotCanvas.ApplySettings()`.

## 5. `PlotCanvas` changes

New field `PlotSettings _settings` (initialized from defaults).

New method `ApplySettings(PlotSettings s)`:
- Stores new settings
- Calls `InvalidateVisual()` to re-render

`OnRender` changes:
- Grid: rendered only if `ShowGrid`, using `Grid` color
- Background: uses `Background` color instead of hardcoded white
- Point labels: if `ShowPointLabels`, after rendering markers, draw `(Xs[i]; Ys[i])` text near each marker position using `Text` color
- Title/labels: use `Text` color

`Curve` and `Fill` are used as **default colors** in `WpfPlotService` when no explicit color is passed to `AddScatter`/`AddPolygon`/`AddMarkers`. If a ViewModel passes a specific color, it overrides the default. Current ViewModels (RCFiberRegionVM, ContourVM) pass explicit colors via `ColorsCS[]`, so their data colors won't change unless the user edits `ColorsCS` separately (out of scope).

**Tooltip:** Override `OnMouseMove` — reverse-transform mouse position to data coordinates via stored transform params. Set `ToolTip` attached property on `PlotCanvas` with formatted `(X; Y)` string. Tooltip hides when `ShowTooltips` is off or mouse leaves canvas.

Reverse transform (applied in `OnRender` after computing sx/sy/ox/oy — store these as fields):
```csharp
Point ToDataCoord(Point pixel) => new(
    (pixel.X - ox) / sx,
    (pixel.Y - oy) / sy);
```

## 6. Main menu entry

In `App.xaml` or `MainWindow.xaml`, add menu item:
```xml
<MenuItem Header="Вид">
   <MenuItem Header="Настройка графиков..." Command="{Binding OpenPlotSettingsCommand}" />
</MenuItem>
```

In `AppViewModel`:
- `RelayCommand OpenPlotSettingsCommand` → opens `SettingsWindow`
- `db.LoadPlotSettings()` called in constructor (or after `db.LoadAll()`)
- `PlotSettings` property, set default if DB has none

## 7. Startup flow

1. `AppViewModel` constructor
2. `db.LoadAll()` (existing)
3. `PlotSettings = db.LoadPlotSettings() ?? PlotSettings.Default`
4. When first `PlotCanvas` is created, settings applied via `IPlotService.ApplySettings()`

## Files created/modified

| Action | File |
|--------|------|
| **Create** | `OpenCS/Utilites/PlotSettings.cs` |
| **Create** | `OpenCS/Views/SettingsWindow.xaml` |
| **Create** | `OpenCS/Views/SettingsWindow.xaml.cs` |
| **Modify** | `OpenCS/Services/IPlotService.cs` — add `ApplySettings` |
| **Modify** | `OpenCS/Services/WpfPlotService.cs` — implement `ApplySettings` |
| **Modify** | `OpenCS/Views/PlotCanvas.cs` — apply settings, grid, labels, tooltip |
| **Modify** | `OpenCS/Utilites/DatabaseService.cs` — `LoadPlotSettings`, `SavePlotSettings` |
| **Modify** | `OpenCS/AppViewModel.cs` — command, settings property |
| **Modify** | `OpenCS/MainWindow.xaml` — menu item (if not already there) |
