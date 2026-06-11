# RebarGroup Editor — Design Spec

**Date:** 2026-06-11  
**Goal:** Заменить `MaterialAreaPage` для категории `RebarGroup` специализированной страницей интерактивного размещения арматурных стержней с тремя стратегиями и двусторонней синхронизацией холст ↔ таблица.

---

## 1. Архитектура и файлы

### Новые файлы

| Файл | Роль |
|------|------|
| `OpenCS/ViewModels/BarItem.cs` | Observable-обёртка одного стержня: X, Y, Diameter (м) / DiameterMm (мм), IsSelected |
| `OpenCS/ViewModels/EdgeItem.cs` | Observable-обёртка ребра линии защ. слоя: Offset (м), Start/End/Normal/HandlePoint |
| `OpenCS/ViewModels/RebarGroupEditorVM.cs` | Главный VM — стратегия, опора, коллекции Bars/Edges, CoverLinePoints, команды |
| `OpenCS/Views/RebarGroupEditorPage.xaml` | 3-колоночная страница (UserControl) |
| `OpenCS/Views/RebarGroupEditorPage.xaml.cs` | Code-behind: wire-up canvas-событий (Mouse*) |
| `OpenCS/Views/RebarGroupCanvas.cs` | Кастомный FrameworkElement: рисует контур, линию защ. слоя, ручки, стержни; Mouse* |

### Изменяемые файлы

| Файл | Изменение |
|------|-----------|
| `OpenCS/AppViewModel.cs` | `NewRebarGroupCommand` → открывает `RebarGroupEditorPage(null, this)`; `CurrentMaterialArea.set` при Category=RebarGroup → открывает `RebarGroupEditorPage(area, this)` |
| `OpenCS/MainWindow.xaml.cs` | `SelectedItemChanged`: `MaterialArea { Category: RebarGroup or SingleBar }` → `RebarGroupEditorPage` вместо `MaterialAreaPage` |
| `OpenCS/Resources/Strings.ru-RU.xaml` + `Strings.en-US.xaml` | Новые ключи |

### Принцип синхронизации

`RebarGroupCanvas` — кастомный `FrameworkElement` с `OnRender` переопределением. VM хранит всё состояние; canvas читает коллекции VM и вызывает команды при мышиных событиях. VM уведомляет через `INotifyPropertyChanged` и `ObservableCollection.CollectionChanged` → canvas вызывает `InvalidateVisual()`. ScottPlot не используется — рисуем WPF-геометриями напрямую.

---

## 2. Модель данных

### BarItem

```csharp
public class BarItem : ViewModelBase
{
    double _x, _y, _d;
    public int    Index      { get; set; }
    public double X          { get => _x; set { _x = value; OnPropertyChanged(); } }
    public double Y          { get => _y; set { _y = value; OnPropertyChanged(); } }
    public double Diameter   { get => _d; set { _d = value; OnPropertyChanged(); OnPropertyChanged(nameof(DiameterMm)); } }
    public double DiameterMm { get => _d * 1000; set { Diameter = value / 1000; } }
    public bool   IsSelected { get; set; }
}
```

### EdgeItem

```csharp
public class EdgeItem : ViewModelBase
{
    public int    Index       { get; set; }
    public double Offset      { get; set; }   // м, ≥ 0
    public XY     Start       { get; init; }  // вершина опорного контура
    public XY     End         { get; init; }
    public XY     Normal      { get; init; }  // единичная внутренняя нормаль
    // HandlePoint = середина(Start,End) + Offset*Normal — вычисляется в VM
}
```

### RebarGroupEditorVM — ключевые свойства

```
// Стратегия
Strategy         : RebarPlacementStrategy  { FromRegion, FromContour, Bare }
AvailableRegions : IReadOnlyList<MaterialArea>   // AreasLive где Category=Region
AvailableContours: IReadOnlyList<Contour>
SelectedRegion   : MaterialArea?
SelectedContour  : Contour?

// Линия защитного слоя
GlobalOffset     : double   (м, default 0.025)
OffsetStep       : double   (м, default 0.001)
Edges            : ObservableCollection<EdgeItem>
CoverLinePoints  : IReadOnlyList<XY>   // пересчитывается при изменении любого Offset

// Стержни
ActiveDiameter   : double   (м)
ActiveDiameterMm : double   (мм) — конвертирующее свойство
Bars             : ObservableCollection<BarItem>
SelectedBar      : BarItem?

// Fill-between
FillMode      : bool
FillCount     : int     (default 1)
FillUseArc    : bool
FillArcRadius : double  (м)

// Сохранение
Tag           : string
EditedArea    : MaterialArea?   // null = новая, иначе редактирование
```

