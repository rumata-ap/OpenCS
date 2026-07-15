# Одноосная диаграмма N-M: план реализации

> **Для агентных исполнителей:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (рекомендуется) или `superpowers:executing-plans` для выполнения этого плана по задачам. Шаги используют чекбоксы (`- [ ]`) для отслеживания.

**Цель:** Добавить библиотечный и OpenCS-task вертикальный срез одноосной диаграммы `N-M`, выполняющий последовательную серию изолированных monotonic moment–curvature расчётов для заданных продольных сил.

**Архитектура:** Существующий `SectionAnalysisService` станет реализацией небольшого контракта исполнителя. Новый `SectionInteractionService` будет валидировать список сил, последовательно вызывать этот контракт, выбирать последнюю сошедшуюся строку каждой истории и агрегировать статусы. WPF остаётся без отдельного графика: task сохраняет типизированный `SectionInteractionResult` в `CalcResult.DataJson`, а каждая точка сохраняет собственные артефакты stage 0–1.

**Технологии:** .NET 9, C#, существующий `OpenCS.OpenSees`, `System.Text.Json`, `CancellationToken`, xUnit, внешний Tcl/OpenSees backend.

---

## Контекст и неизменяемые соглашения

- Работать в ветке `feature/opensees-section-interaction` в worktree `C:\Users\ponomarev\Documents\devel\OpenCS\.worktrees\opensees-section-interaction`.
- Backend хранит силы в `N`, моменты в `N·m`, кривизну в `1/m`; параметры OpenCS task принимают `axialForces` в `kN` и конвертируют их ровно один раз через `CScoreUnitConverter.KiloNewtonsToNewtons`.
- Порядок `axialForces` сохраняется. Список не пустой, все значения конечные, точные дубликаты запрещены.
- Для каждой силы создаётся отдельный вызов `SectionAnalysisService`; его `OpenSeesArtifactStore` уже создаёт уникальный каталог и сохраняет `script.tcl`, `manifest.json`, `stdout.txt`, `stderr.txt`, `exit.json` и recorder-файлы.
- Последняя точка диаграммы берётся как `Rows.LastOrDefault(row => row.Converged)`, а не как последняя строка сходимости. Поэтому частично сошедшаяся история может сохранить полезную последнюю сошедшуюся пару `N-M`, но сама точка остаётся `not_converged`.
- Итоговый статус: `error`, если хотя бы одна точка имеет `error`; иначе `not_converged`, если хотя бы одна точка не имеет `ok`; иначе `ok`.
- `N-Mx-My`, target-force пары, параллельные запуски и WPF-график в этот план не входят.

## Карта файлов

Создать:

- `OpenCS.OpenSees/Analysis/SectionInteractionRequest.cs` — запрос и валидация списка сил.
- `OpenCS.OpenSees/Analysis/SectionInteractionPoint.cs` — одна точка результата.
- `OpenCS.OpenSees/Analysis/SectionInteractionResult.cs` — итог кривой и агрегированный статус.
- `OpenCS.OpenSees/Services/ISectionAnalysisExecutor.cs` — контракт одного внутреннего анализа.
- `OpenCS.OpenSees/Services/SectionInteractionService.cs` — последовательная оркестрация точек.
- `OpenCS/Tasks/OpenSeesSectionInteractionParams.cs` — JSON-параметры новой task.
- `OpenCS/Tasks/OpenSeesSectionInteractionHandler.cs` — адаптация `CrossSection` и запуск сервиса.

Изменить:

- `OpenCS.OpenSees/Services/SectionAnalysisService.cs` — реализовать `ISectionAnalysisExecutor`.
- `OpenCS.OpenSees.Tests/SectionInteractionTests.cs` — unit-тесты модели и оркестрации.
- `OpenCS.OpenSees.Tests/OpenSeesIntegrationTests.cs` — реальный opt-in тест трёх точек `N-M`.
- `OpenCS.OpenSees.Tests/OpenSeesTaskContractTests.cs` — JSON-контракт, конвертация единиц и регистрация kind.
- `OpenCS/Tasks/TaskRunner.cs` — зарегистрировать `opensees_section_interaction_nm`.
- `OpenCS/Views/CalcTaskPropsDialog.xaml.cs` — добавить задачу в список выбора.
- `OpenCS/Resources/Strings.ru-RU.xaml` — русское название task.
- `OpenCS/Resources/Strings.en-US.xaml` — английское название task.
- `OpenCS.OpenSees/README.md` — документировать JSON, статусы и каталоги точек.

---

### Задача 1: Добавить контракты запроса и результата

**Файлы:**

- Создать: `OpenCS.OpenSees/Analysis/SectionInteractionRequest.cs`
- Создать: `OpenCS.OpenSees/Analysis/SectionInteractionPoint.cs`
- Создать: `OpenCS.OpenSees/Analysis/SectionInteractionResult.cs`
- Тест: `OpenCS.OpenSees.Tests/SectionInteractionTests.cs`

- [ ] **Шаг 1: Написать падающие тесты валидации и формы результата.**

```csharp
using OpenCS.OpenSees.Analysis;

namespace OpenCS.OpenSees.Tests;

public sealed class SectionInteractionTests
{
    [Fact]
    public void Request_requires_nonempty_finite_unique_axial_forces()
    {
        SectionInteractionRequest valid = new()
        {
            AxialForcesN = [-100_000, 0, 100_000],
            MaxCurvature = 0.01,
            Increments = 20
        };

        valid.Validate();

        Assert.Throws<ArgumentException>(() => new SectionInteractionRequest
        {
            AxialForcesN = [], MaxCurvature = 0.01, Increments = 20
        }.Validate());
        Assert.Throws<ArgumentException>(() => new SectionInteractionRequest
        {
            AxialForcesN = [0, double.NaN], MaxCurvature = 0.01, Increments = 20
        }.Validate());
        Assert.Throws<ArgumentException>(() => new SectionInteractionRequest
        {
            AxialForcesN = [0, 0], MaxCurvature = 0.01, Increments = 20
        }.Validate());
        Assert.Throws<ArgumentException>(() => new SectionInteractionRequest
        {
            AxialForcesN = [0], MaxCurvature = 0, Increments = 20
        }.Validate());
    }

    [Fact]
    public void Request_preserves_input_order()
    {
        SectionInteractionRequest request = new() { AxialForcesN = [100, -200, 0] };

        Assert.Equal(new[] { 100d, -200d, 0d }, request.AxialForcesN);
    }

    [Fact]
    public void Point_can_keep_last_converged_row_for_not_converged_analysis()
    {
        SectionHistoryRow row = new() { Step = 2, Converged = true, BendingMomentNm = 123 };
        SectionInteractionPoint point = new()
        {
            AxialForceN = 10,
            BendingMomentNm = row.BendingMomentNm,
            TerminalRow = row,
            Status = "not_converged"
        };

        Assert.Equal(123, point.BendingMomentNm);
        Assert.Equal(2, point.TerminalRow!.Step);
        Assert.Equal("not_converged", point.Status);
    }
}
```

- [ ] **Шаг 2: Запустить только новые тесты и убедиться, что они не компилируются из-за отсутствующих типов.**

Запуск: `dotnet test OpenCS.OpenSees.Tests/OpenCS.OpenSees.Tests.csproj --filter FullyQualifiedName~SectionInteractionTests`

Ожидаемый результат: `FAIL` с отсутствующими `SectionInteractionRequest`, `SectionInteractionPoint` и `SectionHistoryRow` в новом файле теста.

- [ ] **Шаг 3: Реализовать минимальные контракты.**

