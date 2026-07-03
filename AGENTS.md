# AGENTS.md

## Communication

Всегда отвечай пользователю на русском языке, если он явно не попросил другой язык.
Все размышления (thinking, chain-of-thought) также веди на русском языке.

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
├── CSmath/               Pure math (vectors, matrices, splines, geometry) — no external deps
├── CSTriangulation/      Triangulation algorithms (Advancing Front, Ruppert CDT) — no external deps
├── CScore/               Domain model & computation engine → depends on CSmath, CSTriangulation
├── CScore.Fire/          Fire resistance calculations → depends on CScore
├── CSfea.Sparse/         Sparse matrix library — no external deps
├── CSfea.Sparse.CSparse/ CSparse interop → depends on CSfea.Sparse
├── CSfea.Core/           FEA core engine → depends on CSfea.Sparse
├── CSfea.CScore/         FEA↔CScore bridge → depends on CSfea.Core, CScore
├── CSfea.Thermal/        Thermal FEA module → depends on CSfea.Core
├── CSfea.Tests/          Tests for FEA modules
├── OpenCS/               WPF entry point (net9.0-windows, UseWPF) → depends on CScore
├── StrainTest/           Strain calculation test project
└── TriTest/              Triangulation test project
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
- **FEA**: `CSfea.Sparse` (sparse matrices), `CSfea.Core` (FEA engine), `CSfea.CScore` (FEA↔CScore bridge), `CSfea.Thermal` (thermal analysis)
- **Fire**: `CScore.Fire` for fire resistance calculations

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
| `CSfea.Sparse/` | Sparse matrix library: CooMatrix, CscMatrix, CsrMatrix, solvers (Cholesky, LU, CG), Reverse Cuthill-McKee reordering |
| `CSfea.Core/` | FEA core: elements (Shell3, Shell4), mesh (ShellMesh), materials, sections, assembly, postprocess |
| `CSfea.CScore/` | FEA↔CScore bridge: CrossSectionBeamResponse, PlateSectionShellResponse, SectionBridgeFactory |
| `CSfea.Thermal/` | Thermal FEA: HeatAssembly, HeatMesh, HeatMeshQuadratic, thermal materials, solvers |
| `CScore.Fire/` | Fire resistance: FireRCheck, FireFiberSection, FireThermalService, FireCurves, FireMaterials |
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
