# Mesh Generation for MaterialArea — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Добавить генерацию сетки фибр (Grid / Ruppert / AdvancingFront) для MaterialArea с хранением в БД, диалогом настройки и предпросмотром на PlotCanvas.

**Architecture:** Три метода генерации (SliceXY, Ruppert, AdvancingFront) уже реализованы в `CScore/Geo.cs`. Новые данные хранятся в таблице `mesh_fibers` (schema v4). Диалог `MeshDialog` открывается из `MaterialAreaVM`, работает напрямую с объектом `MaterialArea`, делает снимок Fibers при открытии — восстанавливает при отмене, сохраняет в БД при применении.

**Tech Stack:** WPF (.NET 9), SQLite (Microsoft.Data.Sqlite), CScore/Geo.cs (Ruppert + AdvancingFront + GridSplit), PlotCanvas (custom DrawingContext renderer)

---

## File Map

| Действие | Файл |
|---|---|
| Изменить | `CScore/MaterialArea.cs` — `MeshMethod` enum, 3 новых свойства, исправить `SliceXY()`/`Triangulate()` |
| Изменить | `OpenCS/Utilites/DatabaseService.cs` — migration v4, `SaveMeshFibers`, `LoadMeshFibersForAreas`, расширить `LoadMaterialAreas`/`SaveMaterialArea` |
| Изменить | `OpenCS/Views/PlotElement.cs` — добавить `FiberMeshElement` |
| Изменить | `OpenCS/Resources/Strings.ru-RU.xaml` — новые ключи |
| Изменить | `OpenCS/Resources/Strings.en-US.xaml` — новые ключи |
| Изменить | `OpenCS/ViewModels/MaterialAreaVM.cs` — `FiberDisplayMode` enum, свойства, команды, `RefreshPlot()` |
| Создать  | `OpenCS/ViewModels/MeshDialogVM.cs` |
| Создать  | `OpenCS/Views/MeshDialog.xaml` |
| Создать  | `OpenCS/Views/MeshDialog.xaml.cs` |
| Изменить | `OpenCS/Views/MaterialAreaPage.xaml` — заменить GroupBox «Сетка» |

---

## Task 1: MeshMethod enum + MaterialArea properties

**Files:**
- Modify: `CScore/MaterialArea.cs`

- [ ] **Step 1: Добавить `using CSTriangulation;` в начало файла**

Открыть `CScore/MaterialArea.cs`, добавить после `using System.Text.Json.Serialization;`:

```csharp
using CSTriangulation;
```

- [ ] **Step 2: Добавить `MeshMethod` enum перед классом `MaterialArea`**

Добавить после строки `public enum AreaCategory { Region, RebarGroup, SingleBar }`:

```csharp
/// <summary>Метод генерации сетки фибр.</summary>
public enum MeshMethod { Grid, Ruppert, AdvancingFront }
```

- [ ] **Step 3: Добавить три новых свойства в класс `MaterialArea`**

После свойства `public AreaCategory Category { get; set; } = AreaCategory.Region;` добавить:

```csharp
/// <summary>Метод генерации сетки фибр.</summary>
public MeshMethod MeshMethod { get; set; } = MeshMethod.Grid;

/// <summary>Максимальная площадь треугольника (доля от площади области).</summary>
public double MeshMaxArea { get; set; } = 0.01;

/// <summary>Минимальный угол треугольника для метода Рупперта, градусы.</summary>
public double MeshMinAngle { get; set; } = 30.0;
```

- [ ] **Step 4: Исправить `SliceXY()` — сохранить точечные волокна**

Найти метод `SliceXY(int nx = 0, int ny = 0)` (~строка 245) и заменить:

```csharp
// БЫЛО:
public void SliceXY(int nx = 0, int ny = 0)
{
    int usedNx = nx > 0 ? nx : NX;
    int usedNy = ny > 0 ? ny : NY;
    Fiber[] res = GridSplit.SliceXY(this, usedNx, usedNy);
    Fibers = [.. res];
}
```

на:

```csharp
/// <summary>Разбивает область на волокна прямоугольной сеткой, сохраняя точечные волокна.</summary>
public void SliceXY(int nx = 0, int ny = 0)
{
    int usedNx = nx > 0 ? nx : NX;
    int usedNy = ny > 0 ? ny : NY;
    Fiber[] res = GridSplit.SliceXY(this, usedNx, usedNy);
    var points = Fibers.Where(f => f.TypeFiber == FiberType.point).ToList();
    Fibers = [.. res, .. points];
}
```

- [ ] **Step 5: Исправить `Triangulate()` — поддержать метод + сохранить точечные волокна**

Найти метод `Triangulate(double maxTrgArea = 0.01, double maxAngl = 30)` (~строка 238) и заменить:

```csharp
// БЫЛО:
/// <summary>Разбивает область на волокна методом триангуляции.</summary>
public void Triangulate(double maxTrgArea = 0.01, double maxAngl = 30)
{
    Fiber[] res = Geo.Triangulation(this, maxTrgArea, maxAngl);
    Fibers = [.. res];
}
```

на:

```csharp
/// <summary>Разбивает область на волокна методом триангуляции, сохраняя точечные волокна.</summary>
public void Triangulate(double maxTrgArea = 0.01, double maxAngl = 30,
    MeshMethod method = MeshMethod.Ruppert)
{
    var triMethod = method == MeshMethod.AdvancingFront
        ? TriangulationMethod.AdvancingFront
        : TriangulationMethod.Ruppert;
    Fiber[] res = Geo.Triangulation(this, maxTrgArea, maxAngl, method: triMethod);
    var points = Fibers.Where(f => f.TypeFiber == FiberType.point).ToList();
    Fibers = [.. res, .. points];
}
```

- [ ] **Step 6: Сборка**

```
dotnet build OpenCS.sln
```

Ожидается: Build succeeded, 0 Error(s).

- [ ] **Step 7: Коммит**

