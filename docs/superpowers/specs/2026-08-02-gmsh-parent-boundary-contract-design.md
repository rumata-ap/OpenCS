# Parent actions и cut-interface contract для вертикального planar fragment

Дата: 2026-08-02

## Статус

Дизайн согласован пользователем в рамках brainstorming-сессии.

Документ уточняет следующий этап дорожной карты Gmsh: перенос воздействий от
родительской FEM-модели и явных boundary templates на первый фрагмент
вертикальной стены. Документ не заменяет исходную спецификацию вертикальных
фрагментов, а фиксирует mesh-независимый контракт, необходимый до production
fragment workflow.

Связанные материалы:

- `docs/superpowers/specs/2026-07-26-vertical-planar-fragmentation-design.md`;
- `docs/superpowers/specs/2026-07-26-opensees-boundary-conditions-connections-design.md`;
- `docs/superpowers/specs/2026-07-26-gmsh-planar-meshing-design.md`;
- `docs/superpowers/specs/2026-07-26-planar-constructive-elements-design.md`;
- Obsidian: `2026-07-26 Анализ спецов OpenSees и рекомендация по началу реализации.md`;
- Obsidian: `2026-08-01 Дорожная карта — интеграция Gmsh срезы.md`;
- Obsidian: `2026-08-01 Дорожная карта — полная интеграция Gmsh.md`.

## Контекст

Срезы Gmsh 1–6 уже реализовали:

- `PlanarRegion`, `Frame3D` и boundary roles;
- производный `PlanarMeshSnapshot` с physical/entity provenance;
- независимые surface/boundary/point `PlanarLoad` и balance-check;
- внутренние constraint loci и FEM-driven derivation;
- OpenSees-first structural constraints;
- `PlanarConnection` и mapping двух независимых сеток;
- shell adapters для OpenSees и CSfea.

Остаётся безопасно передать в самостоятельный фрагмент действие частей
родительской модели, которые были отсечены. Нельзя заменить это действие
локальным `fix`: такая замена создаёт искусственную опору, меняет физику задачи и
может привести к двойному учёту нагрузок.

Первым прикладным объектом является вертикальный или слегка наклонный
плоский фрагмент стены. Он задаётся двумя cut interfaces, обычно нижним и
верхним, и рассчитывается на новой независимой Gmsh-сетке.

## Цели

Контракт должен:

- одинаково описывать parent result и явный boundary template;
- поддерживать force и kinematic subcases, но не смешивать их неявно;
- разрешать `parent`, `template` и `combined` только явным режимом;
- работать независимо от Gmsh node tags и OpenSees tags;
- сохранять frame, units, sign convention и provenance каждого действия;
- переносить action на новую mesh интегрированием по mapped edge chain;
- проверять силу, момент, покрытие DOF и отсутствие двойного учёта;
- позволить OpenSees и CSfea использовать один нормализованный результат;
- оставаться пригодным для будущей beam-фрагментации и floor junction.

## Не входит

В первый implementation package не входят:

- WPF-редактор извлечения стены и fragment assembly;
- автоматическая реконструкция shell-группы из ЛИРА или SCAD;
- полноценные production adapters всех внешних импортёров;
- multi-region Gmsh assembly и shared-node meshing;
- springs, Robin/contact и новые solver equations;
- восстановление фактической нелинейной истории parent-модели;
- автоматическое сложение parent и template actions на одном DOF;
- полная persistence-модель `VerticalPlanarFragment`.

Пакет включает domain contract, synthetic parent/template providers, mapping на
существующий `PlanarMeshSnapshot`, OpenSees/CSfea application capabilities и
сквозные тесты. UI, persistence и production orchestration являются следующими
срезами после стабилизации контракта.

## Инварианты

- `PlanarRegion` остаётся источником геометрии; fragment mesh является
  производным snapshot.
