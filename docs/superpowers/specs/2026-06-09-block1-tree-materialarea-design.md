# Блок 1 — Дерево проекта + MaterialAreaPage: Спецификация

**Дата:** 2026-06-09  
**Статус:** Утверждена

---

## Цель

1. Реструктурировать дерево проекта: убрать мёртвые узлы, добавить «Материальные области» (3 подузла) и «Поперечные сечения» (2 подузла).
2. Сделать `MaterialArea` самостоятельной сущностью проекта (независимой от `CrossSection`).
3. Реализовать `MaterialAreaPage` — редактор одной области с выбором геометрии (Hull/Holes) из пула контуров или созданием нового контура, и превью через `PlotCanvas`.

---

## 1. Модель данных

### 1.1 Новый enum `AreaCategory` в `CScore/MaterialArea.cs`

```csharp
public enum AreaCategory { Region, RebarGroup, SingleBar }
```

- `Region` — полигональная область (бетон/сталь), имеет WKT/Contours
- `RebarGroup` — группа арматурных стержней (point-волокна, кол-во > 1)
- `SingleBar` — одиночный стержень (одно point-волокно)

Добавить в `MaterialArea`:
```csharp
public AreaCategory Category { get; set; } = AreaCategory.Region;
```

### 1.2 DB-схема: `material_areas`

Текущий `section_id INTEGER NOT NULL` → изменить на `section_id INTEGER` (nullable).  
Добавить колонку `category TEXT NOT NULL DEFAULT 'region'`.

Значения category: `'region'`, `'rebar_group'`, `'single_bar'`.

Standalone области (созданные из дерева Блока 1) имеют `section_id IS NULL`.  
Области внутри сечений (созданные в Блоке 2) имеют `section_id NOT NULL`.

Миграция: таблица пересоздаётся (данные в `dbapp.db` не мигрируются, это dev-база).

### 1.3 `DatabaseService` — новая коллекция и методы

```csharp
public ObservableCollection<MaterialArea> MaterialAreas { get; } = [];
```

Новые методы:
- `LoadMaterialAreas()` — загружает строки с `section_id IS NULL`
- `SaveMaterialArea(MaterialArea area)` — upsert по `area.Id`; назначает `section_id = NULL`
- `DeleteMaterialArea(MaterialArea area)` — DELETE + удаление из коллекции

Вызовы в `LoadAll()`:
```
LoadMaterials() → LoadContours() → LoadCrossSections() → LoadMaterialAreas()
```

После `LoadMaterialAreas()` вызвать `ResolveReferencesForStandaloneAreas()` — назначает `Material` и строит диаграммы по `material_id`.

---

## 2. ViewModel

### 2.1 `AppViewModel` — новые поля

```csharp
public ObservableCollection<MaterialArea> MaterialAreas { get; set; }

// Фильтрованные Live-коллекции для дерева
public ObservableCollection<MaterialArea> AreasLive { get; set; }        // Category == Region
public ObservableCollection<MaterialArea> RebarGroupsLive { get; set; }  // Category == RebarGroup
public ObservableCollection<MaterialArea> SingleBarsLive { get; set; }   // Category == SingleBar

// Текущая выбранная область
public MaterialArea? CurrentMaterialArea { get; set; }  // открывает MaterialAreaPage

// Команды
public ICommand NewAreaCommand { get; set; }         // создаёт Region
public ICommand NewRebarGroupCommand { get; set; }   // создаёт RebarGroup (Блок 3)
public ICommand NewSingleBarCommand { get; set; }    // создаёт SingleBar (Блок 3)
public ICommand DeleteMaterialAreaCommand { get; set; }
```

`CurrentMaterialArea` setter:
```csharp
set {
    currentMaterialArea = value;
    CurrentPage = value != null ? new Views.MaterialAreaPage(value, this) : null;
    OnPropertyChanged();
}
```

### 2.2 `CrossSectionsLive` — разделить на два

```csharp
public ObservableCollection<CrossSection> FiberSectionsLive { get; set; }    // !TwoStageSection
public ObservableCollection<CrossSection> TwoStageSectionsLive { get; set; } // TwoStageSection
```