```
git add CScore/MaterialArea.cs
git commit -m "feat(CScore): MeshMethod enum + MaterialArea mesh properties + fix SliceXY/Triangulate preserve points"
```

---

## Task 2: DB migration v4

**Files:**
- Modify: `OpenCS/Utilites/DatabaseService.cs`

- [ ] **Step 1: Добавить таблицу `mesh_fibers` в `EnsureCreated()`**

В методе `EnsureCreated()`, после строки с закрывающим `");` последней таблицы (`point_fibers`), добавить перед `cmd.ExecuteNonQuery()`:

Найти в строке около 183:
```csharp
                diameter    REAL NOT NULL DEFAULT 0,
                eps_p       REAL NOT NULL DEFAULT 0
            );";
         cmd.ExecuteNonQuery();
```

Изменить на:

```csharp
                diameter    REAL NOT NULL DEFAULT 0,
                eps_p       REAL NOT NULL DEFAULT 0
            );
            CREATE TABLE IF NOT EXISTS mesh_fibers (
                id      INTEGER PRIMARY KEY AUTOINCREMENT,
                area_id INTEGER NOT NULL REFERENCES material_areas(id) ON DELETE CASCADE,
                type    TEXT NOT NULL DEFAULT 'poly',
                x       REAL NOT NULL DEFAULT 0,
                y       REAL NOT NULL DEFAULT 0,
                area    REAL NOT NULL DEFAULT 0,
                wkt     TEXT,
                eps_p   REAL NOT NULL DEFAULT 0
            );";
         cmd.ExecuteNonQuery();
```

- [ ] **Step 2: Добавить v4 миграцию в массив `Migrations`**

Найти `Migrations` (строка ~30):

```csharp
         """
         -- v3: добавить pool_contour_id для связи standalone-области с контуром из пула.
         ALTER TABLE material_areas ADD COLUMN pool_contour_id INTEGER REFERENCES contours(id);
         """
```

Добавить после него (перед `];`):

```csharp
         ,
         """
         -- v4: поля параметров сетки в material_areas + таблица mesh_fibers.
         ALTER TABLE material_areas ADD COLUMN mesh_method    TEXT NOT NULL DEFAULT 'grid';
         ALTER TABLE material_areas ADD COLUMN mesh_max_area  REAL NOT NULL DEFAULT 0.01;
         ALTER TABLE material_areas ADD COLUMN mesh_min_angle REAL NOT NULL DEFAULT 30.0;
         CREATE TABLE IF NOT EXISTS mesh_fibers (
             id      INTEGER PRIMARY KEY AUTOINCREMENT,
             area_id INTEGER NOT NULL REFERENCES material_areas(id) ON DELETE CASCADE,
             type    TEXT NOT NULL DEFAULT 'poly',
             x       REAL NOT NULL DEFAULT 0,
             y       REAL NOT NULL DEFAULT 0,
             area    REAL NOT NULL DEFAULT 0,
             wkt     TEXT,
             eps_p   REAL NOT NULL DEFAULT 0
         );
         """
```

- [ ] **Step 3: Поднять `CurrentSchemaVersion` до 4**

Найти строку:
```csharp
      const int CurrentSchemaVersion = 3;
```

Изменить на:
```csharp
      const int CurrentSchemaVersion = 4;
```

- [ ] **Step 4: Сборка**

```
dotnet build OpenCS.sln
```

Ожидается: Build succeeded, 0 Error(s).

- [ ] **Step 5: Коммит**

```
git add OpenCS/Utilites/DatabaseService.cs
git commit -m "feat(DB): schema v4 — mesh_fibers table + mesh params columns in material_areas"
```

---

## Task 3: DatabaseService mesh CRUD

**Files:**
- Modify: `OpenCS/Utilites/DatabaseService.cs`

- [ ] **Step 1: Расширить SELECT в `LoadMaterialAreas()`**

Найти запрос (строка ~558):
```csharp
         cmd.CommandText = """
            SELECT id, num, tag, description, material_id,
                   host_area_id, diagramm_type, nx, ny, wkt, category, pool_contour_id
            FROM material_areas
            WHERE section_id IS NULL
            ORDER BY num
         """;
```

Изменить на:
```csharp
         cmd.CommandText = """
            SELECT id, num, tag, description, material_id,
                   host_area_id, diagramm_type, nx, ny, wkt, category, pool_contour_id,
                   mesh_method, mesh_max_area, mesh_min_angle
            FROM material_areas
            WHERE section_id IS NULL
            ORDER BY num
         """;
```

- [ ] **Step 2: Добавить чтение новых колонок при создании `MaterialArea` в `LoadMaterialAreas()`**

Найти блок создания area (~строка 568):
```csharp
            var area = new MaterialArea
            {
               Id           = r.GetInt32(0),
               ...
               PoolContourId = r.IsDBNull(11) ? null : r.GetInt32(11)
            };
```

Изменить на:
```csharp
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
               Category      = Enum.TryParse<AreaCategory>(r.GetString(10), ignoreCase: true, out var cat) ? cat : AreaCategory.Region,
               PoolContourId = r.IsDBNull(11) ? null : r.GetInt32(11),
               MeshMethod    = Enum.TryParse<CScore.MeshMethod>(r.IsDBNull(12) ? "grid" : r.GetString(12), ignoreCase: true, out var mm) ? mm : CScore.MeshMethod.Grid,
               MeshMaxArea   = r.IsDBNull(13) ? 0.01 : r.GetDouble(13),
               MeshMinAngle  = r.IsDBNull(14) ? 30.0 : r.GetDouble(14)
            };
```

- [ ] **Step 3: Добавить вызов `LoadMeshFibersForAreas` в `LoadMaterialAreas()`**

Найти строку (около 596):
```csharp
         LoadPointFibersForAreas(MaterialAreas, conn);
```

Добавить после неё:
```csharp
         LoadMeshFibersForAreas(MaterialAreas, conn);
```

