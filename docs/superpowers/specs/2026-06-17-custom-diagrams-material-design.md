# Пользовательские диаграммы и Custom-материал — Спецификация

**Дата:** 2026-06-17  
**Статус:** Утверждено

---

## Цель

1. Разрешить создание произвольных σ(ε)-диаграмм (ввод точек вручную + CSV-импорт).
2. Добавить новый тип материала `MatType.Custom`, чей набор диаграмм по CalcType задаётся явно
   из пула проектных диаграмм (не вычисляется из MaterialChars + DiagrammType).

---

## Секция 1: Редактор диаграмм

### DiagramPage — переход в режим редактирования

Существующий `DiagramPage` расширяется до редактируемого:

- **DataGrid** (уже есть, только для просмотра) → становится редактируемым: можно добавлять строки, удалять, менять значения ε и σ.
- Строки с ε < 0 принадлежат ветви сжатия (Ic), строки с ε > 0 — ветви растяжения (It); строка ε = 0 — граничная.
- Кнопка «Загрузить из CSV» — импорт файла `(ε; σ)` с заголовком. Разбиение на Ic/It по знаку ε.
- Кнопки «Сохранить» (`db.SaveDiagram`) и «Отмена» (откат к исходным данным).

### DiagramCanvas (новый)

Файл: `OpenCS/Views/Helpers/DiagramCanvas.cs` — наследник `FrameworkElement`, чистый WPF.

**Ответственность:**
- Рисует Ic-ветвь (синий) и It-ветвь (красный) полилинией через `DrawingContext` в `OnRender`.
- Точки кривой — перетаскиваемые маркеры (MouseDown/MouseMove/MouseUp).
- Перемещение точки мышью → немедленно обновляет соответствующую строку в DataGrid (через ViewModel).
- Зум (колёсо мыши) и пан (ЛКМ на пустом месте) — как в `FireMeshCanvas`.
- DependencyProperty `ViewModel` типа `DiagramEditVM` → подписка на PropertyChanged → `InvalidateVisual()`.

**Оси и подписи:** рисуются через `DrawingContext` (FormattedText), автомасштаб по точкам.

### DiagramEditVM (новый)

Файл: `OpenCS/ViewModels/DiagramEditVM.cs`.

Поля:
- `ObservableCollection<DiagramPoint> Points` — единая коллекция точек (Ic + It), сортируется по ε.
- `string Tag`, `CalcType CalcType`, `MatType MaterialType` — метаданные.

Методы:
- `BuildSplines()` → обновляет `Diagramm.Ic` / `Diagramm.It` из коллекции Points.
- `ImportCsv(string path)` → парсит CSV, заполняет Points.
- `Save()` → вызывает BuildSplines + `db.SaveDiagram`.

### Кнопка «Новая диаграмма»

В `AppViewModel`: команда `AddDiagramCommand` → создаёт пустой `Diagramm`, открывает DiagramPage в режиме новой.  
В `MainWindow.xaml`: кнопка «+» рядом с узлом «Диаграммы» в TreeView.

### Локализация

Новые ключи в `Strings.ru-RU.xaml` / `Strings.en-US.xaml`:

- `AddDiagramTooltip`, `SaveDiagram`, `DiscardChanges`, `ImportFromCsv`
- `CompressionBranch`, `TensionBranch`, `DiagramPointEps`, `DiagramPointSig`

---

## Секция 2: Custom-материал

### CScore/Material.cs

```csharp
// MatType (уже существует) — добавить значение:
Custom = 5

// Material — добавить поля:
public MatType BaseType { get; set; } = MatType.None;
public Dictionary<CalcType, int> CustomDiagramIds { get; set; } = [];

// Новый метод:
public Dictionary<CalcType, Diagramm>? ResolveCustomDiagramms(IReadOnlyList<Diagramm> pool)
// Для каждого CalcType ищет Diagramm с Id == CustomDiagramIds[ct] в pool.
// Проставляет найденному d.MaterialType = BaseType.
// Возвращает null, если pool пуст или ID не найдены.
```

### CScore/MaterialArea.cs

`ResolveAndBuildDiagramms()` получает новый параметр `IReadOnlyList<Diagramm> pool`:

```csharp
if (Material?.Type == MatType.Custom)
    Diagramms = Material.ResolveCustomDiagramms(pool) ?? [];
else
    Diagramms = Material.GetDiagramms(DiagrammType, sp63EtaMin) ?? [];
```

Точки вызова (обновить подписи):
- `DatabaseService.LoadSections()` / `ResolveReferencesForStandaloneAreas()`
- `MaterialAreaVM` (при смене Material и DiagrammType)
- `CalcResultView.xaml.cs`
- `FireRCheckHandler.cs`, `FireRCheckBatchHandler.cs`
- `StrainStateHandler.cs`

### БД — миграция (v17)

```sql
ALTER TABLE materials ADD COLUMN base_type            INTEGER NOT NULL DEFAULT 0;
ALTER TABLE materials ADD COLUMN custom_diagram_ids   TEXT NOT NULL DEFAULT '{}';
```

