# Блок 1 — Дерево проекта + MaterialAreaPage: План реализации

> **Для агентных воркеров:** используйте `superpowers:executing-plans` или `superpowers:subagent-driven-development`.

**Спек:** `docs/superpowers/specs/2026-06-09-block1-tree-materialarea-design.md`  
**Команда сборки:** `dotnet build OpenCS.sln` → ожидается `0 Error(s)`

---

## Task 1: Добавить `AreaCategory` в `CScore/MaterialArea.cs`

**Файлы:** `CScore/MaterialArea.cs`

- [ ] Добавить enum перед классом:
```csharp
public enum AreaCategory { Region, RebarGroup, SingleBar }
```
- [ ] Добавить свойство в класс:
```csharp
public AreaCategory Category { get; set; } = AreaCategory.Region;
```
- [ ] Сборка → `0 Error(s)`
- [ ] Коммит: `feat(CScore): add AreaCategory enum to MaterialArea`

---

## Task 2: Обновить DB-схему в `DatabaseService`

**Файлы:** `OpenCS/Utilites/DatabaseService.cs`

- [ ] В методе `CreateCrossSectionTables()` изменить `material_areas`:
  - `section_id INTEGER NOT NULL` → `section_id INTEGER` (убрать NOT NULL и REFERENCES ... ON DELETE CASCADE, оставить просто `INTEGER`)
  - Добавить колонку: `category TEXT NOT NULL DEFAULT 'region'`
- [ ] Добавить `public ObservableCollection<MaterialArea> MaterialAreas { get; } = [];` в поля класса
- [ ] Добавить метод `LoadMaterialAreas()`:

```csharp
void LoadMaterialAreas()
{
    MaterialAreas.Clear();
    using var conn = new SqliteConnection(_connectionString);
    conn.Open();
    using var cmd = conn.CreateCommand();
    cmd.CommandText = """
        SELECT id, num, tag, description, material_id,
               host_area_id, diagramm_type, nx, ny, wkt, category
        FROM material_areas
        WHERE section_id IS NULL
        ORDER BY num
    """;
    using var r = cmd.ExecuteReader();
    while (r.Read())
    {
        var area = new MaterialArea
        {
            Id           = r.GetInt32(0),
            Num          = r.GetInt32(1),
            Tag          = r.GetString(2),
            Description  = r.IsDBNull(3) ? null : r.GetString(3),
            MaterialId   = r.IsDBNull(4) ? 0 : r.GetInt32(4),
            HostAreaId   = r.IsDBNull(5) ? null : r.GetInt32(5),
            DiagrammType = Enum.Parse<DiagrammType>(r.GetString(6)),
            NX           = r.GetInt32(7),
            NY           = r.GetInt32(8),
            WKT          = r.IsDBNull(9) ? null : r.GetString(9),
            Category     = Enum.Parse<AreaCategory>(r.GetString(10), ignoreCase: true)
        };
        if (area.WKT != null)
        {
            WktHelper.ParseWKTPolygon(area.WKT,
                out var outerX, out var outerY, out var holeXs, out var holeYs);
            var hull = new Contour(outerX, outerY, "hull") { Type = ContourType.Hull };
            area.Contours.Add(hull);
            if (holeXs != null)
                for (int j = 0; j < holeXs.Count; j++)
                    area.Contours.Add(new Contour(holeXs[j], holeYs[j], $"hole{j}") { Type = ContourType.Hole });
        }
        // point fibers
        MaterialAreas.Add(area);
    }
    // Загрузить point_fibers для standalone-областей
    LoadPointFibersForAreas(MaterialAreas, conn);
}

void LoadPointFibersForAreas(IEnumerable<MaterialArea> areas, SqliteConnection conn)
{
    var areaDict = areas.ToDictionary(a => a.Id);
    if (areaDict.Count == 0) return;
    using var cmd = conn.CreateCommand();
    cmd.CommandText = $"SELECT area_id, x, y, area, diameter, eps_p FROM point_fibers WHERE area_id IN ({string.Join(",", areaDict.Keys)})";
    using var r = cmd.ExecuteReader();
    while (r.Read())
    {
        if (!areaDict.TryGetValue(r.GetInt32(0), out var area)) continue;
        area.Fibers.Add(new Fiber(r.GetDouble(1), r.GetDouble(2))
        {
            Area = r.GetDouble(3), Diameter = r.GetDouble(4),
            Eps_p = r.GetDouble(5), TypeFiber = FiberType.point
        });
    }
}
```

