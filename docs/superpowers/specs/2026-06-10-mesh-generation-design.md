# Mesh Generation for MaterialArea — Design Spec
**Date:** 2026-06-10  
**Scope:** деление MaterialArea на фибры (три метода), отображение в превью, сохранение в БД

---

## 1. Контекст

`MaterialArea` — самостоятельная область материала поперечного сечения. Уже хранится в таблице `material_areas`, уже имеет геометрию (Hull + Holes в WKT). Фибры (конечные элементы) нужны для расчёта методом фиброй модели.

Существующие методы в `MaterialArea`:
- `SliceXY(nx, ny)` — ортогональная сетка через Сазерленд–Ходжман. Фибры типа `poly`, вершин может быть произвольное количество.
- `Triangulate(maxTrgArea, maxAngl)` — триангуляция через `Geo.Triangulation()`. Поддерживает два метода: `Ruppert` (CDT + рефайнмент) и `AdvancingFront`. Фибры типа `tri`.

Геометрия фибры хранится в поле `Fiber.WKT` (WKT-строка полигона). Центроид и площадь вычисляются из WKT через `WktHelper`.

---

## 2. База данных

### 2.1 Новая таблица `mesh_fibers` (schema v4)

```sql
CREATE TABLE mesh_fibers (
    id       INTEGER PRIMARY KEY AUTOINCREMENT,
    area_id  INTEGER NOT NULL REFERENCES material_areas(id) ON DELETE CASCADE,
    type     TEXT NOT NULL DEFAULT 'poly',   -- 'poly' | 'tri'
    x        REAL NOT NULL DEFAULT 0,        -- центроид X
    y        REAL NOT NULL DEFAULT 0,        -- центроид Y
    area     REAL NOT NULL DEFAULT 0,        -- площадь фибры
    wkt      TEXT,                           -- полигон в WKT (произвольное число вершин)
    eps_p    REAL NOT NULL DEFAULT 0
);
```

Полигон хранится как WKT-строка (`wkt`), а не набором фиксированных столбцов — потому что при ортогональной нарезке Сазерленда–Ходжмана число вершин клетки непредсказуемо.

### 2.2 Новые столбцы в `material_areas` (миграция v4)

```sql
ALTER TABLE material_areas ADD COLUMN mesh_method    TEXT NOT NULL DEFAULT 'grid';
ALTER TABLE material_areas ADD COLUMN mesh_max_area  REAL NOT NULL DEFAULT 0.01;
ALTER TABLE material_areas ADD COLUMN mesh_min_angle REAL NOT NULL DEFAULT 30.0;
```

Поля `nx` (NX) и `ny` (NY) уже существуют — переиспользуются для сетки.

### 2.3 DatabaseService

Новые методы:
- `SaveMeshFibers(MaterialArea area)` — DELETE + batch INSERT в `mesh_fibers`
- `LoadMeshFibersForAreas(IEnumerable<MaterialArea> areas, SqliteConnection conn)` — аналогично `LoadPointFibersForAreas`
- `SaveMaterialArea` и `LoadMaterialAreas` расширяются для чтения/записи `mesh_method`, `mesh_max_area`, `mesh_min_angle`

---

## 3. UI — диалог `MeshDialog`

Отдельный `Window` (~640×420) открывается кнопкой **«Настроить...»** на правой панели `MaterialAreaPage`.

### 3.1 Макет диалога

```
┌──────────────────────────────────────────────────────────┐
│ Метод: [Ортогональная сетка ▼]                           │
├─ Параметры (250px) ──────┬─ Предпросмотр (*) ────────────┤
│                          │                               │
│ (Grid)  NX: [21]         │                               │
│         NY: [21]         │     PlotCanvas                │
│                          │   (hull + фибры)              │
│ (Tri)   Area: [0.01]     │                               │
│         Angle: [30°]     │                               │
│                          │                               │
├──────────────────────────┴───────────────────────────────┤
│  Фибр: 1 234      [Разбить]    [Применить]   [Отмена]   │
└──────────────────────────────────────────────────────────┘
```

- Параметры переключаются через `Visibility` в зависимости от выбранного метода.
- **«Разбить»** — генерирует фибры в памяти (`area.Fibers` из рабочей копии), обновляет превью и счётчик.
- **«Применить»** — записывает сгенерированные фибры в оригинальный `area.Fibers`, вызывает `SaveMeshFibers(area)`, закрывает диалог.
- **«Отмена»** — откатывает `area.Fibers` к снимку (сохранённому при открытии диалога), закрывает диалог.

### 3.2 `MeshDialogVM`

| Свойство | Тип | Назначение |
|---|---|---|
| `MeshMethod` | `MeshMethod` enum | Grid / Ruppert / AdvancingFront |
| `NX`, `NY` | `int` | параметры сетки |
| `MaxArea` | `double` | макс. площадь треугольника |
| `MinAngle` | `double` | мин. угол Рупперта |
| `IsGrid` | `bool` | `Visibility` для Grid-параметров |
| `IsTriangulation` | `bool` | `Visibility` для Tri-параметров |
| `PlotElements` | `IReadOnlyList<PlotElement>` | элементы превью |
| `FibersCount` | `int` | счётчик фибр |
| `GenerateCommand` | `ICommand` | запуск разбиения |
| `ApplyCommand` | `ICommand` | применить + сохранить |
| `CancelCommand` | `ICommand` | отмена |

