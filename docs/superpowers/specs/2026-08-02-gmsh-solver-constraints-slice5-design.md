# Срез 5: solver-level structural constraints для Gmsh-derived loci

**Дата:** 2026-08-02
**Статус:** дизайн согласован пользователем 2026-08-02
**Ветка:** `feature/gmsh-solver-constraints-slice5`

## Цель

Применить structural interpretation уже построенных `PlanarConstraintObject` к
нормализованной модели OpenSees. В первую очередь поддерживаются derived
`EmbeddedMember` constraints, полученные из FEM topology. Точная геометрия и
mapping остаются ответственностью Gmsh/CScore, а solver equations создаются
только в OpenSees bridge.

Результат среза: существующая `ShellOpenSeesModel` может быть явно обогащена
`equalDOF` и `rigidLink`, получить provenance каждой связи и быть передана
обычному `ShellTclGenerator`. Невозможная или неоднозначная связь блокирует
расчёт вместо частичной или эвристической выдачи.

## Контекст и ограничения

В репозитории уже существуют:

- `PlanarConstraintObject` с `StructuralFacet`, `StructuralRelations`,
  `SourceReferences` и `PlanarDofMask`;
- `PlanarConstraintMeshMapping` с точным point node, ordered curve edge-chain и
  element/node sets;
- `PlanarMeshSnapshotShellModelAdapter`, который создаёт shell-модель и карту
  snapshot node index → OpenSees node tag;
- `ShellOpenSeesModel.EqualDofConstraints` и `.RigidLinks` с валидацией
  неизвестных узлов и конфликтов slave DOF;
- Tcl-генерация `equalDOF` и `rigidLink`.

Срез не добавляет solver-level equations в `CScore` и не пытается вывести
поддержку CSfea из наличия OpenSees API. `CSfea`, `PlanarConnection`, parent
actions, cut boundaries, UI и SQLite остаются за пределами этого среза.

## Архитектура

Добавляется отдельный адаптер в `OpenCS.OpenSees.CScore`:

```text
PlanarMeshSnapshotShellModelAdapter.Build(...)
    -> PlanarStructuralOpenSeesAdapter.Apply(...)
    -> ShellTclGenerator.Generate(...)
```

Существующий mesh-адаптер не начинает автоматически применять constraints.
Вызывающий код явно передаёт effective набор constraints, в который могут
входить сохранённые и request-local derived объекты. Это сохраняет старое
поведение расчётов без structural mapping и не требует записывать derived
objects в `PlanarRegion`.

### Входы адаптера

Адаптер получает:

1. результат `PlanarMeshSnapshotShellModelAdapter`, содержащий базовую
   `ShellOpenSeesModel` и `NodeIndexToTag`;
2. исходный `PlanarMeshSnapshot` с `ConstraintMappings` и координатами узлов;
3. effective `IReadOnlyList<PlanarConstraintObject>`;
4. `FemSchemaTopology` для source-node coordinates и source-element
   connectivity;
5. явную карту `source FEM node id → OpenSees node tag`;
6. `PlanarOpenSeesConstraintOptions`.

Source FEM nodes и beam elements не создаются адаптером. Если source master tag
не найден в переданной OpenSees-модели, результат становится
нерасчётным. Это не позволяет незаметно создать дополнительную или
несовместимую топологию.

### Выход адаптера

Вводится `PlanarOpenSeesConstraintResult` со следующими семантическими полями:

- `ShellOpenSeesModel? Model` — новая модель через `with`, либо `null` при
  blocking diagnostics;
- `bool IsCalculable`;
- список emitted constraints и их provenance;
- список `FemValidationDiagnostic`.

Входная модель не мутируется. При отсутствии constraints возвращается
эквивалентная модель без добавленных связей.

Provenance emitted relation содержит как минимум constraint ID, structural kind,
source member/node, source OpenSees master tag, host snapshot node/index, host
OpenSees slave tag, DOF и выбранную policy.

## API и политика

`PlanarOpenSeesConstraintOptions` задаёт policy для `EmbeddedMember` и
`RigidBody`:

- `EqualDof`;
- `RigidLinkBar`;
- `RigidLinkBeam`.

Policy не может автоматически расширять или сужать `PlanarDofMask`.

Базовое преобразование structural kinds:

| Structural kind | OpenSees policy |
|---|---|
| `Tie` | `equalDOF` |
| `RigidBody` | явно выбранный `rigidLink bar/beam` |
| `EmbeddedMember` | явно выбранный `equalDOF` или `rigidLink` |
| `PointMpc` | blocking `unsupported_mpc` |
| `Support` | blocking `unsupported_structural_kind` |
| `Symmetry` | blocking `unsupported_structural_kind` |

В этом срезе `equalDOF` и `rigidLink` направлены от source/master к
host-shell/slave:

- master — OpenSees tag source FEM node;
- slave — OpenSees tag узла Gmsh shell-сетки;
- DOF — строгое преобразование `PlanarDofMask` в отсортированные номера 1..6.

`rigidLink bar` разрешён только для `UX/UY/UZ`. `rigidLink beam` разрешён
только для полной маски шести DOF. Несовпадение policy и маски блокирует
результат.