- `CutInterface` не создаёт `fix` и не знает solver tags.
- Gmsh tags не являются domain IDs или OpenSees tags.
- Любое действие описывает воздействие отброшенной стороны на сохраняемый
  фрагмент, а не внутреннее усилие parent-элемента.
- Нормаль интерфейса направлена от фрагмента к отброшенной стороне.
- Силовые resultants и prescribed kinematics имеют разные subcases.
- Неполное mapping, неизвестные units/signs и конфликт DOF блокируют расчёт.
- Ненулевое prescribed displacement никогда не кодируется как `fix`.
- Старые snapshot и результаты не удаляются при invalidation и не выбираются
  автоматически вместо актуальных данных.
- Никакие contributions parent и template не складываются молча.

## Архитектура

`VerticalPlanarFragment` остаётся прикладным агрегатом вертикального фрагмента.
Он переиспользует существующие `PlanarRegion`, `Frame3D`, `PlanarConnection`,
constraint mappings и `PlanarMeshSnapshot`, но эти объекты не получают знаний о
parent result.

```text
VerticalPlanarFragment
  ├─ FragmentFrame / WallAxis3D
  ├─ BottomCutInterface
  ├─ TopCutInterface
  ├─ side/opening boundary classification
  ├─ SourceResultScenario
  ├─ BoundaryActionSet
  ├─ MeshSnapshot
  └─ provenance/fingerprint
```

```text
ParentBoundaryActionProvider ─┐
                              ├─ BoundaryActionResolver
BoundaryTemplateProvider ────┘          │
                                        ↓
                              BoundaryActionSet
                                        │
                              CutInterfaceMeshMapper
                                        │
                              OpenSees / CSfea adapter
```

`CutInterface` отвечает за geometry, orientation, сторону parent-модели,
boundary mode и mesh mapping. `BoundaryActionSet` отвечает за физические
действия, их samples, coverage, баланс и provenance. Solver adapters получают
только проверенный результат последнего слоя.

Для v1 переиспользуется существующий `PlanarDofMask`, а также 6-DOF соглашение
проекта `UX`, `UY`, `UZ`, `RX`, `RY`, `RZ`. Новая параллельная маска для cut
interfaces не вводится.

## Cut interface

Каждый interface имеет:

- стабильный доменный `InterfaceId`;
- роль `bottom_cut`, `top_cut` или будущую `side_cut`;
- curve или набор ordered curves в геометрии фрагмента;
- `NormalFromFragmentToOmittedSide`;
- локальный `FragmentFrame` и reference point;
- `BoundaryModeByDof`;
- ссылку на parent omitted side;
- mapping fingerprint и diagnostics.

Нижний и верхний cut по умолчанию являются внешними interfaces. Боковой край
не становится искусственной опорой автоматически:

- физически свободный край получает `free`;
- продолжение отброшенной стены получает `side_cut` и action;
- примыкание к объекту fragment assembly получает `internal_connection`;
- край отверстия сохраняет `opening`, если нет включённого обрамления;
- неизвестная роль является blocking diagnostic.

Геометрия interface не задаётся списком mesh nodes. Она строится из
геометрического пересечения `PlanarRegion` с cut plane и затем отображается на
конкретную mesh.

## Boundary mode по DOF

Для каждой компоненты шести DOF хранится отдельный режим:

```text
force
kinematic
preserve_support
free
incomplete
```

Смысл режимов:

- `force` — на интерфейс передаётся сила и/или момент;
- `kinematic` — задаётся перемещение и/или поворот;
- `preserve_support` — сохраняется только подтверждённая исходная опора;
- `free` — действие отсутствует и физическая граница свободна;
- `incomplete` — режим ещё не разрешён источником и блокирует расчёт.

`preserve_support` не является способом задать parent action. Он допускается
только при наличии provenance исходной опоры и применяется отдельно от
parent/template contributions.

## Boundary action

Действие на interface имеет один из двух независимых subcase-типов:

```text
force
kinematic
```

