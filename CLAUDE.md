# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

OpenCS is a WPF desktop application for computing reinforced concrete (RC) cross-sections per Russian building codes (СП 63.13330). It uses the fiber method — cross-sections are subdivided into small finite areas (fibers), and stress/strain is computed at each fiber using material diagrams.

## Build & Run

```bash
dotnet build OpenCS.sln
dotnet run --project OpenCS
```

No test projects exist in the solution. No CI/CD is configured.

## Solution Structure

```
OpenCS.sln
├── CSmath/       Pure math library (vectors, matrices, splines, geometry)
├── CScore/       Domain model & computation engine
└── OpenCS/       WPF application (entry point, views, view models)
```

**Dependency graph:** `OpenCS → CScore → CSmath`. CScore also references an external `Triangle.dll` from `..\..\SlicePlugin\SlicePlugin\bin\Debug\`.

## Architecture

- **Framework:** .NET 9.0, WPF (Windows-only)
- **Pattern:** MVVM — `ViewModelBase` with `INotifyPropertyChanged`, `RelayCommand` for `ICommand`, `ObservableCollection` for data binding
- **Services:** `ILogService`/`LogService` (replaces TextBlock logging), `IFileDialogService`/`WpfFileDialogService` (file dialogs), `IPlotService`/`WpfPlotService` (ScottPlot abstraction)
- **Persistence:** SQLite via EF Core (`dbapp.db` at solution root). Single `ApplicationContext` in `OpenCS/Utilites/`
- **Plotting:** ScottPlot.WPF for 2D visualization (contours, stress-strain diagrams)
- **DXF Import:** netDxf library for importing cross-section geometry from DXF files
- **Geometry:** NetTopologySuite for WKT/geometry operations in `Contour`

## Key Domain Concepts

- **Material types:** `Concrete`, `ReSteelF` (physical yield), `ReSteelU` (conditional yield), `Steel`
- **Calculation types (CalcType):** C (continuous), CL (continuous long-term), N (temporary), NL (temporary long-term)
- **Diagram types:** L2 (bi-linear), L3 (tri-linear), SP63 (curvilinear per СП 63.13330 appendix)
- **Strain plane:** Defined via `Basis` (three-point) or `Kurvature` (curvature-based), following the Bernoulli hypothesis
- **Fiber integration:** `FiberRegion.Integral()` computes N, My, Mz by summing fiber contributions
- **RCFiberRegion:** Combines concrete fiber region with `ReBarGroup` collections for reinforced concrete analysis

## Key Files

- `OpenCS/AppViewModel.cs` — Main view model, central hub for all model collections and navigation
- `OpenCS/MainWindow.xaml` — Main window with TreeView navigation
- `OpenCS/Utilites/ILogService.cs`, `IFileDialogService.cs`, `IPlotService.cs` — Service interfaces
- `CScore/FiberRegion.cs` — Fiber region: mesh subdivision and stress/strain integration
- `CScore/RCFiberRegion.cs` — RC fiber region: concrete + reinforcement groups
- `CScore/Material.cs` / `MaterialChars.cs` — Material model and per-calculation-type characteristics
- `CScore/Diagramm.cs` — Stress-strain diagram generation (L2, L3, SP63)
- `CScore/Geo.cs` — Static geometry utilities: polygon slicing, triangulation (via Triangle.NET)
- `CScore/GeoProps.cs` — Geometric properties (area, moments of inertia, centroid)
- `CSmath/` — Spline interpolation (linear, Hermite, cubic, Akima), vector/matrix types

## Data Files

- `OpenCS/DataSource/` — CSV files with Russian concrete/steel material properties (heavy concrete, fine-grained concrete, reinforcement steel) per calculation type
- `dbapp.db` — SQLite database at solution root (runtime data, created by EF Core)

## Documentation

All CScore classes and OpenCS ViewModels have Russian XML documentation comments:
- CScore: Fiber, FiberRegion, RCFiberRegion, Material, MaterialChars, Diagramm, Region, ReBar, ReBarGroup, ReBarLayer, Geo, GeoProps, Out, Circle, StressPoint, XY, Load, Kurvature, FiberRegionData, and enums
- OpenCS ViewModels: AppViewModel, ContourVM, RCFiberRegionVM, MaterialVM, FromDxfVM, RebarsVM, DataSourceVM

## Notes

- CScore references an external `Triangle.dll` at a relative path outside this repository; this dependency must be available for builds
- The UI and domain model use Russian-language strings extensively (material names, CSV column headers, UI labels)
- No analyzers, linting, or formatting configuration is present
- `App.xaml` contains global styles: `IconButton`, `IconButton25`, `AppFontSize`, and implicit styles for MenuItem, UserControl, Window, TreeView, TextBlock, TextBox, TreeViewItem