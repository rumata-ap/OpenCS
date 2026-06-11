# Блок 3 — Арматурный рабочий процесс: План реализации

> **Для агентных воркеров:** используйте `superpowers:executing-plans`.  
> **Зависит от:** Блок 1 и Блок 2 завершены, сборка чистая.

**Спек:** `docs/superpowers/specs/2026-06-09-block3-rebar-workflow-design.md`  
**Команда сборки:** `dotnet build OpenCS.sln` → ожидается `0 Error(s)`

---

## Task 1: `BarRowVM`

**Файлы:** создать `OpenCS/ViewModels/BarRowVM.cs`

```csharp
using OpenCS.Utilites;
using System.Windows.Input;

namespace OpenCS.ViewModels
{
    public class BarRowVM : ViewModelBase
    {
        double _x, _y, _diameter;
        readonly System.Action _onChanged;

        public BarRowVM(double x, double y, double d, System.Action onChanged)
        { _x = x; _y = y; _diameter = d; _onChanged = onChanged; }

        public double X
        { get => _x; set { _x = value; OnPropertyChanged(); _onChanged(); } }

        public double Y
        { get => _y; set { _y = value; OnPropertyChanged(); _onChanged(); } }

        public double Diameter
        { get => _diameter; set { _diameter = value; OnPropertyChanged(); _onChanged(); } }

        public ICommand RemoveCommand { get; set; } = null!;
    }
}
```

- [ ] Сборка → `0 Error(s)`
- [ ] Коммит: `feat(VM): add BarRowVM`

---

## Task 2: `RebarGroupVM`

**Файлы:** создать `OpenCS/ViewModels/RebarGroupVM.cs`

- [ ] Реализовать `RebarGroupVM` согласно спеку:
  - `ObservableCollection<BarRowVM> Bars` — инициализируется из `_model.Fibers` (point-волокна)
  - `MaterialArea? HostArea { get; set; }` — при изменении вызывает `RefreshPlot()`
  - `ObservableCollection<MaterialAreaThumbnailVM> AvailableHosts` — из `App.AreasLive` (Category.Region)
  - `AddBarCommand` — добавляет `BarRowVM(0,0,0.016, RefreshPlot)` в `Bars`, вызывает `RefreshPlot()`
  - `RemoveBarCommand` — принимает `BarRowVM`, удаляет, `RefreshPlot()`
  - Поля генератора: `GenRows`, `GenCols`, `GenX0`, `GenY0`, `GenDx`, `GenDy`, `GenDiameter`
  - `GenerateBarsCommand` — очищает `Bars`, генерирует матрицу стержней, `RefreshPlot()`
  - `SaveCommand` — синхронизирует `_model.Fibers` из `Bars`, вызывает `_model.ResolveAndBuildDiagramms()`, `App.db.SaveMaterialArea(_model)`, обновляет `App.RefreshMaterialAreaLiveCollections()`
  - `DeleteCommand` — `App.db.DeleteMaterialArea(_model)`, `App.CurrentPage = null`
- [ ] Реализовать `RefreshPlot()`:
```csharp
public void RefreshPlot()
{
    var elements = new List<PlotElement>();
    if (HostArea?.Hull != null)
    {
        var fill = MatTypeToBrushConverter.GetBrush(HostArea.Material?.Type ?? MatType.None);
        elements.Add(PlotElement.FilledPolygon(HostArea.Hull.X.ToArray(), HostArea.Hull.Y.ToArray(), fill, 0.25));
        foreach (var hole in HostArea.Holes)
            elements.Add(PlotElement.Polygon(hole.X.ToArray(), hole.Y.ToArray(), Colors.White));
    }
    foreach (var bar in Bars)
        if (bar.Diameter > 0)
            elements.Add(PlotElement.FilledCircle(bar.X, bar.Y, bar.Diameter / 2, Colors.OrangeRed));
    PlotElements = elements;
    OnPropertyChanged(nameof(PlotElements));
}
```
- [ ] Сборка → `0 Error(s)`
- [ ] Коммит: `feat(VM): add RebarGroupVM`

---

## Task 3: `HostAreaPicker` (UserControl)

**Файлы:** создать `OpenCS/Views/HostAreaPicker.xaml` + `.xaml.cs`

- [ ] XAML: `GroupBox` «Хост-область» с `WrapPanel` из `MaterialAreaThumbnail`
- [ ] `ItemsSource` — `AvailableHosts` из VM
- [ ] Клик по миниатюре → `ToggleCommand` (выбирает только одну, остальные deselect) → VM обновляет `HostArea`
- [ ] Логика «только одна выбрана»: в `RebarGroupVM` при `ToggleCommand` — сбросить `IsSelected` у всех, установить у выбранной, `HostArea = selected.Model`
- [ ] Code-behind: `DataContext` получает снаружи (используется как часть `RebarGroupPage`)
- [ ] Сборка → `0 Error(s)`
- [ ] Коммит: `feat(UI): add HostAreaPicker control`

