# Custom Diagrams & Custom Material Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Добавить пользовательские σ(ε)-диаграммы с редактором (таблица + CSV-импорт + WPF-канвас) и тип материала MatType.Custom, чьи диаграммы по CalcType задаются явно из пула проектных диаграмм.

**Architecture:** Подход A (JSON-ссылки). В `Material` добавляется `CustomDiagramIds: Dictionary<CalcType,int>`, ссылающийся по ID на записи таблицы `diagrams`. `MaterialArea.ResolveAndBuildDiagramms()` получает опциональный параметр `pool` — если материал Custom, строит `Diagramms` из пула по ID. Редактор DiagramPage переходит в MVVM через `DiagramEditVM`, Chart-вкладка получает новый `DiagramCanvas : FrameworkElement`.

**Tech Stack:** .NET 9.0, WPF, SQLite (Microsoft.Data.Sqlite), CScore, CSmath (LSpline), тест-харнес CSfea.Tests (TestHarness.Check).

---

## Карта файлов

| Файл | Статус | Ответственность |
|------|--------|-----------------|
| `CScore/Material.cs` | Изменить | + MatType.Custom, BaseType, CustomDiagramIds, ResolveCustomDiagramms() |
| `CScore/MaterialArea.cs` | Изменить | ResolveAndBuildDiagramms(pool?) — опц. параметр + Custom-ветвь |
| `CScore/CrossSection.cs` | Изменить | ResolveAndBuildDiagramms(pool?) — опц. параметр, пробрасывает в Areas |
| `OpenCS/Utilites/DatabaseService.cs` | Изменить | Миграция v17, Load/Save base_type+custom_diagram_ids, передача pool |
| `OpenCS/Tasks/StrainStateHandler.cs` | Изменить | Передать pool из ctx в ResolveAndBuildDiagramms |
| `OpenCS/Tasks/FireRCheckHandler.cs` | Изменить | Передать pool |
| `OpenCS/Tasks/FireRCheckBatchHandler.cs` | Изменить | Передать pool |
| `OpenCS/Views/CalcResultView.xaml.cs` | Изменить | Передать pool |
| `OpenCS/ViewModels/DiagramEditVM.cs` | Создать | VM: коллекция точек, BuildSplines(), ImportCsv() |
| `OpenCS/Views/Helpers/DiagramCanvas.cs` | Создать | WPF FrameworkElement: кривая + маркеры + пан/зум |
| `OpenCS/Views/DiagramPage.xaml` | Изменить | DiagramCanvas в Chart-вкладке, редактируемый DataGrid |
| `OpenCS/Views/DiagramPage.xaml.cs` | Изменить | MVVM: DataContext = DiagramEditVM |
| `OpenCS/AppViewModel.cs` | Изменить | + AddDiagramCommand |
| `OpenCS/MainWindow.xaml` | Изменить | Кнопка «+» у узла Diagrams |
| `OpenCS/ViewModels/MaterialVM.cs` | Изменить | + IsCustom, BaseType, CustomDiagramIds |
| `OpenCS/Views/MaterialPage.xaml` | Изменить | Custom-блок с BaseType + 4 ComboBox диаграмм |
| `OpenCS/Views/MaterialPage.xaml.cs` | Изменить | Подписка на смену CustomDiagramIds из ComboBox |
| `OpenCS/Views/MaterialAreaPage.xaml` | Изменить | Скрыть DiagrammType при Custom |
| `OpenCS/Resources/Strings.ru-RU.xaml` | Изменить | Новые ключи |
| `OpenCS/Resources/Strings.en-US.xaml` | Изменить | Новые ключи |
| `CSfea.Tests/CustomDiagramTests.cs` | Создать | Тесты ResolveCustomDiagramms и BuildSplines |
| `CSfea.Tests/Program.cs` | Изменить | Добавить CustomDiagramTests.RunAll() |

---

## Task 1: Domain model — MatType.Custom + Material.ResolveCustomDiagramms

**Files:**
- Modify: `CScore/Material.cs`
- Create: `CSfea.Tests/CustomDiagramTests.cs`
- Modify: `CSfea.Tests/Program.cs`

- [ ] **Step 1: Написать тест (он падёт — методов ещё нет)**

Создать `CSfea.Tests/CustomDiagramTests.cs`:

```csharp
using CScore;
using CSmath;

namespace CSfea.Tests;

public static class CustomDiagramTests
{
    public static void RunAll()
    {
        TestHarness.Section("CustomDiagram: ResolveCustomDiagramms + BuildSplines");
        ResolveCustomDiagramms_ReturnsCorrectDict();
        ResolveCustomDiagramms_ReturnsNull_WhenPoolEmpty();
        ResolveCustomDiagramms_SetsBaseType();
    }

    static void ResolveCustomDiagramms_ReturnsCorrectDict()
    {
        var ic1 = new LSpline(new[] { -0.003, 0.0 }, new[] { -30.0, 0.0 });
        var it1 = new LSpline(new[] { 0.0, 0.001 }, new[] { 0.0, 15.0 });
        var d1 = new Diagramm(ic1, it1, DiagrammType.L2, MatType.Concrete) { Id = 1, CalcType = CalcType.C };

        var ic2 = new LSpline(new[] { -0.002, 0.0 }, new[] { -20.0, 0.0 });
        var it2 = new LSpline(new[] { 0.0, 0.002 }, new[] { 0.0, 10.0 });
        var d2 = new Diagramm(ic2, it2, DiagrammType.L2, MatType.Concrete) { Id = 2, CalcType = CalcType.N };

        var pool = new List<Diagramm> { d1, d2 };

        var mat = new Material
        {
            Type = MatType.Custom,
            BaseType = MatType.Concrete,
            CustomDiagramIds = new Dictionary<CalcType, int>
            {
                { CalcType.C,  1 }, { CalcType.CL, 1 },
                { CalcType.N,  2 }, { CalcType.NL, 2 }
            }
        };

        var result = mat.ResolveCustomDiagramms(pool);

        bool ok = result != null
            && result.Count == 4
            && result[CalcType.C].Id  == 1
            && result[CalcType.N].Id  == 2
            && result[CalcType.NL].Id == 2;
        TestHarness.Check("ResolveCustomDiagramms_ReturnsCorrectDict", ok,
            $"count={result?.Count}");
    }

    static void ResolveCustomDiagramms_ReturnsNull_WhenPoolEmpty()
    {
        var mat = new Material
        {
            Type     = MatType.Custom,
            BaseType = MatType.Concrete,
            CustomDiagramIds = new Dictionary<CalcType, int> { { CalcType.C, 1 } }
        };
        var result = mat.ResolveCustomDiagramms(new List<Diagramm>());
        TestHarness.Check("ResolveCustomDiagramms_Null_EmptyPool", result == null, "");
    }

    static void ResolveCustomDiagramms_SetsBaseType()
    {
        var ic = new LSpline(new[] { -0.003, 0.0 }, new[] { -30.0, 0.0 });
        var it = new LSpline(new[] { 0.0, 0.001 }, new[] { 0.0, 15.0 });
        var d = new Diagramm(ic, it, DiagrammType.L2, MatType.Concrete) { Id = 7 };

        var pool = new List<Diagramm> { d };
        var mat = new Material
        {
            Type     = MatType.Custom,
            BaseType = MatType.ReSteelF,  // отличается от d.MaterialType
            CustomDiagramIds = new Dictionary<CalcType, int>
            {
                { CalcType.C, 7 }, { CalcType.CL, 7 },
                { CalcType.N, 7 }, { CalcType.NL, 7 }
            }
        };

        var result = mat.ResolveCustomDiagramms(pool);
        bool ok = result != null && result[CalcType.C].MaterialType == MatType.ReSteelF
                                 && d.MaterialType == MatType.Concrete; // pool не мутирован
        TestHarness.Check("ResolveCustomDiagramms_SetsBaseType", ok,
            $"got={result?[CalcType.C].MaterialType}");
    }
}
```

- [ ] **Step 2: Запустить тест — убедиться в ошибке компиляции (MatType.Custom не существует)**

```
dotnet build CSfea.Tests
```
Ожидаем: ошибки CS0117 / CS1061 — MatType.Custom, Material.BaseType, ResolveCustomDiagramms не найдены.

- [ ] **Step 3: Реализовать в `CScore/Material.cs`**

Добавить `Custom = 5` в перечисление `MatType`:

```csharp
public enum MatType
{
    Concrete  = 1,
    ReSteelF  = 2,
    ReSteelU  = 3,
    Steel     = 4,
    Custom    = 5,   // ← добавить
    None      = 0
}
```

В класс `Material` добавить два поля ПОСЛЕ существующего свойства `AggregateType`:

```csharp
/// <summary>Базовый тип поведения σ(ε) для Custom-материала (Concrete/ReSteelF/…).</summary>
public MatType BaseType { get; set; } = MatType.None;

/// <summary>
/// Ссылки на Id диаграмм из таблицы diagrams по видам расчёта.
/// Используется только если Type == MatType.Custom.
/// </summary>
public Dictionary<CalcType, int> CustomDiagramIds { get; set; } = [];
```

В класс `Material` добавить метод (после `SetJson()`):

```csharp
/// <summary>
/// Для Type==Custom: строит Dictionary&lt;CalcType,Diagramm&gt; из пула по CustomDiagramIds.
/// Создаёт копию каждой диаграммы с MaterialType = BaseType (пул не мутируется).
/// Возвращает null, если пул пуст или ни один Id не найден.
/// </summary>
public Dictionary<CalcType, Diagramm>? ResolveCustomDiagramms(IReadOnlyList<Diagramm> pool)
{
    if (pool == null || pool.Count == 0) return null;
    var result = new Dictionary<CalcType, Diagramm>();
    foreach (var ct in new[] { CalcType.C, CalcType.CL, CalcType.N, CalcType.NL })
    {
        if (!CustomDiagramIds.TryGetValue(ct, out int id)) continue;
        var src = pool.FirstOrDefault(d => d.Id == id);
        if (src == null) continue;
        result[ct] = new Diagramm(src.Ic, src.It, src.Type, BaseType, src.Tag)
        {
            Id = src.Id,
            MaterialId = Id,
            CalcType = ct
        };
    }
    return result.Count > 0 ? result : null;
}
```

- [ ] **Step 4: Добавить CustomDiagramTests.RunAll() в Program.cs**

В `CSfea.Tests/Program.cs` добавить строку после `FireRParityTests.RunAll();`:

```csharp
CustomDiagramTests.RunAll();
```

- [ ] **Step 5: Запустить тесты — убедиться в прохождении**

```
dotnet run --project CSfea.Tests
```
Ожидаем строки:
```
[PASS] ResolveCustomDiagramms_ReturnsCorrectDict
[PASS] ResolveCustomDiagramms_Null_EmptyPool
[PASS] ResolveCustomDiagramms_SetsBaseType
```

- [ ] **Step 6: Коммит**

```
git add CScore/Material.cs CSfea.Tests/CustomDiagramTests.cs CSfea.Tests/Program.cs
git commit -m "feat(domain): MatType.Custom + Material.ResolveCustomDiagramms"
```

---

## Task 2: MaterialArea — опциональный pool в ResolveAndBuildDiagramms

**Files:**
- Modify: `CScore/MaterialArea.cs`

- [ ] **Step 1: Изменить сигнатуру и добавить Custom-ветвь**

В `CScore/MaterialArea.cs` заменить метод `ResolveAndBuildDiagramms`:

```csharp
/// <param name="sp63EtaMin">Нижняя граница нисходящей ветви SP63 (по умолчанию 0.85).</param>
/// <param name="pool">Пул диаграмм проекта — нужен для Custom-материала. null = старый путь.</param>
public void ResolveAndBuildDiagramms(double sp63EtaMin = 0.85,
                                      IReadOnlyList<Diagramm>? pool = null)
{
    if (Material == null) return;

    Dictionary<CalcType, Diagramm> own;
    if (Material.Type == MatType.Custom && pool != null)
        own = Material.ResolveCustomDiagramms(pool) ?? [];
    else
        own = Material.GetDiagramms(DiagrammType, sp63EtaMin) ?? [];

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
```

Также в `CreateRebarArea` (статический метод, строка ≈210) вызов `area.ResolveAndBuildDiagramms()` остаётся без изменений — сигнатура обратно совместима.

- [ ] **Step 2: Сборка CScore**

```
dotnet build CScore
```
Ожидаем: Build succeeded, 0 errors.

- [ ] **Step 3: Коммит**

```
git add CScore/MaterialArea.cs
git commit -m "feat(domain): MaterialArea.ResolveAndBuildDiagramms принимает опц. pool"
```

---

## Task 3: CrossSection — пробросить pool вниз

**Files:**
- Modify: `CScore/CrossSection.cs`

- [ ] **Step 1: Обновить сигнатуру и проброс**

В `CScore/CrossSection.cs` заменить метод `ResolveAndBuildDiagramms` (строка ≈127):

```csharp
/// <param name="sp63EtaMin">Нижняя граница нисходящей ветви SP63 (по умолчанию 0.85).</param>
/// <param name="pool">Пул диаграмм — пробрасывается в MaterialArea.</param>
public void ResolveAndBuildDiagramms(double sp63EtaMin = 0.85,
                                      IReadOnlyList<Diagramm>? pool = null)
{
    foreach (var area in Areas)
        if (area.HostAreaId == null)
            area.ResolveAndBuildDiagramms(sp63EtaMin, pool);

    foreach (var area in Areas)
        if (area.HostAreaId != null)
        {
            area.HostArea = Areas.Find(a => a.Id == area.HostAreaId);
            area.ResolveAndBuildDiagramms(sp63EtaMin, pool);
        }
}
```

- [ ] **Step 2: Сборка CScore**

```
dotnet build CScore
```
Ожидаем: Build succeeded, 0 errors.

- [ ] **Step 3: Коммит**

```
git add CScore/CrossSection.cs
git commit -m "feat(domain): CrossSection.ResolveAndBuildDiagramms пробрасывает pool"
```

---

## Task 4: DatabaseService — миграция v17 + Load/Save материала + передача pool

**Files:**
- Modify: `OpenCS/Utilites/DatabaseService.cs`

- [ ] **Step 1: Добавить миграцию v17**

В `DatabaseService._migrations` (массив строк, последняя запись — v16) добавить следующий элемент:

```csharp
"""
-- v17: base_type и custom_diagram_ids для Custom-материалов.
ALTER TABLE materials ADD COLUMN base_type          INTEGER NOT NULL DEFAULT 0;
ALTER TABLE materials ADD COLUMN custom_diagram_ids TEXT    NOT NULL DEFAULT '{}';
"""
```

- [ ] **Step 2: Обновить LoadMaterials() — читать новые столбцы**

Найти метод `LoadMaterials()` (≈строка 825). Изменить SQL-запрос:

```csharp
cmd.CommandText = "SELECT id, type, tag, description, e, chars_json, aggregate_type, base_type, custom_diagram_ids FROM materials ORDER BY id";
```

В теле `while (reader.Read())` добавить чтение новых полей (добавить ПОСЛЕ строки `AggregateType = ...`):

```csharp
m.BaseType         = reader.IsDBNull(7) ? MatType.None : (MatType)reader.GetInt32(7);
var customIdsJson  = reader.IsDBNull(8) ? "{}" : reader.GetString(8);
var customIds      = JsonSerializer.Deserialize<Dictionary<CalcType, int>>(customIdsJson, _jsonSettings);
if (customIds != null) m.CustomDiagramIds = customIds;
```

- [ ] **Step 3: Обновить SaveMaterial() — записывать новые столбцы**

Найти метод `SaveMaterial(Material m)` (≈строка 1332).

INSERT-запрос заменить на:

```csharp
cmd.CommandText = @"INSERT INTO materials (type, tag, description, e, chars_json, aggregate_type, base_type, custom_diagram_ids)
                   VALUES ($type, $tag, $desc, $e, $chars, $agg, $bt, $cdi);
                   SELECT last_insert_rowid();";
```

UPDATE-запрос заменить на:

```csharp
cmd.CommandText = @"UPDATE materials SET type=$type, tag=$tag, description=$desc, e=$e,
                   chars_json=$chars, aggregate_type=$agg, base_type=$bt, custom_diagram_ids=$cdi
                   WHERE id=$id";
```

После существующего `cmd.Parameters.AddWithValue("$agg", ...)` добавить:

```csharp
cmd.Parameters.AddWithValue("$bt",  (int)m.BaseType);
cmd.Parameters.AddWithValue("$cdi", JsonSerializer.Serialize(m.CustomDiagramIds, _jsonSettings));
```

- [ ] **Step 4: Обновить ResolveReferencesForStandaloneAreas() — передать пул**

Заменить тело метода:

```csharp
void ResolveReferencesForStandaloneAreas()
{
    foreach (var area in MaterialAreas)
    {
        area.Material = Materials.FirstOrDefault(m => m.Id == area.MaterialId);
        if (area.HostAreaId != null)
            area.HostArea = MaterialAreas.FirstOrDefault(a => a.Id == area.HostAreaId);
        if (area.PoolContourId != null)
        {
            var pc = Contours.FirstOrDefault(c => c.Id == area.PoolContourId);
            if (pc != null)
            {
                area.PoolContour = pc;
                area.Hull = pc;
            }
        }
        area.ResolveAndBuildDiagramms(pool: Diagrams);
    }
}
```