## Correspondence и mapping

Перед генерацией solver relations адаптер строит полный candidate set.

### Point

Для point constraint требуется:

- ровно один `PointNodeIndices`;
- ровно один source FEM node, однозначно связанный с relation;
- source node tag присутствует в OpenSees-модели;
- для policy `EqualDof` координаты source и host совпадают в `ToleranceM`;
- для явно выбранного `RigidLink` offset допускается: source node выбирается
  только по explicit source reference, а не по ближайшей координате.

Точка пересечения source member с плоскостью внутри FEM element не имеет
уникального source node и требует MPC. Она получает blocking diagnostic, а не
связь с ближайшим endpoint.

### Curve

Host chain восстанавливается из упорядоченного `OrderedCurveEdges`, сохраняя
направление mapping. Source chain восстанавливается из connectivity указанных
`SourceElementIds` и `FemSchemaTopology`.

Цепочки принимаются только при:

- одинаковом количестве узлов;
- однозначном соответствии каждого узла;
- для `EqualDof` — совпадении координат в допуске в прямом или обратном порядке;
- для `RigidLink` — сохранении explicit source/host topology correspondence,
  при этом spatial offset допускается.

Проверка не является поиском ближайшего узла. Несколько кандидатов в допуске,
отсутствие кандидата, разная длина chain или пересечение с внутренним FEM
элементом дают `unsupported_mpc` либо специализированную blocking diagnostic.

Нельзя выдать только совпавшие endpoints, если полная curve correspondence не
доказана. Частичное constraint-наложение запрещено.

### Region и несколько relations

`Region` в текущем срезе требует MPC и блокируется. Если один geometry locus
содержит несколько structural relations, каждая relation разрешается отдельно,
но попытка назначить разные masters одному host slave DOF блокирует весь
constraint set. Идентичные relations не должны приводить к дублирующей записи.

## Валидация и обработка ошибок

Адаптер выполняет preflight до изменения модели:

- проверяет наличие constraint mapping для каждого effective constraint;
- проверяет уникальность IDs и допустимость host indices;
- проверяет structural source references и source-node map;
- проверяет конечность и допустимость tolerance;
- проверяет пустую DOF mask;
- проверяет наличие master/slave tags и отличие master от slave;
- проверяет policy-specific DOF restrictions;
- проверяет duplicate/conflicting slave DOF между всеми emitted relations;
- проверяет structural references до передачи модели дальше. Полный
  `ShellOpenSeesModel.Validate()` выполняется на границе
  `ShellTclGenerator.Generate()` после того, как вызывающий код добавил stages.

При первой или последующей blocking diagnostic `Model` равен `null`, а
частично построенные `EqualDofConstraints`/`RigidLinks` наружу не выдаются.
Исходная модель остаётся неизменной. Диагностики должны содержать стабильный
code, constraint/source identifiers и понятное описание причины.

Два уровня защиты сохраняются намеренно:

1. адаптер не выпускает заведомо неполный structural mapping;
2. `ShellOpenSeesModel.Validate()` повторно проверяет node references и
   конфликты после добавления stages, а `ShellTclGenerator` генерирует только
   валидированную модель.

## Тестирование

### Unit-тесты

В `OpenCS.OpenSees.Tests` добавляются проверки:

- point `EmbeddedMember` с policy `EqualDof`;
- point `RigidLinkBar` и `RigidLinkBeam` с корректными масками;
- полная curve correspondence в прямом и обратном порядке;
- mismatch chain, отсутствующий source master, неизвестный host node;
- внутреннее пересечение source element как `unsupported_mpc`;
- `PointMpc`, пустая или несовместимая DOF mask;
- конфликт двух relations на одном slave DOF;
- отсутствие частичной выдачи при ошибке одного constraint-а;
- детерминированный порядок relations и полный provenance;
- regression существующих shell/beam connection fixtures.

### Реальные OpenSees-тесты

Добавляются интеграционные проверки с настоящим `OpenSees.exe`:

- точечная связь с равенством перемещений и глобальным равновесием;
- эксцентричная `rigidLink beam` с проверкой переданного момента;
- генерация Tcl содержит только validated constraints и стабильный порядок.

## Не входит в срез

- автоматический вызов адаптера из WPF lifecycle;
- persistence structural relations в новых таблицах;
- изменение схемы SQLite;
- `PlanarConnection` и две независимые mesh-операции;
- solver-level implementation в CSfea;
- springs, Robin conditions, parent actions и fragment boundary templates;
- общий multi-region CAD input;
- эвристический nearest-node mapping;
- создание source nodes или beam elements внутри адаптера.

## Контрольная точка

Для одного `PlanarMeshSnapshot` и заранее собранной смешанной
`ShellOpenSeesModel` structural constraints применяются отдельным явным
вызовом. Корректные point/curve relations дают детерминированные
`equalDOF`/`rigidLink`, а неоднозначная или неподдержанная relation блокирует
расчёт до запуска OpenSees. Все выданные связи имеют provenance до исходного
constraint и FEM topology.
