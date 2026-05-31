# AGENTS.md

OpenCS — WPF desktop app for computing reinforced concrete cross-sections per Russian building codes (СП 63.13330) using the fiber method.

## Build & Run

```bash
dotnet build OpenCS.sln
dotnet run --project OpenCS
```

No tests, no CI/CD, no linters/formatters. Build is the only verification step.

## Solution Structure

```
OpenCS.sln
├── CSmath/       Pure math (vectors, matrices, splines, geometry) — no external deps
├── CScore/       Domain model & computation engine → depends on CSmath
└── OpenCS/       WPF entry point (net9.0-windows, UseWPF) → depends on CScore
```

**Dependency order matters**: always build/change CSmath first, then CScore, then OpenCS. `dotnet build` on the solution handles order automatically.

## External Dependency

CScore references `Triangle.dll` at `..\..\SlicePlugin\SlicePlugin\bin\Debug\Triangle.dll` (outside this repo). The build **fails** if that path doesn't resolve. Treat any change around `CScore\Geo.cs` triangulation code as potentially breaking this dependency.

## Architecture

- **Framework**: .NET 9.0, WPF (Windows-only, requires `net9.0-windows` TFM)
- **Pattern**: MVVM — `ViewModelBase` (INotifyPropertyChanged), `RelayCommand` (ICommand), `ObservableCollection` for bindings
- **Persistence**: Raw SQLite via `Microsoft.Data.Sqlite` (ADO.NET, **not** EF Core). `DatabaseService` in `OpenCS/Utilites/` manages all CRUD with parameterized SQL. Nested objects serialized as JSON columns via Newtonsoft.Json. DB file is `dbapp.db` at solution root.
- **Plotting**: ScottPlot.WPF v5 for 2D plots
- **DXF Import**: netDxf
- **Geometry**: NetTopologySuite for WKT/Contour operations

## Key Domain Concepts

- **Material types**: `Concrete`, `ReSteelF` (physical yield), `ReSteelU` (conditional yield), `Steel`
- **Calculation types** (CalcType enum): C (continuous), CL (continuous long-term), N (temporary), NL (temporary long-term)
- **Diagram types**: L2 (bi-linear), L3 (tri-linear), SP63 (curvilinear per СП 63 appendix)
- **Strain plane**: `Basis` (three-point) or `Kurvature` (curvature-based)
- **Integration**: `FiberRegion.Integral()` computes N, My, Mz via fiber sums
- **RC analysis**: `RCFiberRegion` wraps `FiberRegion` + `ReBarGroup` collections

## Key Files

| File | Role |
|------|------|
| `OpenCS/AppViewModel.cs` | Main VM hub — owns all collections and `DatabaseService` instance |
| `OpenCS/MainWindow.xaml` | Main window with TreeView navigation |
| `OpenCS/Utilites/DatabaseService.cs` | SQLite CRUD, all table schemas, JSON serialization contracts |
| `OpenCS/Utilites/RelayCommand.cs` | ICommand implementation |
| `OpenCS/Utilites/ViewModelBase.cs` | INotifyPropertyChanged base class |
| `OpenCS/Services/` | LogService, FileDialogService, PlotService abstractions |
| `CScore/FiberRegion.cs` | Mesh subdivision + stress/strain integration |
| `CScore/RCFiberRegion.cs` | RC region: concrete fibers + reinforcement groups |
| `CScore/Material.cs` / `MaterialChars.cs` | Material model with per-calc-type characteristics |
| `CScore/Diagramm.cs` | Stress-strain diagram generation |
| `CScore/Geo.cs` | Polygon slicing, triangulation (Triangle.NET) |
| `OpenCS/DataSource/` | Russian-language CSV files for material properties |

## Important NuGet Packages

- **OpenCS**: CsvHelper, Microsoft.Data.Sqlite, Microsoft.Xaml.Behaviors.Wpf, netDxf, ScottPlot.WPF
- **CScore**: NetTopologySuite, Newtonsoft.Json
- **CSmath**: none

## Style & Conventions

- All domain classes and VMs have **Russian-language XML doc comments**
- UI labels, CSV column headers, material names use Russian
- Global styles in `App.xaml`: `IconButton`, `IconButton25`, `AppFontSize` (13), implicit styles for MenuItem, Window, TreeView, TextBlock, TextBox, DataGridCell, DataGridColumnHeader
- No `.editorconfig`, no analyzers, no formatting config
- Entity IDs are `int` (SQLite auto-increment), assigned after INSERT via `last_insert_rowid()`