- [ ] **Step 4: Добавить метод `LoadMeshFibersForAreas()`**

После метода `LoadPointFibersForAreas` добавить новый приватный метод:

```csharp
      void LoadMeshFibersForAreas(System.Collections.Generic.IEnumerable<MaterialArea> areas, SqliteConnection conn)
      {
         var dict = new System.Collections.Generic.Dictionary<int, MaterialArea>();
         foreach (var a in areas) dict[a.Id] = a;
         if (dict.Count == 0) return;
         using var cmd = conn.CreateCommand();
         cmd.CommandText = $"SELECT area_id, type, x, y, area, wkt, eps_p FROM mesh_fibers WHERE area_id IN ({string.Join(",", dict.Keys)})";
         using var r = cmd.ExecuteReader();
         while (r.Read())
         {
            if (!dict.TryGetValue(r.GetInt32(0), out var area)) continue;
            var fiber = new Fiber(r.GetDouble(2), r.GetDouble(3))
            {
               TypeFiber = Enum.TryParse<FiberType>(r.GetString(1), out var ft) ? ft : FiberType.poly,
               Area      = r.GetDouble(4),
               WKT       = r.IsDBNull(5) ? null : r.GetString(5),
               Eps_p     = r.GetDouble(6)
            };
            area.Fibers.Add(fiber);
         }
      }
```

- [ ] **Step 5: Расширить `SaveMaterialArea()` — INSERT**

Найти INSERT в `SaveMaterialArea()` (~строка 648):
```csharp
               cmd.CommandText = """
                  INSERT INTO material_areas
                     (num, tag, description, material_id, host_area_id,
                      diagramm_type, nx, ny, wkt, category, pool_contour_id)
                  VALUES (@num,@tag,@desc,@mid,@hid,@dtype,@nx,@ny,@wkt,@cat,@pcid);
                  SELECT last_insert_rowid();
               """;
```

Изменить на:
```csharp
               cmd.CommandText = """
                  INSERT INTO material_areas
                     (num, tag, description, material_id, host_area_id,
                      diagramm_type, nx, ny, wkt, category, pool_contour_id,
                      mesh_method, mesh_max_area, mesh_min_angle)
                  VALUES (@num,@tag,@desc,@mid,@hid,@dtype,@nx,@ny,@wkt,@cat,@pcid,
                          @mmethod,@mmaxarea,@mminangle);
                  SELECT last_insert_rowid();
               """;
```

- [ ] **Step 6: Расширить `SaveMaterialArea()` — UPDATE**

Найти UPDATE в `SaveMaterialArea()` (~строка 658):
```csharp
               cmd.CommandText = """
                  UPDATE material_areas SET
                     num=@num, tag=@tag, description=@desc, material_id=@mid,
                     host_area_id=@hid, diagramm_type=@dtype, nx=@nx, ny=@ny,
                     wkt=@wkt, category=@cat, pool_contour_id=@pcid
                  WHERE id=@id;
               """;
```

Изменить на:
```csharp
               cmd.CommandText = """
                  UPDATE material_areas SET
                     num=@num, tag=@tag, description=@desc, material_id=@mid,
                     host_area_id=@hid, diagramm_type=@dtype, nx=@nx, ny=@ny,
                     wkt=@wkt, category=@cat, pool_contour_id=@pcid,
                     mesh_method=@mmethod, mesh_max_area=@mmaxarea, mesh_min_angle=@mminangle
                  WHERE id=@id;
               """;
```

- [ ] **Step 7: Добавить параметры `@mmethod`, `@mmaxarea`, `@mminangle` в блок параметров `SaveMaterialArea()`**

Найти строки (~строка 676):
```csharp
            cmd.Parameters.AddWithValue("@pcid",  (object?)area.PoolContourId ?? DBNull.Value);
            if (isNew) area.Id = (int)(long)cmd.ExecuteScalar()!;
```

Добавить перед `if (isNew)`:
```csharp
            cmd.Parameters.AddWithValue("@mmethod",    area.MeshMethod.ToString().ToLowerInvariant());
            cmd.Parameters.AddWithValue("@mmaxarea",   area.MeshMaxArea);
            cmd.Parameters.AddWithValue("@mminangle",  area.MeshMinAngle);
```

- [ ] **Step 8: Добавить публичный метод `SaveMeshFibers()`**

После метода `DeleteMaterialArea` добавить:

```csharp
      /// <summary>
      /// Сохраняет сеточные волокна (poly/tri) области: обновляет параметры сетки
      /// в material_areas, удаляет старые записи mesh_fibers, добавляет новые.
      /// </summary>
      public void SaveMeshFibers(MaterialArea area)
      {
         if (area.Id == 0) return;
         using var conn = new SqliteConnection($"Data Source={_dataSource}");
         conn.Open();
         using var tx = conn.BeginTransaction();

         using (var cmd = conn.CreateCommand())
         {
            cmd.CommandText = """
               UPDATE material_areas
               SET mesh_method=@mm, mesh_max_area=@ma, mesh_min_angle=@mi
               WHERE id=@id
            """;
            cmd.Parameters.AddWithValue("@id", area.Id);
            cmd.Parameters.AddWithValue("@mm", area.MeshMethod.ToString().ToLowerInvariant());
            cmd.Parameters.AddWithValue("@ma", area.MeshMaxArea);
            cmd.Parameters.AddWithValue("@mi", area.MeshMinAngle);
            cmd.ExecuteNonQuery();
         }

         using (var cmd = conn.CreateCommand())
         {
            cmd.CommandText = "DELETE FROM mesh_fibers WHERE area_id=@aid";
            cmd.Parameters.AddWithValue("@aid", area.Id);
            cmd.ExecuteNonQuery();
         }

         foreach (var f in area.Fibers.Where(f => f.TypeFiber is FiberType.poly or FiberType.tri))
         {
            using var fc = conn.CreateCommand();
            fc.CommandText = "INSERT INTO mesh_fibers(area_id,type,x,y,area,wkt,eps_p) VALUES(@aid,@t,@x,@y,@a,@wkt,@ep)";
            fc.Parameters.AddWithValue("@aid", area.Id);
            fc.Parameters.AddWithValue("@t",   f.TypeFiber.ToString());
            fc.Parameters.AddWithValue("@x",   f.X);
            fc.Parameters.AddWithValue("@y",   f.Y);
            fc.Parameters.AddWithValue("@a",   f.Area);
            fc.Parameters.AddWithValue("@wkt", (object?)f.WKT ?? DBNull.Value);
            fc.Parameters.AddWithValue("@ep",  f.Eps_p);
            fc.ExecuteNonQuery();
         }

         tx.Commit();
      }
```