Оба заполняются из `CrossSections` при загрузке/изменении коллекции.  
`CrossSectionsLive` сохраняется для совместимости сохранения — можно оставить как объединяющая коллекция.

### 2.3 `MaterialAreaVM` — расширение

Добавить свойства:
```csharp
public AreaCategory Category { get => _model.Category; set { _model.Category = value; OnPropertyChanged(); } }
public int NX { get => _model.NX; set { _model.NX = value; OnPropertyChanged(); } }
public int NY { get => _model.NY; set { _model.NY = value; OnPropertyChanged(); } }
public string? WKT => _model.WKT;
public Contour? Hull => _model.Hull;

// Список контуров проекта для выбора Hull/Holes
public ObservableCollection<Contour> ProjectContours => App.Contours;

// Команды геометрии
public ICommand SetHullFromPoolCommand { get; }    // выбрать из ComboBox
public ICommand CreateNewHullCommand { get; }      // создать новый Contour → открыть редактор
public ICommand ClearHullCommand { get; }
public ICommand AddHoleCommand { get; }
public ICommand RemoveHoleCommand { get; }

// Для превью
public IReadOnlyList<PlotElement> PlotElements { get; private set; } = [];
public void RefreshPlot() { ... }  // перестраивает PlotElements по Hull + Holes
```

---

## 3. Дерево проекта (`MainWindow.xaml`)

### 3.1 Новая структура

```
TreeView
├── geometryNode          (Геометрия)
│   ├── Окружности        ItemsSource=CirclesLive
│   └── Контуры           ItemsSource=ContoursLive
├── materialNode          (Материалы)
│   ├── Бетон             ItemsSource=Concretes
│   ├── Арматура          ItemsSource=Armatures
│   ├── Сталь             ItemsSource=Steels
│   └── Диаграммы         ItemsSource=DiagramsLive
├── RegionsNode           (Материальные области) ← бывший RegionsNode, очищен
│   ├── AreasNode         (Области)             ItemsSource=AreasLive
│   ├── RebarGroupsNode   (Группы арматуры)     ItemsSource=RebarGroupsLive
│   └── SingleBarsNode    (Одиночные стержни)   ItemsSource=SingleBarsLive
└── crosssectNode         (Поперечные сечения)
    ├── FiberSectionsNode (Фибровые)             ItemsSource=FiberSectionsLive
    └── TwoStageNode      (Усиление)             ItemsSource=TwoStageSectionsLive
```

### 3.2 Изменения XAML

**RegionsNode**: удалить дочерние `TreeViewItem` для `RcFiberRegionsLive`, `RebarGroupsLive`, `Fibrous`, `Solid`. Добавить три новых подузла с правильными `ItemsSource` и контекстными меню.

**crosssectNode**: `ItemsSource` убрать, добавить два дочерних `TreeViewItem` — `FiberSectionsNode` и `TwoStageNode`.

Иконки для MaterialArea в дереве: `Path` с геометрией из конвертеров (`MatTypeToGeometryConverter`, `MatTypeToBrushConverter`).

### 3.3 `MainWindow.xaml.cs` — `SelectedItemChanged`

Добавить case:
```csharp
case MaterialArea areaItem:
    vm.CurrentMaterialArea = areaItem;
    break;
```

---

## 4. `MaterialAreaPage`

### 4.1 Компоновка

```
┌─ MaterialAreaPage ────────────────────────────────────────────────┐
│ Левая панель (300px)          │  Правая панель (*)                │
│                               │                                   │
│ [Обозначение: ___________]    │                                   │
│ [Материал:  ▼ ComboBox   ]    │        PlotCanvas                 │
│ [Диаграмма: ▼ ComboBox   ]    │    (контур + точечные волокна)    │
│                               │                                   │
│ ── Геометрия Hull ──           │                                   │
│ [Из пула: ▼ ComboBox] [Новый] │                                   │
│ [Очистить]                    │                                   │
│                               │                                   │
│ ── Отверстия ──               │                                   │
│ [ Hole 1: ▼ ComboBox ] [✕]    │                                   │
│ [+ Добавить отверстие]        │                                   │
│                               │                                   │
│ ── Сетка ──                   │                                   │
│ NX: [21]  NY: [21]            │                                   │
│                               │                                   │
│ [Сохранить]  [Удалить]        │                                   │
└───────────────────────────────┴───────────────────────────────────┘
```

