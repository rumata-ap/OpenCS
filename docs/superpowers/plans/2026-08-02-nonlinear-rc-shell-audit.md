# Nonlinear RC-shell audit: provenance, equilibrium, regularization и mesh sensitivity — Implementation Plan

> **Для agentic workers:** REQUIRED SUB-SKILL: Use superpawers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Закрыть технические пробелы nonlinear RC-shell pipeline на базе OpenSees: передача угла армирования, однозначный material/layer provenance, capability-описания optional native responses, strict recording validation, проверка равновесия сил/моментов/реакций/resultants/energy, явный regularization contract и воспроизводимый coarse/medium/fine mesh sensitivity study — без WPF, SQLite и UI.

**Architecture:** Два слоя. Первый — изменения существующего pipeline: `PlateSectionOpenSeesMapper` передаёт нормализованный угол слоя и кладёт `Angle`/`Face` в fingerprint; `NativeShellMaterialSpec` объявляет capability descriptors; `ShellTclGenerator` эмитит `state_order.json` v2 с identity metadata (группы по `(sectionTag, integrationPoint, layerIndex, responseKind)`) и только валидные recorder groups; `ShellOpenSeesModel.Validate()` строго проверяет recording policy против конкретной topology; `ShellStateParser` читает v2 (обязательные metadata) и v1 только как legacy c `state_catalog_provenance_missing`. Второй — чистый audit layer в `OpenCS.OpenSees/Audit/` без WPF/SQLite/Gmsh: structured diagnostics, `ShellAuditPolicy`/preflight, generalized resultants (`r × F + M`), staged equilibrium, energy confidence, regularization policy и capability adapter, `IShellAnalysisRunner` (обёртка generator/artifact store/process runner/parser — audit никогда не доверяет только `Status`), sensitivity case factory/runner/report.

**Tech Stack:** .NET 9, C#, xUnit (`OpenCS.OpenSees.Tests`, `CScore.Tests`), OpenSees 3.8.0 (`C:\Tools\OpenSees\bin\OpenSees.exe`), Gmsh 4.15.2 (`C:\Tools\gmsh-4.15.2-Windows64\gmsh.exe`, только opt-in smoke), raw recorder files, `System.Text.Json`, `TclNumber.Format`.

---

## Baseline (подтверждён на текущем состоянии ветки)

- `dotnet build OpenCS.sln` — сборка успешна, 0 ошибок (на ветке возможны 2 известных MSB9008 warning про отсутствующий `OpenCS.Core.UI` — baseline, срезу не атрибутируются).
- `dotnet test CScore.Tests/CScore.Tests.csproj` — 425 passed, 1 skipped, всего 426.
- `dotnet test OpenCS.Gmsh.Tests/OpenCS.Gmsh.Tests.csproj` — 33 passed, 0 skipped.
- `OpenCS.OpenSees.Tests` — полный параллельный прогон содержит известный flaky pre-existing SQLite cleanup race (isolated-запуски проходят); срезу не атрибутируется.
- На машине разработчика присутствуют `C:\Tools\OpenSees\bin\OpenSees.exe` и `C:\Tools\gmsh-4.15.2-Windows64\gmsh.exe` — реальные integration tests будут **выполняться**, а не только компилироваться. При отсутствии executable они скипаются через `OpenSeesTestExecutable.ResolveOrSkip()`.

## File Structure Map (карта ответственности)

### Создать (все в `OpenCS.OpenSees/`)

| Файл | Ответственность |
|---|---|
| `Audit/ShellDiagnostics.cs` | `ShellDiagnosticSeverity`, `ShellDiagnostic` (code/severity/message/element/IP/layer/artifact/fingerprint), константы стабильных кодов (§13). |
| `Audit/ShellAuditPolicy.cs` | `ShellAuditMode`, `ShellAuditVerdict`, `ShellAuditPolicy` (tolerances, required responses, energy confidence, sensitivity, fingerprint). |
| `Audit/ShellRegularizationPolicy.cs` | `ShellRegularizationMode`, `ShellCharacteristicLengthMethod`, `ShellRegularizationPolicy`. |
| `Audit/IShellRegularizedMaterialAdapter.cs` | Контракт adapter-а, фактически применяющего regularization в native mapping. |
| `Audit/ShellRegularizationCapability.cs` | Registry adapter-ов, `CanApply(mode)`, `CanApplyTo(mode, spec)`; по умолчанию пуст. |
| `Audit/ShellAuditPreflight.cs` | `ShellAuditPreflightResult` (IsCalculable + diagnostics), preflight без запуска OpenSees. |
| `Audit/ShellResultants.cs` | `ShellResultant` (6 компонент), `ShellResultantMath.NodalForce` (`r × F + M`). |
| `Audit/ShellEquilibriumAuditor.cs` | Staged восстановление `P(step)`, reaction resultant, residual, `ShellEquilibriumStepReport`. |
| `Audit/ShellEnergyAuditor.cs` | `ShellEnergyConfidence` (NativeResponse/StateIntegral/ExternalWorkOnly/Unavailable), external work по трапециям, kinematic reaction work. |
| `Audit/ShellCharacteristicLength.cs` | `sqrt(area)` для Q4/T3, `ShellElementCharacteristicLength`. |
| `Audit/ShellAuditReport.cs` | Типизированный `ShellAuditReport` (verdict, equilibrium, energy, regularization, sensitivity, diagnostics). |
| `Audit/IShellAnalysisRunner.cs` | `ShellAnalysisRunResult`, `IShellAnalysisRunner.RunAsync` (генерация→запуск→парсинг, никогда не верит только `Status`). |
| `Audit/ShellAnalysisRunner.cs` | Реализация обёртки над generator/artifact store/`IOpenSeesProcessRunner`/`ShellResultParser`. |
| `Audit/ShellSensitivity.cs` | `ShellSensitivityLevel`, `ShellSensitivityCase`, `IShellSensitivityCaseFactory`. |
| `Audit/ShellSensitivityRunner.cs` | Три уровня, сравнение метрик, verdict rules, проверка различных fingerprints. |
| `Audit/ShellMeshSensitivityReport.cs` | `ShellMeshSensitivityReport`, `ShellSensitivityCaseReport`. |

### Изменить

| Файл | Изменение |
|---|---|
| `CScore/PlateRebar/PlateRebarLayoutFingerprint.cs` | Без изменений кода (уже содержит Face/Asx/Asy/Zsx/Zsy/Angle/MaterialId) — добавляются тесты, доказывающие влияние Angle и Face. |
| `OpenCS.OpenSees.CScore/PlateSectionOpenSeesMapper.cs` | `Asx → normalize(Angle)`, `Asy → normalize(Angle + 90°)`, блокировка нефинитного угла (`rebar_angle_invalid`), `Angle`+`Face` в source fingerprint. |
| `OpenCS.OpenSees/Model/NativeShellMaterialSpec.cs` | `NativeResponseCapability`, `NativeResponseConjugatePair`, `Capabilities`, `HasResponse`. |
| `OpenCS.OpenSees/Structural/ShellMaterialState.cs` | `ShellLayerStateGroup` metadata (SectionTag/MaterialTag/LayerKind/SourceId/CenterZ/Thickness/SectionFingerprint/Unit), `ShellStateCatalogProvenanceKind`, `RCShellLayerState.CatalogGroup`. |
| `OpenCS.OpenSees/Results/ShellStateParser.cs` | v2 (обязательные metadata, group identity по 4 полям, optional response groups), v1 legacy (`state_catalog_provenance_missing`, без фолбэков `materialTag=1`/`Concrete`). |
| `OpenCS.OpenSees/Tcl/ShellTclGenerator.cs` | v2 `state_order.json`, группировка recorder-групп по секциям, optional responses только при поддержке (`unsupported_shell_response`), удаление silent filtering (`recording_selection_invalid`). |
| `OpenCS.OpenSees/Structural/ShellOpenSeesModel.cs` | `ValidateRecordingPolicy` — explicit IP/fiber существуют у каждого элемента/секции области; null = все; mixed topology допустима только при null policy. |
| `OpenCS.OpenSees/Structural/ShellResults.cs` | Без изменений (совместимо: `ProvenanceKind` вычисляется из `Version`). |

### Тесты

| Файл | Ответственность |
|---|---|
| `CScore.Tests/PlateRebar/PlateRebarLayoutFingerprintTests.cs` | Angle/Face меняют layout fingerprint. |
| `OpenCS.OpenSees.Tests/PlateSectionOpenSeesMapperTests.cs` | Углы 45/135, нормализация, `rebar_angle_invalid`, fingerprint. |
| `OpenCS.OpenSees.Tests/NativeShellMaterialSpecTests.cs` | Capability descriptors, отсутствие fake crack/damage/energy. |
| `OpenCS.OpenSees.Tests/ShellStateParserTests.cs` | v2 parse, legacy v1, `state_catalog_provenance_missing`, отсутствие defaults. |
| `OpenCS.OpenSees.Tests/ShellTclGeneratorTests.cs` | v2 JSON, разделение групп по секциям, optional response behavior. |
| `OpenCS.OpenSees.Tests/ShellOpenSeesModelTests.cs` | Strict recording policy validation. |
| `OpenCS.OpenSees.Tests/Audit/ShellAuditPolicyTests.cs` | Diagnostics, policy defaults, preflight verdicts (строгий/diagnostic-only, v1 catalog). |
| `OpenCS.OpenSees.Tests/Audit/ShellEquilibriumAuditorTests.cs` | Staged `P(step)`, `r × F + M`, residual, pass/fail. |
| `OpenCS.OpenSees.Tests/Audit/ShellEnergyAuditorTests.cs` | Confidence modes, external work, kinematic work. |
| `OpenCS.OpenSees.Tests/Audit/ShellRegularizationTests.cs` | Characteristic length Q4/T3, `regularization_unsupported` (Strict Blocked / DiagnosticOnly Warning), fake adapter. |
| `OpenCS.OpenSees.Tests/Audit/ShellAnalysisRunnerTests.cs` | Успех/не-сходимость/ошибка парсинга; runner report, не доверяющий только `Status`. |
| `OpenCS.OpenSees.Tests/Audit/ShellSensitivityRunnerTests.cs` | Verdict rules на deterministic in-memory factory, distinct fingerprints. |
| `OpenCS.OpenSees.Tests/Audit/ShellAuditOpenSeesIntegrationTests.cs` | Реальные OpenSees: angle 45°, Q4/T3/mixed equilibrium, shell-beam junction, catalog v2 metadata, unsupported regularization, mesh sensitivity smoke (Gmsh при наличии, иначе prebuilt). |

## Dependency Order

1. Task 1 (angle/fingerprint) — независим, меняет mapper + добавляет CScore.Tests.
2. Task 2 (capability descriptors) — независим, база для Task 4 и Task 6.
3. Task 3 (provenance типы + parser v2) — база для Task 4 (generator v2).
4. Task 4 (generator v2) — зависит от Task 2 и Task 3.
5. Task 5 (strict recording validation) — зависит от Task 4 (generator-контракт), меняет `ShellOpenSeesModel`.
6. Task 6 (audit policy/preflight/regularization policy) — зависит от Task 2 (capabilities), Task 3 (catalog provenance).
7. Task 7 (resultants + equilibrium) — независим от аудита; база для Task 8, 11, 12.
8. Task 8 (energy) — зависит от Task 3 (catalog) и Task 2 (conjugate pairs).
9. Task 9 (characteristic length + regularization capability) — зависит от Task 6 (policy types).
10. Task 10 (`IShellAnalysisRunner`) — зависит от Task 4 (generator), Task 6 (diagnostics).
11. Task 11 (sensitivity) — зависит от Task 7 (equilibrium residual) и Task 10 (runner).
12. Task 12 (real OpenSees integration) — зависит от Task 1, 3, 4, 7, 9, 10, 11.
13. Task 13 (финальная регрессия) — последний.

## Regression Strategy

- Каждая задача коммитится отдельно; `dotnet build OpenCS.sln` и затронутый тест-проект прогоняются внутри каждого шага.
- Существующие тесты, которые намеренно меняют контракт: `ShellStateParserTests.ParseShellLayers_MapsStressAndStrainRowsToRequestedStep` (переводится на v2) и `ShellTclGeneratorTests.Generate_EmitsShellLayerStressAndStrainRecordersWithoutQ4T3Collisions` (имена файлов v2). Все остальные существующие тесты обязаны остаться зелёными без правок — они покрывают обратную совместимость (v1 legacy, `ShellResults` без изменений).
- Интеграционные OpenSees-тесты используют `OpenSeesTestExecutable.ResolveOrSkip()` — при отсутствии executable скипаются, а не падают.
- Известные baseline issues (2 MSB9008, 1 COM skip в CScore.Tests, flaky SQLite cleanup race в полном параллельном `OpenCS.OpenSees.Tests`) не атрибутируются срезу.

---

## Task 1: Направление армирования Angle в mapper и fingerprint

**Files:**
- Modify: `OpenCS.OpenSees.CScore/PlateSectionOpenSeesMapper.cs`
- Test: `OpenCS.OpenSees.Tests/PlateSectionOpenSeesMapperTests.cs`
- Test: `CScore.Tests/PlateRebar/PlateRebarLayoutFingerprintTests.cs`

- [ ] **Step 1: Write the failing tests**

В `CScore.Tests/PlateRebar/PlateRebarLayoutFingerprintTests.cs` добавить:

```csharp
[Fact]
public void Compute_DifferentAngle_ProducesDifferentFingerprint()
{
    var a = new List<PlateRebarLayer> { new() { Asx = 0.001, Zsx = 0.05, Angle = 0.0 } };
    var b = new List<PlateRebarLayer> { new() { Asx = 0.001, Zsx = 0.05, Angle = 30.0 } };

    Assert.NotEqual(PlateRebarLayoutFingerprint.Compute(a), PlateRebarLayoutFingerprint.Compute(b));
}

[Fact]
public void Compute_DifferentFace_ProducesDifferentFingerprint()
{
    var a = new List<PlateRebarLayer> { new() { Asx = 0.001, Zsx = 0.05, Face = RebarFace.PlusN } };
    var b = new List<PlateRebarLayer> { new() { Asx = 0.001, Zsx = 0.05, Face = RebarFace.MinusN } };

    Assert.NotEqual(PlateRebarLayoutFingerprint.Compute(a), PlateRebarLayoutFingerprint.Compute(b));
}
```

В `OpenCS.OpenSees.Tests/PlateSectionOpenSeesMapperTests.cs` добавить (в конец класса, перед `private static` helper):

```csharp
[Fact]
public void Map_RebarAngle45_MapsXAt45AndYAt135Degrees()
{
    var section = new PlateSection
    {
        H = 0.2,
        NLayers = 2,
        RebarLayers = [
            new PlateRebarLayer { Asx = 0.001, Asy = 0.002, Zsx = -0.07, Zsy = 0.07, Angle = 45.0 }
        ]
    };
    var result = PlateSectionOpenSeesMapper.Map(section, ShellFrame.Identity, Resolver());

    Assert.Contains(result.Section.Layers,
        x => x.Kind == ShellLayerKind.RebarX && x.DirectionDegrees == 45.0 && x.CenterZ == -0.07);
    Assert.Contains(result.Section.Layers,
        x => x.Kind == ShellLayerKind.RebarY && x.DirectionDegrees == 135.0 && x.CenterZ == 0.07);

    var angles = result.Materials
        .Select(m => m.Spec)
        .OfType<PlateRebarShellMaterialSpec>()
        .Select(s => s.AngleDegrees)
        .OrderBy(a => a)
        .ToArray();
    Assert.Equal([45.0, 135.0], angles);
}

[Fact]
public void Map_RebarAngle200_NormalizesToMinus160AndMinus70()
{
    var section = new PlateSection
    {
        H = 0.2,
        NLayers = 2,
        RebarLayers = [
            new PlateRebarLayer { Asx = 0.001, Asy = 0.001, Zsx = -0.07, Zsy = 0.07, Angle = 200.0 }
        ]
    };
    var result = PlateSectionOpenSeesMapper.Map(section, ShellFrame.Identity, Resolver());

    Assert.Contains(result.Section.Layers,
        x => x.Kind == ShellLayerKind.RebarX && x.DirectionDegrees == -160.0);
    Assert.Contains(result.Section.Layers,
        x => x.Kind == ShellLayerKind.RebarY && x.DirectionDegrees == -70.0);
}

[Fact]
public void Map_NonFiniteRebarAngle_ThrowsRebarAngleInvalid()
{
    var section = new PlateSection
    {
        H = 0.2,
        NLayers = 2,
        RebarLayers = [
            new PlateRebarLayer { Asx = 0.001, Zsx = -0.07, Angle = double.NaN }
        ]
    };

    var ex = Assert.Throws<CScoreMappingException>(() =>
        PlateSectionOpenSeesMapper.Map(section, ShellFrame.Identity, Resolver()));

    Assert.Contains("rebar_angle_invalid", ex.Message, StringComparison.OrdinalIgnoreCase);
}

[Fact]
public void Map_RebarAngleAndFace_ChangeSourceAndSectionFingerprint()
{
    var angle0 = MapWithRebar(new PlateRebarLayer { Asx = 0.001, Zsx = -0.07, Angle = 0.0 });
    var angle30 = MapWithRebar(new PlateRebarLayer { Asx = 0.001, Zsx = -0.07, Angle = 30.0 });
    var minusFace = MapWithRebar(new PlateRebarLayer { Asx = 0.001, Zsx = -0.07, Face = RebarFace.MinusN });

    Assert.NotEqual(angle0.SourcePlateSectionFingerprint, angle30.SourcePlateSectionFingerprint);
    Assert.NotEqual(angle0.Fingerprint, angle30.Fingerprint);
    Assert.NotEqual(angle0.SourcePlateSectionFingerprint, minusFace.SourcePlateSectionFingerprint);
    Assert.NotEqual(angle0.Fingerprint, minusFace.Fingerprint);
}

private static RCShellLayeredSection MapWithRebar(PlateRebarLayer layer)
{
    var section = new PlateSection { H = 0.2, NLayers = 2, RebarLayers = [layer] };
    return PlateSectionOpenSeesMapper.Map(section, ShellFrame.Identity, Resolver()).Section;
}
```

- [ ] **Step 2: Run the tests to verify failure**

```powershell
dotnet test CScore.Tests --filter "FullyQualifiedName~PlateRebarLayoutFingerprintTests"
dotnet test OpenCS.OpenSees.Tests --filter "FullyQualifiedName~PlateSectionOpenSeesMapperTests"
```

Expected: `PlateRebarLayoutFingerprintTests` — PASS (код уже содержит Angle/Face; тесты фиксируют поведение). `PlateSectionOpenSeesMapperTests` — FAIL: `Map_RebarAngle45_MapsXAt45AndYAt135Degrees` (получено 0/90), `Map_RebarAngle200...` (получено 0/90), `Map_NonFiniteRebarAngle_ThrowsRebarAngleInvalid` (нет исключения), `Map_RebarAngleAndFace_ChangeSourceAndSectionFingerprint` (fingerprint совпадает).

- [ ] **Step 3: Write the minimal implementation**

В `OpenCS.OpenSees.CScore/PlateSectionOpenSeesMapper.cs`:

1) Добавить два private static метода (рядом с `ValidateArea`):

```csharp
private static void ValidateRebarAngle(double value, int index)
{
    if (!double.IsFinite(value))
        throw new CScoreMappingException(
            $"rebar_angle_invalid: арматурный слой {index}: угол должен быть конечным.");
}

private static double NormalizeDegrees(double degrees)
{
    double value = degrees % 360.0;
    if (value >= 180.0) value -= 360.0;
    if (value < -180.0) value += 360.0;
    return value;
}
```

2) В цикле `for (int sourceIndex = 0; sourceIndex < section.RebarLayers.Count; sourceIndex++)` после вызовов `ValidateArea` добавить:

```csharp
ValidateRebarAngle(source.Angle, sourceIndex);
double angleX = NormalizeDegrees(source.Angle);
double angleY = NormalizeDegrees(source.Angle + 90.0);
```

3) В ветке `if (source.Asx > 0)` заменить `RegisterChain(rebarChain, nextMaterialTag, 0, ...)` на `RegisterChain(rebarChain, nextMaterialTag, angleX, ...)` и `RCShellLayer(... oriented.Tag, 0, ...)` на `... oriented.Tag, angleX, ...`.

4) В ветке `if (source.Asy > 0)` заменить `RegisterChain(rebarChain, nextMaterialTag, 90, ...)` на `RegisterChain(rebarChain, nextMaterialTag, angleY, ...)` и `RCShellLayer(... oriented.Tag, 90, ...)` на `... oriented.Tag, angleY, ...`.

5) В `sourceFingerprint` (внутри `string.Join(";", section.RebarLayers.Select(layer => string.Join(","`, ...))` — после `layer.MaterialId.ToString(CultureInfo.InvariantCulture)` добавить ещё две части:

```csharp
layer.Angle.ToString("G17", CultureInfo.InvariantCulture),
layer.Face.ToString(CultureInfo.InvariantCulture)
```

- [ ] **Step 4: Run the tests to verify pass**

```powershell
dotnet test CScore.Tests --filter "FullyQualifiedName~PlateRebarLayoutFingerprintTests"
dotnet test OpenCS.OpenSees.Tests --filter "FullyQualifiedName~PlateSectionOpenSeesMapperTests"
dotnet build OpenCS.sln
```

Expected: все тесты PASS; build успешен (0 ошибок). Существующие mapper-тесты (`Map_CreatesIndependentXAndYRebarLayers` с Angle=0 → 0/90) остаются зелёными.

- [ ] **Step 5: Commit**

```bash
git add OpenCS.OpenSees.CScore/PlateSectionOpenSeesMapper.cs OpenCS.OpenSees.Tests/PlateSectionOpenSeesMapperTests.cs CScore.Tests/PlateRebar/PlateRebarLayoutFingerprintTests.cs
git commit -m "feat(shell): map rebar angle phi and phi+90 with fingerprint"
```

## Task 2: Capability descriptors NativeShellMaterialSpec

**Files:**
- Modify: `OpenCS.OpenSees/Model/NativeShellMaterialSpec.cs`
- Test: `OpenCS.OpenSees.Tests/NativeShellMaterialSpecTests.cs`

- [ ] **Step 1: Write the failing tests**

В `OpenCS.OpenSees.Tests/NativeShellMaterialSpecTests.cs` добавить:

```csharp
[Fact]
public void ElasticIsotropic_DeclaresRequiredStressAndStrainCapabilities()
{
    var spec = new ElasticIsotropicShellMaterialSpec(30e9, 0.2);

    NativeResponseCapability stress = Assert.Single(spec.Capabilities, c => c.ResponseName == "stress");
    Assert.True(stress.IsRequired);
    Assert.Equal(5, stress.ComponentCount);
    Assert.Equal("Pa", stress.Unit);
    Assert.Equal("stress", stress.TclQueryContract);
    Assert.True(spec.HasResponse("strain"));
}

