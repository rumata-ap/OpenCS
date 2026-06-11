# Remove EF Core — Replace with Microsoft.Data.Sqlite + JSON

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace EF Core with a lightweight SQLite persistence layer using Microsoft.Data.Sqlite and JSON serialization for nested collections, reducing the schema from 12+1 tables to 4.

**Architecture:** A single `DatabaseService` class manages SQLite connection, table creation, and CRUD via parameterized SQL. Nested collections (MaterialChars, StressPoints, Fibers, ReBars) are stored as JSON text columns. The domain model (CScore classes) stays unchanged — only `[NotMapped]` semantics are dropped since we control persistence manually. `AppViewModel` and child ViewModels call `DatabaseService` methods instead of EF Core APIs.

**Tech Stack:** Microsoft.Data.Sqlite (already a transitive dependency via EF Core, will be added directly), Newtonsoft.Json (already in CScore), C# raw SQL.

---

## File Structure

| Action | File | Responsibility |
|--------|------|----------------|
| Create | `OpenCS/Utilites/DatabaseService.cs` | SQLite connection, table creation, load/save CRUD |
| Modify | `OpenCS/AppViewModel.cs` | Replace `ApplicationContext db` with `DatabaseService db`; update load/save calls |
| Modify | `OpenCS/ViewModels/ContourVM.cs` | Replace `mvm.db.SaveChanges()` with `mvm.db.Save()` |
| Modify | `OpenCS/ViewModels/MaterialVM.cs` | Replace `db.Materials.Add/Remove/Entry` with `db.Add/Save` |
| Modify | `OpenCS/ViewModels/FromDxfVM.cs` | Replace `db.AddRange/SaveChanges` with `db.Add/Save` |
| Modify | `OpenCS/ViewModels/RCFiberRegionVM.cs` | Replace `db.RemoveRange/SaveChanges` with `db.Remove/Save` |
| Modify | `OpenCS/ViewModels/RebarsVM.cs` | Replace `db.Add/AddRange/SaveChanges` with `db.Add/Save` |
| Modify | `OpenCS/Utilites/Renumberer.cs` | Remove `.Include()` call |
| Delete | `OpenCS/Utilites/ApplicationContext.cs` | EF Core DbContext — no longer needed |
| Delete | `OpenCS/Migrations/` | EF Core migration files — no longer needed |
| Modify | `OpenCS/OpenCS.csproj` | Remove EF Core packages, add Microsoft.Data.Sqlite |

---

## Database Schema (4 tables)

```sql
CREATE TABLE IF NOT EXISTS materials (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    type INTEGER NOT NULL DEFAULT 0,
    tag TEXT NOT NULL DEFAULT '',
    description TEXT NOT NULL DEFAULT '',
    e REAL NOT NULL DEFAULT 0,
    chars_json TEXT NOT NULL DEFAULT '[]'
);

CREATE TABLE IF NOT EXISTS contours (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    tag TEXT NOT NULL DEFAULT '',
    wkt TEXT NOT NULL DEFAULT '',
    points_json TEXT NOT NULL DEFAULT '[]'
);

CREATE TABLE IF NOT EXISTS circles (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    tag TEXT NOT NULL DEFAULT '',
    x REAL NOT NULL DEFAULT 0,
    y REAL NOT NULL DEFAULT 0,
    diameter REAL NOT NULL DEFAULT 0,
    radius REAL NOT NULL DEFAULT 0,
    area REAL NOT NULL DEFAULT 0,
    type INTEGER NOT NULL DEFAULT 0,
    num INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE IF NOT EXISTS rc_fiber_regions (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    tag TEXT NOT NULL DEFAULT '',
    data_json TEXT NOT NULL DEFAULT '{}'
);
```

**What goes where:**