`SectionInteractionRequest` должен содержать `IReadOnlyList<double> AxialForcesN = []`, `MaxCurvature = 0.01`, `Increments = 20`, `SectionBendingAxis Axis = Mx` и `OpenSeesCoordinateConvention Convention = CScoreDefault`. `Validate()` должен проверить непустой список, конечность каждого значения, точные дубликаты, а затем те же `MaxCurvature`/`Increments`, что проверяет `SectionAnalysisRequest`.

```csharp
public void Validate()
{
    if (AxialForcesN.Count == 0 || AxialForcesN.Any(force => !double.IsFinite(force)))
        throw new ArgumentException("AxialForcesN must contain finite values.", nameof(AxialForcesN));
    if (AxialForcesN.Count != AxialForcesN.Distinct().Count())
        throw new ArgumentException("AxialForcesN must not contain duplicates.", nameof(AxialForcesN));
    if (!double.IsFinite(MaxCurvature) || MaxCurvature <= 0)
        throw new ArgumentException("MaxCurvature must be positive and finite.", nameof(MaxCurvature));
    if (Increments <= 0)
        throw new ArgumentException("Increments must be positive.", nameof(Increments));
}
```

`SectionInteractionPoint` содержит `AxialForceN`, nullable `BendingMomentNm`, nullable `Curvature`, nullable `SectionHistoryRow TerminalRow`, `Status = "error"`, `Diagnostics = []` и `ArtifactDirectory = ""`. `SectionInteractionResult` содержит `Status = "error"`, `Points = []` и `Diagnostics = []`. Все public-типы и свойства получают русские XML-комментарии.

- [ ] **Шаг 4: Запустить тесты и проверить PASS.**

Запуск: `dotnet test OpenCS.OpenSees.Tests/OpenCS.OpenSees.Tests.csproj --filter FullyQualifiedName~SectionInteractionTests`

Ожидаемый результат: все тесты `SectionInteractionTests` проходят.

- [ ] **Шаг 5: Зафиксировать контракт отдельным коммитом.**

```powershell
git add OpenCS.OpenSees/Analysis/SectionInteractionRequest.cs OpenCS.OpenSees/Analysis/SectionInteractionPoint.cs OpenCS.OpenSees/Analysis/SectionInteractionResult.cs OpenCS.OpenSees.Tests/SectionInteractionTests.cs
git commit -m "feat(opensees): add N-M interaction contracts"
```

### Задача 2: Добавить executor-контракт и сервис последовательной оркестрации

**Файлы:**

- Создать: `OpenCS.OpenSees/Services/ISectionAnalysisExecutor.cs`
- Создать: `OpenCS.OpenSees/Services/SectionInteractionService.cs`
- Изменить: `OpenCS.OpenSees/Services/SectionAnalysisService.cs`
- Изменить: `OpenCS.OpenSees.Tests/SectionInteractionTests.cs`

- [ ] **Шаг 1: Добавить тестовый fake executor и падающие тесты порядка, выбора строки и статусов.**

Fake должен записывать каждый `SectionAnalysisRequest` и возвращать результаты из очереди:

```csharp
private sealed class FakeSectionAnalysisExecutor : ISectionAnalysisExecutor
{
    public List<SectionAnalysisRequest> Requests { get; } = [];
    public Queue<SectionAnalysisResult> Results { get; } = [];

    public Task<SectionAnalysisResult> RunAsync(
        OpenSeesSectionModel model,
        SectionAnalysisRequest request,
        OpenSeesRunRequest processRequest,
        CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(Results.Dequeue());
    }
}
```

Добавить тест, который передаёт `AxialForcesN = [100, -200, 300]`, а fake возвращает две converged-строки, затем `not_converged` с одной предыдущей converged-строкой, затем `error`. Тест должен проверить сохранённый порядок запросов, силы `100/-200/300`, выбор последней converged-строки во второй точке, отдельные `ArtifactDirectory` из результатов и итоговый статус `error`.

