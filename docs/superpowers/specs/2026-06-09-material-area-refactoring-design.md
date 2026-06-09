# Дизайн: рефакторинг материальных областей (MaterialArea + CrossSection)

**Дата:** 2026-06-09  
**Статус:** утверждён

---

## Мотивация

Текущая архитектура (`Region → FiberRegion → RCFiberRegion` + вложенный `ReBarGroup`) запутана:

- `FiberRegion` и `RCFiberRegion` — почти одинаковые классы, существуют в двух отдельных коллекциях
- `ReBarGroup` вложен внутрь `RCFiberRegion`, а не является равноправной областью
- Алгоритм приведения к однородному сечению (вычитание + прибавление арматуры) зарыт в `RCFiberRegion.Integral()`
- `Material` тащится в расчёт, хотя в нём нужны только `Diagramms`
- Полиморфизм сломан: часть методов `Integral()` используют `new` вместо `override`

---

## Решение: Подход А — полная замена

Вводится единая концепция **материальной области** (`MaterialArea`) и контейнера **поперечного сечения** (`CrossSection`). Арматура становится равноправной областью. Алгоритм приведения прячется в **дифференциальную диаграмму**.

---

## Раздел 1. Базовая модель данных

### `Fiber` — унифицированный элемент дискретизации

Существующий класс `Fiber` расширяется одним полем:

```csharp
public FiberType Type { get; set; }  // Polygon | Point
```

- `FiberType.Polygon` — волокно из нарезки / триангуляции (существующая логика)
- `FiberType.Point` — точечный элемент (бывший `ReBar`). Добавляются поля: `Diameter`, `Eps_p`, `Nu1`, `Nu2`

Классы `ReBar` и `ReBarLayer` **удаляются**.

---

### `MaterialArea` — единая материальная область

Заменяет `FiberRegion`, `RCFiberRegion`, `ReBarGroup`.

```csharp
public class MaterialArea
{
    public int Id { get; set; }
    public int Num { get; set; }
    public string Tag { get; set; }
    public string? Description { get; set; }

    // Геометрия (null для чисто арматурных областей)
    public List<Contour> Contours { get; set; }
    public string? WKT { get; set; }

    // Параметры дискретизации (для полигональных областей)
    public int NX { get; set; } = 21;
    public int NY { get; set; } = 21;

    // Волокна: List<Fiber> с Type = Polygon или Point
    public List<Fiber> Fibers { get; set; }

    // Диаграммы работы материала (1–4 штуки, по CalcType)
    // Для арматурных областей — дифференциальные (σ_steel − σ_concrete)
    public Dictionary<CalcType, Diagramm> Diagramms { get; set; }

    // Ссылка на материал — для построения диаграмм и отображения в UI
    // В расчёте не участвует напрямую
    [JsonIgnore] public Material? Material { get; set; }
    public int MaterialId { get; set; }

    // Ссылка на бетонную область-носитель (только для арматурных областей)
    // null → используется чистая стальная диаграмма (арматура вне бетона)
    [JsonIgnore] public MaterialArea? HostArea { get; set; }
    public int? HostAreaId { get; set; }

    public DiagrammType DiagrammType { get; set; }
}
```

**Тип области** (`MatType`) выводится из `Material.Type` — отдельного enum не добавляется.

**Ограничение:** минимум одна `MaterialArea` должна быть в любом `CrossSection`. Допустимые конфигурации:
- только бетонные области (без арматуры)
- только стальные / арматурные области (без бетона)
- любая комбинация

---

### Фабричный метод для арматурной области

```csharp
public static MaterialArea CreateRebarArea(
    IEnumerable<Fiber> bars,
    Material steelMaterial,
    DiagrammType steelDiagrammType,
    MaterialArea? hostConcreteArea)   // null → нет бетонного носителя
{
    var area = new MaterialArea { ... };
    area.Fibers = bars.ToList();       // все с Type = Point
    area.Material = steelMaterial;
    area.HostArea = hostConcreteArea;

    var steelDgr = steelMaterial.GetDiagramms(steelDiagrammType);

    if (hostConcreteArea != null)
    {
        // Дифференциальные диаграммы: σ_steel(ε) − σ_concrete(ε)
        area.Diagramms = new Dictionary<CalcType, Diagramm>();
        foreach (var ct in steelDgr.Keys)
            area.Diagramms[ct] = Diagramm.Differential(steelDgr[ct],
                                                        hostConcreteArea.Diagramms[ct]);
    }
    else
    {
        area.Diagramms = steelDgr;    // чистые стальные диаграммы
    }

    return area;
}
```

---

## Раздел 2. Дифференциальные диаграммы

### Принцип

Бетонная область моделируется **брутто** (волокна покрывают всё сечение, включая зону расположения арматуры).

Арматурная `MaterialArea` использует разностную диаграмму:

```
σ_eff(ε) = σ_сталь(ε) − σ_бетон_носителя(ε)
```

