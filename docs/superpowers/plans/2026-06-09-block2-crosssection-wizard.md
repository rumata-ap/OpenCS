# Блок 2 — Мастера создания сечений + CrossSectionView: План реализации

> **Для агентных воркеров:** используйте `superpowers:executing-plans`.  
> **Зависит от:** Блок 1 завершён, сборка чистая.

**Спек:** `docs/superpowers/specs/2026-06-09-block2-crosssection-wizard-design.md`  
**Команда сборки:** `dotnet build OpenCS.sln` → ожидается `0 Error(s)`

---

## Task 1: DB — таблица `cross_section_areas`

**Файлы:** `OpenCS/Utilites/DatabaseService.cs`

- [ ] В `CreateCrossSectionTables()` добавить:
```sql
CREATE TABLE IF NOT EXISTS cross_section_areas (
    section_id  INTEGER NOT NULL REFERENCES cross_sections(id) ON DELETE CASCADE,
    area_id     INTEGER NOT NULL REFERENCES material_areas(id) ON DELETE CASCADE,
    order_num   INTEGER NOT NULL DEFAULT 0,
    PRIMARY KEY (section_id, area_id)
);
```
- [ ] В `LoadCrossSections()` после загрузки областей из `material_areas WHERE section_id IS NOT NULL` — добавить загрузку связей из `cross_section_areas` и дополнять `section.Areas` ссылками на объекты из `db.MaterialAreas`:
```csharp
cmd.CommandText = "SELECT section_id, area_id FROM cross_section_areas ORDER BY section_id, order_num";
// для каждой строки: найти section, найти area в MaterialAreas, добавить в section.Areas
```
- [ ] В `SaveCrossSectionCore()`: после upsert cross_section — удалить старые `cross_section_areas`, вставить новые по `section.Areas`:
```csharp
DELETE FROM cross_section_areas WHERE section_id = @sid;
INSERT INTO cross_section_areas (section_id, area_id, order_num) VALUES ...
```
Сами области не сохраняются здесь — только связи (области — самостоятельные сущности из Блока 1).
- [ ] Сборка → `0 Error(s)`
- [ ] Коммит: `feat(DB): add cross_section_areas junction table`

---

## Task 2: `MaterialAreaThumbnailVM` и `MaterialAreaThumbnail`

**Файлы:** создать `OpenCS/ViewModels/MaterialAreaThumbnailVM.cs`, `OpenCS/Views/MaterialAreaThumbnail.xaml` + `.xaml.cs`

### `MaterialAreaThumbnailVM`
```csharp
public class MaterialAreaThumbnailVM : ViewModelBase
{
    public MaterialArea Model { get; }
    public bool IsSelected { get; set; }  // OnPropertyChanged
    public IReadOnlyList<PlotElement> PlotElements { get; private set; }
    public Brush TypeBrush { get; }
    public ICommand ToggleCommand { get; }

    public MaterialAreaThumbnailVM(MaterialArea area)
    {
        Model = area;
        TypeBrush = MatTypeToBrushConverter.GetBrush(area.Material?.Type ?? MatType.None);
        ToggleCommand = new RelayCommand(_ => { IsSelected = !IsSelected; OnPropertyChanged(nameof(IsSelected)); });
        BuildPlotElements();
    }

    void BuildPlotElements()
    {
        var elements = new List<PlotElement>();
        if (Model.Hull != null)
            elements.Add(PlotElement.FilledPolygon(Model.Hull.X.ToArray(), Model.Hull.Y.ToArray(), TypeBrush, 0.5));
        foreach (var f in Model.Fibers.Where(f => f.TypeFiber == FiberType.point))
            elements.Add(PlotElement.FilledCircle(f.X, f.Y, f.Diameter / 2, Colors.Orange));
        PlotElements = elements;
    }
}
```

