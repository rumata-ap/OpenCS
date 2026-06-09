# MaterialArea + CrossSection Refactoring — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the `Region / FiberRegion / RCFiberRegion / ReBarGroup / ReBar / ReBarLayer` class hierarchy with a unified `MaterialArea` + `CrossSection` model, where rebar areas use differential diagrams (σ_steel − σ_concrete) so that `Integral()` is identical for all area types.

**Architecture:** Single `MaterialArea` class holds geometry + `List<Fiber>` (polygon or point fibers) + `Dictionary<CalcType, Diagramm>`. `CrossSection` is a flat container of `MaterialArea` objects with a uniform `Integral()`. `TwoStageSection` extends `CrossSection` with a frozen Stage-1 curvature. `DifferentialSpline` in CSmath enables diagram subtraction without any special logic in `Integral()`.

**Tech Stack:** .NET 9.0, C#, WPF, Microsoft.Data.Sqlite (ADO.NET), Newtonsoft.Json, ScottPlot.WPF, MVVM (ViewModelBase / RelayCommand).

**Spec:** `docs/superpowers/specs/2026-06-09-material-area-refactoring-design.md`

**Build verification command (used throughout):**
```
dotnet build OpenCS.sln
```
Expected: `Build succeeded. 0 Error(s)`

---

## File Map

### Create
| File | Purpose |
|---|---|
| `CSmath/DifferentialSpline.cs` | ISpline wrapper: f(x) = a.Interpolate(x) − b.Interpolate(x) |
| `CScore/MaterialArea.cs` | Replaces FiberRegion + RCFiberRegion + ReBarGroup |
| `CScore/CrossSection.cs` | Container with uniform Integral() |
| `CScore/TwoStageSection.cs` | Two-stage (staged construction) cross-section |
| `OpenCS/ViewModels/MaterialAreaVM.cs` | VM for MaterialArea |
| `OpenCS/ViewModels/CrossSectionVM.cs` | VM for CrossSection |
| `OpenCS/Views/CrossSectionPage.xaml` + `.xaml.cs` | Edit/create section page |
| `OpenCS/Views/CrossSectionView.xaml` + `.xaml.cs` | Analysis view for section |

### Modify
| File | Change |
|---|---|
| `CScore/Fiber.cs` | Add `point=3` to `FiberType`; add `Diameter` property; change `FiberRegion? Region` → `MaterialArea? Area` |
| `CScore/Diagramm.cs` | Add `static Differential(Diagramm, Diagramm)` factory; add `Sig(MaterialArea, bool, bool)` overload |
| `CScore/GeoProps.cs` | Add `GeoProps(MaterialArea)`, `GeoProps(CrossSection)` constructors; remove old Region/FiberRegion/RCFiberRegion/ReBarGroup constructors |
| `CScore/Geo.cs` | Change `Region` parameters to `MaterialArea` in all public static methods |
| `CScore/GridSplit.cs` | Change `Region` parameters to `MaterialArea` in all public static methods |
| `CScore/Contour.cs` | Update `RegionType` enum values |
| `OpenCS/Utilites/DatabaseService.cs` | New schema; new Load/Save/Delete for CrossSection; remove old RC methods |
| `OpenCS/AppViewModel.cs` | Replace 3 collections with `ObservableCollection<CrossSection> CrossSections` |
| `OpenCS/Resources/Strings.ru-RU.xaml` | Add CrossSection localization keys |
| `OpenCS/Resources/Strings.en-US.xaml` | Add CrossSection localization keys |
| `OpenCS/MainWindow.xaml` | TreeView: add CrossSections node with area-type icons |
| `OpenCS/App.xaml` | Add icon Path geometries and area-type color brushes |

### Delete (Phase 5)
`CScore/Region.cs`, `CScore/FiberRegion.cs`, `CScore/RCFiberRegion.cs`, `CScore/ReBarGroup.cs`, `CScore/ReBar.cs`, `CScore/ReBarLayer.cs`, `CScore/FiberRegionData.cs`,
`OpenCS/ViewModels/RCFiberRegionVM.cs`, `OpenCS/ViewModels/RebarsVM.cs`,
`OpenCS/Views/FiberRegionPage.xaml`, `OpenCS/Views/FiberRegionPage.xaml.cs`,
`OpenCS/Views/FiberRegionView.xaml`, `OpenCS/Views/FiberRegionView.xaml.cs`,
`OpenCS/Views/RCFiberRegionPage.xaml`, `OpenCS/Views/RCFiberRegionPage.xaml.cs`,
`OpenCS/Views/RCFiberRegionView.xaml`, `OpenCS/Views/RCFiberRegionView.xaml.cs`,
`OpenCS/Views/RebarsPage.xaml`, `OpenCS/Views/RebarsPage.xaml.cs`

---

## Phase 1 — Core CScore domain

### Task 1: Add `DifferentialSpline` to CSmath

**Files:**
- Create: `CSmath/DifferentialSpline.cs`

- [ ] **Step 1: Create the file**

```csharp
namespace CSmath
{
    /// <summary>
    /// Разностный сплайн: f(x) = a.Interpolate(x) − b.Interpolate(x).
    /// Используется для дифференциальных диаграмм арматуры (σ_сталь − σ_бетон).
    /// </summary>
    public class DifferentialSpline : ISpline
    {
        readonly ISpline _a;
        readonly ISpline _b;

        public DifferentialSpline(ISpline a, ISpline b) { _a = a; _b = b; }

        // Массивы узлов не используются в обёртке — делегируется к _a
        public double[] X  { get => _a.X;  set { } }
        public double[] Y  { get => _a.Y;  set { } }
        public double[] DY { get => _a.DY; set { } }
        public double[] A  { get => _a.A;  set { } }
        public double[] B  { get => _a.B;  set { } }
        public double[] C  { get => _a.C;  set { } }
        public double[] D  { get => _a.D;  set { } }

        public double Interpolate(double xi) =>
            _a.Interpolate(xi) - _b.Interpolate(xi);

        public double Derivative(double xi, out double interp)
        {
            double da = _a.Derivative(xi, out double va);
            double db = _b.Derivative(xi, out double vb);
            interp = va - vb;
            return da - db;
        }
    }
}
```

- [ ] **Step 2: Build**

```
dotnet build OpenCS.sln
```
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 3: Commit**

```
git add CSmath/DifferentialSpline.cs
git commit -m "feat(CSmath): add DifferentialSpline wrapper"
```

---

### Task 2: Extend `Fiber` — add `point` type and `Diameter`

**Files:**
- Modify: `CScore/Fiber.cs`

Current state: `FiberType { tri = 2, poly = 1, none = 0 }`, property `TypeFiber`, reference `FiberRegion? Region`.

- [ ] **Step 1: Add `point` to enum and `Diameter` field; rename Region reference**

In `CScore/Fiber.cs` make these three changes:

1. Enum — add `point`:
```csharp
public enum FiberType { tri = 2, poly = 1, none = 0, point = 3 }
```

2. Remove `FiberRegion? Region` and `int RegionId` fields; replace with:
```csharp
/// <summary>Диаметр стержня [м]. Только для волокон типа point.</summary>
public double Diameter { get; set; }

/// <summary>Ссылка на родительскую материальную область. Не сериализуется.</summary>
[JsonIgnore] public MaterialArea? Area { get; set; }

/// <summary>Внешний ключ для связи с MaterialArea. Не сериализуется.</summary>
[JsonIgnore] public int AreaId { get; set; }
```

3. Update `ToString()`:
```csharp
public override string ToString()
{
    if (Area == null)
        return $"{Num:D3}#fiber : {Tag} | <No Area>";
    else return $"{Num:D3}#fiber : {Tag} | <{Area.Tag}>";
}
```

4. Add constructor for point fiber (rebar):
```csharp
/// <summary>Создаёт точечное волокно (арматурный стержень).</summary>
public static Fiber CreatePoint(double diameter, double x, double y, double eps_p = 0)
{
    double r = diameter / 2;
    return new Fiber(x, y)
    {
        Diameter = diameter,
        Area = Math.PI * r * r,
        TypeFiber = FiberType.point,
        Eps_p = eps_p
    };
}
```

- [ ] **Step 2: Build**

```
dotnet build OpenCS.sln
```
Expected: errors in files referencing `Fiber.Region` — these are in `FiberRegion.cs` and `RCFiberRegion.cs` which will be deleted in Phase 5. For now the build will fail due to those references. **Acceptable at this step** — this is an intermediate state.

> **Note:** `Fiber.Region` references exist only in old classes scheduled for deletion. The build will be restored in Task 7 when MaterialArea is added and the old classes' `ToString` calls are updated.

Actually — to keep the build green throughout, leave `FiberRegion? Region` in place and ADD the new fields alongside. Remove in Phase 5:

Revised Step 1 — ADD only, don't remove:
```csharp
// Add after existing Region/RegionId fields:
public double Diameter { get; set; }
[JsonIgnore] public MaterialArea? Area { get; set; }
[JsonIgnore] public int AreaId { get; set; }

public static Fiber CreatePoint(double diameter, double x, double y, double eps_p = 0)
{
    double r = diameter / 2;
    return new Fiber(x, y)
    {
        Diameter = diameter,
        Area = Math.PI * r * r,
        TypeFiber = FiberType.point,
        Eps_p = eps_p
    };
}
```

And add `point = 3` to the `FiberType` enum.

- [ ] **Step 3: Build**

```
dotnet build OpenCS.sln
```
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 4: Commit**

```
git add CScore/Fiber.cs
git commit -m "feat(CScore): extend Fiber — point type, Diameter, MaterialArea ref"
```

---

### Task 3: Add `Diagramm.Differential()` factory

**Files:**
- Modify: `CScore/Diagramm.cs`

- [ ] **Step 1: Add static factory method**

Add at the end of the `Diagramm` class (before closing brace):

```csharp
/// <summary>
/// Создаёт разностную диаграмму: σ_eff(ε) = σ_steel(ε) − σ_concrete(ε).
/// Используется для арматурных областей, вложенных в бетон (брутто-сечение).
/// </summary>
public static Diagramm Differential(Diagramm steel, Diagramm concrete)
{
    return new Diagramm(
        new CSmath.DifferentialSpline(steel.Ic, concrete.Ic),
        new CSmath.DifferentialSpline(steel.It, concrete.It),
        steel.Type,
        steel.MaterialType,
        $"diff({steel.Tag}−{concrete.Tag})"
    );
}
```