`MeshDialogVM` принимает `MaterialArea` и `AppViewModel` через конструктор.  
При открытии делает снимок: `_backup = area.Fibers.ToList()`.

---

## 4. Правая панель MaterialAreaPage

Группа «Сетка» заменяет существующую группу «MeshGrid» (NX/NY там остаются в диалоге):

```xaml
<GroupBox Header="Сетка">
  <StackPanel>
    <!-- Переключатель отображения -->
    <ComboBox SelectedItem="{Binding FiberDisplayMode}" ... />

    <!-- Счётчик -->
    <TextBlock Text="{Binding FibersCount, StringFormat='Фибр: {0}'}" />

    <!-- Кнопки -->
    <StackPanel Orientation="Horizontal">
      <Button Content="Настроить..." Command="{Binding OpenMeshDialogCommand}" />
      <Button Content="Очистить"   Command="{Binding ClearMeshCommand}" />
    </StackPanel>
  </StackPanel>
</GroupBox>
```

### Добавления в `MaterialAreaVM`

| Элемент | Назначение |
|---|---|
| `FiberDisplayMode` | enum `FiberDisplayMode` { Centroids, Elements } |
| `FibersCount` | `int` — число poly/tri фибр |
| `OpenMeshDialogCommand` | открывает `MeshDialog` |
| `ClearMeshCommand` | очищает `area.Fibers` (poly/tri), вызывает `SaveMeshFibers` |
| `RefreshPlot()` расширение | рисует `FiberMeshElement` поверх hull/holes |

---

## 5. Новый `FiberMeshElement` в `PlotElement.cs`

Единый элемент для рендеринга всей сетки за один проход через `StreamGeometry` (эффективно при 10k+ фибр):

```csharp
public record FiberMeshElement : PlotElement
{
    public Fiber[] Fibers     { get; init; } = [];
    public bool ShowCentroids { get; init; } = false; // true = точки, false = полигоны
    public Brush Fill         { get; init; } = Brushes.LightSteelBlue;
    public Brush Stroke       { get; init; } = Brushes.SteelBlue;
    public double StrokeThickness { get; init; } = 0.5;
    public double MarkerSize  { get; init; } = 3;
}
```

- `ShowCentroids = false` — каждая фибра рисуется как полигон из `WKT` одним `StreamGeometry`
- `ShowCentroids = true` — каждая фибра рисуется как точка-маркер (центроид X, Y)

---

## 6. Локализация

Добавить ключи в `Strings.ru-RU.xaml` и `Strings.en-US.xaml`:

| Ключ | RU | EN |
|---|---|---|
| `MeshGrid` | Сетка | Mesh |
| `MeshMethod` | Метод | Method |
| `MeshMethod_Grid` | Ортогональная сетка | Orthogonal Grid |
| `MeshMethod_Ruppert` | Триангуляция Рупперта | Ruppert Triangulation |
| `MeshMethod_AdvancingFront` | Фронтальная триангуляция | Advancing Front |
| `MeshGenerate` | Разбить | Generate |
| `MeshApply` | Применить | Apply |
| `MeshClear` | Очистить сетку | Clear Mesh |
| `MeshConfigure` | Настроить... | Configure... |
| `MeshFibersCount` | Фибр: {0} | Fibers: {0} |
| `MeshDisplayMode` | Отображение | Display |
| `MeshDisplayCentroids` | Центроиды | Centroids |
| `MeshDisplayElements` | Элементы | Elements |
| `MeshMaxArea` | Макс. площадь | Max area |
| `MeshMinAngle` | Мин. угол, ° | Min angle, ° |

---

## 7. Порядок изменений (файлы)

1. `CScore/Fiber.cs` — без изменений
2. `OpenCS/Views/PlotElement.cs` — добавить `FiberMeshElement`
3. `OpenCS/Views/PlotCanvas.cs` — добавить рендер `FiberMeshElement`
4. `OpenCS/Utilites/DatabaseService.cs` — миграция v4, `SaveMeshFibers`, `LoadMeshFibersForAreas`, обновить `SaveMaterialArea`/`LoadMaterialAreas`
5. `OpenCS/ViewModels/MeshDialogVM.cs` — новый файл
6. `OpenCS/Views/MeshDialog.xaml` + `MeshDialog.xaml.cs` — новый Window
7. `OpenCS/ViewModels/MaterialAreaVM.cs` — `FiberDisplayMode`, `FibersCount`, команды, `RefreshPlot`
8. `OpenCS/Views/MaterialAreaPage.xaml` — обновить группу «Сетка»
9. `OpenCS/Views/MaterialAreaPage.xaml.cs` — обновить `UpdatePlot`
10. `OpenCS/Resources/Strings.ru-RU.xaml` + `Strings.en-US.xaml` — новые ключи