Добавить отдельный тест без `error`, но с одной `not_converged` точкой; ожидаемый итоговый статус — `not_converged`. Добавить тест с отменённым токеном между точками и проверить, что executor вызван только для уже начавшихся точек, а `OperationCanceledException` не превращается в успешный результат.

- [ ] **Шаг 2: Запустить тесты и убедиться, что отсутствует executor/service.**

Запуск: `dotnet test OpenCS.OpenSees.Tests/OpenCS.OpenSees.Tests.csproj --filter FullyQualifiedName~SectionInteractionTests`

Ожидаемый результат: `FAIL` до появления `ISectionAnalysisExecutor` и `SectionInteractionService`.

- [ ] **Шаг 3: Ввести контракт и подключить существующий сервис.**

Контракт должен быть таким:

```csharp
public interface ISectionAnalysisExecutor
{
    Task<SectionAnalysisResult> RunAsync(
        OpenSeesSectionModel model,
        SectionAnalysisRequest request,
        OpenSeesRunRequest processRequest,
        CancellationToken cancellationToken);
}
```

Изменить объявление `SectionAnalysisService` на `public sealed class SectionAnalysisService : ISectionAnalysisExecutor`; сигнатуру и поведение его текущего `RunAsync` не менять.

- [ ] **Шаг 4: Реализовать `SectionInteractionService`.**

Конструктор принимает `ISectionAnalysisExecutor executor`. Метод:

```csharp
public Task<SectionInteractionResult> RunAsync(
    OpenSeesSectionModel model,
    SectionInteractionRequest request,
    OpenSeesRunRequest processRequest,
    CancellationToken cancellationToken);
```

Он должен вызвать `model.Validate()` и `request.Validate()` до первого запуска. Для каждого `force` в исходном порядке создать `SectionAnalysisRequest` с теми же `MaxCurvature`, `Increments`, `Axis`, `Convention` и `AxialForceN = force`. Перед каждой точкой вызвать `cancellationToken.ThrowIfCancellationRequested()`.

Результат одной точки строится так:

```csharp
SectionHistoryRow? lastConverged = analysis.Rows.LastOrDefault(row => row.Converged);
new SectionInteractionPoint
{
    AxialForceN = force,
    BendingMomentNm = lastConverged?.BendingMomentNm,
    Curvature = lastConverged?.Curvature,
    TerminalRow = lastConverged,
    Status = analysis.Status,
    Diagnostics = analysis.Diagnostics,
    ArtifactDirectory = analysis.ArtifactDirectory
};
```

После executor снова проверить токен, чтобы отмена, перехваченная внутренним сервисом, не позволила начать следующую точку. Необработанное исключение одной точки преобразовать в точку `error` с текстом исключения и продолжить остальные точки; `OperationCanceledException` пробросить. Итоговый статус вычислять по правилам из spec. `Diagnostics` результата должны содержать только агрегированные сообщения, а подробности каждой точки остаются в `Points`.

- [ ] **Шаг 5: Запустить тесты и проверить PASS.**

Запуск: `dotnet test OpenCS.OpenSees.Tests/OpenCS.OpenSees.Tests.csproj --filter FullyQualifiedName~SectionInteractionTests`

Ожидаемый результат: все unit-тесты контракта, порядка, выбора строки, статусов и отмены проходят.

- [ ] **Шаг 6: Зафиксировать сервис отдельным коммитом.**

```powershell
git add OpenCS.OpenSees/Services/ISectionAnalysisExecutor.cs OpenCS.OpenSees/Services/SectionInteractionService.cs OpenCS.OpenSees/Services/SectionAnalysisService.cs OpenCS.OpenSees.Tests/SectionInteractionTests.cs
git commit -m "feat(opensees): orchestrate sequential N-M analyses"
```

### Задача 3: Добавить параметры и task handler OpenCS

**Файлы:**

- Создать: `OpenCS/Tasks/OpenSeesSectionInteractionParams.cs`
- Создать: `OpenCS/Tasks/OpenSeesSectionInteractionHandler.cs`
- Изменить: `OpenCS.OpenSees.Tests/OpenSeesTaskContractTests.cs`