### `MaterialAreaThumbnail.xaml`
```xml
<UserControl x:Class="OpenCS.Views.MaterialAreaThumbnail" Width="130" Height="140">
  <Border BorderThickness="2" Padding="4"
          BorderBrush="{Binding IsSelected, Converter={StaticResource BoolToHighlightConverter}}">
    <StackPanel>
      <local:PlotCanvas Height="90" Elements="{Binding PlotElements}" SquareAxes="True"/>
      <TextBlock Text="{Binding Model.Tag}" FontSize="10" TextWrapping="Wrap" Margin="2,2,2,0"/>
      <Rectangle Height="4" Fill="{Binding TypeBrush}" Margin="0,2,0,0"/>
    </StackPanel>
  </Border>
</UserControl>
```

- [ ] Реализовать `BoolToHighlightConverter`: `true` → `AccentBrush`, `false` → `Transparent`
- [ ] Сборка → `0 Error(s)`
- [ ] Коммит: `feat(UI): add MaterialAreaThumbnailVM and MaterialAreaThumbnail control`

---

## Task 3: `CrossSectionWizardVM` и `CrossSectionWizard`

**Файлы:** создать `OpenCS/ViewModels/CrossSectionWizardVM.cs`, `OpenCS/Views/CrossSectionWizard.xaml` + `.xaml.cs`

### `CrossSectionWizardVM`
```csharp
public class CrossSectionWizardVM : ViewModelBase
{
    readonly AppViewModel _app;
    readonly CrossSection? _editing;  // null = создание нового

    public int Step { get; private set; } = 1;
    public string Tag { get; set; } = "Новое сечение";
    public ObservableCollection<MaterialAreaThumbnailVM> AllAreas { get; }
    public ObservableCollection<MaterialAreaThumbnailVM> SelectedAreas { get; }
    public ICommand NextCommand { get; }
    public ICommand BackCommand { get; }
    public ICommand CreateCommand { get; }
    public ICommand MoveUpCommand { get; }
    public ICommand MoveDownCommand { get; }

    public CrossSectionWizardVM(AppViewModel app, CrossSection? editing = null)
    {
        _app = app; _editing = editing;
        Tag = editing?.Tag ?? "Новое сечение";
        AllAreas = new(app.MaterialAreas
            .Where(a => a.Category == AreaCategory.Region || a.Category == AreaCategory.RebarGroup)
            .Select(a => new MaterialAreaThumbnailVM(a)));
        SelectedAreas = new();
        if (editing != null)
            foreach (var a in editing.Areas)
            {
                var thumb = AllAreas.FirstOrDefault(t => t.Model == a);
                if (thumb != null) { thumb.IsSelected = true; SelectedAreas.Add(thumb); }
            }
        NextCommand   = new RelayCommand(_ => { if (Step == 1) Step = 2; OnPropertyChanged(nameof(Step)); });
        BackCommand   = new RelayCommand(_ => { if (Step == 2) Step = 1; OnPropertyChanged(nameof(Step)); });
        CreateCommand = new RelayCommand(_ => Create());
        MoveUpCommand   = new RelayCommand(o => MoveUp(o as MaterialAreaThumbnailVM));
        MoveDownCommand = new RelayCommand(o => MoveDown(o as MaterialAreaThumbnailVM));
    }

    void Create()
    {
        var section = _editing ?? new CrossSection();
        section.Tag = Tag;
        section.Areas.Clear();
        foreach (var t in SelectedAreas) section.Areas.Add(t.Model);
        _app.db.SaveCrossSection(section);
        if (_editing == null)
        {
            _app.CrossSections.Add(section);
            _app.RefreshSectionLiveCollections();
        }
        _app.CurrentPage = new Views.CrossSectionView(section, _app);
    }
}
```

### `CrossSectionWizard.xaml`

Двухшаговый wizard:
- Шаг 1: `TextBox` Tag
- Шаг 2: `WrapPanel` с `MaterialAreaThumbnail` + список выбранных + кнопки ↑↓

Переключение шагов через `DataTrigger` по `Step` или `ContentControl` с `ContentTemplateSelector`.

- [ ] Сборка → `0 Error(s)`
- [ ] Коммит: `feat(UI): add CrossSectionWizard (simple fiber section)`

---

## Task 4: `TwoStageSectionWizardVM` и `TwoStageSectionWizard`

**Файлы:** создать `OpenCS/ViewModels/TwoStageSectionWizardVM.cs`, `OpenCS/Views/TwoStageSectionWizard.xaml` + `.xaml.cs`