- [ ] Добавить метод `ResolveReferencesForStandaloneAreas()`:
```csharp
void ResolveReferencesForStandaloneAreas()
{
    foreach (var area in MaterialAreas)
    {
        area.Material = Materials.FirstOrDefault(m => m.Id == area.MaterialId);
        if (area.HostAreaId != null)
            area.HostArea = MaterialAreas.FirstOrDefault(a => a.Id == area.HostAreaId);
        area.ResolveAndBuildDiagramms();
    }
}
```

- [ ] Добавить `public void SaveMaterialArea(MaterialArea area)`:
```csharp
public void SaveMaterialArea(MaterialArea area)
{
    using var conn = new SqliteConnection(_connectionString);
    conn.Open();
    using var tx = conn.BeginTransaction();
    bool isNew = area.Id == 0;
    using (var cmd = conn.CreateCommand())
    {
        if (isNew)
        {
            cmd.CommandText = """
                INSERT INTO material_areas
                    (num, tag, description, material_id, host_area_id,
                     diagramm_type, nx, ny, wkt, category)
                VALUES (@num,@tag,@desc,@mid,@hid,@dtype,@nx,@ny,@wkt,@cat);
                SELECT last_insert_rowid();
            """;
        }
        else
        {
            cmd.CommandText = """
                UPDATE material_areas SET
                    num=@num, tag=@tag, description=@desc, material_id=@mid,
                    host_area_id=@hid, diagramm_type=@dtype, nx=@nx, ny=@ny,
                    wkt=@wkt, category=@cat
                WHERE id=@id;
            """;
            cmd.Parameters.AddWithValue("@id", area.Id);
        }
        cmd.Parameters.AddWithValue("@num",   area.Num);
        cmd.Parameters.AddWithValue("@tag",   area.Tag);
        cmd.Parameters.AddWithValue("@desc",  (object?)area.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@mid",   area.MaterialId == 0 ? DBNull.Value : area.MaterialId);
        cmd.Parameters.AddWithValue("@hid",   (object?)area.HostAreaId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@dtype", area.DiagrammType.ToString());
        cmd.Parameters.AddWithValue("@nx",    area.NX);
        cmd.Parameters.AddWithValue("@ny",    area.NY);
        cmd.Parameters.AddWithValue("@wkt",   (object?)area.WKT ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@cat",   area.Category.ToString().ToLowerInvariant());
        if (isNew) area.Id = (int)(long)cmd.ExecuteScalar()!;
        else cmd.ExecuteNonQuery();
    }
    // Пересохранить point_fibers
    using (var cmd = conn.CreateCommand())
    {
        cmd.CommandText = "DELETE FROM point_fibers WHERE area_id = @aid";
        cmd.Parameters.AddWithValue("@aid", area.Id);
        cmd.ExecuteNonQuery();
    }
    foreach (var f in area.Fibers.Where(f => f.TypeFiber == FiberType.point))
    {
        using var fc = conn.CreateCommand();
        fc.CommandText = "INSERT INTO point_fibers(area_id,x,y,area,diameter,eps_p) VALUES(@aid,@x,@y,@a,@d,@ep)";
        fc.Parameters.AddWithValue("@aid", area.Id);
        fc.Parameters.AddWithValue("@x",   f.X);
        fc.Parameters.AddWithValue("@y",   f.Y);
        fc.Parameters.AddWithValue("@a",   f.Area);
        fc.Parameters.AddWithValue("@d",   f.Diameter);
        fc.Parameters.AddWithValue("@ep",  f.Eps_p);
        fc.ExecuteNonQuery();
    }
    tx.Commit();
    if (isNew && !MaterialAreas.Contains(area))
        MaterialAreas.Add(area);
}
```

- [ ] Добавить `public void DeleteMaterialArea(MaterialArea area)`:
```csharp
public void DeleteMaterialArea(MaterialArea area)
{
    if (area.Id == 0) { MaterialAreas.Remove(area); return; }
    using var conn = new SqliteConnection(_connectionString);
    conn.Open();
    using var cmd = conn.CreateCommand();
    cmd.CommandText = "DELETE FROM material_areas WHERE id = @id";
    cmd.Parameters.AddWithValue("@id", area.Id);
    cmd.ExecuteNonQuery();
    MaterialAreas.Remove(area);
}
```

- [ ] В `LoadAll()` добавить после `LoadCrossSections(); ResolveReferencesForCrossSections();`:
```csharp
LoadMaterialAreas();
ResolveReferencesForStandaloneAreas();
```

