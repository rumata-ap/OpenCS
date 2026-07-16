# Пространственная диаграмма N-Mx-My OpenSees: дизайн

## Цель

Добавить в OpenCS первый пространственный срез интеграции с OpenSees: для каждой заданной продольной силы `N` построить полный оборот лучей кривизны и получить совместные компоненты `Mx` и `My`.

Расчёт остаётся монотонным. Каждый луч для пары `(N, угол)` запускается независимо, поэтому состояние нелинейных материалов не переносится между направлениями, а диагностика и артефакты каждого запуска сохраняются отдельно.

## Область среза

В срез входят:

- библиотечные контракты запроса, строки истории, точки и итогового результата;
- 3D Tcl-модель OpenSees для fiber section;
- parser пространственной истории;
- последовательный сервис оркестрации `(N, угол)`;
- OpenCS task и JSON-контракт;
- unit-, contract-, snapshot- и opt-in integration-тесты;
- README с единицами, соглашением знаков и структурой результата.

В срез не входят WPF-график, target-force обратная задача, параллельное выполнение, пространственная batch-задача, циклическая история материалов и структурная модель.

## Входной контракт

Публичный OpenCS JSON task имеет kind:

```text
opensees_section_interaction_n_mx_my
```

Параметры task. Набор продольных сил выбирается в UI и хранится в `CalcTask.ForceSetId`; `axialForces` в JSON не дублируются:

```json
{
  "angleStepDegrees": 45,
  "maxCurvature": 0.01,
  "increments": 20,
  "timeoutSeconds": 300,
  "executablePath": "C:/path/to/OpenSees.exe"
}
```

Handler находит bar `ForceSet` по `CalcTask.ForceSetId`, берёт значения `N` его строк в исходном порядке и удаляет точные дубликаты. Эти значения задаются в кН и ровно один раз переводятся в Н через существующий `CScoreUnitConverter.KiloNewtonsToNewtons`. В библиотечном контракте силы хранятся в Н, кривизна — в `1/м`, моменты — в Н·м.

`angleStepDegrees` должен быть конечным, положительным и делить `360` без остатка. Сервис генерирует углы `0, step, ..., 360-step`; угол `360` не добавляется, поскольку дублирует `0`. Порядок сил и углов детерминирован и сохраняется.

Кривизна для угла `theta` и радиального шага `kappa` задаётся так:

```text
CurvatureMx = kappa * cos(theta)
CurvatureMy = kappa * sin(theta)
```

Угол `0°` означает `+Mx`, `90°` — `+My`, направление увеличения угла — против часовой стрелки в плоскости `(Mx, My)`. Отрицательные направления получаются при `180°` и `270°`.

Библиотечный запрос проверяет непустой список конечных уникальных сил, конечный положительный `MaxCurvature`, положительные `Increments` и корректный шаг угла. Допускается ровно один источник значения `360/step`; floating-point остаток проверяется с абсолютной погрешностью, достаточной для обычных десятичных шагов, но не разрешает шаги, дающие неполный оборот.

## Нейтральная модель и отображение осей

Существующий `OpenSeesSectionModel` и mapping prepared `CrossSection` переиспользуются без изменения. Fiber-координаты остаются в соглашении:

```text
OpenSees Y = CScore Y
OpenSees Z = CScore X
```

Для 3D section response OpenSees компоненты отображаются явно:

```text
OpenSees P  -> CScore N
OpenSees Mz -> CScore Mx
OpenSees My -> CScore My
```

Таким образом, имя `CurvatureMx` означает кривизну, сопряжённую с `Mx`, и не зависит от индекса DOF OpenSees. В Tcl-адаптере не допускаются неименованные перестановки массивов.

## Архитектура

2D и 3D расчёты имеют отдельные контракты и протоколы, чтобы не изменить стабильный stage 0–1:

- `SpatialSectionAnalysisRequest` — одна внутренняя точка `(N, угол)`;
- `SpatialSectionHistoryRow` — строка с `N`, `Mx`, `My`, `CurvatureMx`, `CurvatureMy`, модулем кривизны, сходимостью и невязкой;
- `SpatialSectionAnalysisResult` — история, статус, диагностика и каталог артефактов;
- `SectionSpatialInteractionRequest` — силы, шаг угла и радиальные параметры;
- `SectionSpatialInteractionPoint` — полная радиальная история для `(N, угол)`, последняя сошедшаяся строка и её артефакты;
- `SectionSpatialInteractionResult` — все точки и агрегированный статус;
- `SpatialSectionTclGenerator` и `SpatialSectionResultParser` — 3D Tcl и строгий parser;
- `SpatialSectionAnalysisService` — один изолированный запуск;
- `SectionSpatialInteractionService` — последовательный перебор сил и углов.