---

## Task 4: `RebarGroupPage`

**Файлы:** создать `OpenCS/Views/RebarGroupPage.xaml` + `.xaml.cs`

- [ ] XAML: двухколоночный Grid (320px + *)
  - Левая панель:
    - TextBox «Обозначение», ComboBox «Материал» (из `App.Armatures`), ComboBox «Диаграмма»
    - `HostAreaPicker` (вложенный UserControl)
    - `TabControl` с вкладками «Список» и «Генератор»
      - Вкладка «Список»: `DataGrid` с колонками X, Y, Ø и кнопкой удалить; кнопка «+ Добавить»
      - Вкладка «Генератор»: поля Рядов, Столбцов, x0, y0, dx, dy, Ø, кнопка «Сгенерировать»
    - Кнопки «Сохранить», «Удалить»
  - Правая панель: `<local:PlotCanvas Elements="{Binding PlotElements}" SquareAxes="True"/>`
- [ ] Code-behind:
```csharp
public RebarGroupPage(MaterialArea area, AppViewModel app)
{
    InitializeComponent();
    var vm = new RebarGroupVM(area, app);
    DataContext = vm;
    vm.PropertyChanged += (_, e) => {
        if (e.PropertyName == nameof(vm.PlotElements))
            preview.SetElements(vm.PlotElements);
    };
}
```
- [ ] Сборка → `0 Error(s)`
- [ ] Коммит: `feat(UI): add RebarGroupPage`

---

## Task 5: `SingleBarVM` и `SingleBarPage`

**Файлы:** создать `OpenCS/ViewModels/SingleBarVM.cs`, `OpenCS/Views/SingleBarPage.xaml` + `.xaml.cs`

- [ ] `SingleBarVM`: упрощённый `RebarGroupVM` — без генератора, один bar (X, Y, Diameter)
- [ ] `SingleBarPage.xaml`: вертикальная компоновка — Tag, Material, Diagram, HostAreaPicker, три поля (X, Y, Ø), PlotCanvas, Сохранить/Удалить
- [ ] Code-behind аналогично `RebarGroupPage`
- [ ] Сборка → `0 Error(s)`
- [ ] Коммит: `feat(UI): add SingleBarVM and SingleBarPage`

---

## Task 6: Обновить `AppViewModel` — команды создания арматуры

**Файлы:** `OpenCS/AppViewModel.cs`

- [ ] Добавить команды (заглушки из Блока 1 были):
```csharp
NewRebarGroupCommand = new RelayCommand(_ => NewRebarGroup());
NewSingleBarCommand  = new RelayCommand(_ => NewSingleBar());
```
- [ ] Реализовать:
```csharp
void NewRebarGroup()
{
    var area = new MaterialArea { Tag = "Группа арматуры", Category = AreaCategory.RebarGroup };
    CurrentPage = new Views.RebarGroupPage(area, this);
}

void NewSingleBar()
{
    var area = new MaterialArea { Tag = "Стержень", Category = AreaCategory.SingleBar };
    CurrentPage = new Views.SingleBarPage(area, this);
}
```
- [ ] `CurrentMaterialArea` setter — уже имеет switch из Блока 1, добавить `RebarGroup` и `SingleBar` cases если не добавлено
- [ ] `RefreshMaterialAreaLiveCollections()` — вызывается из `SaveCommand` через `App.RefreshMaterialAreaLiveCollections()` (сделать public если нужно)
- [ ] Сборка → `0 Error(s)`
- [ ] Коммит: `feat(VM): wire RebarGroup and SingleBar creation commands`

---

## Task 7: Обновить дерево — контекстные меню арматуры

**Файлы:** `OpenCS/MainWindow.xaml`

- [ ] `RebarGroupsNode` ContextMenu: заменить заглушку на `NewRebarGroupCommand`
- [ ] `SingleBarsNode` ContextMenu: заменить заглушку на `NewSingleBarCommand`
- [ ] Сборка → `0 Error(s)`
- [ ] Коммит: `feat(UI): wire rebar tree context menus`

---

## Task 8: Локализация + финальная проверка

**Файлы:** `OpenCS/Resources/Strings.ru-RU.xaml`, `OpenCS/Resources/Strings.en-US.xaml`

- [ ] Добавить ключи из спека раздел 6 (`NewRebarGroup`, `NewSingleBar`, `HostArea`, `Bars`, `BarsList`, `BarsGenerator`, `GenerateBars`, `AddBar`, `Rows`, `Cols`, `Spacing`)
- [ ] `dotnet build OpenCS.sln` → `0 Error(s)`
- [ ] Запуск: создать группу арматуры → выбрать хост-область → добавить стержни вручную → сгенерировать → PlotCanvas показывает стержни поверх контура → сохранить → перезапуск → загружается из БД
- [ ] Создать одиночный стержень — аналогичная проверка
- [ ] Коммит финальный если нужен