- [ ] Сборка → `0 Error(s)`
- [ ] Коммит: `feat(DB): standalone MaterialAreas — LoadMaterialAreas, Save, Delete`

---

## Task 3: Расширить `MaterialAreaVM`

**Файлы:** `OpenCS/ViewModels/MaterialAreaVM.cs`

- [ ] Добавить свойства и команды согласно спеку (AreaCategory, NX, NY, ProjectContours, Hull, PlotElements)
- [ ] Реализовать `RefreshPlot()`:
```csharp
public void RefreshPlot()
{
    var elements = new List<PlotElement>();
    var hull = _model.Hull;
    if (hull != null)
    {
        var fill = App.GetBrushForMaterialType(_model.Material?.Type ?? MatType.None);
        elements.Add(PlotElement.FilledPolygon(hull.X.ToArray(), hull.Y.ToArray(), fill, opacity: 0.5));
    }
    foreach (var hole in _model.Holes)
        elements.Add(PlotElement.Polygon(hole.X.ToArray(), hole.Y.ToArray(), Colors.White, dashed: true));
    foreach (var f in _model.Fibers.Where(f => f.TypeFiber == FiberType.point))
        elements.Add(PlotElement.FilledCircle(f.X, f.Y, f.Diameter / 2, Colors.Orange));
    PlotElements = elements;
    OnPropertyChanged(nameof(PlotElements));
}
```
- [ ] Реализовать `SetHullFromPoolCommand`: при выборе контура из ComboBox вызвать `_model.Hull = selected; _model.SetWKT(); RefreshPlot();`
- [ ] Реализовать `ClearHullCommand`, `AddHoleCommand`, `RemoveHoleCommand`
- [ ] Сборка → `0 Error(s)`
- [ ] Коммит: `feat(VM): extend MaterialAreaVM — geometry, plot, hull/hole commands`

---

## Task 4: Обновить `AppViewModel`

**Файлы:** `OpenCS/AppViewModel.cs`

- [ ] Добавить поля и свойства:
  - `public ObservableCollection<MaterialArea> MaterialAreas { get; set; }`
  - `public ObservableCollection<MaterialArea> AreasLive { get; set; }`
  - `public ObservableCollection<MaterialArea> RebarGroupsLive { get; set; }`
  - `public ObservableCollection<MaterialArea> SingleBarsLive { get; set; }`
  - `MaterialArea? currentMaterialArea;`
  - `public MaterialArea? CurrentMaterialArea { get; set; }` — setter открывает `MaterialAreaPage`
  - `public ICommand NewAreaCommand { get; set; }`
  - `public ICommand DeleteMaterialAreaCommand { get; set; }`
- [ ] В конструкторе после загрузки DB:
```csharp
MaterialAreas = db.MaterialAreas;
RefreshMaterialAreaLiveCollections();
MaterialAreas.CollectionChanged += (_, _) => { RefreshMaterialAreaLiveCollections(); IsDirty = true; };
NewAreaCommand = new RelayCommand(_ => NewArea());
DeleteMaterialAreaCommand = new RelayCommand(_ => DeleteMaterialArea());
```
- [ ] Добавить метод:
```csharp
void RefreshMaterialAreaLiveCollections()
{
    AreasLive      = new(MaterialAreas.Where(a => a.Category == AreaCategory.Region));
    RebarGroupsLive = new(MaterialAreas.Where(a => a.Category == AreaCategory.RebarGroup));
    SingleBarsLive  = new(MaterialAreas.Where(a => a.Category == AreaCategory.SingleBar));
    OnPropertyChanged(nameof(AreasLive));
    OnPropertyChanged(nameof(RebarGroupsLive));
    OnPropertyChanged(nameof(SingleBarsLive));
}
```
- [ ] Добавить `FiberSectionsLive` и `TwoStageSectionsLive`:
```csharp
public ObservableCollection<CrossSection> FiberSectionsLive { get; set; }
public ObservableCollection<CrossSection> TwoStageSectionsLive { get; set; }

void RefreshSectionLiveCollections()
{
    FiberSectionsLive    = new(CrossSections.Where(s => s is not TwoStageSection));
    TwoStageSectionsLive = new(CrossSections.OfType<TwoStageSection>());
    OnPropertyChanged(nameof(FiberSectionsLive));
    OnPropertyChanged(nameof(TwoStageSectionsLive));
}
```
- [ ] В методах `NewProject` / `LoadProject`: перестраивать Live-коллекции после загрузки
- [ ] В `SaveProject()`: добавить `foreach (var a in MaterialAreas) db.SaveMaterialArea(a);`
- [ ] Сборка → `0 Error(s)`
- [ ] Коммит: `feat(VM): add MaterialAreas collection and Live collections to AppViewModel`