| Old table(s) | New location | Format |
|---|---|---|
| `Materials` | `materials` | Flat columns + `chars_json` (List\<MaterialChars\>) |
| `MatChars` | `materials.chars_json` | JSON array of 4 MaterialChars objects |
| `Contours` | `contours` | Flat columns + `points_json` |
| `Points` (StressPoint) | `contours.points_json` | JSON array of StressPoint objects |
| `Circles` | `circles` | All flat columns (no nesting) |
| `Regions` | `rc_fiber_regions.data_json` | JSON (includes discriminator, WKT, material_id, fibers) |
| `FiberRegions` | `rc_fiber_regions.data_json` | JSON (nested inside data_json) |
| `RCFiberRegions` | `rc_fiber_regions` | Flat tag + `data_json` for everything else |
| `Fibers` | `rc_fiber_regions.data_json` | JSON (nested inside region objects) |
| `ReBarGroups` | `rc_fiber_regions.data_json` | JSON (nested inside data_json) |
| `ReBars` + `ReBarLayers` | `rc_fiber_regions.data_json` | JSON (nested inside rebar groups, with discriminator) |
| `ContourRegion` | Eliminated | Regions reference Contour by id inside JSON |

---

## JSON Serialization Format for `rc_fiber_regions.data_json`

```json
{
  "contour_id": 1,
  "regions": [
    {
      "discriminator": "FiberRegion",
      "tag": "region1",
      "wkt": "POLYGON((0 0, 1 0, 1 1, 0 1, 0 0))",
      "h": 0.5,
      "material_id": 1,
      "nx": 10,
      "ny": 10,
      "atr": 0,
      "antr": 0,
      "fibers": [ ... ]
    }
  ],
  "rebar_groups": [
    {
      "tag": "group1",
      "type": 0,
      "material_id": 2,
      "rebars": [
        { "discriminator": "ReBar", "x": 0.1, "y": 0.2, "diameter": 0.012, ... },
        { "discriminator": "ReBarLayer", "x": 0.05, "y": 0.05, "diameter": 0.016, "nd": 4, "pos": 1, "as": 0.0002, ... }
      ]
    }
  ]
}
```

---

### Task 1: Create DatabaseService

**Files:**
- Create: `OpenCS/Utilites/DatabaseService.cs`

- [ ] **Step 1: Create DatabaseService with connection management and table creation**

Create `OpenCS/Utilites/DatabaseService.cs` with:
- Constructor opens `SqliteConnection` to `dbapp.db`
- `EnsureCreated()` creates all 4 tables with `CREATE TABLE IF NOT EXISTS`
- `Dispose()` closes connection
- Add `using Microsoft.Data.Sqlite;` and `using Newtonsoft.Json;`

```csharp
using CScore;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json;
using System.Collections.ObjectModel;

namespace OpenCS.Utilites
{
   /// <summary>
   /// Сервис работы с базой данных SQLite. Заменяет EF Core ApplicationContext.
   /// Хранит подключение к БД и управляет CRUD-операциями через параметризованный SQL.
   /// Вложенные коллекции сериализуются в JSON-колонки.
   /// </summary>
   public class DatabaseService : IDisposable
   {
      private readonly SqliteConnection _connection;

      public DatabaseService()
      {
         _connection = new SqliteConnection("Data Source=dbapp.db");
         _connection.Open();
         EnsureCreated();
      }

      private void EnsureCreated()
      {
         var cmd = _connection.CreateCommand();
         cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS materials (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                type INTEGER NOT NULL DEFAULT 0,
                tag TEXT NOT NULL DEFAULT '',
                description TEXT NOT NULL DEFAULT '',
                e REAL NOT NULL DEFAULT 0,
                chars_json TEXT NOT NULL DEFAULT '[]'
            );
            CREATE TABLE IF NOT EXISTS contours (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                tag TEXT NOT NULL DEFAULT '',
                wkt TEXT NOT NULL DEFAULT '',
                points_json TEXT NOT NULL DEFAULT '[]'
            );
            CREATE TABLE IF NOT EXISTS circles (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                tag TEXT NOT NULL DEFAULT '',
                x REAL NOT NULL DEFAULT 0,
                y REAL NOT NULL DEFAULT 0,
                diameter REAL NOT NULL DEFAULT 0,
                radius REAL NOT NULL DEFAULT 0,
                area REAL NOT NULL DEFAULT 0,
                type INTEGER NOT NULL DEFAULT 0,
                num INTEGER NOT NULL DEFAULT 0
            );
            CREATE TABLE IF NOT EXISTS rc_fiber_regions (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                tag TEXT NOT NULL DEFAULT '',
                data_json TEXT NOT NULL DEFAULT '{}'
            );
         ";
         cmd.ExecuteNonQuery();
      }

      public void Dispose()
      {
         _connection.Close();
         _connection.Dispose();
      }
```

