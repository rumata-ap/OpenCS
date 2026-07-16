# OpenSees spatial N-Mx-My Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (recommended) or `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Реализовать пространственную OpenSees-задачу `N-Mx-My`, задаваемую через выбранный `ForceSet`, с полярным result view и опциональной историей `M–κ`.

**Architecture:** Существующий 2D OpenSees pipeline не изменяется. Для 3D добавляются отдельные request/result/parser/generator/service-типы, использующие общий `OpenSeesSectionModel`, `OpenSeesProcessRunner` и `OpenSeesArtifactStore`. OpenCS task извлекает уникальные `N` из `CalcTask.ForceSetId`, а WPF result view группирует сохранённые точки по N и отображает полярный контур `Mx–My`.

**Tech Stack:** .NET 9, C#, WPF, existing `PlotCanvas`/`WpfPlotService`, `System.Text.Json`, xUnit, внешний Tcl/OpenSees backend.

Спецификации:

- `docs/superpowers/specs/2026-07-16-opensees-spatial-interaction-design.md`
- `docs/superpowers/specs/2026-07-16-opensees-spatial-interaction-ui-design.md`

---

## File map

### Create: OpenSees core

- `OpenCS.OpenSees/Analysis/SpatialSectionAnalysisRequest.cs` — одна внутренняя 3D-точка `(N, angle)` и вычисление двух компонент максимальной кривизны.
- `OpenCS.OpenSees/Analysis/SpatialSectionHistoryRow.cs` — одна строка совместной 3D-истории.
- `OpenCS.OpenSees/Analysis/SpatialSectionAnalysisResult.cs` — результат одного внешнего запуска.
- `OpenCS.OpenSees/Analysis/SectionSpatialInteractionRequest.cs` — список N, шаг угла и радиальные параметры.
- `OpenCS.OpenSees/Analysis/SectionSpatialInteractionPoint.cs` — одна точка поверхности, включая полную `HistoryRows`.
- `OpenCS.OpenSees/Analysis/SectionSpatialInteractionResult.cs` — итог поверхности и агрегированный статус.
- `OpenCS.OpenSees/Tcl/ISpatialSectionTclGenerator.cs` — контракт 3D Tcl generator.
- `OpenCS.OpenSees/Tcl/SpatialSectionTclGenerator.cs` — 3D zero-length fiber section script.
- `OpenCS.OpenSees/Results/SpatialSectionResultParser.cs` — строгий parser 3D history.
- `OpenCS.OpenSees/Services/ISpatialSectionAnalysisExecutor.cs` — контракт одного пространственного запуска.
- `OpenCS.OpenSees/Services/SpatialSectionAnalysisService.cs` — generate → artifact → runner → parser.
- `OpenCS.OpenSees/Services/SectionSpatialInteractionService.cs` — последовательный перебор N и углов.

### Modify: OpenCS task pipeline

- `OpenCS/Tasks/OpenSeesSpatialInteractionParams.cs` — JSON-параметры без дублирования N.
- `OpenCS/Tasks/OpenSeesSpatialInteractionHandler.cs` — ForceSet → unique N → adapter → spatial service.
- `OpenCS/Tasks/CalcTaskForceHelper.cs` — разрешить dummy `LoadItem` для нового kind.
- `OpenCS/Tasks/TaskRunner.cs` — зарегистрировать новый kind.
- `OpenCS/Views/CalcTaskPropsDialog.xaml.cs` — поля, preview N и сохранение/восстановление task.
- `OpenCS/Views/CalcTaskPropsDialog.xaml` — локализованный UI-панель параметров.
- `OpenCS/Views/CalcResultView.xaml.cs` — route нового kind.
- `OpenCS/Resources/Strings.ru-RU.xaml`, `OpenCS/Resources/Strings.en-US.xaml` — все новые подписи/ошибки.
- `OpenCS.OpenSees/README.md` — JSON, ForceSet semantics и result view.

### Create: OpenCS result view

