# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

OpenCS is a WPF desktop application for computing reinforced concrete (RC) cross-sections per Russian building codes (СП 63.13330). It uses the fiber method — cross-sections are subdivided into small finite areas (fibers), and stress/strain is computed at each fiber using material diagrams.

## Build & Run

```bash
dotnet build OpenCS.sln
dotnet run --project OpenCS
```

No CI/CD, no linters/formatters. Two testing conventions coexist: `CSfea.Tests` is a hand-rolled console harness (PASS/FAIL via `TestHarness.Check`, no external test-framework packages) covering CSfea/CScore FEA-adjacent logic; `CScore.Tests` is a standard xUnit project (`Microsoft.NET.Test.Sdk`, `xunit`, `xunit.runner.visualstudio`, `coverlet.collector`) for pure CScore domain logic — run with `dotnet test CScore.Tests`.

## Shell

Developer is always on Windows. **Use the PowerShell tool, not Bash**, for all commands (git, dotnet, output filtering). Use PowerShell syntax: `Select-String` instead of `grep`, `2>$null` instead of `2>/dev/null`, `$env:VAR`, etc. The Bash tool resolves to a POSIX `/usr/bin/bash` that lacks PowerShell cmdlets, so `... | Select-String ...` fails with `command not found`.

**Do not ask permission to run PowerShell commands** — run them directly without prompting. This includes build/test/git/output-filtering commands such as `dotnet build OpenCS.sln 2>$null | Select-String -Pattern "error|Ошибок|Предупреждений|успешно"`.

**`dotnet build` always runs without confirmation** — never ask before running any `dotnet build *` command. It is safe, read-only from a project state perspective, and is the only verification step in this project.

## Two computers / local vs remote Git (обязательно читать в начале сессии)

Developer works on **two PCs**. Each machine has its **own local git clone**. Synology Drive (or any file sync) must **not** be the source of truth for this repo — it previously caused “vanished” features when histories diverged while files looked present.

**Source of truth = `origin` (GitHub).** Code moves between machines only via `git push` / `git fetch` + `git pull` (or checkout of a pushed branch).

### Agent startup checklist (before assuming code is missing or starting large work)

1. `git fetch origin`
2. `git status -sb` and `git branch --show-current` — note ahead/behind vs `origin/<branch>`
3. If the user expects yesterday’s work from another PC and it is “not here”:
   - compare `git log --oneline HEAD..origin/master` and `origin/master..HEAD`
   - check whether the current branch is a **feature** branch that never got those commits
   - **do not** conclude the feature was deleted until remote and branch divergence are checked
4. Prefer integrating remote first: `git checkout master` → `git pull origin master` (or merge `origin/master` into the active feature branch) when the user wants “all recent work”

### Daily workflow (tell / follow when switching PCs)

| When | Action |
|------|--------|
| Leaving PC A | `git add` → `git commit` → `git push -u origin HEAD` (push the branch you worked on, even if WIP) |
| Starting PC B | `git fetch origin` → `git checkout <same-branch>` → `git pull` |
| Long unfinished work | stay on `feature/...`, push often; avoid weeks of unpushed commits only on local `master` |

### Do / Don’t

- **Do** treat “files on disk” ≠ “feature available”: without a push, the other PC (and `origin`) do not have it.
- **Do** expect local `master` and `origin/master` to diverge if someone merged only locally — reconcile with merge/pull, not by re-implementing.
- **Don’t** use Synology Drive (or similar) to sync the git working tree between PCs; if Drive is used at all, exclude `.git`, `bin/`, `obj/`, `.vs/` — better: keep the clone **outside** Drive entirely.
- **Don’t** edit the same unpushed branch on both PCs in parallel.
- **Don’t** force-push `master` unless the user explicitly asks.

### PowerShell examples

```powershell
git fetch origin
git status -sb
git log --oneline -5 HEAD
git log --oneline -5 origin/master
git rev-list --left-right --count origin/master...HEAD   # behind...ahead
```

## Solution Structure

