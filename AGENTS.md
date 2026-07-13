# AGENTS.md

## Communication

Всегда отвечай пользователю на русском языке, если он явно не попросил другой язык.
Все размышления (thinking, chain-of-thought) также веди на русском языке.

OpenCS — WPF desktop app for computing reinforced concrete cross-sections per Russian building codes (СП 63.13330) using the fiber method.

## Два компьютера / локальный и удалённый Git (читать в начале сессии)

Разработчик работает с **двух ПК**. На каждом — **свой локальный клон**. Synology Drive (и любой файловый sync) **не** является источником правды для репозитория: из‑за этого уже «пропадали» фичи, когда истории разошлись, а файлы на диске выглядели нормально.

**Источник правды = `origin` (GitHub).** Код между машинами только через `git push` / `git fetch` + `git pull` (или checkout запушенной ветки).

### Чеклист агента при старте (до вывода «фичи нет» и до крупной работы)

1. `git fetch origin`
2. `git status -sb` и `git branch --show-current` — смотреть ahead/behind относительно `origin/<ветка>`
3. Если пользователь ждёт вчерашнюю работу с другого компа, а «её нет»:
   - сравнить `git log --oneline HEAD..origin/master` и `origin/master..HEAD`
   - проверить, не открыта ли **feature**-ветка без этих коммитов
   - **не** считать, что фичу удалили, пока не проверена развилка с remote
4. Сначала подтянуть remote: `git checkout master` → `git pull origin master` (или влить `origin/master` в текущую feature), если нужны «все свежие правки»

### Ритуал смены компа

| Когда | Действие |
|------|----------|
| Уход с ПК A | `git add` → `git commit` → `git push -u origin HEAD` (пушить рабочую ветку, даже WIP) |
| Старт на ПК B | `git fetch origin` → `git checkout <та же ветка>` → `git pull` |
| Долгий WIP | жить на `feature/...`, пушить часто; не копить недели только на локальном `master` без push |

### Можно / нельзя

- **Можно/нужно:** помнить, что «файлы на диске» ≠ «фича доступна» — без push на другом компе и на `origin` её нет.
- **Можно/нужно:** ждать расхождения local `master` и `origin/master` после локальных merge — чинить merge/pull, а не писать фичу заново.
- **Нельзя:** синхронизировать git-рабочую копию через Synology Drive; если Drive всё же используется — исключить `.git`, `bin/`, `obj/`, `.vs/`; лучше держать клон **вне** Drive.
- **Нельзя:** править одну и ту же незапушенную ветку параллельно на двух ПК.
- **Нельзя:** force-push в `master`, если пользователь явно не просил.

### PowerShell

```powershell
git fetch origin
git status -sb
git log --oneline -5 HEAD
git log --oneline -5 origin/master
git rev-list --left-right --count origin/master...HEAD   # behind...ahead
```

## Build & Run

```bash
dotnet build OpenCS.sln
dotnet run --project OpenCS
```

No CI/CD, no linters/formatters. Two testing conventions coexist: `CSfea.Tests` is a hand-rolled console harness (PASS/FAIL via `TestHarness.Check`, no external test-framework packages) covering CSfea/CScore FEA-adjacent logic; `CScore.Tests` is a standard xUnit project (`Microsoft.NET.Test.Sdk`, `xunit`, `xunit.runner.visualstudio`, `coverlet.collector`) for pure CScore domain logic — run with `dotnet test CScore.Tests`.

## Solution Structure