- `OpenCS/ViewModels/OpenSeesSpatialInteractionResultVM.cs` — grouping, selection, formatting and redraw state.
- `OpenCS/Views/OpenSeesSpatialInteractionResultView.xaml` — tabs `Mx–My`, `M–κ`, `Диагностика`.
- `OpenCS/Views/OpenSeesSpatialInteractionResultView.xaml.cs` — bindings, `WpfPlotService`, equal-axis redraw.

### Test files

- `OpenCS.OpenSees.Tests/SpatialInteractionTests.cs`
- `OpenCS.OpenSees.Tests/SpatialTclGeneratorTests.cs`
- `OpenCS.OpenSees.Tests/SpatialSectionResultParserTests.cs`
- `OpenCS.OpenSees.Tests/SpatialSectionAnalysisServiceTests.cs`
- `OpenCS.OpenSees.Tests/OpenSeesSpatialTaskContractTests.cs`
- `OpenCS.OpenSees.Tests/OpenSeesSpatialInteractionResultVMTests.cs`
- `OpenCS.OpenSees.Tests/OpenSeesSpatialIntegrationTests.cs`

---

### Task 1: Add spatial analysis contracts with failing tests

**Files:** Create the six files under `OpenCS.OpenSees/Analysis/`; test `OpenCS.OpenSees.Tests/SpatialInteractionTests.cs`.

- [x] **Step 1: Write validation and geometry tests.**

```csharp
[Fact]
public void Request_generates_full_turn_without_duplicate_360()
{
    SectionSpatialInteractionRequest request = new()
    {
        AxialForcesN = [-100_000, 0, 100_000],
        AngleStepDegrees = 45,
        MaxCurvature = 0.01,
        Increments = 20
    };

    Assert.Equal(8, request.GenerateAnglesDegrees().Count);
    Assert.Equal(0, request.GenerateAnglesDegrees()[0]);
    Assert.Equal(315, request.GenerateAnglesDegrees()[^1]);
}

[Fact]
public void Spatial_point_maps_zero_and_ninety_degrees_to_Mx_and_My()
{
    var zero = SpatialSectionAnalysisRequest.At(0, 0, 0.01, 20);
    var ninety = SpatialSectionAnalysisRequest.At(0, 90, 0.01, 20);

    Assert.Equal(0.01, zero.CurvatureMxAtMax, 12);
    Assert.Equal(0, zero.CurvatureMyAtMax, 12);
    Assert.Equal(0, ninety.CurvatureMxAtMax, 12);
    Assert.Equal(0.01, ninety.CurvatureMyAtMax, 12);
}
```

- [x] **Step 2: Run the filtered tests and confirm the types are missing.**

Run: `dotnet test OpenCS.OpenSees.Tests/OpenCS.OpenSees.Tests.csproj --filter FullyQualifiedName~SpatialInteractionTests`

Expected: FAIL with missing spatial contract types.

- [x] **Step 3: Implement the contracts.**

`SectionSpatialInteractionRequest` must contain `IReadOnlyList<double> AxialForcesN`, `AngleStepDegrees`, `MaxCurvature`, `Increments`, `Convention`, `Validate()`, and `GenerateAnglesDegrees()`. `Validate()` rejects empty/non-finite/duplicate N, non-finite positive curvature, non-positive increments, and a positive finite angle step that does not divide 360 within a documented absolute tolerance. `GenerateAnglesDegrees()` returns `0, step, ..., 360-step` using deterministic rounding.

`SpatialSectionAnalysisRequest` stores `AxialForceN`, `AngleDegrees`, `MaxCurvature`, `Increments`, and `Convention`; it exposes `CurvatureMxAtMax = MaxCurvature * cos(angle)` and `CurvatureMyAtMax = MaxCurvature * sin(angle)`.

`SpatialSectionHistoryRow` must include `Step`, `LoadFactor`, `AxialForceN`, `MomentMxNm`, `MomentMyNm`, `CurvatureMx`, `CurvatureMy`, `CurvatureMagnitude`, `Converged`, `Residual`, `OpenSeesMzNm`, `OpenSeesMyNm`, `OpenSeesRotationY`, and `OpenSeesRotationZ`.