- [ ] **Шаг 1: Написать падающие contract-тесты для JSON.**

Добавить тест для JSON:

```json
{
  "axialForces": [-1000, 0, 1000],
  "maxCurvature": 0.02,
  "increments": 40,
  "axis": "My",
  "timeoutSeconds": 90,
  "executablePath": "C:/OpenSees.exe"
}
```

Проверить три силы в `kN`, параметры кривизны, ось, timeout и путь. Проверить, что `{}` использует `AxialForcesKn = [0]`, положительные defaults `MaxCurvature = 0.01`, `Increments = 20`, `TimeoutSeconds = 300`, а пустой список, `NaN`/`Infinity` после десериализации, неположительные `increments`/timeout/maxCurvature, дубликаты и неизвестная ось отклоняются `ArgumentException`.

- [ ] **Шаг 2: Запустить contract-тесты и убедиться, что новый тип отсутствует.**

Запуск: `dotnet test OpenCS.OpenSees.Tests/OpenCS.OpenSees.Tests.csproj --filter FullyQualifiedName~OpenSeesTaskContractTests`

Ожидаемый результат: новые тесты не компилируются до создания `OpenSeesSectionInteractionParams`.

- [ ] **Шаг 3: Реализовать parser параметров.**

Тип должен использовать `System.Text.Json` с `PropertyNameCaseInsensitive = true`, публичное свойство `IReadOnlyList<double> AxialForcesKn`, defaults из шага 1 и нормализацию `Axis` к строго `Mx`/`My`. Не использовать русские строки в task-контракте, так как сообщения parser не показываются напрямую в XAML.

- [ ] **Шаг 4: Проверить единицы и сериализуемость результата.**

В contract-тесте проверить:

```csharp
OpenSeesSectionInteractionParams parameters = OpenSeesSectionInteractionParams.Parse(json);
double[] axialForcesN = parameters.AxialForcesKn
    .Select(CScoreUnitConverter.KiloNewtonsToNewtons)
    .ToArray();

Assert.Equal(new[] { -1_000_000d, 0d, 1_000_000d }, axialForcesN);
string jsonResult = JsonSerializer.Serialize(new SectionInteractionResult
{
    Status = "ok",
    Points = [new SectionInteractionPoint { AxialForceN = 1_000, BendingMomentNm = 2_000 }]
});
Assert.Contains("\"AxialForceN\":1000", jsonResult);
```

- [ ] **Шаг 5: Реализовать handler.**

`OpenSeesSectionInteractionHandler : ITaskHandler` получает kind `opensees_section_interaction_nm`. В `Run` он должен повторить проверенный stage 0–1 pipeline: проверить cancellation, распарсить params, выбрать `Mx`/`My`, получить materials/diagrams из `TaskRunContext.Database`, вызвать `CrossSectionToOpenSeesAdapter.Build`, разрешить executable через `OpenSeesExecutableResolver`, сконвертировать `AxialForcesKn` в `AxialForcesN`, создать `SectionInteractionRequest`, затем создать `SectionAnalysisService` и `SectionInteractionService` с `OpenSeesArtifactStore` под `AppContext.BaseDirectory/OpenSeesArtifacts`.

Для каждой точки использовать один и тот же `OpenSeesRunRequest` с executable и timeout; внутренний interaction service обеспечит уникальные каталоги. Возвратить `CalcResult` с `Status = result.Status` и `DataJson = JsonSerializer.Serialize(result)`. `OperationCanceledException` вернуть как `not_converged`, прочие исключения — как `error`, не выбрасывая их наружу.

- [ ] **Шаг 6: Запустить все task-contract тесты.**

Запуск: `dotnet test OpenCS.OpenSees.Tests/OpenCS.OpenSees.Tests.csproj --filter FullyQualifiedName~OpenSeesTaskContractTests`