Общие `OpenSeesProcessRunner`, `OpenSeesArtifactStore`, `OpenSeesRunRequest`, resolver и `OpenSeesSectionModel` не дублируются.

Порядок обхода точек: внешний цикл по исходному списку `N`, внутренний цикл по углам от `0°` до `360° - step`. Для каждой точки создаётся собственный вызов `SpatialSectionAnalysisService`, а значит собственный рабочий каталог и recorder-файлы.

## 3D Tcl-модель

Генератор создаёт `model basic -ndm 3 -ndf 6`, fiber section с существующими material tags и `zeroLengthSection` в 3D. Узлы имеют нулевое расстояние; первый узел закреплён. У второго узла активны только осевое перемещение `DOF 1` и вращения `DOF 5`/`DOF 6`; поперечные перемещения `DOF 2`/`DOF 3` и кручение `DOF 4` фиксируются. `DOF 6` задаёт `CurvatureMx` и сопряжён с OpenSees `Mz`, а `DOF 5` задаёт `CurvatureMy` и сопряжён с OpenSees `My`.

После приложения `N` и его фиксации сценарий задаёт два совместных single-point displacement constraints для вращений второго узла. Их значения пропорциональны `CurvatureMx` и `CurvatureMy`, а `LoadControl` увеличивает общий радиальный множитель от нуля до единицы. На каждом шаге в `section_history.out` записываются:

```text
step loadFactor axialForceN momentMxNm momentMyNm curvatureMx curvatureMy curvatureMagnitude converged residual
```

`section force` и node response сохраняются как raw OpenSees values, затем parser переводит их в именованные CScore-компоненты через `OpenSeesCoordinateConvention`. Все числа в Tcl форматируются invariant-culture `G17`; пользовательские пути не интерполируются как Tcl-команды.

## Статусы и ошибки

Для точки используются `ok`, `not_converged`, `error`. Полная радиальная история сохраняется в `HistoryRows`; последняя сошедшаяся строка сохраняется отдельно даже если более поздний радиальный шаг не сошёлся. Если сходящихся строк нет, конечные значения остаются `null`.

Итоговый статус:

- `error`, если хотя бы одна точка имеет ошибку mapping, запуска, parser или артефактов;
- `not_converged`, если ошибок нет, но хотя бы одна точка не сошлась;
- `ok`, если все точки сошлись.

Ошибки и отмена не маскируются. `CancellationToken` передаётся через все async-границы; отмена прекращает последующие точки и сохраняет уже созданные каталоги. Артефакты сохраняются при ошибке процесса, отсутствии marker и повреждённой истории.

## OpenCS task

`OpenSeesSectionSpatialInteractionHandler` повторяет проверенный pipeline stage 0–1: проверяет отмену, разбирает JSON, получает bar `ForceSet` и его `N` из `TaskRunContext.Database`, получает materials/diagrams, строит prepared `OpenSeesSectionModel`, разрешает executable, конвертирует силы к Н, создаёт пространственный request и запускает сервис под `AppContext.BaseDirectory/OpenSeesArtifacts`.

Результат сериализуется через `System.Text.Json` в `CalcResult.DataJson`, а `CalcResult.Status` получает агрегированный статус. Исключения подготовки и запуска преобразуются в типизированный `error`-результат с диагностикой и не выбрасываются наружу task runner.

В оба resource dictionary добавляется локализованный ключ `CalcTaskKind_opensees_section_interaction_n_mx_my`; видимые строки в коде и XAML не хардкодятся.

## Тестирование и критерии готовности

Нужны следующие проверки:

1. Контрактный тест валидирует силы, шаг угла, количество углов и порядок точек.
2. Тест формул подтверждает `0° = +Mx`, `90° = +My`, отрицательные направления и радиальные шаги.
3. Snapshot-тест проверяет 3D Tcl, fiber mapping, recorder schema, invariant decimals и отсутствие 2D DOF-команд.
4. Parser-тесты покрывают корректную историю, пустой файл, пропущенные колонки, нечисловые значения и обрыв истории.
5. Fake-executor тестирует порядок `(N, угол)`, выбор последней сходящейся строки, агрегацию статусов и отмену.
6. Task-contract тест проверяет `kind`, единицы кН→Н, defaults и сериализацию результата.
7. Opt-in OpenSees integration-тест запускает полный оборот на упругой симметричной секции, проверяет чистые направления, знак компонентов, число углов и уникальные артефакты.
8. После реализации проходят `dotnet test OpenCS.OpenSees.Tests`, `dotnet test CScore.Tests` и `dotnet build OpenCS.sln`.

Критерий готовности: prepared `CrossSection` преобразуется в 3D fiber section; для каждой силы строится полный оборот направлений; `Mx` и `My` записываются совместно с корректными знаками; частичные ошибки сохраняют диагностику; task возвращает детерминированный JSON и не ломает существующий 2D pipeline.