`SpatialSectionAnalysisResult` contains `Status`, `Rows`, `Diagnostics`, `ArtifactDirectory`, and `OpenSeesRunResult? RunResult`. `SectionSpatialInteractionPoint` contains `AxialForceN`, `AngleDegrees`, nullable terminal `MomentMxNm`/`MomentMyNm`/curvatures, `TerminalRow`, full `HistoryRows`, `Status`, `Diagnostics`, and `ArtifactDirectory`. `SectionSpatialInteractionResult` contains `Status`, ordered `Points`, and `Diagnostics`.

Every public type/property receives Russian XML documentation.

- [x] **Step 4: Run the tests.**

Run: `dotnet test OpenCS.OpenSees.Tests/OpenCS.OpenSees.Tests.csproj --filter FullyQualifiedName~SpatialInteractionTests`

Expected: all spatial contract tests PASS.

- [x] **Step 5: Commit.**

```powershell
git add OpenCS.OpenSees/Analysis OpenCS.OpenSees.Tests/SpatialInteractionTests.cs
git commit -m "feat(opensees): add spatial interaction contracts"
```

### Task 2: Add the strict 3D history parser

**Files:** Create `OpenCS.OpenSees/Results/SpatialSectionResultParser.cs`, `OpenCS.OpenSees.Tests/SpatialSectionResultParserTests.cs`.

- [x] **Step 1: Write parser tests.**

Cover a marker-backed invariant-culture file with 10 columns, comments, blank lines, `0/1` convergence flags, and exact mapping of `Mz → MomentMxNm`, `My → MomentMyNm`, `rotationZ → CurvatureMx`, `rotationY → CurvatureMy`. Add tests for missing history, missing marker, empty data, wrong column count, invalid number, invalid boolean and non-finite values.

- [x] **Step 2: Run the filtered parser tests.**

Run: `dotnet test OpenCS.OpenSees.Tests/OpenCS.OpenSees.Tests.csproj --filter FullyQualifiedName~SpatialSectionResultParserTests`

Expected: FAIL because the parser does not exist.

- [x] **Step 3: Implement the parser.**

Require these columns after comments are skipped:

```text
step loadFactor axialForceN openSeesMzNm openSeesMyNm rotationY rotationZ curvatureMagnitude converged residual
```

Parse every numeric value with `InvariantCulture`, require `double.IsFinite`, map the values to the named CScore components, and throw `OpenSeesResultException` with a stable code for every malformed input. Require at least one row and a non-empty `completed.marker`.

- [x] **Step 4: Run tests and commit.**

Run: `dotnet test OpenCS.OpenSees.Tests/OpenCS.OpenSees.Tests.csproj --filter FullyQualifiedName~SpatialSectionResultParserTests`

Expected: PASS.

```powershell
git add OpenCS.OpenSees/Results/SpatialSectionResultParser.cs OpenCS.OpenSees.Tests/SpatialSectionResultParserTests.cs
git commit -m "feat(opensees): parse spatial section histories"
```

### Task 3: Generate deterministic 3D Tcl

**Files:** Create `OpenCS.OpenSees/Tcl/ISpatialSectionTclGenerator.cs`, `OpenCS.OpenSees/Tcl/SpatialSectionTclGenerator.cs`, `OpenCS.OpenSees.Tests/SpatialTclGeneratorTests.cs`.

- [x] **Step 1: Write snapshot and culture tests.**

Build a two-fiber `OpenSeesSectionModel` and assert the generated script contains exactly:

```text
model basic -ndm 3 -ndf 6
fix 1 1 1 1 1 1 1
fix 2 0 1 1 1 0 0
zeroLengthSection
sp 2 5
sp 2 6
section_history.out
completed.marker
```

Assert fiber order, materials, `OpenSees Mz → CScore Mx`, `OpenSees My → CScore My`, invariant decimal formatting under a comma-decimal current culture, no absolute executable path in Tcl, and no 2D `-ndm 2 -ndf 3`.

