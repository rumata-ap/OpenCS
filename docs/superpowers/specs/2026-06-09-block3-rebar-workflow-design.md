# Блок 3 — Арматурный рабочий процесс: Спецификация

**Дата:** 2026-06-09  
**Статус:** Утверждена  
**Зависит от:** Блок 1 (MaterialAreaPage, standalone MaterialArea), Блок 2 (MaterialAreaThumbnail)

---

## Цель

Реализовать создание и редактирование арматурных MaterialArea двух видов:
- **Группа арматуры** (`AreaCategory.RebarGroup`) — несколько точечных волокон (стержней) с возможностью указать хост-область, задать стержни вручную или через генератор (сетка рядов).
- **Одиночный стержень** (`AreaCategory.SingleBar`) — одно точечное волокно.

При создании обоих видов: графический выбор хост-области (бетонная/стальная область-носитель) с визуализацией размещения стержней поверх контура.

---

## 1. Новые страницы

### 1.1 `RebarGroupPage` — редактор группы арматуры

```
┌─ RebarGroupPage ───────────────────────────────────────────────────┐
│ Левая панель (320px)            │  Правая панель (*)               │
│                                 │                                  │
│ [Обозначение: ____________]     │                                  │
│ [Материал: ▼ (арматура)]        │   PlotCanvas                    │
│ [Диаграмма: ▼]                  │  (контур хост-области +          │
│                                 │   стержни поверх)                │
│ ── Хост-область (бетон) ──      │                                  │
│ [Выбрать графически ▼]          │                                  │
│  (миниатюры Region-областей)    │                                  │
│                                 │                                  │
│ ── Стержни ──                   │                                  │
│ Вкладки: [Список] [Генератор]   │                                  │
│                                 │                                  │
│ [Список]:                       │                                  │
│  X      Y      Ø      [✕]       │                                  │
│  0.05   0.10   0.016  [✕]       │                                  │
│  0.35   0.10   0.016  [✕]       │                                  │
│  [+ Добавить стержень]          │                                  │
│                                 │                                  │
│ [Генератор]:                    │                                  │
│  Рядов: [2]  Столбцов: [3]      │                                  │
│  x0:[0.05] y0:[0.10]            │                                  │
│  dx:[0.15] dy:[0.10]            │                                  │
│  Ø: [0.016]                     │                                  │
│  [Сгенерировать]                │                                  │
│                                 │                                  │
│ [Сохранить]  [Удалить]          │                                  │
└─────────────────────────────────┴──────────────────────────────────┘
```

### 1.2 `SingleBarPage` — редактор одиночного стержня

Упрощённая версия `RebarGroupPage` — без вкладок и генератора, только одна строка координат:

```
[Обозначение] [Материал] [Диаграмма]
── Хост-область ──
[Выбрать графически ▼]
── Стержень ──
X: [___]  Y: [___]  Ø: [___]
── PlotCanvas превью ──
[Сохранить] [Удалить]
```

---

## 2. Выбор хост-области

### 2.1 Компонент `HostAreaPicker`

`UserControl`, переиспользуется в `RebarGroupPage` и `SingleBarPage`.

Содержит `WrapPanel` из `MaterialAreaThumbnail` (из Блока 2), отфильтрованных по `AreaCategory.Region`.  
Клик по миниатюре → выбирает хост-область, PlotCanvas правой панели перестраивается: показывает контур хост-области + текущие стержни.

Если хост-область не выбрана — PlotCanvas показывает только стержни в системе координат без фона.

### 2.2 VM

```csharp
public class RebarGroupVM : ViewModelBase
{
    public MaterialArea Model { get; }
    public ObservableCollection<BarRowVM> Bars { get; }  // строки в таблице
    public MaterialArea? HostArea { get; set; }          // выбранная хост-область
    public ObservableCollection<MaterialAreaThumbnailVM> AvailableHosts { get; }

    // Генератор
    public int GenRows { get; set; }
    public int GenCols { get; set; }
    public double GenX0 { get; set; }
    public double GenY0 { get; set; }
    public double GenDx { get; set; }
    public double GenDy { get; set; }
    public double GenDiameter { get; set; }

    public ICommand AddBarCommand { get; }
    public ICommand RemoveBarCommand { get; }
    public ICommand GenerateBarsCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand DeleteCommand { get; }

    public IReadOnlyList<PlotElement> PlotElements { get; private set; }
    public void RefreshPlot() { ... }
}

public class BarRowVM : ViewModelBase
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Diameter { get; set; }
    public ICommand RemoveCommand { get; }
}
```

