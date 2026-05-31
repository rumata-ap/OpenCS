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
├── CSmath/            Pure math (vectors, matrices, splines, geometry) — no external deps
├── CScore/            Domain model & computation engine → depends on CSmath, CSTriangulation
├── CSTriangulation/   Triangulation algorithms (Advancing Front, Ruppert CDT) — no external deps
└── OpenCS/            WPF entry point (net9.0-windows, UseWPF) → depends on CScore
```

**Dependency order matters**: always build/change CSmath first, then CSTriangulation, then CScore, then OpenCS. `dotnet build` on the solution handles order automatically.

## External Dependency

~~CScore references `Triangle.dll`~~ — REMOVED. Triangulation is now provided by `CSTriangulation` project (custom C# port from GreenSectionPy). No external DLL required.

## Architecture

- **Framework**: .NET 9.0, WPF (Windows-only, requires `net9.0-windows` TFM)
- **Pattern**: MVVM — `ViewModelBase` (INotifyPropertyChanged), `RelayCommand` (ICommand), `ObservableCollection` for bindings
- **Persistence**: Raw SQLite via `Microsoft.Data.Sqlite` (ADO.NET, **not** EF Core). `DatabaseService` in `OpenCS/Utilites/` manages all CRUD with parameterized SQL. Nested objects serialized as JSON columns via Newtonsoft.Json. DB file is `dbapp.db` at solution root.
- **Plotting**: ScottPlot.WPF v5 for 2D plots
- **DXF Import**: netDxf
- **Geometry**: Custom `WktHelper.cs` + `GridSplit.cs` (Sutherland–Hodgman) for WKT/contour operations; `CSTriangulation` project for Delaunay/Ruppert triangulation

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
| `CScore/Geo.cs` | Polygon slicing, triangulation (CSTriangulation: Advancing Front & Ruppert) |
| `OpenCS/DataSource/` | Russian-language CSV files for material properties |

## Important NuGet Packages

- **OpenCS**: CsvHelper, Microsoft.Data.Sqlite, Microsoft.Xaml.Behaviors.Wpf, netDxf, ScottPlot.WPF
- **CScore**: Newtonsoft.Json
- **CSTriangulation**: none
- **CSmath**: none

## Style & Conventions

- All domain classes and VMs have **Russian-language XML doc comments**
- UI labels, CSV column headers, material names use Russian
- **No hardcoded UI strings.** All user-visible text in XAML must use `DynamicResource` keys defined in `OpenCS/Resources/Strings.ru-RU.xaml` (and `Strings.en-US.xaml` for English). Localizable strings include labels, headers, tooltips, button text, and ComboBox item names. Never write Russian or English text directly in `.xaml` or `.cs` files if it will be shown to the user — always add a key to both resource dictionaries and reference it via `{DynamicResource KeyName}`.
- Global styles in `App.xaml`: `IconButton`, `IconButton25`, `AppFontSize` (13), implicit styles for MenuItem, Window, TreeView, TextBlock, TextBox, DataGridCell, DataGridColumnHeader
- No `.editorconfig`, no analyzers, no formatting config
- Entity IDs are `int` (SQLite auto-increment), assigned after INSERT via `last_insert_rowid()`