- [ ] **Step 2: Build**

```
dotnet build OpenCS.sln
```
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 3: Commit**

```
git add CScore/Diagramm.cs
git commit -m "feat(CScore): add Diagramm.Differential() factory"
```

---

### Task 4: Create `MaterialArea`

**Files:**
- Create: `CScore/MaterialArea.cs`

- [ ] **Step 1: Create the file**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace CScore
{
    /// <summary>
    /// Материальная область поперечного сечения — единая замена для FiberRegion,
    /// RCFiberRegion и ReBarGroup. Содержит геометрию контуров, коллекцию волокон
    /// и словарь диаграмм работы материала по видам расчёта.
    /// Для арматурных областей (TypeFiber.point) Diagramms содержит разностные
    /// диаграммы (σ_сталь − σ_бетон_носителя).
    /// </summary>
    [Serializable]
    public class MaterialArea
    {
        public int Id { get; set; }
        public int Num { get; set; }
        public string Tag { get; set; } = "";
        public string? Description { get; set; }

        // Геометрия (null для чисто арматурных областей)
        public List<Contour> Contours { get; set; } = [];
        public string? WKT { get; set; }
        public double H { get; set; }

        // Параметры нарезки
        public int NX { get; set; } = 21;
        public int NY { get; set; } = 21;

        // Волокна: FiberType.poly / tri — из нарезки; FiberType.point — стержни
        public List<Fiber> Fibers { get; set; } = [];

        // Диаграммы работы материала по видам расчёта
        // Для арматурных областей с HostArea — разностные (σ_steel − σ_concrete)
        [JsonIgnore]
        public Dictionary<CalcType, Diagramm> Diagramms { get; set; } = [];

        // Ссылка на материал (для построения диаграмм и UI)
        [JsonIgnore] public Material? Material { get; set; }
        public int MaterialId { get; set; }

        // Ссылка на бетонную область-носитель (только для арматурных областей)
        [JsonIgnore] public MaterialArea? HostArea { get; set; }
        public int? HostAreaId { get; set; }

        public DiagrammType DiagrammType { get; set; } = DiagrammType.L2;

        // Внешний ключ к CrossSection
        [JsonIgnore] public int SectionId { get; set; }

        public MaterialArea() { }

        public override string ToString() =>
            Material == null
                ? $"{Num:D3}#MaterialArea : {Tag} | <No Material>"
                : $"{Num:D3}#MaterialArea : {Tag} | <{Material.Tag}>";

        /// <summary>Внешний контур области.</summary>
        [JsonIgnore]
        public Contour? Hull
        {
            get => Contours.FirstOrDefault(c => c.Type == ContourType.Hull);
            set
            {
                if (value == null) return;
                value.Type = ContourType.Hull;
                int idx = Contours.FindIndex(c => c.Type == ContourType.Hull);
                if (idx >= 0) Contours[idx] = value;
                else Contours.Insert(0, value);
            }
        }

        /// <summary>Отверстия области.</summary>
        [JsonIgnore]
        public IList<Contour> Holes =>
            Contours.Where(c => c.Type == ContourType.Hole).ToList();

        /// <summary>Обновляет WKT и H по текущему Hull.</summary>
        public void SetWKT()
        {
            if (Hull == null) return;
            var hullPts = Hull.X.Zip(Hull.Y, (x, y) => (x, y)).ToList();
            List<List<(double X, double Y)>>? holeRings = null;
            if (Holes.Count > 0)
            {
                holeRings = [];
                foreach (var h in Holes)
                    holeRings.Add(h.X.Zip(h.Y, (x, y) => (x, y)).ToList());
            }
            WKT = WktHelper.PolygonToWKT(Hull.X, Hull.Y, holeRings);
            if (Hull.X.Count > 0)
                H = Hull.Y.Max() - Hull.Y.Min();
        }

        /// <summary>Назначает материал и строит диаграммы.</summary>
        public void SetMaterial(Material material, DiagrammType diagrammType)
        {
            Material = material;
            DiagrammType = diagrammType;
            Diagramms = material.GetDiagramms(diagrammType);
        }

        /// <summary>
        /// Пересчитывает диаграммы после загрузки из БД.
        /// Для арматурной области с HostArea строит разностные диаграммы.
        /// </summary>
        public void ResolveAndBuildDiagramms()
        {
            if (Material == null) return;
            var own = Material.GetDiagramms(DiagrammType);

            if (HostArea != null && HostArea.Diagramms.Count > 0)
            {
                Diagramms = [];
                foreach (var ct in own.Keys)
                    Diagramms[ct] = Diagramm.Differential(own[ct], HostArea.Diagramms[ct]);
            }
            else
            {
                Diagramms = own;
            }
        }

        /// <summary>
        /// Вычисляет деформации и напряжения по кривизне плоскости деформаций.
        /// </summary>
        public void SetEps(Kurvature k, CalcType calc, bool ten = true, bool ca = true)
        {
            if (!Diagramms.TryGetValue(calc, out var dgr)) return;

            foreach (var f in Fibers)
                f.Eps = k.e0 + k.ky * f.Y + k.kz * f.X;

            for (int i = 0; i < Fibers.Count; i++)
                dgr.Sig(Fibers[i], ten, ca);

            if (Hull != null)
                foreach (var pt in Hull.Points)
                {
                    pt.Eps = k.e0 + k.ky * pt.Y + k.kz * pt.X;
                    dgr.Sig(pt, ten, ca);
                }
        }

        /// <summary>
        /// Фабричный метод: создаёт арматурную область с дифференциальными диаграммами.
        /// </summary>
        public static MaterialArea CreateRebarArea(
            IEnumerable<Fiber> bars,
            Material steelMaterial,
            DiagrammType steelDiagrammType,
            MaterialArea? hostConcreteArea)
        {
            var area = new MaterialArea
            {
                Material = steelMaterial,
                MaterialId = steelMaterial.Id,
                DiagrammType = steelDiagrammType,
                HostArea = hostConcreteArea,
                HostAreaId = hostConcreteArea?.Id,
                Fibers = bars.ToList()
            };
            area.ResolveAndBuildDiagramms();
            return area;
        }

        /// <summary>
        /// Автоопределение HostArea по вхождению точечных волокон в контуры бетонных областей.
        /// </summary>
        public static void AutoResolveHostAreas(IEnumerable<MaterialArea> allAreas)
        {
            var concreteAreas = allAreas
                .Where(a => a.Material?.Type == MatType.Concrete && a.WKT != null)
                .ToList();

            var rebarAreas = allAreas
                .Where(a => a.Fibers.Any(f => f.TypeFiber == FiberType.point))
                .ToList();

            foreach (var rebar in rebarAreas)
            {
                if (rebar.HostAreaId != null) continue; // уже назначено вручную
                foreach (var conc in concreteAreas)
                {
                    bool allInside = rebar.Fibers
                        .Where(f => f.TypeFiber == FiberType.point)
                        .All(f => WktHelper.PointInPolygon(conc.WKT!, f.X, f.Y));
                    if (allInside)
                    {
                        rebar.HostArea = conc;
                        rebar.HostAreaId = conc.Id;
                        break;
                    }
                }
                rebar.ResolveAndBuildDiagramms();
            }
        }

        /// <summary>Разбивает область на волокна методом триангуляции.</summary>
        public void Triangulate(double maxTrgArea = 0.01, double maxAngl = 30)
        {
            Fiber[] res = Geo.Triangulation(this, maxTrgArea, maxAngl);
            Fibers = [.. res];
        }

        /// <summary>Разбивает область на волокна прямоугольной сеткой.</summary>
        public void SliceXY(int nx = 0, int ny = 0)
        {
            int usedNx = nx > 0 ? nx : NX;
            int usedNy = ny > 0 ? ny : NY;
            Fiber[] res = GridSplit.SliceXY(this, usedNx, usedNy);
            Fibers = [.. res];
        }

        /// <summary>Начальное приближение кривизны (упругая стадия).</summary>
        public Kurvature Guess(Load load)
        {
            var pr = new GeoProps(this);
            return new Kurvature
            {
                e0 = load.N / pr.EA,
                ky = load.My / pr.EIy,
                kz = load.Mz / pr.EIx
            };
        }

        public MaterialArea Clone()
        {
            var clone = new MaterialArea
            {
                Tag = Tag, Description = Description,
                NX = NX, NY = NY, WKT = WKT, H = H,
                Material = Material, MaterialId = MaterialId,
                DiagrammType = DiagrammType,
                HostArea = HostArea, HostAreaId = HostAreaId,
                Diagramms = new Dictionary<CalcType, Diagramm>(Diagramms),
                Contours = [.. Contours],
                Fibers = Fibers.Select(f => f.Clone()).ToList()
            };
            return clone;
        }
    }
}
```

> **Note:** `Geo.Triangulation(this, ...)` and `GridSplit.SliceXY(this, ...)` will work after Task 5 updates those methods to accept `MaterialArea`. Add a `#pragma warning disable` or leave as compile error until Task 5.

Для компиляции на данном шаге — временно закомментируй методы `Triangulate()` и `SliceXY()` в теле класса:
```csharp
// Временно: раскомментировать после Task 5
// public void Triangulate(...) { ... }
// public void SliceXY(...) { ... }
```

- [ ] **Step 2: Build**

```
dotnet build OpenCS.sln
```
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 3: Commit**

```
git add CScore/MaterialArea.cs
git commit -m "feat(CScore): add MaterialArea class"
```

---

### Task 5: Update `Geo.cs` and `GridSplit.cs` to accept `MaterialArea`

**Files:**
- Modify: `CScore/Geo.cs`
- Modify: `CScore/GridSplit.cs`

Both files have public static methods that take `Region region` as first parameter. `MaterialArea` has the same properties: `Hull`, `Contours`, `WKT`, `H`.

- [ ] **Step 1: Update Geo.cs — change `Region` → `MaterialArea` in all public signatures**