### Force subcase

Force action хранит ordered samples по нормированной координате интерфейса
`s ∈ [0, 1]`. В sample присутствуют:

- три компоненты силы;
- три компоненты момента на единицу длины;
- frame и units;
- reference point;
- source contribution references.

Силовые resultants для верхнего/нижнего shell-сечения соответствуют нормальной
силе, поперечным силам, изгибающим и крутящему компонентам поддерживаемой
модели `PlateSection`. Provider обязан привести их к физическому действию со
стороны omitted side.

Перенос момента к reference point выполняется явно:

```text
Minterface = Melement + r × F
```

Знак не выводится из имени `top`/`bottom` или из порядка mesh nodes. Он следует
из `NormalFromFragmentToOmittedSide`, исходного frame и записанной sign policy.

### Kinematic subcase

Kinematic action имеет ordered samples с шестью компонентами `U/R` и явной
политикой interpolation. В первой реализации поддерживается геометрическая
интерполяция по ordered interface nodes/edges и source elements. Значения не
подменяются нулём при отсутствии sample.

## Source mode и объединение

Источники выбираются явно:

```text
parent
template
combined
```

`parent` использует только parent provider, `template` — только template
provider. `combined` разрешает только непересекающееся покрытие DOF.

Правила `combined`:

- force и force на одном DOF являются конфликтом;
- kinematic и kinematic на одном DOF являются конфликтом, даже если значения
  совпадают;
- force и kinematic на одном DOF всегда являются конфликтом;
- contributions не суммируются автоматически;
- намеренная сумма должна быть заранее подготовлена внутри одного provider-а
  с сохранением списка contributions;
- непересекающиеся DOF из parent и template разрешаются в общий set.

Такой строгий режим предотвращает двойной учёт и сохраняет однозначность
происхождения действия.

## Parent result scenario и provider

`SourceResultScenario` описывает одно состояние parent-модели:

- source model fingerprint и provenance;
- result identity;
- load case или combination;
- для nonlinear result — конкретные `StageIndex` и converged `StepIndex`;
- units и sign convention;
- source topology и mapping policy;
- выбранный force/kinematic subcase;
- явная residual policy, если она включена.

Нелинейная история не реконструируется. Parent используется как одно состояние
`P0 → U0, F0`. При необходимости синтетический путь

```text
P(λ) = λP0
U(λ) = λU0
BoundaryAction(λ) = λBoundaryAction0
```

строится отдельной policy и не выдаётся за фактическую историю трещин,
пластичности или prestress.

`ParentBoundaryActionProvider` получает scenario, source topology, cut geometry
и requested subcase. Он возвращает нормализованные samples, contribution
breakdown и diagnostics. Provider не знает Gmsh node IDs и OpenSees tags.

### Parent kinematic provider

Provider:

- сопоставляет cut geometry с parent shell/beam topology;
- извлекает `U/R` из parent result;
- строит ordered samples по interface;
- интерполирует только по геометрически подтверждённым source elements;
- не использует nearest-node эвристику;
- блокирует неполное source coverage.

### Parent force provider

Provider:

- собирает contributions от parent shell elements, beam elements, nodal loads,
  reactions и нормализованных constraints в рамках выбранной policy;
- переводит исходные local resultants в global, затем в `FragmentFrame`;
- переносит момент к interface reference point;
- возвращает contribution breakdown и их provenance;
- выполняет force/moment balance-check;
- блокирует неизвестные units, signs, incomplete mapping и неполный баланс.

Допускается residual policy:

```text
BoundaryAction = RetainedResidual - RetainedExplicitLoad
```

Она включается только явно в scenario и сохраняет перечень retained
contributions, вошедших в остаток. Это не скрытый fallback provider-а.

Первый implementation package использует synthetic parent topology/result
fixtures. Позднее OpenSees `ShellResult`, ЛИРА и SCAD подключаются как adapters
к тому же provider boundary без изменения domain model.

