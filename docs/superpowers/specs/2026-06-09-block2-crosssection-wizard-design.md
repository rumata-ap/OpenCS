# Блок 2 — Мастера создания сечений + CrossSectionView: Спецификация

**Дата:** 2026-06-09  
**Статус:** Утверждена  
**Зависит от:** Блок 1 (MaterialAreas как самостоятельные сущности)

---

## Цель

1. Два мастера создания поперечных сечений: `CrossSection` (простое фибровое) и `TwoStageSection` (усиление/двухстадийное).
2. Графический выбор MaterialArea: каждая область отображается как миниатюра с отрисованным контуром — пользователь кликает для включения/исключения из сечения.
3. `CrossSectionView` — наглядное визуальное представление составного сечения (все области с цветовой кодировкой по типу материала).
4. Связь MaterialArea ↔ CrossSection через таблицу `cross_section_areas`.

---

## 1. DB-схема

### 1.1 Новая таблица `cross_section_areas`

```sql
CREATE TABLE IF NOT EXISTS cross_section_areas (
    section_id  INTEGER NOT NULL REFERENCES cross_sections(id) ON DELETE CASCADE,
    area_id     INTEGER NOT NULL REFERENCES material_areas(id) ON DELETE CASCADE,
    order_num   INTEGER NOT NULL DEFAULT 0,
    PRIMARY KEY (section_id, area_id)
);
```

### 1.2 Изменение `material_areas`

`section_id` уже nullable (из Блока 1). В Блоке 2 он больше не используется для хранения принадлежности к сечению — используется `cross_section_areas`. Колонка `section_id` остаётся в таблице для обратной совместимости, но при сохранении через `SaveCrossSection()` не заполняется.

### 1.3 `LoadCrossSections()` — изменение

После загрузки сечений и областей — загрузить связи из `cross_section_areas`:

```csharp
using var cmd = conn.CreateCommand();
cmd.CommandText = "SELECT section_id, area_id FROM cross_section_areas ORDER BY section_id, order_num";
// для каждой строки: найти section по id, найти area в db.MaterialAreas по area_id, добавить в section.Areas
```

### 1.4 `SaveCrossSection()` — изменение

После сохранения самого сечения (upsert `cross_sections`):
```csharp
// Удалить старые связи
DELETE FROM cross_section_areas WHERE section_id = @sid;
// Сохранить новые
INSERT INTO cross_section_areas (section_id, area_id, order_num) VALUES ...
```

Сами `MaterialArea` сохраняются отдельно через `db.SaveMaterialArea()` (они самостоятельные).

---

## 2. Мастер `CrossSectionWizard` (простое сечение)

### 2.1 Шаги мастера

**Шаг 1 — Название:**
- TextBox «Обозначение сечения»
- Кнопки «Далее» / «Отмена»

**Шаг 2 — Выбор областей:**
- Заголовок: «Выберите материальные области»
- `WrapPanel` с миниатюрами MaterialArea (AreaCategory.Region и RebarGroup)
- Каждая миниатюра (`MaterialAreaThumbnail`): 120×120px, `PlotCanvas` с контуром области, имя, тип материала (цветная полоска), чекбокс/выделение по клику
- Выбранные миниатюры подсвечиваются рамкой
- Панель справа: список выбранных областей с кнопками ↑↓ (порядок)
- Кнопки «Назад» / «Создать»

### 2.2 `CrossSectionWizardVM`

```csharp
public class CrossSectionWizardVM : ViewModelBase
{
    public int Step { get; private set; } = 1;  // 1..2
    public string Tag { get; set; }
    public ObservableCollection<MaterialAreaThumbnailVM> AllAreas { get; }
    public ObservableCollection<MaterialAreaThumbnailVM> SelectedAreas { get; }
    public ICommand NextCommand { get; }
    public ICommand BackCommand { get; }
    public ICommand CreateCommand { get; }
    public ICommand ToggleAreaCommand { get; }  // клик по миниатюре
    public ICommand MoveUpCommand { get; }
    public ICommand MoveDownCommand { get; }
}
```

### 2.3 `MaterialAreaThumbnailVM`

```csharp
public class MaterialAreaThumbnailVM : ViewModelBase
{
    public MaterialArea Model { get; }
    public bool IsSelected { get; set; }
    public IReadOnlyList<PlotElement> PlotElements { get; }  // контур для PlotCanvas
    public Brush TypeBrush { get; }  // цвет по MaterialType
}
```

---

## 3. Мастер `TwoStageSectionWizard` (двухстадийное)

### 3.1 Шаги мастера

**Шаг 1 — Название:**
- TextBox «Обозначение»

**Шаг 2 — Этап 1:**
- ComboBox «Сечение этапа 1» (из FiberSectionsLive) — выбор уже существующего
- Поля «Замороженная кривизна»: e0, ky, kz (три TextBox)
- Подсказка: «Задаются после предварительного расчёта сечения 1-го этапа»