Ожидаемый результат: PASS, включая существующие тесты moment–curvature.

- [ ] **Шаг 7: Зафиксировать task pipeline отдельным коммитом.**

```powershell
git add OpenCS/Tasks/OpenSeesSectionInteractionParams.cs OpenCS/Tasks/OpenSeesSectionInteractionHandler.cs OpenCS.OpenSees.Tests/OpenSeesTaskContractTests.cs
git commit -m "feat(opensees): add N-M task contract and handler"
```

### Задача 4: Зарегистрировать task и локализовать название

**Файлы:**

- Изменить: `OpenCS/Tasks/TaskRunner.cs`
- Изменить: `OpenCS/Views/CalcTaskPropsDialog.xaml.cs`
- Изменить: `OpenCS/Resources/Strings.ru-RU.xaml`
- Изменить: `OpenCS/Resources/Strings.en-US.xaml`
- Изменить: `OpenCS.OpenSees.Tests/OpenSeesTaskContractTests.cs`

- [ ] **Шаг 1: Добавить проверку регистрации.**

В `TaskRunner.KindList` проверить наличие точного значения `opensees_section_interaction_nm` рядом с существующим `opensees_section_moment_curvature`.

- [ ] **Шаг 2: Добавить resource keys в оба словаря.**

Добавить одинаковый ключ `CalcTaskKind_opensees_section_interaction_nm` в `Strings.ru-RU.xaml` и `Strings.en-US.xaml`. Русское значение должно быть `OpenSees: диаграмма N–M`, английское — `OpenSees: N–M interaction`. В `.cs` и `.xaml` не добавлять видимый текст напрямую: список task должен использовать `Loc.S("CalcTaskKind_opensees_section_interaction_nm")`.

- [ ] **Шаг 3: Зарегистрировать обработчик и добавить пункт выбора.**

В словарь `Handlers` добавить:

```csharp
["opensees_section_interaction_nm"] = new OpenSeesSectionInteractionHandler(),
```

В список `CalcTaskPropsDialog` добавить `new()` с `Id = "opensees_section_interaction_nm"`, `Label = Loc.S("CalcTaskKind_opensees_section_interaction_nm")`, той же группой `other`, что используется текущей OpenSees task.

- [ ] **Шаг 4: Проверить локализацию и регистрацию сборкой.**

Запуск: `dotnet test OpenCS.OpenSees.Tests/OpenCS.OpenSees.Tests.csproj --filter FullyQualifiedName~OpenSeesTaskContractTests`

Затем: `dotnet build OpenCS/OpenCS.csproj --no-restore`

Ожидаемый результат: contract-тесты и WPF-сборка проходят без новых предупреждений.

- [ ] **Шаг 5: Зафиксировать регистрацию отдельным коммитом.**

```powershell
git add OpenCS/Tasks/TaskRunner.cs OpenCS/Views/CalcTaskPropsDialog.xaml.cs OpenCS/Resources/Strings.ru-RU.xaml OpenCS/Resources/Strings.en-US.xaml OpenCS.OpenSees.Tests/OpenSeesTaskContractTests.cs
git commit -m "feat(opensees): register N-M calculation task"
```

### Задача 5: Добавить реальную интеграцию и документацию

**Файлы:**

- Изменить: `OpenCS.OpenSees.Tests/OpenSeesIntegrationTests.cs`
- Изменить: `OpenCS.OpenSees/README.md`

- [ ] **Шаг 1: Написать opt-in integration-тест трёх точек.**

Использовать существующие `OpenSeesTestExecutable.ResolveOrSkip()` и `ElasticSection()`. Создать `SectionInteractionService` поверх реальных `SectionAnalysisService`, `SectionMomentCurvatureTclGenerator`, `OpenSeesProcessRunner` и временного `OpenSeesArtifactStore`. Запросить `AxialForcesN = [-100_000, 0, 100_000]`, `MaxCurvature = 1e-5`, `Increments = 2`.

