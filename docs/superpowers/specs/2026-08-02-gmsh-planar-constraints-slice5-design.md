# Gmsh PlanarConstraintObject и АЖТ footprints: срез 5

**Дата:** 2026-08-02
**Статус:** согласованный дизайн

## Назначение

Добавить во внутреннюю геометрию одного `PlanarRegion` точные mesh-loci для
точек, линий и областей, которые должны присутствовать в Gmsh-сетке. Срез
закрывает геометрический контракт для АЖТ, embedded-объектов и mesh-only
разбиений, но не применяет structural constraints к solver.

После среза путь должен выглядеть так:

```text
PlanarRegion + PlanarConstraintObjects
  -> validation
  -> deterministic .geo
  -> gmsh.exe, MSH 4.1 ASCII
  -> nodes/elements/entities
  -> ConstraintMeshMappings
  -> PlanarMeshSnapshot
```

`PlanarRegion` остаётся источником инженерской геометрии. Gmsh-сетка,
physical/entity tags и mappings являются производными данными.

## Границы среза

### Входит

- `PlanarConstraintObject` в `CScore.Planar`;
- независимые `StructuralFacet` и `MeshFacet`;
- точечные, полилинейные и полигональные локусы в координатах `U/V` региона;
- `embedded_point`, `embedded_curve`, `embedded_region` и
  `conforming_partition`;
- генерация внутренней геометрии в deterministic `.geo`;
- переход основного Gmsh pipeline на MSH 4.1 ASCII;
- чтение `$PhysicalNames`, `$Entities`, node blocks и element blocks;
- entity/physical provenance для host boundary и constraint-объектов;
- `ConstraintMeshMapping` для point, curve и region;
- валидация host-региона, отверстий, конфликтов и полноты mapping;
- fingerprint constraints и инвалидация производных snapshots;
- SQLite round-trip constraint-объектов и mappings, миграция схемы v46 -> v47;
- unit, parser, mapping, persistence и реальный Gmsh integration tests.

### Не входит

- WPF-редактор constraint-объектов;
- `RigidTransferDomain` и импорт нормализованной АЖТ из ЛИРА/SCAD;
- `equalDOF`, `rigidLink`, MPC, supports и solver-level equations;
- `PlanarConnection` и общая mesh нескольких `PlanarRegion`;
- production fragment workflow;
- элементы второго порядка, бинарный MSH, объёмная mesh;
- mortar mapping, поиск ближайших узлов как основной метод и автоматическое
  разрешение пересечений.

## Архитектура

`PlanarRegion` получает коллекцию:

```text
PlanarRegion
  ├─ Contours: Hull + Holes
  ├─ BoundarySegments
  ├─ ConstraintObjects
  └─ derived PlanarMeshSnapshot
```

Constraint владеет локальной геометрией и двумя независимыми семантическими
гранями:

```text
PlanarConstraintObject
  ├─ Id: string
  ├─ Tag
  ├─ Geometry2D
  ├─ StructuralFacet
  ├─ MeshFacet
  ├─ MasterReference?
  ├─ DofMask
  ├─ StructuralFrame?
  ├─ Tolerance
  └─ Source/Provenance
```

`Id` уникален внутри `PlanarRegion` и используется только как стабильный
логический ключ provenance. Он не совпадает с Gmsh tag, OpenCS ID или OpenSees
tag.

Новый constraint передаётся через существующий `PlanarMeshingRequest` вместе с
`PlanarRegion`; отдельный второй источник геометрии не вводится. Реализация
расширяет `PlanarRegion` и его fingerprint, а не создаёт параллельную модель
геометрии.

## Доменный контракт

### Geometry2D

В первой версии поддерживаются только линейные локальные геометрии:

```text
Point  = одна PlanarPoint2D(U, V)
Curve  = упорядоченная polyline из минимум двух PlanarPoint2D
Region = простой замкнутый polygon из минимум трёх PlanarPoint2D
```

Дуги, сплайны и внутренние отверстия constraint-region не входят в срез. Локус
может касаться внешней границы endpoint-ом; пересечение с host boundary без
общего endpoint запрещено. Локус внутри hole запрещён.

### StructuralFacet

Поддерживаются значения:

```text
None
RigidBody
Tie
EmbeddedMember
PointMpc
Support
Symmetry
```

Правила валидации:

- `None` не требует master reference и DOF mask;
- `RigidBody`, `PointMpc` и `EmbeddedMember` требуют непустой
  `MasterReference`;
- `Support` и `Symmetry` требуют непустой `DofMask`;
- `Tie` может быть локальным либо ссылаться на master reference;
- заданный `StructuralFrame` должен быть валидным ортонормированным
  `Frame3D`;