---

## Task 5: Создать `MaterialAreaPage`

**Файлы:** создать `OpenCS/Views/MaterialAreaPage.xaml` + `.xaml.cs`

- [ ] XAML: двухколоночный Grid (300px + *)
  - Левая: TextBox Tag, ComboBox Material (из `App.Materials`), ComboBox DiagrammType, секция Hull (ComboBox + Button «Новый» + Button «Очистить»), секция Holes (ItemsControl + кнопка добавить), NX/NY TextBox, Button «Сохранить», Button «Удалить»
  - Правая: `<local:PlotCanvas x:Name="preview"/>` — привязка к `PlotElements`
- [ ] Code-behind: `DataContext = new MaterialAreaVM(area, app)`; подписаться на `vm.PropertyChanged` по `PlotElements` → вызывать `preview.SetElements(vm.PlotElements)`
- [ ] Кнопка «Новый контур» создаёт пустой Contour через `app.db.AddContour(new Contour(...))` и открывает `app.CurrentPage = new ContourPlot(...)` — возврат через дерево
- [ ] Сборка → `0 Error(s)`
- [ ] Коммит: `feat(UI): add MaterialAreaPage with geometry editor and PlotCanvas preview`

---

## Task 6: Реструктурировать дерево (`MainWindow.xaml` + `.xaml.cs`)

**Файлы:** `OpenCS/MainWindow.xaml`, `OpenCS/MainWindow.xaml.cs`

- [ ] **RegionsNode**: удалить все старые дочерние `TreeViewItem` (RcFiberRegionsLive, RebarGroupsLive, Fibrous, Solid). Добавить три новых:
  ```xml
  <TreeViewItem Header="{DynamicResource Areas}" ItemsSource="{Binding AreasLive}">
    <!-- ItemTemplate: Path иконка + TextBlock Tag -->
    <!-- ContextMenu: NewArea, DeleteArea -->
  </TreeViewItem>
  <TreeViewItem Header="{DynamicResource RebarGroups}" ItemsSource="{Binding RebarGroupsLive}">
    <!-- ItemTemplate: аналогично -->
    <!-- ContextMenu: NewRebarGroup (stub), DeleteArea -->
  </TreeViewItem>
  <TreeViewItem Header="{DynamicResource SingleBars}" ItemsSource="{Binding SingleBarsLive}">
    <!-- ContextMenu: NewSingleBar (stub), DeleteArea -->
  </TreeViewItem>
  ```
- [ ] **crosssectNode**: убрать `ItemsSource`. Добавить два дочерних:
  ```xml
  <TreeViewItem Header="{DynamicResource FiberSections}" ItemsSource="{Binding FiberSectionsLive}">
    <!-- HierarchicalDataTemplate для CrossSection → Areas -->
    <!-- ContextMenu: NewCrossSectionCommand -->
  </TreeViewItem>
  <TreeViewItem Header="{DynamicResource TwoStageSections}" ItemsSource="{Binding TwoStageSectionsLive}">
    <!-- ContextMenu: NewTwoStageSectionCommand (stub) -->
  </TreeViewItem>
  ```
- [ ] `SelectedItemChanged` — добавить case:
  ```csharp
  case MaterialArea areaItem:
      vm.CurrentMaterialArea = areaItem;
      break;
  ```
- [ ] Сборка → `0 Error(s)`
- [ ] Коммит: `feat(UI): restructure TreeView — MaterialAreas (3 nodes) + CrossSections (2 nodes)`

---

## Task 7: Локализация

**Файлы:** `OpenCS/Resources/Strings.ru-RU.xaml`, `OpenCS/Resources/Strings.en-US.xaml`

- [ ] Добавить все ключи из спека раздел 5
- [ ] Сборка → `0 Error(s)`
- [ ] Коммит: `feat(i18n): add Block 1 localization keys`

---

## Task 8: Финальная проверка

- [ ] `dotnet build OpenCS.sln` → `0 Error(s)`
- [ ] Запустить приложение: дерево отображается корректно, выбор MaterialArea открывает MaterialAreaPage, Hull/Hole выбираются из пула, PlotCanvas отображает контур
- [ ] Создать новую область → сохранить → перезапустить → убедиться что загружается из БД
- [ ] Коммит итоговый если есть незакоммиченные изменения