- [ ] **Step 2: Add Load methods**

Add load methods that read all rows and populate ObservableCollections. Each load method selects all rows, deserializes JSON columns, and assigns `Id` from the database row.

```csharp
      public ObservableCollection<Material> LoadMaterials()
      {
         var result = new ObservableCollection<Material>();
         var cmd = _connection.CreateCommand();
         cmd.CommandText = "SELECT id, type, tag, description, e, chars_json FROM materials ORDER BY id";
         using var reader = cmd.ExecuteReader();
         while (reader.Read())
         {
            var m = new Material
            {
               Id = reader.GetInt32(0),
               Type = (MatType)reader.GetInt32(1),
               Tag = reader.GetString(2),
               Description = reader.GetString(3),
               E = reader.GetDouble(4)
            };
            var charsJson = reader.GetString(5);
            var chars = JsonConvert.DeserializeObject<List<MaterialChars>>(charsJson);
            if (chars != null && chars.Count == 4)
               m.MaterialChars = chars;
            result.Add(m);
         }
         return result;
      }

      public ObservableCollection<Contour> LoadContours()
      {
         var result = new ObservableCollection<Contour>();
         var cmd = _connection.CreateCommand();
         cmd.CommandText = "SELECT id, tag, wkt, points_json FROM contours ORDER BY id";
         using var reader = cmd.ExecuteReader();
         while (reader.Read())
         {
            var c = new Contour
            {
               Id = reader.GetInt32(0),
               Tag = reader.GetString(1)
            };
            c.WKT = reader.GetString(2);
            var pointsJson = reader.GetString(3);
            var points = JsonConvert.DeserializeObject<List<StressPoint>>(pointsJson,
               new JsonSerializerSettings { ObjectCreationHandling = ObjectCreationHandling.Replace });
            if (points != null)
               foreach (var p in points) { p.Id = 0; c.Points.Add(p); }
            result.Add(c);
         }
         return result;
      }

      public ObservableCollection<CircleP> LoadCircles()
      {
         var result = new ObservableCollection<CircleP>();
         var cmd = _connection.CreateCommand();
         cmd.CommandText = "SELECT id, tag, x, y, diameter, radius, area, type, num FROM circles ORDER BY id";
         using var reader = cmd.ExecuteReader();
         while (reader.Read())
         {
            var c = new CircleP
            {
               Id = reader.GetInt32(0),
               Tag = reader.GetString(1),
               X = reader.GetDouble(2),
               Y = reader.GetDouble(3),
               Diameter = reader.GetDouble(4),
               Radius = reader.GetDouble(5),
               Area = reader.GetDouble(6),
               Type = (PointType)reader.GetInt32(7),
               Num = reader.GetInt32(8)
            };
            result.Add(c);
         }
         return result;
      }

      public ObservableCollection<RCFiberRegion> LoadRCFiberRegions()
      {
         var result = new ObservableCollection<RCFiberRegion>();
         var cmd = _connection.CreateCommand();
         cmd.CommandText = "SELECT id, tag, data_json FROM rc_fiber_regions ORDER BY id";
         using var reader = cmd.ExecuteReader();
         while (reader.Read())
         {
            var r = new RCFiberRegion { Id = reader.GetInt32(0), Tag = reader.GetString(1) };
            var dataJson = reader.GetString(2);
            // TODO: deserialize regions, rebar groups, resolve material/contour references
            result.Add(r);
         }
         return result;
      }
```

