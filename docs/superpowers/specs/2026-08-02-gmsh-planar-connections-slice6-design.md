# Срез 6: PlanarConnection и две независимые сетки

**Дата:** 2026-08-02
**Статус:** утверждено пользователем 2026-08-02
**Область:** solver-независимый contract и mapping интерфейса между двумя `PlanarRegion`

## Цель

Подготовить связи плита-стена, плита-балка и стена-стена, сохраняя модель «один
`PlanarRegion` — один независимый запуск Gmsh». Один пространственный интерфейс должен
быть представлен двумя локальными геометриями, передан в оба Gmsh-run и после remesh
однозначно сопоставлен с ordered mesh chains обеих сторон.

Результат среза — source contract связи, request-local Gmsh loci и
`PlanarConnectionMeshMapping`. Срез не создаёт solver equations и не добавляет UI.

## Границы

Входит:

- `PlanarConnection`, `ConnectionLocus` и `PlanarConnectionGraph` в `CScore.Planar`;
- три mesh mode: `ConformingPartition`, `EmbeddedLocus`, `IndependentMpc`;
- открытые polyline-locus в локальных координатах обеих сторон;
- проверка совпадения двух локальных locus-ов в глобальном 3D;
- request-local derived `PlanarConstraintObject` для каждого региона;
- независимые Gmsh runs с provenance и fingerprints;
- solver-независимый mapping двух snapshot-ов с ordered nodes/edges, ориентацией,
  параметризацией и exact pairs там, где они возможны;
- persistence source connections и mappings, миграция SQLite v48 -> v49;
- blocking diagnostics и unit/real-Gmsh/persistence tests.

Не входит:

- `equalDOF`, `rigidLink`, MPC, springs, Robin conditions и любые другие solver equations;
- общий multi-region CAD input или общий Gmsh mesh;
- shared Gmsh node IDs между регионами;
- UI редактор и автоматическое remesh из UI;
- точечные и площадные connection loci; первый инкремент поддерживает только открытые
  кривые.

## Архитектура

### Domain contract

`ConnectionLocus` содержит `RegionId`, упорядоченные локальные `PlanarPoint2D` и локальный
tag. Список точек является открытой polyline; направление от первой точки к последней
значимо.

`PlanarConnection` содержит положительный integer `Id`, назначенный при сохранении,
`Tag`, два locus-а, mesh mode и единый `MatchingToleranceM`. Несохранённая связь с `Id == 0`
не может запускать meshing workflow; это исключает коллизию request-local IDs. Locus-ы не
содержат ссылок на живые `PlanarRegion`; регионы
разрешаются через IDs при orchestration. Это позволяет сохранять связь отдельно от
конкретных объектов и явно диагностировать удалённые регионы.

`PlanarConnectionGraph` содержит набор connections и выполняет graph-level validation:

- обе стороны существуют в переданном наборе регионов;
- стороны различны;
- connection ID уникален;
- одна и та же пара регионов и один и тот же spatial locus не получают duplicate connections;
- каждый locus валиден как открытая polyline.

### Derived Gmsh loci

Для стороны с `RegionId` создаётся request-local `PlanarConstraintObject` с
детерминированным ID `connection:{connectionId}:region:{regionId}`. Его geometry — `Curve`,
structural facet — `None`, tolerance — `MatchingToleranceM`, provenance содержит
connection ID и region ID.

Mesh facet определяется режимом:

- `ConformingPartition` -> `PlanarMeshKind.ConformingPartition`;
- `EmbeddedLocus` -> `PlanarMeshKind.EmbeddedCurve`;
- `IndependentMpc` -> `PlanarMeshKind.EmbeddedCurve`.

Derived objects объединяются с ручными и FEM-derived constraints только внутри
`PlanarMeshingRequest`. `PlanarRegion.ConstraintObjects` не мутируется, а
`ConstraintSourceFingerprint` включает fingerprint connection contract.

### Компоненты