Добавить строку в `DatabaseService._migrations` (следующий индекс после последней v16-миграции).

### OpenCS/ViewModels/MaterialVM.cs

Новые прокси-свойства:
- `MatType BaseType` → `material.BaseType`
- `Dictionary<CalcType, int> CustomDiagramIds` → `material.CustomDiagramIds`
- `bool IsCustom => material.Type == MatType.Custom`

### OpenCS/Views/MaterialPage.xaml

- Условный блок (Visibility, конвертер `IsCustom`):
  - **Если Custom:** скрыть вкладки C/CL/N/NL (MaterialChars), показать:
    - ComboBox «Базовый тип» (ItemsSource: Concrete/ReSteelF/ReSteelU/Steel)
    - 4 строки ComboBox (C, CL, N, NL), ItemsSource = `db.Diagrams`
  - **Если не Custom:** стандартный вид без изменений.

### OpenCS/Views/MaterialAreaPage.xaml

- При `MaterialType == Custom`: скрыть ComboBox выбора DiagrammType (он игнорируется движком).

---

## Секция 3: Поток данных

```
Запуск / загрузка проекта:
  LoadMaterials()  →  db.Materials  (читает base_type, custom_diagram_ids)
  LoadDiagrams()   →  db.Diagrams   (пул Diagramm-объектов со спайнами)
  LoadSections()   →
    ↳ ResolveReferences() — привязывает Material к MaterialArea по MaterialId
    ↳ для каждой MaterialArea:
         ResolveAndBuildDiagramms(sp63EtaMin, db.Diagrams)
           ├── Type != Custom → GetDiagramms(DiagrammType)    [старый путь]
           └── Type == Custom → ResolveCustomDiagramms(pool)  [новый путь]
```

`Diagramm.Sig()` не изменяется. `MaterialType` проставляется в `ResolveCustomDiagramms` и дальше
работает существующая логика ветвления (Concrete/ReSteelF/ReSteelU).

Стандартные материалы (Concrete, ReSteelF, ReSteelU, Steel) полностью обратно совместимы.

### Предотвращение мёртвых ссылок

Перед удалением диаграммы из пула (`DiagramPage.Delete_Click`) — проверять, не ссылается ли
на неё какой-либо Custom-материал. Если да — показать предупреждение и блокировать удаление.

---

## Тестирование

| Сценарий | Проверка |
|----------|----------|
| Создать диаграмму вручную (ввод точек) | Сохранилась в `diagrams`, сплайн строится корректно |
| CSV-импорт диаграммы | Точки разбиты по знаку ε на Ic/It |
| Создать Custom-материал, назначить диаграммы | `custom_diagram_ids` сохранён в JSON |
| Перезагрузить проект | Диаграммы резолвятся из пула, MaterialType == BaseType |
| MaterialArea с Custom-материалом | Интеграция фибр выдаёт ненулевые σ |
| Удаление диаграммы, используемой Custom-материалом | Блокируется предупреждением |
| Стандартный материал после миграции | Работает как прежде (base_type=0, custom_diagram_ids='{}') |

---

## Файлы затронутые изменениями

### Новые файлы
- `OpenCS/Views/Helpers/DiagramCanvas.cs`
- `OpenCS/ViewModels/DiagramEditVM.cs`

### Изменённые файлы
| Файл | Изменение |
|------|-----------|
| `CScore/Material.cs` | + `MatType.Custom`, `BaseType`, `CustomDiagramIds`, `ResolveCustomDiagramms()` |
| `CScore/MaterialArea.cs` | `ResolveAndBuildDiagramms(pool)` — новый параметр + ветвь Custom |
| `OpenCS/Utilites/DatabaseService.cs` | Миграция v17, чтение/запись `base_type`/`custom_diagram_ids`, блокировка удаления |
| `OpenCS/ViewModels/MaterialVM.cs` | + `BaseType`, `CustomDiagramIds`, `IsCustom` |
| `OpenCS/AppViewModel.cs` | + `AddDiagramCommand` |
| `OpenCS/Views/DiagramPage.xaml` + `.cs` | Редактируемый DataGrid, DiagramCanvas, CSV-импорт, Save/Discard |
| `OpenCS/Views/MaterialPage.xaml` + `.cs` | Custom-блок с BaseType-комбо и 4 ComboBox диаграмм |
| `OpenCS/Views/MaterialAreaPage.xaml` | Скрытие DiagrammType при Custom |
| `OpenCS/Views/CalcResultView.xaml.cs` | Передача pool в ResolveAndBuildDiagramms |
| `OpenCS/Tasks/FireRCheckHandler.cs` | Передача pool |
| `OpenCS/Tasks/FireRCheckBatchHandler.cs` | Передача pool |
| `OpenCS/Tasks/StrainStateHandler.cs` | Передача pool |
| `OpenCS/Resources/Strings.ru-RU.xaml` | Новые ключи |
| `OpenCS/Resources/Strings.en-US.xaml` | Новые ключи |