**Шаг 3 — Области этапа 2:**
- Аналогично шагу 2 `CrossSectionWizard` — графический выбор областей для усиления (добавляемые области 2-го этапа)

**Шаг 4 — Подтверждение:**
- Краткий summary: имя, этап 1 (ссылка), кол-во областей этапа 2
- Кнопка «Создать»

### 3.2 `TwoStageSectionWizardVM`

Аналогично `CrossSectionWizardVM`, плюс:
```csharp
public CrossSection? Stage1Section { get; set; }
public double FrozenE0 { get; set; }
public double FrozenKy { get; set; }
public double FrozenKz { get; set; }
```

---

## 4. `MaterialAreaThumbnail` (UserControl)

Переиспользуется во всех мастерах и в `CrossSectionView`.

```xml
<UserControl x:Class="OpenCS.Views.MaterialAreaThumbnail">
  <Border Width="120" Height="130" BorderThickness="2"
          BorderBrush="{Binding IsSelected, Converter=...}">
    <StackPanel>
      <local:PlotCanvas Width="110" Height="90" ... />
      <TextBlock Text="{Binding Model.Tag}" TextWrapping="Wrap" FontSize="10"/>
      <Rectangle Height="4" Fill="{Binding TypeBrush}"/>
    </StackPanel>
  </Border>
</UserControl>
```

---

## 5. `CrossSectionView` — визуальное представление

### 5.1 Компоновка

```
┌─ CrossSectionView ──────────────────────────────────────────────┐
│ Левая панель (200px)          │  Правая панель (*)              │
│                               │                                 │
│ [Имя сечения] FontWeight=Bold │                                 │
│                               │    PlotCanvas                   │
│ Список областей:              │  (все области наложены,         │
│  ● Бетонная обл. 1            │   цветовая кодировка            │
│  ○ Арм. группа 1              │   по материалу)                 │
│  ● Бетонная обл. 2            │                                 │
│                               │                                 │
│ [Редактировать] [Удалить]     │                                 │
└───────────────────────────────┴─────────────────────────────────┘
```

### 5.2 Рендер PlotCanvas

`CrossSectionVM.RefreshSectionPlot()`:
- Для каждой области: добавить `PlotElement.Polygon(hull, fill=typeBrush, opacity=0.6)`
- Point-волокна (стержни): `PlotElement.Circle(x, y, r, fill=rebarBrush)`
- Holes: `PlotElement.Polygon(hole, fill=backgroundBrush, stroke=gray)`

Цвета совпадают с `MatTypeToBrushConverter`.

### 5.3 Кнопки

- «Редактировать» → открывает мастер (тот же `CrossSectionWizard` или `TwoStageSectionWizard`) с заполненными данными текущего сечения
- «Удалить» → `db.DeleteCrossSection(section)`, обновление Live-коллекций

---

## 6. AppViewModel — изменения

```csharp
// Новые Live-коллекции (из CrossSections):
public ObservableCollection<CrossSection> FiberSectionsLive { get; set; }
public ObservableCollection<CrossSection> TwoStageSectionsLive { get; set; }

// Команды мастеров:
public ICommand NewFiberSectionCommand { get; set; }    // открывает CrossSectionWizard
public ICommand NewTwoStageSectionCommand { get; set; } // открывает TwoStageSectionWizard
```

`NewCrossSectionCommand` (существующий) → переименовать в `NewFiberSectionCommand`.

---

## 7. Файлы — сводка

| Действие | Файл |
|---|---|
| Изменить | `OpenCS/Utilites/DatabaseService.cs` — cross_section_areas, LoadCrossSections, SaveCrossSection |
| Изменить | `OpenCS/AppViewModel.cs` — FiberSectionsLive, TwoStageSectionsLive, команды мастеров |
| Изменить | `OpenCS/ViewModels/CrossSectionVM.cs` — RefreshSectionPlot |
| Создать  | `OpenCS/ViewModels/CrossSectionWizardVM.cs` |
| Создать  | `OpenCS/ViewModels/TwoStageSectionWizardVM.cs` |
| Создать  | `OpenCS/ViewModels/MaterialAreaThumbnailVM.cs` |
| Создать  | `OpenCS/Views/CrossSectionWizard.xaml` + `.xaml.cs` |
| Создать  | `OpenCS/Views/TwoStageSectionWizard.xaml` + `.xaml.cs` |
| Создать  | `OpenCS/Views/MaterialAreaThumbnail.xaml` + `.xaml.cs` |
| Изменить | `OpenCS/Views/CrossSectionView.xaml` + `.xaml.cs` — полный рендер |
| Изменить | `OpenCS/MainWindow.xaml` — контекстные меню FiberSections/TwoStage |
| Изменить | `OpenCS/MainWindow.xaml.cs` — SelectedItemChanged |
| Изменить | `OpenCS/Resources/Strings.*.xaml` — ключи мастеров |

---

## 8. Не входит в Блок 2

- Создание и редактирование арматурных групп → Блок 3
- Расчёт (итерационное равновесие) → будущая фаза
- Нарезка на волокна из UI → будущая фаза