- `CScore.Planar`: domain records/classes, validation, connection fingerprint и
  `PlanarConnectionMapper`;
- `OpenCS.Gmsh`: тонкий connection orchestration поверх существующего `IPlanarMesher`,
  который строит два request-local набора constraints и запускает два независимых
  `BuildAsync`;
- `GmshPlanarGeoBuilder`, MSH 4.1 reader и существующий constraint mapper остаются
  механизмом physical/entity provenance для каждой стороны;
- `OpenCS`/database layer: persistence v49 без WPF surface;
- `OpenCS.OpenSees.CScore` и `CSfea.CScore` в срезе не изменяются.

## Геометрическая проверка

Для каждой точки локального locus-а вычисляется глобальная точка
`Frame.Origin + Frame.LocalX * U + Frame.LocalY * V`.

Две пространственные piecewise-linear curves считаются одним интерфейсом, если:

- их начала и концы совпадают в пределах `MatchingToleranceM` в прямой или обратной
  ориентации;
- длины отличаются не более чем на тот же допуск;
- каждая вершина каждой polyline имеет расстояние до другой polyline не более допуска;
- ни один locus не имеет нулевых сегментов, самопересечений или нечисловых координат.

Ориентация стороны B сохраняется как `Forward` или `Reverse` относительно канонического
направления стороны A. Это направление не выводится из порядка Gmsh tags.

Каждый локальный derived constraint дополнительно проходит существующий
`PlanarConstraintValidator`: host boundary, holes, self-intersection и совместимость
geometry/mesh facet.

## Mapping snapshots

`PlanarConnectionMeshMapping` хранит:

- connection ID и mesh mode;
- connection fingerprint, IDs и input fingerprints двух snapshots;
- mapping каждой стороны: region ID, constraint ID, ordered node indices и ordered edges;
- ориентацию стороны относительно канонической кривой;
- фактические 3D-координаты и cumulative arclength/нормированный параметр `s` nodes;
- exact node pairs, когда chains имеют однозначное попарное соответствие;
- diagnostics и `IsCalculable`.

`PlanarConnectionMapper` принимает connection, оба текущих `PlanarRegion` (для
local-to-global transform) и оба snapshots:

1. Проверяет, что оба snapshot расчётны, принадлежат ожидающим регионам и проходят
   `PlanarMeshSnapshotValidator`.
2. Находит ровно один constraint mapping по deterministic ID на каждой стороне.
3. Восстанавливает chain только из `OrderedCurveEdges`; nearest-node поиск запрещён.
4. Проверяет отсутствие разрыва, ветвления, цикла и повторных узлов.
5. Нормализует ориентацию по глобальным endpoint coordinates и вычисляет `s`.
6. Применяет mode-specific compatibility checks.

Для `ConformingPartition` требуются одинаковые количества nodes/edges и попарное
совпадение 3D-координат в допуске; только тогда создаются exact node pairs.

Для `EmbeddedLocus` chains могут иметь разную partition. Сохраняются независимые chains,
ориентация и `s`; physical relation не создаётся.

Для `IndependentMpc` сохраняются те же независимые chains и parameterization, чтобы
будущий solver adapter мог выбрать interpolation/MPC policy. В этом срезе weights,
equations и solver tags не создаются.

Любая ошибка возвращает blocking diagnostics и `IsCalculable=false`. Частичный mapping
не передаётся backend-у как расчётный.

## Data flow

1. Orchestration получает connection, regions, settings и исходные constraints обеих
   сторон.
2. `PlanarConnectionGraph` и connection validator выполняют preflight без запуска Gmsh.
3. Для каждого региона строится отдельный `PlanarMeshingRequest` с derived connection
   constraint и composite source fingerprint.
4. `IPlanarMesher.BuildAsync` запускается дважды. Каждый запуск получает собственную
   artifact directory, Gmsh tags и snapshot.
5. Снимки сохраняются независимо; их `ConstraintMappings` содержат connection provenance.
6. `PlanarConnectionMapper` создаёт mapping только через connection ID, fingerprints и
   3D geometry.