### 4.2 Поведение Hull

- **ComboBox «Из пула»** → `ItemsSource=ProjectContours`, `DisplayMemberPath="Tag"`. При выборе: `area.Hull = selectedContour; area.SetWKT(); vm.RefreshPlot()`.
- **Кнопка «Новый»** → создаёт пустой `Contour`, добавляет в `AppViewModel.Contours` через `db.AddContour()`, открывает `ContourPage` как `CurrentPage`. После возврата пользователь выбирает созданный контур из ComboBox.
- **Кнопка «Очистить»** → `area.Contours.RemoveAll(c => c.Type == ContourType.Hull); area.WKT = null; vm.RefreshPlot()`.

> Примечание: «Новый контур» **не переходит** автоматически обратно в `MaterialAreaPage` — пользователь выбирает область в дереве снова. Это намеренно простая навигация без сложного wizard-flow.

### 4.3 Holes

Каждая hole — строка: `ComboBox` (из ProjectContours) + кнопка `[✕]`.  
«Добавить отверстие» → добавляет строку с пустым ComboBox.  
При выборе контура: `area.Contours.Add(hole); area.SetWKT(); vm.RefreshPlot()`.

### 4.4 PlotCanvas — `RefreshPlot()`

Строит `List<PlotElement>`:
- Hull: `PlotElement.Polygon(hull.X, hull.Y, fillColor, strokeColor)`
- Каждый Hole: `PlotElement.Polygon(hole.X, hole.Y, transparent, dashedStroke)`
- Point-волокна: `PlotElement.Circle(f.X, f.Y, f.Diameter/2)`

Вызывается при изменении Hull, Holes, добавлении стержней.

### 4.5 «Сохранить»

Вызывает `db.SaveMaterialArea(area)`, обновляет Live-коллекции в AppViewModel.

---

## 5. Локализация

Добавить ключи в `Strings.ru-RU.xaml` и `Strings.en-US.xaml`:

```xml
<!-- Блок 1 -->
<String x:Key="MaterialAreas">Материальные области</String>
<String x:Key="Areas">Области</String>
<String x:Key="RebarGroups">Группы арматуры</String>
<String x:Key="SingleBars">Одиночные стержни</String>
<String x:Key="FiberSections">Фибровые</String>
<String x:Key="TwoStageSections">Усиление</String>
<String x:Key="NewArea">Новая область</String>
<String x:Key="DeleteArea">Удалить область</String>
<String x:Key="Hull">Внешний контур (Hull)</String>
<String x:Key="Holes">Отверстия</String>
<String x:Key="SelectFromPool">Выбрать из пула</String>
<String x:Key="NewContour">Новый контур</String>
<String x:Key="ClearHull">Очистить</String>
<String x:Key="AddHole">Добавить отверстие</String>
<String x:Key="MeshGrid">Сетка</String>
```

---

## 6. Не входит в Блок 1

- Создание групп арматуры / одиночных стержней → Блок 3
- Связывание MaterialArea с CrossSection → Блок 2
- Мастера создания сечений → Блок 2
- Графический выбор областей для сечений → Блок 2
- Нарезка на волокна (SliceXY / Triangulate) из UI → Блок 2+

---

## 7. Файлы — сводка

| Действие | Файл |
|---|---|
| Изменить | `CScore/MaterialArea.cs` — добавить `AreaCategory` |
| Изменить | `OpenCS/Utilites/DatabaseService.cs` — схема, LoadMaterialAreas, SaveMaterialArea, DeleteMaterialArea |
| Изменить | `OpenCS/AppViewModel.cs` — MaterialAreas, Live-коллекции, команды |
| Изменить | `OpenCS/ViewModels/MaterialAreaVM.cs` — новые свойства и команды |
| Создать  | `OpenCS/Views/MaterialAreaPage.xaml` + `.xaml.cs` |
| Изменить | `OpenCS/MainWindow.xaml` — дерево |
| Изменить | `OpenCS/MainWindow.xaml.cs` — SelectedItemChanged |
| Изменить | `OpenCS/Resources/Strings.ru-RU.xaml` |
| Изменить | `OpenCS/Resources/Strings.en-US.xaml` |