- [x] **Step 2: Run the filtered tests and confirm the generator is missing.**

Run: `dotnet test OpenCS.OpenSees.Tests/OpenCS.OpenSees.Tests.csproj --filter FullyQualifiedName~SpatialTclGeneratorTests`

Expected: FAIL because the 3D generator does not exist.

- [x] **Step 3: Implement the generator.**

Use the existing material and fiber emission policy from `SectionMomentCurvatureTclGenerator`, but emit `model basic -ndm 3 -ndf 6`, 3D node/fix commands, a six-component axial load, and a zero-length section. Use DOF 5 for `CurvatureMy`/OpenSees `My` and DOF 6 for `CurvatureMx`/OpenSees `Mz`; use `LoadControl` with two proportional `sp` values so the common load factor goes from zero to one.

Write `section_history.out` with the 10-column schema from Task 2, record one row per radial increment, break on a non-zero `analyze` return, close all files, create `completed.marker`, and call `wipe`. Use `TclNumber.Format` for every numeric value and `InvariantCulture` for integer interpolation.

- [x] **Step 4: Run and commit.**

Run: `dotnet test OpenCS.OpenSees.Tests/OpenCS.OpenSees.Tests.csproj --filter FullyQualifiedName~SpatialTclGeneratorTests`

Expected: PASS.

```powershell
git add OpenCS.OpenSees/Tcl/ISpatialSectionTclGenerator.cs OpenCS.OpenSees/Tcl/SpatialSectionTclGenerator.cs OpenCS.OpenSees.Tests/SpatialTclGeneratorTests.cs
git commit -m "feat(opensees): generate 3D spatial section Tcl"
```

### Task 4: Add one-run spatial analysis service

**Files:** Create `OpenCS.OpenSees/Services/ISpatialSectionAnalysisExecutor.cs`, `OpenCS.OpenSees/Services/SpatialSectionAnalysisService.cs`, `OpenCS.OpenSees.Tests/SpatialSectionAnalysisServiceTests.cs`.

- [x] **Step 1: Write fake generator/runner tests.**

Assert call order `Generate → ArtifactStore.Create/WriteScript/WriteManifest → runner → parser → final manifest`, propagation of the `AngleDegrees` request, preservation of artifacts on non-zero exit, missing marker and parser errors, and cancellation before generation and during runner execution.

- [x] **Step 2: Run the filtered tests and confirm missing service types.**

Run: `dotnet test OpenCS.OpenSees.Tests/OpenCS.OpenSees.Tests.csproj --filter FullyQualifiedName~SpatialSectionAnalysisServiceTests`

Expected: FAIL because the spatial executor/service do not exist.

- [x] **Step 3: Implement the service.**

Mirror `SectionAnalysisService`’s artifact lifecycle, but inject the spatial generator and spatial parser. Return `error` for generation/setup/parser failures, `not_converged` for non-zero/timeout/cancelled/non-converged histories, and `ok` only when every parsed row is converged. Keep `ArtifactDirectory`, `RunResult`, all rows and diagnostics in the returned result.

- [x] **Step 4: Run and commit.**

Run: `dotnet test OpenCS.OpenSees.Tests/OpenCS.OpenSees.Tests.csproj --filter FullyQualifiedName~SpatialSectionAnalysisServiceTests`

Expected: PASS.

```powershell
git add OpenCS.OpenSees/Services/ISpatialSectionAnalysisExecutor.cs OpenCS.OpenSees/Services/SpatialSectionAnalysisService.cs OpenCS.OpenSees.Tests/SpatialSectionAnalysisServiceTests.cs
git commit -m "feat(opensees): add spatial section analysis service"
```

### Task 5: Orchestrate N and angle points

**Files:** Create `OpenCS.OpenSees/Services/SectionSpatialInteractionService.cs`; modify `OpenCS.OpenSees/Analysis/SectionSpatialInteractionPoint.cs`; extend `OpenCS.OpenSees.Tests/SpatialInteractionTests.cs`.

- [x] **Step 1: Add fake-executor orchestration tests.**