- [ ] 4 шага мастера (Tag → Этап1 + кривизна → Области этапа2 → Summary)
- [ ] `TwoStageSectionWizardVM`: аналог `CrossSectionWizardVM` + `Stage1Section`, `FrozenE0/Ky/Kz`
- [ ] `Create()`: создаёт `TwoStageSection`, связывает `Stage1`, заполняет `Stage1Kurvature`, сохраняет
- [ ] Сборка → `0 Error(s)`
- [ ] Коммит: `feat(UI): add TwoStageSectionWizard`

---

## Task 5: Обновить `CrossSectionView` — полный рендер

**Файлы:** `OpenCS/Views/CrossSectionView.xaml`, `OpenCS/Views/CrossSectionView.xaml.cs`, `OpenCS/ViewModels/CrossSectionVM.cs`

- [ ] В `CrossSectionVM` добавить:
```csharp
public IReadOnlyList<PlotElement> PlotElements { get; private set; }

public void RefreshSectionPlot()
{
    var elements = new List<PlotElement>();
    foreach (var area in _model.Areas)
    {
        var brush = MatTypeToBrushConverter.GetBrush(area.Material?.Type ?? MatType.None);
        if (area.Hull != null)
            elements.Add(PlotElement.FilledPolygon(area.Hull.X.ToArray(), area.Hull.Y.ToArray(), brush, 0.6));
        foreach (var hole in area.Holes)
            elements.Add(PlotElement.Polygon(hole.X.ToArray(), hole.Y.ToArray(), Colors.White, dashed: false));
        foreach (var f in area.Fibers.Where(f => f.TypeFiber == FiberType.point))
            elements.Add(PlotElement.FilledCircle(f.X, f.Y, f.Diameter / 2, Colors.Orange));
    }
    PlotElements = elements;
    OnPropertyChanged(nameof(PlotElements));
}
```
- [ ] XAML: двухколоночный Grid — список областей слева, `PlotCanvas` справа. Кнопки «Редактировать» (→ Wizard) и «Удалить»
- [ ] Вызов `RefreshSectionPlot()` в конструкторе `CrossSectionVM`
- [ ] Сборка → `0 Error(s)`
- [ ] Коммит: `feat(UI): implement CrossSectionView with PlotCanvas render`

---

## Task 6: Обновить `AppViewModel` — команды мастеров

**Файлы:** `OpenCS/AppViewModel.cs`

- [ ] Добавить команды:
```csharp
public ICommand NewFiberSectionCommand { get; set; }
public ICommand NewTwoStageSectionCommand { get; set; }
```
- [ ] `NewFiberSectionCommand` → `CurrentPage = new Views.CrossSectionWizard(this)`
- [ ] `NewTwoStageSectionCommand` → `CurrentPage = new Views.TwoStageSectionWizard(this)`
- [ ] Старый `NewCrossSectionCommand` заменить на `NewFiberSectionCommand`
- [ ] Сборка → `0 Error(s)`
- [ ] Коммит: `feat(VM): wire CrossSection wizard commands`

---

## Task 7: Обновить дерево — контекстные меню мастеров

**Файлы:** `OpenCS/MainWindow.xaml`

- [ ] `FiberSectionsNode`: ContextMenu → `NewFiberSectionCommand`, `EditCrossSectionCommand`, `DeleteCrossSectionCommand`
- [ ] `TwoStageNode`: ContextMenu → `NewTwoStageSectionCommand`, `DeleteCrossSectionCommand`
- [ ] Сборка → `0 Error(s)`
- [ ] Коммит: `feat(UI): update tree context menus for wizard commands`

---

## Task 8: Локализация + финальная проверка

- [ ] Добавить ключи мастеров в `Strings.*.xaml` (`WizardStep1`, `WizardStep2`, `CreateSection`, `Stage1Section`, `FrozenCurvature` и т.д.)
- [ ] `dotnet build OpenCS.sln` → `0 Error(s)`
- [ ] Запуск: создать сечение через wizard, убедиться что области выбираются графически, CrossSectionView отрисовывает сечение
- [ ] Коммит финальный если нужен