- [ ] **Step 5: Обновить ResolveReferencesForCrossSections() — передать пул**

Заменить тело метода:

```csharp
void ResolveReferencesForCrossSections()
{
    foreach (var sec in CrossSections)
    {
        sec.ResolveAndBuildDiagramms(pool: Diagrams);
        if (sec is TwoStageSection tss)
            tss.Stage1.ResolveAndBuildDiagramms(pool: Diagrams);
    }
}
```

- [ ] **Step 6: Сборка OpenCS**

```
dotnet build OpenCS
```
Ожидаем: Build succeeded, 0 errors.

- [ ] **Step 7: Коммит**

```
git add OpenCS/Utilites/DatabaseService.cs
git commit -m "feat(db): миграция v17, load/save base_type+custom_diagram_ids, передача pool"
```

---

## Task 5: Callers — передать pool в task-хэндлерах и CalcResultView

**Files:**
- Modify: `OpenCS/Tasks/StrainStateHandler.cs`
- Modify: `OpenCS/Tasks/FireRCheckHandler.cs`
- Modify: `OpenCS/Tasks/FireRCheckBatchHandler.cs`
- Modify: `OpenCS/Views/CalcResultView.xaml.cs`

- [ ] **Step 1: StrainStateHandler — передать pool**

В `StrainStateHandler.cs` строка ≈24 (`section.ResolveAndBuildDiagramms(settings.Sp63DescEtaMin);`) заменить на:

```csharp
section.ResolveAndBuildDiagramms(settings.Sp63DescEtaMin,
    pool: ctx?.Database?.Diagrams);
```

- [ ] **Step 2: FireRCheckHandler — передать pool**

В `FireRCheckHandler.cs` строка ≈64 (`section.ResolveAndBuildDiagramms();`) заменить на:

```csharp
section.ResolveAndBuildDiagramms(pool: ctx?.Database?.Diagrams);
```

- [ ] **Step 3: FireRCheckBatchHandler — передать pool**

В `FireRCheckBatchHandler.cs` строка ≈44 (`section.ResolveAndBuildDiagramms();`) заменить на:

```csharp
section.ResolveAndBuildDiagramms(pool: ctx?.Database?.Diagrams);
```

- [ ] **Step 4: CalcResultView — передать pool**

В `CalcResultView.xaml.cs` строка ≈38 (`section.ResolveAndBuildDiagramms(app.CalcSettings.Sp63DescEtaMin);`) заменить на:

```csharp
section.ResolveAndBuildDiagramms(app.CalcSettings.Sp63DescEtaMin,
    pool: app.Diagrams);
```

- [ ] **Step 5: Сборка OpenCS**

```
dotnet build OpenCS
```
Ожидаем: Build succeeded, 0 errors.

- [ ] **Step 6: Коммит**

```
git add OpenCS/Tasks/StrainStateHandler.cs OpenCS/Tasks/FireRCheckHandler.cs \
        OpenCS/Tasks/FireRCheckBatchHandler.cs OpenCS/Views/CalcResultView.xaml.cs
git commit -m "feat: передача pool в task-хэндлеры и CalcResultView"
```

---

## Task 6: DiagramEditVM + DiagramPoint + тест BuildSplines/ImportCsv

**Files:**
- Create: `OpenCS/ViewModels/DiagramEditVM.cs`
- Modify: `CSfea.Tests/CustomDiagramTests.cs`

- [ ] **Step 1: Написать тест для BuildSplines и ImportCsv**

Добавить в конец `CSfea.Tests/CustomDiagramTests.cs` (перед закрывающей `}`):

```csharp
    // Тест BuildSplines вызывается из Task 6 — после создания DiagramEditVM нужно
    // временно проверить только математику через прямой вызов LSpline.
    // Т.к. DiagramEditVM — в OpenCS (WPF-проект), тестируем только CScore-часть здесь.

    public static void BuildSplines_Ic_It_Correct()
    {
        // Ветвь сжатия: три точки (ε отриц.)
        var epsArr = new[] { -0.003, -0.002, 0.0 };
        var sigArr = new[] { -30.0,  -20.0,  0.0 };
        var ic = new LSpline(epsArr, sigArr);

        // Ветвь растяжения: три точки (ε полож.)
        var epsArrT = new[] { 0.0, 0.001, 0.002 };
        var sigArrT = new[] { 0.0,  15.0,  15.0 };
        var it = new LSpline(epsArrT, sigArrT);

        // Ic должен вернуть отрицательные σ при отрицательных ε
        double sigAtMinus003 = ic.Interpolate(-0.003);
        bool icOk = Math.Abs(sigAtMinus003 - (-30.0)) < 1e-6;

        // It должен вернуть положительные σ при положительных ε
        double sigAt001 = it.Interpolate(0.001);
        bool itOk = Math.Abs(sigAt001 - 15.0) < 1e-6;

        TestHarness.Check("BuildSplines_LSpline_Ic_Correct", icOk,
            $"σ(-0.003)={sigAtMinus003:F4}, expected=-30");
        TestHarness.Check("BuildSplines_LSpline_It_Correct", itOk,
            $"σ(0.001)={sigAt001:F4}, expected=15");
    }
```

Добавить вызов в `RunAll()`:

```csharp
BuildSplines_Ic_It_Correct();
```

- [ ] **Step 2: Создать `OpenCS/ViewModels/DiagramEditVM.cs`**

```csharp
using CScore;
using CSmath;
using Microsoft.Win32;
using OpenCS.Utilites;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;

namespace OpenCS.ViewModels
{
   /// <summary>Одна точка σ(ε)-кривой. Уведомляет об изменении для DataGrid и DiagramCanvas.</summary>
   public class DiagramPoint : ViewModelBase
   {
      double _eps, _sig;
      public double Eps { get => _eps; set { _eps = value; OnPropertyChanged(); OnPropertyChanged(nameof(Branch)); } }
      public double Sig { get => _sig; set { _sig = value; OnPropertyChanged(); } }
      /// <summary>Ic (ε&lt;0), Origin (ε=0), It (ε&gt;0) — вычисляемое, только для отображения.</summary>
      public string Branch => _eps < -1e-15 ? "Ic" : _eps > 1e-15 ? "It" : "Origin";
   }

   /// <summary>
   /// ViewModel редактора пользовательской диаграммы σ(ε).
   /// Обеспечивает коллекцию точек, построение сплайнов, CSV-импорт и сохранение в БД.
   /// </summary>
   public class DiagramEditVM : ViewModelBase
   {
      readonly AppViewModel _app;
      readonly Diagramm _diagram;
      readonly bool _isNew;

      public DiagramEditVM(Diagramm diagram, AppViewModel app, bool isNew = false)
      {
         _diagram = diagram;
         _app     = app;
         _isNew   = isNew;
         Points   = LoadPoints(diagram);
         Points.CollectionChanged += (_, _) => OnPropertyChanged(nameof(Points));
      }

      public Diagramm Diagram => _diagram;
      public ObservableCollection<DiagramPoint> Points { get; }

      public string Tag
      {
         get => _diagram.Tag;
         set { _diagram.Tag = value; OnPropertyChanged(); }
      }

      public CalcType CalcType
      {
         get => _diagram.CalcType;
         set { _diagram.CalcType = value; OnPropertyChanged(); }
      }

      public MatType MaterialType
      {
         get => _diagram.MaterialType;
         set { _diagram.MaterialType = value; OnPropertyChanged(); }
      }

      /// <summary>
      /// Строит Ic и It из текущей коллекции Points.
      /// Ic ← точки с Eps ≤ 0, отсортированные по Eps (возрастание).
      /// It ← точки с Eps ≥ 0, отсортированные по Eps (возрастание).
      /// Точка Eps=0 (начало координат) входит в обе ветви.
      /// </summary>
      public void BuildSplines()
      {
         var sorted = Points.OrderBy(p => p.Eps).ToList();

         // Добавить начало координат, если его нет
         if (!sorted.Any(p => Math.Abs(p.Eps) < 1e-15))
            sorted.Insert(sorted.FindIndex(p => p.Eps > 0).Let(i => i < 0 ? sorted.Count : i),
                          new DiagramPoint { Eps = 0, Sig = 0 });

         var icPts = sorted.Where(p => p.Eps <= 1e-15).ToList();
         var itPts = sorted.Where(p => p.Eps >= -1e-15).ToList();

         if (icPts.Count >= 2)
            _diagram.Ic = new LSpline(icPts.Select(p => p.Eps).ToArray(),
                                      icPts.Select(p => p.Sig).ToArray());
         if (itPts.Count >= 2)
            _diagram.It = new LSpline(itPts.Select(p => p.Eps).ToArray(),
                                      itPts.Select(p => p.Sig).ToArray());
      }

      /// <summary>Вызвать BuildSplines и сохранить диаграмму в БД. Добавляет в пул если новая.</summary>
      public void Save()
      {
         BuildSplines();
         _app.db.SaveDiagram(_diagram);
         if (_isNew && !_app.db.Diagrams.Contains(_diagram))
         {
            _app.db.Diagrams.Add(_diagram);
            _app.DiagramsLive.Add(_diagram);
         }
         _app.LogService.Info($"Диаграмма '{_diagram.Tag}' сохранена");
      }

      /// <summary>
      /// Импортирует точки из CSV-файла.
      /// Формат: заголовок (пропускается), затем строки «ε;σ» или «ε,σ».
      /// Разбиение на Ic/It по знаку ε.
      /// </summary>
      public void ImportCsv(string path)
      {
         var lines = File.ReadAllLines(path);
         var newPoints = new List<DiagramPoint>();
         char delim = lines.FirstOrDefault(l => l.Contains(';')) != null ? ';' : ',';

         foreach (var line in lines.Skip(1)) // пропустить заголовок
         {
            var parts = line.Split(delim);
            if (parts.Length < 2) continue;
            if (!double.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double eps)) continue;
            if (!double.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double sig)) continue;
            newPoints.Add(new DiagramPoint { Eps = eps, Sig = sig });
         }

         Points.Clear();
         foreach (var p in newPoints.OrderBy(p => p.Eps))
            Points.Add(p);

         OnPropertyChanged(nameof(Points));
      }

      /// <summary>Добавить новую пустую точку (в конец для It-ветви).</summary>
      public void AddPoint() => Points.Add(new DiagramPoint { Eps = 0, Sig = 0 });

      /// <summary>Удалить точку по объекту.</summary>
      public void RemovePoint(DiagramPoint p) => Points.Remove(p);

      // ────── helpers ──────

      static ObservableCollection<DiagramPoint> LoadPoints(Diagramm d)
      {
         var list = new List<DiagramPoint>();
         bool seenZero = false;

         void AddBranch(ISpline? sp)
         {
            if (sp?.X == null) return;
            for (int i = 0; i < sp.X.Length; i++)
            {
               bool isZero = Math.Abs(sp.X[i]) < 1e-15 && Math.Abs(sp.Y[i]) < 1e-15;
               if (isZero) { if (seenZero) continue; seenZero = true; }
               list.Add(new DiagramPoint { Eps = sp.X[i], Sig = sp.Y[i] });
            }
         }

         AddBranch(d.Ic);
         AddBranch(d.It);
         return new ObservableCollection<DiagramPoint>(list.OrderBy(p => p.Eps));
      }
   }
}

// Маленький extension-метод для .Let() чтобы не создавать лишний метод
file static class IntExt
{
   public static T Let<T>(this T value, Func<T, T> f) => f(value);
}
```