Use axial forces `[100_000, -200_000]` and `AngleStepDegrees = 90`; assert requests are `[100000/0, 100000/90, 100000/180, 100000/270, -200000/0, ...]`, all request angles are preserved, each point retains the complete `HistoryRows`, the terminal values come from `Rows.LastOrDefault(row => row.Converged)`, and every point retains its artifact directory.

Add tests for aggregate `error`, aggregate `not_converged`, cancellation between points and an exception from one fake run becoming an error point without corrupting the remaining order.

- [x] **Step 2: Implement the sequential service.**

Validate model/request/process request, loop in the exact order above, invoke `ISpatialSectionAnalysisExecutor.RunAsync`, copy all rows into `HistoryRows`, choose the last converged row, and aggregate statuses as `error > not_converged > ok`. Do not parallelize or reuse material state across points.

- [x] **Step 3: Run and commit.**

Run: `dotnet test OpenCS.OpenSees.Tests/OpenCS.OpenSees.Tests.csproj --filter FullyQualifiedName~SpatialInteractionTests`

Expected: PASS.

```powershell
git add OpenCS.OpenSees/Services/SectionSpatialInteractionService.cs OpenCS.OpenSees/Analysis/SectionSpatialInteractionPoint.cs OpenCS.OpenSees.Tests/SpatialInteractionTests.cs
git commit -m "feat(opensees): orchestrate spatial interaction points"
```

### Task 6: Connect the task to ForceSet and register it

**Files:** Create `OpenCS/Tasks/OpenSeesSpatialInteractionParams.cs`, `OpenCS/Tasks/OpenSeesSpatialInteractionHandler.cs`; modify `OpenCS/Tasks/CalcTaskForceHelper.cs`, `OpenCS/Tasks/TaskRunner.cs`; create `OpenCS.OpenSees.Tests/OpenSeesSpatialTaskContractTests.cs`.

- [x] **Step 1: Write task contract tests.**

Assert parsing of:

```json
{"angleStepDegrees":45,"maxCurvature":0.01,"increments":20,"timeoutSeconds":300,"executablePath":"C:/OpenSees.exe"}
```

Assert defaults, rejection of non-dividing angle steps and non-positive values, registration of `opensees_section_interaction_n_mx_my`, and `OpenSeesSpatialInteractionParams.ExtractAxialForcesKn(forceSet)` preserving first-seen unique N values. Assert `[-1000, 0, 1000]` kN become `[-1000000, 0, 1000000]` N exactly once.

- [x] **Step 2: Run the filtered tests and confirm missing task types.**

Run: `dotnet test OpenCS.OpenSees.Tests/OpenCS.OpenSees.Tests.csproj --filter FullyQualifiedName~OpenSeesSpatialTaskContractTests`

Expected: FAIL because the new params/handler/kind do not exist.

- [x] **Step 3: Implement params and ForceSet resolver.**

`OpenSeesSpatialInteractionParams.Parse` owns only `AngleStepDegrees`, `MaxCurvature`, `Increments`, `TimeoutSeconds`, and `ExecutablePath`. Add `OpenSeesSpatialInteractionParams.ExtractAxialForcesKn(ForceSet forceSet)` that selects `Items.Select(item => item.N)`, removes exact duplicates while preserving order, and rejects an empty/non-finite result. The handler finds `ctx.Database.ForceSets.Single(fs => fs.Id == task.ForceSetId && fs.Kind == "bar")` and calls this method.

- [x] **Step 4: Implement handler and dummy-force routing.**

Build the prepared model exactly as the existing interaction handler does, resolve executable, create `SectionSpatialInteractionRequest`, create `SpatialSectionAnalysisService` and `SectionSpatialInteractionService`, and serialize the typed result. Add the new kind to `CalcTaskForceHelper.UsesDummyForceItem` so `CalcTaskExecutor.TryResolve` permits `ForceItemId = 0`. Register the handler in `TaskRunner`.

- [x] **Step 5: Run and commit.**

