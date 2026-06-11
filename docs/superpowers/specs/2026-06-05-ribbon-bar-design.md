# RibbonBar — Лента главного окна

**Дата:** 2026-06-05  
**Статус:** Утверждён

---

## Цель

Заменить оба `ToolBarTray` в `MainWindow` кастомной лентой (ribbon) на базе стилизованного WPF `TabControl`. Меню (File / Edit / View / Settings) остаётся без изменений. Новых NuGet-зависимостей не добавляется.

---

## Архитектура

### Новый компонент

`OpenCS/Views/RibbonBar.xaml` — `UserControl`.  
`DataContext` наследуется от `MainWindow` (то есть `AppViewModel`). Явная передача не нужна — достаточно `DataContext="{Binding}"` при вставке в MainWindow.

### Изменения в MainWindow.xaml

1. Удалить `ToolBarTray` горизонтальный (Grid.Row="1", Grid.Column="0..2").
2. Удалить `ToolBarTray` вертикальный (Grid.Column="3", Grid.Row="1..4").
3. Удалить `ColumnDefinition` колонки 3 (ширина `Auto`).
4. Удалить `GridSplitter` горизонтальный (он был в Grid.Column="2", Grid.Row="3") — **оставить**, он нужен для разделения ContentControl и LogViewer.
5. Добавить `<local:RibbonBar Grid.Row="1" Grid.Column="0" Grid.ColumnSpan="3"/>` вместо удалённых тулбаров.

Результирующая сетка колонок:

| # | Ширина | Содержимое |
|---|--------|------------|
| 0 | 0.4\*  | TabControl (TreeView / DataSelection) |
| 1 | Auto   | GridSplitter вертикальный |
| 2 | \*     | ContentControl + ListView (Log) |

---

## Структура RibbonBar

```
RibbonBar (UserControl, Height ~70px)
└── TabControl
    └── TabItem Header="Главная"
        └── StackPanel (Horizontal, VerticalAlignment=Center)
            ├── RibbonGroup "Геометрия"
            ├── RibbonGroup "Материалы"
            ├── RibbonGroup "Регионы"
            └── RibbonGroup "Настройки"
```

### RibbonGroup

Каждая группа — `Border` с правым разделителем 1px, внутри:

```
Border
└── StackPanel (Vertical)
    ├── StackPanel (Horizontal) — кнопки
    └── TextBlock — имя группы (FontSize=10, Foreground=Gray, HorizontalAlignment=Center)
```

Имена групп — через `DynamicResource` (новые ключи `RibbonGroupGeometry`, `RibbonGroupMaterials`, `RibbonGroupRegions`, `RibbonGroupSettings`).

### Кнопки

- Стиль: `{StaticResource IconButton}` (существующий, 38×38)
- Содержимое: только иконка (`Image`)
- `ToolTip="{DynamicResource <ключ>}"`
- `Command="{Binding <команда>}"`

| Группа | Кнопка | Иконка | ToolTip-ключ | Команда |
|--------|--------|--------|--------------|---------|
| Геометрия | Из DXF | `di_dxf_file_xaml` | `FromDxf` *(существует)* | `FromDxfCommand` |
| Геометрия | Новый контур | `addDrawingImage` | `TipNewContour` | `NewContourCommand` |
| Геометрия | Удалить контур | `deleteDrawingImage` | `TipDelContour` | `DelContourCommand` |
| Материалы | Новый материал | `/Images/add__32.png` | `TipNewMaterial` | `NewMaterialCommand` |
| Материалы | Удалить материал | `/Images/delete--32.png` | `TipDelMaterial` | `DelMaterialCommand` |
| Регионы | Новая RC-область | `/Images/add__32.png` | `TipNewRCRegion` | `NewRCFiberRegionCommand` |
| Регионы | Удалить RC-область | `/Images/delete__32.png` | `TipDelRCRegion` | `DeleteRCFiberRegionCommand` |
| Настройки | Настройки графиков | `/Images/diagramma--32.png` | `TipPlotSettings` | `OpenPlotSettingsCommand` |
| Настройки | Настройки CSV | `/Images/diagramma--32.png` | `TipCsvSettings` | `OpenCsvSettingsCommand` |

> Иконки для настроек — временные заглушки, можно заменить позже без изменения архитектуры.

---

## Локализация

### Новые ключи в Strings.ru-RU.xaml и Strings.en-US.xaml

| Ключ | ru-RU | en-US |
|------|-------|-------|
| `RibbonGroupGeometry` | Геометрия | Geometry |
| `RibbonGroupMaterials` | Материалы | Materials |
| `RibbonGroupRegions` | Регионы | Regions |
| `RibbonGroupSettings` | Настройки | Settings |
| `TipNewContour` | Новый контур | New contour |
| `TipDelContour` | Удалить контур | Delete contour |
| `TipNewMaterial` | Новый материал | New material |
| `TipDelMaterial` | Удалить материал | Delete material |
| `TipNewRCRegion` | Новая армированная область | New RC region |
| `TipDelRCRegion` | Удалить армированную область | Delete RC region |
| `TipPlotSettings` | Настройки графиков | Plot settings |
| `TipCsvSettings` | Настройки экспорта CSV | CSV export settings |

---

## Что не меняется

- `Menu` (File / Edit / View / Settings) — без изменений
- Все команды в `AppViewModel` — без изменений
- TreeView, ContentControl, LogViewer, StatusBar — без изменений
- Стиль `IconButton` в `App.xaml` — без изменений

---

## Файлы, затронутые реализацией

| Файл | Действие |
|------|----------|
| `OpenCS/Views/RibbonBar.xaml` | Создать |
| `OpenCS/Views/RibbonBar.xaml.cs` | Создать (code-behind, пустой) |
| `OpenCS/MainWindow.xaml` | Изменить (удалить тулбары, колонку, добавить RibbonBar) |
| `OpenCS/Resources/Strings.ru-RU.xaml` | Добавить 12 ключей |
| `OpenCS/Resources/Strings.en-US.xaml` | Добавить 12 ключей |