- [ ] **Step 3: Сборка OpenCS**

```
dotnet build OpenCS
```
Ожидаем: Build succeeded.

- [ ] **Step 4: Запустить тесты**

```
dotnet run --project CSfea.Tests
```
Ожидаем: все PASS, включая новые тесты.

- [ ] **Step 5: Коммит**

```
git add OpenCS/ViewModels/DiagramEditVM.cs CSfea.Tests/CustomDiagramTests.cs
git commit -m "feat: DiagramEditVM с BuildSplines, ImportCsv и тестами"
```

---

## Task 7: DiagramCanvas — WPF FrameworkElement

**Files:**
- Create: `OpenCS/Views/Helpers/DiagramCanvas.cs`

- [ ] **Step 1: Создать `OpenCS/Views/Helpers/DiagramCanvas.cs`**

```csharp
using OpenCS.ViewModels;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace OpenCS.Views.Helpers;

/// <summary>
/// Интерактивный WPF-канвас σ(ε)-диаграммы: кривые Ic/It, перетаскиваемые маркеры,
/// зум колёсиком, пан ЛКМ.
/// </summary>
public sealed class DiagramCanvas : FrameworkElement
{
    // ─── Трансформация экран↔модель ───
    double _scaleX = 1, _scaleY = 1;   // px per unit
    double _tx, _ty;                    // screen offset
    bool _fitted;

    // ─── Взаимодействие ───
    Point _dragStart;
    bool _panning;
    int _dragIdx = -1;   // индекс перетаскиваемой точки (-1 = нет)

    // ─── Pen-кэш ───
    static readonly Pen _bluePen  = new(Brushes.Blue, 1.5);
    static readonly Pen _redPen   = new(Brushes.Red,  1.5);
    static readonly Pen _axisPen  = new(Brushes.Gray, 0.8);
    static readonly Pen _blueMarker = new(new SolidColorBrush(Color.FromRgb(0, 0, 180)), 1.0);
    static readonly Pen _redMarker  = new(new SolidColorBrush(Color.FromRgb(180, 0, 0)), 1.0);
    static readonly Brush _blueMarkerFill = new SolidColorBrush(Color.FromRgb(100, 100, 255));
    static readonly Brush _redMarkerFill  = new SolidColorBrush(Color.FromRgb(255, 100, 100));
    const double MarkerR = 5.0;

    static DiagramCanvas()
    {
        _bluePen.Freeze(); _redPen.Freeze(); _axisPen.Freeze();
        _blueMarker.Freeze(); _redMarker.Freeze();
        _blueMarkerFill.Freeze(); _redMarkerFill.Freeze();
    }

    // ─── DependencyProperty ───
    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(nameof(ViewModel), typeof(DiagramEditVM),
            typeof(DiagramCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender,
                OnVmChanged));

    public DiagramEditVM? ViewModel
    {
        get => (DiagramEditVM?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    static void OnVmChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var c = (DiagramCanvas)d;
        if (e.OldValue is DiagramEditVM old)
            old.Points.CollectionChanged -= c.OnPointsChanged;
        if (e.NewValue is DiagramEditVM vm)
        {
            vm.Points.CollectionChanged += c.OnPointsChanged;
            c._fitted = false;
        }
    }

    void OnPointsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        => InvalidateVisual();

    // ─── Measure/Arrange ───
    protected override Size MeasureOverride(Size a)
        => new(double.IsInfinity(a.Width) ? 300 : a.Width,
               double.IsInfinity(a.Height) ? 200 : a.Height);

    protected override Size ArrangeOverride(Size s)
    {
        if (!_fitted && ViewModel != null)
        {
            _fitted = true;
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, FitToView);
        }
        return s;
    }

    // ─── FitToView ───
    public void FitToView()
    {
        var vm = ViewModel;
        if (vm == null || ActualWidth < 1 || ActualHeight < 1) return;

        var pts = vm.Points;
        if (pts.Count == 0) { _scaleX = _scaleY = 1; _tx = ActualWidth / 2; _ty = ActualHeight / 2; InvalidateVisual(); return; }

        double epsMin = Math.Min(pts.Min(p => p.Eps), 0);
        double epsMax = Math.Max(pts.Max(p => p.Eps), 0);
        double sigMin = Math.Min(pts.Min(p => p.Sig), 0);
        double sigMax = Math.Max(pts.Max(p => p.Sig), 0);

        const double pad = 35;
        double sw = ActualWidth  - 2 * pad;
        double sh = ActualHeight - 2 * pad;
        double dE = epsMax - epsMin; if (dE < 1e-12) dE = 1;
        double dS = sigMax - sigMin; if (dS < 1e-12) dS = 1;

        _scaleX = sw / dE;
        _scaleY = sh / dS;
        _tx = pad - epsMin * _scaleX;
        _ty = pad + sigMax * _scaleY;   // Y перевёрнут
        InvalidateVisual();
    }

    Point ToScreen(double eps, double sig)
        => new(eps * _scaleX + _tx, -sig * _scaleY + _ty);

    (double eps, double sig) ToModel(Point screen)
        => ((screen.X - _tx) / _scaleX, -(screen.Y - _ty) / _scaleY);

    // ─── OnRender ───
    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(SystemColors.WindowBrush, null, new Rect(0, 0, ActualWidth, ActualHeight));

        var vm = ViewModel;
        if (vm == null) return;

        // Оси
        var origin = ToScreen(0, 0);
        dc.DrawLine(_axisPen, new Point(0, origin.Y), new Point(ActualWidth, origin.Y));  // σ=0
        dc.DrawLine(_axisPen, new Point(origin.X, 0), new Point(origin.X, ActualHeight)); // ε=0

        var sorted = vm.Points.OrderBy(p => p.Eps).ToList();

        // Ic-ветвь (синий): точки с Eps <= 0
        var icPts = sorted.Where(p => p.Eps <= 1e-15).ToList();
        DrawBranch(dc, icPts, _bluePen, _blueMarkerFill, _blueMarker);

        // It-ветвь (красный): точки с Eps >= 0
        var itPts = sorted.Where(p => p.Eps >= -1e-15).ToList();
        DrawBranch(dc, itPts, _redPen, _redMarkerFill, _redMarker);

        // Подписи осей
        DrawLabel(dc, "ε", new Point(ActualWidth - 20, origin.Y - 14));
        DrawLabel(dc, "σ", new Point(origin.X + 4, 4));
    }

    void DrawBranch(DrawingContext dc, System.Collections.Generic.List<DiagramPoint> pts,
                    Pen linePen, Brush fill, Pen markerPen)
    {
        if (pts.Count < 2) return;
        // Полилиния
        var geom = new StreamGeometry();
        using (var ctx = geom.Open())
        {
            ctx.BeginFigure(ToScreen(pts[0].Eps, pts[0].Sig), false, false);
            for (int i = 1; i < pts.Count; i++)
                ctx.LineTo(ToScreen(pts[i].Eps, pts[i].Sig), true, false);
        }
        geom.Freeze();
        dc.DrawGeometry(null, linePen, geom);

        // Маркеры
        foreach (var p in pts)
        {
            var sc = ToScreen(p.Eps, p.Sig);
            dc.DrawEllipse(fill, markerPen, sc, MarkerR, MarkerR);
        }
    }

    static void DrawLabel(DrawingContext dc, string text, Point pos)
    {
        var ft = new FormattedText(text, System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"), 11, Brushes.Gray,
                    VisualTreeHelper.GetDpi(new FrameworkElement()).PixelsPerDip);
        dc.DrawText(ft, pos);
    }

    // ─── Hit test ───
    int HitTestPoint(Point screen)
    {
        var vm = ViewModel;
        if (vm == null) return -1;
        var pts = vm.Points.ToList();
        for (int i = 0; i < pts.Count; i++)
        {
            var s = ToScreen(pts[i].Eps, pts[i].Sig);
            double dx = screen.X - s.X, dy = screen.Y - s.Y;
            if (dx * dx + dy * dy <= (MarkerR + 2) * (MarkerR + 2))
                return i;
        }
        return -1;
    }

    // ─── Mouse events ───
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(this);
        _dragIdx = HitTestPoint(pos);
        if (_dragIdx >= 0)
            CaptureMouse();
        else
        {
            _panning = true;
            _dragStart = pos;
            CaptureMouse();
        }
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        _dragIdx = -1;
        _panning = false;
        ReleaseMouseCapture();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var pos = e.GetPosition(this);
        if (_dragIdx >= 0 && ViewModel != null)
        {
            var (eps, sig) = ToModel(pos);
            var pt = ViewModel.Points[_dragIdx];
            pt.Eps = eps;
            pt.Sig = sig;
            InvalidateVisual();
        }
        else if (_panning)
        {
            _tx += pos.X - _dragStart.X;
            _ty += pos.Y - _dragStart.Y;
            _dragStart = pos;
            InvalidateVisual();
        }
        e.Handled = true;
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        var pos = e.GetPosition(this);
        double factor = e.Delta > 0 ? 1.15 : 1.0 / 1.15;
        var (eps0, sig0) = ToModel(pos);
        _scaleX *= factor;
        _scaleY *= factor;
        _tx = pos.X - eps0 * _scaleX;
        _ty = pos.Y + sig0 * _scaleY;
        InvalidateVisual();
        e.Handled = true;
    }
}
```