- [ ] **Step 3: Add Save/Add/Delete methods**

Add per-entity CRUD methods using `INSERT OR REPLACE` for saves and `DELETE` for removals.

```csharp
      public void SaveMaterial(Material m)
      {
         var cmd = _connection.CreateCommand();
         cmd.CommandText = m.Id == 0
            ? @"INSERT INTO materials (type, tag, description, e, chars_json) VALUES ($type, $tag, $desc, $e, $chars); SELECT last_insert_rowid();"
            : @"UPDATE materials SET type=$type, tag=$tag, description=$desc, e=$e, chars_json=$chars WHERE id=$id";
         cmd.Parameters.AddWithValue("$type", (int)m.Type);
         cmd.Parameters.AddWithValue("$tag", m.Tag);
         cmd.Parameters.AddWithValue("$desc", m.Description);
         cmd.Parameters.AddWithValue("$e", m.E);
         cmd.Parameters.AddWithValue("$chars", JsonConvert.SerializeObject(m.MaterialChars));
         if (m.Id != 0)
            cmd.Parameters.AddWithValue("$id", m.Id);
         if (m.Id == 0)
            m.Id = (int)(long)cmd.ExecuteScalar()!;
         else
            cmd.ExecuteNonQuery();
      }

      public void DeleteMaterial(Material m)
      {
         var cmd = _connection.CreateCommand();
         cmd.CommandText = "DELETE FROM materials WHERE id=$id";
         cmd.Parameters.AddWithValue("$id", m.Id);
         cmd.ExecuteNonQuery();
      }

      public void SaveContour(Contour c) { /* similar pattern */ }
      public void DeleteContour(Contour c) { /* similar pattern */ }
      public void SaveCircle(CircleP c) { /* similar pattern */ }
      public void DeleteCircle(CircleP c) { /* similar pattern */ }
      public void SaveRCFiberRegion(RCFiberRegion r) { /* similar pattern */ }
      public void DeleteRCFiberRegion(RCFiberRegion r) { /* similar pattern */ }
```

- [ ] **Step 4: Add convenience methods matching current call sites**

Add `AddRange` and `Save` convenience methods to match current call patterns.

```csharp
      public void AddMaterial(Material m) { Materials.Add(m); SaveMaterial(m); }
      public void AddContour(Contour c) { /* add to collection + save */ }
      public void AddCircle(CircleP c) { /* add to collection + save */ }
      public void AddRange(IEnumerable<CircleP> circles) { foreach (var c in circles) AddCircle(c); }
      public void AddRange(IEnumerable<Contour> contours) { foreach (var c in contours) AddContour(c); }
      // etc.
```

- [ ] **Step 5: Build and verify no compile errors**

Run: `dotnet build OpenCS.sln`
Expected: Build succeeds (DatabaseService compiles, but AppViewModel still uses ApplicationContext)

---

### Task 2: Update AppViewModel to use DatabaseService

**Files:**
- Modify: `OpenCS/AppViewModel.cs`

- [ ] **Step 1: Replace ApplicationContext with DatabaseService**

Change line 9 from `using Microsoft.EntityFrameworkCore;` to `using OpenCS.Utilites;` (if not already).
Change line 29 from `internal ApplicationContext db = new();` to `internal DatabaseService db = new();`.

- [ ] **Step 2: Replace EF Core load calls with DatabaseService methods**

Replace lines 310-337 (the 12 `Load()` + 12 `Local.ToObservableCollection()` + 3 `Include()` calls) with:

```csharp
MaterialChars = db.LoadMaterialChars();  // or compute from Materials
Materials = db.LoadMaterials();
Points = db.LoadPoints();  // loaded as part of contours, separate collection for binding
Circles = db.LoadCircles();
Contours = db.LoadContours();
Regions = db.LoadRegions();  // loaded as part of RCFiberRegions
FiberRegions = db.LoadFiberRegions();  // loaded as part of RCFiberRegions
RcFiberRegions = db.LoadRCFiberRegions();
Fibers = db.LoadFibers();  // loaded as part of regions
Rebars = db.LoadRebars();  // loaded as part of rebar groups
RebarLayers = db.LoadRebarLayers();  // loaded as part of rebar groups
RebarGroups = db.LoadRebarGroups();  // loaded as part of RCFiberRegions
```

Note: Some collections (Points, Fibers, Rebars, etc.) were previously separate tables but are now stored as JSON inside their parent entities. The load methods must populate these flat collections from the deserialized parent data.

- [ ] **Step 3: Replace delete operations**

Replace `db.Materials.Remove(CurrentMaterial); db.SaveChanges();` with `db.DeleteMaterial(CurrentMaterial);` at line 408-409.

Replace `db.Remove(region); db.Contours.Remove(currentContour.Contour); db.SaveChanges();` at lines 434-438 with:
```csharp
foreach (var region in currentContour.Regions)
   db.DeleteRegion(region);
db.DeleteContour(currentContour.Contour);
```

Replace `db.Remove(CurrentRCfiberRegion); db.SaveChanges();` at lines 489-490 with `db.DeleteRCFiberRegion(CurrentRCfiberRegion);`.

- [ ] **Step 4: Build and verify**

Run: `dotnet build OpenCS.sln`

---

### Task 3: Update child ViewModels

**Files:**
- Modify: `OpenCS/ViewModels/ContourVM.cs`
- Modify: `OpenCS/ViewModels/MaterialVM.cs`
- Modify: `OpenCS/ViewModels/FromDxfVM.cs`
- Modify: `OpenCS/ViewModels/RCFiberRegionVM.cs`
- Modify: `OpenCS/ViewModels/RebarsVM.cs`
- Modify: `OpenCS/Utilites/Renumberer.cs`

- [ ] **Step 1: Update ContourVM.cs (3 SaveChanges calls)**

Replace `mvm.db.SaveChanges()` at lines 225, 324, 332 with `mvm.db.SaveContour(Contour)` or appropriate save method.

- [ ] **Step 2: Update MaterialVM.cs (Add + Entry.Modified)**

Line 100-101: Replace `mvm.db.Materials.Add(material); mvm.db.SaveChanges();` with `mvm.db.AddMaterial(material);`
Line 120-121: Replace `mvm.db.Entry(material).State = EntityState.Modified; mvm.db.SaveChanges();` with `mvm.db.SaveMaterial(material);`
Remove `using Microsoft.EntityFrameworkCore;`

- [ ] **Step 3: Update FromDxfVM.cs (AddRange calls)**

Lines 298-299: Replace `mvm.db.AddRange(circlesPrj); mvm.db.SaveChanges();` with `mvm.db.AddRange(circlesPrj);` (the DatabaseService.AddRange handles save)
Lines 311-312: Replace `mvm.db.AddRange(contoursPrj); mvm.db.SaveChanges();` with `mvm.db.AddRange(contoursPrj);`

- [ ] **Step 4: Update RCFiberRegionVM.cs (RemoveRange + SaveChanges)**

Lines 410-411: Replace `mvm.db.RemoveRange(region.Fibers); mvm.db.SaveChanges();` with `mvm.db.SaveRCFiberRegion(...)` (saving the parent re-serializes the nested fibers)
Lines 663-664, 1012-1013: Same pattern
Line 669, 921: Replace `mvm.db.SaveChanges();` with appropriate save call
Remove `using Microsoft.EntityFrameworkCore;`