In `CScore/Geo.cs`, change all occurrences of `Region region` parameter to `MaterialArea region` (the body does not change — it uses `region.Hull`, `region.Contours`, etc. which exist on `MaterialArea`):

```csharp
public static Fiber[] SliceXY(MaterialArea region, int nx = 40, int ny = 40)
public static Fiber[] SliceY(MaterialArea region, int ny = 40)
public static Fiber[] SliceX(MaterialArea region, int nx = 40)
public static Fiber[] Triangulation(MaterialArea region, double maxTrgArea = 0.01,
    double maxAngl = 30, double scale = 8, ...)
static Fiber[] TriangulationRuppert(MaterialArea region, ...)
static Fiber[] TriangulationAdvancingFront(MaterialArea region, ...)
```

- [ ] **Step 2: Update GridSplit.cs — same change**

In `CScore/GridSplit.cs`, change all `Region region` → `MaterialArea region`:

```csharp
public static Fiber[] Slice(MaterialArea region, int nx = 0, int ny = 0)
public static Fiber[] SliceXY(MaterialArea region, int nx = 40, int ny = 40)
public static Fiber[] SliceY(MaterialArea region, int ny = 40)
public static Fiber[] SliceX(MaterialArea region, int nx = 40)
```

- [ ] **Step 3: Uncomment `Triangulate()` and `SliceXY()` in `MaterialArea.cs`**

Remove the `//` comments from the two methods added in Task 4.

- [ ] **Step 4: Also update `Region.cs` methods to compile** — since `Region.Triangulation()` calls `Geo.Triangulation(this, ...)` and `this` is a `Region`, not a `MaterialArea`, there will be a compile error. Fix by temporarily keeping a `Region`-based overload in `Geo.cs`:

Add private adapter in `Geo.cs`:
```csharp
// Временный переходный адаптер — удалить в Phase 5 вместе с Region
internal static Fiber[] Triangulation(Region region, double maxTrgArea = 0.01,
    double maxAngl = 30, double scale = 8, bool useRuppert = true)
{
    // создаём временный MaterialArea-адаптер из Region
    var ma = new MaterialArea
    {
        Tag = region.Tag,
        Contours = region.Contours,
        WKT = region.WKT,
        H = region.H
    };
    return Triangulation(ma, maxTrgArea, maxAngl, scale, useRuppert);
}
```

Do the same adapter pattern for `GridSplit.cs` where needed.

- [ ] **Step 5: Build**

```
dotnet build OpenCS.sln
```
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 6: Commit**

```
git add CScore/Geo.cs CScore/GridSplit.cs CScore/MaterialArea.cs
git commit -m "feat(CScore): update Geo/GridSplit to accept MaterialArea"
```

---

### Task 6: Add `GeoProps(MaterialArea)` and `GeoProps(CrossSection)` constructors

**Files:**
- Modify: `CScore/GeoProps.cs`