- [ ] **Step 9: Сборка**

```
dotnet build OpenCS.sln
```

Ожидается: Build succeeded, 0 Error(s).

- [ ] **Step 10: Коммит**

```
git add OpenCS/Utilites/DatabaseService.cs
git commit -m "feat(DB): LoadMeshFibersForAreas + SaveMeshFibers + extend Load/Save for mesh columns"
```

---

## Task 4: FiberMeshElement

**Files:**
- Modify: `OpenCS/Views/PlotElement.cs`

- [ ] **Step 1: Добавить `using CScore;` если ещё нет**

Открыть `OpenCS/Views/PlotElement.cs`. В начале файла должны быть `using System;`, `using System.Windows;`, `using System.Windows.Media;`. Добавить при необходимости:

```csharp
using CScore;
```

- [ ] **Step 2: Добавить класс `FiberMeshElement` в конец файла**

Добавить перед последней `}` файла (до закрытия namespace):

```csharp
   /// <summary>
   /// Элемент рендеринга сетки фибр. Все фибры рисуются одним StreamGeometry
   /// (режим Elements) или как маркеры-точки (режим Centroids).
   /// </summary>
   public record FiberMeshElement : PlotElement
   {
      public Fiber[] Fibers          { get; init; } = [];
      public bool ShowCentroids      { get; init; } = false;
      public Brush Fill              { get; init; } = Brushes.LightSteelBlue;
      public Brush Stroke            { get; init; } = Brushes.SteelBlue;
      public double StrokeThickness  { get; init; } = 0.5;
      public double MarkerSize       { get; init; } = 3;

      public override void Render(DrawingContext dc, Func<double, double, Point> toPixel)
      {
         if (Fibers.Length == 0) return;

         if (ShowCentroids)
         {
            double half = MarkerSize / 2.0;
            foreach (var f in Fibers)
            {
               var pt = toPixel(f.X, f.Y);
               dc.DrawEllipse(Fill, null, pt, half, half);
            }
         }
         else
         {
            var stream = new StreamGeometry();
            using (var ctx = stream.Open())
            {
               foreach (var f in Fibers)
               {
                  if (f.WKT == null) continue;
                  WktHelper.ParseWKTPolygon(f.WKT, out var xs, out var ys, out _, out _);
                  if (xs.Count < 3) continue;
                  ctx.BeginFigure(toPixel(xs[0], ys[0]), isFilled: true, isClosed: true);
                  for (int i = 1; i < xs.Count; i++)
                     ctx.LineTo(toPixel(xs[i], ys[i]), isStroked: true, isSmoothJoin: false);
               }
            }
            stream.Freeze();
            dc.DrawGeometry(Fill, new Pen(Stroke, StrokeThickness), stream);
         }
      }
   }
```

- [ ] **Step 3: Добавить `using CScore;` в начало PlotElement.cs**

В начале файла перед `using System;` добавить:
```csharp
using CScore;
```

- [ ] **Step 4: Сборка**

```
dotnet build OpenCS.sln
```

Ожидается: Build succeeded, 0 Error(s).

- [ ] **Step 5: Коммит**

```
git add OpenCS/Views/PlotElement.cs
git commit -m "feat(UI): FiberMeshElement — single StreamGeometry renderer for poly/tri fibers"
```

---

## Task 5: Localization strings

**Files:**
- Modify: `OpenCS/Resources/Strings.ru-RU.xaml`
- Modify: `OpenCS/Resources/Strings.en-US.xaml`

- [ ] **Step 1: Добавить ключи в `Strings.ru-RU.xaml`**

Найти конец файла (последний `</ResourceDictionary>`) и добавить перед ним:

```xml
   <!-- Mesh generation -->
   <system:String x:Key="MeshGrid">Сетка</system:String>
   <system:String x:Key="MeshMethod">Метод</system:String>
   <system:String x:Key="MeshMethod_Grid">Ортогональная сетка</system:String>
   <system:String x:Key="MeshMethod_Ruppert">Триангуляция Рупперта</system:String>
   <system:String x:Key="MeshMethod_AdvancingFront">Фронтальная триангуляция</system:String>
   <system:String x:Key="MeshGenerate">Разбить</system:String>
   <system:String x:Key="MeshApply">Применить</system:String>
   <system:String x:Key="MeshClear">Очистить</system:String>
   <system:String x:Key="MeshConfigure">Настроить...</system:String>
   <system:String x:Key="MeshFibersCount">Фибр: </system:String>
   <system:String x:Key="MeshDisplayMode">Отображение</system:String>
   <system:String x:Key="MeshDisplayCentroids">Центроиды</system:String>
   <system:String x:Key="MeshDisplayElements">Элементы</system:String>
   <system:String x:Key="MeshMaxArea">Макс. площадь</system:String>
   <system:String x:Key="MeshMinAngle">Мин. угол, °</system:String>
```

- [ ] **Step 2: Добавить ключи в `Strings.en-US.xaml`**

Найти конец файла и добавить перед `</ResourceDictionary>`:

```xml
   <!-- Mesh generation -->
   <system:String x:Key="MeshGrid">Mesh</system:String>
   <system:String x:Key="MeshMethod">Method</system:String>
   <system:String x:Key="MeshMethod_Grid">Orthogonal Grid</system:String>
   <system:String x:Key="MeshMethod_Ruppert">Ruppert Triangulation</system:String>
   <system:String x:Key="MeshMethod_AdvancingFront">Advancing Front</system:String>
   <system:String x:Key="MeshGenerate">Generate</system:String>
   <system:String x:Key="MeshApply">Apply</system:String>
   <system:String x:Key="MeshClear">Clear Mesh</system:String>
   <system:String x:Key="MeshConfigure">Configure...</system:String>
   <system:String x:Key="MeshFibersCount">Fibers: </system:String>
   <system:String x:Key="MeshDisplayMode">Display</system:String>
   <system:String x:Key="MeshDisplayCentroids">Centroids</system:String>
   <system:String x:Key="MeshDisplayElements">Elements</system:String>
   <system:String x:Key="MeshMaxArea">Max area</system:String>
   <system:String x:Key="MeshMinAngle">Min angle, °</system:String>
```

- [ ] **Step 3: Сборка**

```
dotnet build OpenCS.sln
```

Ожидается: Build succeeded, 0 Error(s).

- [ ] **Step 4: Коммит**

```
git add OpenCS/Resources/Strings.ru-RU.xaml OpenCS/Resources/Strings.en-US.xaml
git commit -m "feat(i18n): mesh generation localization keys (RU + EN)"
```

---

## Task 6: FiberDisplayMode enum + MaterialAreaVM extensions

**Files:**
- Modify: `OpenCS/ViewModels/MaterialAreaVM.cs`

- [ ] **Step 1: Добавить `FiberDisplayMode` enum перед классом**

В начале файла после `using System.Windows.Media;` добавить enum:

```csharp
/// <summary>Режим отображения сетки фибр на превью.</summary>
public enum FiberDisplayMode { Centroids, Elements }
```

- [ ] **Step 2: Добавить поле `_fiberDisplayMode` и свойства в класс**

После поля `readonly MaterialArea _model;` добавить:

```csharp
      FiberDisplayMode _fiberDisplayMode = FiberDisplayMode.Elements;
```

После свойства `public IReadOnlyList<PlotElement> PlotElements { get; private set; } = [];` добавить:

```csharp
      public FiberDisplayMode FiberDisplayMode
      {
         get => _fiberDisplayMode;
         set { _fiberDisplayMode = value; OnPropertyChanged(); RefreshPlot(); }
      }

      public int FibersCount
         => _model.Fibers.Count(f => f.TypeFiber is FiberType.poly or FiberType.tri);

      public ICommand OpenMeshDialogCommand { get; }
      public ICommand ClearMeshCommand { get; }
```

- [ ] **Step 3: Добавить инициализацию команд в конструктор**

В конструкторе `MaterialAreaVM(...)` после строки `DeleteCommand = new RelayCommand(_ => Delete());` добавить:

```csharp
         OpenMeshDialogCommand = new RelayCommand(_ => OpenMeshDialog());
         ClearMeshCommand      = new RelayCommand(_ => ClearMesh());
```

- [ ] **Step 4: Добавить приватные методы `OpenMeshDialog()` и `ClearMesh()`**

После метода `Delete()` добавить:

```csharp
      void OpenMeshDialog()
      {
         if (_model.Id == 0) Save();
         var dlg = new Views.MeshDialog(_model, App)
         {
            Owner = System.Windows.Application.Current.MainWindow
         };
         if (dlg.ShowDialog() == true)
         {
            OnPropertyChanged(nameof(FibersCount));
            RefreshPlot();
         }
      }

      void ClearMesh()
      {
         _model.Fibers.RemoveAll(f => f.TypeFiber is FiberType.poly or FiberType.tri);
         App.db.SaveMeshFibers(_model);
         OnPropertyChanged(nameof(FibersCount));
         RefreshPlot();
      }
```

- [ ] **Step 5: Расширить `RefreshPlot()` — добавить `FiberMeshElement`**

Найти в `RefreshPlot()` блок, который добавляет `CircleElement` для point-волокон (~строка 123):

```csharp
         foreach (var f in _model.Fibers.Where(f => f.TypeFiber == FiberType.point))
            elements.Add(new CircleElement
            {
               X = f.X, Y = f.Y, Radius = f.Diameter / 2,
               Fill = Brushes.OrangeRed, Stroke = Brushes.DarkRed, StrokeThickness = 0.5
            });
```

Перед этим `foreach` добавить:

```csharp
         var meshFibers = _model.Fibers
            .Where(f => f.TypeFiber is FiberType.poly or FiberType.tri)
            .ToArray();
         if (meshFibers.Length > 0)
            elements.Add(new FiberMeshElement
            {
               Fibers = meshFibers,
               ShowCentroids = (_fiberDisplayMode == FiberDisplayMode.Centroids)
            });

```

- [ ] **Step 6: Сборка**

```
dotnet build OpenCS.sln
```

Ожидается: Build succeeded, 0 Error(s).

- [ ] **Step 7: Коммит**

```
git add OpenCS/ViewModels/MaterialAreaVM.cs
git commit -m "feat(UI): MaterialAreaVM — FiberDisplayMode, FibersCount, OpenMesh/ClearMesh commands, mesh in RefreshPlot"
```

---

## Task 7: MeshDialogVM

**Files:**
- Create: `OpenCS/ViewModels/MeshDialogVM.cs`

- [ ] **Step 1: Создать файл `MeshDialogVM.cs`**