```
OpenCS.sln
├── CSmath/               Pure math (vectors, matrices, splines, geometry) — no external deps
├── CSTriangulation/      Triangulation algorithms (Advancing Front, Ruppert CDT) — no external deps
├── CScore/               Domain model & computation engine → depends on CSmath, CSTriangulation
├── CScore.Tests/         xUnit tests for CScore domain logic (pure, no WPF) → depends on CScore
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

`CSTriangulation` references **Triangle.dll** (managed Triangle.NET, `TriangleNet.*` namespace) — **temporarily reinstated** as the engine behind `CSTriangulation.Ruppert.Triangulator` because the custom CDT+Refine implementation ignored `maxAngl` always, and `maxTrgArea` when the region had holes. The DLL is vendored at `CSTriangulation/libs/Triangle/Triangle.dll` (and must also be referenced directly — via the same `HintPath` — in every exe-output project that transitively needs it at runtime: `OpenCS`, `CSfea.Tests`, `StrainTest`, `TriTest`; a raw `Reference` declared only in `CSTriangulation.csproj` is copied to consumers' `bin/` but isn't added to their `.deps.json`). `TriangulationMethod.AdvancingFront` is unaffected (still the custom CSTriangulation port). See `docs/superpowers/specs/2026-07-11-ruppert-trianglenet-design.md` for details; the old `CDT.cs`/`Mesh.cs`/`Refine.cs` files remain in the repo, unused, for a future fix.

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

## Sign Conventions (проверять перед любой задачей с моментами!)

- **`Mx = ∫σ·y·dA`, `My = ∫σ·x·dA`** (см. `Fiber.Mx`/`Fiber.My`). Следствие: **положительный `Mx` растягивает грань с `y > 0` (верхнюю)**, а не нижнюю; аналогично `My` растягивает грань с `x > 0`.
- Для сечения, армированного **снизу** (арматура при `y < 0`), момент, раскрывающий трещину у арматуры, — **отрицательный** `Mx`. Подавать в решатель положительный момент для такого сечения означает растяжение верхней (голой бетонной) грани — задача без решения при `ten=false` (нет арматуры, воспринимающей растяжение), и метод Ньютона будет казаться «расходящимся», хотя сам решатель корректен.
- **Перед тем как чинить «нестабильный» солвер деформаций/трещин — сначала проверь знак момента** (соответствует ли направление растяжения расположению арматуры/растянутой зоны) и не превышен ли предел несущей способности сечения. Инцидент 2026-07-13: несколько часов отладки демпфирования Ньютона, аналитического Якобиана и trust-region ушли на задачу, у которой изначально не было решения из-за неверного знака момента в тестовых данных.

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
- **CScore.Tests**: Microsoft.NET.Test.Sdk, xunit, xunit.runner.visualstudio, coverlet.collector
- **CSTriangulation**: none
- **CSmath**: none

## Specs & Plans

Все спеки (spec) и планы реализации (plan) размещаются в `docs/superpowers/`:
- `docs/superpowers/specs/` — спеки, дизайн-документы, спецификации фич
- `docs/superpowers/plans/` — планы реализации, пошаговые инструкции

Именование: `YYYY-MM-DD-<краткое-описание>.md` для планов, `YYYY-MM-DD-<краткое-описание>-design.md` для спек.

Не размещать спеки и планы в корне `docs/` или других ad-hoc директориях.

## Style & Conventions

- All domain classes and VMs have **Russian-language XML doc comments**
- UI labels, CSV column headers, material names use Russian
- **No hardcoded UI strings.** All user-visible text in XAML must use `DynamicResource` keys defined in `OpenCS/Resources/Strings.ru-RU.xaml` (and `Strings.en-US.xaml` for English). Localizable strings include labels, headers, tooltips, button text, and ComboBox item names. Never write Russian or English text directly in `.xaml` or `.cs` files if it will be shown to the user — always add a key to both resource dictionaries and reference it via `{DynamicResource KeyName}`.
- Global styles in `App.xaml`: `IconButton`, `IconButton25`, `AppFontSize` (13), implicit styles for MenuItem, Window, TreeView, TextBlock, TextBox, DataGridCell, DataGridColumnHeader
- No `.editorconfig`, no analyzers, no formatting config
- Entity IDs are `int` (SQLite auto-increment), assigned after INSERT via `last_insert_rowid()`
