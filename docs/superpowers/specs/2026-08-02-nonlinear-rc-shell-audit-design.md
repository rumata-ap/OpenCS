# Nonlinear RC-shell audit: provenance, equilibrium, regularization и mesh sensitivity

**Дата:** 2026-08-02
**Статус:** утверждённая спецификация
**Ветка:** `feature/nonlinear-rc-shell-audit`

## 1. Цель

Закрыть технические пробелы nonlinear RC-shell pipeline на базе OpenSees:

- передавать произвольное направление слоёв `PlateRebarLayer.Angle`;
- сохранять однозначный material/layer provenance для material states;
- описывать и проверять capability optional native responses;
- блокировать неверный выбор integration point/fiber вместо silent filtering;
- проверять равновесие сил и моментов, реакции, resultants и energy;
- ввести явный regularization contract;
- выполнять воспроизводимое coarse/medium/fine mesh sensitivity study.

Результат должен отличать физически подтверждённый audit от diagnostic-only и не может
объявлять количественно пригодным результат без подтверждённой regularization, energy или
damage/crack capability, если это потребовано policy.

## 2. Границы

### Входит

1. Расширение существующего native shell mapping и material-state catalog.
2. Strict preflight для model/material-state recording policy.
3. Чистый audit layer без WPF и SQLite-зависимостей.
4. Адаптеры для external work, state-integral energy и capability diagnostics.
5. Абстракция factory для трёх mesh levels и отчёт sensitivity.
6. Unit-тесты и реальные OpenSees integration tests.

### Не входит

- WPF UI, новые calculation task kinds и persistence audit reports в SQLite;
- автоматическое создание `PlanarRegion`, Gmsh topology или fragment workflow;
- восстановление настоящей истории parent nonlinear result;
- вычисление фиктивных crack/damage indicators из одного stress/strain;
- скрытая замена native material на `PlateSection.Compute()` или CSfea;
- объявление regularization capability без фактического backend adapter-а.

## 3. Архитектура

Срез состоит из двух слоёв.

### 3.1. Изменения существующего pipeline

Изменяются существующие mapper/generator/parser-контракты:

- `PlateSectionOpenSeesMapper` передаёт угол исходного слоя;
- `RCShellLayer` содержит направление и material/layer provenance;
- `NativeShellMaterialSpec` объявляет response и regularization capabilities;
- `ShellStateRecordingPolicy` проверяется против конкретной topology;
- `ShellTclGenerator` генерирует только валидные recorder groups;
- `ShellStateParser` разбирает обязательные metadata и optional responses.

### 3.2. Отдельный audit layer

В `OpenCS.OpenSees/Audit/` находятся чистые типы и сервисы:

- `ShellAuditPolicy`;
- `ShellRegularizationPolicy`;
- `ShellAuditReport`;
- `ShellEquilibriumAuditor`;
- `ShellEnergyAuditor`;
- `ShellSensitivityRunner`;
- `ShellMeshSensitivityReport`;
- `IShellAnalysisRunner` для генерации, запуска и парсинга одного normalized case;
- интерфейс factory для построения нормализованной модели на уровне сетки.

Audit layer принимает `ShellOpenSeesModel` и parsed `ShellResult`, но не создаёт WPF,
SQLite или Gmsh objects. Для sensitivity он получает factory, который строит новую
нормализованную модель и сохраняет исходный fingerprint/provenance.

## 4. Направление армирования

`PlateRebarLayer.Angle` задаёт угол направления `Asx` относительно локальной оси `LocalX`
shell-слоя.

- `Asx > 0` отображается как `PlateRebarShellMaterialSpec(angle = Angle)`;
- `Asy > 0` отображается как `PlateRebarShellMaterialSpec(angle = Angle + 90°)`;
- угол нормализуется в детерминированный диапазон `[-180°, 180°)`;
- `Face`, `Zsx/Zsy`, material id и направление не объединяются при дедупликации;
- `Angle`, `Asx`, `Asy`, face и z-координаты входят в layout/section fingerprint.