```csharp
using CScore;
using OpenCS.Views;

using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace OpenCS.ViewModels
{
   /// <summary>ViewModel диалога настройки и генерации сетки фибр MaterialArea.</summary>
   public class MeshDialogVM : ViewModelBase
   {
      readonly MaterialArea _area;
      readonly List<Fiber> _backup;
      readonly Window _window;

      MeshMethod _meshMethod;
      int _nx, _ny;
      double _maxArea, _minAngle;
      IReadOnlyList<PlotElement> _plotElements = [];
      int _fibersCount;

      public MeshDialogVM(MaterialArea area, AppViewModel app, Window window)
      {
         _area   = area;
         App     = app;
         _window = window;

         // Snapshot текущих волокон — восстанавливается при отмене
         _backup = area.Fibers.ToList();

         // Начальные значения из модели
         _meshMethod = area.MeshMethod;
         _nx         = area.NX;
         _ny         = area.NY;
         _maxArea    = area.MeshMaxArea;
         _minAngle   = area.MeshMinAngle;
         _fibersCount = area.Fibers.Count(f => f.TypeFiber is FiberType.poly or FiberType.tri);

         GenerateCommand = new RelayCommand(_ => Generate());
         ApplyCommand    = new RelayCommand(_ => Apply());
         CancelCommand   = new RelayCommand(_ => Cancel());

         RefreshPreview();
      }

      public AppViewModel App { get; }

      public MeshMethod MeshMethod
      {
         get => _meshMethod;
         set
         {
            _meshMethod = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsGrid));
            OnPropertyChanged(nameof(IsTriangulation));
            OnPropertyChanged(nameof(MeshMethodIndex));
         }
      }

      /// <summary>Индекс выбранного метода для ComboBox (0=Grid, 1=Ruppert, 2=AdvancingFront).</summary>
      public int MeshMethodIndex
      {
         get => (int)_meshMethod;
         set => MeshMethod = (MeshMethod)value;
      }

      public bool IsGrid          => _meshMethod == MeshMethod.Grid;
      public bool IsTriangulation => _meshMethod != MeshMethod.Grid;

      public int NX
      {
         get => _nx;
         set { _nx = value; OnPropertyChanged(); }
      }

      public int NY
      {
         get => _ny;
         set { _ny = value; OnPropertyChanged(); }
      }

      public double MaxArea
      {
         get => _maxArea;
         set { _maxArea = value; OnPropertyChanged(); }
      }

      public double MinAngle
      {
         get => _minAngle;
         set { _minAngle = value; OnPropertyChanged(); }
      }

      public IReadOnlyList<PlotElement> PlotElements
      {
         get => _plotElements;
         private set { _plotElements = value; OnPropertyChanged(); }
      }

      public int FibersCount
      {
         get => _fibersCount;
         private set { _fibersCount = value; OnPropertyChanged(); }
      }

      public ICommand GenerateCommand { get; }
      public ICommand ApplyCommand    { get; }
      public ICommand CancelCommand   { get; }

      void Generate()
      {
         switch (_meshMethod)
         {
            case MeshMethod.Grid:
               _area.SliceXY(_nx, _ny);
               break;
            case MeshMethod.Ruppert:
               _area.Triangulate(_maxArea, _minAngle, MeshMethod.Ruppert);
               break;
            case MeshMethod.AdvancingFront:
               _area.Triangulate(_maxArea, _minAngle, MeshMethod.AdvancingFront);
               break;
         }
         FibersCount = _area.Fibers.Count(f => f.TypeFiber is FiberType.poly or FiberType.tri);
         RefreshPreview();
      }

      void Apply()
      {
         _area.MeshMethod  = _meshMethod;
         _area.MeshMaxArea = _maxArea;
         _area.MeshMinAngle = _minAngle;
         App.db.SaveMeshFibers(_area);
         _window.DialogResult = true;
      }

      void Cancel()
      {
         _area.Fibers.Clear();
         _area.Fibers.AddRange(_backup);
         _window.DialogResult = false;
      }

      void RefreshPreview()
      {
         var elements = new List<PlotElement>();
         var hull = _area.Hull;

         if (hull != null && hull.X.Count > 0)
            elements.Add(new PolygonElement
            {
               Xs = [.. hull.X], Ys = [.. hull.Y],
               Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(60, 100, 149, 237)),
               Stroke = Brushes.SteelBlue, StrokeThickness = 1.5
            });

         foreach (var hole in _area.Holes)
            if (hole.X.Count > 0)
               elements.Add(new PolygonElement
               {
                  Xs = [.. hole.X], Ys = [.. hole.Y],
                  Fill = Brushes.White, Stroke = Brushes.Gray, StrokeThickness = 1
               });

         var meshFibers = _area.Fibers
            .Where(f => f.TypeFiber is FiberType.poly or FiberType.tri)
            .ToArray();
         if (meshFibers.Length > 0)
            elements.Add(new FiberMeshElement
            {
               Fibers = meshFibers,
               ShowCentroids = false
            });

         PlotElements = elements;
      }
   }
}
```

- [ ] **Step 2: Сборка**

```
dotnet build OpenCS.sln
```

Ожидается: Build succeeded, 0 Error(s).

- [ ] **Step 3: Коммит**

```
git add OpenCS/ViewModels/MeshDialogVM.cs
git commit -m "feat(UI): MeshDialogVM — mesh dialog view model with Generate/Apply/Cancel"
```

---

## Task 8: MeshDialog.xaml + MeshDialog.xaml.cs

**Files:**
- Create: `OpenCS/Views/MeshDialog.xaml`
- Create: `OpenCS/Views/MeshDialog.xaml.cs`

- [ ] **Step 1: Создать `MeshDialog.xaml`**