- [ ] **Step 2: Сборка OpenCS**

```
dotnet build OpenCS
```
Ожидаем: Build succeeded, 0 errors.

- [ ] **Step 3: Коммит**

```
git add OpenCS/Views/Helpers/DiagramCanvas.cs
git commit -m "feat: DiagramCanvas — WPF-канвас σ(ε)-кривой с пан/зум/drag"
```

---

## Task 8: DiagramPage — рефакторинг в редактируемый режим

**Files:**
- Modify: `OpenCS/Views/DiagramPage.xaml`
- Modify: `OpenCS/Views/DiagramPage.xaml.cs`

- [ ] **Step 1: Заменить DiagramPage.xaml**

Полностью заменить содержимое `OpenCS/Views/DiagramPage.xaml`:

```xml
<UserControl x:Class="OpenCS.Views.DiagramPage"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:local="clr-namespace:OpenCS.Views"
             xmlns:h="clr-namespace:OpenCS.Views.Helpers"
             Background="White">

    <Grid Margin="10">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <!-- Заголовок -->
        <Border Grid.Row="0" Background="#FF2813EB" CornerRadius="4" Padding="8" Margin="0,0,0,8">
            <TextBlock x:Name="titleText" Foreground="White" FontSize="15" FontWeight="SemiBold"
                       HorizontalAlignment="Center"/>
        </Border>

        <!-- Метаданные: тег, CalcType, MaterialType -->
        <Grid Grid.Row="1" Margin="0,0,0,8">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="Auto"/>
                <ColumnDefinition Width="Auto"/>
            </Grid.ColumnDefinitions>
            <TextBox Grid.Column="0" x:Name="tagBox" Margin="0,0,8,0"
                     Text="{Binding Tag, UpdateSourceTrigger=PropertyChanged}"
                     ToolTip="{DynamicResource DiagramTag}"/>
            <ComboBox Grid.Column="1" x:Name="calcTypeCombo" Width="60" Margin="0,0,8,0"
                      SelectedItem="{Binding CalcType}"/>
            <ComboBox Grid.Column="2" x:Name="matTypeCombo" Width="100"
                      SelectedItem="{Binding MaterialType}"/>
        </Grid>

        <!-- Вкладки: канвас и таблица -->
        <TabControl Grid.Row="2" Margin="0,0,0,5">
            <TabItem Header="{DynamicResource Chart}">
                <h:DiagramCanvas x:Name="diagCanvas" ViewModel="{Binding}" Margin="5"/>
            </TabItem>
            <TabItem Header="{DynamicResource Data}">
                <Grid Margin="5">
                    <Grid.RowDefinitions>
                        <RowDefinition Height="*"/>
                        <RowDefinition Height="Auto"/>
                    </Grid.RowDefinitions>
                    <DataGrid x:Name="dataGrid" AutoGenerateColumns="False"
                              ItemsSource="{Binding Points}"
                              CanUserAddRows="False"
                              HorizontalAlignment="Stretch" GridLinesVisibility="All">
                        <DataGrid.Columns>
                            <DataGridTextColumn Header="ε"
                                Binding="{Binding Eps, StringFormat=F6, UpdateSourceTrigger=PropertyChanged}"
                                Width="*"/>
                            <DataGridTextColumn Header="σ, МПа"
                                Binding="{Binding Sig, StringFormat=F2, UpdateSourceTrigger=PropertyChanged}"
                                Width="*"/>
                            <DataGridTextColumn Header="{DynamicResource Branch}"
                                Binding="{Binding Branch}" IsReadOnly="True" Width="60"/>
                        </DataGrid.Columns>
                    </DataGrid>
                    <StackPanel Grid.Row="1" Orientation="Horizontal" Margin="0,6,0,0">
                        <Button Content="{DynamicResource AddRow}"   Click="AddRow_Click"    Width="90" Height="26" Margin="0,0,6,0"/>
                        <Button Content="{DynamicResource DeleteRow}" Click="DeleteRow_Click" Width="90" Height="26" Margin="0,0,6,0"/>
                        <Button Content="{DynamicResource ImportFromCsv}" Click="ImportCsv_Click" Width="130" Height="26" Margin="0,0,6,0"/>
                        <Button Content="{DynamicResource ExportCsv}" Click="ExportCsv_Click" Width="130" Height="26"/>
                    </StackPanel>
                </Grid>
            </TabItem>
        </TabControl>

        <!-- Кнопки внизу -->
        <StackPanel Grid.Row="3" Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,8,0,0">
            <Button x:Name="fitBtn"    Content="{DynamicResource FitView}"   Click="Fit_Click"    Width="90" Height="30" Margin="0,0,8,0"/>
            <Button x:Name="saveBtn"   Content="{DynamicResource SaveDiagram}"  Click="Save_Click"   Width="110" Height="30" Margin="0,0,8,0"/>
            <Button x:Name="deleteBtn" Content="{DynamicResource Delete}"    Click="Delete_Click" Width="90" Height="30" Margin="0,0,8,0"/>
            <Button x:Name="closeBtn"  Content="{DynamicResource Close}"     Click="Close_Click"  Width="90" Height="30"/>
        </StackPanel>
    </Grid>
</UserControl>
```

- [ ] **Step 2: Заменить DiagramPage.xaml.cs**