7. Mapping сохраняется с конкретной парой snapshots.

Изменение геометрии, frame, connection locus, mode, tolerance, Gmsh version или generator
делает соответствующие snapshots/mapping stale, но не удаляет историю.

## Persistence и provenance

Миграция повышает SQLite schema version с v48 до v49.

Таблица `planar_connections` хранит source contract: integer entity ID, tag, IDs регионов,
две JSON polylines, mode, tolerance и connection fingerprint. Внешние ссылки не должны
оставлять connection с удалённым регионом.

Таблица `planar_connection_mappings` хранит connection ID, snapshot IDs обеих сторон,
connection/snapshot fingerprints, mapping JSON, diagnostics JSON и `is_calculable`. Ключ
mapping определяется connection и конкретной парой snapshots, поэтому несколько версий
mesh могут существовать одновременно.

Mapping актуален только при совпадении connection fingerprint, обоих snapshot fingerprints,
region IDs и mode; иначе он остаётся историческим, но блокируется при использовании.

Request-local connection constraints не записываются в
`planar_regions.constraint_objects_json`. Они восстанавливаются из source connection при
новом build, а provenance внутри snapshot позволяет воспроизвести конкретный Gmsh-run.

## Diagnostics

Минимальный набор blocking codes:

- `planar_connection_id_duplicate`;
- `planar_connection_id_invalid`;
- `planar_connection_region_unknown`;
- `planar_connection_same_region`;
- `planar_connection_locus_invalid`;
- `planar_connection_locus_space_mismatch`;
- `planar_connection_snapshot_not_calculable`;
- `planar_connection_snapshot_region_mismatch`;
- `planar_connection_mapping_missing`;
- `planar_connection_mapping_ambiguous`;
- `planar_connection_chain_invalid`;
- `planar_connection_orientation_ambiguous`;
- `planar_connection_conforming_partition_mismatch`;
- `planar_connection_fingerprint_stale`.

Нельзя молча переходить на nearest node, менять порядок узлов для исправления ошибки,
создавать artificial support или считать несовпадающие chains conforming.

## Testing

### CScore.Tests

- local-to-global преобразование для Identity и наклонных `Frame3D`;
- совпадение locus-ов при обратной ориентации и rejection пространственного mismatch;
- validation graph: duplicate ID, неизвестный/одинаковый регион, плохая polyline;
- детерминированные derived constraint IDs и fingerprints;
- mapper для conforming chains, reverse orientation и blocking mismatch;
- успешные nonmatching chains для embedded/independent modes;
- missing/ambiguous mappings и stale fingerprints;
- persistence model round-trip, если тестовый harness доступен без WPF.

### OpenCS.Gmsh.Tests

- две независимые `.geo`-генерации содержат ожидаемые connection physical/entity names;
- request-local constraints не изменяют source `PlanarRegion`;
- реальный Gmsh строит две сетки для перпендикулярной или наклонной пары регионов,
  пересекающихся по отрезку;
- MSH 4.1 mappings обеих сторон дают calculable connection mapping;
- отверстия, boundary mappings и прежние single-region constraint mappings не ломаются.

### Database tests

- migration v48 -> v49;
- save/load connections и mappings с JSON полями, diagnostics и fingerprints;
- удаление региона не оставляет dangling connections;
- старые snapshots читаются с legacy semantics.

## Criteria of completion

- Две независимые реальные Gmsh-сетки одного spatial interface проходят до
  `PlanarConnectionMeshMapping` без общих Gmsh IDs.
- `conforming_partition` блокируется при mismatch partition, а embedded/independent
  режимы допускают разные chains.
- Source connection, snapshot provenance и mapping восстанавливаются из SQLite.
- Ни один solver adapter и UI не изменён для применения equations.
- Новые тесты зелёные; известная pre-existing SQLite cleanup race отдельно фиксируется в
  результате проверки и не маскируется.