### Команды VM

| Команда | Параметр | Действие |
|---------|----------|----------|
| `ChangeStrategyCommand` | `RebarPlacementStrategy` | переключает стратегию, пересчитывает Edges |
| `SelectReferenceCommand` | — | при изменении опоры — перестраивает Edges из hull опоры |
| `AddBarCommand` | `(double x, double y)` | добавляет BarItem в Bars |
| `MoveBarCommand` | `(BarItem, double x, double y)` | обновляет X/Y |
| `DeleteBarCommand` | `BarItem` | удаляет из Bars |
| `SelectBarCommand` | `BarItem` | выделяет, снимает с остальных |
| `AdjustEdgeCommand` | `(EdgeItem, double delta)` | offset += delta, зажать ≥ 0 |
| `MoveEdgeHandleCommand` | `(EdgeItem, double newOffset)` | прямое задание offset от drag |
| `ResetAllOffsetsCommand` | — | Offset = GlobalOffset для всех Edges |
| `FillBetweenCommand` | `(BarItem b1, BarItem b2)` | вставить FillCount стержней |
| `SaveCommand` | — | сохраняет MaterialArea |
| `CancelCommand` | — | `App.CurrentPage = null` |

---

## 3. Layout страницы

```
┌────────────────┬──────────────────────────┬─────────────────────────┐
│  Левая  180px  │      Холст  *            │    Правая  220px        │
│                │                          │                         │
│ ○ По области   │  [опорный контур серый]  │ ┌── Рёбра ───────────┐  │
│ ○ По контуру   │                          │ │ №  Отступ(м)  ± ±  │  │
│ ○ Свободная    │  [линия защ. слоя синяя] │ │ 1   0.025     + -  │  │
│                │  [ручки ◇ на серединах]  │ │ 2   0.025     + -  │  │
│ Опора:         │                          │ │ 3   0.030     + -  │  │
│ [ComboBox    ] │  ● стержень              │ └────────────────────┘  │
│                │  ● стержень              │                         │
│ Отступ: 0.025м │                          │ ┌── Стержни ──────────┐ │
│ Шаг:    0.001м │                          │ │ №   X     Y    d мм │×│
│ [Сбросить все] │                          │ │ 1  0.10  0.05   32  │×│
│                │                          │ │ 2  0.20  0.05   32  │×│
│ ─ Стержень ──  │                          │ │ 3  0.30  0.05   25  │×│
│ Ø: [32  ] мм   │                          │ └────────────────────┘  │
│                │                          │                         │
│ ─ Заполнить ─  │                          │                         │
│ N: [3]         │                          │                         │
│ ○ По прямой    │                          │                         │
│ ○ По дуге      │                          │                         │
│   R: [0.15] м  │                          │                         │
│ [Режим заполн.]│                          │                         │
│                │                          │                         │
│ ──────────── ─ │                          │                         │
│ Тег: [______]  │                          │                         │
│ [Сохранить]    │                          │                         │
│ [Отмена]       │                          │                         │
└────────────────┴──────────────────────────┴─────────────────────────┘
```

**Правила видимости:**
- `ComboBox` опоры + таблица рёбер + «Сбросить все»: скрыты при стратегии 3
- `ComboBox` заполнен `AreasLive` (стратегия 1) или `ContoursLive` (стратегия 2)
- Поле «R» дуги: скрыто при «По прямой»
- Кнопка «Режим заполнения» подсвечивается при `FillMode=true`

---

## 4. Поведение холста (RebarGroupCanvas)

### Режим Normal (FillMode=false)

| Событие | Условие | Действие |
|---------|---------|----------|
| `MouseLeftButtonDown` на пустом месте | — | `AddBarCommand(x, y)` |
| `MouseLeftButtonDown` на стержне | — | начать drag, `SelectBarCommand(bar)` |
| `MouseMove` при drag стержня | — | snap-check → `MoveBarCommand(bar, x', y')` |
| `MouseLeftButtonUp` | — | завершить drag |
| `MouseLeftButtonDown` на ручке ◇ | — | начать drag ручки |
| `MouseMove` при drag ручки | — | вычислить новый offset = проекция мыши на нормаль − базовая точка → `MoveEdgeHandleCommand` |
| `MouseMove` над ребром | — | highlight ребра, тултип с текущим offset |

### Режим Fill (FillMode=true)