### 2.3 `GenerateBarsCommand`

Генерирует матрицу стержней `Rows × Cols`:
```csharp
for (int r = 0; r < GenRows; r++)
    for (int c = 0; c < GenCols; c++)
        Bars.Add(new BarRowVM { X = GenX0 + c * GenDx, Y = GenY0 + r * GenDy, Diameter = GenDiameter });
RefreshPlot();
```

---

## 3. PlotCanvas — визуализация

`RefreshPlot()` в `RebarGroupVM`:

```csharp
var elements = new List<PlotElement>();

// Контур хост-области (фон)
if (HostArea?.Hull != null)
    elements.Add(PlotElement.Polygon(hull.X, hull.Y, concreteBrush, opacity: 0.3));

// Отверстия хост-области
foreach (var hole in HostArea?.Holes ?? [])
    elements.Add(PlotElement.Polygon(hole.X, hole.Y, whiteBrush, opacity: 1.0));

// Стержни
foreach (var bar in Bars)
    elements.Add(PlotElement.Circle(bar.X, bar.Y, bar.Diameter / 2, rebarBrush));

PlotElements = elements;
OnPropertyChanged(nameof(PlotElements));
```

---

## 4. AppViewModel — изменения

```csharp
// Команды для создания из дерева:
public ICommand NewRebarGroupCommand { get; set; }  // AreaCategory.RebarGroup
public ICommand NewSingleBarCommand { get; set; }   // AreaCategory.SingleBar

// Текущая область (уже есть из Блока 1):
public MaterialArea? CurrentMaterialArea { get; set; }
```

`CurrentMaterialArea` setter должен различать тип и открывать нужную страницу:
```csharp
CurrentPage = area.Category switch {
    AreaCategory.Region    => new MaterialAreaPage(area, this),
    AreaCategory.RebarGroup => new RebarGroupPage(area, this),
    AreaCategory.SingleBar  => new SingleBarPage(area, this),
    _ => null
};
```

---

## 5. Сохранение в БД

`SaveMaterialArea(area)` в `DatabaseService` уже покрывает всё:
- Point-волокна сохраняются в `point_fibers`
- `host_area_id` и `category` сохраняются в `material_areas`

Никаких новых таблиц не требуется.

После сохранения `RebarGroupVM`:
- Заменить `area.Fibers` на список из `Bars` → `Fiber.CreatePoint(bar.Diameter, bar.X, bar.Y)`
- Вызвать `area.ResolveAndBuildDiagramms()` — автоматически строит дифференциальные диаграммы, если `HostArea != null`
- Вызвать `db.SaveMaterialArea(area)`

---

## 6. Локализация

Добавить ключи в оба `Strings.*.xaml`:

```xml
<String x:Key="NewRebarGroup">Новая группа арматуры</String>
<String x:Key="NewSingleBar">Новый одиночный стержень</String>
<String x:Key="HostArea">Хост-область (бетон)</String>
<String x:Key="Bars">Стержни</String>
<String x:Key="BarsList">Список</String>
<String x:Key="BarsGenerator">Генератор</String>
<String x:Key="GenerateBars">Сгенерировать</String>
<String x:Key="AddBar">Добавить стержень</String>
<String x:Key="Rows">Рядов</String>
<String x:Key="Cols">Столбцов</String>
<String x:Key="Spacing">Шаг</String>
```

---

## 7. Файлы — сводка

| Действие | Файл |
|---|---|
| Изменить | `OpenCS/AppViewModel.cs` — CurrentMaterialArea switch, NewRebarGroupCommand, NewSingleBarCommand |
| Создать  | `OpenCS/ViewModels/RebarGroupVM.cs` |
| Создать  | `OpenCS/ViewModels/SingleBarVM.cs` |
| Создать  | `OpenCS/ViewModels/BarRowVM.cs` |
| Создать  | `OpenCS/Views/RebarGroupPage.xaml` + `.xaml.cs` |
| Создать  | `OpenCS/Views/SingleBarPage.xaml` + `.xaml.cs` |
| Создать  | `OpenCS/Views/HostAreaPicker.xaml` + `.xaml.cs` |
| Изменить | `OpenCS/MainWindow.xaml` — контекстные меню RebarGroups/SingleBars |
| Изменить | `OpenCS/Resources/Strings.*.xaml` |

---

## 8. Не входит в Блок 3

- Расстановка стержней по DXF — будущая фаза
- Импорт арматурных групп из внешних источников — будущая фаза
- Расчёт (итерационное равновесие) — будущая фаза