Run: `dotnet test OpenCS.OpenSees.Tests/OpenCS.OpenSees.Tests.csproj --filter FullyQualifiedName~OpenSeesSpatialTaskContractTests`

Expected: PASS.

```powershell
git add OpenCS/Tasks/OpenSeesSpatialInteractionParams.cs OpenCS/Tasks/OpenSeesSpatialInteractionHandler.cs OpenCS/Tasks/CalcTaskForceHelper.cs OpenCS/Tasks/TaskRunner.cs OpenCS.OpenSees.Tests/OpenSeesSpatialTaskContractTests.cs
git commit -m "feat(opensees): add spatial interaction task"
```

### Task 7: Add the task-definition dialog panel

**Files:** Modify `OpenCS/Views/CalcTaskPropsDialog.xaml.cs`, `OpenCS/Views/CalcTaskPropsDialog.xaml`; modify both resource dictionaries; extend `OpenCS.OpenSees.Tests/OpenSeesSpatialTaskContractTests.cs` where serialization can be tested without WPF.

- [x] **Step 1: Add view-model properties and preview tests.**

Add `IsOpenSeesSpatialInteraction`, `OpenSeesAngleStepDegrees`, `OpenSeesMaxCurvature`, `OpenSeesIncrements`, `OpenSeesTimeoutSeconds`, `OpenSeesExecutablePath`, `OpenSeesForceSetNPreview`, and `OpenSeesSpatialPointCount`. The preview must derive from `SelectedForceSet.Items`, distinct N values in source order, and `360 / step`.

- [x] **Step 2: Load existing task values.**

When `existing.Kind == "opensees_section_interaction_n_mx_my"`, select `existing.ForceSetId`, parse `OpenSeesSpatialInteractionParams`, and format numeric fields with invariant `G6`. Set `ForceItemId` to zero in the view model and notify all visibility/preview properties when kind or ForceSet changes.

- [x] **Step 3: Add the localized XAML panel.**

Add one `StackPanel`/`GroupBox` controlled by `IsOpenSeesSpatialInteraction` with a bar `ForceSet` ComboBox, N preview, angle step, curvature, increments, timeout and executable path. Hide the ordinary `LoadItem` selector for this kind. Every `TextBlock`, `ToolTip`, header and unit uses a `DynamicResource` key present in both dictionaries.

- [x] **Step 4: Serialize on OK.**

Before creating `CalcTask`, require a selected section and bar ForceSet with at least one unique N. Parse all numeric fields, show localized warning and keep the dialog open on invalid values. Create:

```csharp
new CalcTask
{
    Kind = "opensees_section_interaction_n_mx_my",
    SectionId = SelectedSection.Id,
    ForceSetId = SelectedForceSet.Id,
    ForceItemId = 0,
    CalcType = SelectedCalcType,
    ParamsJson = new OpenSeesSpatialInteractionParams
    {
        AngleStepDegrees = angleStepDegrees,
        MaxCurvature = maxCurvature,
        Increments = increments,
        TimeoutSeconds = timeoutSeconds,
        ExecutablePath = string.IsNullOrWhiteSpace(executablePath) ? null : executablePath.Trim()
    }.ToJson()
}
```

- [x] **Step 5: Build the OpenCS project.**

Run: `dotnet build OpenCS/OpenCS.csproj --no-restore`

Expected: successful build with no new XAML/resource errors.

- [x] **Step 6: Commit.**

```powershell
git add OpenCS/Views/CalcTaskPropsDialog.xaml OpenCS/Views/CalcTaskPropsDialog.xaml.cs OpenCS/Resources/Strings.ru-RU.xaml OpenCS/Resources/Strings.en-US.xaml
git commit -m "feat(opensees): add spatial task dialog parameters"
```

### Task 8: Add typed result view model

**Files:** Create `OpenCS/ViewModels/OpenSeesSpatialInteractionResultVM.cs`, `OpenCS.OpenSees.Tests/OpenSeesSpatialInteractionResultVMTests.cs`.

- [x] **Step 1: Write grouping/selection tests.**