- facet описывает будущий structural action, но в этом срезе не генерирует
  команды solver.

`MasterReference` хранит provider и логический ключ внешнего master-объекта.
Это минимальный contract для будущего `RigidTransferDomain`, а не его
реализация.

### MeshFacet

Поддерживаются значения:

```text
None
EmbeddedPoint
EmbeddedCurve
EmbeddedRegion
ConformingPartition
```

Совместимость с геометрией:

| Geometry2D | Допустимый MeshFacet |
|---|---|
| Point | `None`, `EmbeddedPoint` |
| Curve | `None`, `EmbeddedCurve`, `ConformingPartition` |
| Region | `None`, `EmbeddedRegion`, `ConformingPartition` |

Для нового Gmsh snapshot structural facet без mesh facet считается неполным и
делает snapshot нерасчётным. Mesh-only объект допускается, но отсутствие
однозначного запрошенного mesh mapping также блокирует snapshot: внутреннюю
геометрию нельзя silently игнорировать.

`ConformingPartition` означает, что локус становится частью топологии shell
mesh. Для region это означает неперекрывающиеся подповерхности без удаления
host material. `EmbeddedRegion` также сохраняет материал host-региона, но не
требует structural partition contract; его element set определяется через
связанные Gmsh entities и геометрическую проверку.

## Подготовка и валидация

До запуска Gmsh выполняется отдельный constraint validation pass:

- каждый constraint имеет непустой уникальный `Id`;
- все координаты и допуски конечны;
- точки, кривые и области не вырождены;
- curve и region не имеют самопересечений;
- локус лежит внутри host hull либо на разрешённой внешней границе;
- локус не попадает в отверстие и не пересекает его без допустимого endpoint;
- несовместимые пересечения или вложенность constraint-объектов диагностируются;
- structural facets, конфликтующие на одной геометрии или DOF, блокируют запуск;
- `ConformingPartition` требует пригодного deterministic partition plan;
- geometry и mesh facet совместимы по таблице контракта.

Автоматическое разрешение пересечений, вложенных rigid zones и конфликтующих DOF
не выполняется. Ошибка подготовки возвращается как диагностический
нерасчётный результат без запуска внешнего процесса.

После чтения MSH дополнительно проверяются принадлежность узлов плоскости,
host-региону и отсутствие новых отверстий или disconnected islands.

## MSH 4.1 pipeline

Основной `GmshPlanarMesher` генерирует MSH 4.1 ASCII. Поддерживаются разделы:

- `$MeshFormat` с ASCII flag и версией 4.1;
- `$PhysicalNames`;
- `$Entities`;
- `$Nodes` с node blocks;
- `$Elements` с element blocks.

Поддерживаются только линейные типы:

| MSH type | Значение |
|---:|---|
| 15 | point |
| 1 | line |
| 2 | triangle |
| 3 | quadrangle |

Элементы второго порядка, объёмные элементы, binary MSH и неизвестные типы
создают blocking diagnostic.

Generator использует deterministic logical physical groups:

```text
host boundary groups
host surface group
constraint:<id>:point
constraint:<id>:curve
constraint:<id>:region
```

Числовые physical и entity tags выделяются отдельным allocator-ом и живут
только в артефакте запуска. `$Entities` связывает entity с physical group, а
manifest и physical names связывают group с логическим constraint ID.

Для `ConformingPartition` generator строит внутреннюю топологию так, чтобы
границы constraint были рёбрами элементов, поверхности не перекрывались, а
совпадающие nodes не дублировались. Конкретные Gmsh CAD-команды являются
деталью generator-а; доменный контракт зависит от результата, а не от порядка
Gmsh entities.

MSH reader уплотняет raw node/element IDs в существующие dense indices
`PlanarMeshNode.Index` и `PlanarMeshElement.Index`. Порядок узлов T3/Q4 после
импорта нормализуется в сторону `Frame3D.LocalZ`, как в текущем pipeline.

## Snapshot и mapping

`PlanarMeshSnapshot` сохраняет существующие nodes, elements и boundary mappings
и получает:

```text
MeshFormatVersion
ConstraintMappings
EntityProvenance
```

`EntityProvenance` содержит logical constraint ID, dimension, entity tag,
physical group и physical name.

`ConstraintMeshMapping` содержит:

```text
ConstraintObjectId
PointNodeIndices
OrderedCurveEdges
CurveElementIndices
RegionNodeIndices
RegionElementIndices
EntityProvenance
Diagnostics
```

Правила:

- `EmbeddedPoint` сопоставляется ровно с одним node в пределах tolerance;
- «ближайший node» за пределами tolerance не принимается;
- curve сопоставляется с непрерывной упорядоченной цепочкой уникальных edges;
- цепочка проверяется по endpoints, длине, связности и отсутствию ветвлений;
- порядок цепочки определяется координатной связностью, не Gmsh tags;
- region получает node/element set по physical/entity provenance и геометрической
  проверке;