В результате `Integral()` для **всех** областей одинаков — простая сумма без специальной логики.

### `Diagramm.Differential`

Новый статический метод в классе `Diagramm`:

```csharp
public static Diagramm Differential(Diagramm steel, Diagramm concrete)
```

Создаёт `Diagramm` с обёртками `DifferentialSpline` над `ISpline`:

```csharp
// DifferentialSpline в CSmath
double f(double eps)  => a.f(eps)  - b.f(eps);
double df(double eps) => a.df(eps) - b.df(eps);
```

`ISpline` в `CSmath` не меняется — добавляется только новый класс-реализация.

### Флаги `ten` / `ca`

Работают корректно автоматически:
- Если бетон не работает на растяжение (`ten=false`): `σ_concrete = 0` при `ε > 0` → `σ_eff = σ_steel`. Правильно.
- Если арматура не работает на сжатие (`ca=false`): `σ_steel = 0` при `ε < 0` → `σ_eff = −σ_concrete`. Правильно: вычитаем двойной учёт бетона без вклада арматуры.

### Хранение

Дифференциальные диаграммы **не сохраняются в БД** — пересчитываются при `ResolveReferences()` после загрузки, аналогично текущему `SetMaterial()`.

---

## Раздел 3. Иерархия сечений

### `CrossSection`

```csharp
public class CrossSection
{
    public int Id { get; set; }
    public int Num { get; set; }
    public string Tag { get; set; }
    public string? Description { get; set; }

    // Минимум 1 область
    public List<MaterialArea> Areas { get; set; }

    public virtual Load Integral(Kurvature k, CalcType calc = CalcType.C,
                                  bool ten = true, bool ca = true)
    {
        double N = 0, Mx = 0, My = 0;
        foreach (var area in Areas)
        {
            area.SetEps(k, calc, ten, ca);
            foreach (var f in area.Fibers)
            { N += f.N; Mx += f.My; My += f.Mz; }
        }
        return new Load { Calc = calc, N = N, My = Mx, Mz = My };
    }
}
```

### `TwoStageSection : CrossSection`

Предназначен для сборно-монолитных сечений и сечений с усилением.

```csharp
public class TwoStageSection : CrossSection
{
    // Сечение 1-го этапа (до усиления / омоноличивания)
    public CrossSection Stage1 { get; set; }

    // Замороженная кривизна плоскости деформаций от нагрузки 1-го этапа
    public Kurvature Stage1Kurvature { get; set; }

    // Areas (из базового CrossSection) = области 2-го этапа

    public override Load Integral(Kurvature k, CalcType calc = CalcType.C,
                                   bool ten = true, bool ca = true)
    {
        double N = 0, Mx = 0, My = 0;

        // Этап 1: ε_total = ε_текущее + ε_замороженное
        Kurvature k1 = k + Stage1Kurvature;
        foreach (var area in Stage1.Areas)
        {
            area.SetEps(k1, calc, ten, ca);
            foreach (var f in area.Fibers)
            { N += f.N; Mx += f.My; My += f.Mz; }
        }

        // Этап 2: ε_total = ε_текущее
        foreach (var area in Areas)
        {
            area.SetEps(k, calc, ten, ca);
            foreach (var f in area.Fibers)
            { N += f.N; Mx += f.My; My += f.Mz; }
        }

        return new Load { Calc = calc, N = N, My = Mx, Mz = My };
    }
}
```

`Kurvature` получает оператор `+` (сложение `e0`, `ky`, `kz` покомпонентно).

### Автоматическое определение `HostArea`

При добавлении арматурных Point-волокон в `CrossSection`:

1. Для каждого стержня — point-in-polygon против всех бетонных `MaterialArea` сечения
2. Стержни группируются по найденной `HostArea`
3. Если стержень не попадает ни в одну бетонную область — `HostAreaId = null`
4. `HostAreaId` перезаписываем вручную при необходимости
5. Автоматически пересчитывается при изменении геометрии бетонной области

Point-in-polygon реализуется через существующий инструментарий `CSmath` / `WktHelper`.

---

## Раздел 4. Схема базы данных

Без миграции старых данных. Новые таблицы с нуля.

### `cross_sections`
```sql
CREATE TABLE cross_sections (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    num         INTEGER NOT NULL,
    tag         TEXT NOT NULL,
    description TEXT,
    type        TEXT NOT NULL DEFAULT 'simple'  -- 'simple' | 'two_stage'
);
```

### `cross_section_stages` (только для `TwoStageSection`)
```sql
CREATE TABLE cross_section_stages (
    section_id       INTEGER NOT NULL REFERENCES cross_sections(id),
    stage1_section_id INTEGER NOT NULL REFERENCES cross_sections(id),
    e0               REAL NOT NULL DEFAULT 0,
    ky               REAL NOT NULL DEFAULT 0,
    kz               REAL NOT NULL DEFAULT 0
);
```