[Fact]
public void PlasticDamageConcrete_DoesNotFakeDamageCrackOrEnergyCapabilities()
{
    var spec = new PlasticDamageConcretePlaneStressShellMaterialSpec(
        3.0e10, 0.2, 3.0e6, 3.0e7, 0.6, 0.5, 2.0, 0.14);

    Assert.True(spec.HasResponse("stress"));
    Assert.True(spec.HasResponse("strain"));
    Assert.False(spec.HasResponse("tangent"));
    Assert.False(spec.HasResponse("damage"));
    Assert.False(spec.HasResponse("crack"));
    Assert.False(spec.HasResponse("energy"));
}

[Fact]
public void PlateRebar_DeclaresStressStrainWithoutEnergyCapability()
{
    var spec = new PlateRebarShellMaterialSpec(5, 45);

    Assert.True(spec.HasResponse("stress"));
    Assert.True(spec.HasResponse("strain"));
    Assert.False(spec.HasResponse("energy"));
    Assert.Contains(spec.Capabilities, c => c.ResponseName == "stress" && c.Warnings.Count > 0);
}
```

- [ ] **Step 2: Run the tests to verify failure**

```powershell
dotnet test OpenCS.OpenSees.Tests --filter "FullyQualifiedName~NativeShellMaterialSpecTests"
```

Expected: FAIL — типы `NativeResponseCapability` и член `Capabilities` не существуют (CS compilation error / member missing).

- [ ] **Step 3: Write the minimal implementation**

В `OpenCS.OpenSees/Model/NativeShellMaterialSpec.cs`:

1) Добавить в конец namespace (после `Steel02UniaxialShellMaterialSpec`) два новых типа:

```csharp
/// <summary>Описание capability одного native response материала: имя, контракт Tcl-запроса,
/// число компонент, единицы, обязательность и сопряжённые stress/strain пары для
/// state-integral energy. Контракт задаётся backend adapter-ом, а не пользователем.</summary>
public sealed record NativeResponseCapability(
    string ResponseName,
    string TclQueryContract,
    int ComponentCount,
    string Unit,
    bool IsRequired,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<NativeResponseConjugatePair> ConjugatePairs);

/// <summary>Сопряжённая пара response-имён (например stress/strain) для численной интеграции
/// state integral energy. Отсутствие пары означает, что StateIntegral недоступен.</summary>
public sealed record NativeResponseConjugatePair(string StressResponse, string StrainResponse);
```

2) В `NativeShellMaterialSpec` добавить абстрактный член и helper:

```csharp
/// <summary>Описания native response capabilities материала.</summary>
public abstract IReadOnlyList<NativeResponseCapability> Capabilities { get; }

/// <summary>Проверяет, объявлен ли материалом response с указанным именем.</summary>
public bool HasResponse(string responseName) =>
    Capabilities.Any(capability =>
        string.Equals(capability.ResponseName, responseName, StringComparison.Ordinal));