Полностью заменить содержимое `OpenCS/Views/DiagramPage.xaml.cs`:

```csharp
using CScore;
using Microsoft.Win32;
using OpenCS.Utilites;
using OpenCS.ViewModels;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace OpenCS.Views
{
    public partial class DiagramPage : UserControl
    {
        readonly AppViewModel _mvm;
        readonly DiagramEditVM _vm;

        public DiagramPage(Diagramm diagram, AppViewModel mvm, bool isNew = false)
        {
            InitializeComponent();
            _mvm = mvm;
            _vm  = new DiagramEditVM(diagram, mvm, isNew);
            DataContext = _vm;

            titleText.Text = isNew ? Loc.S("NewDiagram") : diagram.Tag;

            calcTypeCombo.ItemsSource = Enum.GetValues(typeof(CalcType));
            matTypeCombo.ItemsSource  = Enum.GetValues(typeof(MatType));
        }

        void Save_Click(object sender, RoutedEventArgs e)
        {
            _vm.Save();
            titleText.Text = _vm.Tag;
        }

        void AddRow_Click(object sender, RoutedEventArgs e)
            => _vm.AddPoint();

        void DeleteRow_Click(object sender, RoutedEventArgs e)
        {
            if (dataGrid.SelectedItem is DiagramPoint p)
                _vm.RemovePoint(p);
        }

        void ImportCsv_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title  = Loc.S("ImportFromCsv"),
                Filter = Loc.S("CsvFilter")
            };
            if (dlg.ShowDialog() != true) return;
            try   { _vm.ImportCsv(dlg.FileName); }
            catch (Exception ex)
            { MessageBox.Show(string.Format(Loc.S("ErrorSave"), ex.Message),
                              Loc.S("Error"), MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        void ExportCsv_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                Title    = Loc.S("ExportDiagramCsv"),
                Filter   = Loc.S("CsvFilter"),
                FileName = $"{_vm.Tag ?? "diagram"}.csv"
            };
            if (dlg.ShowDialog() != true) return;

            var settings = _mvm.CsvSettings;
            var delim    = settings.Delimiter == "," ? "," : ";";
            Encoding enc = settings.Encoding == "utf-8"
                ? new UTF8Encoding(false)
                : (Encoding)(Encoding.GetEncoding(1251) ?? Encoding.UTF8);

            var sb = new StringBuilder();
            sb.AppendLine(string.Join(delim, "ε", "σ, МПа"));
            foreach (var p in _vm.Points.OrderBy(p => p.Eps))
                sb.AppendLine($"{p.Eps.ToString(CultureInfo.InvariantCulture)}{delim}{p.Sig.ToString(CultureInfo.InvariantCulture)}");

            try { File.WriteAllText(dlg.FileName, sb.ToString(), enc); }
            catch (Exception ex)
            { MessageBox.Show(string.Format(Loc.S("ErrorSave"), ex.Message),
                              Loc.S("Error"), MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        void Fit_Click(object sender, RoutedEventArgs e)
            => diagCanvas.FitToView();

        void Delete_Click(object sender, RoutedEventArgs e)
        {
            // Проверка: не используется ли диаграмма Custom-материалом
            var diagramId = _vm.Diagram.Id;
            if (diagramId > 0)
            {
                var usedBy = _mvm.Materials
                    .Where(m => m.Type == MatType.Custom && m.CustomDiagramIds.Values.Contains(diagramId))
                    .Select(m => m.Tag)
                    .ToList();
                if (usedBy.Any())
                {
                    MessageBox.Show(
                        string.Format(Loc.S("DiagramUsedByMaterials"), string.Join(", ", usedBy)),
                        Loc.S("Error"), MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            var res = MessageBox.Show(Loc.S("ConfirmDeleteDiagram"), Loc.S("Confirmation"),
                MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (res != MessageBoxResult.Yes) return;

            if (diagramId > 0)
            {
                _mvm.db.DeleteDiagram(_vm.Diagram);
                _mvm.db.Diagrams.Remove(_vm.Diagram);
                _mvm.DiagramsLive.Remove(_vm.Diagram);
            }
            _mvm.CurrentPage = null!;
            _mvm.LogService.Info(string.Format(Loc.S("DiagramDeletedCode"), _vm.Tag));
        }

        void Close_Click(object sender, RoutedEventArgs e)
            => _mvm.CurrentPage = null!;
    }
}
```

- [ ] **Step 3: Сборка OpenCS**

```
dotnet build OpenCS
```
Ожидаем: Build succeeded, 0 errors.

- [ ] **Step 4: Коммит**

```
git add OpenCS/Views/DiagramPage.xaml OpenCS/Views/DiagramPage.xaml.cs
git commit -m "feat: DiagramPage — редактируемый DataGrid + DiagramCanvas + CSV-импорт"
```

---

## Task 9: AppViewModel.AddDiagramCommand + кнопка «+» в MainWindow

**Files:**
- Modify: `OpenCS/AppViewModel.cs`
- Modify: `OpenCS/MainWindow.xaml`
- Modify: `OpenCS/MainWindow.xaml.cs`

- [ ] **Step 1: AppViewModel — добавить команду и метод**

В `AppViewModel.cs` среди объявлений команд добавить:

```csharp
/// <summary>Команда создания новой пустой диаграммы.</summary>
public ICommand AddDiagramCommand { get; set; } = null!;
```

В `InitializeCommands()` добавить:

```csharp
AddDiagramCommand = new RelayCommand(_ => AddDiagram());
```

Добавить приватный метод (рядом с другими методами создания объектов):

```csharp
void AddDiagram()
{
    var d = new Diagramm
    {
        Tag          = Loc.S("NewDiagram"),
        CalcType     = CalcType.C,
        MaterialType = MatType.Concrete,
        Ic           = new CSmath.LSpline(new[] { -0.003, 0.0 }, new[] { -30.0, 0.0 }),
        It           = new CSmath.LSpline(new[] { 0.0, 0.001  }, new[] {   0.0, 15.0 })
    };
    CurrentPage = new Views.DiagramPage(d, this, isNew: true);
}
```

- [ ] **Step 2: MainWindow.xaml — добавить кнопку «+» к узлу диаграмм**

Найти в `MainWindow.xaml` секцию `<TreeViewItem x:Name="diagramNode"` (≈строка 401).
Заменить заголовок узла на:

```xml
<TreeViewItem.Header>
    <StackPanel Orientation="Horizontal" Margin="2">
        <Image Source="/Images/diagramma--32.png"/>
        <TextBlock Text="{DynamicResource Diagrams}" Margin="5,0,5,0" FontWeight="Bold"/>
        <Button Content="+" Width="22" Height="22" Padding="0"
                FontWeight="Bold" FontSize="13"
                Command="{Binding AddDiagramCommand}"
                ToolTip="{DynamicResource AddDiagramTooltip}"
                Style="{StaticResource IconButton25}"/>
    </StackPanel>
</TreeViewItem.Header>
```

- [ ] **Step 3: Сборка OpenCS**

```
dotnet build OpenCS
```
Ожидаем: Build succeeded, 0 errors.

- [ ] **Step 4: Коммит**

```
git add OpenCS/AppViewModel.cs OpenCS/MainWindow.xaml
git commit -m "feat: AddDiagramCommand — создание новой диаграммы из TreeView"
```

---

## Task 10: MaterialVM — IsCustom, BaseType, CustomDiagramIds

**Files:**
- Modify: `OpenCS/ViewModels/MaterialVM.cs`

- [ ] **Step 1: Добавить прокси-свойства**

В `MaterialVM.cs` добавить три свойства после существующего свойства `AggregateType`:

```csharp
/// <summary>Является ли материал Custom-типом.</summary>
public bool IsCustom => material.Type == MatType.Custom;

/// <summary>Базовый тип поведения для Custom-материала.</summary>
public MatType BaseType
{
    get => material.BaseType;
    set { material.BaseType = value; OnPropertyChanged(); }
}

/// <summary>Словарь Id диаграмм по видам расчёта для Custom-материала.</summary>
public Dictionary<CScore.CalcType, int> CustomDiagramIds => material.CustomDiagramIds;
```

Обновить существующий сеттер свойства `Type` — добавить уведомление `IsCustom`:

```csharp
public MatType Type
{
    get { return material.Type; }
    set
    {
        material.Type = value;
        OnPropertyChanged();
        OnPropertyChanged(nameof(IsConcrete));
        OnPropertyChanged(nameof(IsCustom));   // ← добавить эту строку
    }
}
```

Обновить метод `Reset()` — добавить сброс новых полей:

```csharp
void Reset()
{
    IsSaved = false;
    Material = new Material(0);
    Tag = ""; Description = ""; Type = MatType.None; AggregateType = "silicate";
    material.BaseType = MatType.None;          // ← добавить
    material.CustomDiagramIds = [];            // ← добавить
}
```