Это требует проверки обоих уровней fingerprint: `PlateRebarLayoutFingerprint` для
пространственного поля и `PlateSectionOpenSeesMapper` для исходного section fingerprint.
Нельзя считать requirement выполненным только потому, что угол вошёл в fingerprint native
material spec.

Если `Asx` и `Asy` заданы, это два независимых smeared layers. Объединение их в один
ортотропный материал запрещено. `RCShellLayer.DirectionDegrees` и Tcl material command
должны содержать фактические `φ` и `φ+90°`, а не placeholder `0°/90°`.

При конечном, но нестандартном угле mapping разрешён. Нефинитный угол блокируется
диагностикой `rebar_angle_invalid`.

## 5. Material and layer provenance

### 5.1. Нормализованный layer

Каждый `RCShellLayer` должен сохранять:

- индекс и `ShellLayerKind`;
- `CenterZ` и эквивалентную толщину;
- итоговый material tag;
- направление в градусах;
- стабильный `SourceId` исходного слоя;
- fingerprint исходного PlateSection/layout.

Для rebar `Thickness` трактуется как эквивалентная smeared thickness, полученная из
`As` на единицу ширины. Это явно сохраняется как approximation warning.

### 5.2. Catalog версии 2

`state_order.json` получает `version = 2`. Каждый `shellLayerGroup` содержит:

- `sectionTag`;
- `integrationPoint`;
- `layerIndex`;
- `responseKind`;
- `elementTags`;
- `fileName`;
- `componentCount`;
- `unit`;
- `materialTag`;
- `layerKind`;
- `sourceId`;
- `centerZ`;
- `thickness`;
- `sectionFingerprint`.

Metadata обязательны для v2. Stress/strain и optional response groups используют одну и ту
же identity metadata.

Recorder group identity включает как минимум `(sectionTag, integrationPoint, layerIndex,
responseKind)`. Элементы разных секций не объединяются в одну v2 group, даже если у них
совпадает число слоёв. `elementTags` внутри группы относятся к одной section identity,
поэтому `materialTag`, `sourceId`, `centerZ`, `thickness` и `sectionFingerprint` однозначны
для всей группы.

`ShellStateParser` не подставляет `materialTag = 1` или `Concrete`. Catalog v1 разрешается
разобрать только в legacy режиме с `state_catalog_provenance_missing`; строгий audit и typed state
mapping его не принимают. Так сохраняется возможность открыть старый artifact без ложного
утверждения о происхождении состояния.

`RCShellLayerState` получает source id, center/thickness и section fingerprint либо ссылку на
полный catalog entry. `MaterialTag` и `ShellLayerKind` для v2 обязательны.

## 6. Native response capabilities

`NativeShellMaterialSpec` предоставляет capability descriptors с полями:

- стабильное имя response;
- Tcl response/query contract, задаваемый backend adapter-ом, а не пользователем;
- количество компонент или explicit variable-width policy;
- единицы;
- признак обязательности/опциональности;
- предупреждения и ограничения применения.

Минимально обязательны `stress` и `strain`. `tangent`, `damage`, `crack` и `energy`
запрашиваются только через opt-in policy и только если все material layers, которые должны
их записывать, поддерживают response.

Если native material не предоставляет crack/damage response, OpenCS сохраняет доступные
stress/strain/tangent и warning. Нулевая жёсткость, знак stress или превышение прочности не
превращаются автоматически в crack/damage flag.

Optional groups записываются в catalog и materialized state dictionary. Parser проверяет
component count, units metadata, строку шага и finite values так же строго, как stress/strain.

## 7. Blocking recording validation

В этом срезе recording policy не содержит per-element selectors. Явный выбор применяется ко
всем shell elements или всем nonlinear beam elements соответствующего model. Per-element
selection остаётся отдельной capability.

`ShellOpenSeesModel.Validate()` вызывает validation recording policy после проверки topology:

- явные shell IP должны существовать у каждого shell element в заявленной области;
- явные nonlinear beam IP должны существовать у каждого nonlinear beam element;
- явные beam fiber indices должны существовать во всех затронутых beam sections;
- отрицательные и нулевые индексы блокируются;
- при `null` выбираются все применимые позиции каждого конкретного element/section;
- смешанная topology с различным числом IP допускается только при implicit `null` policy;
  explicit выбор, не покрывающий всю область, блокируется.

Генератор не использует silent `continue` или filtering для невалидных requested positions.
Все emitted groups должны иметь проверенную applicability map. Отсутствие requested group,
несогласованный layer index или невозможность materialize state дают
`recording_selection_invalid`.

## 8. Audit policy и verdict

`ShellAuditPolicy` содержит:

- absolute и relative equilibrium tolerances;
- требования к обязательным responses;
- energy mode и минимальный confidence;
- `ShellRegularizationPolicy`;
- sensitivity levels, sizes и comparison tolerances;
- режим `Strict` или `DiagnosticOnly`.

`ShellAuditReport` имеет verdict:

- `Passed` — все обязательные checks подтверждены;
- `Warning` — результат usable с явно перечисленными ограничениями;
- `Blocked` — preflight или обязательная capability не выполнена;
- `MeshDependent` — три запуска сошлись, но sensitivity tolerance превышена.

Частичный solver output, exit code 0 без подтверждённой сходимости, NaN/Inf или неполный
обязательный recorder не могут получить `Passed`.

Если один sensitivity case не построен, не запущен или не сошёлся, verdict равен `Blocked`
с `sensitivity_case_incomplete`. `MeshDependent` используется только когда все три case
успешно завершены и сравнение показало превышение tolerance.

## 9. Equilibrium and reactions

### 9.1. Generalized resultants

Вводится шестикомпонентный global resultant:

```text
(Fx, Fy, Fz, Mx, My, Mz)
```

Для nodal force в точке `r` момент относительно глобального начала равен `r × F + M`.
Для reactions используется та же операция. Поэтому проверяются не только суммы component
forces, но и моменты с учётом координат узлов.

### 9.2. Staged loads

Для converged step с `StageIndex = k` и текущим `LoadFactor = λ` audit восстанавливает:

```text
P(step) = Σ(Pstage[i] · MaxLoadFactor[i]), i < k
         + Pstage[k] · λ
```

Это соответствует `loadConst` и новым proportional patterns, эмитируемым
`ShellTclGenerator`. Для каждого шага сохраняются applied resultant, recorded reaction
resultant и residual `P + R`.

`ShellEquilibriumStepReport` содержит absolute/relative residual и pass/fail. Ошибка
равновесия учитывает six DOF и policy tolerances. Element forces и section resultants
сохраняются как отдельные локальные checks и provenance, но не подменяют support-reaction
balance.

## 10. Energy

`ShellEnergyAuditor` выдаёт значения и confidence:

1. `NativeResponse` — native material/backend отдал проверенный energy response.
2. `StateIntegral` — численная интеграция сопряжённых component pairs, объявленных
   response capability, по истории состояний с IP weights, площадью element и толщиной
   layer. Неизвестный порядок пяти raw components не используется неявно.
3. `ExternalWorkOnly` — работа force/moment nodal loads по трапециям между соседними
   converged steps, включая момент-ротацию.
4. `Unavailable` — обязательные исходные данные отсутствуют.

Материальная работа и dissipated energy не смешиваются с external work. Если доступна только
stress/strain history, report помечает state integral как оценку и не называет его native
dissipation. Strict policy требует confidence не ниже настроенного уровня; иначе verdict
`Blocked` или `Warning` в зависимости от режима.