/// <summary>Стандартный набор обязательных shell stress/strain capabilities (5 компонент, Па).</summary>
protected static IReadOnlyList<NativeResponseCapability> RequiredShellStressStrain() =>
[
    new("stress", "stress", 5, "Pa", true, [], []),
    new("strain", "strain", 5, "Pa", true, [], [])
];
```

3) В каждый конкретный record добавить override (для `ElasticIsotropicShellMaterialSpec`, `PlateRebarShellMaterialSpec`, `ElasticUniaxialShellMaterialSpec`, `PlasticDamageConcretePlaneStressShellMaterialSpec`, `PlateFromPlaneStressShellMaterialSpec`, `Steel01UniaxialShellMaterialSpec`, `Steel02UniaxialShellMaterialSpec`):

```csharp
public override IReadOnlyList<NativeResponseCapability> Capabilities => RequiredShellStressStrain();
```

Для `PlateRebarShellMaterialSpec` использовать явный список с warning про smeared-аппроксимацию z-координаты:

```csharp
public override IReadOnlyList<NativeResponseCapability> Capabilities =>
[
    new("stress", "stress", 5, "Pa", true,
        ["Smeared-арматура задана отдельными native слоями; точное сохранение z-координаты LayeredShell требует отдельной capability-проверки."], []),
    new("strain", "strain", 5, "Pa", true, [], [])
];
```

Для uniaxial материалов (`ElasticUniaxialShellMaterialSpec`, `Steel01UniaxialShellMaterialSpec`, `Steel02UniaxialShellMaterialSpec`) добавить private static helper `RequiredUniaxialStressStrain()` рядом с `RequiredShellStressStrain`:

```csharp
protected static IReadOnlyList<NativeResponseCapability> RequiredUniaxialStressStrain() =>
[
    new("stress", "stress", 1, "Pa", true, [], []),
    new("strain", "strain", 1, "Pa", true, [], [])
];
```

- [ ] **Step 4: Run the tests to verify pass**

```powershell
dotnet test OpenCS.OpenSees.Tests --filter "FullyQualifiedName~NativeShellMaterialSpecTests"
dotnet build OpenCS.sln
```

Expected: PASS; build успешен. `PlasticDamageConcretePlaneStress` не объявляет tangent/damage/crack/energy — это требование §6 и §11 (никаких фиктивных capability без реальной верификации).

- [ ] **Step 5: Commit**

```bash
git add OpenCS.OpenSees/Model/NativeShellMaterialSpec.cs OpenCS.OpenSees.Tests/NativeShellMaterialSpecTests.cs
git commit -m "feat(shell): declare native response capability descriptors"
```

## Task 3: Provenance типы и parser state_order v2 / legacy v1

**Files:**
- Modify: `OpenCS.OpenSees/Structural/ShellMaterialState.cs`
- Modify: `OpenCS.OpenSees/Results/ShellStateParser.cs`
- Test: `OpenCS.OpenSees.Tests/ShellStateParserTests.cs`

- [ ] **Step 1: Write the failing tests**

В `OpenCS.OpenSees.Tests/ShellStateParserTests.cs`:

1) Переписать существующий тест `ParseShellLayers_MapsStressAndStrainRowsToRequestedStep` на v2 (JSON с полной metadata):

```csharp
[Fact]
public void ParseShellLayers_MapsStressAndStrainRowsToRequestedStep()
{
    string directory = CreateTempDirectory();
    try
    {
        File.WriteAllText(Path.Combine(directory, "state_order.json"), """
        {
          "version": 2,
          "shellLayerGroups": [
            { "sectionTag": 20, "integrationPoint": 1, "layerIndex": 1, "responseKind": "stress",
              "elementTags": [10, 11], "fileName": "stress.out", "componentCount": 5, "unit": "Pa",
              "materialTag": 1, "layerKind": "Concrete", "sourceId": "concrete:1:0",
              "centerZ": -0.075, "thickness": 0.05, "sectionFingerprint": "fp-a" },
            { "sectionTag": 20, "integrationPoint": 1, "layerIndex": 1, "responseKind": "strain",
              "elementTags": [10, 11], "fileName": "strain.out", "componentCount": 5, "unit": "Pa",
              "materialTag": 1, "layerKind": "Concrete", "sourceId": "concrete:1:0",
              "centerZ": -0.075, "thickness": 0.05, "sectionFingerprint": "fp-a" }
          ],
          "beamFiberLocations": [],
          "optionalResponses": []
        }
        """);
        File.WriteAllText(Path.Combine(directory, "step_status.out"), """
        1 0 0.5 1 0
        2 0 1.0 1 0
        """);
        File.WriteAllText(Path.Combine(directory, "stress.out"),
            "0.5 1 2 3 4 5 6 7 8 9 10\n1.0 11 12 13 14 15 16 17 18 19 20\n");
        File.WriteAllText(Path.Combine(directory, "strain.out"),
            "0.5 0.1 0.2 0.3 0.4 0.5 0.6 0.7 0.8 0.9 1.0\n1.0 1.1 1.2 1.3 1.4 1.5 1.6 1.7 1.8 1.9 2.0\n");

        var parser = new ShellStateParser();
        var catalog = parser.ParseCatalog(directory);
        Assert.Equal(ShellStateCatalogProvenanceKind.V2WithProvenance, catalog.ProvenanceKind);
        var states = parser.ParseShellLayers(directory, catalog, 10, 1, 1, 2);

        var state = Assert.Single(states);
        Assert.Equal(2, state.Key.StepIndex);
        Assert.Equal(1.0, state.Key.LoadFactor);
        Assert.Equal([11d, 12, 13, 14, 15], state.Stress);
        Assert.Equal([1.1, 1.2, 1.3, 1.4, 1.5], state.Strain);
        Assert.NotNull(state.CatalogGroup);
        Assert.Equal(20, state.CatalogGroup!.SectionTag);
        Assert.Equal(1, state.CatalogGroup!.MaterialTag);
        Assert.Equal(ShellLayerKind.Concrete, state.CatalogGroup!.LayerKind);
        Assert.Equal("concrete:1:0", state.CatalogGroup!.SourceId);
        Assert.Equal(-0.075, state.CatalogGroup!.CenterZ!.Value, 12);
        Assert.Equal("fp-a", state.CatalogGroup!.SectionFingerprint);
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}
```

2) Добавить новые тесты:

```csharp
[Fact]
public void ParseCatalog_V2_RequiresProvenanceMetadata()
{
    string directory = CreateTempDirectory();
    try
    {
        File.WriteAllText(Path.Combine(directory, "state_order.json"), """
        {
          "version": 2,
          "shellLayerGroups": [
            { "integrationPoint": 1, "layerIndex": 1, "responseKind": "stress",
              "elementTags": [10], "fileName": "stress.out", "componentCount": 5 }
          ],
          "beamFiberLocations": [],
          "optionalResponses": []
        }
        """);

        var ex = Assert.Throws<OpenSeesResultException>(() =>
            new ShellStateParser().ParseCatalog(directory));

        Assert.Equal("InvalidStateOrder", ex.Code);
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

[Fact]
public void ParseCatalog_V1_IsLegacyWithoutProvenance()
{
    string directory = CreateTempDirectory();
    try
    {
        File.WriteAllText(Path.Combine(directory, "state_order.json"), """
        {
          "version": 1,
          "shellLayerGroups": [
            { "integrationPoint": 1, "layerIndex": 1, "responseKind": "stress",
              "elementTags": [10], "fileName": "stress.out", "componentCount": 5 }
          ],
          "beamFiberLocations": [],
          "optionalResponses": []
        }
        """);

        ShellStateCatalog catalog = new ShellStateParser().ParseCatalog(directory);

        Assert.Equal(ShellStateCatalogProvenanceKind.V1LegacyMissing, catalog.ProvenanceKind);
        ShellLayerStateGroup group = Assert.Single(catalog.ShellLayerGroups);
        Assert.Null(group.MaterialTag);
        Assert.Null(group.LayerKind);
        Assert.Null(group.SourceId);
        Assert.Null(group.SectionFingerprint);
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

[Fact]
public void ParseShellLayers_V1Legacy_RejectsWithProvenanceMissingInsteadOfDefaults()
{
    string directory = CreateTempDirectory();
    try
    {
        File.WriteAllText(Path.Combine(directory, "state_order.json"), """
        {
          "version": 1,
          "shellLayerGroups": [
            { "integrationPoint": 1, "layerIndex": 1, "responseKind": "stress",
              "elementTags": [10], "fileName": "stress.out", "componentCount": 5 },
            { "integrationPoint": 1, "layerIndex": 1, "responseKind": "strain",
              "elementTags": [10], "fileName": "strain.out", "componentCount": 5 }
          ],
          "beamFiberLocations": [],
          "optionalResponses": []
        }
        """);
        File.WriteAllText(Path.Combine(directory, "step_status.out"), "1 0 1.0 1 0\n");
        File.WriteAllText(Path.Combine(directory, "stress.out"), "1.0 1 2 3 4 5\n");
        File.WriteAllText(Path.Combine(directory, "strain.out"), "1.0 0.1 0.2 0.3 0.4 0.5\n");

        var ex = Assert.Throws<OpenSeesResultException>(() =>
            new ShellStateParser().ParseShellLayers(
                directory, new ShellStateParser().ParseCatalog(directory), 10, 1, 1, 1));

        Assert.Equal("state_catalog_provenance_missing", ex.Code);
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

[Fact]
public void ParseCatalog_V2_TwoSectionsWithSameLayerCount_ProduceSeparateGroups()
{
    string directory = CreateTempDirectory();
    try
    {
        File.WriteAllText(Path.Combine(directory, "state_order.json"), """
        {
          "version": 2,
          "shellLayerGroups": [
            { "sectionTag": 20, "integrationPoint": 1, "layerIndex": 1, "responseKind": "stress",
              "elementTags": [10], "fileName": "s20.out", "componentCount": 5, "unit": "Pa",
              "materialTag": 1, "layerKind": "Concrete", "sourceId": "a:0",
              "centerZ": -0.05, "thickness": 0.1, "sectionFingerprint": "fp-a" },
            { "sectionTag": 21, "integrationPoint": 1, "layerIndex": 1, "responseKind": "stress",
              "elementTags": [11], "fileName": "s21.out", "componentCount": 5, "unit": "Pa",
              "materialTag": 2, "layerKind": "Concrete", "sourceId": "b:0",
              "centerZ": -0.05, "thickness": 0.1, "sectionFingerprint": "fp-b" }
          ],
          "beamFiberLocations": [],
          "optionalResponses": []
        }
        """);

        ShellStateCatalog catalog = new ShellStateParser().ParseCatalog(directory);

        Assert.Equal(2, catalog.ShellLayerGroups.Count);
        Assert.Equal([20, 21], catalog.ShellLayerGroups.Select(g => g.SectionTag).ToArray());
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}
```

- [ ] **Step 2: Run the tests to verify failure**

```powershell
dotnet test OpenCS.OpenSees.Tests --filter "FullyQualifiedName~ShellStateParserTests"
```

Expected: FAIL — `ShellStateCatalogProvenanceKind` и `CatalogGroup` не существуют (compile error); v2 JSON не парсится (поля sectionTag/layerKind не читаются).

- [ ] **Step 3: Write the minimal implementation**

1) В `OpenCS.OpenSees/Structural/ShellMaterialState.cs`:

Добавить enum и расширить `ShellLayerStateGroup` (заменив существующие `MaterialTag { get; init; }` / `LayerKind { get; init; }` — типы те же `int?`/`ShellLayerKind?`):

```csharp
/// <summary>Происхождение metadata material-state catalog: v2 — полное, v1 — legacy без provenance.</summary>
public enum ShellStateCatalogProvenanceKind
{
    /// <summary>Catalog v2 с полной identity metadata (MaterialTag, LayerKind, SourceId, ...).</summary>
    V2WithProvenance,

    /// <summary>Legacy v1 без metadata — строгий audit и typed mapping не принимают.</summary>
    V1LegacyMissing
}
```

`ShellLayerStateGroup` (полная замена):

```csharp
/// <summary>Группа shell recorder для одной секции, IP, слоя и response (state_order v2).</summary>
public sealed record ShellLayerStateGroup(
    int IntegrationPoint,
    int LayerIndex,
    string ResponseKind,
    IReadOnlyList<int> ElementTags,
    string FileName,
    int ComponentCount)
{
    /// <summary>Tag слоистой секции OpenSees (v2, обязателен).</summary>
    public int? SectionTag { get; init; }

    /// <summary>Tag материала слоя (v2, обязателен; v1 — null, defaults запрещены).</summary>
    public int? MaterialTag { get; init; }

    /// <summary>Назначение слоя (v2, обязательно; v1 — null).</summary>
    public ShellLayerKind? LayerKind { get; init; }

    /// <summary>Стабильный SourceId исходного слоя (v2).</summary>
    public string? SourceId { get; init; }

    /// <summary>Координата центра слоя, м (v2).</summary>
    public double? CenterZ { get; init; }

    /// <summary>Эквивалентная толщина слоя, м (v2; у арматуры — smeared толщина из As).</summary>
    public double? Thickness { get; init; }

    /// <summary>Fingerprint исходного PlateSection/layout (v2).</summary>
    public string? SectionFingerprint { get; init; }

    /// <summary>Единицы response (v2).</summary>
    public string? Unit { get; init; }
}
```

`ShellStateCatalog` — добавить вычисляемое свойство `ProvenanceKind` (сигнатура конструктора не меняется):

```csharp
/// <summary>Лёгкий каталог material-state файлов и их колонок.</summary>
public sealed record ShellStateCatalog(
    int Version,
    IReadOnlyList<ShellLayerStateGroup> ShellLayerGroups,
    IReadOnlyList<ShellBeamFiberLocation> BeamFiberLocations,
    IReadOnlyList<string> OptionalResponses)
{
    /// <summary>Происхождение metadata: v2 — полное, v1 — legacy missing.</summary>
    public ShellStateCatalogProvenanceKind ProvenanceKind =>
        Version >= 2
            ? ShellStateCatalogProvenanceKind.V2WithProvenance
            : ShellStateCatalogProvenanceKind.V1LegacyMissing;
}
```

`RCShellLayerState` — добавить поле и параметр конструктора (в конец, чтобы существующий вызов с именованными параметрами не сломался):

```csharp
    /// <summary>Ссылка на полный catalog entry с provenance (sourceId, centerZ, thickness, fingerprint).</summary>
    public ShellLayerStateGroup? CatalogGroup { get; }
```

и в конструктор добавить параметр `ShellLayerStateGroup? CatalogGroup = null` с присваиванием `this.CatalogGroup = CatalogGroup;`.

2) В `OpenCS.OpenSees/Results/ShellStateParser.cs`:

Расширить `LayerGroupDto`:

```csharp
    private sealed record LayerGroupDto(
        int IntegrationPoint,
        int LayerIndex,
        string? ResponseKind,
        List<int>? ElementTags,
        string? FileName,
        int ComponentCount,
        int? SectionTag,
        int? MaterialTag,
        ShellLayerKind? LayerKind,
        string? SourceId,
        double? CenterZ,
        double? Thickness,
        string? SectionFingerprint,
        string? Unit);
```

В `ParseCatalog` заменить блок разбора групп:

```csharp
            bool isV2 = dto.Version >= 2;
            if (dto.Version is not (1 or 2))
                throw new OpenSeesResultException("InvalidStateOrder", $"Неподдерживаемая версия material-state catalog: {dto.Version}.");

            var groups = (dto.ShellLayerGroups ?? []).Select(group =>
            {
                if (group.IntegrationPoint <= 0 || group.LayerIndex <= 0 ||
                    string.IsNullOrWhiteSpace(group.ResponseKind) ||
                    string.IsNullOrWhiteSpace(group.FileName) || group.ElementTags is null ||
                    group.ElementTags.Count == 0 || group.ElementTags.Any(tag => tag <= 0) ||
                    group.ElementTags.Distinct().Count() != group.ElementTags.Count ||
                    (group.ResponseKind is not ("stress" or "strain") && group.ResponseKind is not null && isV2 == false))
                    throw new OpenSeesResultException("InvalidStateOrder", "Некорректная shell material-state recorder group.");
                if (isV2)
                {
                    if (group.SectionTag is not (> 0))
                        throw new OpenSeesResultException("InvalidStateOrder", "v2 group: отсутствует sectionTag.");
                    if (group.MaterialTag is not (> 0))
                        throw new OpenSeesResultException("InvalidStateOrder", "v2 group: отсутствует materialTag.");
                    if (group.LayerKind is null)
                        throw new OpenSeesResultException("InvalidStateOrder", "v2 group: отсутствует layerKind.");
                    if (string.IsNullOrWhiteSpace(group.SourceId))
                        throw new OpenSeesResultException("InvalidStateOrder", "v2 group: отсутствует sourceId.");
                    if (group.CenterZ is not double centerZ || !double.IsFinite(centerZ))
                        throw new OpenSeesResultException("InvalidStateOrder", "v2 group: некорректный centerZ.");
                    if (group.Thickness is not double thickness || !double.IsFinite(thickness) || thickness <= 0)
                        throw new OpenSeesResultException("InvalidStateOrder", "v2 group: некорректная толщина.");
                    if (string.IsNullOrWhiteSpace(group.SectionFingerprint))
                        throw new OpenSeesResultException("InvalidStateOrder", "v2 group: отсутствует sectionFingerprint.");
                    if (string.IsNullOrWhiteSpace(group.Unit))
                        throw new OpenSeesResultException("InvalidStateOrder", "v2 group: отсутствует unit.");
                    if (group.ComponentCount <= 0)
                        throw new OpenSeesResultException("InvalidStateOrder", "v2 group: некорректный componentCount.");
                }
                EnsureSafePath(directory, group.FileName);
                return new ShellLayerStateGroup(
                    group.IntegrationPoint, group.LayerIndex, group.ResponseKind!,
                    group.ElementTags, group.FileName, group.ComponentCount)
                {
                    SectionTag = group.SectionTag,
                    MaterialTag = group.MaterialTag,
                    LayerKind = group.LayerKind,
                    SourceId = group.SourceId,
                    CenterZ = group.CenterZ,
                    Thickness = group.Thickness,
                    SectionFingerprint = group.SectionFingerprint,
                    Unit = group.Unit
                };
            }).ToArray();
```

Примечание: условие про `ResponseKind` упрощено — для v2 допускаются любые непустые responseKind (optional groups), для v1 сохраняется прежнее ограничение `stress|strain` (обратная совместимость). Заменить блок duplicate-проверки на identity по 4 полям (для v1 `SectionTag` = null → 0):

```csharp
            var duplicateGroups = groups
                .GroupBy(group => (group.SectionTag ?? 0, group.IntegrationPoint, group.LayerIndex, group.ResponseKind))
                .FirstOrDefault(group => group.Count() > 1);
            if (duplicateGroups is not null)
                throw new OpenSeesResultException("DuplicateStateGroup", "В material-state catalog повторяется shell recorder group.");
```

В `ParseShellLayers` заменить строки 137-145 (фолбэки `?? 1` / `?? ShellLayerKind.Concrete`) на строгую проверку provenance и передачу группы:

```csharp
            if (stressGroup.SectionTag is null || stressGroup.MaterialTag is not (> 0) ||
                stressGroup.LayerKind is null || string.IsNullOrWhiteSpace(stressGroup.SourceId))
                throw new OpenSeesResultException("state_catalog_provenance_missing",
                    "Material-state catalog не содержит provenance (v1 legacy); строгий разбор состояния невозможен.");

            return
            [
                new RCShellLayerState(
                    new RCShellMaterialStateKey(
                        targetStep.StepIndex, targetStep.StageIndex, targetStep.LoadFactor,
                        elementTag, integrationPoint, layerIndex,
                        ShellMaterialStateLocationKind.ShellLayer),
                    stressGroup.MaterialTag!.Value,
                    stressGroup.LayerKind!.Value,
                    stressRow[(1 + stressElementIndex * 5)..(1 + (stressElementIndex + 1) * 5)],
                    strainRow[(1 + strainElementIndex * 5)..(1 + (strainElementIndex + 1) * 5)],
                    CatalogGroup: stressGroup)
            ];
```

- [ ] **Step 4: Run the tests to verify pass**

```powershell
dotnet test OpenCS.OpenSees.Tests --filter "FullyQualifiedName~ShellStateParserTests"
dotnet test OpenCS.OpenSees.Tests --filter "FullyQualifiedName~ShellResultsTests"
dotnet build OpenCS.sln
```

Expected: PASS — включая legacy-тесты `ParseShellLayers_AllowsMissingFileWhenNoStepConverged` (v1, `return []` до доступа к группе) и `ParseShellLayers_RejectsWrongColumnCount` (v1, ошибка данных срабатывает раньше проверки provenance, т.к. та стоит после чтения строк). `ShellResultsTests` зелёные (конструктор `ShellStateCatalog` не изменился).

- [ ] **Step 5: Commit**

```bash
git add OpenCS.OpenSees/Structural/ShellMaterialState.cs OpenCS.OpenSees/Results/ShellStateParser.cs OpenCS.OpenSees.Tests/ShellStateParserTests.cs
git commit -m "feat(shell): state_order v2 provenance and legacy v1 without defaults"
```

## Task 4: ShellTclGenerator — эмиссия state_order v2 и групп по секциям

**Files:**
- Modify: `OpenCS.OpenSees/Tcl/ShellTclGenerator.cs`
- Modify: `OpenCS.OpenSees/Structural/ShellMaterialState.cs` (поле `OptionalResponses` в policy — см. шаг 3)
- Test: `OpenCS.OpenSees.Tests/ShellTclGeneratorTests.cs`

- [ ] **Step 1: Write the failing tests**

В `OpenCS.OpenSees.Tests/ShellTclGeneratorTests.cs`:

1) Обновить тест `Generate_EmitsShellLayerStressAndStrainRecordersWithoutQ4T3Collisions` — заменить ожидаемые имена файлов на v2 (секция 20):

```csharp
        Assert.Contains(
            "recorder Element -file shell_layer_s20_ip1_layer1_stress.out -closeOnWrite -time -ele 10 11 material 1 fiber 1 stress",
            script);
        Assert.Contains(
            "recorder Element -file shell_layer_s20_ip1_layer1_strain.out -closeOnWrite -time -ele 10 11 material 1 fiber 1 strain",
            script);
        Assert.Contains(
            "recorder Element -file shell_layer_s20_ip4_layer4_stress.out -closeOnWrite -time -ele 10 material 4 fiber 4 stress",
            script);
        Assert.DoesNotContain("shell_layer_s20_ip4_layer4_stress.out -closeOnWrite -time -ele 10 11", script);
        Assert.Contains("\"version\":2", script);
```

2) Добавить новые тесты:

```csharp
[Fact]
public void Generate_TwoSectionsWithSameLayerCount_EmitsSeparateRecorderGroups()
{
    var baseModel = Q4Model();
    var model = baseModel with
    {
        Sections =
        [
            baseModel.Sections[0],
            new(21, "plate-b", 0.2, ShellFrame.Identity,
                baseModel.Sections[0].Layers.Select((layer, i) => layer with
                {
                    MaterialTag = 1,
                    SourceId = $"concrete-b:{i}"
                }).ToArray(),
                ShellMappingMode.Exact, [], "section-fingerprint-b")
        ],
        Materials = baseModel.Materials,
        Elements =
        [
            baseModel.Elements[0],
            new(11, ShellElementKind.ASDShellQ4, [1, 2, 3, 4], 21, "section-fingerprint-b",
                ShellFrame.Identity, ShellIntegrationPolicy.Full, null)
        ]
    };

    var script = new ShellTclGenerator().Generate(model);

    Assert.Contains("recorder Element -file shell_layer_s20_ip1_layer1_stress.out -closeOnWrite -time -ele 10 material 1 fiber 1 stress", script);
    Assert.Contains("recorder Element -file shell_layer_s21_ip1_layer1_stress.out -closeOnWrite -time -ele 11 material 1 fiber 1 stress", script);
    Assert.DoesNotContain("shell_layer_s20_ip1_layer1_stress.out -closeOnWrite -time -ele 10 11", script);
}

[Fact]
public void Generate_StateOrderV2CarriesProvenanceMetadata()
{
    var script = new ShellTclGenerator().Generate(Q4Model());

    Assert.Contains("\"version\":2", script);
    Assert.Contains("\"sectionTag\":20", script);
    Assert.Contains("\"layerKind\":\"Concrete\"", script);
    Assert.Contains("\"sourceId\":\"concrete:", script);
    Assert.Contains("\"sectionFingerprint\":\"section-fingerprint\"", script);
    Assert.Contains("\"centerZ\":", script);
    Assert.Contains("\"thickness\":", script);
    Assert.Contains("\"unit\":\"Pa\"", script);
}

[Fact]
public void Generate_RequestedUnsupportedOptionalResponse_ThrowsUnsupportedShellResponse()
{
    var model = Q4Model() with
    {
        MaterialStateRecording = new ShellStateRecordingPolicy
        {
            OptionalResponses = ["tangent"]
        }
    };

    var ex = Assert.Throws<InvalidOperationException>(() => new ShellTclGenerator().Generate(model));

    Assert.Contains("unsupported_shell_response", ex.Message, StringComparison.OrdinalIgnoreCase);
}
```

- [ ] **Step 2: Run the tests to verify failure**

```powershell
dotnet test OpenCS.OpenSees.Tests --filter "FullyQualifiedName~ShellTclGeneratorTests"
```

Expected: FAIL — `OptionalResponses` не существует в policy (compile error); `state_order.json` по-прежнему v1 с `version = 1` и без `sectionTag`/`layerKind`/metadata; имена файлов старые (`shell_layer_ip1_layer1_stress.out`).

- [ ] **Step 3: Write the minimal implementation**

1) В `OpenCS.OpenSees/Structural/ShellMaterialState.cs`, в `ShellStateRecordingPolicy`, добавить поле:

```csharp
    /// <summary>Opt-in optional response-имена (например "tangent"); null или пусто = не запрашивать.
    /// Запрашиваются только response-имена, поддерживаемые ВСЕМИ слоями группы (см. генератор).</summary>
    public IReadOnlyList<string>? OptionalResponses { get; init; }
```

2) В `OpenCS.OpenSees/Tcl/ShellTclGenerator.cs`:

Заменить метод `EmitShellLayerStateRecorders` целиком (группировка по секциям, v2 metadata, optional responses с проверкой поддержки):

```csharp
    /// <summary>Эмитирует material recorder-группы по слоям LayeredShell с group identity
    /// (sectionTag, integrationPoint, layerIndex, responseKind) и возвращает v2-catalog.</summary>
    private static List<ShellLayerStateGroup> EmitShellLayerStateRecorders(
        ShellOpenSeesModel model,
        Action<string> line)
    {
        var groups = new List<ShellLayerStateGroup>();
        if (!model.MaterialStateRecording.RecordShellLayers)
            return groups;

        var sections = model.Sections.ToDictionary(section => section.Tag);
        var materials = model.Materials.ToDictionary(material => material.Tag);
        int maxIp = model.Elements.Max(element => element.IntegrationPointCount);
        int[] selectedIps = model.MaterialStateRecording.ShellIntegrationPoints is { } requestedIps
            ? requestedIps.Distinct().OrderBy(ip => ip).ToArray()
            : Enumerable.Range(1, maxIp).ToArray();

        var optionalResponses = model.MaterialStateRecording.OptionalResponses ?? [];

        foreach (int sectionTag in model.Elements.Select(element => element.SectionTag).Distinct().OrderBy(tag => tag))
        {
            RCShellLayeredSection section = sections[sectionTag];
            for (int layerIndex = 1; layerIndex <= section.Layers.Count; layerIndex++)
            {
                RCShellLayer layer = section.Layers[layerIndex - 1];
                NativeShellMaterialDefinition material = materials[layer.MaterialTag];
                int[] elementTags = model.Elements
                    .Where(element => element.SectionTag == sectionTag && element.IntegrationPointCount >= maxIp)
                    .OrderBy(element => element.Tag)
                    .Select(element => element.Tag)
                    .ToArray();

                foreach (int point in selectedIps)
                {
                    int[] pointElementTags = elementTags
                        .Where(tag => model.Elements.Single(e => e.Tag == tag).IntegrationPointCount >= point)
                        .OrderBy(tag => tag)
                        .ToArray();
                    if (pointElementTags.Length == 0) continue;

                    foreach (string response in RequestedResponses(section, layer, material, optionalResponses))
                    {
                        (int ComponentCount, string Unit) contract = ResponseContract(material, response);
                        string fileName = $"shell_layer_s{sectionTag}_ip{point}_layer{layerIndex}_{response}.out";
                        line($"recorder Element -file {fileName} -closeOnWrite -time -ele {string.Join(' ', pointElementTags)} material {point} fiber {layerIndex} {response}");
                        groups.Add(new ShellLayerStateGroup(point, layerIndex, response, pointElementTags, fileName, contract.ComponentCount)
                        {
                            SectionTag = sectionTag,
                            MaterialTag = layer.MaterialTag,
                            LayerKind = layer.Kind,
                            SourceId = layer.SourceId,
                            CenterZ = layer.CenterZ,
                            Thickness = layer.Thickness,
                            SectionFingerprint = section.Fingerprint,
                            Unit = contract.Unit
                        });
                    }
                }
            }
        }

        return groups;
    }

    /// <summary>Возвращает response-имена группы: обязательные stress/strain + запрошенные optional.
    /// Optional-имя, не поддерживаемое материалом слоя, блокирует генерацию (unsupported_shell_response).</summary>
    private static IEnumerable<string> RequestedResponses(
        RCShellLayeredSection section,
        RCShellLayer layer,
        NativeShellMaterialDefinition material,
        IReadOnlyList<string> optionalResponses)
    {
        foreach (string response in optionalResponses)
        {
            if (!material.Spec.HasResponse(response))
                throw new InvalidOperationException(
                    $"unsupported_shell_response: слой {layer.SourceId} (секция {section.Tag}) не поддерживает response «{response}».");
        }

        return new[] { "stress", "strain" }.Concat(optionalResponses);
    }

    /// <summary>Возвращает контракт (число компонент, единицы) response из capability материала.</summary>
    private static (int ComponentCount, string Unit) ResponseContract(
        NativeShellMaterialDefinition material,
        string response) =>
        material.Spec.Capabilities.Single(capability =>
            string.Equals(capability.ResponseName, response, StringComparison.Ordinal)) is { } contract
                ? (contract.ComponentCount, contract.Unit)
                : throw new InvalidOperationException(
                    $"unsupported_shell_response: материал {material.Tag} не поддерживает «{response}».");
```

Примечание: блок `foreach (int point in selectedIps)` с `if (point <= 0 || point > maxIp) continue;` из старой версии удаляется; вместо этого `Validate()` (Task 5) гарантирует корректность выбора, а здесь остаётся только фильтр «у элемента есть точка» (applicability map). Группировка по `(sectionTag, point, layer, response)` гарантирует, что элементы разных секций не объединяются.

3) Заменить блок генерации `stateOrderJson` (в `Generate`) на v2 с полной metadata:

```csharp
        string stateOrderJson = JsonSerializer.Serialize(new
        {
            version = 2,
            shellLayerGroups = shellLayerGroups.Select(group => new
            {
                sectionTag = group.SectionTag,
                integrationPoint = group.IntegrationPoint,
                layerIndex = group.LayerIndex,
                responseKind = group.ResponseKind,
                elementTags = group.ElementTags,
                fileName = group.FileName,
                componentCount = group.ComponentCount,
                unit = group.Unit,
                materialTag = group.MaterialTag,
                layerKind = group.LayerKind.ToString(),
                sourceId = group.SourceId,
                centerZ = group.CenterZ,
                thickness = group.Thickness,
                sectionFingerprint = group.SectionFingerprint
            }),
            beamFiberLocations = beamFiberLocations.Select(location => new
            {
                elementTag = location.ElementTag,
                integrationPoint = location.IntegrationPoint,
                fiberIndex = location.FiberIndex,
                sectionTag = location.SectionTag,
                y = location.Y,
                z = location.Z,
                materialTag = location.MaterialTag
            }),
            optionalResponses = model.MaterialStateRecording.OptionalResponses ?? []
        });
```

- [ ] **Step 4: Run the tests to verify pass**

```powershell
dotnet test OpenCS.OpenSees.Tests --filter "FullyQualifiedName~ShellTclGeneratorTests"
dotnet test OpenCS.OpenSees.Tests --filter "FullyQualifiedName~ShellStateParserTests"
dotnet build OpenCS.sln
```

Expected: PASS. Существующие тесты shell material-state integration (`ShellMaterialStateIntegrationTests`) сохраняют число групп (одна секция на модель → 4 IP × 4 слоя × 2 = 32 и т.д.) — они не проверяют имена файлов, поэтому остаются зелёными.

- [ ] **Step 5: Commit**

```bash
git add OpenCS.OpenSees/Tcl/ShellTclGenerator.cs OpenCS.OpenSees/Structural/ShellMaterialState.cs OpenCS.OpenSees.Tests/ShellTclGeneratorTests.cs
git commit -m "feat(shell): emit state_order v2 recorder groups keyed by section"
```

## Task 5: Strict recording policy validation

**Files:**
- Modify: `OpenCS.OpenSees/Structural/ShellOpenSeesModel.cs`
- Modify: `OpenCS.OpenSees/Tcl/ShellTclGenerator.cs` (удаление silent filtering в `BuildBeamFiberLocations`)
- Test: `OpenCS.OpenSees.Tests/ShellOpenSeesModelTests.cs`
- Test: `OpenCS.OpenSees.Tests/ShellTclGeneratorTests.cs`

- [ ] **Step 1: Write the failing tests**

В `OpenCS.OpenSees.Tests/ShellOpenSeesModelTests.cs` добавить:

```csharp
[Fact]
public void Validate_RejectsExplicitShellIpMissingFromSomeElement()
{
    var model = BaseModel() with
    {
        Elements =
        [
            .. BaseModel().Elements,
            new(11, ShellElementKind.ASDShellT3, [1, 2, 3], 20, Fingerprint,
                ShellFrame.Identity, ShellIntegrationPolicy.Full, null)
        ],
        MaterialStateRecording = new ShellStateRecordingPolicy { ShellIntegrationPoints = [4] }
    };

    var ex = Assert.Throws<InvalidOperationException>(model.Validate);
    Assert.Contains("recording_selection_invalid", ex.Message, StringComparison.OrdinalIgnoreCase);
}

[Fact]
public void Validate_AcceptsExplicitShellIpExistingInAllElements()
{
    var model = BaseModel() with
    {
        Elements =
        [
            .. BaseModel().Elements,
            new(11, ShellElementKind.ASDShellT3, [1, 2, 3], 20, Fingerprint,
                ShellFrame.Identity, ShellIntegrationPolicy.Full, null)
        ],
        MaterialStateRecording = new ShellStateRecordingPolicy { ShellIntegrationPoints = [1] }
    };

    model.Validate();
}

[Fact]
public void Validate_RejectsBeamIpBeyondElementCount()
{
    var model = BaseModel() with
    {
        NonlinearBeamSections = new Dictionary<int, OpenSeesSectionModel>
        {
            [30] = new() { GJ = 1000, Materials = [new() { Tag = 40, Native = new Steel01Spec(4e8, 2e11, 0.01) }],
                Fibers = [new(0, 0, 0.001, 40)] }
        },
        NonlinearBeamElements = [new(100, 3, 4, 30, 3, (0, 1, 0))],
        MaterialStateRecording = new ShellStateRecordingPolicy { BeamIntegrationPoints = [4] }
    };

    var ex = Assert.Throws<InvalidOperationException>(model.Validate);
    Assert.Contains("recording_selection_invalid", ex.Message, StringComparison.OrdinalIgnoreCase);
}

[Fact]
public void Validate_RejectsBeamFiberIndexBeyondSectionFibers()
{
    var model = BaseModel() with
    {
        NonlinearBeamSections = new Dictionary<int, OpenSeesSectionModel>
        {
            [30] = new() { GJ = 1000, Materials = [new() { Tag = 40, Native = new Steel01Spec(4e8, 2e11, 0.01) }],
                Fibers = [new(0, 0, 0.001, 40)] }
        },
        NonlinearBeamElements = [new(100, 3, 4, 30, 3, (0, 1, 0))],
        MaterialStateRecording = new ShellStateRecordingPolicy { BeamFiberIndices = [1] }
    };

    var ex = Assert.Throws<InvalidOperationException>(model.Validate);
    Assert.Contains("recording_selection_invalid", ex.Message, StringComparison.OrdinalIgnoreCase);
}
```

В `OpenCS.OpenSees.Tests/ShellTclGeneratorTests.cs` добавить:

```csharp
[Fact]
public void Generate_RejectsShellIpOutsideModelRange()
{
    var model = Q4Model() with
    {
        MaterialStateRecording = new ShellStateRecordingPolicy { ShellIntegrationPoints = [9] }
    };

    var ex = Assert.Throws<InvalidOperationException>(() => new ShellTclGenerator().Generate(model));

    Assert.Contains("recording_selection_invalid", ex.Message, StringComparison.OrdinalIgnoreCase);
}

[Fact]
public void Generate_RejectsBeamFiberSelectionOutsideSectionFibers()
{
    var model = Q4Model() with
    {
        NonlinearBeamSections = new Dictionary<int, OpenSeesSectionModel>
        {
            [30] = new() { GJ = 1000, Materials = [new() { Tag = 40, Native = new Steel01Spec(4e8, 2e11, 0.01) }],
                Fibers = [new(0, 0, 0.001, 40)] }
        },
        NonlinearBeamElements = [new(200, 1, 2, 30, 3, (0, 1, 0))],
        MaterialStateRecording = new ShellStateRecordingPolicy { BeamFiberIndices = [1] }
    };

    var ex = Assert.Throws<InvalidOperationException>(() => new ShellTclGenerator().Generate(model));

    Assert.Contains("recording_selection_invalid", ex.Message, StringComparison.OrdinalIgnoreCase);
}
```

- [ ] **Step 2: Run the tests to verify failure**

```powershell
dotnet test OpenCS.OpenSees.Tests --filter "FullyQualifiedName~ShellOpenSeesModelTests|FullyQualifiedName~ShellTclGeneratorTests"
```

Expected: FAIL — `Validate_AcceptsExplicitShellIpExistingInAllElements` может пройти, но `Validate_RejectsExplicitShellIpMissingFromSomeElement`, `Validate_RejectsBeamIpBeyondElementCount`, `Validate_RejectsBeamFiberIndexBeyondSectionFibers` и генераторные тесты падают (валидация отсутствует).

- [ ] **Step 3: Write the minimal implementation**

1) В `OpenCS.OpenSees/Structural/ShellOpenSeesModel.cs`, в `Validate()` после `Policy.Validate(); MaterialStateRecording.Validate();` добавить вызов `ValidateRecordingPolicy();` и сам метод:

```csharp
    /// <summary>Строго проверяет recording policy против конкретной topology: явные shell/beam IP и
    /// beam fiber индексы должны существовать у КАЖДОГО элемента/секции заявленной области;
    /// null означает все применимые позиции; mixed topology с разным числом IP допустима только
    /// при implicit null policy (явный выбор, не покрывающий всю область, блокируется).</summary>
    private void ValidateRecordingPolicy()
    {
        if (MaterialStateRecording.RecordShellLayers && MaterialStateRecording.ShellIntegrationPoints is { } shellIps)
        {
            foreach (NormalizedShellElement element in Elements)
            {
                if (shellIps.Any(ip => ip > element.IntegrationPointCount))
                    throw new InvalidOperationException(
                        $"recording_selection_invalid: shell IP {string.Join(",", shellIps)} не существует у элемента {element.Tag} (число IP {element.IntegrationPointCount}).");
            }
        }

        if (MaterialStateRecording.RecordBeamFibers)
        {
            if (MaterialStateRecording.BeamIntegrationPoints is { } beamIps)
            {
                foreach (FemNonlinearElement beam in NonlinearBeamElements)
                {
                    if (beamIps.Any(ip => ip > beam.NumIntegrationPoints))
                        throw new InvalidOperationException(
                            $"recording_selection_invalid: beam IP {string.Join(",", beamIps)} не существует у элемента {beam.Tag} (число IP {beam.NumIntegrationPoints}).");
                }
            }

            if (MaterialStateRecording.BeamFiberIndices is { } fiberIndices)
            {
                foreach (OpenSeesSectionModel section in NonlinearBeamSections.Values)
                {
                    if (fiberIndices.Any(index => index >= section.Fibers.Count))
                        throw new InvalidOperationException(
                            $"recording_selection_invalid: beam fiber index {string.Join(",", fiberIndices)} не существует в секции (число fibers {section.Fibers.Count}).");
                }
            }
        }
    }
```

Отрицательные и нулевые индексы уже блокируются `ShellStateRecordingPolicy.Validate()` (существующий код, вызываемый в `Validate()`).

2) В `OpenCS.OpenSees/Tcl/ShellTclGenerator.cs`, в `BuildBeamFiberLocations`, заменить silent filtering на явные исключения:

```csharp
            int[] ips = model.MaterialStateRecording.BeamIntegrationPoints is { } requestedIps
                ? requestedIps.Distinct().OrderBy(ip => ip).ToArray()
                : Enumerable.Range(1, element.NumIntegrationPoints).ToArray();
            foreach (int ip in ips)
                if (ip < 1 || ip > element.NumIntegrationPoints)
                    throw new InvalidOperationException(
                        $"recording_selection_invalid: IP {ip} не существует у beam-элемента {element.Tag} (число IP {element.NumIntegrationPoints}).");
            int[] fibers = model.MaterialStateRecording.BeamFiberIndices is { } requestedFibers
                ? requestedFibers.Distinct().OrderBy(index => index).ToArray()
                : Enumerable.Range(0, section.Fibers.Count).ToArray();
            foreach (int fiberIndex in fibers)
                if (fiberIndex < 0 || fiberIndex >= section.Fibers.Count)
                    throw new InvalidOperationException(
                        $"recording_selection_invalid: fiber {fiberIndex} не существует в секции {element.SectionTag} (число fibers {section.Fibers.Count}).");
```

- [ ] **Step 4: Run the tests to verify pass**

```powershell
dotnet test OpenCS.OpenSees.Tests --filter "FullyQualifiedName~ShellOpenSeesModelTests|FullyQualifiedName~ShellTclGeneratorTests"
dotnet build OpenCS.sln
```

Expected: PASS. Существующие тесты (`Validate_AcceptsSortedDistinctMaterialStateFilters` c `[2,1,2]` на Q4, `Generate_EmitsShellLayerStressAndStrainRecordersWithoutQ4T3Collisions` с null policy) остаются зелёными.

- [ ] **Step 5: Commit**

```bash
git add OpenCS.OpenSees/Structural/ShellOpenSeesModel.cs OpenCS.OpenSees/Tcl/ShellTclGenerator.cs OpenCS.OpenSees.Tests/ShellOpenSeesModelTests.cs OpenCS.OpenSees.Tests/ShellTclGeneratorTests.cs
git commit -m "feat(shell): strict recording policy validation and no silent filtering"
```

## Task 6: Audit diagnostics, policy, regularization policy, preflight

**Files:**
- Create: `OpenCS.OpenSees/Audit/ShellDiagnostics.cs`
- Create: `OpenCS.OpenSees/Audit/ShellAuditPolicy.cs`
- Create: `OpenCS.OpenSees/Audit/ShellRegularizationPolicy.cs`
- Create: `OpenCS.OpenSees/Audit/ShellAuditPreflight.cs`
- Create: `OpenCS.OpenSees.Tests/Audit/ShellAuditPolicyTests.cs`

- [ ] **Step 1: Write the failing tests**

Создать `OpenCS.OpenSees.Tests/Audit/ShellAuditPolicyTests.cs`:

```csharp
using OpenCS.OpenSees.Audit;
using OpenCS.OpenSees.Structural;
using OpenCS.OpenSees.Tests.Fixtures;

namespace OpenCS.OpenSees.Tests.Audit;

public sealed class ShellAuditPolicyTests
{
    [Fact]
    public void AuditPolicy_DefaultsToDiagnosticOnlyWithExternalWorkEnergy()
    {
        var policy = new ShellAuditPolicy();

        Assert.Equal(ShellAuditMode.DiagnosticOnly, policy.Mode);
        Assert.Equal(["stress", "strain"], policy.RequiredResponses);
        Assert.Equal(ShellEnergyConfidenceRequirement.ExternalWorkOnly, policy.MinEnergyConfidence);
        Assert.Equal(ShellRegularizationMode.None, policy.Regularization.Mode);
        Assert.Equal(3, policy.SensitivityLevels.Count);
    }

    [Fact]
    public void Preflight_StrictCrackBand_WithoutAdapter_BlocksWithRegularizationUnsupported()
    {
        var policy = new ShellAuditPolicy
        {
            Mode = ShellAuditMode.Strict,
            Regularization = new ShellRegularizationPolicy { Mode = ShellRegularizationMode.CrackBand }
        };

        ShellAuditPreflightResult result = ShellAuditPreflight.Run(
            ShellModelFixtures.Q4Elastic(), V2Catalog(), policy, new ShellRegularizationCapability([]));

        Assert.False(result.IsCalculable);
        Assert.Contains(result.Diagnostics, d =>
            d.Code == ShellDiagnosticCodes.RegularizationUnsupported &&
            d.Severity == ShellDiagnosticSeverity.Blocking);
    }

    [Fact]
    public void Preflight_DiagnosticOnlyCrackBand_WithoutAdapter_WarnsButCalculable()
    {
        var policy = new ShellAuditPolicy
        {
            Mode = ShellAuditMode.DiagnosticOnly,
            Regularization = new ShellRegularizationPolicy { Mode = ShellRegularizationMode.CrackBand }
        };

        ShellAuditPreflightResult result = ShellAuditPreflight.Run(
            ShellModelFixtures.Q4Elastic(), V2Catalog(), policy, new ShellRegularizationCapability([]));

        Assert.True(result.IsCalculable);
        Assert.Contains(result.Diagnostics, d =>
            d.Code == ShellDiagnosticCodes.RegularizationUnsupported &&
            d.Severity == ShellDiagnosticSeverity.Warning);
    }

    [Fact]
    public void Preflight_V1LegacyCatalog_BlocksWithProvenanceMissing()
    {
        var policy = new ShellAuditPolicy { Mode = ShellAuditMode.Strict };
        var legacyCatalog = new ShellStateCatalog(1, [], [], []);

        ShellAuditPreflightResult result = ShellAuditPreflight.Run(
            ShellModelFixtures.Q4Elastic(), legacyCatalog, policy, new ShellRegularizationCapability([]));

        Assert.False(result.IsCalculable);
        Assert.Contains(result.Diagnostics, d => d.Code == ShellDiagnosticCodes.StateCatalogProvenanceMissing);
    }

    [Fact]
    public void Preflight_MissingCatalog_BlocksWithProvenanceMissing()
    {
        var result = ShellAuditPreflight.Run(
            ShellModelFixtures.Q4Elastic(), null, new ShellAuditPolicy(), new ShellRegularizationCapability([]));

        Assert.False(result.IsCalculable);
        Assert.Contains(result.Diagnostics, d => d.Code == ShellDiagnosticCodes.StateCatalogProvenanceMissing);
    }

    [Fact]
    public void Diagnostic_CarriesCodeSeverityMessageAndOptionalContext()
    {
        var diagnostic = new ShellDiagnostic(
            ShellDiagnosticCodes.RecordingSelectionInvalid, ShellDiagnosticSeverity.Blocking,
            "Некорректный выбор IP.", ElementTag: 10, IntegrationPoint: 4);

        Assert.Equal("recording_selection_invalid", diagnostic.Code);
        Assert.Equal(ShellDiagnosticSeverity.Blocking, diagnostic.Severity);
        Assert.Equal(10, diagnostic.ElementTag);
        Assert.Equal(4, diagnostic.IntegrationPoint);
    }

    private static ShellStateCatalog V2Catalog() => new(2, [], [], []);
}
```

- [ ] **Step 2: Run the tests to verify failure**

```powershell
dotnet test OpenCS.OpenSees.Tests --filter "FullyQualifiedName~ShellAuditPolicyTests"
```

Expected: FAIL — пространство `OpenCS.OpenSees.Audit` и типы не существуют (compile error).

- [ ] **Step 3: Write the minimal implementation**

Создать `OpenCS.OpenSees/Audit/ShellDiagnostics.cs`:

```csharp
namespace OpenCS.OpenSees.Audit;

/// <summary>Severity структурированной диагностики audit.</summary>
public enum ShellDiagnosticSeverity
{
    /// <summary>Информация без влияния на verdict.</summary>
    Info,

    /// <summary>Предупреждение — результат usable с ограничениями.</summary>
    Warning,

    /// <summary>Блокирующая диагностика — verdict Blocked.</summary>
    Blocking
}

/// <summary>Структурированная диагностика: стабильный код, severity, сообщение и контекст.</summary>
public sealed record ShellDiagnostic(
    string Code,
    ShellDiagnosticSeverity Severity,
    string Message,
    int? ElementTag = null,
    int? IntegrationPoint = null,
    int? LayerIndex = null,
    string? ArtifactDirectory = null,
    string? SourceFingerprint = null);

/// <summary>Стабильные blocking/warning коды диагностик audit (спека §13).</summary>
public static class ShellDiagnosticCodes
{
    public const string RebarAngleInvalid = "rebar_angle_invalid";
    public const string StateCatalogProvenanceMissing = "state_catalog_provenance_missing";
    public const string UnsupportedShellResponse = "unsupported_shell_response";
    public const string RecordingSelectionInvalid = "recording_selection_invalid";
    public const string MaterialTangentUnavailable = "material_tangent_unavailable";
    public const string RegularizationUnsupported = "regularization_unsupported";
    public const string EnergyUnavailable = "energy_unavailable";
    public const string EquilibriumNotSatisfied = "equilibrium_not_satisfied";
    public const string MeshDependent = "mesh_dependent";
    public const string ResultOutputIncomplete = "result_output_incomplete";
    public const string SensitivityCaseIncomplete = "sensitivity_case_incomplete";
}
```

Создать `OpenCS.OpenSees/Audit/ShellRegularizationPolicy.cs`:

```csharp
namespace OpenCS.OpenSees.Audit;

/// <summary>Режим regularization мягчения: наличие enum/поля НЕ является применением regularization —
/// требуется IShellRegularizedMaterialAdapter (§11).</summary>
public enum ShellRegularizationMode
{
    /// <summary>Regularization не применяется.</summary>
    None,

    /// <summary>Характеристическая длина элемента (sqrt(area)) передаётся в native material.</summary>
    ElementCharacteristicLength,

    /// <summary>Метод полосы растрескивания (CrackBand).</summary>
    CrackBand,

    /// <summary>Энергия разрушения (FractureEnergy).</summary>
    FractureEnergy
}

/// <summary>Метод вычисления характеристической длины элемента.</summary>
public enum ShellCharacteristicLengthMethod
{
    /// <summary>Квадратный корень из площади элемента: sqrt(area).</summary>
    SqrtArea
}

/// <summary>Политика regularization audit-расчёта.</summary>
public sealed record ShellRegularizationPolicy
{
    /// <summary>Запрошенный режим regularization.</summary>
    public ShellRegularizationMode Mode { get; init; } = ShellRegularizationMode.None;

    /// <summary>Метод вычисления характеристической длины.</summary>
    public ShellCharacteristicLengthMethod Method { get; init; } = ShellCharacteristicLengthMethod.SqrtArea;
}
```

Создать `OpenCS.OpenSees/Audit/IShellRegularizedMaterialAdapter.cs`:

```csharp
using OpenCS.OpenSees.Model;

namespace OpenCS.OpenSees.Audit;

/// <summary>Контракт adapter-а, ФАКТИЧЕСКИ применяющего regularization в native material mapping.
/// Наличие enum или поля в manifest не является применением regularization (§11).</summary>
public interface IShellRegularizedMaterialAdapter
{
    /// <summary>Режим regularization, который реализует adapter.</summary>
    ShellRegularizationMode Mode { get; }

    /// <summary>Проверяет, может ли adapter применить свой режим к данному native материалу.</summary>
    bool CanApply(NativeShellMaterialSpec spec);
}
```

Создать `OpenCS.OpenSees/Audit/ShellRegularizationCapability.cs`:

```csharp
using OpenCS.OpenSees.Model;

namespace OpenCS.OpenSees.Audit;

/// <summary>Registry adapter-ов regularization. По умолчанию пуст: текущий срез не объявляет
/// capability PlasticDamageConcretePlaneStress без реального OpenSees verification, поэтому
/// ElementCharacteristicLength/CrackBand/FractureEnergy не поддерживаются (§11).</summary>
public sealed class ShellRegularizationCapability
{
    private readonly IReadOnlyList<IShellRegularizedMaterialAdapter> _adapters;

    public ShellRegularizationCapability(IReadOnlyList<IShellRegularizedMaterialAdapter> adapters)
    {
        _adapters = adapters ?? throw new ArgumentNullException(nameof(adapters));
    }

    /// <summary>Проверяет, поддерживает ли какой-либо adapter заданный режим.</summary>
    public bool CanApply(ShellRegularizationMode mode) =>
        _adapters.Any(adapter => adapter.Mode == mode);

    /// <summary>Проверяет, поддерживает ли adapter режим для конкретного native материала.</summary>
    public bool CanApplyTo(ShellRegularizationMode mode, NativeShellMaterialSpec spec) =>
        _adapters.Any(adapter => adapter.Mode == mode && adapter.CanApply(spec));

    /// <summary>Режимы, поддерживаемые зарегистрированными adapter-ами.</summary>
    public IReadOnlyList<ShellRegularizationMode> SupportedModes =>
        _adapters.Select(adapter => adapter.Mode).Distinct().ToArray();
}
```

Создать `OpenCS.OpenSees/Audit/ShellAuditPolicy.cs`:

```csharp
namespace OpenCS.OpenSees.Audit;

/// <summary>Режим аудита: строгий блокирует verdict при невыполненных обязательных требованиях,
/// diagnostic-only превращает их в Warning.</summary>
public enum ShellAuditMode
{
    /// <summary>Обязательные checks блокируют расчёт (Blocked).</summary>
    Strict,

    /// <summary>Результат usable с явно перечисленными ограничениями (Warning).</summary>
    DiagnosticOnly
}

/// <summary>Verdict audit-расчёта (§8).</summary>
public enum ShellAuditVerdict
{
    /// <summary>Все обязательные checks подтверждены.</summary>
    Passed,

    /// <summary>Результат usable с явно перечисленными ограничениями.</summary>
    Warning,

    /// <summary>Preflight или обязательная capability не выполнены.</summary>
    Blocked,

    /// <summary>Три sensitivity-запуска сошлись, но tolerance превышена.</summary>
    MeshDependent
}

/// <summary>Требуемый минимальный confidence energy audit (§10).</summary>
public enum ShellEnergyConfidenceRequirement
{
    /// <summary>Native material/backend вернул проверенный energy response.</summary>
    NativeResponse,

    /// <summary>Численная интеграция сопряжённых component pairs.</summary>
    StateIntegral,

    /// <summary>Работа force/moment nodal loads по трапециям.</summary>
    ExternalWorkOnly,

    /// <summary>Обязательные исходные данные отсутствуют.</summary>
    Unavailable
}

/// <summary>Политика audit-расчёта shell-модели: tolerances, обязательные response, energy,
/// regularization, sensitivity-уровни и fingerprint политики.</summary>
public sealed record ShellAuditPolicy
{
    /// <summary>Режим Strict или DiagnosticOnly.</summary>
    public ShellAuditMode Mode { get; init; } = ShellAuditMode.DiagnosticOnly;

    /// <summary>Абсолютный допуск равновесия (шесть DOF).</summary>
    public double AbsoluteEquilibriumTolerance { get; init; } = 1e-3;

    /// <summary>Относительный допуск равновесия.</summary>
    public double RelativeEquilibriumTolerance { get; init; } = 1e-3;

    /// <summary>Обязательные response-имена материала.</summary>
    public IReadOnlyList<string> RequiredResponses { get; init; } = ["stress", "strain"];

    /// <summary>Минимальный confidence energy.</summary>
    public ShellEnergyConfidenceRequirement MinEnergyConfidence { get; init; } =
        ShellEnergyConfidenceRequirement.ExternalWorkOnly;

    /// <summary>Политика regularization.</summary>
    public ShellRegularizationPolicy Regularization { get; init; } = new();

    /// <summary>Уровни mesh sensitivity (coarse/medium/fine).</summary>
    public IReadOnlyList<ShellSensitivityLevel> SensitivityLevels { get; init; } =
        [ShellSensitivityLevel.Coarse, ShellSensitivityLevel.Medium, ShellSensitivityLevel.Fine];

    /// <summary>Относительный допуск сравнения sensitivity-метрик.</summary>
    public double SensitivityRelativeTolerance { get; init; } = 0.1;

    /// <summary>Fingerprint содержательных настроек политики — изменение меняет fingerprint среза.</summary>
    public string Fingerprint { get; init; } = "";
}
```

Создать `OpenCS.OpenSees/Audit/ShellAuditPreflight.cs`:

```csharp
using OpenCS.OpenSees.Structural;

namespace OpenCS.OpenSees.Audit;

/// <summary>Результат preflight: можно ли вообще считать расчёт + структурированные диагностики
/// (без запуска OpenSees).</summary>
public sealed record ShellAuditPreflightResult(
    bool IsCalculable,
    IReadOnlyList<ShellDiagnostic> Diagnostics);

/// <summary>Preflight audit: provenance catalog, regularization capability и обязательные response.
/// Исключения ShellOpenSeesModel.Validate() преобразуются в result_output_incomplete.</summary>
public static class ShellAuditPreflight
{
    public static ShellAuditPreflightResult Run(
        ShellOpenSeesModel model,
        ShellStateCatalog? catalog,
        ShellAuditPolicy policy,
        ShellRegularizationCapability regularization)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(regularization);

        var diagnostics = new List<ShellDiagnostic>();
        bool calculable = true;

        if (catalog is null)
        {
            diagnostics.Add(new ShellDiagnostic(
                ShellDiagnosticCodes.StateCatalogProvenanceMissing,
                ShellDiagnosticSeverity.Blocking,
                "Отсутствует material-state catalog — provenance недоступен."));
            calculable = false;
        }
        else if (catalog.ProvenanceKind == ShellStateCatalogProvenanceKind.V1LegacyMissing)
        {
            diagnostics.Add(new ShellDiagnostic(
                ShellDiagnosticCodes.StateCatalogProvenanceMissing,
                ShellDiagnosticSeverity.Blocking,
                "Material-state catalog v1 без provenance; строгий audit невозможен."));
            calculable = false;
        }

        if (policy.Regularization.Mode != ShellRegularizationMode.None &&
            !regularization.CanApply(policy.Regularization.Mode))
        {
            bool blocking = policy.Mode == ShellAuditMode.Strict;
            diagnostics.Add(new ShellDiagnostic(
                ShellDiagnosticCodes.RegularizationUnsupported,
                blocking ? ShellDiagnosticSeverity.Blocking : ShellDiagnosticSeverity.Warning,
                $"Режим regularization «{policy.Regularization.Mode}» не поддерживается ни одним " +
                "native-адаптером; regularization_applied=false; результат не называется mesh-independent."));
            if (blocking) calculable = false;
        }

        foreach (string response in policy.RequiredResponses)
        {
            if (model.Materials.Any(material => !material.Spec.HasResponse(response)))
            {
                diagnostics.Add(new ShellDiagnostic(
                    ShellDiagnosticCodes.UnsupportedShellResponse,
                    ShellDiagnosticSeverity.Blocking,
                    $"Обязательный response «{response}» не поддерживается всеми материалами модели."));
                calculable = false;
            }
        }

        return new ShellAuditPreflightResult(calculable, diagnostics);
    }
}
```

- [ ] **Step 4: Run the tests to verify pass**

```powershell
dotnet test OpenCS.OpenSees.Tests --filter "FullyQualifiedName~ShellAuditPolicyTests"
dotnet build OpenCS.sln
```

Expected: PASS. `V2Catalog()` = `new ShellStateCatalog(2, [], [], [])` → `ProvenanceKind == V2WithProvenance` (Task 3), поэтому preflight проходит provenance-проверку.

- [ ] **Step 5: Commit**

```bash
git add OpenCS.OpenSees/Audit/ShellDiagnostics.cs OpenCS.OpenSees/Audit/ShellAuditPolicy.cs OpenCS.OpenSees/Audit/ShellRegularizationPolicy.cs OpenCS.OpenSees/Audit/IShellRegularizedMaterialAdapter.cs OpenCS.OpenSees/Audit/ShellRegularizationCapability.cs OpenCS.OpenSees/Audit/ShellAuditPreflight.cs OpenCS.OpenSees.Tests/Audit/ShellAuditPolicyTests.cs
git commit -m "feat(audit): policy, regularization contract and preflight diagnostics"
```

## Task 7: Generalized resultants и staged equilibrium auditor

**Files:**
- Create: `OpenCS.OpenSees/Audit/ShellResultants.cs`
- Create: `OpenCS.OpenSees/Audit/ShellEquilibriumAuditor.cs`
- Create: `OpenCS.OpenSees.Tests/Audit/ShellEquilibriumAuditorTests.cs`

- [ ] **Step 1: Write the failing tests**

Создать `OpenCS.OpenSees.Tests/Audit/ShellEquilibriumAuditorTests.cs`:

```csharp
using OpenCS.OpenSees.Audit;
using OpenCS.OpenSees.Model;
using OpenCS.OpenSees.Structural;

namespace OpenCS.OpenSees.Tests.Audit;

public sealed class ShellEquilibriumAuditorTests
{
    [Fact]
    public void AppliedResultantAtStep_SingleStage_ScalesByCurrentLambda()
    {
        var stages = new[]
        {
            new ShellNonlinearStage
            {
                Tag = "s0",
                MaxLoadFactor = 1.0,
                Loads = [new ShellNodalLoad(2, 0, 0, -1000, 0, 0, 0)]
            }
        };

        ShellResultant applied = ShellEquilibriumAuditor.AppliedResultantAtStep(stages, 0, 0.5);

        Assert.Equal(0, applied.Fx, 12);
        Assert.Equal(-500, applied.Fz, 12);
    }

    [Fact]
    public void AppliedResultantAtStep_MultiStage_UsesPreviousStageMaxPlusCurrentLambda()
    {
        var stages = new[]
        {
            new ShellNonlinearStage { Tag = "dead", MaxLoadFactor = 2.0,
                Loads = [new ShellNodalLoad(2, 0, 0, -500, 0, 0, 0)] },
            new ShellNonlinearStage { Tag = "live", MaxLoadFactor = 1.0,
                Loads = [new ShellNodalLoad(3, 0, 0, -300, 0, 0, 0)] }
        };

        ShellResultant applied = ShellEquilibriumAuditor.AppliedResultantAtStep(stages, 1, 0.5);

        // P = Pstage[0]*MaxLoadFactor[0] + Pstage[1]*0.5 = -500*2 + (-300)*0.5 = -1150
        Assert.Equal(-1150, applied.Fz, 12);
    }

    [Fact]
    public void NodalForce_MomentAboutOrigin_IsCrossProductPlusMoment()
    {
        // Сила Fz = 1000 в точке r = (2, 0, 0): r x F = (0, -2000, 0).
        ShellResultant force = ShellResultantMath.NodalForce(2, 0, 0, new ShellResultant(0, 0, 1000, 0, 0, 0));

        Assert.Equal(0, force.Fx, 12);
        Assert.Equal(0, force.Fz, 12);
        Assert.Equal(-2000, force.My, 12);
    }

    [Fact]
    public void ReactionResultant_SumsForceAndMomentWithNodeCoordinates()
    {
        var nodes = new Dictionary<int, NormalizedShellNode>
        {
            [1] = new(1, 0, 0, 0, new bool[6], null),
            [2] = new(2, 4, 0, 0, new bool[6], null)
        };
        var reactions = new[]
        {
            new ShellNodeReaction(1, 0, 0, 500, 0, 0, 0),
            new ShellNodeReaction(2, 0, 0, 500, 0, 0, 0)
        };

        ShellResultant resultant = ShellEquilibriumAuditor.ReactionResultant(reactions, nodes);

        Assert.Equal(1000, resultant.Fz, 12);
        Assert.Equal(-2000, resultant.My, 12); // 0*500 + (-4*500)
    }

    [Fact]
    public void Evaluate_WithinTolerance_Passes()
    {
        var report = ShellEquilibriumAuditor.Evaluate(
            stepIndex: 1, stageIndex: 0, loadFactor: 1.0,
            applied: new ShellResultant(0, 0, -1000, 0, 0, 0),
            reaction: new ShellResultant(0, 0, 1000.0005, 0, 0, 0),
            new ShellAuditPolicy { AbsoluteEquilibriumTolerance = 1e-3, RelativeEquilibriumTolerance = 1e-3 });

        Assert.True(report.Pass);
        Assert.True(report.AbsoluteError <= 1e-3);
    }

    [Fact]
    public void Evaluate_BeyondTolerance_FailsWithEquilibriumNotSatisfiedCapability()
    {
        var report = ShellEquilibriumAuditor.Evaluate(
            stepIndex: 1, stageIndex: 0, loadFactor: 1.0,
            applied: new ShellResultant(0, 0, -1000, 0, 0, 0),
            reaction: new ShellResultant(0, 0, 700, 0, 0, 0),
            new ShellAuditPolicy { AbsoluteEquilibriumTolerance = 1e-3, RelativeEquilibriumTolerance = 1e-3 });

        Assert.False(report.Pass);
    }
}
```

- [ ] **Step 2: Run the tests to verify failure**

```powershell
dotnet test OpenCS.OpenSees.Tests --filter "FullyQualifiedName~ShellEquilibriumAuditorTests"
```

Expected: FAIL — типы не существуют (compile error).

- [ ] **Step 3: Write the minimal implementation**

Создать `OpenCS.OpenSees/Audit/ShellResultants.cs`:

```csharp
namespace OpenCS.OpenSees.Audit;

/// <summary>Шестикомпонентный глобальный resultant (Fx, Fy, Fz, Mx, My, Mz) в Н и Н·м.</summary>
public sealed record ShellResultant(double Fx, double Fy, double Fz, double Mx, double My, double Mz)
{
    /// <summary>Нулевой resultant.</summary>
    public static ShellResultant Zero => new(0, 0, 0, 0, 0, 0);

    /// <summary>Складывает два resultanta.</summary>
    public static ShellResultant operator +(ShellResultant left, ShellResultant right) =>
        new(left.Fx + right.Fx, left.Fy + right.Fy, left.Fz + right.Fz,
            left.Mx + right.Mx, left.My + right.My, left.Mz + right.Mz);

    /// <summary>Умножает resultant на скаляр (коэффициент нагрузки).</summary>
    public static ShellResultant operator *(ShellResultant value, double scale) =>
        new(value.Fx * scale, value.Fy * scale, value.Fz * scale,
            value.Mx * scale, value.My * scale, value.Mz * scale);

    /// <summary>Максимальная по модулю компонента.</summary>
    public double MaxAbsoluteComponent =>
        new[] { Fx, Fy, Fz, Mx, My, Mz }.Max(Math.Abs);
}

/// <summary>Математика глобальных resultants: момент от узловой силы в точке r — r × F + M.</summary>
public static class ShellResultantMath
{
    /// <summary>Момент силы F, приложенной в точке (rx, ry, rz), относительно глобального начала:
    /// r × F + M. Используется и для nodal forces, и для reactions (§9.1).</summary>
    public static ShellResultant NodalForce(double rx, double ry, double rz, ShellResultant force) => new(
        force.Fx, force.Fy, force.Fz,
        ry * force.Fz - rz * force.Fy + force.Mx,
        rz * force.Fx - rx * force.Fz + force.My,
        rx * force.Fy - ry * force.Fx + force.Mz);
}
```

Создать `OpenCS.OpenSees/Audit/ShellEquilibriumAuditor.cs`:

```csharp
using OpenCS.OpenSees.Structural;

namespace OpenCS.OpenSees.Audit;

/// <summary>Отчёт равновесия одного шага: applied, reaction, residual и pass/fail по шести DOF.</summary>
public sealed record ShellEquilibriumStepReport(
    int StepIndex,
    int StageIndex,
    double LoadFactor,
    ShellResultant Applied,
    ShellResultant Reaction,
    ShellResultant Residual,
    double AbsoluteError,
    double RelativeError,
    bool Pass);

/// <summary>Проверка глобального равновесия сил и моментов. Момент от узловой силы в точке r
/// равен r × F + M — проверяются не только суммы сил, но и моменты с учётом координат узлов (§9).</summary>
public static class ShellEquilibriumAuditor
{
    /// <summary>Восстанавливает полный applied resultant шага: для стадии k с текущим λ —
    /// P = Σ(Pstage[i] · MaxLoadFactor[i]) для i &lt; k + Pstage[k] · λ (§9.2, loadConst).</summary>
    public static ShellResultant AppliedResultantAtStep(
        IReadOnlyList<ShellNonlinearStage> stages,
        int stageIndex,
        double loadFactor)
    {
        ShellResultant total = ShellResultant.Zero;
        for (int i = 0; i < stages.Count && i < stageIndex; i++)
            total += StageResultant(stages[i]) * stages[i].MaxLoadFactor;
        if (stageIndex >= 0 && stageIndex < stages.Count)
            total += StageResultant(stages[stageIndex]) * loadFactor;
        return total;
    }

    /// <summary>Суммарный шестикомпонентный resultant узловых нагрузок стадии.</summary>
    private static ShellResultant StageResultant(ShellNonlinearStage stage)
    {
        ShellResultant sum = ShellResultant.Zero;
        foreach (ShellNodalLoad load in stage.Loads)
            sum += new ShellResultant(load.Fx, load.Fy, load.Fz, load.Mx, load.My, load.Mz);
        return sum;
    }

    /// <summary>Суммарный resultant реакций с моментами относительно глобального начала (r × F + M).</summary>
    public static ShellResultant ReactionResultant(
        IReadOnlyList<ShellNodeReaction> reactions,
        IReadOnlyDictionary<int, NormalizedShellNode> nodes)
    {
        ShellResultant sum = ShellResultant.Zero;
        foreach (ShellNodeReaction reaction in reactions)
        {
            NormalizedShellNode node = nodes[reaction.NodeTag];
            sum += ShellResultantMath.NodalForce(node.X, node.Y, node.Z,
                new ShellResultant(reaction.Fx, reaction.Fy, reaction.Fz,
                    reaction.Mx, reaction.My, reaction.Mz));
        }
        return sum;
    }

    /// <summary>Вычисляет residual P + R и проверяет равновесие по policy tolerances.</summary>
    public static ShellEquilibriumStepReport Evaluate(
        int stepIndex,
        int stageIndex,
        double loadFactor,
        ShellResultant applied,
        ShellResultant reaction,
        ShellAuditPolicy policy)
    {
        ShellResultant residual = applied + reaction;
        double absolute = residual.MaxAbsoluteComponent;
        double scale = Math.Max(1.0, applied.MaxAbsoluteComponent);
        double relative = absolute / scale;
        bool pass = absolute <= policy.AbsoluteEquilibriumTolerance ||
                    relative <= policy.RelativeEquilibriumTolerance;

        return new ShellEquilibriumStepReport(
            stepIndex, stageIndex, loadFactor, applied, reaction, residual,
            absolute, relative, pass);
    }
}
```

- [ ] **Step 4: Run the tests to verify pass**

```powershell
dotnet test OpenCS.OpenSees.Tests --filter "FullyQualifiedName~ShellEquilibriumAuditorTests"
dotnet build OpenCS.sln
```

Expected: PASS. `AppliedResultantAtStep` — при `stageIndex=1`, `stages.Count=2`: цикл `i < 1` берёт стадию 0 × MaxLoadFactor 2.0 → -1000, затем `Pstage[1] · 0.5` → -150, итог -1150.

- [ ] **Step 5: Commit**

```bash
git add OpenCS.OpenSees/Audit/ShellResultants.cs OpenCS.OpenSees/Audit/ShellEquilibriumAuditor.cs OpenCS.OpenSees.Tests/Audit/ShellEquilibriumAuditorTests.cs
git commit -m "feat(audit): generalized resultants and staged equilibrium check"
```

## Task 8: Energy auditor

**Files:**
- Create: `OpenCS.OpenSees/Audit/ShellEnergyAuditor.cs`
- Create: `OpenCS.OpenSees.Tests/Audit/ShellEnergyAuditorTests.cs`

- [ ] **Step 1: Write the failing tests**

Создать `OpenCS.OpenSees.Tests/Audit/ShellEnergyAuditorTests.cs`:

```csharp
using OpenCS.OpenSees.Audit;
using OpenCS.OpenSees.Structural;

namespace OpenCS.OpenSees.Tests.Audit;

public sealed class ShellEnergyAuditorTests
{
    [Fact]
    public void DetermineConfidence_NativeEnergyResponse_IsNativeResponse()
    {
        ShellEnergyConfidence confidence = ShellEnergyAuditor.DetermineConfidence(
            hasNativeEnergyResponse: true, hasStateIntegralData: true, hasLoadHistory: true);

        Assert.Equal(ShellEnergyConfidence.NativeResponse, confidence);
    }

    [Fact]
    public void DetermineConfidence_StateIntegralWithoutNative_IsStateIntegral()
    {
        ShellEnergyConfidence confidence = ShellEnergyAuditor.DetermineConfidence(
            hasNativeEnergyResponse: false, hasStateIntegralData: true, hasLoadHistory: true);

        Assert.Equal(ShellEnergyConfidence.StateIntegral, confidence);
    }

    [Fact]
    public void DetermineConfidence_LoadHistoryOnly_IsExternalWorkOnly()
    {
        ShellEnergyConfidence confidence = ShellEnergyAuditor.DetermineConfidence(
            hasNativeEnergyResponse: false, hasStateIntegralData: false, hasLoadHistory: true);

        Assert.Equal(ShellEnergyConfidence.ExternalWorkOnly, confidence);
    }

    [Fact]
    public void DetermineConfidence_NoSources_IsUnavailable()
    {
        ShellEnergyConfidence confidence = ShellEnergyAuditor.DetermineConfidence(
            hasNativeEnergyResponse: false, hasStateIntegralData: false, hasLoadHistory: false);

        Assert.Equal(ShellEnergyConfidence.Unavailable, confidence);
    }

    [Fact]
    public void ExternalWork_TrapezoidRule_IntegratesWorkDotOverLoadFactor()
    {
        var samples = new[]
        {
            new ShellEnergySample(LoadFactor: 0.0, WorkDot: 0.0),
            new ShellEnergySample(LoadFactor: 0.5, WorkDot: 100.0),
            new ShellEnergySample(LoadFactor: 1.0, WorkDot: 250.0)
        };

        double work = ShellEnergyAuditor.ExternalWork(samples);

        // ½(0+100)·0.5 + ½(100+250)·0.5 = 25 + 87.5 = 112.5
        Assert.Equal(112.5, work, 12);
    }

    [Fact]
    public void ExternalWork_EmptySamples_IsZero()
    {
        Assert.Equal(0.0, ShellEnergyAuditor.ExternalWork([]), 12);
    }

    [Fact]
    public void KinematicReactionWork_SumsForceTimesDisplacementOverNodes()
    {
        var step = new RCShellStepResult(
            1, 0, 1.0, true,
            [new ShellNodeDisplacement(1, 0, 0, 0.001, 0, 0, 0)],
            [new ShellNodeReaction(1, 0, 0, 1000, 0, 0, 0)],
            [], [], []);

        double work = ShellEnergyAuditor.KinematicReactionWork([step]);

        Assert.Equal(1.0, work, 12);
    }
}
```

- [ ] **Step 2: Run the tests to verify failure**

```powershell
dotnet test OpenCS.OpenSees.Tests --filter "FullyQualifiedName~ShellEnergyAuditorTests"
```

Expected: FAIL — типы `ShellEnergyAuditor`, `ShellEnergyConfidence`, `ShellEnergySample` не существуют (compile error).

- [ ] **Step 3: Write the minimal implementation**

Создать `OpenCS.OpenSees/Audit/ShellEnergyAuditor.cs`:

```csharp
using OpenCS.OpenSees.Structural;

namespace OpenCS.OpenSees.Audit;

/// <summary>Уровень достоверности energy audit (§10). Порядок объявления важен: значение enum
/// растёт от наиболее достоверного к наименее — NativeResponse(0) &lt; StateIntegral(1) &lt;
/// ExternalWorkOnly(2) &lt; Unavailable(3).</summary>
public enum ShellEnergyConfidence
{
    /// <summary>Native material/backend вернул проверенный energy response.</summary>
    NativeResponse,

    /// <summary>Численная интеграция сопряжённых component pairs из material-state catalog.</summary>
    StateIntegral,

    /// <summary>Работа force/moment nodal loads по трапециям.</summary>
    ExternalWorkOnly,

    /// <summary>Обязательные исходные данные отсутствуют.</summary>
    Unavailable
}

/// <summary>Точка на кривой внешней работы: коэффициент нагрузки и WorkDot = Σ(нагрузка·перемещение)
/// по всем узлам на этом шаге.</summary>
public sealed record ShellEnergySample(double LoadFactor, double WorkDot);

/// <summary>Energy audit: определение confidence и численная интеграция внешней/кинематической работы (§10).</summary>
public static class ShellEnergyAuditor
{
    /// <summary>Определяет confidence по доступности источников energy: native response — приоритет,
    /// затем state integral, затем внешняя работа, иначе Unavailable.</summary>
    public static ShellEnergyConfidence DetermineConfidence(
        bool hasNativeEnergyResponse,
        bool hasStateIntegralData,
        bool hasLoadHistory)
    {
        if (hasNativeEnergyResponse)
            return ShellEnergyConfidence.NativeResponse;
        if (hasStateIntegralData)
            return ShellEnergyConfidence.StateIntegral;
        if (hasLoadHistory)
            return ShellEnergyConfidence.ExternalWorkOnly;
        return ShellEnergyConfidence.Unavailable;
    }

    /// <summary>Внешняя работа по правилу трапеций по loadFactor: Σ ½(Wdotᵢ + Wdotᵢ₋₁)·Δλ.</summary>
    public static double ExternalWork(IReadOnlyList<ShellEnergySample> samples)
    {
        double work = 0;
        for (int i = 1; i < samples.Count; i++)
        {
            ShellEnergySample previous = samples[i - 1];
            ShellEnergySample current = samples[i];
            work += 0.5 * (previous.WorkDot + current.WorkDot) * (current.LoadFactor - previous.LoadFactor);
        }
        return work;
    }

    /// <summary>Кинематическая работа реакций: Σ по шагам Σ по узлам (R · u) по шести DOF.</summary>
    public static double KinematicReactionWork(IReadOnlyList<RCShellStepResult> steps)
    {
        double total = 0;
        foreach (RCShellStepResult step in steps)
        {
            if (!step.Converged)
                continue;
            var displacements = step.Displacements.ToDictionary(displacement => displacement.NodeTag);
            foreach (ShellNodeReaction reaction in step.Reactions)
            {
                if (!displacements.TryGetValue(reaction.NodeTag, out ShellNodeDisplacement? displacement))
                    continue;
                total += reaction.Fx * displacement.Ux + reaction.Fy * displacement.Uy +
                         reaction.Fz * displacement.Uz + reaction.Mx * displacement.Rx +
                         reaction.My * displacement.Ry + reaction.Mz * displacement.Rz;
            }
        }
        return total;
    }
}
```

- [ ] **Step 4: Run the tests to verify pass**

```powershell
dotnet test OpenCS.OpenSees.Tests --filter "FullyQualifiedName~ShellEnergyAuditorTests"
dotnet build OpenCS.sln
```

Expected: PASS; build успешен. `ExternalWork` — трапеции 25 + 87.5 = 112.5; `KinematicReactionWork` — 1000·0.001 = 1.0.

- [ ] **Step 5: Commit**

```bash
git add OpenCS.OpenSees/Audit/ShellEnergyAuditor.cs OpenCS.OpenSees.Tests/Audit/ShellEnergyAuditorTests.cs
git commit -m "feat(audit): energy confidence and external/kinematic work"
```

## Task 9: Characteristic length и regularization capability

**Files:**
- Create: `OpenCS.OpenSees/Audit/ShellCharacteristicLength.cs`
- Create: `OpenCS.OpenSees.Tests/Audit/ShellRegularizationTests.cs`

- [ ] **Step 1: Write the failing tests**

Создать `OpenCS.OpenSees.Tests/Audit/ShellRegularizationTests.cs`:

```csharp
using OpenCS.OpenSees.Audit;
using OpenCS.OpenSees.Model;
using OpenCS.OpenSees.Structural;

namespace OpenCS.OpenSees.Tests.Audit;

public sealed class ShellRegularizationTests
{
    private static NormalizedShellNode[] QuadNodes() =>
    [
        new(1, 0, 0, 0, new bool[6], null),
        new(2, 2, 0, 0, new bool[6], null),
        new(3, 2, 1, 0, new bool[6], null),
        new(4, 0, 1, 0, new bool[6], null)
    ];

    private static NormalizedShellNode[] TriNodes() =>
    [
        new(1, 0, 0, 0, new bool[6], null),
        new(2, 2, 0, 0, new bool[6], null),
        new(3, 0, 1, 0, new bool[6], null)
    ];

    [Fact]
    public void CharacteristicLength_Q4_IsSquareRootOfArea()
    {
        var element = new NormalizedShellElement(10, ShellElementKind.ASDShellQ4, [1, 2, 3, 4],
            20, "s", ShellFrame.Identity, ShellIntegrationPolicy.Full, "q4");

        ShellElementCharacteristicLength length =
            ShellCharacteristicLength.Compute(element, QuadNodes().ToDictionary(node => node.Tag));

        Assert.Equal(2.0, length.Area, 12);
        Assert.Equal(Math.Sqrt(2.0), length.CharacteristicLength, 12);
    }

    [Fact]
    public void CharacteristicLength_T3_IsSquareRootOfArea()
    {
        var element = new NormalizedShellElement(11, ShellElementKind.ASDShellT3, [1, 2, 3],
            20, "s", ShellFrame.Identity, ShellIntegrationPolicy.Reduced, "t3");

        ShellElementCharacteristicLength length =
            ShellCharacteristicLength.Compute(element, TriNodes().ToDictionary(node => node.Tag));

        Assert.Equal(1.0, length.Area, 12);
        Assert.Equal(1.0, length.CharacteristicLength, 12);
    }

    [Fact]
    public void CharacteristicLength_DegenerateElement_Throws()
    {
        var element = new NormalizedShellElement(12, ShellElementKind.ASDShellT3, [1, 1, 1],
            20, "s", ShellFrame.Identity, ShellIntegrationPolicy.Reduced, "degenerate");

        Assert.Throws<ArgumentException>(() =>
            ShellCharacteristicLength.Compute(element, TriNodes().ToDictionary(node => node.Tag)));
    }

    [Fact]
    public void Capability_EmptyRegistry_SupportsNothing()
    {
        var capability = new ShellRegularizationCapability([]);

        Assert.False(capability.CanApply(ShellRegularizationMode.CrackBand));
        Assert.False(capability.CanApply(ShellRegularizationMode.ElementCharacteristicLength));
        Assert.Empty(capability.SupportedModes);
    }

    [Fact]
    public void Capability_FakeAdapter_MatchesModeAndSpec()
    {
        var capability = new ShellRegularizationCapability([new FakeCrackBandAdapter()]);
        var spec = new ElasticIsotropicShellMaterialSpec(30e9, 0.2);

        Assert.True(capability.CanApply(ShellRegularizationMode.CrackBand));
        Assert.False(capability.CanApply(ShellRegularizationMode.FractureEnergy));
        Assert.True(capability.CanApplyTo(ShellRegularizationMode.CrackBand, spec));
        Assert.Equal([ShellRegularizationMode.CrackBand], capability.SupportedModes);
    }

    private sealed class FakeCrackBandAdapter : IShellRegularizedMaterialAdapter
    {
        public ShellRegularizationMode Mode => ShellRegularizationMode.CrackBand;

        public bool CanApply(NativeShellMaterialSpec spec) => true;
    }
}
```

- [ ] **Step 2: Run the tests to verify failure**

```powershell
dotnet test OpenCS.OpenSees.Tests --filter "FullyQualifiedName~ShellRegularizationTests"
```

Expected: FAIL — `ShellCharacteristicLength` и `ShellElementCharacteristicLength` не существуют (compile error).

- [ ] **Step 3: Write the minimal implementation**

Создать `OpenCS.OpenSees/Audit/ShellCharacteristicLength.cs`:

```csharp
using OpenCS.OpenSees.Structural;

namespace OpenCS.OpenSees.Audit;

/// <summary>Характеристическая длина элемента: sqrt(площади) (§11).</summary>
public sealed record ShellElementCharacteristicLength(
    int ElementTag,
    ShellElementKind ElementKind,
    double Area,
    double CharacteristicLength);

/// <summary>Вычисление характеристической длины shell-элемента из геометрии узлов: sqrt(area) (§11.3).</summary>
public static class ShellCharacteristicLength
{
    /// <summary>Площадь и характеристическую длину (sqrt(area)) Q4/T3 элемента по координатам узлов.</summary>
    public static ShellElementCharacteristicLength Compute(
        NormalizedShellElement element,
        IReadOnlyDictionary<int, NormalizedShellNode> nodes)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(nodes);

        double area = element.Kind switch
        {
            ShellElementKind.ASDShellQ4 => QuadArea(element, nodes),
            ShellElementKind.ASDShellT3 => TriangleArea(element, nodes),
            _ => throw new ArgumentOutOfRangeException(nameof(element), $"Неизвестный тип элемента {element.Kind}.")
        };

        if (!double.IsFinite(area) || area <= 0)
            throw new ArgumentException($"Элемент {element.Tag} имеет вырожденную площадь {area}.", nameof(element));

        return new ShellElementCharacteristicLength(element.Tag, element.Kind, area, Math.Sqrt(area));
    }

    private static double QuadArea(NormalizedShellElement element, IReadOnlyDictionary<int, NormalizedShellNode> nodes)
    {
        double[] a = Node(element, nodes, element.NodeTags[0]);
        double[] b = Node(element, nodes, element.NodeTags[1]);
        double[] c = Node(element, nodes, element.NodeTags[2]);
        double[] d = Node(element, nodes, element.NodeTags[3]);
        return 0.5 * Norm(Cross(Sub(c, a), Sub(d, b)));
    }

    private static double TriangleArea(NormalizedShellElement element, IReadOnlyDictionary<int, NormalizedShellNode> nodes)
    {
        double[] a = Node(element, nodes, element.NodeTags[0]);
        double[] b = Node(element, nodes, element.NodeTags[1]);
        double[] c = Node(element, nodes, element.NodeTags[2]);
        return 0.5 * Norm(Cross(Sub(b, a), Sub(c, a)));
    }

    private static double[] Node(NormalizedShellElement element, IReadOnlyDictionary<int, NormalizedShellNode> nodes, int tag)
    {
        if (!nodes.TryGetValue(tag, out NormalizedShellNode? node))
            throw new ArgumentException($"Элемент {element.Tag} ссылается на неизвестный узел {tag}.", nameof(nodes));
        return [node.X, node.Y, node.Z];
    }

    private static double[] Sub(double[] left, double[] right) =>
        [left[0] - right[0], left[1] - right[1], left[2] - right[2]];

    private static double[] Cross(double[] left, double[] right) =>
    [
        left[1] * right[2] - left[2] * right[1],
        left[2] * right[0] - left[0] * right[2],
        left[0] * right[1] - left[1] * right[0]
    ];

    private static double Norm(double[] vector) =>
        Math.Sqrt(vector[0] * vector[0] + vector[1] * vector[1] + vector[2] * vector[2]);
}
```

- [ ] **Step 4: Run the tests to verify pass**

```powershell
dotnet test OpenCS.OpenSees.Tests --filter "FullyQualifiedName~ShellRegularizationTests"
dotnet build OpenCS.sln
```

Expected: PASS; build успешен. Q4 (0,0)-(2,1): диагонали (2,1,0)×(-2,1,0) → |(0,0,4)|/2 = 2.0, длина √2; T3: площадь 1.0, длина 1.0; вырожденный элемент (все узлы совпадают) — `ArgumentException`. Preflight-поведение `regularization_unsupported` (Strict → Blocking, DiagnosticOnly → Warning) уже покрыто в Task 6 (`ShellAuditPolicyTests`) и не дублируется.

- [ ] **Step 5: Commit**

```bash
git add OpenCS.OpenSees/Audit/ShellCharacteristicLength.cs OpenCS.OpenSees.Tests/Audit/ShellRegularizationTests.cs
git commit -m "feat(audit): characteristic length sqrt(area) and regularization capability"
```

## Task 10: IShellAnalysisRunner — обёртка «генерация → запуск → парсинг»

**Files:**
- Create: `OpenCS.OpenSees/Audit/IShellAnalysisRunner.cs`
- Create: `OpenCS.OpenSees/Audit/ShellAnalysisRunner.cs`
- Create: `OpenCS.OpenSees.Tests/Audit/ShellAnalysisRunnerTests.cs`

- [ ] **Step 1: Write the failing tests**

Создать `OpenCS.OpenSees.Tests/Audit/ShellAnalysisRunnerTests.cs`:

```csharp
using System.IO;
using OpenCS.OpenSees.Artifacts;
using OpenCS.OpenSees.Audit;
using OpenCS.OpenSees.Results;
using OpenCS.OpenSees.Runtime;
using OpenCS.OpenSees.Structural;
using OpenCS.OpenSees.Tcl;
using OpenCS.OpenSees.Tests.Fixtures;

namespace OpenCS.OpenSees.Tests.Audit;

public sealed class ShellAnalysisRunnerTests
{
    [Fact]
    public void DetermineOutcome_NonZeroExitCode_IsExecutionFailedEvenWhenStatusCompleted()
    {
        var process = new OpenSeesRunResult { ExitCode = 1 };
        var parsed = new ShellResult { Status = "completed" };

        Assert.Equal(ShellAnalysisOutcome.ExecutionFailed,
            ShellAnalysisRunner.DetermineOutcome(process, parsed, parseError: null));
    }

    [Fact]
    public void DetermineOutcome_TimedOut_IsTimedOut()
    {
        var process = new OpenSeesRunResult { ExitCode = 0, TimedOut = true };

        Assert.Equal(ShellAnalysisOutcome.TimedOut,
            ShellAnalysisRunner.DetermineOutcome(process, parsed: null, parseError: null));
    }

    [Fact]
    public void DetermineOutcome_ParseError_IsParseFailed()
    {
        var process = new OpenSeesRunResult { ExitCode = 0 };

        Assert.Equal(ShellAnalysisOutcome.ParseFailed,
            ShellAnalysisRunner.DetermineOutcome(process, parsed: null, parseError: "boom"));
    }

    [Fact]
    public void DetermineOutcome_ConvergedStep_IsCompleted()
    {
        var process = new OpenSeesRunResult { ExitCode = 0 };
        var parsed = new ShellResult
        {
            Steps = [new RCShellStepResult(1, 0, 1.0, true, [], [], [], [], [])]
        };

        Assert.Equal(ShellAnalysisOutcome.Completed,
            ShellAnalysisRunner.DetermineOutcome(process, parsed, parseError: null));
    }

    [Fact]
    public void DetermineOutcome_NoConvergedStep_IsNotConvergedEvenWhenStatusCompleted()
    {
        var process = new OpenSeesRunResult { ExitCode = 0 };
        var parsed = new ShellResult
        {
            Status = "completed",
            Steps = [new RCShellStepResult(1, 0, 1.0, false, [], [], [], [], [])]
        };

        // Статус "completed" по маркеру — но ни один шаг не сошёлся: runner не доверяет Status.
        Assert.Equal(ShellAnalysisOutcome.NotConverged,
            ShellAnalysisRunner.DetermineOutcome(process, parsed, parseError: null));
    }

    [Fact]
    public async Task RunAsync_Success_WritesArtifactsAndParsesCompleted()
    {
        ShellOpenSeesModel model = ShellModelFixtures.Q4Elastic();
        using var temp = new TempRoot("opencs-audit-runner-success");
        var runner = new ShellAnalysisRunner(
            new ShellTclGenerator(),
            new OpenSeesArtifactStore(temp.Root),
            new WriteFixtureProcessRunner(model, converged: true),
            new ShellResultParser());

        ShellAnalysisRunResult result = await runner.RunAsync(model, @"C:\fake\OpenSees.exe", CancellationToken.None);

        Assert.Equal(ShellAnalysisOutcome.Completed, result.Outcome);
        Assert.NotNull(result.Result);
        Assert.NotNull(result.ArtifactDirectory);
        Assert.True(File.Exists(Path.Combine(result.ArtifactDirectory, "script.tcl")));
        Assert.True(File.Exists(Path.Combine(result.ArtifactDirectory, "exit.json")));
    }

    [Fact]
    public async Task RunAsync_NoConvergedStepWithMarker_IsNotConverged()
    {
        ShellOpenSeesModel model = ShellModelFixtures.Q4Elastic();
        using var temp = new TempRoot("opencs-audit-runner-notconverged");
        var runner = new ShellAnalysisRunner(
            new ShellTclGenerator(),
            new OpenSeesArtifactStore(temp.Root),
            new WriteFixtureProcessRunner(model, converged: false),
            new ShellResultParser());

        ShellAnalysisRunResult result = await runner.RunAsync(model, @"C:\fake\OpenSees.exe", CancellationToken.None);

        Assert.Equal(ShellAnalysisOutcome.NotConverged, result.Outcome);
    }

    [Fact]
    public async Task RunAsync_ParseFailure_IsParseFailed()
    {
        ShellOpenSeesModel model = ShellModelFixtures.Q4Elastic();
        using var temp = new TempRoot("opencs-audit-runner-parsefailed");
        var runner = new ShellAnalysisRunner(
            new ShellTclGenerator(),
            new OpenSeesArtifactStore(temp.Root),
            new NoOutputProcessRunner(),
            new ShellResultParser());

        ShellAnalysisRunResult result = await runner.RunAsync(model, @"C:\fake\OpenSees.exe", CancellationToken.None);

        Assert.Equal(ShellAnalysisOutcome.ParseFailed, result.Outcome);
        Assert.Null(result.Result);
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));
    }

    [Fact]
    public async Task RunAsync_NonZeroExitCode_IsExecutionFailedDespiteValidParse()
    {
        ShellOpenSeesModel model = ShellModelFixtures.Q4Elastic();
        using var temp = new TempRoot("opencs-audit-runner-exitcode");
        var runner = new ShellAnalysisRunner(
            new ShellTclGenerator(),
            new OpenSeesArtifactStore(temp.Root),
            new WriteFixtureProcessRunner(model, converged: true) { ExitCode = 1 },
            new ShellResultParser());

        ShellAnalysisRunResult result = await runner.RunAsync(model, @"C:\fake\OpenSees.exe", CancellationToken.None);

        Assert.Equal(ShellAnalysisOutcome.ExecutionFailed, result.Outcome);
    }

    private static void WriteFixture(string directory, ShellOpenSeesModel model, bool converged)
    {
        NormalizedShellNode[] nodes = model.Nodes.OrderBy(node => node.Tag).ToArray();
        NormalizedShellElement[] elements = model.Elements.OrderBy(element => element.Tag).ToArray();
        int[] restrained = nodes.Where(node => node.Fixed.Any(fixedDof => fixedDof))
            .Select(node => node.Tag).ToArray();

        File.WriteAllText(Path.Combine(directory, "recorder_order.json"),
            "{\"nodeTags\":[" + string.Join(',', nodes.Select(node => node.Tag)) +
            "],\"restrainedTags\":[" + string.Join(',', restrained) +
            "],\"shellElementTags\":[" + string.Join(',', elements.Select(element => element.Tag)) +
            "],\"nonlinearBeamElementTags\":[],\"sectionForceGroups\":[]}");
        File.WriteAllText(Path.Combine(directory, "step_status.out"),
            converged ? "1 0 1.0 1 0\n" : "1 0 1.0 0 0\n");
        File.WriteAllText(Path.Combine(directory, "shell_node_disp.out"),
            "1.0 " + string.Join(' ', Enumerable.Repeat("0.001", nodes.Length * 6)) + "\n");
        if (restrained.Length > 0)
            File.WriteAllText(Path.Combine(directory, "shell_node_reactions.out"),
                "1.0 " + string.Join(' ', Enumerable.Repeat("100", restrained.Length * 6)) + "\n");
        File.WriteAllText(Path.Combine(directory, "shell_element_forces.out"),
            "1.0 " + string.Join(' ', Enumerable.Repeat("1", elements.Sum(element => element.NodeTags.Count * 6))) + "\n");
        File.WriteAllText(Path.Combine(directory, "completed.marker"), "done\n");
    }

    private sealed class WriteFixtureProcessRunner : IOpenSeesProcessRunner
    {
        private readonly ShellOpenSeesModel _model;
        private readonly bool _converged;

        public WriteFixtureProcessRunner(ShellOpenSeesModel model, bool converged)
        {
            _model = model;
            _converged = converged;
        }

        public int ExitCode { get; init; }

        public Task<OpenSeesRunResult> RunAsync(OpenSeesRunRequest request, CancellationToken cancellationToken)
        {
            WriteFixture(request.WorkingDirectory, _model, _converged);
            return Task.FromResult(new OpenSeesRunResult { ExitCode = ExitCode });
        }
    }

    private sealed class NoOutputProcessRunner : IOpenSeesProcessRunner
    {
        public Task<OpenSeesRunResult> RunAsync(OpenSeesRunRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new OpenSeesRunResult { ExitCode = 0 });
    }

    private sealed class TempRoot : IDisposable
    {
        public TempRoot(string name)
        {
            Root = Path.Combine(Path.GetTempPath(), name, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify failure**

```powershell
dotnet test OpenCS.OpenSees.Tests --filter "FullyQualifiedName~ShellAnalysisRunnerTests"
```

Expected: FAIL — типы `ShellAnalysisOutcome`, `ShellAnalysisRunResult`, `IShellAnalysisRunner`, `ShellAnalysisRunner` не существуют (compile error).

- [ ] **Step 3: Write the minimal implementation**

Создать `OpenCS.OpenSees/Audit/IShellAnalysisRunner.cs`:

```csharp
using OpenCS.OpenSees.Structural;

namespace OpenCS.OpenSees.Audit;

/// <summary>Исход audit-запуска shell-расчёта. Намеренно не ограничен «Status» из файла: решение
/// принимается по exit code, таймауту, ошибке парсинга и сходимости шагов (§7.3).</summary>
public enum ShellAnalysisOutcome
{
    /// <summary>Расчёт завершился, парсинг успешен, есть сошедшийся шаг.</summary>
    Completed,

    /// <summary>Процесс завершился без ошибок, но ни один шаг не сошёлся.</summary>
    NotConverged,

    /// <summary>Файлы результата не читаются (ошибка парсинга).</summary>
    ParseFailed,

    /// <summary>Ненулевой exit code процесса OpenSees.</summary>
    ExecutionFailed,

    /// <summary>Процесс завершился по таймауту.</summary>
    TimedOut
}

/// <summary>Результат audit-запуска: исход, распарсенный результат, каталог артефактов и ошибка.</summary>
public sealed record ShellAnalysisRunResult(
    ShellAnalysisOutcome Outcome,
    ShellResult? Result,
    string? ArtifactDirectory,
    string? ErrorMessage);

/// <summary>Запуск shell-расчёта: генерация Tcl → запуск OpenSees → парсинг результатов.
/// Audit никогда не доверяет только «Status» из файла — всегда проверяется exit code,
/// таймаут, ошибка парсинга и наличие сошедшихся шагов (§7.3).</summary>
public interface IShellAnalysisRunner
{
    /// <summary>Выполняет расчёт модели и возвращает типизированный исход запуска.</summary>
    Task<ShellAnalysisRunResult> RunAsync(
        ShellOpenSeesModel model,
        string executablePath,
        CancellationToken cancellationToken);
}
```

Создать `OpenCS.OpenSees/Audit/ShellAnalysisRunner.cs`:

```csharp
using OpenCS.OpenSees.Artifacts;
using OpenCS.OpenSees.Results;
using OpenCS.OpenSees.Runtime;
using OpenCS.OpenSees.Structural;
using OpenCS.OpenSees.Tcl;

namespace OpenCS.OpenSees.Audit;

/// <summary>Реализация IShellAnalysisRunner поверх generator / artifact store / process runner / parser.</summary>
public sealed class ShellAnalysisRunner : IShellAnalysisRunner
{
    private readonly ShellTclGenerator _generator;
    private readonly OpenSeesArtifactStore _artifactStore;
    private readonly IOpenSeesProcessRunner _processRunner;
    private readonly ShellResultParser _resultParser;
    private readonly TimeSpan _timeout;

    /// <summary>Создаёт runner. При timeout == default используется 60 секунд.</summary>
    public ShellAnalysisRunner(
        ShellTclGenerator generator,
        OpenSeesArtifactStore artifactStore,
        IOpenSeesProcessRunner processRunner,
        ShellResultParser resultParser,
        TimeSpan timeout = default)
    {
        _generator = generator ?? throw new ArgumentNullException(nameof(generator));
        _artifactStore = artifactStore ?? throw new ArgumentNullException(nameof(artifactStore));
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
        _resultParser = resultParser ?? throw new ArgumentNullException(nameof(resultParser));
        _timeout = timeout == default ? TimeSpan.FromSeconds(60) : timeout;
    }

    /// <inheritdoc />
    public async Task<ShellAnalysisRunResult> RunAsync(
        ShellOpenSeesModel model,
        string executablePath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);

        OpenSeesArtifact artifact = _artifactStore.Create();
        _artifactStore.WriteScript(artifact, _generator.Generate(model));

        OpenSeesRunResult run = await _processRunner.RunAsync(
            new OpenSeesRunRequest
            {
                ExecutablePath = executablePath,
                WorkingDirectory = artifact.DirectoryPath,
                ScriptPath = artifact.ScriptPath,
                Timeout = _timeout
            }, cancellationToken);

        _artifactStore.WriteRunResult(artifact, run);

        ShellResult? parsed = null;
        string? parseError = null;
        try
        {
            parsed = _resultParser.Parse(
                artifact.DirectoryPath, model.Elements.ToDictionary(element => element.Tag));
        }
        catch (Exception exception)
        {
            parseError = exception.Message;
        }

        ShellAnalysisOutcome outcome = DetermineOutcome(run, parsed, parseError);
        string? errorMessage = parseError ??
            (outcome is ShellAnalysisOutcome.ExecutionFailed or ShellAnalysisOutcome.TimedOut ? run.Stderr : null);

        return new ShellAnalysisRunResult(outcome, parsed, artifact.DirectoryPath, errorMessage);
    }

    /// <summary>Определяет исход запуска. Приоритет: TimedOut → ExitCode != 0 → ошибка парсинга →
    /// наличие сошедшегося шага. Status из файла НЕ используется как источник истины.</summary>
    public static ShellAnalysisOutcome DetermineOutcome(
        OpenSeesRunResult processResult,
        ShellResult? parsed,
        string? parseError)
    {
        ArgumentNullException.ThrowIfNull(processResult);

        if (processResult.TimedOut)
            return ShellAnalysisOutcome.TimedOut;
        if (processResult.ExitCode != 0)
            return ShellAnalysisOutcome.ExecutionFailed;
        if (parseError is not null || parsed is null)
            return ShellAnalysisOutcome.ParseFailed;
        if (parsed.Steps.Any(step => step.Converged))
            return ShellAnalysisOutcome.Completed;
        return ShellAnalysisOutcome.NotConverged;
    }
}
```

- [ ] **Step 4: Run the tests to verify pass**

```powershell
dotnet test OpenCS.OpenSees.Tests --filter "FullyQualifiedName~ShellAnalysisRunnerTests"
dotnet build OpenCS.sln
```

Expected: PASS; build успешен. `RunAsync_NonZeroExitCode_IsExecutionFailedDespiteValidParse` доказывает ключевое требование: exit code перекрывает корректно распарсенный результат; `RunAsync_NoConvergedStepWithMarker_IsNotConverged` — «completed»-маркер не заменяет сходимость шагов.

- [ ] **Step 5: Commit**

```bash
git add OpenCS.OpenSees/Audit/IShellAnalysisRunner.cs OpenCS.OpenSees/Audit/ShellAnalysisRunner.cs OpenCS.OpenSees.Tests/Audit/ShellAnalysisRunnerTests.cs
git commit -m "feat(audit): analysis runner that never trusts status alone"
```

## Task 11: Mesh sensitivity — factory, runner, report

**Files:**
- Create: `OpenCS.OpenSees/Audit/ShellSensitivity.cs`
- Create: `OpenCS.OpenSees/Audit/ShellSensitivityRunner.cs`
- Create: `OpenCS.OpenSees/Audit/ShellMeshSensitivityReport.cs`
- Create: `OpenCS.OpenSees.Tests/Audit/ShellSensitivityRunnerTests.cs`

- [ ] **Step 1: Write the failing tests**

Создать `OpenCS.OpenSees.Tests/Audit/ShellSensitivityRunnerTests.cs`:

```csharp
using OpenCS.OpenSees.Audit;
using OpenCS.OpenSees.Structural;

namespace OpenCS.OpenSees.Tests.Audit;

public sealed class ShellSensitivityRunnerTests
{
    [Fact]
    public void Evaluate_MetricsWithinTolerance_Passes()
    {
        ShellMeshSensitivityReport report = ShellSensitivityRunner.Evaluate(
            [Case(ShellSensitivityLevel.Coarse, 100.0, "mesh:a"),
             Case(ShellSensitivityLevel.Medium, 100.5, "mesh:b"),
             Case(ShellSensitivityLevel.Fine, 101.0, "mesh:c")],
            relativeTolerance: 0.1);

        Assert.Equal(ShellAuditVerdict.Passed, report.Verdict);
        Assert.True(report.MaxRelativeDeviation <= 0.1);
    }

    [Fact]
    public void Evaluate_DeviationBeyondTolerance_IsMeshDependent()
    {
        ShellMeshSensitivityReport report = ShellSensitivityRunner.Evaluate(
            [Case(ShellSensitivityLevel.Coarse, 100.0, "mesh:a"),
             Case(ShellSensitivityLevel.Fine, 150.0, "mesh:b")],
            relativeTolerance: 0.1);

        Assert.Equal(ShellAuditVerdict.MeshDependent, report.Verdict);
        Assert.Contains(report.Diagnostics, d => d.Code == ShellDiagnosticCodes.MeshDependent);
    }

    [Fact]
    public void Evaluate_DuplicateFingerprints_Blocks()
    {
        ShellMeshSensitivityReport report = ShellSensitivityRunner.Evaluate(
            [Case(ShellSensitivityLevel.Coarse, 100.0, "same"),
             Case(ShellSensitivityLevel.Medium, 101.0, "same"),
             Case(ShellSensitivityLevel.Fine, 102.0, "other")],
            relativeTolerance: 0.1);

        Assert.Equal(ShellAuditVerdict.Blocked, report.Verdict);
        Assert.Contains(report.Diagnostics, d => d.Code == ShellDiagnosticCodes.SensitivityCaseIncomplete);
    }

    [Fact]
    public void Evaluate_FailedCase_Blocks()
    {
        ShellSensitivityCaseReport failed = new(
            ShellSensitivityLevel.Fine, 0, "mesh:c", ShellAnalysisOutcome.ExecutionFailed);
        ShellMeshSensitivityReport report = ShellSensitivityRunner.Evaluate(
            [Case(ShellSensitivityLevel.Coarse, 100.0, "mesh:a"), failed],
            relativeTolerance: 0.1);

        Assert.Equal(ShellAuditVerdict.Blocked, report.Verdict);
    }

    [Fact]
    public void MetricFor_ExtractsMaxReactionComponent()
    {
        var result = new ShellResult
        {
            Status = "completed",
            Reactions =
            [
                new ShellNodeReaction(1, 0, 0, 1200, 0, 0, 0),
                new ShellNodeReaction(2, 0, 0, -800, 0, 0, 0)
            ]
        };

        Assert.Equal(1200.0, ShellSensitivityRunner.MetricFor(result), 12);
    }

    [Fact]
    public async Task RunAsync_DeterministicFactoryAndFakeRunner_ProducesEvaluatedReport()
    {
        var factory = new FixedCaseFactory(
        [
            new ShellSensitivityCase(ShellSensitivityLevel.Coarse, new ShellOpenSeesModel(), "mesh:a"),
            new ShellSensitivityCase(ShellSensitivityLevel.Medium, new ShellOpenSeesModel(), "mesh:b"),
            new ShellSensitivityCase(ShellSensitivityLevel.Fine, new ShellOpenSeesModel(), "mesh:c")
        ]);
        var runner = new ShellSensitivityRunner(factory, new FakeAnalysisRunner(
            CompletedWithReactions(100.0),
            CompletedWithReactions(150.0),
            CompletedWithReactions(200.0)));
        var policy = new ShellAuditPolicy { SensitivityRelativeTolerance = 0.1 };

        ShellMeshSensitivityReport report = await runner.RunAsync(policy, @"C:\fake\OpenSees.exe", CancellationToken.None);

        Assert.Equal(3, report.Cases.Count);
        Assert.Equal(ShellAuditVerdict.MeshDependent, report.Verdict);
        Assert.All(report.Cases, c => Assert.Equal(ShellAnalysisOutcome.Completed, c.Outcome));
    }

    private static ShellSensitivityCaseReport Case(ShellSensitivityLevel level, double metric, string fingerprint) =>
        new(level, metric, fingerprint, ShellAnalysisOutcome.Completed);

    private static ShellAnalysisRunResult CompletedWithReactions(double fz) =>
        new(ShellAnalysisOutcome.Completed,
            new ShellResult
            {
                Status = "completed",
                Reactions = [new ShellNodeReaction(1, 0, 0, fz, 0, 0, 0)]
            },
            null, null);

    private sealed class FixedCaseFactory : IShellSensitivityCaseFactory
    {
        private readonly IReadOnlyList<ShellSensitivityCase> _cases;

        public FixedCaseFactory(IReadOnlyList<ShellSensitivityCase> cases) => _cases = cases;

        public IReadOnlyList<ShellSensitivityCase> Create(IReadOnlyList<ShellSensitivityLevel> levels) => _cases;
    }

    private sealed class FakeAnalysisRunner : IShellAnalysisRunner
    {
        private readonly ShellAnalysisRunResult[] _results;
        private int _index;

        public FakeAnalysisRunner(params ShellAnalysisRunResult[] results) => _results = results;

        public Task<ShellAnalysisRunResult> RunAsync(
            ShellOpenSeesModel model, string executablePath, CancellationToken cancellationToken) =>
            Task.FromResult(_results[_index++ % _results.Length]);
    }
}
```

- [ ] **Step 2: Run the tests to verify failure**

```powershell
dotnet test OpenCS.OpenSees.Tests --filter "FullyQualifiedName~ShellSensitivityRunnerTests"
```

Expected: FAIL — типы `ShellSensitivityLevel`, `ShellSensitivityCase`, `IShellSensitivityCaseFactory`, `ShellSensitivityRunner`, `ShellMeshSensitivityReport`, `ShellSensitivityCaseReport` не существуют (compile error).

- [ ] **Step 3: Write the minimal implementation**

Создать `OpenCS.OpenSees/Audit/ShellSensitivity.cs`:

```csharp
using OpenCS.OpenSees.Structural;

namespace OpenCS.OpenSees.Audit;

/// <summary>Уровень mesh sensitivity (coarse/medium/fine).</summary>
public enum ShellSensitivityLevel
{
    /// <summary>Грубая сетка.</summary>
    Coarse,

    /// <summary>Средняя сетка.</summary>
    Medium,

    /// <summary>Мелкая сетка.</summary>
    Fine
}

/// <summary>Один sensitivity-запуск: уровень, модель и fingerprint источника сетки.
/// Fingerprint ОБЯЗАН различаться между запусками — иначе это не сравнение разных сеток.</summary>
public sealed record ShellSensitivityCase(
    ShellSensitivityLevel Level,
    ShellOpenSeesModel Model,
    string SourceFingerprint);

/// <summary>Фабрика sensitivity-запусков. Реальная реализация строит coarse/medium/fine сетки
/// (Gmsh) или использует prebuilt модели; unit-тесты используют детерминированную in-memory фабрику.</summary>
public interface IShellSensitivityCaseFactory
{
    /// <summary>Создаёт по одному запуску на каждый уровень.</summary>
    IReadOnlyList<ShellSensitivityCase> Create(IReadOnlyList<ShellSensitivityLevel> levels);
}
```

Создать `OpenCS.OpenSees/Audit/ShellMeshSensitivityReport.cs`:

```csharp
namespace OpenCS.OpenSees.Audit;

/// <summary>Отчёт одного sensitivity-запуска: метрика и исход.</summary>
public sealed record ShellSensitivityCaseReport(
    ShellSensitivityLevel Level,
    double Metric,
    string SourceFingerprint,
    ShellAnalysisOutcome Outcome);

/// <summary>Итоговый отчёт mesh sensitivity: метрики уровней, максимальное относительное отклонение,
/// verdict и диагностики.</summary>
public sealed record ShellMeshSensitivityReport(
    IReadOnlyList<ShellSensitivityCaseReport> Cases,
    double MaxRelativeDeviation,
    ShellAuditVerdict Verdict,
    IReadOnlyList<ShellDiagnostic> Diagnostics);
```

Создать `OpenCS.OpenSees/Audit/ShellSensitivityRunner.cs`:

```csharp
using OpenCS.OpenSees.Structural;

namespace OpenCS.OpenSees.Audit;

/// <summary>Запускает sensitivity-запуски через IShellAnalysisRunner и оценивает verdict по
/// относительному отклонению метрик между уровнями сетки (§12).</summary>
public sealed class ShellSensitivityRunner
{
    private readonly IShellSensitivityCaseFactory _factory;
    private readonly IShellAnalysisRunner _analysisRunner;

    /// <summary>Создаёт sensitivity runner поверх фабрики запусков и analysis runner-а.</summary>
    public ShellSensitivityRunner(IShellSensitivityCaseFactory factory, IShellAnalysisRunner analysisRunner)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _analysisRunner = analysisRunner ?? throw new ArgumentNullException(nameof(analysisRunner));
    }

    /// <summary>Метрика результата для сравнения сеток: максимальная по модулю компонента реакций.</summary>
    public static double MetricFor(ShellResult? result)
    {
        if (result is null)
            return 0;
        double max = 0;
        foreach (ShellNodeReaction reaction in result.Reactions)
        {
            max = Math.Max(max, Math.Abs(reaction.Fx));
            max = Math.Max(max, Math.Abs(reaction.Fy));
            max = Math.Max(max, Math.Abs(reaction.Fz));
            max = Math.Max(max, Math.Abs(reaction.Mx));
            max = Math.Max(max, Math.Abs(reaction.My));
            max = Math.Max(max, Math.Abs(reaction.Mz));
        }
        return max;
    }

    /// <summary>Оценивает verdict: Blocked при неполных/неразличимых запусках, MeshDependent при
    /// превышении допуска, иначе Passed (§12.4).</summary>
    public static ShellMeshSensitivityReport Evaluate(
        IReadOnlyList<ShellSensitivityCaseReport> cases,
        double relativeTolerance)
    {
        ArgumentNullException.ThrowIfNull(cases);
        var diagnostics = new List<ShellDiagnostic>();
        ShellAuditVerdict verdict = ShellAuditVerdict.Passed;

        if (cases.Count < 2)
        {
            verdict = ShellAuditVerdict.Blocked;
            diagnostics.Add(new ShellDiagnostic(
                ShellDiagnosticCodes.SensitivityCaseIncomplete, ShellDiagnosticSeverity.Blocking,
                $"Sensitivity требует минимум 2 запуска, получено {cases.Count}."));
        }

        if (cases.Select(c => c.SourceFingerprint).Distinct().Count() != cases.Count)
        {
            verdict = ShellAuditVerdict.Blocked;
            diagnostics.Add(new ShellDiagnostic(
                ShellDiagnosticCodes.SensitivityCaseIncomplete, ShellDiagnosticSeverity.Blocking,
                "Sensitivity-запуски имеют одинаковые source fingerprints — сравнение разных сеток невозможно."));
        }

        ShellSensitivityCaseReport? failed = cases.FirstOrDefault(c => c.Outcome != ShellAnalysisOutcome.Completed);
        if (failed is not null)
        {
            verdict = ShellAuditVerdict.Blocked;
            diagnostics.Add(new ShellDiagnostic(
                ShellDiagnosticCodes.SensitivityCaseIncomplete, ShellDiagnosticSeverity.Blocking,
                $"Sensitivity-запуск {failed.Level} завершился с {failed.Outcome}."));
        }

        double maxDeviation = 0;
        for (int i = 0; i < cases.Count; i++)
        {
            for (int j = i + 1; j < cases.Count; j++)
            {
                double denominator = Math.Max(
                    Math.Max(Math.Abs(cases[i].Metric), Math.Abs(cases[j].Metric)), 1e-12);
                maxDeviation = Math.Max(maxDeviation,
                    Math.Abs(cases[i].Metric - cases[j].Metric) / denominator);
            }
        }

        if (verdict == ShellAuditVerdict.Passed && maxDeviation > relativeTolerance)
        {
            verdict = ShellAuditVerdict.MeshDependent;
            diagnostics.Add(new ShellDiagnostic(
                ShellDiagnosticCodes.MeshDependent, ShellDiagnosticSeverity.Warning,
                $"Относительное отклонение sensitivity-метрик {maxDeviation:G3} превышает допуск {relativeTolerance:G3}."));
        }

        return new ShellMeshSensitivityReport(cases, maxDeviation, verdict, diagnostics);
    }

    /// <summary>Выполняет sensitivity study: создаёт запуски, гоняет каждый через analysis runner
    /// и возвращает отчёт с verdict.</summary>
    public async Task<ShellMeshSensitivityReport> RunAsync(
        ShellAuditPolicy policy,
        string executablePath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(policy);
        IReadOnlyList<ShellSensitivityCase> shellCases = _factory.Create(policy.SensitivityLevels);
        var reports = new List<ShellSensitivityCaseReport>(shellCases.Count);
        foreach (ShellSensitivityCase shellCase in shellCases)
        {
            ShellAnalysisRunResult run = await _analysisRunner.RunAsync(
                shellCase.Model, executablePath, cancellationToken);
            reports.Add(new ShellSensitivityCaseReport(
                shellCase.Level, MetricFor(run.Result), shellCase.SourceFingerprint, run.Outcome));
        }
        return Evaluate(reports, policy.SensitivityRelativeTolerance);
    }
}
```

- [ ] **Step 4: Run the tests to verify pass**

```powershell
dotnet test OpenCS.OpenSees.Tests --filter "FullyQualifiedName~ShellSensitivityRunnerTests"
dotnet build OpenCS.sln
```

Expected: PASS; build успешен. `RunAsync_DeterministicFactoryAndFakeRunner_ProducesEvaluatedReport` — метрики 100/150/200 дают отклонение 0.5 > 0.1 → MeshDependent; in-memory фабрика детерминирована (без Gmsh/OpenSees).

- [ ] **Step 5: Commit**

```bash
git add OpenCS.OpenSees/Audit/ShellSensitivity.cs OpenCS.OpenSees/Audit/ShellSensitivityRunner.cs OpenCS.OpenSees/Audit/ShellMeshSensitivityReport.cs OpenCS.OpenSees.Tests/Audit/ShellSensitivityRunnerTests.cs
git commit -m "feat(audit): mesh sensitivity factory, runner and report"
```

## Task 12: ShellAuditReport, verdict resolver и реальные OpenSees integration tests

**Files:**
- Create: `OpenCS.OpenSees/Audit/ShellAuditReport.cs`
- Create: `OpenCS.OpenSees.Tests/Audit/ShellAuditReportTests.cs`
- Create: `OpenCS.OpenSees.Tests/Audit/ShellAuditOpenSeesIntegrationTests.cs`

- [ ] **Step 1: Write the failing tests**

Создать `OpenCS.OpenSees.Tests/Audit/ShellAuditReportTests.cs`:

```csharp
using OpenCS.OpenSees.Audit;
using OpenCS.OpenSees.Structural;

namespace OpenCS.OpenSees.Tests.Audit;

public sealed class ShellAuditReportTests
{
    [Fact]
    public void Resolve_BlockedPreflight_ReturnsBlocked()
    {
        var preflight = new ShellAuditPreflightResult(false,
            [new ShellDiagnostic(ShellDiagnosticCodes.StateCatalogProvenanceMissing,
                ShellDiagnosticSeverity.Blocking, "Нет catalog.")]);

        ShellAuditVerdict verdict = ShellAuditVerdictResolver.Resolve(
            preflight, [], ShellEnergyConfidence.ExternalWorkOnly, new ShellAuditPolicy(), sensitivity: null);

        Assert.Equal(ShellAuditVerdict.Blocked, verdict);
    }

    [Fact]
    public void Resolve_EverythingPasses_ReturnsPassed()
    {
        ShellAuditVerdict verdict = ShellAuditVerdictResolver.Resolve(
            new ShellAuditPreflightResult(true, []),
            [PassingStep()],
            ShellEnergyConfidence.ExternalWorkOnly,
            new ShellAuditPolicy(),
            sensitivity: null);

        Assert.Equal(ShellAuditVerdict.Passed, verdict);
    }

    [Fact]
    public void Resolve_EquilibriumFailure_ReturnsWarning()
    {
        ShellAuditVerdict verdict = ShellAuditVerdictResolver.Resolve(
            new ShellAuditPreflightResult(true, []),
            [PassingStep() with { Pass = false }],
            ShellEnergyConfidence.ExternalWorkOnly,
            new ShellAuditPolicy(),
            sensitivity: null);

        Assert.Equal(ShellAuditVerdict.Warning, verdict);
    }

    [Fact]
    public void Resolve_EnergyBelowRequirement_ReturnsWarning()
    {
        ShellAuditVerdict verdict = ShellAuditVerdictResolver.Resolve(
            new ShellAuditPreflightResult(true, []),
            [PassingStep()],
            ShellEnergyConfidence.Unavailable,
            new ShellAuditPolicy
            {
                MinEnergyConfidence = ShellEnergyConfidenceRequirement.ExternalWorkOnly
            },
            sensitivity: null);

        Assert.Equal(ShellAuditVerdict.Warning, verdict);
    }

    [Fact]
    public void Resolve_MeshDependentSensitivity_ReturnsMeshDependent()
    {
        var sensitivity = new ShellMeshSensitivityReport([], 0.5, ShellAuditVerdict.MeshDependent, []);

        ShellAuditVerdict verdict = ShellAuditVerdictResolver.Resolve(
            new ShellAuditPreflightResult(true, []), [PassingStep()],
            ShellEnergyConfidence.ExternalWorkOnly, new ShellAuditPolicy(), sensitivity);

        Assert.Equal(ShellAuditVerdict.MeshDependent, verdict);
    }

    [Fact]
    public void Resolve_BlockedSensitivity_ReturnsBlocked()
    {
        var sensitivity = new ShellMeshSensitivityReport([], 0, ShellAuditVerdict.Blocked,
            [new ShellDiagnostic(ShellDiagnosticCodes.SensitivityCaseIncomplete,
                ShellDiagnosticSeverity.Blocking, "Мало запусков.")]);

        ShellAuditVerdict verdict = ShellAuditVerdictResolver.Resolve(
            new ShellAuditPreflightResult(true, []), [PassingStep()],
            ShellEnergyConfidence.ExternalWorkOnly, new ShellAuditPolicy(), sensitivity);

        Assert.Equal(ShellAuditVerdict.Blocked, verdict);
    }

    private static ShellEquilibriumStepReport PassingStep() => new(
        1, 0, 1.0,
        new ShellResultant(0, 0, -1000, 0, 0, 0),
        new ShellResultant(0, 0, 1000, 0, 0, 0),
        new ShellResultant(0, 0, 0, 0, 0, 0),
        AbsoluteError: 0,
        RelativeError: 0,
        Pass: true);
}
```

Создать `OpenCS.OpenSees.Tests/Audit/ShellAuditOpenSeesIntegrationTests.cs`:

```csharp
using System.IO;
using CScore;
using CScore.Fem;
using CScore.Planar;
using CScore.PlateRebar;
using OpenCS.Gmsh;
using OpenCS.OpenSees.Audit;
using OpenCS.OpenSees.CScore;
using OpenCS.OpenSees.Model;
using OpenCS.OpenSees.Runtime;
using OpenCS.OpenSees.Structural;
using OpenCS.OpenSees.Tcl;
using OpenCS.OpenSees.Tests.Fixtures;
using ShellResult = OpenCS.OpenSees.Structural.ShellResult;

namespace OpenCS.OpenSees.Tests.Audit;

/// <summary>Реальные OpenSees-тесты audit: равновесие Q4/T3/shell-beam, угол арматуры 45°,
/// catalog v2 provenance, preflight regularization и mesh sensitivity smoke (Gmsh при наличии,
/// иначе prebuilt coarse/medium/fine). Скипаются через OpenSeesTestExecutable.ResolveOrSkip().</summary>
public sealed class ShellAuditOpenSeesIntegrationTests
{
    [Fact]
    public async Task Q4WithTipLoad_EquilibriumPasses_WhenExecutableAvailable()
    {
        string executable = OpenSeesTestExecutable.ResolveOrSkip();
        ShellOpenSeesModel model = ShellModelFixtures.Q4WithTipLoad();

        ShellAnalysisRunResult run = await Runner().RunAsync(model, executable, CancellationToken.None);

        Assert.Equal(ShellAnalysisOutcome.Completed, run.Outcome);
        ShellEquilibriumStepReport equilibrium = EquilibriumOf(model, run.Result!);
        Assert.True(equilibrium.Pass, $"Residual: {equilibrium.Residual}");
    }

    [Fact]
    public async Task T3WithTipLoad_EquilibriumPasses_WhenExecutableAvailable()
    {
        string executable = OpenSeesTestExecutable.ResolveOrSkip();
        ShellOpenSeesModel model = ShellModelFixtures.T3WithTipLoad(
            OpenCS.OpenSees.Structural.ShellIntegrationPolicy.Full);

        ShellAnalysisRunResult run = await Runner().RunAsync(model, executable, CancellationToken.None);

        Assert.Equal(ShellAnalysisOutcome.Completed, run.Outcome);
        ShellEquilibriumStepReport equilibrium = EquilibriumOf(model, run.Result!);
        Assert.True(equilibrium.Pass, $"Residual: {equilibrium.Residual}");
    }

    [Fact]
    public async Task SharedNodeColumn_EquilibriumPasses_WhenExecutableAvailable()
    {
        string executable = OpenSeesTestExecutable.ResolveOrSkip();
        ShellOpenSeesModel model = ShellBeamConnectionFixtures.SharedNodeColumn();

        ShellAnalysisRunResult run = await Runner().RunAsync(model, executable, CancellationToken.None);

        Assert.Equal(ShellAnalysisOutcome.Completed, run.Outcome);
        ShellEquilibriumStepReport equilibrium = EquilibriumOf(model, run.Result!);
        Assert.True(equilibrium.Pass, $"Residual: {equilibrium.Residual}");
    }

    [Fact]
    public async Task RebarAngle45_ElementRunsAndEquilibriumPasses_WhenExecutableAvailable()
    {
        string executable = OpenSeesTestExecutable.ResolveOrSkip();

        var section = new PlateSection
        {
            H = 0.2,
            NLayers = 2,
            RebarLayers = [new PlateRebarLayer { Asx = 0.001, Zsx = -0.07, Angle = 45.0 }]
        };
        PlateSectionShellMappingResult mapped = PlateSectionOpenSeesMapper.Map(
            section, ShellFrame.Identity, new RebarCapableResolver());

        // Task 1: угол слоя передаётся в native секцию как phi / phi+90.
        Assert.Contains(mapped.Section.Layers,
            layer => layer.Kind == ShellLayerKind.RebarX && layer.DirectionDegrees == 45.0);
        Assert.Contains(mapped.Section.Layers,
            layer => layer.Kind == ShellLayerKind.RebarY && layer.DirectionDegrees == 135.0);

        var model = new ShellOpenSeesModel
        {
            Nodes =
            [
                new(1, 0, 0, 0, [true, true, true, true, true, true], "angle:1"),
                new(2, 1, 0, 0, new bool[6], "angle:2"),
                new(3, 1, 1, 0, new bool[6], "angle:3"),
                new(4, 0, 1, 0, [true, true, true, true, true, true], "angle:4")
            ],
            Materials = mapped.Materials,
            Sections = [mapped.Section],
            Elements = [new(10, ShellElementKind.ASDShellQ4, [1, 2, 3, 4],
                mapped.Section.Tag, mapped.Section.Fingerprint,
                ShellFrame.Identity, ShellIntegrationPolicy.Full, "angle:e:10")],
            Stages = [new() { Tag = "stage-1",
                Loads = [new(2, 0, 0, -1000, 0, 0, 0), new(3, 0, 0, -1000, 0, 0, 0)] }]
        };
        model.Validate();

        ShellAnalysisRunResult run = await Runner().RunAsync(model, executable, CancellationToken.None);

        Assert.Equal(ShellAnalysisOutcome.Completed, run.Outcome);
        ShellEquilibriumStepReport equilibrium = EquilibriumOf(model, run.Result!);
        Assert.True(equilibrium.Pass, $"Residual: {equilibrium.Residual}");
    }

    [Fact]
    public async Task Q4Run_ProducesV2CatalogProvenance_WhenExecutableAvailable()
    {
        string executable = OpenSeesTestExecutable.ResolveOrSkip();
        ShellOpenSeesModel model = ShellModelFixtures.Q4Elastic();

        ShellAnalysisRunResult run = await Runner().RunAsync(model, executable, CancellationToken.None);

        Assert.Equal(ShellAnalysisOutcome.Completed, run.Outcome);
        ShellStateCatalog? catalog = run.Result!.StateCatalog;
        Assert.NotNull(catalog);
        Assert.Equal(ShellStateCatalogProvenanceKind.V2WithProvenance, catalog!.ProvenanceKind);
        Assert.NotEmpty(catalog.ShellLayerGroups);
        Assert.All(catalog.ShellLayerGroups, group =>
        {
            Assert.True(group.SectionTag > 0);
            Assert.True(group.MaterialTag > 0);
            Assert.NotNull(group.LayerKind);
            Assert.False(string.IsNullOrWhiteSpace(group.SourceId));
        });
    }

    [Fact]
    public void UnsupportedRegularization_StrictPreflight_BlocksWithoutOpenSees()
    {
        var policy = new ShellAuditPolicy
        {
            Mode = ShellAuditMode.Strict,
            Regularization = new ShellRegularizationPolicy { Mode = ShellRegularizationMode.CrackBand }
        };

        ShellAuditPreflightResult preflight = ShellAuditPreflight.Run(
            ShellModelFixtures.Q4Elastic(), V2Catalog(), policy, new ShellRegularizationCapability([]));

        Assert.False(preflight.IsCalculable);
        Assert.Contains(preflight.Diagnostics, d =>
            d.Code == ShellDiagnosticCodes.RegularizationUnsupported &&
            d.Severity == ShellDiagnosticSeverity.Blocking);
    }

    [Fact]
    public async Task Q4WithTipLoad_FullAuditFlow_Passes_WhenExecutableAvailable()
    {
        string executable = OpenSeesTestExecutable.ResolveOrSkip();
        ShellOpenSeesModel model = ShellModelFixtures.Q4WithTipLoad();
        var policy = new ShellAuditPolicy
        {
            Mode = ShellAuditMode.Strict,
            AbsoluteEquilibriumTolerance = 1e-2,
            RelativeEquilibriumTolerance = 1e-2
        };

        ShellAuditPreflightResult preflight = ShellAuditPreflight.Run(
            model, V2Catalog(), policy, new ShellRegularizationCapability([]));
        Assert.True(preflight.IsCalculable);

        ShellAnalysisRunResult run = await Runner().RunAsync(model, executable, CancellationToken.None);
        Assert.Equal(ShellAnalysisOutcome.Completed, run.Outcome);
        Assert.NotNull(run.Result!.StateCatalog);

        ShellEquilibriumStepReport equilibrium = EquilibriumOf(model, run.Result);
        ShellEnergyConfidence energy = ShellEnergyAuditor.DetermineConfidence(
            hasNativeEnergyResponse: false, hasStateIntegralData: false, hasLoadHistory: true);

        ShellAuditVerdict verdict = ShellAuditVerdictResolver.Resolve(
            preflight, [equilibrium], energy, policy, sensitivity: null);

        Assert.Equal(ShellAuditVerdict.Passed, verdict);
    }

    [Fact]
    public async Task MeshSensitivitySmoke_ThreeLevels_NotBlocked_WhenExecutableAvailable()
    {
        string executable = OpenSeesTestExecutable.ResolveOrSkip();
        const string gmshPath = @"C:\Tools\gmsh-4.15.2-Windows64\gmsh.exe";

        IReadOnlyList<ShellSensitivityCase> cases;
        if (File.Exists(gmshPath))
        {
            cases = await BuildGmshCasesAsync(gmshPath);
        }
        else
        {
            // Prebuilt coarse/medium/fine — smoke без Gmsh, без false remesh claim.
            cases =
            [
                new ShellSensitivityCase(ShellSensitivityLevel.Coarse, BuildSquarePatch(1, "prebuilt:1x1"), "prebuilt:1x1"),
                new ShellSensitivityCase(ShellSensitivityLevel.Medium, BuildSquarePatch(2, "prebuilt:2x2"), "prebuilt:2x2"),
                new ShellSensitivityCase(ShellSensitivityLevel.Fine, BuildSquarePatch(4, "prebuilt:4x4"), "prebuilt:4x4")
            ];
        }

        var sensitivity = new ShellSensitivityRunner(new FixedCaseFactory(cases), Runner());
        var policy = new ShellAuditPolicy { SensitivityRelativeTolerance = 0.1 };
        ShellMeshSensitivityReport report = await sensitivity.RunAsync(policy, executable, CancellationToken.None);

        Assert.Equal(3, report.Cases.Count);
        Assert.All(report.Cases, c => Assert.Equal(ShellAnalysisOutcome.Completed, c.Outcome));
        Assert.NotEqual(ShellAuditVerdict.Blocked, report.Verdict);
    }

    private static ShellAnalysisRunner Runner() => new(
        new ShellTclGenerator(),
        new OpenSeesArtifactStore(Path.Combine(Path.GetTempPath(), "opencs-audit-artifacts")),
        new OpenSeesProcessRunner(),
        new ShellResultParser(),
        TimeSpan.FromSeconds(60));

    private static ShellEquilibriumStepReport EquilibriumOf(ShellOpenSeesModel model, ShellResult result)
    {
        RCShellStepResult last = result.Steps.Last(step => step.Converged);
        ShellResultant applied = ShellEquilibriumAuditor.AppliedResultantAtStep(
            model.Stages, last.StageIndex, last.LoadFactor);
        ShellResultant reaction = ShellEquilibriumAuditor.ReactionResultant(
            last.Reactions, model.Nodes.ToDictionary(node => node.Tag));
        return ShellEquilibriumAuditor.Evaluate(
            last.StepIndex, last.StageIndex, last.LoadFactor,
            applied, reaction, new ShellAuditPolicy
            {
                AbsoluteEquilibriumTolerance = 1e-2,
                RelativeEquilibriumTolerance = 1e-2
            });
    }

    private static ShellStateCatalog V2Catalog() => new(2, [], [], []);

    private static async Task<IReadOnlyList<ShellSensitivityCase>> BuildGmshCasesAsync(string gmshPath)
    {
        string gmshRoot = Path.Combine(Path.GetTempPath(), "opencs-audit-sensitivity", Guid.NewGuid().ToString("N"));
        try
        {
            var section = new PlateSection { H = GmshOpenSeesPatchTestFixture.Thickness, NLayers = 4 };
            var field = new PlateRebarField([], []);
            var cases = new List<ShellSensitivityCase>(3);

            (PlanarMeshElementMode Mode, double Size, ShellSensitivityLevel Level)[] levels =
            [
                (PlanarMeshElementMode.Triangles, 0.7, ShellSensitivityLevel.Coarse),
                (PlanarMeshElementMode.Mixed, 0.35, ShellSensitivityLevel.Medium),
                (PlanarMeshElementMode.Quads, 0.175, ShellSensitivityLevel.Fine)
            ];
            foreach ((PlanarMeshElementMode mode, double size, ShellSensitivityLevel level) in levels)
            {
                PlanarMeshSnapshot snapshot = await BuildSnapshotAsync(gmshPath, gmshRoot, mode, size);
                PlanarMeshShellModelResult built = PlanarMeshSnapshotShellModelAdapter.Build(
                    snapshot, Frame3D.Identity, section, field, new ConcreteResolver());
                ShellOpenSeesModel model = GmshOpenSeesPatchTestFixture.BuildLoadedModel(built, snapshot);
                cases.Add(new ShellSensitivityCase(level, model, $"gmsh:{mode}:{size}"));
            }
            return cases;
        }
        finally
        {
            if (Directory.Exists(gmshRoot))
                Directory.Delete(gmshRoot, recursive: true);
        }
    }

    private static async Task<PlanarMeshSnapshot> BuildSnapshotAsync(
        string gmshPath, string artifactRoot, PlanarMeshElementMode mode, double size)
    {
        var region = PlanarRegion.CreateFromContour(
            new Contour
            {
                X = [0, GmshOpenSeesPatchTestFixture.Length, GmshOpenSeesPatchTestFixture.Length, 0],
                Y = [0, 0, GmshOpenSeesPatchTestFixture.Width, GmshOpenSeesPatchTestFixture.Width]
            },
            frame: Frame3D.Identity);

        var mesher = new GmshPlanarMesher(new GmshPlanarMesherOptions
        {
            ExecutablePath = gmshPath,
            ArtifactRoot = artifactRoot
        });

        PlanarMeshSnapshot snapshot = await mesher.BuildAsync(
            new PlanarMeshingRequest(region, new PlanarMeshSettings(size, 6, mode)));

        Assert.True(snapshot.IsCalculable,
            string.Join("; ", snapshot.Diagnostics.Select(d => d.Message)));
        return snapshot;
    }

    private static ShellOpenSeesModel BuildSquarePatch(int subdivisions, string fingerprint)
    {
        int grid = subdivisions + 1;
        double side = 1.0;
        var nodes = new List<NormalizedShellNode>(grid * grid);
        for (int i = 0; i < grid; i++)
        {
            for (int j = 0; j < grid; j++)
            {
                bool fixedNode = i == 0;
                int tag = i * grid + j + 1;
                nodes.Add(new NormalizedShellNode(tag,
                    j * side / subdivisions, i * side / subdivisions, 0,
                    fixedNode ? [true, true, true, true, true, true] : new bool[6],
                    $"smoke:{fingerprint}:n:{tag}"));
            }
        }

        var elements = new List<NormalizedShellElement>(subdivisions * subdivisions);
        for (int i = 0; i < subdivisions; i++)
        {
            for (int j = 0; j < subdivisions; j++)
            {
                int a = i * grid + j + 1;
                int b = a + grid;
                int c = b + 1;
                int d = a + 1;
                int tag = i * subdivisions + j + 1;
                elements.Add(new NormalizedShellElement(tag, ShellElementKind.ASDShellQ4, [a, b, c, d],
                    20, fingerprint, ShellFrame.Identity, ShellIntegrationPolicy.Full,
                    $"smoke:{fingerprint}:e:{tag}"));
            }
        }

        int firstTop = subdivisions * grid + 1;
        var loads = new List<ShellNodalLoad>(grid);
        for (int j = 0; j < grid; j++)
            loads.Add(new ShellNodalLoad(firstTop + j, 0, 0, -1000.0 / subdivisions, 0, 0, 0));

        return new ShellOpenSeesModel
        {
            Nodes = nodes,
            Materials = [new(1, "smoke:concrete", new ElasticIsotropicShellMaterialSpec(30e9, 0.2))],
            Sections = [new(20, "smoke:plate", 0.2, ShellFrame.Identity,
                [
                    new(0, ShellLayerKind.Concrete, -0.075, 0.05, 1, 0, "smoke:c0"),
                    new(1, ShellLayerKind.Concrete, -0.025, 0.05, 1, 0, "smoke:c1"),
                    new(2, ShellLayerKind.Concrete, 0.025, 0.05, 1, 0, "smoke:c2"),
                    new(3, ShellLayerKind.Concrete, 0.075, 0.05, 1, 0, "smoke:c3")
                ],
                ShellMappingMode.Exact, [], fingerprint)],
            Elements = elements,
            Stages = [new() { Tag = "stage-1", Loads = loads }]
        };
    }

    private sealed class FixedCaseFactory : IShellSensitivityCaseFactory
    {
        private readonly IReadOnlyList<ShellSensitivityCase> _cases;

        public FixedCaseFactory(IReadOnlyList<ShellSensitivityCase> cases) => _cases = cases;

        public IReadOnlyList<ShellSensitivityCase> Create(IReadOnlyList<ShellSensitivityLevel> levels) => _cases;
    }

    private sealed class ConcreteResolver : IPlateSectionShellMaterialResolver
    {
        public IReadOnlyList<NativeShellMaterialDefinition> ResolveConcrete(int sourceMaterialId) =>
            [new(1, $"concrete:{sourceMaterialId}", new ElasticIsotropicShellMaterialSpec(30e9, 0.2))];

        public IReadOnlyList<NativeShellMaterialDefinition> ResolveRebar(int sourceMaterialId) =>
            throw new NotSupportedException("Smoke-тест не использует армирование.");
    }

    private sealed class RebarCapableResolver : IPlateSectionShellMaterialResolver
    {
        public IReadOnlyList<NativeShellMaterialDefinition> ResolveConcrete(int sourceMaterialId) =>
            [new(1, $"concrete:{sourceMaterialId}", new ElasticIsotropicShellMaterialSpec(30e9, 0.2))];

        public IReadOnlyList<NativeShellMaterialDefinition> ResolveRebar(int sourceMaterialId) =>
        [
            new(500, $"rebar:{sourceMaterialId}:uniaxial", new ElasticUniaxialShellMaterialSpec(200e9)),
            new(2, $"rebar:{sourceMaterialId}:plate", new PlateRebarShellMaterialSpec(500, 0))
        ];
    }
}
```

- [ ] **Step 2: Run the tests to verify failure**

```powershell
dotnet test OpenCS.OpenSees.Tests --filter "FullyQualifiedName~ShellAuditReportTests"
dotnet test OpenCS.OpenSees.Tests --filter "FullyQualifiedName~ShellAuditOpenSeesIntegrationTests"
```

Expected: FAIL — `ShellAuditReport`, `ShellAuditVerdictResolver` не существуют (compile error интеграционных тестов).

- [ ] **Step 3: Write the minimal implementation**

Создать `OpenCS.OpenSees/Audit/ShellAuditReport.cs`:

```csharp
namespace OpenCS.OpenSees.Audit;

/// <summary>Типизированный отчёт audit-расчёта shell-модели (§8): verdict, preflight, равновесие,
/// energy, regularization, sensitivity и диагностики.</summary>
public sealed record ShellAuditReport(
    ShellAuditVerdict Verdict,
    ShellAuditPreflightResult Preflight,
    IReadOnlyList<ShellEquilibriumStepReport> EquilibriumSteps,
    ShellEnergyConfidence EnergyConfidence,
    double ExternalWork,
    bool RegularizationApplied,
    IReadOnlyList<ShellRegularizationMode> SupportedRegularizationModes,
    ShellMeshSensitivityReport? Sensitivity,
    IReadOnlyList<ShellDiagnostic> Diagnostics);

/// <summary>Собирает verdict audit-отчёта из preflight, равновесия, energy и sensitivity (§8).
/// Порядок ShellEnergyConfidence: NativeResponse(0) &lt; StateIntegral(1) &lt; ExternalWorkOnly(2)
/// &lt; Unavailable(3) — confidence достаточно, если (int)confidence &lt;= (int)требование.</summary>
public static class ShellAuditVerdictResolver
{
    /// <summary>Правила: Blocked preflight/sensitivity → Blocked; провал равновесия или недостаточный
    /// confidence energy → Warning; MeshDependent sensitivity → MeshDependent; иначе Passed.</summary>
    public static ShellAuditVerdict Resolve(
        ShellAuditPreflightResult preflight,
        IReadOnlyList<ShellEquilibriumStepReport> equilibriumSteps,
        ShellEnergyConfidence energyConfidence,
        ShellAuditPolicy policy,
        ShellMeshSensitivityReport? sensitivity)
    {
        ArgumentNullException.ThrowIfNull(preflight);
        ArgumentNullException.ThrowIfNull(equilibriumSteps);
        ArgumentNullException.ThrowIfNull(policy);

        if (!preflight.IsCalculable)
            return ShellAuditVerdict.Blocked;
        if (sensitivity is not null && sensitivity.Verdict == ShellAuditVerdict.Blocked)
            return ShellAuditVerdict.Blocked;
        if (equilibriumSteps.Any(step => !step.Pass))
            return ShellAuditVerdict.Warning;
        if ((int)energyConfidence > (int)policy.MinEnergyConfidence)
            return ShellAuditVerdict.Warning;
        if (sensitivity is not null && sensitivity.Verdict == ShellAuditVerdict.MeshDependent)
            return ShellAuditVerdict.MeshDependent;
        return ShellAuditVerdict.Passed;
    }
}
```

- [ ] **Step 4: Run the tests to verify pass**

```powershell
dotnet test OpenCS.OpenSees.Tests --filter "FullyQualifiedName~ShellAuditReportTests"
dotnet test OpenCS.OpenSees.Tests --filter "FullyQualifiedName~ShellAuditOpenSeesIntegrationTests"
dotnet build OpenCS.sln
```

Expected: `ShellAuditReportTests` — PASS всегда. `ShellAuditOpenSeesIntegrationTests` — PASS при наличии `C:\Tools\OpenSees\bin\OpenSees.exe` (на машине разработчика есть) и скип при его отсутствии; `MeshSensitivitySmoke` использует Gmsh при наличии фиксированного пути, иначе prebuilt 1×1/2×2/4×4 Q4 — без false remesh claim. Build успешен.

- [ ] **Step 5: Commit**

```bash
git add OpenCS.OpenSees/Audit/ShellAuditReport.cs OpenCS.OpenSees.Tests/Audit/ShellAuditReportTests.cs OpenCS.OpenSees.Tests/Audit/ShellAuditOpenSeesIntegrationTests.cs
git commit -m "feat(audit): audit report, verdict resolver and real OpenSees integration tests"
```

## Task 13: Финальная регрессия

**Files:**
- (нет новых файлов — верификация собранного плана)

- [ ] **Step 1: Полная сборка решения**

```powershell
dotnet build OpenCS.sln
```

Expected: 0 ошибок (известные 2 MSB9008 warning про отсутствующий `OpenCS.Core.UI` — baseline, срезу не атрибутируются).

- [ ] **Step 2: Регрессия всех тестовых проектов**

```powershell
dotnet test CScore.Tests/CScore.Tests.csproj
dotnet test OpenCS.Gmsh.Tests/OpenCS.Gmsh.Tests.csproj
dotnet test OpenCS.OpenSees.Tests --filter "FullyQualifiedName~ShellAudit"
```

Expected: CScore.Tests — 425 passed, 1 skipped (baseline); OpenCS.Gmsh.Tests — 33 passed; OpenCS.OpenSees.Tests (Audit-фильтр) — все новые тесты зелёные. Полный параллельный прогон `OpenCS.OpenSees.Tests` содержит известный flaky pre-existing SQLite cleanup race — isolated-запуски проходят, срезу не атрибутируется.

- [ ] **Step 3: Реальные OpenSees-прогоны**

```powershell
dotnet test OpenCS.OpenSees.Tests --filter "FullyQualifiedName~ShellAuditOpenSeesIntegrationTests"
dotnet test OpenCS.OpenSees.Tests --filter "FullyQualifiedName~ShellOpenSeesIntegrationTests"
```

Expected: PASS — executable присутствует (`C:\Tools\OpenSees\bin\OpenSees.exe`), реальные расчёты выполняются.

- [ ] **Step 4: Проверка отсутствия dead references и порядка задач**

Проверить, что каждый файл из File Structure Map создан/изменён в плане, каждый тип, используемый в тестах задачи N, определён в задаче ≤ N (Task 1 → 13 по Dependency Order), и ни в одном шаге нет «TODO», «implement later» или пустых плейсхолдеров.

- [ ] **Step 5: Итоговое состояние git**

```bash
git status
git log --oneline -15
```

Expected: ветка `feature/nonlinear-rc-shell-audit`; 13 коммитов задач + коммит плана; рабочая копия чистая. Если регрессия потребовала правок — оформить их отдельным коммитом:

```bash
git add -A
git commit -m "fix(audit): address final regression findings"
```