```xml
<Window x:Class="OpenCS.Views.MeshDialog"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Mesh"
        Width="640" Height="420"
        WindowStartupLocation="CenterOwner"
        ResizeMode="NoResize">
   <Window.Resources>
      <BooleanToVisibilityConverter x:Key="BoolToVis"/>
   </Window.Resources>

   <Grid Margin="8">
      <Grid.RowDefinitions>
         <RowDefinition Height="Auto"/>
         <RowDefinition Height="*"/>
         <RowDefinition Height="Auto"/>
      </Grid.RowDefinitions>

      <!-- Метод -->
      <StackPanel Grid.Row="0" Orientation="Horizontal" Margin="0,0,0,8">
         <TextBlock Text="{DynamicResource MeshMethod}"
                    VerticalAlignment="Center" Margin="0,0,8,0"/>
         <ComboBox SelectedIndex="{Binding MeshMethodIndex}" Width="200">
            <ComboBoxItem Content="{DynamicResource MeshMethod_Grid}"/>
            <ComboBoxItem Content="{DynamicResource MeshMethod_Ruppert}"/>
            <ComboBoxItem Content="{DynamicResource MeshMethod_AdvancingFront}"/>
         </ComboBox>
      </StackPanel>

      <!-- Основная область: параметры + превью -->
      <Grid Grid.Row="1">
         <Grid.ColumnDefinitions>
            <ColumnDefinition Width="220"/>
            <ColumnDefinition Width="*"/>
         </Grid.ColumnDefinitions>

         <!-- Панель параметров -->
         <StackPanel Grid.Column="0" Margin="0,0,8,0">

            <!-- Параметры сетки (Grid) -->
            <StackPanel Visibility="{Binding IsGrid, Converter={StaticResource BoolToVis}}">
               <TextBlock Text="NX:" Margin="0,0,0,2"/>
               <TextBox Text="{Binding NX, UpdateSourceTrigger=PropertyChanged}" Margin="0,0,0,6"/>
               <TextBlock Text="NY:" Margin="0,0,0,2"/>
               <TextBox Text="{Binding NY, UpdateSourceTrigger=PropertyChanged}" Margin="0,0,0,6"/>
            </StackPanel>

            <!-- Параметры триангуляции (Ruppert / AdvancingFront) -->
            <StackPanel Visibility="{Binding IsTriangulation, Converter={StaticResource BoolToVis}}">
               <TextBlock Text="{DynamicResource MeshMaxArea}" Margin="0,0,0,2"/>
               <TextBox Text="{Binding MaxArea, UpdateSourceTrigger=PropertyChanged}" Margin="0,0,0,6"/>
               <TextBlock Text="{DynamicResource MeshMinAngle}" Margin="0,0,0,2"/>
               <TextBox Text="{Binding MinAngle, UpdateSourceTrigger=PropertyChanged}" Margin="0,0,0,6"/>
            </StackPanel>

         </StackPanel>

         <!-- Превью -->
         <Border Grid.Column="1" BorderThickness="1"
                 BorderBrush="{DynamicResource {x:Static SystemColors.ControlLightBrushKey}}">
            <local:PlotCanvas x:Name="preview"
                              xmlns:local="clr-namespace:OpenCS.Views"/>
         </Border>
      </Grid>

      <!-- Нижняя панель: счётчик + кнопки -->
      <Grid Grid.Row="2" Margin="0,8,0,0">
         <Grid.ColumnDefinitions>
            <ColumnDefinition Width="*"/>
            <ColumnDefinition Width="Auto"/>
         </Grid.ColumnDefinitions>

         <TextBlock Grid.Column="0" VerticalAlignment="Center">
            <Run Text="{DynamicResource MeshFibersCount}"/>
            <Run Text="{Binding FibersCount}"/>
         </TextBlock>

         <StackPanel Grid.Column="1" Orientation="Horizontal">
            <Button Content="{DynamicResource MeshGenerate}"
                    Command="{Binding GenerateCommand}"
                    Padding="12,4" Margin="0,0,8,0"/>
            <Button Content="{DynamicResource MeshApply}"
                    Command="{Binding ApplyCommand}"
                    Padding="12,4" Margin="0,0,8,0"/>
            <Button Content="{DynamicResource Cancel}"
                    Command="{Binding CancelCommand}"
                    Padding="12,4"/>
         </StackPanel>
      </Grid>
   </Grid>
</Window>
```

**Примечание**: `{DynamicResource Cancel}` — уже существующий ключ в Strings-файлах. Проверить наличие ключа `Cancel` в ресурсах. Если его нет, добавить в оба файла: RU → "Отмена", EN → "Cancel".

- [ ] **Step 2: Создать `MeshDialog.xaml.cs`**

```csharp
using CScore;
using OpenCS.ViewModels;

using System.Collections.Generic;
using System.Windows;

namespace OpenCS.Views
{
   public partial class MeshDialog : Window
   {
      MeshDialogVM _vm;

      public MeshDialog(MaterialArea area, AppViewModel app)
      {
         InitializeComponent();
         _vm = new MeshDialogVM(area, app, this);
         DataContext = _vm;
         _vm.PropertyChanged += (_, e) =>
         {
            if (e.PropertyName == nameof(MeshDialogVM.PlotElements))
               UpdatePreview();
         };
         UpdatePreview();
      }

      void UpdatePreview()
      {
         var elements = _vm.PlotElements;
         if (elements.Count == 0) { preview.Clear(); return; }

         double xMin = double.MaxValue, xMax = double.MinValue;
         double yMin = double.MaxValue, yMax = double.MinValue;

         foreach (var el in elements)
         {
            if (el is PolygonElement p)
            {
               for (int i = 0; i < p.Xs.Length; i++)
               {
                  if (p.Xs[i] < xMin) xMin = p.Xs[i];
                  if (p.Xs[i] > xMax) xMax = p.Xs[i];
                  if (p.Ys[i] < yMin) yMin = p.Ys[i];
                  if (p.Ys[i] > yMax) yMax = p.Ys[i];
               }
            }
         }

         if (xMin > xMax) { preview.Clear(); return; }
         if (xMax - xMin < 1e-9) { xMin -= 0.1; xMax += 0.1; }
         if (yMax - yMin < 1e-9) { yMin -= 0.1; yMax += 0.1; }

         preview.Draw(elements, xMin, xMax, yMin, yMax, squareAxes: true);
      }
   }
}
```

- [ ] **Step 3: Проверить ключ `Cancel` в ресурсных файлах**

Выполнить поиск:
```
grep -n "Cancel" OpenCS/Resources/Strings.ru-RU.xaml
```