## Boundary template provider

`BoundaryTemplateProvider` выдаёт те же normalized samples, что и parent provider,
но значения задаются явно. Template обязан содержать:

- interface identity;
- subcase type;
- covered DOF;
- ordered samples или uniform field;
- frame и units;
- sign convention;
- interpolation policy;
- provenance template version/identity.

Template может быть задан в `FragmentFrame` или в global frame. Frame и units
обязательны. Непокрытый DOF остаётся незаданным и разрешается только другим
source mode; нулевое значение не считается покрытием.

## Нормализация

```text
raw parent result / explicit template
        ↓
geometric source mapping
        ↓
frame + unit + sign normalization
        ↓
ordered BoundaryAction samples
        ↓
coverage / balance / conflict validation
        ↓
BoundaryActionSet
```

`BoundaryActionResolver` не выполняет solver-specific преобразований. Он
разрешает source mode, проверяет покрытие DOF и создаёт единый immutable-ready
результат для следующего слоя.

## Mapping на PlanarMeshSnapshot

Для каждого interface строится:

```text
CutInterfaceMeshMapping
  ├─ InterfaceId
  ├─ ordered snapshot node indices
  ├─ ordered snapshot edge pairs
  ├─ cumulative arclength
  ├─ normalized s
  ├─ orientation and normal
  ├─ snapshot fingerprint
  └─ diagnostics
```

Структура повторяет проверенные в `PlanarConnectionMeshMapping` правила
orientation, cumulative arclength, reverse direction и stale fingerprint, но
семантически относится к cut interface.

### Force mapping

`BoundaryActionMeshMapper`:

- проверяет полное покрытие ordered edge chain;
- интерполирует samples по `s`;
- интегрирует силы и моменты по реальным mesh edges;
- собирает consistent nodal actions;
- проверяет applied/mapped force и moment с явными допусками;
- учитывает разрывы из-за отверстий как отдельные curves;
- не назначает action ближайшим node и не отбрасывает короткие сегменты.

### Kinematic mapping

Mapper:

- вычисляет `U/R` в каждом mapped interface node по явной interpolation policy;
- возвращает prescribed DOF assignments;
- проверяет duplicate и incompatible values;
- не преобразует ненулевое значение в fixed zero;
- сохраняет node-to-sample provenance.

## Solver adapters

Adapters получают только calculable `BoundaryActionMeshMappingResult`.

### OpenSees

Для OpenSees:

- force actions становятся `ShellNodalLoad` внутри выбранной стадии;
- `preserve_support` попадает в `NormalizedShellNode.Fixed`;
- kinematic actions становятся явными `sp`/prescribed DOF;
- fixed/prescribed conflict проверяется до Tcl generation;
- solver tags назначаются отдельным allocator-ом и не попадают в domain
  provenance вместо исходных IDs.

Текущий `ShellNonlinearStage` содержит только узловые силы. Реализация
контракта потребует расширить normalized shell model/Tcl generator поддержкой
prescribed DOF, не кодируя их через искусственный `fix`.

### CSfea

Для CSfea:

- force actions собираются в полный nodal force vector;
- `preserve_support` и kinematic actions передаются через `fixedDofs` и
  `uFixed`;
- linear и nonlinear Dirichlet paths используют один boundary contract;
- нерасчётный mapping или неподдержанный capability блокирует вызов solver-а.

Существующий `ShellMesh.SolveLinear`/nonlinear `BoundaryConditions` уже имеет
явное разделение fixed DOF и prescribed values, поэтому CSfea adapter не должен
изобретать отдельный способ кодирования.

## Диагностики

Любая ошибка подготовки или mapping-а создаёт диагностический результат, но не
расчётный solver input. Solver запускается только при `IsCalculable=true`.

### Blocking diagnostics