Deserialize a result with two N groups and four angles. Assert `AvailableAxialForces` keeps result order, selecting N exposes only its points, selecting an angle updates the selected point, changing N preserves the angle when available and falls back to the first angle otherwise, and `HistoryRows` drives the M–κ series.

- [x] **Step 2: Implement the VM.**

Expose `AvailableAxialForces`, `SelectedAxialForce`, `AvailableAngles`, `SelectedAngle`, `SelectedPoint`, `PolarMxKnM`, `PolarMyKnM`, `HistoryCurvatureMx`, `HistoryCurvatureMy`, `HistoryMomentMxKnM`, `HistoryMomentMyKnM`, `PointRows`, `Diagnostics`, `StatusText`, and redraw/fit commands. Convert N from N to kN and moments from N·m to kN·m only for display; keep model values in SI.

- [x] **Step 3: Handle empty/error/partial results.**

Expose an empty-state message when JSON contains an error or no points, mark non-converged points in the table, and keep the terminal values null when no history row converged.

- [x] **Step 4: Run the VM/grouping tests and commit.**

Run: `dotnet test OpenCS.OpenSees.Tests/OpenCS.OpenSees.Tests.csproj --filter FullyQualifiedName~OpenSeesSpatialInteractionResultVMTests`

Expected: PASS.

```powershell
git add OpenCS/ViewModels/OpenSeesSpatialInteractionResultVM.cs OpenCS.OpenSees.Tests
git commit -m "feat(opensees): prepare spatial interaction result VM"
```

### Task 9: Add polar and optional M–κ result tabs

**Files:** Create `OpenCS/Views/OpenSeesSpatialInteractionResultView.xaml`, `OpenCS/Views/OpenSeesSpatialInteractionResultView.xaml.cs`; modify `OpenCS/Views/CalcResultView.xaml.cs`; add localized keys to both resource dictionaries.

- [x] **Step 1: Add the result route contract test.**

Assert that the new kind is handled before the generic fallback in `CalcResultView` and that the view is constructed with `new OpenSeesSpatialInteractionResultView(result)`.

- [x] **Step 2: Create the XAML layout.**

Use a `TabControl` with localized headers for `Mx–My`, `M–κ`, and diagnostics. The first tab contains a `ComboBox` for N, a square `PlotCanvas`, an angle selector, a selected-point summary and a `DataGrid`. The second tab contains a `PlotCanvas` and the selected history table. The third tab contains status, diagnostics and artifact directory text.

- [x] **Step 3: Bind the polar plot.**

Instantiate `WpfPlotService` for the polar `PlotCanvas`, call `EnableSquareAxes()`, draw the selected group in kN·m, add markers for all angles, add a highlighted marker for the selected angle, and set localized Mx/My labels. Redraw on N/angle/VM changes and keep equal X/Y scale.

- [x] **Step 4: Bind the optional M–κ plot.**

Instantiate a second `WpfPlotService`; draw `HistoryCurvatureMx`/`HistoryMomentMxKnM` and `HistoryCurvatureMy`/`HistoryMomentMyKnM` as separate lines. The tab must remain empty-state-safe for a point without `HistoryRows` and must never trigger a recalculation.

- [x] **Step 5: Route and build.**

Add:

```csharp
if (task?.Kind == "opensees_section_interaction_n_mx_my")
{
    Content = new OpenSeesSpatialInteractionResultView(result);
    return;
}
```

Run: `dotnet build OpenCS/OpenCS.csproj --no-restore`

Expected: successful WPF build.

- [x] **Step 6: Commit.**

```powershell
git add OpenCS/Views/OpenSeesSpatialInteractionResultView.xaml OpenCS/Views/OpenSeesSpatialInteractionResultView.xaml.cs OpenCS/Views/CalcResultView.xaml.cs OpenCS/Resources/Strings.ru-RU.xaml OpenCS/Resources/Strings.en-US.xaml
git commit -m "feat(opensees): add spatial interaction result view"
```

### Task 10: Add real opt-in spatial integration coverage