### `material_areas`
```sql
CREATE TABLE material_areas (
    id             INTEGER PRIMARY KEY AUTOINCREMENT,
    section_id     INTEGER NOT NULL REFERENCES cross_sections(id),
    num            INTEGER NOT NULL,
    tag            TEXT NOT NULL,
    description    TEXT,
    material_id    INTEGER REFERENCES materials(id),
    host_area_id   INTEGER REFERENCES material_areas(id),  -- null для бетона / внешней арматуры
    diagramm_type  TEXT NOT NULL DEFAULT 'L2',             -- 'L2' | 'L3' | 'SP63'
    nx             INTEGER NOT NULL DEFAULT 21,
    ny             INTEGER NOT NULL DEFAULT 21,
    wkt            TEXT                                     -- null для чисто арматурных областей
);
```

### `fibers`

Хранятся только Point-волокна (стержни с фиксированными координатами).  
Polygon-волокна не хранятся — пересчитываются при открытии.

```sql
CREATE TABLE fibers (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    area_id     INTEGER NOT NULL REFERENCES material_areas(id),
    x           REAL NOT NULL,
    y           REAL NOT NULL,
    area        REAL NOT NULL,
    diameter    REAL,
    eps_p       REAL NOT NULL DEFAULT 0
);
```

---

## Раздел 5. VM и UI слой

### AppViewModel

```csharp
// Было:
ObservableCollection<FiberRegion>    FiberRegions
ObservableCollection<RCFiberRegion>  RcFiberRegions
ObservableCollection<ReBarGroup>     RebarGroups

// Стало:
ObservableCollection<CrossSection>   CrossSections
```

`DatabaseService`: `LoadCrossSections()`, `SaveCrossSection()`, `DeleteCrossSection()`.

### Дерево навигации (TreeView)

```
CrossSections
  ├── [🔲] Прямоугольная колонна 400×600        ← CrossSection (simple)
  │     ├── [🟦] Бетон B25                      ← MaterialArea, Concrete
  │     ├── [🟦] Бетон B20 (ядро)               ← MaterialArea, Concrete
  │     ├── [🟠] Арматура A500 нижняя           ← MaterialArea, ReSteelF
  │     └── [🟡] Канаты К1500                   ← MaterialArea, ReSteelU
  │
  └── [🔲] Усиление ригеля                      ← TwoStageSection
        ├── [Этап 1]
        │     ├── [🟦] Бетон B20
        │     └── [🟠] Арматура A400
        └── [Этап 2]
              ├── [🟦] Бетон B25
              └── [🟩] Сталь С255
```

**Цветовая и иконочная схема по `Material.Type`:**

| MatType | Цвет | HEX | Иконка |
|---|---|---|---|
| `Concrete` | синий | `#3B82F6` | залитый квадрат |
| `ReSteelF` | оранжевый | `#F97316` | группа кружков |
| `ReSteelU` | жёлтый | `#EAB308` | группа кружков, пунктир |
| `Steel` | зелёный | `#22C55E` | контурный прямоугольник |

Реализация: `DataTrigger` в XAML по `Material.Type`. Иконки — `Path`-геометрии 16×16 в ресурсах.  
Этапы `TwoStageSection` — группирующие узлы без иконки, заголовок курсивом.

### Страницы

| Было | Стало |
|---|---|
| `RCFiberRegionPage` | `CrossSectionPage` |
| `RCFiberRegionView` | `CrossSectionView` |
| Отдельный `ReBarGroupView` | встроен в `CrossSectionPage` |

### ViewModel

```csharp
public class MaterialAreaVM : ViewModelBase
{
    public MaterialArea Model { get; }
    // команды: AddFibers, AddRebars, RemoveArea, SetDiagrammType, ...
}

public class CrossSectionVM : ViewModelBase
{
    public CrossSection Model { get; }
    public ObservableCollection<MaterialAreaVM> Areas { get; }
}
```

---

## Что удаляется

| Класс | Причина |
|---|---|
| `Region` | геометрия переходит в `MaterialArea` |
| `FiberRegion` | заменяется `MaterialArea` |
| `RCFiberRegion` | заменяется `CrossSection` |
| `ReBarGroup` | заменяется `MaterialArea` с Point-волокнами |
| `ReBar` | поля переходят в `Fiber` |
| `ReBarLayer` | логика слоя переходит в метод `MaterialArea` |

---

## Что остаётся без изменений

- `Material`, `MaterialChars`, `Diagramm`, `DiagrammType`, `CalcType`, `MatType`
- `Fiber` (расширяется, не заменяется)
- `Contour`, `WktHelper`, `GridSplit`, `Geo` (триангуляция)
- `GeoProps`, `Load`, `Kurvature`, `Basis`
- Весь `CSmath` (добавляется только `DifferentialSpline`)
- `CSTriangulation` — без изменений

---

## Бэклог (вне текущего скоупа)

- `TwoStageSection` с вариантом Б: каждый этап имеет свою плоскость деформаций, решение — система связанных задач с совместностью деформаций на стыке