(Note: `CrossSection` doesn't exist yet — add placeholder using `List<MaterialArea>`. Full version completed in Task 8.)

- [ ] **Step 1: Add `GeoProps(MaterialArea)` constructor**

Add after the existing `GeoProps(FiberRegion)` constructor:

```csharp
/// <summary>
/// Вычисляет геометрические характеристики материальной области
/// суммированием по волокнам (включая точечные).
/// </summary>
public GeoProps(MaterialArea area, GeoPropsType propsType = GeoPropsType.First)
{
    double E;
    foreach (var f in area.Fibers)
    {
        E = propsType == GeoPropsType.First ? f.E : f.E2;
        if (E == 0 && area.Material != null) E = area.Material.E;
        double a  = f.Area;
        double sy = f.Area * f.X;
        double sx = f.Area * f.Y;
        double iy = sy * f.X;
        double ix = sx * f.Y;
        double ixy = a * f.X * f.Y;
        A   += a;   Sy  += sy;  Sx  += sx;
        Iy  += iy;  Ix  += ix;  Ixy += ixy;
        EA  += a * E;  ESy += sy * E;  ESx += sx * E;
        EIy += iy * E; EIx += ix * E; EIxy += ixy * E;
    }
    if (EA > 0) Centroid = new XY(ESy / EA, ESx / EA);
}
```

- [ ] **Step 2: Build**

```
dotnet build OpenCS.sln
```
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 3: Commit**

```
git add CScore/GeoProps.cs
git commit -m "feat(CScore): add GeoProps(MaterialArea) constructor"
```

---

### Task 7: Create `CrossSection` and `TwoStageSection`

**Files:**
- Create: `CScore/CrossSection.cs`
- Create: `CScore/TwoStageSection.cs`

- [ ] **Step 1: Create `CrossSection.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CScore
{
    /// <summary>
    /// Поперечное сечение — контейнер материальных областей с единым интегралом.
    /// Минимум одна MaterialArea.
    /// </summary>
    [Serializable]
    public class CrossSection
    {
        public int Id { get; set; }
        public int Num { get; set; }
        public string Tag { get; set; } = "";
        public string? Description { get; set; }

        public List<MaterialArea> Areas { get; set; } = [];

        public CrossSection() { }

        public override string ToString() => $"{Num:D3}#CrossSection : {Tag}";

        /// <summary>
        /// Вычисляет деформации и напряжения во всех областях по кривизне.
        /// </summary>
        public virtual void SetEps(Kurvature k, CalcType calc,
                                    bool ten = true, bool ca = true)
        {
            foreach (var area in Areas)
                area.SetEps(k, calc, ten, ca);
        }

        /// <summary>
        /// Единый интеграл по всем областям (бетон + арматура с разностными диаграммами).
        /// </summary>
        public virtual Load Integral(Kurvature k, CalcType calc = CalcType.C,
                                      bool ten = true, bool ca = true)
        {
            SetEps(k, calc, ten, ca);
            double N = 0, Mx = 0, My = 0;
            foreach (var area in Areas)
                foreach (var f in area.Fibers)
                { N += f.N; Mx += f.My; My += f.Mz; }
            return new Load { Calc = calc, N = N, My = Mx, Mz = My };
        }

        /// <summary>Интеграл + геометрические характеристики.</summary>
        public virtual Load Integral(Kurvature k, out GeoProps props,
                                      CalcType calc = CalcType.C,
                                      bool ten = true, bool ca = true)
        {
            var load = Integral(k, calc, ten, ca);
            props = new GeoProps(this);
            return load;
        }

        /// <summary>Начальное приближение кривизны (упругая стадия).</summary>
        public Kurvature Guess(Load load)
        {
            var pr = new GeoProps(this);
            return new Kurvature
            {
                e0 = load.N / pr.EA,
                ky = load.My / pr.EIy,
                kz = load.Mz / pr.EIx
            };
        }

        /// <summary>
        /// Строит диаграммы для всех областей после загрузки из БД.
        /// </summary>
        public void ResolveAndBuildDiagramms()
        {
            // Сначала — бетонные области (нет HostArea)
            foreach (var area in Areas)
                if (area.HostAreaId == null)
                    area.ResolveAndBuildDiagramms();

            // Затем — арматурные (зависят от бетонных)
            foreach (var area in Areas)
                if (area.HostAreaId != null)
                {
                    area.HostArea = Areas.Find(a => a.Id == area.HostAreaId);
                    area.ResolveAndBuildDiagramms();
                }
        }
    }
}
```

- [ ] **Step 2: Add `GeoProps(CrossSection)` constructor to `GeoProps.cs`**

Add after `GeoProps(MaterialArea)`:

```csharp
/// <summary>Геометрические характеристики составного сечения.</summary>
public GeoProps(CrossSection section, GeoPropsType propsType = GeoPropsType.First)
{
    foreach (var area in section.Areas)
    {
        var ap = new GeoProps(area, propsType);
        A   += ap.A;   Sy  += ap.Sy;  Sx  += ap.Sx;
        Iy  += ap.Iy;  Ix  += ap.Ix;  Ixy += ap.Ixy;
        EA  += ap.EA;  ESy += ap.ESy; ESx += ap.ESx;
        EIy += ap.EIy; EIx += ap.EIx; EIxy += ap.EIxy;
    }
    if (EA > 0) Centroid = new XY(ESy / EA, ESx / EA);
}
```

- [ ] **Step 3: Create `TwoStageSection.cs`**

```csharp
using System;
using System.Text.Json.Serialization;

namespace CScore
{
    /// <summary>
    /// Двухэтапное поперечное сечение — для сборно-монолитных конструкций
    /// и усиления ЖБ сечений. Этап 1 имеет замороженную маску деформаций
    /// (Stage1Kurvature). Деформация волокон этапа 1: ε_total = ε_current + ε_stage1.
    /// </summary>
    [Serializable]
    public class TwoStageSection : CrossSection
    {
        /// <summary>Сечение первого этапа (до усиления / омоноличивания).</summary>
        public CrossSection Stage1 { get; set; } = new();

        /// <summary>
        /// Замороженная кривизна плоскости деформаций от нагрузки первого этапа.
        /// Задаётся после предварительного расчёта сечения первого этапа.
        /// </summary>
        public Kurvature Stage1Kurvature { get; set; }

        /// <summary>
        /// Id сечения первого этапа в БД (разрешается при загрузке).
        /// </summary>
        public int Stage1SectionId { get; set; }

        public TwoStageSection() { }

        /// <inheritdoc/>
        public override Load Integral(Kurvature k, CalcType calc = CalcType.C,
                                       bool ten = true, bool ca = true)
        {
            double N = 0, Mx = 0, My = 0;

            // Этап 1: ε_total = ε_current + ε_stage1 (замороженная маска)
            Kurvature k1 = k + Stage1Kurvature;
            foreach (var area in Stage1.Areas)
            {
                area.SetEps(k1, calc, ten, ca);
                foreach (var f in area.Fibers)
                { N += f.N; Mx += f.My; My += f.Mz; }
            }

            // Этап 2: ε_total = ε_current (Areas из базового CrossSection)
            foreach (var area in Areas)
            {
                area.SetEps(k, calc, ten, ca);
                foreach (var f in area.Fibers)
                { N += f.N; Mx += f.My; My += f.Mz; }
            }

            return new Load { Calc = calc, N = N, My = Mx, Mz = My };
        }

        /// <inheritdoc/>
        public override void SetEps(Kurvature k, CalcType calc,
                                     bool ten = true, bool ca = true)
        {
            Kurvature k1 = k + Stage1Kurvature;
            foreach (var area in Stage1.Areas)
                area.SetEps(k1, calc, ten, ca);
            foreach (var area in Areas)
                area.SetEps(k, calc, ten, ca);
        }
    }
}
```

- [ ] **Step 4: Build**

```
dotnet build OpenCS.sln
```
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 5: Commit**

```
git add CScore/CrossSection.cs CScore/TwoStageSection.cs CScore/GeoProps.cs
git commit -m "feat(CScore): add CrossSection, TwoStageSection; GeoProps(CrossSection)"
```

---

## Phase 2 — Database

### Task 8: New database schema in `DatabaseService`

**Files:**
- Modify: `OpenCS/Utilites/DatabaseService.cs`

- [ ] **Step 1: Add new public collections**

In `DatabaseService`, add alongside existing collections:
```csharp
public ObservableCollection<CrossSection> CrossSections { get; } = [];
```

- [ ] **Step 2: Add `CreateNewTables()` method**

```csharp
void CreateCrossSectionTables(SqliteConnection conn)
{
    using var cmd = conn.CreateCommand();
    cmd.CommandText = """
        CREATE TABLE IF NOT EXISTS cross_sections (
            id          INTEGER PRIMARY KEY AUTOINCREMENT,
            num         INTEGER NOT NULL DEFAULT 0,
            tag         TEXT NOT NULL DEFAULT '',
            description TEXT,
            type        TEXT NOT NULL DEFAULT 'simple'
        );
        CREATE TABLE IF NOT EXISTS cross_section_stages (
            section_id        INTEGER NOT NULL REFERENCES cross_sections(id) ON DELETE CASCADE,
            stage1_section_id INTEGER NOT NULL REFERENCES cross_sections(id),
            e0                REAL NOT NULL DEFAULT 0,
            ky                REAL NOT NULL DEFAULT 0,
            kz                REAL NOT NULL DEFAULT 0
        );
        CREATE TABLE IF NOT EXISTS material_areas (
            id             INTEGER PRIMARY KEY AUTOINCREMENT,
            section_id     INTEGER NOT NULL REFERENCES cross_sections(id) ON DELETE CASCADE,
            num            INTEGER NOT NULL DEFAULT 0,
            tag            TEXT NOT NULL DEFAULT '',
            description    TEXT,
            material_id    INTEGER REFERENCES materials(id),
            host_area_id   INTEGER REFERENCES material_areas(id),
            diagramm_type  TEXT NOT NULL DEFAULT 'L2',
            nx             INTEGER NOT NULL DEFAULT 21,
            ny             INTEGER NOT NULL DEFAULT 21,
            wkt            TEXT
        );
        CREATE TABLE IF NOT EXISTS point_fibers (
            id          INTEGER PRIMARY KEY AUTOINCREMENT,
            area_id     INTEGER NOT NULL REFERENCES material_areas(id) ON DELETE CASCADE,
            x           REAL NOT NULL DEFAULT 0,
            y           REAL NOT NULL DEFAULT 0,
            area        REAL NOT NULL DEFAULT 0,
            diameter    REAL NOT NULL DEFAULT 0,
            eps_p       REAL NOT NULL DEFAULT 0
        );
    """;
    cmd.ExecuteNonQuery();
}
```

Call `CreateCrossSectionTables(conn)` inside the existing `CreateTables()` or `OpenOrCreate()` method.

- [ ] **Step 3: Build**

```
dotnet build OpenCS.sln
```
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 4: Commit**

```
git add OpenCS/Utilites/DatabaseService.cs
git commit -m "feat(DB): add cross_sections / material_areas / point_fibers tables"
```

---

### Task 9: `LoadCrossSections()` in `DatabaseService`

**Files:**
- Modify: `OpenCS/Utilites/DatabaseService.cs`

- [ ] **Step 1: Add load method**

```csharp
void LoadCrossSections()
{
    CrossSections.Clear();

    using var conn = new SqliteConnection(_connectionString);
    conn.Open();

    // Загружаем сечения
    var sections = new Dictionary<int, CrossSection>();
    using (var cmd = conn.CreateCommand())
    {
        cmd.CommandText = "SELECT id, num, tag, description, type FROM cross_sections ORDER BY num";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            CrossSection cs = r.GetString(4) == "two_stage"
                ? new TwoStageSection()
                : new CrossSection();
            cs.Id = r.GetInt32(0);
            cs.Num = r.GetInt32(1);
            cs.Tag = r.GetString(2);
            cs.Description = r.IsDBNull(3) ? null : r.GetString(3);
            sections[cs.Id] = cs;
        }
    }

    // Загружаем области
    var areas = new Dictionary<int, MaterialArea>();
    using (var cmd = conn.CreateCommand())
    {
        cmd.CommandText = """
            SELECT id, section_id, num, tag, description,
                   material_id, host_area_id, diagramm_type, nx, ny, wkt
            FROM material_areas ORDER BY section_id, num
        """;
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var area = new MaterialArea
            {
                Id = r.GetInt32(0),
                SectionId = r.GetInt32(1),
                Num = r.GetInt32(2),
                Tag = r.GetString(3),
                Description = r.IsDBNull(4) ? null : r.GetString(4),
                MaterialId = r.IsDBNull(5) ? 0 : r.GetInt32(5),
                HostAreaId = r.IsDBNull(6) ? null : r.GetInt32(6),
                DiagrammType = Enum.Parse<DiagrammType>(r.GetString(7)),
                NX = r.GetInt32(8),
                NY = r.GetInt32(9),
                WKT = r.IsDBNull(10) ? null : r.GetString(10)
            };
            if (area.WKT != null)
            {
                WktHelper.ParseWKTPolygon(area.WKT,
                    out var outerX, out var outerY,
                    out var holeXs, out var holeYs);
                var hull = new Contour(outerX, outerY, "hull");
                hull.Type = ContourType.Hull;
                area.Contours.Add(hull);
                if (holeXs != null)
                    for (int j = 0; j < holeXs.Count; j++)
                    {
                        var hole = new Contour(holeXs[j], holeYs[j], $"hole{j}");
                        hole.Type = ContourType.Hole;
                        area.Contours.Add(hole);
                    }
            }
            areas[area.Id] = area;
            if (sections.TryGetValue(area.SectionId, out var sec))
                sec.Areas.Add(area);
        }
    }

    // Загружаем точечные волокна (стержни)
    using (var cmd = conn.CreateCommand())
    {
        cmd.CommandText = "SELECT area_id, x, y, area, diameter, eps_p FROM point_fibers";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            int areaId = r.GetInt32(0);
            if (!areas.TryGetValue(areaId, out var area)) continue;
            var f = new Fiber(r.GetDouble(1), r.GetDouble(2))
            {
                Area = r.GetDouble(3),
                Diameter = r.GetDouble(4),
                Eps_p = r.GetDouble(5),
                TypeFiber = FiberType.point
            };
            area.Fibers.Add(f);
        }
    }

    // Загружаем Stage1 для TwoStageSection
    using (var cmd = conn.CreateCommand())
    {
        cmd.CommandText = "SELECT section_id, stage1_section_id, e0, ky, kz FROM cross_section_stages";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            int sId = r.GetInt32(0); int s1Id = r.GetInt32(1);
            if (sections.TryGetValue(sId, out var sec) && sec is TwoStageSection tss
                && sections.TryGetValue(s1Id, out var stage1))
            {
                tss.Stage1 = stage1;
                tss.Stage1SectionId = s1Id;
                tss.Stage1Kurvature = new Kurvature
                { e0 = r.GetDouble(2), ky = r.GetDouble(3), kz = r.GetDouble(4) };
            }
        }
    }

    foreach (var sec in sections.Values)
        CrossSections.Add(sec);
}
```

- [ ] **Step 2: Add `ResolveReferencesForCrossSections()` call after loading materials**

After existing `ResolveReferences()` call in the load sequence, add:
```csharp
void ResolveReferencesForCrossSections()
{
    foreach (var sec in CrossSections)
    {
        foreach (var area in sec.Areas)
            area.Material = Materials.FirstOrDefault(m => m.Id == area.MaterialId);
        sec.ResolveAndBuildDiagramms();
        if (sec is TwoStageSection tss)
            foreach (var area in tss.Stage1.Areas)
                area.Material = Materials.FirstOrDefault(m => m.Id == area.MaterialId);
    }
}
```

- [ ] **Step 3: Call `LoadCrossSections()` and `ResolveReferencesForCrossSections()` in `Load()`**

In the existing `Load()` method, after `LoadMaterials()` and `ResolveReferences()`:
```csharp
LoadCrossSections();
ResolveReferencesForCrossSections();
```

- [ ] **Step 4: Build**

```
dotnet build OpenCS.sln
```
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 5: Commit**

```
git add OpenCS/Utilites/DatabaseService.cs
git commit -m "feat(DB): add LoadCrossSections() and reference resolution"
```

---

### Task 10: `SaveCrossSection()` and `DeleteCrossSection()` in `DatabaseService`

**Files:**
- Modify: `OpenCS/Utilites/DatabaseService.cs`

- [ ] **Step 1: Add `SaveCrossSection()`**

```csharp
public void SaveCrossSection(CrossSection section)
{
    using var conn = new SqliteConnection(_connectionString);
    conn.Open();
    using var tx = conn.BeginTransaction();

    // Upsert cross_section
    using (var cmd = conn.CreateCommand())
    {
        bool isNew = section.Id == 0;
        if (isNew)
        {
            cmd.CommandText = """
                INSERT INTO cross_sections (num, tag, description, type)
                VALUES (@num, @tag, @desc, @type);
                SELECT last_insert_rowid();
            """;
        }
        else
        {
            cmd.CommandText = """
                UPDATE cross_sections SET num=@num, tag=@tag, description=@desc, type=@type
                WHERE id=@id;
            """;
            cmd.Parameters.AddWithValue("@id", section.Id);
        }
        cmd.Parameters.AddWithValue("@num", section.Num);
        cmd.Parameters.AddWithValue("@tag", section.Tag);
        cmd.Parameters.AddWithValue("@desc", (object?)section.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@type",
            section is TwoStageSection ? "two_stage" : "simple");
        if (isNew) section.Id = (int)(long)cmd.ExecuteScalar()!;
        else cmd.ExecuteNonQuery();
    }

    // Удаляем старые области (каскадно удалятся point_fibers)
    using (var cmd = conn.CreateCommand())
    {
        cmd.CommandText = "DELETE FROM material_areas WHERE section_id = @sid";
        cmd.Parameters.AddWithValue("@sid", section.Id);
        cmd.ExecuteNonQuery();
    }

    // Сохраняем области
    var allAreas = new List<MaterialArea>(section.Areas);
    if (section is TwoStageSection tss2)
    {
        // Сначала сохраняем Stage1 как отдельное сечение
        SaveCrossSection(tss2.Stage1);
        allAreas.AddRange(tss2.Stage1.Areas); // для сохранения волокон
    }

    foreach (var area in section.Areas)
        SaveMaterialArea(area, section.Id, conn);

    if (section is TwoStageSection tss3)
    {
        foreach (var area in tss3.Stage1.Areas)
            SaveMaterialArea(area, tss3.Stage1.Id, conn);

        // Upsert stage link
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            DELETE FROM cross_section_stages WHERE section_id = @sid;
            INSERT INTO cross_section_stages (section_id, stage1_section_id, e0, ky, kz)
            VALUES (@sid, @s1id, @e0, @ky, @kz);
        """;
        cmd.Parameters.AddWithValue("@sid", tss3.Id);
        cmd.Parameters.AddWithValue("@s1id", tss3.Stage1.Id);
        cmd.Parameters.AddWithValue("@e0", tss3.Stage1Kurvature.e0);
        cmd.Parameters.AddWithValue("@ky", tss3.Stage1Kurvature.ky);
        cmd.Parameters.AddWithValue("@kz", tss3.Stage1Kurvature.kz);
        cmd.ExecuteNonQuery();
    }

    tx.Commit();
}

void SaveMaterialArea(MaterialArea area, int sectionId, SqliteConnection conn)
{
    using var cmd = conn.CreateCommand();
    cmd.CommandText = """
        INSERT INTO material_areas
            (section_id, num, tag, description, material_id,
             host_area_id, diagramm_type, nx, ny, wkt)
        VALUES (@sid, @num, @tag, @desc, @mid, @hid, @dtype, @nx, @ny, @wkt);
        SELECT last_insert_rowid();
    """;
    cmd.Parameters.AddWithValue("@sid", sectionId);
    cmd.Parameters.AddWithValue("@num", area.Num);
    cmd.Parameters.AddWithValue("@tag", area.Tag);
    cmd.Parameters.AddWithValue("@desc", (object?)area.Description ?? DBNull.Value);
    cmd.Parameters.AddWithValue("@mid", area.MaterialId == 0 ? DBNull.Value : area.MaterialId);
    cmd.Parameters.AddWithValue("@hid", (object?)area.HostAreaId ?? DBNull.Value);
    cmd.Parameters.AddWithValue("@dtype", area.DiagrammType.ToString());
    cmd.Parameters.AddWithValue("@nx", area.NX);
    cmd.Parameters.AddWithValue("@ny", area.NY);
    cmd.Parameters.AddWithValue("@wkt", (object?)area.WKT ?? DBNull.Value);
    area.Id = (int)(long)cmd.ExecuteScalar()!;

    // Сохраняем точечные волокна
    foreach (var f in area.Fibers.Where(f => f.TypeFiber == FiberType.point))
    {
        using var fc = conn.CreateCommand();
        fc.CommandText = """
            INSERT INTO point_fibers (area_id, x, y, area, diameter, eps_p)
            VALUES (@aid, @x, @y, @a, @d, @ep);
        """;
        fc.Parameters.AddWithValue("@aid", area.Id);
        fc.Parameters.AddWithValue("@x", f.X);
        fc.Parameters.AddWithValue("@y", f.Y);
        fc.Parameters.AddWithValue("@a", f.Area);
        fc.Parameters.AddWithValue("@d", f.Diameter);
        fc.Parameters.AddWithValue("@ep", f.Eps_p);
        fc.ExecuteNonQuery();
    }
}
```

- [ ] **Step 2: Add `DeleteCrossSection()`**

```csharp
public void DeleteCrossSection(CrossSection section)
{
    if (section.Id == 0) return;
    using var conn = new SqliteConnection(_connectionString);
    conn.Open();
    using var cmd = conn.CreateCommand();
    // Каскадное удаление через ON DELETE CASCADE
    cmd.CommandText = "DELETE FROM cross_sections WHERE id = @id";
    cmd.Parameters.AddWithValue("@id", section.Id);
    cmd.ExecuteNonQuery();
    CrossSections.Remove(section);
}
```

- [ ] **Step 3: Build**

```
dotnet build OpenCS.sln
```
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 4: Commit**

```
git add OpenCS/Utilites/DatabaseService.cs
git commit -m "feat(DB): add SaveCrossSection() and DeleteCrossSection()"
```

---

## Phase 3 — ViewModel

### Task 11: Create `MaterialAreaVM`

**Files:**
- Create: `OpenCS/ViewModels/MaterialAreaVM.cs`

- [ ] **Step 1: Create the file**

```csharp
using CScore;
using OpenCS.Services;
using OpenCS.Utilites;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace OpenCS.ViewModels
{
    /// <summary>ViewModel для MaterialArea.</summary>
    public class MaterialAreaVM : ViewModelBase
    {
        MaterialArea _model;

        public MaterialAreaVM(MaterialArea model, AppViewModel app)
        {
            _model = model;
            App = app;
            RemoveAreaCommand = new RelayCommand(_ => App.RemoveMaterialArea(this));
        }

        public AppViewModel App { get; }
        public MaterialArea Model => _model;

        public string Tag
        {
            get => _model.Tag;
            set { _model.Tag = value; OnPropertyChanged(); }
        }

        public Material? Material
        {
            get => _model.Material;
            set
            {
                _model.Material = value;
                _model.MaterialId = value?.Id ?? 0;
                _model.ResolveAndBuildDiagramms();
                OnPropertyChanged();
                OnPropertyChanged(nameof(MaterialType));
            }
        }

        public MatType MaterialType => _model.Material?.Type ?? MatType.None;

        public DiagrammType DiagrammType
        {
            get => _model.DiagrammType;
            set
            {
                _model.DiagrammType = value;
                _model.ResolveAndBuildDiagramms();
                OnPropertyChanged();
            }
        }

        public ICommand RemoveAreaCommand { get; }
    }
}
```

- [ ] **Step 2: Add stub `RemoveMaterialArea` to `AppViewModel`** (to make it compile — full impl in Task 12):

```csharp
public void RemoveMaterialArea(MaterialAreaVM vm)
{
    // реализация в Task 12
}
```

- [ ] **Step 3: Build**

```
dotnet build OpenCS.sln
```
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 4: Commit**

```
git add OpenCS/ViewModels/MaterialAreaVM.cs OpenCS/AppViewModel.cs
git commit -m "feat(VM): add MaterialAreaVM"
```

---

### Task 12: Create `CrossSectionVM` and wire `AppViewModel`

**Files:**
- Create: `OpenCS/ViewModels/CrossSectionVM.cs`
- Modify: `OpenCS/AppViewModel.cs`

- [ ] **Step 1: Create `CrossSectionVM.cs`**

```csharp
using CScore;
using OpenCS.Utilites;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace OpenCS.ViewModels
{
    public class CrossSectionVM : ViewModelBase
    {
        readonly CrossSection _model;

        public CrossSectionVM(CrossSection model, AppViewModel app)
        {
            _model = model;
            App = app;
            Areas = new ObservableCollection<MaterialAreaVM>(
                model.Areas.Select(a => new MaterialAreaVM(a, app)));

            AddConcreteAreaCommand    = new RelayCommand(_ => AddArea(MatType.Concrete));
            AddRebarAreaCommand       = new RelayCommand(_ => AddArea(MatType.ReSteelF));
            AddSteelAreaCommand       = new RelayCommand(_ => AddArea(MatType.Steel));
        }

        public AppViewModel App { get; }
        public CrossSection Model => _model;

        public string Tag
        {
            get => _model.Tag;
            set { _model.Tag = value; OnPropertyChanged(); }
        }

        public ObservableCollection<MaterialAreaVM> Areas { get; }

        public ICommand AddConcreteAreaCommand { get; }
        public ICommand AddRebarAreaCommand { get; }
        public ICommand AddSteelAreaCommand { get; }

        void AddArea(MatType type)
        {
            var area = new MaterialArea { Tag = $"Область {Areas.Count + 1}" };
            _model.Areas.Add(area);
            var vm = new MaterialAreaVM(area, App);
            Areas.Add(vm);
            App.IsDirty = true;
        }
    }
}
```

- [ ] **Step 2: Update `AppViewModel.cs`**

Add new fields and collection:

```csharp
// --- Новые поля (добавить рядом с существующими коллекциями) ---
ObservableCollection<CrossSection> crossSectionsLive;
CrossSection? currentCrossSection;

public ObservableCollection<CrossSection> CrossSections { get; set; }
public ObservableCollection<CrossSection> CrossSectionsLive
{
    get => crossSectionsLive;
    set { crossSectionsLive = value; OnPropertyChanged(); }
}

public CrossSection? CurrentCrossSection
{
    get => currentCrossSection;
    set
    {
        currentCrossSection = value;
        CurrentPage = value != null
            ? new Views.CrossSectionView(value, this)
            : null;
        OnPropertyChanged();
    }
}

public ICommand NewCrossSectionCommand { get; set; }
public ICommand EditCrossSectionCommand { get; set; }
public ICommand DeleteCrossSectionCommand { get; set; }
```

In the constructor, after existing initialization:

```csharp
CrossSections = db.CrossSections;
CrossSections.CollectionChanged += (_, _) => IsDirty = true;
CrossSectionsLive = new ObservableCollection<CrossSection>(CrossSections);
CrossSectionsRenumber();

NewCrossSectionCommand    = new RelayCommand(_ => NewCrossSection());
EditCrossSectionCommand   = new RelayCommand(_ => EditCrossSection());
DeleteCrossSectionCommand = new RelayCommand(_ => DeleteCrossSection());
```

Add methods:
```csharp
void CrossSectionsRenumber()
{
    for (int i = 0; i < CrossSections.Count; i++)
        CrossSections[i].Num = i + 1;
}

void NewCrossSection()
{
    CurrentPage = new Views.CrossSectionPage(this);
}

void EditCrossSection()
{
    if (currentCrossSection == null) return;
    CurrentPage = new Views.CrossSectionPage(currentCrossSection, this);
}

void DeleteCrossSection()
{
    if (currentCrossSection == null) return;
    db.DeleteCrossSection(currentCrossSection);
    CrossSections.Remove(currentCrossSection);
    CrossSectionsLive = new ObservableCollection<CrossSection>(CrossSections);
    CrossSectionsRenumber();
    currentCrossSection = null;
    CurrentPage = null;
    OnPropertyChanged(nameof(CurrentCrossSection));
}

public void RemoveMaterialArea(MaterialAreaVM vm)
{
    var sec = CrossSections.FirstOrDefault(s => s.Areas.Contains(vm.Model));
    if (sec == null) return;
    sec.Areas.Remove(vm.Model);
    IsDirty = true;
}
```

Also update `SaveProject()` to include cross sections:
```csharp
foreach (var sec in CrossSections)
    db.SaveCrossSection(sec);
```

- [ ] **Step 3: Build**

```
dotnet build OpenCS.sln
```
Expected: errors on missing `Views.CrossSectionView` and `Views.CrossSectionPage` — these will be created in Phase 4. Add temporary stubs:

In `OpenCS/Views/`, create placeholder files:

`CrossSectionView.xaml.cs` stub:
```csharp
namespace OpenCS.Views
{
    public partial class CrossSectionView : System.Windows.Controls.UserControl
    {
        public CrossSectionView(CScore.CrossSection section, OpenCS.AppViewModel app)
        { InitializeComponent(); }
    }
}
```
`CrossSectionPage.xaml.cs` stub (same pattern).

Create minimal XAML for each (just `<UserControl>` tags). Full XAML is in Phase 4.

- [ ] **Step 4: Build**

```
dotnet build OpenCS.sln
```
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 5: Commit**

```
git add OpenCS/ViewModels/CrossSectionVM.cs OpenCS/AppViewModel.cs
git add OpenCS/Views/CrossSectionView.xaml OpenCS/Views/CrossSectionView.xaml.cs
git add OpenCS/Views/CrossSectionPage.xaml OpenCS/Views/CrossSectionPage.xaml.cs
git commit -m "feat(VM): add CrossSectionVM, wire AppViewModel.CrossSections"
```

---

## Phase 4 — UI

### Task 13: Add localization strings

**Files:**
- Modify: `OpenCS/Resources/Strings.ru-RU.xaml`
- Modify: `OpenCS/Resources/Strings.en-US.xaml`

- [ ] **Step 1: Add to `Strings.ru-RU.xaml`**

```xml
<!-- CrossSection -->
<system:String x:Key="CrossSections">Поперечные сечения</system:String>
<system:String x:Key="NewCrossSection">Новое сечение</system:String>
<system:String x:Key="EditCrossSection">Редактировать сечение</system:String>
<system:String x:Key="DeleteCrossSection">Удалить сечение</system:String>
<system:String x:Key="CrossSectionStage1">Этап 1</system:String>
<system:String x:Key="CrossSectionStage2">Этап 2</system:String>
<system:String x:Key="MaterialArea">Материальная область</system:String>
<system:String x:Key="AddConcreteArea">Добавить бетонную область</system:String>
<system:String x:Key="AddRebarArea">Добавить арматурную область</system:String>
<system:String x:Key="AddSteelArea">Добавить стальную область</system:String>
<system:String x:Key="HostArea">Бетонная область-носитель</system:String>
```

- [ ] **Step 2: Add to `Strings.en-US.xaml`**

```xml
<!-- CrossSection -->
<system:String x:Key="CrossSections">Cross-Sections</system:String>
<system:String x:Key="NewCrossSection">New Section</system:String>
<system:String x:Key="EditCrossSection">Edit Section</system:String>
<system:String x:Key="DeleteCrossSection">Delete Section</system:String>
<system:String x:Key="CrossSectionStage1">Stage 1</system:String>
<system:String x:Key="CrossSectionStage2">Stage 2</system:String>
<system:String x:Key="MaterialArea">Material Area</system:String>
<system:String x:Key="AddConcreteArea">Add Concrete Area</system:String>
<system:String x:Key="AddRebarArea">Add Rebar Area</system:String>
<system:String x:Key="AddSteelArea">Add Steel Area</system:String>
<system:String x:Key="HostArea">Host Concrete Area</system:String>
```

- [ ] **Step 3: Build**

```
dotnet build OpenCS.sln
```
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 4: Commit**

```
git add OpenCS/Resources/Strings.ru-RU.xaml OpenCS/Resources/Strings.en-US.xaml
git commit -m "feat(i18n): add CrossSection localization keys"
```

---

### Task 14: Add area-type icons and brushes to `App.xaml`

**Files:**
- Modify: `OpenCS/App.xaml`

- [ ] **Step 1: Add color brushes and icon geometries**

Inside the `<Application.Resources>` → `<ResourceDictionary.MergedDictionaries>` section, add:

```xml
<!-- MaterialArea type brushes -->
<SolidColorBrush x:Key="AreaBrush_Concrete"  Color="#3B82F6"/>
<SolidColorBrush x:Key="AreaBrush_ReSteelF"  Color="#F97316"/>
<SolidColorBrush x:Key="AreaBrush_ReSteelU"  Color="#EAB308"/>
<SolidColorBrush x:Key="AreaBrush_Steel"      Color="#22C55E"/>
<SolidColorBrush x:Key="AreaBrush_None"       Color="#9CA3AF"/>

<!-- Icon Path geometries 16x16 -->
<!-- Concrete: filled square -->
<Geometry x:Key="Icon_Concrete">M1,1 H15 V15 H1 Z</Geometry>
<!-- ReSteelF: three circles -->
<Geometry x:Key="Icon_ReSteelF">
    M3,8 A2,2 0 1,0 3.001,8 Z
    M8,8 A2,2 0 1,0 8.001,8 Z
    M13,8 A2,2 0 1,0 13.001,8 Z
</Geometry>
<!-- ReSteelU: three dashed circles (use same path, dashed stroke applied in style) -->
<Geometry x:Key="Icon_ReSteelU">
    M3,8 A2,2 0 1,0 3.001,8 Z
    M8,8 A2,2 0 1,0 8.001,8 Z
    M13,8 A2,2 0 1,0 13.001,8 Z
</Geometry>
<!-- Steel: outline rectangle -->
<Geometry x:Key="Icon_Steel">M1,3 H15 V13 H1 Z M2,4 H14 V12 H2 Z</Geometry>
```

- [ ] **Step 2: Add `DataTemplate` for area icon (used in TreeView)**

```xml
<DataTemplate x:Key="MaterialAreaIconTemplate" DataType="{x:Type local:MaterialAreaVM}">
    <StackPanel Orientation="Horizontal">
        <Path Width="14" Height="14" Margin="0,0,4,0" Stretch="Uniform"
              Fill="{Binding MaterialType, Converter={StaticResource MatTypeToBrushConverter}}">
            <Path.Data>
                <Binding Path="MaterialType"
                         Converter="{StaticResource MatTypeToGeometryConverter}"/>
            </Path.Data>
        </Path>
        <TextBlock Text="{Binding Tag}"/>
    </StackPanel>
</DataTemplate>
```

Add value converters `MatTypeToBrushConverter` and `MatTypeToGeometryConverter` as simple `IValueConverter` classes in `OpenCS/Converters/`:

**`OpenCS/Converters/MatTypeToBrushConverter.cs`:**
```csharp
using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using CScore;

namespace OpenCS.Converters
{
    public class MatTypeToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            if (value is not MatType mt) return Brushes.Gray;
            return mt switch
            {
                MatType.Concrete  => new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6)),
                MatType.ReSteelF  => new SolidColorBrush(Color.FromRgb(0xF9, 0x73, 0x16)),
                MatType.ReSteelU  => new SolidColorBrush(Color.FromRgb(0xEA, 0xB3, 0x08)),
                MatType.Steel     => new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E)),
                _                 => Brushes.Gray
            };
        }
        public object ConvertBack(object v, Type t, object p, CultureInfo c) =>
            throw new NotImplementedException();
    }
}
```

**`OpenCS/Converters/MatTypeToGeometryConverter.cs`:**
```csharp
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using CScore;

namespace OpenCS.Converters
{
    public class MatTypeToGeometryConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            if (value is not MatType mt) return Geometry.Empty;
            string key = mt switch
            {
                MatType.Concrete => "Icon_Concrete",
                MatType.ReSteelF => "Icon_ReSteelF",
                MatType.ReSteelU => "Icon_ReSteelU",
                MatType.Steel    => "Icon_Steel",
                _                => "Icon_Concrete"
            };
            return Application.Current.TryFindResource(key) as Geometry ?? Geometry.Empty;
        }
        public object ConvertBack(object v, Type t, object p, CultureInfo c) =>
            throw new NotImplementedException();
    }
}
```

Register converters in `App.xaml`:
```xml
<local:MatTypeToBrushConverter x:Key="MatTypeToBrushConverter"/>
<local:MatTypeToGeometryConverter x:Key="MatTypeToGeometryConverter"/>
```

- [ ] **Step 3: Build**

```
dotnet build OpenCS.sln
```
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 4: Commit**

```
git add OpenCS/App.xaml OpenCS/Converters/
git commit -m "feat(UI): add area-type brushes, icons, and converters"
```

---

### Task 15: Implement `CrossSectionPage` (edit/create)

**Files:**
- Modify: `OpenCS/Views/CrossSectionPage.xaml`
- Modify: `OpenCS/Views/CrossSectionPage.xaml.cs`

- [ ] **Step 1: Replace stub XAML with full page**

`CrossSectionPage.xaml` — страница создания/редактирования сечения:

```xml
<UserControl x:Class="OpenCS.Views.CrossSectionPage"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="clr-namespace:OpenCS.ViewModels"
             xmlns:conv="clr-namespace:OpenCS.Converters">
    <UserControl.Resources>
        <conv:MatTypeToBrushConverter x:Key="MatTypeToBrush"/>
        <conv:MatTypeToGeometryConverter x:Key="MatTypeToGeom"/>
    </UserControl.Resources>
    <Grid Margin="8">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <!-- Заголовок -->
        <StackPanel Grid.Row="0" Orientation="Horizontal" Margin="0,0,0,8">
            <TextBlock Text="{DynamicResource MaterialArea}" FontWeight="SemiBold"
                       VerticalAlignment="Center" Margin="0,0,8,0"/>
            <TextBox Text="{Binding Tag, UpdateSourceTrigger=PropertyChanged}"
                     Width="200"/>
        </StackPanel>

        <!-- Список областей -->
        <ListBox Grid.Row="1" ItemsSource="{Binding Areas}"
                 BorderThickness="1" Padding="4">
            <ListBox.ItemTemplate>
                <DataTemplate DataType="{x:Type vm:MaterialAreaVM}">
                    <Border BorderThickness="0,0,0,1"
                            BorderBrush="{DynamicResource {x:Static SystemColors.ControlLightBrushKey}}"
                            Padding="4,4,4,4">
                        <Grid>
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="18"/>
                                <ColumnDefinition Width="*"/>
                                <ColumnDefinition Width="Auto"/>
                            </Grid.ColumnDefinitions>
                            <!-- Иконка типа -->
                            <Path Grid.Column="0" Width="14" Height="14" Stretch="Uniform"
                                  Fill="{Binding MaterialType, Converter={StaticResource MatTypeToBrush}}"
                                  Data="{Binding MaterialType, Converter={StaticResource MatTypeToGeom}}"/>
                            <!-- Тег -->
                            <TextBlock Grid.Column="1" Text="{Binding Tag}"
                                       VerticalAlignment="Center" Margin="6,0"/>
                            <!-- Кнопка удалить -->
                            <Button Grid.Column="2" Style="{StaticResource IconButton25}"
                                    Command="{Binding RemoveAreaCommand}"
                                    ToolTip="Удалить область">
                                <Path Data="M4,4 L12,12 M12,4 L4,12" Stroke="Gray"
                                      StrokeThickness="1.5" StrokeLineCap="Round"/>
                            </Button>
                        </Grid>
                    </Border>
                </DataTemplate>
            </ListBox.ItemTemplate>
        </ListBox>

        <!-- Кнопки добавления -->
        <StackPanel Grid.Row="2" Orientation="Horizontal" Margin="0,8,0,0" Spacing="4">
            <Button Content="{DynamicResource AddConcreteArea}"
                    Command="{Binding AddConcreteAreaCommand}" Padding="8,4"/>
            <Button Content="{DynamicResource AddRebarArea}"
                    Command="{Binding AddRebarAreaCommand}" Padding="8,4"/>
            <Button Content="{DynamicResource AddSteelArea}"
                    Command="{Binding AddSteelAreaCommand}" Padding="8,4"/>
        </StackPanel>
    </Grid>
</UserControl>
```

`CrossSectionPage.xaml.cs`:

```csharp
using CScore;
using OpenCS.ViewModels;
using System.Windows.Controls;

namespace OpenCS.Views
{
    public partial class CrossSectionPage : UserControl
    {
        public CrossSectionPage(AppViewModel app)
        {
            InitializeComponent();
            var section = new CrossSection { Tag = "Новое сечение" };
            app.CrossSections.Add(section);
            DataContext = new CrossSectionVM(section, app);
        }

        public CrossSectionPage(CrossSection section, AppViewModel app)
        {
            InitializeComponent();
            DataContext = new CrossSectionVM(section, app);
        }
    }
}
```

- [ ] **Step 2: Build**

```
dotnet build OpenCS.sln
```
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 3: Commit**

```
git add OpenCS/Views/CrossSectionPage.xaml OpenCS/Views/CrossSectionPage.xaml.cs
git commit -m "feat(UI): implement CrossSectionPage"
```

---

### Task 16: Implement `CrossSectionView` (analysis view)

**Files:**
- Modify: `OpenCS/Views/CrossSectionView.xaml`
- Modify: `OpenCS/Views/CrossSectionView.xaml.cs`

- [ ] **Step 1: Implement XAML**

`CrossSectionView.xaml` — страница просмотра/расчёта сечения. Структура аналогична текущему `RCFiberRegionView.xaml`:

```xml
<UserControl x:Class="OpenCS.Views.CrossSectionView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:conv="clr-namespace:OpenCS.Converters">
    <UserControl.Resources>
        <conv:MatTypeToBrushConverter x:Key="MatTypeToBrush"/>
        <conv:MatTypeToGeometryConverter x:Key="MatTypeToGeom"/>
    </UserControl.Resources>
    <Grid>
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="220"/>
            <ColumnDefinition Width="*"/>
        </Grid.ColumnDefinitions>

        <!-- Левая панель: список областей -->
        <Border Grid.Column="0" BorderThickness="0,0,1,0"
                BorderBrush="{DynamicResource {x:Static SystemColors.ControlLightBrushKey}}">
            <StackPanel Margin="8">
                <TextBlock Text="{Binding Model.Tag}" FontWeight="Bold" Margin="0,0,0,8"/>
                <ItemsControl ItemsSource="{Binding Areas}">
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <StackPanel Orientation="Horizontal" Margin="0,2">
                                <Path Width="12" Height="12" Stretch="Uniform" Margin="0,0,4,0"
                                      Fill="{Binding MaterialType, Converter={StaticResource MatTypeToBrush}}"
                                      Data="{Binding MaterialType, Converter={StaticResource MatTypeToGeom}}"/>
                                <TextBlock Text="{Binding Tag}" VerticalAlignment="Center"/>
                            </StackPanel>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
            </StackPanel>
        </Border>

        <!-- Правая панель: геометрия (DXF canvas placeholder) -->
        <Grid Grid.Column="1" Background="White">
            <TextBlock Text="Геометрия сечения" HorizontalAlignment="Center"
                       VerticalAlignment="Center" Foreground="LightGray" FontSize="16"/>
        </Grid>
    </Grid>
</UserControl>
```

`CrossSectionView.xaml.cs`:

```csharp
using CScore;
using OpenCS.ViewModels;
using System.Windows.Controls;

namespace OpenCS.Views
{
    public partial class CrossSectionView : UserControl
    {
        public CrossSectionView(CrossSection section, AppViewModel app)
        {
            InitializeComponent();
            DataContext = new CrossSectionVM(section, app);
        }
    }
}
```

- [ ] **Step 2: Build**

```
dotnet build OpenCS.sln
```
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 3: Commit**

```
git add OpenCS/Views/CrossSectionView.xaml OpenCS/Views/CrossSectionView.xaml.cs
git commit -m "feat(UI): implement CrossSectionView"
```

---

### Task 17: Update `MainWindow.xaml` TreeView

**Files:**
- Modify: `OpenCS/MainWindow.xaml`

- [ ] **Step 1: Add CrossSections node to TreeView**

Find the existing `<TreeView>` in `MainWindow.xaml`. Add a new `<TreeViewItem>` alongside existing items for Materials, FiberRegions, etc.:

```xml
<!-- CrossSections tree node -->
<TreeViewItem Header="{DynamicResource CrossSections}"
              ItemsSource="{Binding CrossSectionsLive}"
              IsExpanded="True">
    <TreeViewItem.ItemTemplate>
        <HierarchicalDataTemplate DataType="{x:Type cs:CrossSection}"
                                  ItemsSource="{Binding Areas}">
            <!-- Сечение -->
            <StackPanel Orientation="Horizontal">
                <Path Width="14" Height="14" Margin="0,0,4,0" Stretch="Uniform"
                      Fill="Gray" Data="M1,1 H7 L15,8 L7,15 H1 Z"/>
                <TextBlock Text="{Binding Tag}"/>
            </StackPanel>

            <!-- Шаблон для MaterialArea (дочерние узлы) -->
            <HierarchicalDataTemplate.ItemTemplate>
                <DataTemplate DataType="{x:Type cs:MaterialArea}">
                    <StackPanel Orientation="Horizontal">
                        <Path Width="13" Height="13" Margin="0,0,4,0" Stretch="Uniform"
                              Fill="{Binding Material.Type,
                                     Converter={StaticResource MatTypeToBrushConverter}}"
                              Data="{Binding Material.Type,
                                     Converter={StaticResource MatTypeToGeometryConverter}}"/>
                        <TextBlock Text="{Binding Tag}"/>
                    </StackPanel>
                </DataTemplate>
            </HierarchicalDataTemplate.ItemTemplate>
        </HierarchicalDataTemplate>
    </TreeViewItem.ItemTemplate>

    <!-- Контекстное меню -->
    <TreeViewItem.ContextMenu>
        <ContextMenu>
            <MenuItem Header="{DynamicResource NewCrossSection}"
                      Command="{Binding DataContext.NewCrossSectionCommand,
                                RelativeSource={RelativeSource AncestorType=Window}}"/>
            <MenuItem Header="{DynamicResource EditCrossSection}"
                      Command="{Binding DataContext.EditCrossSectionCommand,
                                RelativeSource={RelativeSource AncestorType=Window}}"/>
            <MenuItem Header="{DynamicResource DeleteCrossSection}"
                      Command="{Binding DataContext.DeleteCrossSectionCommand,
                                RelativeSource={RelativeSource AncestorType=Window}}"/>
        </ContextMenu>
    </TreeViewItem.ContextMenu>
</TreeViewItem>
```

Add namespace alias in root `<Window>` element:
```xml
xmlns:cs="clr-namespace:CScore;assembly=CScore"
```

- [ ] **Step 2: Wire `SelectedItemChanged` to set `CurrentCrossSection`**

In the `TreeView.SelectedItemChanged` handler in `MainWindow.xaml.cs`:

```csharp
case CScore.CrossSection cs:
    vm.CurrentCrossSection = cs;
    break;
```

- [ ] **Step 3: Build**

```
dotnet build OpenCS.sln
```
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 4: Commit**

```
git add OpenCS/MainWindow.xaml OpenCS/MainWindow.xaml.cs
git commit -m "feat(UI): add CrossSections to TreeView with area-type icons"
```

---

## Phase 5 — Cleanup (delete old classes)

> At this point all new functionality exists and builds. Phase 5 removes the old code. Do tasks in order — each step removes one layer of dependencies.

### Task 18: Remove old `Diagramm.Sig(ReBarGroup)` and update `Diagramm`

**Files:**
- Modify: `CScore/Diagramm.cs`

- [ ] **Step 1: Add `Sig(MaterialArea, bool, bool)` overload, remove `Sig(ReBarGroup, bool, bool)` and `Sig(FiberRegion, ...)` overloads after confirming no external callers**

Replace:
```csharp
public virtual void Sig(FiberRegion group, bool tenB = true, bool comprA = true)
```
With:
```csharp
public virtual void Sig(MaterialArea area, bool tenB = true, bool comprA = true)
{
    for (int i = 0; i < area.Fibers.Count; i++)
        Sig(area.Fibers[i], tenB, comprA);
}
```

Remove the `Sig(ReBar, bool, bool)` overload.

- [ ] **Step 2: Build**

```
dotnet build OpenCS.sln
```
Expected: errors only in files scheduled for deletion (`RCFiberRegion.cs`, `ReBarGroup.cs`). If errors elsewhere, fix before proceeding.

- [ ] **Step 3: Commit partial**

```
git add CScore/Diagramm.cs
git commit -m "refactor(CScore): replace Sig(FiberRegion) with Sig(MaterialArea)"
```

---

### Task 19: Delete old CScore classes

**Files to delete:**
`CScore/Region.cs`, `CScore/FiberRegion.cs`, `CScore/RCFiberRegion.cs`,
`CScore/ReBarGroup.cs`, `CScore/ReBar.cs`, `CScore/ReBarLayer.cs`, `CScore/FiberRegionData.cs`

- [ ] **Step 1: Delete files**

```bash
rm CScore/Region.cs CScore/FiberRegion.cs CScore/RCFiberRegion.cs
rm CScore/ReBarGroup.cs CScore/ReBar.cs CScore/ReBarLayer.cs CScore/FiberRegionData.cs
```

- [ ] **Step 2: Remove now-broken constructors from `GeoProps.cs`**

Delete:
- `GeoProps(Region region)` constructor
- `GeoProps(FiberRegion fiberRegion, ...)` constructor
- `GeoProps(RCFiberRegion fiberRegion, ...)` constructor
- `GeoProps(ReBarGroup group, ...)` constructor

- [ ] **Step 3: Remove transition adapters from `Geo.cs` and `GridSplit.cs`**

Delete the `internal static Fiber[] Triangulation(Region region, ...)` adapter methods added in Task 5.

- [ ] **Step 4: Update `Fiber.cs`** — remove `FiberRegion? Region` and `int RegionId` fields; update `ToString()` to use `Area`:

```csharp
[JsonIgnore] public MaterialArea? Area { get; set; }
[JsonIgnore] public int AreaId { get; set; }

public override string ToString() =>
    Area == null
        ? $"{Num:D3}#fiber : {Tag} | <No Area>"
        : $"{Num:D3}#fiber : {Tag} | <{Area.Tag}>";
```

- [ ] **Step 5: Build**

```
dotnet build OpenCS.sln
```
Expected: errors in `OpenCS` layer (old ViewModels and Views still reference deleted classes). Fix by proceeding to Task 20.

---

### Task 20: Delete old ViewModels and Views

**Files to delete:**
`OpenCS/ViewModels/RCFiberRegionVM.cs`, `OpenCS/ViewModels/RebarsVM.cs`,
`OpenCS/Views/FiberRegionPage.xaml`, `OpenCS/Views/FiberRegionPage.xaml.cs`,
`OpenCS/Views/FiberRegionView.xaml`, `OpenCS/Views/FiberRegionView.xaml.cs`,
`OpenCS/Views/RCFiberRegionPage.xaml`, `OpenCS/Views/RCFiberRegionPage.xaml.cs`,
`OpenCS/Views/RCFiberRegionView.xaml`, `OpenCS/Views/RCFiberRegionView.xaml.cs`,
`OpenCS/Views/RebarsPage.xaml`, `OpenCS/Views/RebarsPage.xaml.cs`

- [ ] **Step 1: Delete files**

```bash
rm OpenCS/ViewModels/RCFiberRegionVM.cs OpenCS/ViewModels/RebarsVM.cs
rm OpenCS/Views/FiberRegionPage.xaml OpenCS/Views/FiberRegionPage.xaml.cs
rm OpenCS/Views/FiberRegionView.xaml OpenCS/Views/FiberRegionView.xaml.cs
rm OpenCS/Views/RCFiberRegionPage.xaml OpenCS/Views/RCFiberRegionPage.xaml.cs
rm OpenCS/Views/RCFiberRegionView.xaml OpenCS/Views/RCFiberRegionView.xaml.cs
rm OpenCS/Views/RebarsPage.xaml OpenCS/Views/RebarsPage.xaml.cs
```

- [ ] **Step 2: Remove from `.csproj` if listed explicitly**

Check `OpenCS/OpenCS.csproj` — if XAML files are listed with `<Page>` or `<Compile>` tags, remove those entries.

- [ ] **Step 3: Build**

```
dotnet build OpenCS.sln
```
Expected: errors in `AppViewModel.cs` referencing old collections/types.

---

### Task 21: Clean up `AppViewModel` — remove old collections

**Files:**
- Modify: `OpenCS/AppViewModel.cs`

- [ ] **Step 1: Remove all references to deleted types**

Remove:
- Fields: `rebarGroupsLive`, `fiberRegionsLive`, `rcFiberRegionsLive`, `currentRCfiberRegion`
- Properties: `RebarGroups`, `RebarGroupsLive`, `FiberRegions`, `FiberRegionsLive`, `RcFiberRegions`, `RcFiberRegionsLive`, `CurrentRCfiberRegion`
- Commands: `NewRCFiberRegionCommand`, `EditRCFiberRegionCommand`, `DeleteRCFiberRegionCommand`
- Methods: `NewRCFiberRegion()`, `EditRCFiberRegion()`, `DeleteRCFiberRegion()`, `FiberRegionsRenumber()`, `RCFiberRegionsRenumber()`
- Constructor lines: all assignments to removed properties/collections

Remove from constructor:
```csharp
// Удалить:
FiberRegions = db.FiberRegions;
RcFiberRegions = db.RcFiberRegions;
// и все связанные CollectionChanged подписки
```

- [ ] **Step 2: Build**

```
dotnet build OpenCS.sln
```
Expected: errors in `MainWindow.xaml` / `MainWindow.xaml.cs` referencing old TreeView nodes.

---

### Task 22: Clean up `MainWindow.xaml` and `DatabaseService`

**Files:**
- Modify: `OpenCS/MainWindow.xaml`
- Modify: `OpenCS/MainWindow.xaml.cs`
- Modify: `OpenCS/Utilites/DatabaseService.cs`

- [ ] **Step 1: Remove old TreeView nodes from `MainWindow.xaml`**

Delete `<TreeViewItem>` nodes for `FiberRegionsLive` and `RcFiberRegionsLive`.
Remove their handlers from `SelectedItemChanged`.

- [ ] **Step 2: Remove old DB methods from `DatabaseService.cs`**

Remove:
- `FiberRegions` and `RcFiberRegions` and `RebarGroups` collections
- `LoadRCFiberRegions()` method
- `SaveRCFiberRegion()`, `DeleteRCFiberRegion()` methods
- `AddRebar()`, `AddRebars()`, `RemoveFibers()` (RC-specific overloads)
- Old `ResolveReferences()` for RC types

- [ ] **Step 3: Update `RegionType` enum in `Contour.cs`**

Replace with updated values:
```csharp
public enum RegionType { Contour = 1, MaterialArea = 2, Fiber = 3, CrossSection = 4 }
```

- [ ] **Step 4: Build**

```
dotnet build OpenCS.sln
```
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 5: Final commit**

```
git add -A
git commit -m "refactor: remove old Region/FiberRegion/RCFiberRegion hierarchy; cleanup complete"
```

---

## Self-Review

### Spec coverage check

| Spec requirement | Task |
|---|---|
| `DifferentialSpline` in CSmath | Task 1 |
| `FiberType.point` + `Diameter` in `Fiber` | Task 2 |
| `Diagramm.Differential()` | Task 3 |
| `MaterialArea` class with all fields | Task 4 |
| `Geo`/`GridSplit` accept `MaterialArea` | Task 5 |
| `GeoProps(MaterialArea)` | Task 6 |
| `CrossSection.Integral()` | Task 7 |
| `TwoStageSection` with Stage1Kurvature | Task 7 |
| New DB schema (4 tables) | Task 8 |
| Load cross sections from DB | Task 9 |
| Save/Delete cross sections | Task 10 |
| `MaterialAreaVM` with icon/color binding | Tasks 11, 14 |
| `CrossSectionVM` | Task 12 |
| AppViewModel.CrossSections | Task 12 |
| Localization keys | Task 13 |
| Area-type icons + brushes | Task 14 |
| `CrossSectionPage` | Task 15 |
| `CrossSectionView` | Task 16 |
| TreeView with icons | Task 17 |
| AutoResolveHostAreas | Task 4 (in MaterialArea.cs) |
| Delete old classes | Tasks 18–22 |
| Min 1 area per section | Enforced in CrossSectionVM (UI) |

### Type consistency check

- `MaterialArea.SetEps` called in `CrossSection.Integral` ✓
- `GeoProps(CrossSection)` calls `GeoProps(MaterialArea)` ✓
- `Diagramm.Differential(steel, concrete)` returns `Diagramm` ✓
- `Kurvature +` operator already exists ✓
- `CrossSectionVM.Areas` is `ObservableCollection<MaterialAreaVM>` ✓
- `AppViewModel.CrossSections` bound to `CrossSectionsLive` in TreeView ✓
- `MatTypeToBrushConverter` and `MatTypeToGeometryConverter` registered in `App.xaml` ✓