| Шаг | Действие |
|-----|----------|
| Клик на стержень 1 | выделить синим, запомнить как `_fillBar1` |
| Клик на стержень 2 | `FillBetweenCommand(_fillBar1, bar2)`, выйти из FillMode |
| Клик на пустое место | отменить выбор первого стержня |

### Snap к вершинам линии защитного слоя

При drag стержня: если расстояние до ближайшей точки `CoverLinePoints` < `snapThreshold` (5 px → пересчитать в модельные через текущий масштаб) — переместить стержень точно в эту точку.

### FitToView

При загрузке страницы и при смене опорной геометрии: вычислить bounding box `CoverLinePoints` ∪ `Bars` (или, если пусто, заглушку 1×1 м), добавить 10% padding → задать `ScaleTransform` и `TranslateTransform`.

---

## 5. Алгоритмы

### RecomputeCoverLine

Входные данные: опорный контур (CCW, вершины $P_0..P_{n-1}$), массив `Offset[i]`.

```
Для каждого ребра i (P[i] → P[(i+1) % n]):
  e[i] = нормированный вектор ребра
  n[i] = (-e[i].Y, e[i].X)          // левая нормаль = внутренняя для CCW
  // смещённая прямая: P[i] + Offset[i]*n[i] + t*e[i]

Для каждой вершины i:
  Q[i] = пересечение смещённой прямой i и смещённой прямой (i-1+n)%n
  Если прямые параллельны (|cross| < ε): Q[i] = P[i] + Offset[i]*n[i]

CoverLinePoints = Q[0..n-1]
```

### FillBetween — по прямой

```
step = (B2 - B1) / (N + 1)
for k = 1..N:
    new BarItem(B1 + k*step, ActiveDiameter)
```

### FillBetween — по дуге

```
chord = B2 - B1
d = |chord| / 2
// радиус дуги R задан пользователем, R >= d
h = sqrt(R*R - d*d)
mid = (B1 + B2) / 2
perp = нормаль к chord (выбрать сторону — ближе к центру опорного контура)
center = mid + h * perp

angle1 = atan2(B1.Y - center.Y, B1.X - center.X)
angle2 = atan2(B2.Y - center.Y, B2.X - center.X)
// выбрать кратчайшую дугу от angle1 к angle2
for k = 1..N:
    angle = angle1 + k*(angle2 - angle1)/(N+1)
    new BarItem(center + R*(cos(angle), sin(angle)), ActiveDiameter)
```

---

## 6. Сохранение

```csharp
void Save()
{
    var area = EditedArea ?? new MaterialArea();
    area.Tag      = Tag;
    area.Category = AreaCategory.RebarGroup;
    area.HostAreaId = SelectedRegion?.Id;
    area.Fibers.Clear();
    foreach (var b in Bars)
        area.Fibers.Add(Fiber.CreatePoint(b.Diameter, b.X, b.Y));
    App.db.SaveMaterialArea(area);
    if (!App.MaterialAreas.Contains(area))
        App.MaterialAreas.Add(area);
    else
    {
        App.RefreshMaterialAreaLiveCollections();
        App.IsDirty = true;
    }
    App.LogService.Info($"Группа арматуры «{area.Tag}» сохранена");
}
```

---

## 7. Строковые ресурсы (новые ключи)

| Ключ | ru-RU | en-US |
|------|-------|-------|
| `RgStrategyRegion` | По области | By region |
| `RgStrategyContour` | По контуру | By contour |
| `RgStrategyFree` | Свободная | Free |
| `RgReference` | Опора | Reference |
| `RgCoverOffset` | Отступ | Cover offset |
| `RgOffsetStep` | Шаг | Step |
| `RgResetOffsets` | Сбросить все | Reset all |
| `RgBarDiameter` | Ø | Ø |
| `RgFillN` | N | N |
| `RgFillStraight` | По прямой | Straight |
| `RgFillArc` | По дуге | By arc |
| `RgFillArcR` | R | R |
| `RgFillMode` | Режим заполнения | Fill mode |
| `RgEdgesTable` | Рёбра | Edges |
| `RgBarsTable` | Стержни | Bars |
| `RgTag` | Тег группы | Group tag |

---

## 8. Локализация диаметров

Везде в UI диаметр стержней отображается и вводится в **мм**. В модели хранится в **м**.  
`BarItem.DiameterMm` — конвертирующее свойство (×1000 / ÷1000).  
`RebarGroupEditorVM.ActiveDiameterMm` — аналогично.  
Все остальные размеры (отступы, координаты X/Y, радиус дуги) — в **м**.
