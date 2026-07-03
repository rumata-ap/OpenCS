# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

OpenCS is a WPF desktop application for computing reinforced concrete (RC) cross-sections per Russian building codes (СП 63.13330). It uses the fiber method — cross-sections are subdivided into small finite areas (fibers), and stress/strain is computed at each fiber using material diagrams.

## Build & Run

```bash
dotnet build OpenCS.sln
dotnet run --project OpenCS
```

No tests, no CI/CD, no linters/formatters. Build is the only verification step.

## Shell

Developer is always on Windows. **Use the PowerShell tool, not Bash**, for all commands (git, dotnet, output filtering). Use PowerShell syntax: `Select-String` instead of `grep`, `2>$null` instead of `2>/dev/null`, `$env:VAR`, etc. The Bash tool resolves to a POSIX `/usr/bin/bash` that lacks PowerShell cmdlets, so `... | Select-String ...` fails with `command not found`.

**Do not ask permission to run PowerShell commands** — run them directly without prompting. This includes build/test/git/output-filtering commands such as `dotnet build OpenCS.sln 2>$null | Select-String -Pattern "error|Ошибок|Предупреждений|успешно"`.

**`dotnet build` always runs without confirmation** — never ask before running any `dotnet build *` command. It is safe, read-only from a project state perspective, and is the only verification step in this project.

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
- **Plotting**: Pure WPF — custom `Canvas`-derived controls (`FiberCanvas`, `DiagramCanvas`) render 2D plots directly (no external charting library)
- **DXF Import**: netDxf
- **Geometry**: Custom `WktHelper.cs` + `GridSplit.cs` (Sutherland–Hodgman) for WKT/contour operations; `CSTriangulation` project for Delaunay/Ruppert triangulation

## Key Domain Concepts

- **Material types**: `Concrete`, `ReSteelF` (physical yield), `ReSteelU` (conditional yield), `Steel`
- **Calculation types** (CalcType enum): C (continuous), CL (continuous long-term), N (temporary), NL (temporary long-term)
- **Diagram types**: L2 (bi-linear), L3 (tri-linear), SP63 (curvilinear per СП 63 appendix), Custom (user-defined σ(ε) points, editable via DiagramPage)
- **Calc task types**: `strain_state` (single НДС), `strain_state_batch` (batch, parallel execution)
- **Section types**: `CrossSection` (RC, fiber method), `TwoStageSection` (two-stage pour), `PlateSection` (plate/shell, FEM-based)
- **Strain plane**: `Basis` (three-point) or `Kurvature` (curvature-based)
- **Integration**: `MaterialArea.Integral()` computes N, My, Mz via fiber sums; `GreenIntegrator` provides Green's theorem contour integration (GL quadrature, no explicit mesh)
- **RC analysis**: `CrossSection` owns `MaterialArea` list + `ReBarGroup` collections
- **Load combinations**: `CScore/Combinations/` — SP20.13330 combinatorics (`Combinator`, `SP20Combinations`)

## Key Files

| File | Role |
|------|------|
| `OpenCS/AppViewModel.cs` | Main VM hub — owns all collections and `DatabaseService` instance |
| `OpenCS/MainWindow.xaml` | Main window with TreeView navigation |
| `OpenCS/Utilites/DatabaseService.cs` | SQLite CRUD, all table schemas, JSON serialization contracts |
| `OpenCS/Utilites/RelayCommand.cs` | ICommand implementation |
| `OpenCS/Utilites/ViewModelBase.cs` | INotifyPropertyChanged base class |
| `OpenCS/Services/` | LogService, FileDialogService, PlotService abstractions |
| `OpenCS/Tasks/TaskRunner.cs` | Calc task execution engine — dispatches handlers, manages status |
| `OpenCS/Tasks/StrainStateBatchHandler.cs` | Parallel batch strain state calculator |
| `OpenCS/ViewModels/SectionPlotVM.cs` | Fiber color map plot VM (σ and ε, diverging colormap) |
| `OpenCS/ViewModels/StrainSummaryVM.cs` | Detailed strain state summary (stiffness, extrema, rebar) |
| `OpenCS/ViewModels/StrainStateBatchVM.cs` | Batch НДС results VM — BatchRow list with IsConverged |
| `OpenCS/ViewModels/DiagramEditVM.cs` | Custom σ(ε) diagram editor VM — splines, CSV import |
| `OpenCS/Views/Helpers/FiberCanvas.cs` | WPF canvas — fiber mesh rendering, zoom/pan, HitTest tooltip |
| `OpenCS/Views/Helpers/DiagramCanvas.cs` | WPF canvas — σ(ε) diagram with pan/zoom/drag markers |
| `OpenCS/Views/Helpers/ColormapHelper.cs` | Diverging colormap blue↔white↔red for fiber visualization |
| `CScore/MaterialArea.cs` | Fiber mesh subdivision + stress/strain integration (N, My, Mz) |
| `CScore/CrossSection.cs` | RC cross-section: MaterialArea list + ReBarGroup collections |
| `CScore/TwoStageSection.cs` | Two-stage pour cross-section |
| `CScore/PlateSection.cs` | Plate/shell section domain model (ShellStrainState, ShellResult) |
| `CScore/GreenIntegrator.cs` | Green's theorem contour integration (GL quadrature, no mesh) |
| `CScore/Combinations/` | SP20.13330 load combinations (Combinator, SP20Combinations) |
| `CScore/Material.cs` / `MaterialChars.cs` | Material model with per-calc-type characteristics |
| `CScore/Diagramm.cs` | Stress-strain diagram generation (L2, L3, SP63, Custom) |
| `CScore/Geo.cs` | Polygon slicing, triangulation (CSTriangulation: Advancing Front & Ruppert) |
| `CScore/GeoProps.cs` | Geometric properties (area, moments of inertia, centroid) |
| `CSTriangulation/` | Triangulation library: AdvancingFront.cs, Ruppert/ (CDT, Mesh, Refine, Triangulator), Optimize.cs |
| `OpenCS/DataSource/` | Russian-language CSV files for material properties |

## Important NuGet Packages

- **OpenCS**: CsvHelper, Microsoft.Data.Sqlite, Microsoft.Xaml.Behaviors.Wpf, netDxf
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