- [ ] **Step 5: Update RebarsVM.cs (Add + AddRange)**

Lines 221-222: Replace `MVM.db.Add(c); MVM.db.SaveChanges();` with `MVM.db.AddCircle(c);`
Lines 271-272: Replace `MVM.db.AddRange(list); MVM.db.SaveChanges();` with `MVM.db.AddRange(list);`
Lines 280-281: Same pattern for rebars

- [ ] **Step 6: Update Renumberer.cs**

Line 59: Replace `var rcFiberRegions = mvm.db.RCFiberRegions.Include(r => r.Contours).ToList();` with `mvm.RcFiberRegions.ToList();` (already loaded in memory, no need to re-query)
Remove `using Microsoft.EntityFrameworkCore;`

- [ ] **Step 7: Build and verify**

Run: `dotnet build OpenCS.sln`

---

### Task 4: Remove EF Core and old infrastructure

**Files:**
- Delete: `OpenCS/Utilites/ApplicationContext.cs`
- Delete: `OpenCS/Migrations/` (entire directory)
- Modify: `OpenCS/OpenCS.csproj`

- [ ] **Step 1: Delete ApplicationContext.cs and Migrations directory**

Delete `OpenCS/Utilites/ApplicationContext.cs`
Delete `OpenCS/Migrations/` (all 3 files)

- [ ] **Step 2: Remove EF Core packages from csproj**

Remove from `OpenCS/OpenCS.csproj`:
```xml
<PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="9.0.1" />
<PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="9.0.1" />
```

Add:
```xml
<PackageReference Include="Microsoft.Data.Sqlite" Version="9.0.1" />
```

Note: `Microsoft.Data.Sqlite` is the lightweight ADO.NET provider. Version 9.0.1 matches the current runtime.

- [ ] **Step 3: Remove EF Core using statements**

In all modified files, remove `using Microsoft.EntityFrameworkCore;` and `using Microsoft.EntityFrameworkCore.ChangeTracking;` if present. Verify no other EF Core references remain.

- [ ] **Step 4: Clean up [NotMapped] attributes in CScore**

The `[NotMapped]` attribute (`System.ComponentModel.DataAnnotations.Schema.NotMappedAttribute`) is from the .NET runtime, not EF Core, so it's harmless. However, since we now control serialization manually, these attributes serve no purpose. This is a LOW PRIORITY cleanup — skip if time is limited.

- [ ] **Step 5: Delete old database files**

Delete `dbapp.db` files that were created by EF Core. The new DatabaseService will create a fresh database with the simplified schema on first run.

- [ ] **Step 6: Build and verify**

Run: `dotnet build OpenCS.sln`
Expected: Build succeeds with 0 errors

---

### Task 5: Test and verify

- [ ] **Step 1: Run the application**

Run: `dotnet run --project OpenCS`
Expected: Application starts without errors, creates new `dbapp.db` with 4 tables

- [ ] **Step 2: Verify database schema**

Use a SQLite tool or the DatabaseService to verify that all 4 tables were created with correct columns.

- [ ] **Step 3: Test basic operations**

1. Add a material → verify it persists in `materials` table
2. Import a contour from DXF → verify it persists in `contours` table with `points_json`
3. Create an RCFiberRegion → verify it persists in `rc_fiber_regions` table with `data_json`
4. Delete an entity → verify it's removed from the database

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "refactor: replace EF Core with Microsoft.Data.Sqlite + JSON

- Replace ApplicationContext (DbContext) with DatabaseService
- Simplify schema from 12+1 tables to 4 (materials, contours, circles, rc_fiber_regions)
- Store nested collections (MaterialChars, StressPoints, Fibers, ReBars) as JSON columns
- Remove EF Core packages, add Microsoft.Data.Sqlite directly
- Delete Migrations directory
- Update all ViewModels to use DatabaseService methods"
```