- boundary nodes региона могут одновременно входить в host boundary mapping;
- внутренний region не становится отверстием и не удаляет host material;
- для `ConformingPartition` каждый locus edge должен совпадать с edge элементов,
  а adjacent sub-surfaces не должны перекрываться;
- неизвестный entity, несовпадающий physical group, неполное покрытие или
  неоднозначность делают snapshot нерасчётным.

Существующие shell adapters продолжают использовать dense node/element indices.
Constraint mapping не создаёт OpenSees tags и не меняет topology adapters до
отдельного structural среза.

## Persistence и invalidation

Схема SQLite повышается с v46 до v47:

- в `planar_regions` добавляется `constraint_objects_json` с default `[]`;
- в `planar_mesh_snapshots` добавляются `mesh_format_version` и
  `entity_provenance_json`;
- создаётся `planar_mesh_constraint_mappings` с отдельной записью на constraint;
- mappings хранят point nodes, curve edges/elements, region nodes/elements,
  entity provenance и diagnostics в JSON columns;
- старые `planar_regions` и snapshots читаются без constraints;
- `fem_mesh_*` и импортированные LIRA/SCAD meshes не изменяются.

JSON constraints использует явные discriminators `GeometryKind`,
`StructuralKind` и `MeshKind`. Round-trip не зависит от полиморфного serializer
magic.

`PlanarGeometryFingerprint` включает canonical geometry constraint-объектов,
structural/mesh facets, master reference, DOF mask, tolerance и logical IDs.
`PlanarMeshFingerprint` по-прежнему добавляет settings, фактическую версию Gmsh
и generator version. Любое изменение этих данных помечает старый snapshot
устаревшим, но не удаляет его и не запускает remesh автоматически.

## Ошибки и расчётность

- validation error до процесса не создаёт сомнительную mesh;
- timeout, missing executable, non-zero exit code и parser error сохраняют
  operation artifacts и diagnostic snapshot;
- неизвестные MSH types/entity tags не пропускаются молча;
- неполный mapping любого требуемого mesh facet блокирует `IsCalculable`;
- structural object без mesh mapping блокирует snapshot;
- mesh-only object с неполной семантикой может получить warning, но потеря
  запрошенной геометрии является error;
- solver adapters не получают snapshot с blocking diagnostics;
- все diagnostics сохраняются вместе со snapshot и operation manifest.

## Проверки

### Unit и contract tests

- domain validation для point/curve/region и facet compatibility;
- fingerprint меняется при изменении geometry, facet, master reference, DOF mask
  или tolerance;
- MSH 4.1 header, physical names, entities, node blocks и element blocks;
- dense index mapping, T3/Q4 orientation и unsupported element diagnostics;
- entity/physical provenance;
- exact embedded point и rejection of out-of-tolerance nearest node;
- ordered curve chain, gaps, branches, wrong endpoints и incomplete coverage;
- region mapping, preserved material и hole rejection;
- overlapping/nested constraint diagnostics;
- JSON round-trip `PlanarRegion` с constraints;
- SQLite round-trip snapshot с constraint mappings.

### Integration tests

Реальный `gmsh.exe` строит один host-регион с отверстием и проверяет:

1. embedded point;
2. embedded curve;
3. internal rigid-zone polygon с `ConformingPartition`;
4. mesh-only internal partition без structural facet;
5. сохранение host hole и material;
6. уникальность nodes на совпадающих фрагментах;
7. deterministic physical/entity provenance;
8. сохранение `.geo`, `.msh`, manifest и logs.

Регрессии перед завершением среза:

- `dotnet build OpenCS.sln --no-restore`;
- `dotnet test CScore.Tests/CScore.Tests.csproj --no-build --no-restore`;
- `dotnet test OpenCS.Gmsh.Tests/OpenCS.Gmsh.Tests.csproj --no-build --no-restore`;
- `dotnet test OpenCS.OpenSees.Tests/OpenCS.OpenSees.Tests.csproj --no-build --no-restore`;
- существующие pre-existing flaky SQLite cleanup tests фиксируются отдельно от
  результатов среза и не маскируются изменением допуска.

## Контрольная точка

Один `PlanarRegion` с Hull и Hole, содержащий точку, внутреннюю линию и polygon
АЖТ, получает воспроизводимый MSH 4.1 snapshot. Каждому locus соответствует
однозначный node/edge/element mapping с entity provenance, host material и
отверстие сохранены, snapshot переживает SQLite round-trip, а solver-level
constraints остаются явно отложенными до следующего среза.