```
OpenCS.sln
├── CSmath/            Pure math (vectors, matrices, splines, geometry) — no external deps
├── CScore/            Domain model & computation engine → depends on CSmath, CSTriangulation
├── CScore.Tests/      xUnit tests for CScore domain logic (pure, no WPF) → depends on CScore
├── CSTriangulation/   Triangulation algorithms (Advancing Front, Ruppert CDT) — no external deps
└── OpenCS/            WPF entry point (net9.0-windows, UseWPF) → depends on CScore
```

**Dependency order matters**: always build/change CSmath first, then CSTriangulation, then CScore, then OpenCS. `dotnet build` on the solution handles order automatically.

## External Dependency

`CSTriangulation` references **Triangle.dll** (managed Triangle.NET, `TriangleNet.*` namespace) — **temporarily reinstated** as the engine behind `CSTriangulation.Ruppert.Triangulator` because the custom CDT+Refine implementation ignored `maxAngl` always, and `maxTrgArea` when the region had holes. The DLL is vendored at `CSTriangulation/libs/Triangle/Triangle.dll` (and must also be referenced directly — via the same `HintPath` — in every exe-output project that transitively needs it at runtime: `OpenCS`, `CSfea.Tests`, `StrainTest`, `TriTest`; a raw `Reference` declared only in `CSTriangulation.csproj` is copied to consumers' `bin/` but isn't added to their `.deps.json`). `TriangulationMethod.AdvancingFront` is unaffected (still the custom CSTriangulation port). See `docs/superpowers/specs/2026-07-11-ruppert-trianglenet-design.md` for details; the old `CDT.cs`/`Mesh.cs`/`Refine.cs` files remain in the repo, unused, for a future fix.

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

## Sign Conventions (проверять перед любой задачей с моментами!)

- **`Mx = ∫σ·y·dA`, `My = ∫σ·x·dA`** (см. `Fiber.Mx`/`Fiber.My`). Следствие: **положительный `Mx` растягивает грань с `y > 0` (верхнюю)**, а не нижнюю; аналогично `My` растягивает грань с `x > 0`.
- Для сечения, армированного **снизу** (арматура при `y < 0`), момент, раскрывающий трещину у арматуры, — **отрицательный** `Mx`. Подавать в решатель положительный момент для такого сечения означает растяжение верхней (голой бетонной) грани — задача без решения при `ten=false` (нет арматуры, воспринимающей растяжение), и метод Ньютона будет казаться «расходящимся», хотя сам решатель корректен.
- **Перед тем как чинить «нестабильный» солвер деформаций/трещин — сначала проверь знак момента** (соответствует ли направление растяжения расположению арматуры/растянутой зоны) и не превышен ли предел несущей способности сечения. Инцидент 2026-07-13: несколько часов отладки демпфирования Ньютона, аналитического Якобиана и trust-region ушли на задачу, у которой изначально не было решения из-за неверного знака момента в тестовых данных. Подробности: `docs/superpowers/specs/` история недоступна вне репо — см. заметку в Obsidian «OpenCS — расчёт трещин РЕШЕНО».

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
- **CScore.Tests**: Microsoft.NET.Test.Sdk, xunit, xunit.runner.visualstudio, coverlet.collector
- **CSTriangulation**: none
- **CSmath**: none

## Style & Conventions

- All domain classes and VMs have **Russian-language XML doc comments**
- UI labels, CSV column headers, material names use Russian
- **No hardcoded UI strings.** All user-visible text in XAML must use `DynamicResource` keys defined in `OpenCS/Resources/Strings.ru-RU.xaml` (and `Strings.en-US.xaml` for English). Localizable strings include labels, headers, tooltips, button text, and ComboBox item names. Never write Russian or English text directly in `.xaml` or `.cs` files if it will be shown to the user — always add a key to both resource dictionaries and reference it via `{DynamicResource KeyName}`.
- Global styles in `App.xaml`: `IconButton`, `IconButton25`, `AppFontSize` (13), implicit styles for MenuItem, Window, TreeView, TextBlock, TextBox, DataGridCell, DataGridColumnHeader
- No `.editorconfig`, no analyzers, no formatting config
- Entity IDs are `int` (SQLite auto-increment), assigned after INSERT via `last_insert_rowid()`