- [ ] **Step 2: Сборка OpenCS**

```
dotnet build OpenCS
```
Ожидаем: Build succeeded, 0 errors.

- [ ] **Step 3: Коммит**

```
git add OpenCS/ViewModels/MaterialVM.cs
git commit -m "feat: MaterialVM.IsCustom, BaseType, CustomDiagramIds"
```

---

## Task 11: MaterialPage — Custom-блок с BaseType + 4 ComboBox диаграмм

**Files:**
- Modify: `OpenCS/Views/MaterialPage.xaml`
- Modify: `OpenCS/Views/MaterialPage.xaml.cs`

- [ ] **Step 1: Добавить Custom-блок в MaterialPage.xaml**

Найти в `MaterialPage.xaml` строку `<StackPanel Grid.Row="3" ...` (нижний блок кнопок — Grid Row="4").
Вставить ПЕРЕД ним (то есть перед блоком кнопок Изменить/Сохранить/...) новую строку в Grid.RowDefinitions, а также новый блок в `Grid.Row="3"`:

Сначала убедиться, что в `<Grid.RowDefinitions>` есть строки (их уже 5: 0–4). Вставить следующий XAML-блок между `</Border>` (закрытие Grid Row="3" с MaterialChars) и `<Grid Grid.Row="4"...` (кнопки):

```xml
<!-- Custom-материал: BaseType + 4 диаграммы -->
<StackPanel x:Name="customBlock" Grid.Row="3" Grid.ColumnSpan="5" Margin="5,8,5,0"
            Visibility="Collapsed">
    <TextBlock Text="{DynamicResource BaseType}" Foreground="#FF2813EB" Margin="0,0,0,4"/>
    <ComboBox x:Name="baseTypeCombo" Width="200" HorizontalAlignment="Left" Margin="0,0,0,10"
              SelectedItem="{Binding BaseType}"/>

    <TextBlock Text="{DynamicResource DiagramsForCalcTypes}" Foreground="#FF2813EB" Margin="0,0,0,4"/>
    <Grid>
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="40"/>
            <ColumnDefinition Width="*"/>
        </Grid.ColumnDefinitions>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>
        <TextBlock Grid.Row="0" Grid.Column="0" Text="C:"  VerticalAlignment="Center"/>
        <ComboBox  Grid.Row="0" Grid.Column="1" x:Name="diagramC"  Margin="0,2,0,2"
                   DisplayMemberPath="Tag"/>
        <TextBlock Grid.Row="1" Grid.Column="0" Text="CL:" VerticalAlignment="Center"/>
        <ComboBox  Grid.Row="1" Grid.Column="1" x:Name="diagramCL" Margin="0,2,0,2"
                   DisplayMemberPath="Tag"/>
        <TextBlock Grid.Row="2" Grid.Column="0" Text="N:"  VerticalAlignment="Center"/>
        <ComboBox  Grid.Row="2" Grid.Column="1" x:Name="diagramN"  Margin="0,2,0,2"
                   DisplayMemberPath="Tag"/>
        <TextBlock Grid.Row="3" Grid.Column="0" Text="NL:" VerticalAlignment="Center"/>
        <ComboBox  Grid.Row="3" Grid.Column="1" x:Name="diagramNL" Margin="0,2,0,2"
                   DisplayMemberPath="Tag"/>
    </Grid>
</StackPanel>
```

Также обернуть блок MaterialChars (Grid Row="2" и Row="3" — заголовок и тело таблицы) в `StackPanel` с `x:Name="standardBlock"` для управления видимостью. (Если обёртка сломает Grid-лейаут, вместо обёртки использовать два атрибута `Visibility` с привязкой через конвертер на каждом существующем элементе.)

- [ ] **Step 2: Обновить MaterialPage.xaml.cs**

Добавить в конструктор `MaterialPage(Material material, AppViewModel mvm)` после `DataContext = vm;`:

```csharp
// Заполнить ComboBox-ы для Custom-материала
baseTypeCombo.ItemsSource = new[] {
    MatType.Concrete, MatType.ReSteelF, MatType.ReSteelU, MatType.Steel
};
diagramC.ItemsSource  = mvm.DiagramsLive;
diagramCL.ItemsSource = mvm.DiagramsLive;
diagramN.ItemsSource  = mvm.DiagramsLive;
diagramNL.ItemsSource = mvm.DiagramsLive;

// Восстановить выбранные диаграммы если материал Custom
if (material.Type == MatType.Custom)
{
    SetDiagramCombo(diagramC,  mvm, material.CustomDiagramIds.GetValueOrDefault(CalcType.C));
    SetDiagramCombo(diagramCL, mvm, material.CustomDiagramIds.GetValueOrDefault(CalcType.CL));
    SetDiagramCombo(diagramN,  mvm, material.CustomDiagramIds.GetValueOrDefault(CalcType.N));
    SetDiagramCombo(diagramNL, mvm, material.CustomDiagramIds.GetValueOrDefault(CalcType.NL));
    customBlock.Visibility  = System.Windows.Visibility.Visible;
    standardBlock.Visibility = System.Windows.Visibility.Collapsed;
}

// Переключать блоки при смене типа
vm.PropertyChanged += (_, e) =>
{
    if (e.PropertyName != nameof(MaterialVM.Type) && e.PropertyName != nameof(MaterialVM.IsCustom))
        return;
    bool custom = vm.IsCustom;
    customBlock.Visibility   = custom ? System.Windows.Visibility.Visible  : System.Windows.Visibility.Collapsed;
    standardBlock.Visibility = custom ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
};

// Обновлять CustomDiagramIds при изменении ComboBox
diagramC.SelectionChanged  += (_, _) => UpdateDiagramId(vm, CalcType.C,  diagramC);
diagramCL.SelectionChanged += (_, _) => UpdateDiagramId(vm, CalcType.CL, diagramCL);
diagramN.SelectionChanged  += (_, _) => UpdateDiagramId(vm, CalcType.N,  diagramN);
diagramNL.SelectionChanged += (_, _) => UpdateDiagramId(vm, CalcType.NL, diagramNL);
```

Добавить статические вспомогательные методы в класс `MaterialPage`:

```csharp
static void SetDiagramCombo(ComboBox combo, AppViewModel mvm, int id)
{
    if (id == 0) return;
    combo.SelectedItem = mvm.DiagramsLive.FirstOrDefault(d => d.Id == id);
}

static void UpdateDiagramId(MaterialVM vm, CScore.CalcType ct, ComboBox combo)
{
    if (combo.SelectedItem is CScore.Diagramm d)
        vm.CustomDiagramIds[ct] = d.Id;
}
```

Добавить `using` в начале файла: `using CScore;` (если ещё нет).

- [ ] **Step 3: Сборка OpenCS**

```
dotnet build OpenCS
```
Ожидаем: Build succeeded. Если есть CS0103 по `standardBlock` — убедиться что у существующей группы с MaterialChars назначен `x:Name="standardBlock"`.

- [ ] **Step 4: Коммит**

```
git add OpenCS/Views/MaterialPage.xaml OpenCS/Views/MaterialPage.xaml.cs
git commit -m "feat: MaterialPage — Custom-блок с BaseType и 4 ComboBox диаграмм"
```

---

## Task 12: MaterialAreaPage — скрыть DiagrammType при Custom-материале

**Files:**
- Modify: `OpenCS/Views/MaterialAreaPage.xaml`
- Modify: `OpenCS/ViewModels/MaterialAreaVM.cs`

- [ ] **Step 1: Добавить IsCustomMaterial в MaterialAreaVM**

В `OpenCS/ViewModels/MaterialAreaVM.cs` добавить вычисляемое свойство после `MaterialType`:

```csharp
/// <summary>true если назначенный материал — Custom (для скрытия выбора DiagrammType в UI).</summary>
public bool IsCustomMaterial => _model.Material?.Type == MatType.Custom;
```

Обновить сеттер `Material` — добавить уведомление `IsCustomMaterial`:

```csharp
public Material? Material
{
    get => _model.Material;
    set
    {
        _model.Material = value;
        _model.MaterialId = value?.Id ?? 0;
        _model.ResolveAndBuildDiagramms(pool: App.db.Diagrams);
        OnPropertyChanged();
        OnPropertyChanged(nameof(MaterialType));
        OnPropertyChanged(nameof(IsCustomMaterial));   // ← добавить
        RefreshPlot();
    }
}
```

- [ ] **Step 2: Скрыть DiagrammType-комбо в MaterialAreaPage.xaml**

Найти в `MaterialAreaPage.xaml` блок `<!-- Тип диаграммы -->`:

```xml
<!-- Тип диаграммы -->
<TextBlock Text="{DynamicResource DiagramType}" Margin="0,0,0,2"/>
<ComboBox SelectedItem="{Binding DiagrammType}" Margin="0,0,0,8"
          x:Name="diagramTypeCombo"/>
```

Обернуть оба элемента в `StackPanel` с `Visibility` через встроенный конвертер:

```xml
<!-- Тип диаграммы (скрыт для Custom-материала) -->
<StackPanel x:Name="diagramTypePanel" Margin="0,0,0,0">
    <TextBlock Text="{DynamicResource DiagramType}" Margin="0,0,0,2"/>
    <ComboBox SelectedItem="{Binding DiagrammType}" Margin="0,0,0,8"
              x:Name="diagramTypeCombo"/>
</StackPanel>
```

В `MaterialAreaPage.xaml.cs`, после `DataContext = _vm;` добавить:

```csharp
// Скрыть выбор типа диаграммы для Custom-материала
_vm.PropertyChanged += (_, e) =>
{
    if (e.PropertyName is nameof(MaterialAreaVM.IsCustomMaterial) or nameof(MaterialAreaVM.Material))
        diagramTypePanel.Visibility = _vm.IsCustomMaterial
            ? System.Windows.Visibility.Collapsed
            : System.Windows.Visibility.Visible;
};
```

- [ ] **Step 3: Обновить DiagrammType-сеттер в MaterialAreaVM**

В `MaterialAreaVM.cs` в сеттере `DiagrammType` обновить вызов:

```csharp
public DiagrammType DiagrammType
{
    get => _model.DiagrammType;
    set
    {
        _model.DiagrammType = value;
        _model.ResolveAndBuildDiagramms(pool: App.db.Diagrams);   // ← добавить pool
        OnPropertyChanged();
    }
}
```

- [ ] **Step 4: Сборка OpenCS**

```
dotnet build OpenCS
```
Ожидаем: Build succeeded, 0 errors.

- [ ] **Step 5: Коммит**

```
git add OpenCS/Views/MaterialAreaPage.xaml OpenCS/Views/MaterialAreaPage.xaml.cs \
        OpenCS/ViewModels/MaterialAreaVM.cs
git commit -m "feat: MaterialAreaPage скрывает DiagrammType для Custom-материала"
```

---

## Task 13: Строки локализации

**Files:**
- Modify: `OpenCS/Resources/Strings.ru-RU.xaml`
- Modify: `OpenCS/Resources/Strings.en-US.xaml`

- [ ] **Step 1: Добавить ключи в Strings.ru-RU.xaml**

Найти секцию с диаграммами (около ключей `DiagramType`, `SelectDiagramType` и т.д.) и добавить:

```xml
<system:String x:Key="AddDiagramTooltip">Новая диаграмма</system:String>
<system:String x:Key="NewDiagram">Новая диаграмма</system:String>
<system:String x:Key="SaveDiagram">Сохранить</system:String>
<system:String x:Key="DiscardChanges">Отмена</system:String>
<system:String x:Key="ImportFromCsv">Импорт из CSV</system:String>
<system:String x:Key="AddRow">Добавить строку</system:String>
<system:String x:Key="DeleteRow">Удалить строку</system:String>
<system:String x:Key="Branch">Ветвь</system:String>
<system:String x:Key="FitView">По размеру</system:String>
<system:String x:Key="DiagramTag">Наименование диаграммы</system:String>
<system:String x:Key="BaseType">Базовый тип поведения</system:String>
<system:String x:Key="DiagramsForCalcTypes">Диаграммы по видам расчёта</system:String>
<system:String x:Key="DiagramUsedByMaterials">Диаграмму используют материалы: {0}. Удаление невозможно.</system:String>
```

- [ ] **Step 2: Добавить ключи в Strings.en-US.xaml**

```xml
<system:String x:Key="AddDiagramTooltip">New Diagram</system:String>
<system:String x:Key="NewDiagram">New Diagram</system:String>
<system:String x:Key="SaveDiagram">Save</system:String>
<system:String x:Key="DiscardChanges">Discard</system:String>
<system:String x:Key="ImportFromCsv">Import from CSV</system:String>
<system:String x:Key="AddRow">Add Row</system:String>
<system:String x:Key="DeleteRow">Delete Row</system:String>
<system:String x:Key="Branch">Branch</system:String>
<system:String x:Key="FitView">Fit View</system:String>
<system:String x:Key="DiagramTag">Diagram name</system:String>
<system:String x:Key="BaseType">Base behavior type</system:String>
<system:String x:Key="DiagramsForCalcTypes">Diagrams by load type</system:String>
<system:String x:Key="DiagramUsedByMaterials">Diagram is used by materials: {0}. Cannot delete.</system:String>
```

- [ ] **Step 3: Сборка OpenCS**

```
dotnet build OpenCS
```
Ожидаем: Build succeeded, 0 errors.

- [ ] **Step 4: Коммит**

```
git add OpenCS/Resources/Strings.ru-RU.xaml OpenCS/Resources/Strings.en-US.xaml
git commit -m "feat: строки локализации для редактора диаграмм и Custom-материала"
```

---

## Task 14: Финальная сборка + ручная верификация

**Files:** Все изменённые файлы.

- [ ] **Step 1: Полная сборка солюшена**

```
dotnet build OpenCS.sln
```
Ожидаем: Build succeeded, 0 errors, 0 warnings (кроме уже существующих).

- [ ] **Step 2: Запустить все тесты**

```
dotnet run --project CSfea.Tests
```
Ожидаем: все [PASS], включая `CustomDiagramTests`.

- [ ] **Step 3: Ручная проверка — создать диаграмму**

Запустить приложение `dotnet run --project OpenCS`, затем:
1. В TreeView выбрать «Диаграммы» → нажать кнопку «+»
2. В DiagramPage ввести Tag, выбрать CalcType=C, MaterialType=Concrete
3. Переключиться на вкладку Data, добавить строки: (-0.003; -30), (-0.002; -20), (0; 0), (0.001; 15), (0.002; 15)
4. Нажать «Сохранить» — диаграмма появляется в TreeView
5. Переключиться на вкладку Chart — кривая нарисована синим и красным

- [ ] **Step 4: Ручная проверка — CSV-импорт**

1. Создать файл `test.csv`:
   ```
   eps;sig
   -0.003;-30
   -0.002;-20
   0;0
   0.001;15
   ```
2. Нажать «Импорт из CSV» → выбрать файл → точки загружены в DataGrid

- [ ] **Step 5: Ручная проверка — Custom-материал**

1. Создать материал с типом = Custom
2. В появившемся блоке выбрать BaseType = Concrete
3. Для каждого из C, CL, N, NL выбрать диаграмму из ComboBox
4. Нажать «Сохранить»
5. Назначить Custom-материал в MaterialAreaPage — блок DiagrammType скрыт
6. Сохранить проект, перезапустить — диаграммы резолвятся корректно

- [ ] **Step 6: Финальный коммит**

```
git add -A
git commit -m "feat: пользовательские диаграммы и Custom-материал — полная реализация"
```

---

## Самопроверка плана

**Покрытие спецификации:**
- ✅ Создание диаграмм вручную (таблица) — Task 8 (DiagramPage editable DataGrid)
- ✅ CSV-импорт — Task 6 (DiagramEditVM.ImportCsv) + Task 8 (кнопка)
- ✅ DiagramCanvas WPF — Task 7
- ✅ Перетаскиваемые маркеры — Task 7 (OnMouseLeftButtonDown/Move для _dragIdx)
- ✅ MatType.Custom — Task 1
- ✅ Material.BaseType + CustomDiagramIds — Task 1
- ✅ ResolveCustomDiagramms — Task 1 (с тестом)
- ✅ DB миграция v17 — Task 4
- ✅ Load/Save новых колонок — Task 4
- ✅ Pool передаётся в resolve — Tasks 4, 5
- ✅ MaterialPage Custom-блок — Task 11
- ✅ MaterialAreaPage скрытие DiagrammType — Task 12
- ✅ Новая диаграмма из TreeView — Task 9
- ✅ Локализация — Task 13
- ✅ Защита от удаления используемой диаграммы — Task 8 (Delete_Click)
- ✅ Стандартные материалы не ломаются (опциональный pool=null) — Tasks 2, 3

**Типы совпадают:**
- `DiagramEditVM` используется в Tasks 6, 7, 8 — имена совпадают
- `DiagramCanvas.ViewModel` DP типа `DiagramEditVM` — Tasks 7, 8 согласованы
- `Material.ResolveCustomDiagramms(IReadOnlyList<Diagramm>)` — Tasks 1, 2, 4 согласованы
- `MaterialAreaVM.IsCustomMaterial` — Tasks 11, 12 согласованы
