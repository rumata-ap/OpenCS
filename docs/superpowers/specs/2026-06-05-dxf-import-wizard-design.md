# DXF Import Wizard — Design Spec

**Date:** 2026-06-05  
**Project:** OpenCS  
**Status:** Approved

---

## Overview

Replace the existing blind list-based DXF import (`FromDxfPage`, 3 tabs) with an interactive
single-panel view where primitives are selected visually by clicking directly on the DXF canvas.
The import result (Contour / CircleP objects saved to DB via DatabaseService) remains unchanged.

---

## Goals

- Visual selection of DXF primitives with mouse (click, Shift+click, Ctrl+click)
- Zoom (mouse wheel) and pan (right mouse button drag) on the DXF canvas
- Color coding by layer
- Support for additional DXF primitive types: Arc (approximated) and Line (stitched into chains)
- No hardcoded user-visible strings — all text via DynamicResource keys in Strings.ru-RU.xaml / Strings.en-US.xaml
- Existing save logic (DatabaseService, AppViewModel renumber) unchanged

---

## Layout: `FromDxfPage` (single panel, no tabs)

```
┌─ Toolbar ──────────────────────────────────────────────────────┐
│  [📁]  Набор: [_________]  Единицы: [мм ▼]                    │
├─ Col 0 (160px) ┬─ Col 1 (*) ──────────────────┬─ Col 2 (200px)┤
│  GroupBox      │                              │  GroupBox      │
│  "DxfLayers"   │   DxfInteractiveView         │  "Contours"    │
│                │   WPF Canvas +               │  ListBox       │
│  ■ Layer1      │   MatrixTransform            │  [AddCaps]     │
│  ■ Layer2      │                              │  GroupBox      │
│  ■ Layer3      │   zoom: mouse wheel          │  "DxfCircles"  │
│                │   pan:  right mouse drag     │  ListBox       │
│                │   select: LMB / Shift / Ctrl │  [AddCaps]     │
└────────────────┴──────────────────────────────┴────────────────┘
```

- **Col 0**: Layer legend — `ItemsControl` bound to `Layers` (list of `LayerInfo`).
  Each item: colored `Rectangle` (14×14) + `TextBlock` with layer name.
- **Col 1**: `DxfInteractiveView` — new interactive control (see below).
- **Col 2**: Two stacked `GroupBox` sections (Contours / Circles), each with a `ListBox`
  bound to `ContoursPrj` / `CirclesPrj` and an «AddCaps» save button.

---

## New Control: `DxfInteractiveView`

**Files:** `OpenCS/Views/DxfInteractiveView.xaml` + `DxfInteractiveView.xaml.cs`

### XAML structure

```
UserControl
└── Border (ClipToBounds=True, Background=#2B2B2B)
    └── Canvas x:Name="InnerCanvas"
        └── (Path and Ellipse elements added programmatically)
```

`InnerCanvas.RenderTransform = new MatrixTransform()` — single matrix for scale + translate.

### Public API

```csharp
void Load(IReadOnlyList<DxfPrimitive> primitives, IReadOnlyList<LayerInfo> layers)
void ClearAll()
Action<IReadOnlyList<DxfPrimitive>>? SelectionChanged { get; set; }
```

### Mouse interactions

| Action | Effect |
|---|---|
| Mouse wheel | Zoom toward cursor: scale matrix around cursor point |
| Right button drag | Pan: translate matrix by delta |
| LMB on shape | Select (no modifier: clear all + select this) |
| Shift+LMB on shape | Add to selection |
| Ctrl+LMB on shape | Toggle this item |
| LMB on empty area | Clear selection (no modifier) |

### Primitive rendering

- Each `DxfPrimitive` of kind `Contour` → `Path` (Geometry from Xs/Ys points)
- Each `DxfPrimitive` of kind `Circle` → `Ellipse` (positioned via Canvas.Left/Top, Width/Height from Radius)
- Each shape: `Tag = DxfPrimitive` (for reverse lookup)
- Normal state: `Stroke = layer color`, `StrokeThickness = 1.5`, `Fill = Transparent`
- Selected state: `Stroke = #FFD700` (yellow), `StrokeThickness = 3.0`

After any selection change: invoke `SelectionChanged` callback with current selected primitives.

---

## New Model: `DxfPrimitive`

**File:** `OpenCS/ViewModels/DxfPrimitive.cs`

```csharp
public enum DxfPrimitiveKind { Contour, Circle }

public class DxfPrimitive
{
    public DxfPrimitiveKind Kind   { get; init; }
    public string           LayerName { get; init; }
    public bool             IsSelected { get; set; }

    // Populated when Kind == Contour
    public double[]?  Xs      { get; init; }
    public double[]?  Ys      { get; init; }
    public Contour?   Contour { get; init; }

    // Populated when Kind == Circle
    public double    CenterX { get; init; }
    public double    CenterY { get; init; }
    public double    Radius  { get; init; }
    public CircleP?  Circle  { get; init; }
}
```