**Files:** Create `OpenCS.OpenSees.Tests/OpenSeesSpatialIntegrationTests.cs`; modify `OpenCS.OpenSees.Tests/Fixtures/CrossSectionFixtures.cs` to expose the reusable symmetric elastic section fixture used by both 2D and 3D integration tests.

- [x] **Step 1: Add the opt-in test.**

Resolve `OPENSEES_EXE` with the existing skip helper. Run a symmetric elastic rectangular section at `N = 0` and angles `0, 90, 180, 270` with two radial increments. Assert status `ok`, four unique artifact directories, completion markers, non-empty histories, positive `Mx` at positive `CurvatureMx`, positive `My` at positive `CurvatureMy`, and opposite signs at 180/270.

- [x] **Step 2: Run the integration test.**

Run: `$env:OPENSEES_EXE='C:\Tools\OpenSees\bin\OpenSees.exe'; dotnet test OpenCS.OpenSees.Tests/OpenCS.OpenSees.Tests.csproj --filter FullyQualifiedName~OpenSeesSpatialIntegrationTests`

Expected: PASS with a compatible executable, otherwise SKIPPED by the existing opt-in helper.

- [x] **Step 3: Commit.**

```powershell
git add OpenCS.OpenSees.Tests/OpenSeesSpatialIntegrationTests.cs OpenCS.OpenSees.Tests/Fixtures/CrossSectionFixtures.cs
git commit -m "test(opensees): cover spatial interaction integration"
```

### Task 11: Update documentation and run the full verification

**Files:** Modify `OpenCS.OpenSees/README.md`; update both OpenSees specs/plans only to mark completed checkboxes after verification.

- [ ] **Step 1: Document the user workflow.**

Document selecting a bar `ForceSet`, unique N extraction, `angleStepDegrees`, the JSON stored in `ParamsJson`, the polar result tab, optional M–κ tab, status aggregation and artifact navigation. State explicitly that `Mx/My` values from ForceSet rows are ignored and that all OpenSees runs are sequential and independent.

- [ ] **Step 2: Run focused tests.**

```powershell
dotnet test OpenCS.OpenSees.Tests/OpenCS.OpenSees.Tests.csproj
```

Expected: all pure tests PASS; real OpenSees tests PASS or SKIP only when `OPENSEES_EXE` is absent.

- [ ] **Step 3: Run domain tests and solution build.**

```powershell
dotnet test CScore.Tests/CScore.Tests.csproj
dotnet build OpenCS.sln
```

Expected: PASS/build success with only documented pre-existing warnings.

- [ ] **Step 4: Perform manual WPF verification.**

Create a task from `CalcTaskPropsDialog`, select a ForceSet with repeated N values, verify the preview deduplicates N and counts directions, save/edit the task, run it with OpenSees, open the result, switch N, select an angle row, open `M–κ`, inspect diagnostics and verify artifact directories.

- [ ] **Step 5: Inspect generated artifacts.**

Open one `script.tcl`, `manifest.json`, `section_history.out`, and serialized result. Verify `-ndm 3 -ndf 6`, DOF 5/6 mapping, SI values, invariant decimals, no executable path interpolation, full `HistoryRows`, and unique artifacts.

- [ ] **Step 6: Commit documentation and plan completion.**

```powershell
git add OpenCS.OpenSees/README.md docs/superpowers/specs docs/superpowers/plans/2026-07-16-opensees-spatial-interaction.md
git commit -m "docs(opensees): document spatial interaction workflow"
```

## Plan self-review

- Backend spec coverage: contracts Task 1; parser Task 2; Tcl Task 3; one-run lifecycle Task 4; sequential N/angle orchestration Task 5; ForceSet/task registration Task 6.
- UI spec coverage: dialog fields and ForceSet preview Task 7; typed selection VM Task 8; polar/M–κ/diagnostic tabs and route Task 9.
- Verification coverage: opt-in OpenSees Task 10; README, full tests, WPF manual path and artifact inspection Task 11.
- No target-force, 3D surface renderer, manual N list, parallel batch or unrelated refactor is included.