Для staged workflow `P(step)` берётся из полного вектора из §9.2. Инкрементальная внешняя
работа вычисляется как `0.5 · (Pprev + Pstep) · (Ustep - Uprev)`. Работа
prescribed-displacement DOF (`sp`) считается отдельным `KinematicReactionWork` по recorded
reactions и не смешивается с force-pattern work. Для material state integration catalog
сохраняет порядок компонентов и сопряжённые stress/strain pairs; при отсутствии такой
декларации `StateIntegral` имеет статус `Unavailable`.

## 11. Regularization

`ShellRegularizationPolicy` поддерживает:

- `None`;
- `ElementCharacteristicLength`;
- `CrackBand`;
- `FractureEnergy`.

Для каждого shell element characteristic length вычисляется из локальной геометрии как
`sqrt(area)`. В report сохраняются area, length, element tag, method и source fingerprint.

`ElementCharacteristicLength`, `CrackBand` и `FractureEnergy` требуют
`IShellRegularizedMaterialAdapter`, который фактически применяет length/energy в native
material mapping. Наличие enum или поля в manifest не является применением regularization.

Текущий срез реализует общий contract, вычисление/проверку characteristic length и strict
diagnostics. Native adapter для `PlasticDamageConcretePlaneStress` может объявить
`ElementCharacteristicLength` только после реального verification, показывающего изменение
softening response согласно длине. `CrackBand` и `FractureEnergy` при отсутствии такого
adapter-а распознаются policy, но блокируются `regularization_unsupported`; это проверяемое
поведение, а не silently deferred feature.

Текущий `PlasticDamageConcretePlaneStress` объявляется capability только после реального
OpenSees verification. Если adapter не умеет выбранный режим:

- Strict softening audit получает `regularization_unsupported` и `Blocked`;
- DiagnosticOnly получает `Warning` с `regularization_applied = false`;
- результат не называется mesh-independent.

Изменение regularization policy, characteristic length method или material adapter меняет
section/model fingerprint.

## 12. Mesh sensitivity

`ShellSensitivityRunner` получает `IShellSensitivityCaseFactory`, который по
`ShellSensitivityLevel` возвращает:

- normalized `ShellOpenSeesModel`;
- requested target size/scale и способ построения case;
- source mesh/settings fingerprint;
- artifact identity;
- выбранный displacement observable и load path.

Factory является владельцем remeshing или выбора заранее построенных snapshots. В этом
срезе audit layer не создаёт Gmsh geometry: production caller может передать Gmsh-backed
factory, а tests используют deterministic in-memory factory. Factory обязан вернуть три
различных mesh/settings fingerprints и явно указать, применялся ли scale к target size.
Runner не делает вид, что изменил mesh, если factory вернул ту же topology.

Минимальный набор уровней:

```text
coarse = 2.0 * base target size
medium = 1.0 * base target size
fine   = 0.5 * base target size
```

Каждый уровень запускается тем же generator/runner/parser и получает отдельную artifact
directory. Сравниваются:

- convergence and last load factor;
- load-displacement curve на нормализованных load factors;
- final reactions и equilibrium residual;
- external/material work и energy confidence;
- layer state и crack/damage localization, только если capability присутствует.

Невозможность сравнить metric отмечается `unavailable`, а не нулём. Превышение policy
tolerance выдаёт `MeshDependent`. Отчёт содержит все case fingerprints и diagnostics,
поэтому повторный sensitivity run воспроизводим.

## 13. Diagnostics и error handling

Стабильные blocking/warning codes:

- `rebar_angle_invalid`;
- `state_catalog_provenance_missing`;
- `unsupported_shell_response`;
- `recording_selection_invalid`;
- `material_tangent_unavailable`;
- `regularization_unsupported`;
- `energy_unavailable`;
- `equilibrium_not_satisfied`;
- `mesh_dependent`;
- `result_output_incomplete`.

Для v1 catalog без provenance используется только `state_catalog_provenance_missing`; это
единственный код данного случая во всех parser/audit слоях.