- interface не пересекает или не покрывает fragment geometry;
- неоднозначная orientation, normal или `FragmentFrame`;
- stale `PlanarMeshSnapshot`;
- неполное source topology или mesh edge mapping;
- неизвестные units или sign convention;
- отсутствующий requested parent result;
- nonlinear parent step не converged;
- force/moment balance вне допуска;
- overlapping or conflicting source DOF;
- force/kinematic conflict;
- prescribed DOF поверх incompatible fixed DOF;
- action применяется к другому snapshot fingerprint;
- unclassified side/opening boundary.

### Warnings

- frame или axis восстановлены по geometry;
- используется synthetic source;
- включена residual policy;
- source result интерполирован по discrete elements;
- часть source contribution имеет неполную provenance, если это явно разрешено
  policy;
- исходная mesh используется только как provenance, а не как fragment mesh.

## Provenance и invalidation

Каждый action сохраняет provenance до:

- parent model/member/element/node или template field;
- result/load case/stage/step;
- source local frame и conversion;
- reference point и moment transfer;
- interface и mesh mapping;
- balance-check values и tolerances.

Fingerprint fragment/action pipeline включает:

```text
source topology + source result scenario
+ PlanarRegion / clipped geometry
+ FragmentFrame / cut planes / boundary roles
+ PlanarConnections / constraint objects
+ BoundaryActionSet source mode and policy
+ parent result or template fingerprint
+ units/sign conventions
+ mesh snapshot fingerprint
+ mapper/adapter version
```

Изменение любого входа инвалидирует derived `BoundaryActionSet`, mesh mapping и
solver-ready application. Старые snapshots/results сохраняются для диагностики
и provenance, но не выбираются автоматически.

## Тестовая стратегия

### CScore.Tests

- нормализация interface direction и знака force action;
- моментный перенос `Melement + r × F`;
- coverage force/kinematic/preserve-support по DOF;
- source modes `parent`, `template`, `combined`;
- overlapping DOF и запрет force+kinematic;
- interpolation samples и монотонность `s`;
- fingerprint/invalidation;
- contribution provenance.

### OpenCS.Gmsh.Tests

- cut line в `.geo` как boundary/physical group;
- MSH mapping в непрерывную ordered chain;
- reverse orientation без изменения физического знака;
- incomplete coverage, hole на cut и stale snapshot;
- force mapping на T3/Q4/mixed snapshot с сохранением силы и момента.

### OpenCS.OpenSees.Tests

- force action попадает в shell model только как nodal load;
- ненулевой prescribed DOF генерируется как `sp`, а не `fix`;
- fixed/prescribed conflict блокируется до процесса;
- реальный OpenSees patch вертикальной стены для force subcase;
- отдельный реальный patch для kinematic subcase с проверкой displacement/reaction;
- `combined` с disjoint DOF проходит, overlapping source блокируется.

### CSfea.Tests

- force mapping собирает полный nodal vector с сохранением баланса;
- `uFixed` корректно передаётся в linear/nonlinear Dirichlet solver;
- нерасчётный mapping не допускается до solver.

Тесты должны проверять физические значения, знаки, реакции и баланс, а не только
отсутствие исключения или успешный exit code внешнего процесса.

## Контрольная точка

Переход к следующему fragment orchestration-срезу разрешён после того, как:

1. synthetic parent и template дают идентичный нормализованный результат при
   одинаковых inputs;
2. disjoint `combined` mode сохраняет coverage без двойного учёта;
3. overlapping mode и неполный mapping блокируются;
4. force mapping на новой Gmsh mesh сохраняет силу и момент;
5. kinematic mapping даёт prescribed DOF, не fixed zero;
6. OpenSees и CSfea adapters принимают один boundary contract;
7. provenance позволяет восстановить источник каждого mapped action;
8. изменение source result, template, geometry или mesh invalidates derived data.

После этой точки отдельный срез может добавить extraction vertical wall,
persistence/UI и nonlinear production run, начиная с одной стены без floor
junction.