**`LayerInfo` record:**

```csharp
public record LayerInfo(string Name, string HexColor);
```

Layer colors assigned in declaration order from fixed palette:
`["#318CE7", "#9457EB", "#CC397B", "#F07427", "#F4CA16", "#20B2AA"]` (same as existing DxfPlot).

---

## DXF Parsing Changes (`FromDxfVM.OpenDxf`)

### Supported primitives (extended)

| DXF type | Handling |
|---|---|
| `Polyline2D` | As before: vertices → Contour |
| `Circle` | As before: center + radius → CircleP |
| `Arc` | Approximate as 32-point polyline from StartAngle to EndAngle → Contour |
| `Line` | Collect per layer, stitch closed chains → Contour (see algorithm below) |

### Arc approximation (32 points)

netDxf `Arc.StartAngle` / `Arc.EndAngle` are in **degrees** — convert to radians before trigonometry.

```
start_rad = start_angle * π / 180
end_rad   = end_angle   * π / 180
for i = 0..32:
    angle = start_rad + i * (end_rad - start_rad) / 32
    x = center.X + radius * cos(angle)
    y = center.Y + radius * sin(angle)
```

### Line stitching algorithm

1. Normalize all endpoints with snap tolerance `tol = 1e-6 * scale` (round to grid)
2. Build adjacency graph: `Dictionary<(double x, double y), List<int>> graph`
3. DFS from unvisited edges: collect vertex chain until cycle closes or no continuation
4. Closed chain (first == last after traversal) → `Contour` with repeated first point
5. Open chain → `Contour` without repetition (degenerate case, still imported)

---

## `FromDxfVM` Changes

### Removed

- `ContourInCommand`, `ContoursInCommand`, `ContourOutCommand`
- `CircleInCommand`, `CirclesInCommand`, `CircleOutCommand`
- Fields `ContoursListBox`, `CirclesListBox` (code-behind references)
- Collections `Contours`, `Circles` (raw DXF pool — replaced by `_primitives`)
- Property `CurrentPlot` (`UserControl?`) — canvas is now declared statically in XAML

### Added

- `List<DxfPrimitive> _primitives` — internal list of all parsed primitives
- `List<LayerInfo> _layers` — layer legend (bound to Col 0 panel)
- `ObservableCollection<LayerInfo> Layers` — bindable layer list
- `Action<IReadOnlyList<DxfPrimitive>>? OnSelectionChanged` — callback from canvas;
  rebuilds `ContoursPrj` and `CirclesPrj` from selected primitives

### Unchanged

- `ContoursPrj`, `CirclesPrj` — staging collections before DB save
- `SaveContoursCommand` → `mvm.db.AddRange(contoursPrj)` + `mvm.ContoursRenumber()`
- `SaveCirclesCommand` → `mvm.db.AddRange(circlesPrj)` + `mvm.CirclesRenumber()`
- `OpenDXFCommand`, `GeometrySet`, `UnitIdx`, `Units`

---

## `FromDxfPage` Code-Behind Wiring

```csharp
public FromDxfPage(AppViewModel mvm)
{
    InitializeComponent();
    var vm = new FromDxfVM { mvm = mvm };
    DataContext = vm;
    InteractiveCanvas.SelectionChanged = vm.OnSelectionChanged;
}
```

Single connection point between canvas and VM — no ListBox references, no transfer commands.

---

## Localization: New Resource Keys

Add to both `Strings.ru-RU.xaml` and `Strings.en-US.xaml`:

| Key | Russian | English |
|---|---|---|
| `DxfLayers` | Слои | Layers |
| `DxfCircles` | Окружности | Circles |

Check whether `Contours` key already exists; reuse if so.

---

## Files Summary

| Action | File |
|---|---|
| Create | `OpenCS/ViewModels/DxfPrimitive.cs` |
| Create | `OpenCS/Views/DxfInteractiveView.xaml` |
| Create | `OpenCS/Views/DxfInteractiveView.xaml.cs` |
| Modify | `OpenCS/ViewModels/FromDxfVM.cs` |
| Modify | `OpenCS/Views/FromDxfPage.xaml` |
| Modify | `OpenCS/Views/FromDxfPage.xaml.cs` |
| Modify | `OpenCS/Resources/Strings.ru-RU.xaml` |
| Modify | `OpenCS/Resources/Strings.en-US.xaml` |
| Delete | `OpenCS/Views/DxfPlot.xaml` |
| Delete | `OpenCS/Views/DxfPlot.xaml.cs` |