`ShellAuditPreflightResult` содержит `IsCalculable` и список structured diagnostics. Audit
перехватывает исключения generic `ShellOpenSeesModel.Validate()` и преобразует их в
`result_output_incomplete` или более специфичный code; preflight diagnostics возвращаются без
запуска OpenSees. `IShellAnalysisRunner` отделяет audit от `IOpenSeesProcessRunner` и
позволяет подменять process runner в unit tests. Runtime/parser diagnostics сохраняют artifact
directory и не превращают неполный результат в успешный. Каждая diagnostics entry содержит
code, severity, human-readable message и, если применимо, element/IP/layer, artifact или
source fingerprint.

## 14. Верификация

### Unit tests

- mapping `Angle = 45°`, `φ+90°`, face/z/material separation и fingerprint;
- mapper provenance для concrete и rebar layers;
- catalog v2 serialization/parsing и отказ от ложных defaults;
- optional response groups, component count, units и finite validation;
- invalid shell IP, beam IP и fiber selection как blocking diagnostics;
- staged load reconstruction с force/moment относительно координат узлов;
- energy confidence modes;
- characteristic length для Q4/T3;
- sensitivity verdicts на deterministic fake case factory.

Новые domain types и VMs, если они появятся, получают русские XML doc comments согласно
`AGENTS.md`; в этот срез UI/VM не входит.

### Реальный OpenSees

- anisotropic `Asx != Asy` shell с углом 45°: generated Tcl, convergence, direction provenance
  и физический response check;
- Q4, T3 и mixed shell equilibrium по force/moment residual;
- shell-beam junction с reaction balance;
- material state catalog с material/layer/source metadata;
- фактически поддержанный optional response, если capability probe его подтверждает;
- unsupported response и unsupported regularization как explicit warning/blocking behavior;
- coarse/medium/fine smoke с одинаковым load path и отдельными artifacts. При наличии
  `C:\Tools\gmsh-4.15.2-Windows64\gmsh.exe` запускается Gmsh-backed factory; без Gmsh
  реальный OpenSees smoke использует deterministic prebuilt mesh factory и не маскирует
  отсутствие Gmsh под успешный remesh.

### Регрессия

До завершения среза должны быть запущены:

- `dotnet build OpenCS.sln`;
- `dotnet test CScore.Tests/CScore.Tests.csproj`;
- `dotnet test OpenCS.OpenSees.Tests/OpenCS.OpenSees.Tests.csproj`;
- `dotnet test OpenCS.Gmsh.Tests/OpenCS.Gmsh.Tests.csproj`.

Известные baseline issues не приписываются срезу: два MSB9008 warning о отсутствующем
`OpenCS.Core.UI`, один COM skip в `CScore.Tests` и flaky SQLite cleanup race в полном
параллельном `OpenCS.OpenSees.Tests`.

## 15. Совместимость и provenance

- Исходные `PlateSection`, `PlanarRegion`, Gmsh snapshots и imported mesh не изменяются
  audit layer-ом.
- Старый v1 state catalog читается только в legacy/display режиме и не участвует в strict
  audit без provenance.
- Gmsh/OpenSees tags не становятся OpenCS domain IDs.
- Все изменения angle, material capability, state catalog version, recording policy,
  regularization, mesh case и generator/parser version входят в fingerprints.
- UI и SQLite schema в этот срез не добавляются.

## 16. Критерий завершения

Срез считается закрытым, когда:

1. Все backend contracts из разделов 4–7 реализованы и покрыты unit tests.
2. Audit report формируется для converged и non-converged runs без ложного `Passed`.
3. Equilibrium, energy confidence, regularization status и sensitivity verdict сохраняются
   в типизированной модели и artifact report.
4. Реальные OpenSees tests подтверждают angle/provenance/equilibrium и capability behavior.
5. Unsupported native features приводят к явным warning/blocking diagnostics.
6. Build и обязательные test projects проходят с зафиксированными baseline exceptions.
   OpenSees integration tests используют `ResolveOrSkip()` при отсутствии executable;
   на машине с `C:\Tools\OpenSees\bin\OpenSees.exe` они должны реально выполняться, а не
   только компилироваться.