Проверить `Status == "ok"`, ровно три точки в исходном порядке, конечные `BendingMomentNm` и `Curvature`, положительную кривизну и три различных существующих `ArtifactDirectory`. В каждом каталоге проверить `completed.marker`, `section_history.out` и `manifest.json`. В `finally` удалить временный root так же, как в существующих integration-тестах.

- [ ] **Шаг 2: Запустить только integration-тест.**

Запуск: `dotnet test OpenCS.OpenSees.Tests/OpenCS.OpenSees.Tests.csproj --filter FullyQualifiedName~OpenSeesIntegrationTests`

Ожидаемый результат: тест проходит при заданном `OPENSEES_EXE`; без executable opt-in-тесты пропускаются штатным `SkipException`.

- [ ] **Шаг 3: Обновить README.**

Добавить JSON-пример:

```powershell
$env:OPENSEES_EXE = 'C:\path\to\OpenSees.exe'
```

```json
{
  "axialForces": [-1000, 0, 1000],
  "maxCurvature": 0.01,
  "increments": 20,
  "axis": "Mx",
  "timeoutSeconds": 300
}
```

Явно указать, что `axialForces` задаются в `kN`, backend работает в SI, точки запускаются последовательно, каждая точка получает собственный каталог, а последняя converged-строка сохраняется даже при статусе `not_converged`. Перечислить агрегированные статусы и исключить из текущей версии `N-Mx-My`, target-force и parallel batch.

- [ ] **Шаг 4: Зафиксировать интеграцию и документацию.**

```powershell
git add OpenCS.OpenSees.Tests/OpenSeesIntegrationTests.cs OpenCS.OpenSees/README.md
git commit -m "test(opensees): cover N-M interaction integration"
```

### Задача 6: Полная проверка stage и handoff

- [ ] **Шаг 1: Запустить полный набор OpenSees-тестов.**

```powershell
dotnet test OpenCS.OpenSees.Tests/OpenCS.OpenSees.Tests.csproj
```

Ожидаемый результат: все pure-тесты проходят, integration-тесты проходят при `OPENSEES_EXE` или штатно пропускаются без него.

- [ ] **Шаг 2: Запустить доменные тесты и сборку решения.**

```powershell
dotnet test CScore.Tests/CScore.Tests.csproj
dotnet build OpenCS.sln --no-restore
```

Ожидаемый результат: тесты и solution build успешны; существующие предупреждения вне OpenSees не исправлять в рамках этого среза.

- [ ] **Шаг 3: Проверить артефакты и Git-состояние.**

Проверить один реальный каталог взаимодействия: три отдельных подкаталога, SI-значения в `script.tcl`, порядок `N`, наличие `manifest.json`, `exit.json`, `section_history.out` и `completed.marker`. Выполнить `git diff --check` и `git status -sb`; в рабочем дереве должны остаться только осознанные изменения/коммиты этой ветки.

- [ ] **Шаг 4: Зафиксировать выполнение плана и handoff.**

```powershell
git add docs/superpowers/plans/2026-07-15-opensees-section-interaction.md
git commit -m "docs(opensees): add N-M implementation plan"
```

После этого следующая граница реализации — отдельная спецификация `N-Mx-My` или target-force, без расширения текущего task скрытыми параметрами.

## Самопроверка плана

- Все требования спецификации покрыты задачами 1–6: модели, последовательный executor, статусы, артефакты, cancellation, task contract, локализация и opt-in integration.
- В плане нет шагов с `TODO`, `TBD`, неопределёнными файлами или ссылками на несуществующие методы; контракт `ISectionAnalysisExecutor.RunAsync` определён до его использования.
- Существующая task moment–curvature не меняет семантику запроса; новая task использует отдельный kind и отдельный params-класс.
- Параллелизм, пространственная диаграмма и target-force явно исключены, поэтому план остаётся одним тестируемым вертикальным срезом.