Если ключ `Cancel` не найден — добавить в оба файла (перед секцией Mesh generation):

`Strings.ru-RU.xaml`: `<system:String x:Key="Cancel">Отмена</system:String>`
`Strings.en-US.xaml`: `<system:String x:Key="Cancel">Cancel</system:String>`

- [ ] **Step 4: Сборка**

```
dotnet build OpenCS.sln
```

Ожидается: Build succeeded, 0 Error(s).

- [ ] **Step 5: Коммит**

```
git add OpenCS/Views/MeshDialog.xaml OpenCS/Views/MeshDialog.xaml.cs
git commit -m "feat(UI): MeshDialog — mesh generation dialog with preview PlotCanvas"
```

---

## Task 9: MaterialAreaPage.xaml update

**Files:**
- Modify: `OpenCS/Views/MaterialAreaPage.xaml`

- [ ] **Step 1: Заменить GroupBox «Сетка» (MeshGrid)**

Найти секцию (~строка 74):

```xml
            <!-- Сетка -->
            <GroupBox Header="{DynamicResource MeshGrid}" Margin="0,0,0,8">
               <Grid Margin="4">
                  <Grid.ColumnDefinitions>
                     <ColumnDefinition Width="Auto"/>
                     <ColumnDefinition Width="*"/>
                     <ColumnDefinition Width="Auto"/>
                     <ColumnDefinition Width="*"/>
                  </Grid.ColumnDefinitions>
                  <TextBlock Text="NX:" Grid.Column="0" VerticalAlignment="Center" Margin="0,0,4,0"/>
                  <TextBox Text="{Binding NX, UpdateSourceTrigger=PropertyChanged}"
                           Grid.Column="1" Margin="0,0,8,0"/>
                  <TextBlock Text="NY:" Grid.Column="2" VerticalAlignment="Center" Margin="0,0,4,0"/>
                  <TextBox Text="{Binding NY, UpdateSourceTrigger=PropertyChanged}"
                           Grid.Column="3"/>
               </Grid>
            </GroupBox>
```

Заменить на:

```xml
            <!-- Сетка -->
            <GroupBox Header="{DynamicResource MeshGrid}" Margin="0,0,0,8">
               <StackPanel Margin="4">
                  <!-- Режим отображения -->
                  <TextBlock Text="{DynamicResource MeshDisplayMode}" Margin="0,0,0,2"/>
                  <ComboBox SelectedIndex="{Binding FiberDisplayModeIndex}" Margin="0,0,0,6">
                     <ComboBoxItem Content="{DynamicResource MeshDisplayElements}"/>
                     <ComboBoxItem Content="{DynamicResource MeshDisplayCentroids}"/>
                  </ComboBox>
                  <!-- Счётчик фибр -->
                  <TextBlock Margin="0,0,0,6">
                     <Run Text="{DynamicResource MeshFibersCount}"/>
                     <Run Text="{Binding FibersCount}"/>
                  </TextBlock>
                  <!-- Кнопки -->
                  <StackPanel Orientation="Horizontal">
                     <Button Content="{DynamicResource MeshConfigure}"
                             Command="{Binding OpenMeshDialogCommand}"
                             Padding="8,3" Margin="0,0,4,0"/>
                     <Button Content="{DynamicResource MeshClear}"
                             Command="{Binding ClearMeshCommand}"
                             Padding="8,3"/>
                  </StackPanel>
               </StackPanel>
            </GroupBox>
```

- [ ] **Step 2: Добавить `FiberDisplayModeIndex` в `MaterialAreaVM`**

Поскольку ComboBox привязан к индексу, добавить в `MaterialAreaVM.cs` вычисляемое свойство:

```csharp
      public int FiberDisplayModeIndex
      {
         get => (int)_fiberDisplayMode;
         set => FiberDisplayMode = (FiberDisplayMode)value;
      }
```

Добавить после свойства `FiberDisplayMode`.

- [ ] **Step 3: Сборка**

```
dotnet build OpenCS.sln
```

Ожидается: Build succeeded, 0 Error(s).

- [ ] **Step 4: Коммит**

```
git add OpenCS/Views/MaterialAreaPage.xaml OpenCS/ViewModels/MaterialAreaVM.cs
git commit -m "feat(UI): MaterialAreaPage — replace NX/NY grid group with mesh control panel"
```

---

## Task 10: Финальная проверка

- [ ] **Step 1: Полная сборка решения**

```
dotnet build OpenCS.sln
```

Ожидается: Build succeeded, 0 Error(s), 0 Warning(s) (или только допустимые предупреждения, не связанные с новым кодом).

- [ ] **Step 2: Ручное smoke-тестирование**

```
dotnet run --project OpenCS
```

Проверить:
1. Приложение запускается без ошибок.
2. Открыть или создать MaterialArea с назначенным Hull-контуром, сохранить (кнопка «Сохранить»).
3. В группе «Сетка» появились ComboBox (Отображение), счётчик «Фибр: 0» и кнопки «Настроить...» / «Очистить».
4. Нажать «Настроить...» — открывается диалог MeshDialog.
5. В диалоге: выбрать метод «Ортогональная сетка», NX=10, NY=10, нажать «Разбить» — превью показывает сетку, счётчик обновляется.
6. Нажать «Применить» — диалог закрывается, на главной странице счётчик показывает кол-во фибр.
7. Переключить Отображение → «Центроиды» — превью перерисовывается с точками.
8. Переключить обратно → «Элементы» — превью показывает полигоны.
9. Нажать «Очистить» — счётчик сбрасывается в 0, превью без сетки.
10. Открыть диалог снова, выбрать «Триангуляция Рупперта», MaxArea=0.005, нажать «Разбить» → «Применить».
11. Закрыть проект, открыть снова — фибры загружаются из БД.

- [ ] **Step 3: Финальный коммит (если остались несохранённые изменения)**

```
git add -A
git commit -m "feat: mesh generation — complete implementation"
```
