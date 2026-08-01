# Срез 4: PlanarLoad и boundary contract

**Дата:** 2026-08-01
**Статус:** утверждённый рабочий контракт первого инкремента среза 4

## Цель

Добавить нагрузки, независимые от конкретной Gmsh-сетки, и преобразовывать их в
явные узловые нагрузки и boundary sets сохранённого `PlanarMeshSnapshot`.
Результат должен сохранять равнодействующую и первый момент с контролируемым
допуском и запрещать неявное сопоставление границы или точки.

## Границы этого инкремента

Входят:

- доменная модель `PlanarLoad` для равномерной поверхностной, краевой и точечной
  нагрузки;
- локальная и глобальная система координат;
- явная привязка краевой нагрузки к `PlanarBoundaryKey`;
- маппинг surface load по площади T3/Q4, boundary load по цепочке граничных
  рёбер и point load по точному узлу;
- `PlanarBoundaryContractMapper`, выдающий наборы узлов и рёбер по ролям
  `BoundaryRole`;
- баланс силы и момента в глобальной системе координат до и после дискретизации;
- диагностический нерасчётный результат при отсутствующем, неполном или
  неоднозначном mapping-е;
- преобразование результата в `ShellNodalLoad` для OpenSees и в полный вектор
  нагрузок `CSfea.Core.ShellMesh`.

Не входят:

- автоматические закрепления для ролей `Support`, `Junction` или `Opening`;
- `PlanarConstraintObject`, `PlanarConnection` и cut-interface;
- распределение нагрузки по ближайшему узлу;
- MSH 4.1 и внутренние mesh loci;
- UI и persistence нагрузок.

## Доменная модель

```text
PlanarLoad
  Tag: string
  Kind: Surface | Boundary | Point
  CoordinateSystem: Local | Global
  Fx/Fy/Fz: double
  TargetBoundary: PlanarBoundaryKey?   // только Boundary
  PointU/PointV: double?                // только Point, в local frame региона
  PointToleranceM: double               // только Point, default 1e-9
```

Смысл компонент зависит от `Kind`:

- `Surface`: force per area, Н/м²;
- `Boundary`: force per length, Н/м;
- `Point`: force, Н.

Система `Local` означает компоненты по `Frame3D.LocalX/Y/Z`, а `Global` — по
мировым X/Y/Z. Момент в `PlanarLoad` не задаётся: этот контракт мапит только
транслational shell loads. Вектор допускает отрицательные и нулевые компоненты,
но обязан быть конечным; нулевая нагрузка допустима.

`PlanarBoundaryKey` должен совпадать с одним `PlanarMeshBoundaryMapping` по
значению, а не по позиции в списке. Для `Point` координата сначала переводится
в глобальную систему через `Frame3D`; допускается только один snapshot node с
расстоянием не более `PointToleranceM`. Если совпадений нет или их несколько,
результат содержит error diagnostic и `IsCalculable == false`.

## Boundary contract

`PlanarBoundaryContractMapper` сопоставляет `PlanarRegion.BoundarySegments` и
`PlanarMeshSnapshot.BoundaryMappings`. Для каждой роли формируется:

```text
PlanarBoundarySet
  Role: BoundaryRole
  BoundaryKeys: IReadOnlyList<PlanarBoundaryKey>
  NodeIndices: IReadOnlyList<int>       // unique, в порядке появления mappings
  Edges: IReadOnlyList<(int A, int B)>  // consecutive pairs, без дубликатов
```

Каждый `BoundarySegment` обязан иметь ровно одно mapping. Отсутствующее mapping,
дубликат ключа или неизвестный узел — ошибка. Роли `Unclassified`, `Free`,
`Support`, `BeamJunction`, `WallJunction`, `Opening`, `Load` только маркируют
геометрию. Mapper не создаёт DOF restrictions и не превращает `Support` в
закрепление.

## Дискретизация

Для всех нагрузок ведётся один аккумулятор по `snapshot node Index`. Если на один
узел попало несколько нагрузок, компоненты суммируются.

### Surface

Для T3 используется постоянная нагрузка `traction * area / 3` на каждый узел.
Для Q4 используется bilinear consistent integration в четырёх точках Гаусса;
это сохраняет силу и первый момент для произвольного выпуклого плоского Q4.

### Boundary

Для каждого consecutive edge `(A, B)` из mapping:

```text
fA += traction * length / 2
fB += traction * length / 2
```

Такой mapping использует только реальную цепочку snapshot и не требует узлов,
которых нет на указанном граничном сегменте.

### Point

Вся сила прикладывается к единственному узлу, совпавшему с заданной точкой в
пределах `PointToleranceM`. «Ближайший» узел, если он не попадает в допуск,
не принимается.

## Balance check

Результат содержит:

```text
PlanarLoadResult
  IsCalculable: bool
  Diagnostics: IReadOnlyList<FemValidationDiagnostic>
  NodalLoads: IReadOnlyDictionary<int, PlanarVector3>
  BoundarySets: IReadOnlyList<PlanarBoundarySet>
  AppliedForceGlobal: PlanarVector3
  AppliedMomentAboutOriginGlobal: PlanarVector3
  MappedForceGlobal: PlanarVector3
  MappedMomentAboutOriginGlobal: PlanarVector3
```

Для surface и boundary исходная сила и момент интегрируются аналитически по
элементам/рёбрам; для point — `r × F`. Маппинг считается сбалансированным, если
абсолютная ошибка каждого компонента не превышает
`ForceTolerance + RelativeTolerance * max(1, |reference|)` для силы и
`MomentTolerance + RelativeTolerance * max(1, |reference|)` для момента.
Ошибку баланса нельзя скрывать округлением или silently принимать.

По умолчанию `RelativeTolerance = 1e-9`, абсолютные допуски равны `1e-9` в
соответствующих единицах. Для плохой геометрии, неизвестных узлов, неполного
boundary mapping или несбалансированной нагрузки результат нерасчётный.

## Backend adapters

OpenSees adapter переводит `snapshot node Index` через уже существующий
`NodeIndexToTag` и создаёт `ShellNodalLoad` с компонентами в Н и без моментов.
Неизвестный snapshot node — ошибка, а не пропуск.

CSfea adapter создаёт массив длины `mesh.NDof` и записывает силы в DOF 0..2
узлов; rotational DOF остаются нулевыми. Индекс snapshot node должен совпадать с
индексом узла `ShellMesh`, что является свойством существующего геометрического
адаптера и проверяется явно.

## Проверки

Минимальный набор unit-тестов:

1. local surface load на наклонённом `Frame3D` преобразуется в global и сохраняет
   силу/момент на T3;
2. Q4 surface load сохраняет первый момент;
3. boundary load по цепочке из двух рёбер не дублирует общий узел и сохраняет
   force/moment;
4. point load принимает точный узел, но блокирует отсутствие и неоднозначность;
5. boundary role mapper возвращает явные nodes/edges и блокирует неполное покрытие;
6. суммарные нагрузки на одном узле складываются;
7. OpenSees и CSfea adapters сохраняют node provenance и не меняют компоненты.
