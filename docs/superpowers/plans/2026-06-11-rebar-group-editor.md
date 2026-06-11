# Rebar Group Editor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Заменить `MaterialAreaPage` для категории `RebarGroup` специализированной страницей интерактивного размещения арматурных стержней с тремя стратегиями, WPF-холстом и двусторонней синхронизацией холст ↔ таблица.

**Architecture:** Три новых файла VM (`BarItem`, `EdgeItem`, `RebarGroupEditorVM`) и два файла View (`RebarGroupCanvas` как кастомный `FrameworkElement`, `RebarGroupEditorPage` как UserControl). Холст рисует через `OnRender`/`DrawingContext`, hit-test вручную, drag через `CaptureMouse`. VM вычисляет `CoverLinePoints` при каждом изменении `EdgeItem.Offset`.

**Tech Stack:** .NET 9, WPF, C#, `System.Windows.Media.DrawingContext`, `Microsoft.Data.Sqlite` (через существующий `DatabaseService`).

---

## Файловая карта

| Действие | Файл |
|----------|------|
| Создать | `OpenCS/ViewModels/BarItem.cs` |
| Создать | `OpenCS/ViewModels/EdgeItem.cs` |
| Создать | `OpenCS/ViewModels/RebarGroupEditorVM.cs` |
| Создать | `OpenCS/Views/RebarGroupCanvas.cs` |
| Создать | `OpenCS/Views/RebarGroupEditorPage.xaml` |
| Создать | `OpenCS/Views/RebarGroupEditorPage.xaml.cs` |
| Изменить | `OpenCS/AppViewModel.cs` — строки NewRebarGroup(), CurrentMaterialArea.set |
| Изменить | `OpenCS/Resources/Strings.ru-RU.xaml` |
| Изменить | `OpenCS/Resources/Strings.en-US.xaml` |

---

## Task 1: BarItem и EdgeItem

**Files:**
- Create: `OpenCS/ViewModels/BarItem.cs`
- Create: `OpenCS/ViewModels/EdgeItem.cs`

- [ ] **Step 1: Создать BarItem.cs**

```csharp
namespace OpenCS.ViewModels
{
    /// <summary>Один арматурный стержень в группе.</summary>
    public class BarItem : ViewModelBase
    {
        double _x, _y, _d;
        bool _isSelected;

        public int Index { get; set; }

        public double X
        {
            get => _x;
            set { _x = value; OnPropertyChanged(); }
        }

        public double Y
        {
            get => _y;
            set { _y = value; OnPropertyChanged(); }
        }

        /// <summary>Диаметр в метрах.</summary>
        public double Diameter
        {
            get => _d;
            set { _d = value; OnPropertyChanged(); OnPropertyChanged(nameof(DiameterMm)); }
        }

        /// <summary>Диаметр в мм — для отображения в UI.</summary>
        public double DiameterMm
        {
            get => _d * 1000;
            set { Diameter = value / 1000; }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }
    }
}
```

- [ ] **Step 2: Создать EdgeItem.cs**

```csharp
namespace OpenCS.ViewModels
{
    /// <summary>Ребро линии защитного слоя бетона.</summary>
    public class EdgeItem : ViewModelBase
    {
        double _offset;

        public int Index { get; set; }

        /// <summary>Отступ от опорного ребра в метрах (≥ 0).</summary>
        public double Offset
        {
            get => _offset;
            set { _offset = value < 0 ? 0 : value; OnPropertyChanged(); }
        }

        // Геометрия опорного ребра (задаётся при инициализации, не меняется)
        public double StartX { get; init; }
        public double StartY { get; init; }
        public double EndX   { get; init; }
        public double EndY   { get; init; }

        /// <summary>Единичная внутренняя нормаль.</summary>
        public double NormalX { get; init; }
        public double NormalY { get; init; }

        /// <summary>Экранная позиция ручки: середина ребра + Offset*Normal.</summary>
        public (double X, double Y) HandlePoint =>
        (
            (StartX + EndX) / 2 + Offset * NormalX,
            (StartY + EndY) / 2 + Offset * NormalY
        );
    }
}
```

- [ ] **Step 3: Собрать проект**

```
dotnet build OpenCS.sln -c Debug
```

Ожидаемый результат: `Ошибок: 0`

- [ ] **Step 4: Коммит**

```
git add OpenCS/ViewModels/BarItem.cs OpenCS/ViewModels/EdgeItem.cs
git commit -m "feat(rebar-editor): BarItem and EdgeItem view models"
```

---

## Task 2: RebarGroupEditorVM — свойства, стратегия, опора, cover line

**Files:**
- Create: `OpenCS/ViewModels/RebarGroupEditorVM.cs`

- [ ] **Step 1: Создать RebarGroupEditorVM.cs**

```csharp
using CScore;
using OpenCS.Utilites;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;

namespace OpenCS.ViewModels
{
    public enum RebarPlacementStrategy { FromRegion, FromContour, Bare }

    /// <summary>ViewModel страницы задания группы арматурных стержней.</summary>
    public class RebarGroupEditorVM : ViewModelBase
    {
        public AppViewModel App { get; }
        public MaterialArea? EditedArea { get; }

        RebarPlacementStrategy _strategy = RebarPlacementStrategy.Bare;
        MaterialArea? _selectedRegion;
        Contour? _selectedContour;
        double _globalOffset = 0.025;
        double _offsetStep   = 0.001;
        double _activeDiameter = 0.012; // 12 мм
        string _tag = "Группа";
        bool _fillMode;
        int _fillCount = 2;
        bool _fillUseArc;
        double _fillArcRadius = 0.15;
        IReadOnlyList<(double X, double Y)> _coverLinePoints = [];
        IReadOnlyList<(double X, double Y)> _referencePoints = [];

        public RebarGroupEditorVM(MaterialArea? area, AppViewModel app)
        {
            App = app;
            EditedArea = area;

            Bars  = [];
            Edges = [];

            // Кортежи передаются как ValueTuple — используем явную форму для надёжного pattern matching
            AddBarCommand         = new RelayCommand(o => { if (o is ValueTuple<double,double> pt) AddBar(pt.Item1, pt.Item2); });
            MoveBarCommand        = new RelayCommand(o => { if (o is ValueTuple<BarItem,double,double> mt) MoveBar(mt.Item1, mt.Item2, mt.Item3); });
            DeleteBarCommand      = new RelayCommand(o => { if (o is BarItem b) DeleteBar(b); });
            SelectBarCommand      = new RelayCommand(o => SelectBar(o as BarItem));
            AdjustEdgeCommand     = new RelayCommand(o => { if (o is ValueTuple<EdgeItem,double> et) AdjustEdge(et.Item1, et.Item2); });
            MoveEdgeHandleCommand = new RelayCommand(o => { if (o is ValueTuple<EdgeItem,double> eh) SetEdgeOffset(eh.Item1, eh.Item2); });
            ResetAllOffsetsCommand= new RelayCommand(_ => ResetAllOffsets());
            FillBetweenCommand    = new RelayCommand(o => { if (o is ValueTuple<BarItem,BarItem> fb) FillBetween(fb.Item1, fb.Item2); });
            SaveCommand           = new RelayCommand(_ => Save());
            CancelCommand         = new RelayCommand(_ => App.CurrentPage = null!);

            // Определить начальную стратегию
            if (app.AreasLive.Any())       _strategy = RebarPlacementStrategy.FromRegion;
            else if (app.Contours.Any())   _strategy = RebarPlacementStrategy.FromContour;

            // Загрузить данные существующей области
            if (area != null)
            {
                _tag = area.Tag;
                foreach (var f in area.Fibers.Where(f => f.TypeFiber == FiberType.point))
                    Bars.Add(new BarItem { X = f.X, Y = f.Y, Diameter = f.Diameter, Index = Bars.Count + 1 });
            }

            InitStrategyReference();
        }

        // ── Стратегия ────────────────────────────────────────────────────────

        public RebarPlacementStrategy Strategy
        {
            get => _strategy;
            set { _strategy = value; OnPropertyChanged(); InitStrategyReference(); }
        }

        public bool StrategyFromRegion  { get => _strategy == RebarPlacementStrategy.FromRegion;  set { if (value) Strategy = RebarPlacementStrategy.FromRegion; } }
        public bool StrategyFromContour { get => _strategy == RebarPlacementStrategy.FromContour; set { if (value) Strategy = RebarPlacementStrategy.FromContour; } }
        public bool StrategyBare        { get => _strategy == RebarPlacementStrategy.Bare;        set { if (value) Strategy = RebarPlacementStrategy.Bare; } }

        public bool HasReference => _strategy != RebarPlacementStrategy.Bare;

        public IReadOnlyList<MaterialArea> AvailableRegions  => App.AreasLive;
        public IReadOnlyList<Contour>      AvailableContours => App.Contours;

        public MaterialArea? SelectedRegion
        {
            get => _selectedRegion;
            set { _selectedRegion = value; OnPropertyChanged(); if (value != null) BuildEdgesFromContour(GetHullPoints(value.Hull)); }
        }

        public Contour? SelectedContour
        {
            get => _selectedContour;
            set { _selectedContour = value; OnPropertyChanged(); if (value != null) BuildEdgesFromContour(ContourPoints(value)); }
        }

        // ── Линия защитного слоя ─────────────────────────────────────────────

        public double GlobalOffset
        {
            get => _globalOffset;
            set { _globalOffset = value; OnPropertyChanged(); }
        }

        public double OffsetStep
        {
            get => _offsetStep;
            set { _offsetStep = value; OnPropertyChanged(); }
        }

        public ObservableCollection<EdgeItem> Edges { get; }

        public IReadOnlyList<(double X, double Y)> CoverLinePoints
        {
            get => _coverLinePoints;
            private set { _coverLinePoints = value; OnPropertyChanged(); }
        }

        public IReadOnlyList<(double X, double Y)> ReferencePoints
        {
            get => _referencePoints;
            private set { _referencePoints = value; OnPropertyChanged(); }
        }

        // ── Стержни ──────────────────────────────────────────────────────────

        public double ActiveDiameter
        {
            get => _activeDiameter;
            set { _activeDiameter = value; OnPropertyChanged(); OnPropertyChanged(nameof(ActiveDiameterMm)); }
        }

        public double ActiveDiameterMm
        {
            get => _activeDiameter * 1000;
            set { ActiveDiameter = value / 1000; }
        }

        public BarItem? SelectedBar { get; private set; }

        public ObservableCollection<BarItem> Bars { get; }

        // ── Fill-between ─────────────────────────────────────────────────────

        public bool FillMode
        {
            get => _fillMode;
            set { _fillMode = value; OnPropertyChanged(); }
        }

        public int FillCount
        {
            get => _fillCount;
            set { _fillCount = value < 1 ? 1 : value; OnPropertyChanged(); }
        }

        public bool FillUseArc
        {
            get => _fillUseArc;
            set { _fillUseArc = value; OnPropertyChanged(); }
        }

        public double FillArcRadius
        {
            get => _fillArcRadius;
            set { _fillArcRadius = value; OnPropertyChanged(); }
        }

        // ── Сохранение ───────────────────────────────────────────────────────

        public string Tag
        {
            get => _tag;
            set { _tag = value; OnPropertyChanged(); }
        }

        // ── Команды ──────────────────────────────────────────────────────────

        public ICommand AddBarCommand          { get; }
        public ICommand MoveBarCommand         { get; }
        public ICommand DeleteBarCommand       { get; }
        public ICommand SelectBarCommand       { get; }
        public ICommand AdjustEdgeCommand      { get; }
        public ICommand MoveEdgeHandleCommand  { get; }
        public ICommand ResetAllOffsetsCommand { get; }
        public ICommand FillBetweenCommand     { get; }
        public ICommand SaveCommand            { get; }
        public ICommand CancelCommand          { get; }

        // ── Инициализация ────────────────────────────────────────────────────

        void InitStrategyReference()
        {
            OnPropertyChanged(nameof(StrategyFromRegion));
            OnPropertyChanged(nameof(StrategyFromContour));
            OnPropertyChanged(nameof(StrategyBare));
            OnPropertyChanged(nameof(HasReference));

            if (_strategy == RebarPlacementStrategy.FromRegion && AvailableRegions.Any())
            {
                SelectedRegion = AvailableRegions[0];
            }
            else if (_strategy == RebarPlacementStrategy.FromContour && AvailableContours.Any())
            {
                SelectedContour = AvailableContours[0];
            }
            else
            {
                Edges.Clear();
                ReferencePoints = [];
                CoverLinePoints = [];
            }
        }

        void BuildEdgesFromContour(List<(double X, double Y)> pts)
        {
            if (pts.Count < 3) return;
            // Убедиться что контур CCW
            if (SignedArea(pts) < 0) pts.Reverse();

            ReferencePoints = pts;
            Edges.Clear();

            int n = pts.Count;
            for (int i = 0; i < n; i++)
            {
                var (sx, sy) = pts[i];
                var (ex, ey) = pts[(i + 1) % n];
                double len = Math.Sqrt((ex - sx) * (ex - sx) + (ey - sy) * (ey - sy));
                if (len < 1e-10) continue;
                // Левая нормаль для CCW-контура = внутренняя
                double nx = -(ey - sy) / len;
                double ny =  (ex - sx) / len;
                Edges.Add(new EdgeItem
                {
                    Index   = Edges.Count + 1,
                    Offset  = _globalOffset,
                    StartX  = sx, StartY = sy,
                    EndX    = ex, EndY   = ey,
                    NormalX = nx, NormalY = ny
                });
            }
            RecomputeCoverLine();
        }

        static List<(double X, double Y)> GetHullPoints(Contour? hull)
        {
            if (hull == null || hull.X.Count < 3) return [];
            var pts = new List<(double X, double Y)>(hull.X.Count);
            for (int i = 0; i < hull.X.Count; i++)
                pts.Add((hull.X[i], hull.Y[i]));
            return pts;
        }

        static List<(double X, double Y)> ContourPoints(Contour c)
        {
            var pts = new List<(double X, double Y)>(c.X.Count);
            for (int i = 0; i < c.X.Count; i++)
                pts.Add((c.X[i], c.Y[i]));
            return pts;
        }

        static double SignedArea(List<(double X, double Y)> pts)
        {
            double a = 0;
            int n = pts.Count;
            for (int i = 0; i < n; i++)
            {
                var (x1, y1) = pts[i];
                var (x2, y2) = pts[(i + 1) % n];
                a += x1 * y2 - x2 * y1;
            }
            return a / 2;
        }

        // ── Вычисление линии защитного слоя ──────────────────────────────────

        public void RecomputeCoverLine()
        {
            int n = Edges.Count;
            if (n < 3) { CoverLinePoints = []; return; }

            var pts = new (double X, double Y)[n];
            for (int i = 0; i < n; i++)
            {
                var ePrev = Edges[(i - 1 + n) % n];
                var eCurr = Edges[i];

                double q1x = ePrev.StartX + ePrev.Offset * ePrev.NormalX;
                double q1y = ePrev.StartY + ePrev.Offset * ePrev.NormalY;
                double d1x = ePrev.EndX - ePrev.StartX;
                double d1y = ePrev.EndY - ePrev.StartY;

                double q2x = eCurr.StartX + eCurr.Offset * eCurr.NormalX;
                double q2y = eCurr.StartY + eCurr.Offset * eCurr.NormalY;
                double d2x = eCurr.EndX - eCurr.StartX;
                double d2y = eCurr.EndY - eCurr.StartY;

                pts[i] = IntersectLines(q1x, q1y, d1x, d1y, q2x, q2y, d2x, d2y);
            }
            CoverLinePoints = pts;
        }

        /// <summary>Пересечение двух параметрических прямых: Q1+t*d1 и Q2+s*d2.</summary>
        static (double X, double Y) IntersectLines(
            double q1x, double q1y, double d1x, double d1y,
            double q2x, double q2y, double d2x, double d2y)
        {
            double cross = d1x * d2y - d1y * d2x;
            if (Math.Abs(cross) < 1e-12) return (q1x, q1y);
            double dx = q2x - q1x, dy = q2y - q1y;
            double t = (d2y * dx - d2x * dy) / cross;
            return (q1x + t * d1x, q1y + t * d1y);
        }
    }
}
```

- [ ] **Step 2: Собрать проект**

```
dotnet build OpenCS.sln -c Debug
```

Ожидаемый результат: `Ошибок: 0`

- [ ] **Step 3: Коммит**

```
git add OpenCS/ViewModels/RebarGroupEditorVM.cs
git commit -m "feat(rebar-editor): RebarGroupEditorVM — strategy, edges, cover line"
```

---

## Task 3: RebarGroupEditorVM — команды стержней и FillBetween

**Files:**
- Modify: `OpenCS/ViewModels/RebarGroupEditorVM.cs` — добавить методы в конец класса перед закрывающей `}`

- [ ] **Step 1: Добавить методы управления стержнями и FillBetween**

Вставить перед последней `}` класса `RebarGroupEditorVM`:

```csharp
        // ── Методы стержней ───────────────────────────────────────────────────

        void AddBar(double x, double y)
        {
            var bar = new BarItem { X = x, Y = y, Diameter = _activeDiameter, Index = Bars.Count + 1 };
            Bars.Add(bar);
            RenumberBars();
        }

        void MoveBar(BarItem bar, double x, double y)
        {
            bar.X = x;
            bar.Y = y;
        }

        void DeleteBar(BarItem bar)
        {
            Bars.Remove(bar);
            RenumberBars();
            if (SelectedBar == bar) SelectBar(null);
        }

        void SelectBar(BarItem? bar)
        {
            if (SelectedBar != null) SelectedBar.IsSelected = false;
            SelectedBar = bar;
            if (bar != null) bar.IsSelected = true;
            OnPropertyChanged(nameof(SelectedBar));
        }

        void RenumberBars()
        {
            for (int i = 0; i < Bars.Count; i++)
                Bars[i].Index = i + 1;
        }

        // ── Методы рёбер ──────────────────────────────────────────────────────

        void AdjustEdge(EdgeItem edge, double delta)
        {
            edge.Offset = Math.Max(0, edge.Offset + delta);
            RecomputeCoverLine();
        }

        void SetEdgeOffset(EdgeItem edge, double newOffset)
        {
            // Округлить до шага
            double step = _offsetStep > 0 ? _offsetStep : 0.001;
            edge.Offset = Math.Max(0, Math.Round(newOffset / step) * step);
            RecomputeCoverLine();
        }

        void ResetAllOffsets()
        {
            foreach (var e in Edges)
                e.Offset = _globalOffset;
            RecomputeCoverLine();
        }

        // ── Fill Between ──────────────────────────────────────────────────────

        void FillBetween(BarItem b1, BarItem b2)
        {
            FillMode = false;
            if (_fillUseArc)
                FillBetweenArc(b1, b2);
            else
                FillBetweenStraight(b1, b2);
            RenumberBars();
        }

        void FillBetweenStraight(BarItem b1, BarItem b2)
        {
            int n = _fillCount;
            double dx = (b2.X - b1.X) / (n + 1);
            double dy = (b2.Y - b1.Y) / (n + 1);
            // Вставить после b1 в правильном порядке
            int idx = Bars.IndexOf(b1) + 1;
            for (int k = 1; k <= n; k++)
                Bars.Insert(idx + k - 1, new BarItem
                {
                    X = b1.X + k * dx,
                    Y = b1.Y + k * dy,
                    Diameter = _activeDiameter
                });
        }

        void FillBetweenArc(BarItem b1, BarItem b2)
        {
            double midX = (b1.X + b2.X) / 2;
            double midY = (b1.Y + b2.Y) / 2;
            double halfChord = Math.Sqrt((b2.X - b1.X) * (b2.X - b1.X) + (b2.Y - b1.Y) * (b2.Y - b1.Y)) / 2;
            double R = _fillArcRadius;
            if (R < halfChord + 1e-6) R = halfChord + 1e-6;
            double h = Math.Sqrt(R * R - halfChord * halfChord);

            // Перпендикуляр к хорде (левая нормаль)
            double chordDx = b2.X - b1.X, chordDy = b2.Y - b1.Y;
            double len = Math.Sqrt(chordDx * chordDx + chordDy * chordDy);
            double perpX = -chordDy / len, perpY = chordDx / len;

            // Центр дуги со стороны нормали (к центру опорного контура если есть, иначе левая сторона)
            (double cx, double cy) = ChooseArcCenter(midX, midY, perpX, perpY, h);

            double angle1 = Math.Atan2(b1.Y - cy, b1.X - cx);
            double angle2 = Math.Atan2(b2.Y - cy, b2.X - cx);
            // Короткая дуга
            double dAngle = angle2 - angle1;
            if (dAngle > Math.PI)  dAngle -= 2 * Math.PI;
            if (dAngle < -Math.PI) dAngle += 2 * Math.PI;

            int n = _fillCount;
            int idx = Bars.IndexOf(b1) + 1;
            for (int k = 1; k <= n; k++)
            {
                double a = angle1 + k * dAngle / (n + 1);
                Bars.Insert(idx + k - 1, new BarItem
                {
                    X = cx + R * Math.Cos(a),
                    Y = cy + R * Math.Sin(a),
                    Diameter = _activeDiameter
                });
            }
        }

        (double cx, double cy) ChooseArcCenter(double mx, double my,
            double perpX, double perpY, double h)
        {
            // Выбрать центр, ближайший к центру опорного контура
            double cx1 = mx + h * perpX, cy1 = my + h * perpY;
            double cx2 = mx - h * perpX, cy2 = my - h * perpY;
            if (_referencePoints.Count == 0) return (cx1, cy1);

            double refCx = _referencePoints.Average(p => p.X);
            double refCy = _referencePoints.Average(p => p.Y);
            double d1 = (cx1 - refCx) * (cx1 - refCx) + (cy1 - refCy) * (cy1 - refCy);
            double d2 = (cx2 - refCx) * (cx2 - refCx) + (cy2 - refCy) * (cy2 - refCy);
            return d1 < d2 ? (cx1, cy1) : (cx2, cy2);
        }

        // ── Сохранение ────────────────────────────────────────────────────────

        void Save()
        {
            var area = EditedArea ?? new MaterialArea();
            area.Tag      = _tag;
            area.Category = AreaCategory.RebarGroup;
            area.HostAreaId = _selectedRegion?.Id;
            area.Fibers.Clear();
            foreach (var b in Bars)
                area.Fibers.Add(Fiber.CreatePoint(b.Diameter, b.X, b.Y));
            App.db.SaveMaterialArea(area);
            if (!App.MaterialAreas.Contains(area))
                App.MaterialAreas.Add(area);
            else
            {
                App.RefreshMaterialAreaLiveCollections();
                App.IsDirty = true;
            }
            App.LogService.Info($"Группа арматуры «{area.Tag}» сохранена");
        }
```

- [ ] **Step 2: Собрать проект**

```
dotnet build OpenCS.sln -c Debug
```

Ожидаемый результат: `Ошибок: 0`

- [ ] **Step 3: Коммит**

```
git add OpenCS/ViewModels/RebarGroupEditorVM.cs
git commit -m "feat(rebar-editor): bar commands, edge adjustment, FillBetween, Save"
```

---

## Task 4: Подключить AppViewModel

**Files:**
- Modify: `OpenCS/AppViewModel.cs`

- [ ] **Step 1: Изменить NewRebarGroup() и CurrentMaterialArea.set**

Найти в `AppViewModel.cs` метод `NewRebarGroup()` и заменить:

```csharp
      void NewRebarGroup()
      {
         var area = new MaterialArea
         {
            Tag = $"Группа {RebarGroupsLive.Count + 1}",
            Category = AreaCategory.RebarGroup
         };
         CurrentPage = new Views.RebarGroupEditorPage(area, this);
      }
```

Найти setter `CurrentMaterialArea` и заменить:

```csharp
      public MaterialArea? CurrentMaterialArea
      {
         get => currentMaterialArea;
         set
         {
            currentMaterialArea = value;
            if (value != null)
               CurrentPage = value.Category == AreaCategory.RebarGroup
                  ? (System.Windows.Controls.UserControl)new Views.RebarGroupEditorPage(value, this)
                  : new Views.MaterialAreaPage(value, this);
         }
      }
```

- [ ] **Step 2: Собрать проект (ожидается ошибка — RebarGroupEditorPage ещё не создана)**

```
dotnet build OpenCS.sln -c Debug
```

Ожидаемый результат: ошибка `CS0246: тип 'RebarGroupEditorPage' не найден` — это нормально, продолжаем.

- [ ] **Step 3: Коммит (с TODO-заглушкой)**

```
git add OpenCS/AppViewModel.cs
git commit -m "feat(rebar-editor): route RebarGroup to RebarGroupEditorPage"
```

---

## Task 5: Строковые ресурсы

**Files:**
- Modify: `OpenCS/Resources/Strings.ru-RU.xaml`
- Modify: `OpenCS/Resources/Strings.en-US.xaml`

- [ ] **Step 1: Добавить ключи в Strings.ru-RU.xaml**

Вставить перед закрывающим `</ResourceDictionary>`:

```xml
   <!-- Rebar Group Editor -->
   <system:String x:Key="RgStrategyGroup">Стратегия</system:String>
   <system:String x:Key="RgStrategyRegion">По области</system:String>
   <system:String x:Key="RgStrategyContour">По контуру</system:String>
   <system:String x:Key="RgStrategyFree">Свободная</system:String>
   <system:String x:Key="RgReference">Опора</system:String>
   <system:String x:Key="RgCoverOffset">Отступ</system:String>
   <system:String x:Key="RgOffsetStep">Шаг</system:String>
   <system:String x:Key="RgResetOffsets">Сбросить все</system:String>
   <system:String x:Key="RgBarDiameter">Ø</system:String>
   <system:String x:Key="RgBarDiameterUnit">мм</system:String>
   <system:String x:Key="RgFillN">N</system:String>
   <system:String x:Key="RgFillStraight">По прямой</system:String>
   <system:String x:Key="RgFillArc">По дуге</system:String>
   <system:String x:Key="RgFillArcR">R, м</system:String>
   <system:String x:Key="RgFillMode">Режим заполнения</system:String>
   <system:String x:Key="RgEdgesTable">Рёбра</system:String>
   <system:String x:Key="RgBarsTable">Стержни</system:String>
   <system:String x:Key="RgGroupTag">Тег группы</system:String>
```

- [ ] **Step 2: Добавить ключи в Strings.en-US.xaml**

```xml
   <!-- Rebar Group Editor -->
   <system:String x:Key="RgStrategyGroup">Strategy</system:String>
   <system:String x:Key="RgStrategyRegion">By region</system:String>
   <system:String x:Key="RgStrategyContour">By contour</system:String>
   <system:String x:Key="RgStrategyFree">Free</system:String>
   <system:String x:Key="RgReference">Reference</system:String>
   <system:String x:Key="RgCoverOffset">Cover</system:String>
   <system:String x:Key="RgOffsetStep">Step</system:String>
   <system:String x:Key="RgResetOffsets">Reset all</system:String>
   <system:String x:Key="RgBarDiameter">Ø</system:String>
   <system:String x:Key="RgBarDiameterUnit">mm</system:String>
   <system:String x:Key="RgFillN">N</system:String>
   <system:String x:Key="RgFillStraight">Straight</system:String>
   <system:String x:Key="RgFillArc">By arc</system:String>
   <system:String x:Key="RgFillArcR">R, m</system:String>
   <system:String x:Key="RgFillMode">Fill mode</system:String>
   <system:String x:Key="RgEdgesTable">Edges</system:String>
   <system:String x:Key="RgBarsTable">Bars</system:String>
   <system:String x:Key="RgGroupTag">Group tag</system:String>
```

- [ ] **Step 3: Собрать проект**

```
dotnet build OpenCS.sln -c Debug
```

Ожидаемый результат: прежние ошибки из Task 4 (RebarGroupEditorPage не найден).

- [ ] **Step 4: Коммит**

```
git add OpenCS/Resources/Strings.ru-RU.xaml OpenCS/Resources/Strings.en-US.xaml
git commit -m "feat(rebar-editor): localization strings"
```

---

## Task 6: RebarGroupCanvas — рендеринг

**Files:**
- Create: `OpenCS/Views/RebarGroupCanvas.cs`

- [ ] **Step 1: Создать RebarGroupCanvas.cs**

```csharp
using OpenCS.ViewModels;

using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace OpenCS.Views
{
    /// <summary>
    /// Интерактивный WPF-холст для редактора групп арматуры.
    /// Рисует через OnRender; hit-test и drag реализованы вручную.
    /// </summary>
    public class RebarGroupCanvas : FrameworkElement
    {
        RebarGroupEditorVM? _vm;

        // Координатный трансформ: model → screen
        double _scale  = 200;  // px/м
        double _originX = 0;   // модельные X при screen.X = 0
        double _originY = 0;   // модельные Y при screen.Y = 0 (ось Y инвертирована)

        // Drag-состояние
        BarItem?  _dragBar;
        EdgeItem? _dragEdge;
        Point     _dragStartScreen;
        double    _dragEdgeMidX, _dragEdgeMidY;
        bool      _hasDragged;

        // Fill-состояние
        BarItem? _fillBar1;

        // Hover
        EdgeItem? _hoverEdge;

        static readonly Pen   _refPen    = new(Brushes.LightGray, 1.5);
        static readonly Pen   _coverPen  = new(new SolidColorBrush(Color.FromRgb(59, 130, 246)), 1.5)
                                           { DashStyle = DashStyles.Dash };
        static readonly Pen   _barPen    = new(new SolidColorBrush(Color.FromRgb(153, 27, 27)), 1.0);
        static readonly Brush _barFill   = new SolidColorBrush(Color.FromRgb(249, 115, 22));
        static readonly Brush _selFill   = new SolidColorBrush(Color.FromRgb(37, 99, 235));
        static readonly Brush _fill1Fill = new SolidColorBrush(Color.FromRgb(14, 165, 233));
        static readonly Brush _handleNormal  = new SolidColorBrush(Color.FromRgb(100, 149, 237));
        static readonly Brush _handleHover   = Brushes.Orange;

        public RebarGroupCanvas()
        {
            Focusable = true;
            ClipToBounds = true;
        }

        public void SetVM(RebarGroupEditorVM vm)
        {
            _vm = vm;
            vm.PropertyChanged += OnVmPropertyChanged;
            vm.Bars.CollectionChanged  += OnCollectionChanged;
            vm.Edges.CollectionChanged += OnCollectionChanged;
            FitToView();
        }

        void OnVmPropertyChanged(object? s, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(RebarGroupEditorVM.CoverLinePoints)
                               or nameof(RebarGroupEditorVM.ReferencePoints)
                               or nameof(RebarGroupEditorVM.FillMode))
                Dispatcher.Invoke(InvalidateVisual);
        }

        void OnCollectionChanged(object? s, NotifyCollectionChangedEventArgs e)
            => Dispatcher.Invoke(InvalidateVisual);

        // ── Рендеринг ────────────────────────────────────────────────────────

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, w, h));
            if (_vm == null) return;

            // Опорный контур (серый)
            DrawPolyline(dc, _refPen, _vm.ReferencePoints, closed: true);

            // Линия защитного слоя (синяя пунктир)
            DrawPolyline(dc, _coverPen, _vm.CoverLinePoints, closed: true);

            // Ручки рёбер
            foreach (var edge in _vm.Edges)
            {
                var (hx, hy) = edge.HandlePoint;
                var sp = ToScreen(hx, hy);
                DrawDiamond(dc, sp, edge == _hoverEdge ? _handleHover : _handleNormal);
            }

            // Стержни
            foreach (var bar in _vm.Bars)
            {
                var sp = ToScreen(bar.X, bar.Y);
                double r = Math.Max(4, bar.Diameter / 2 * _scale);
                Brush fill = bar.IsSelected ? _selFill :
                             bar == _fillBar1 ? _fill1Fill : _barFill;
                dc.DrawEllipse(fill, _barPen, sp, r, r);
            }
        }

        void DrawPolyline(DrawingContext dc, Pen pen,
            System.Collections.Generic.IReadOnlyList<(double X, double Y)> pts, bool closed)
        {
            if (pts.Count < 2) return;
            var geom = new StreamGeometry();
            using var ctx = geom.Open();
            ctx.BeginFigure(ToScreen(pts[0].X, pts[0].Y), false, closed);
            for (int i = 1; i < pts.Count; i++)
                ctx.LineTo(ToScreen(pts[i].X, pts[i].Y), true, false);
            geom.Freeze();
            dc.DrawGeometry(null, pen, geom);
        }

        static void DrawDiamond(DrawingContext dc, Point center, Brush fill)
        {
            const double s = 5;
            var geom = new StreamGeometry();
            using var ctx = geom.Open();
            ctx.BeginFigure(new Point(center.X, center.Y - s), true, true);
            ctx.LineTo(new Point(center.X + s, center.Y), true, false);
            ctx.LineTo(new Point(center.X, center.Y + s), true, false);
            ctx.LineTo(new Point(center.X - s, center.Y), true, false);
            geom.Freeze();
            dc.DrawGeometry(fill, new Pen(Brushes.Gray, 0.8), geom);
        }

        // ── Координатные трансформы ───────────────────────────────────────────

        Point ToScreen(double mx, double my)
            => new(_scale * (mx - _originX),
                   ActualHeight - _scale * (my - _originY));

        (double X, double Y) ToModel(Point sp)
            => (sp.X / _scale + _originX,
                (ActualHeight - sp.Y) / _scale + _originY);

        public void FitToView()
        {
            if (_vm == null || ActualWidth < 1 || ActualHeight < 1) return;

            double xMin = double.MaxValue, xMax = double.MinValue;
            double yMin = double.MaxValue, yMax = double.MinValue;

            void Expand(double x, double y)
            {
                if (x < xMin) xMin = x; if (x > xMax) xMax = x;
                if (y < yMin) yMin = y; if (y > yMax) yMax = y;
            }

            foreach (var p in _vm.ReferencePoints)   Expand(p.X, p.Y);
            foreach (var p in _vm.CoverLinePoints)    Expand(p.X, p.Y);
            foreach (var b in _vm.Bars)               Expand(b.X, b.Y);

            if (xMin > xMax) { xMin = -0.5; xMax = 0.5; yMin = -0.5; yMax = 0.5; }

            double padX = (xMax - xMin) * 0.15 + 0.01;
            double padY = (yMax - yMin) * 0.15 + 0.01;
            xMin -= padX; xMax += padX;
            yMin -= padY; yMax += padY;

            double sx = ActualWidth  / (xMax - xMin);
            double sy = ActualHeight / (yMax - yMin);
            _scale   = Math.Min(sx, sy);
            _originX = xMin + (ActualWidth  / _scale - (xMax - xMin)) / 2 * -1;
            // Центрировать по X: originX = xMin - (ActualWidth/_scale - (xMax-xMin))/2
            double modelW = ActualWidth  / _scale;
            double modelH = ActualHeight / _scale;
            _originX = xMin - (modelW - (xMax - xMin)) / 2;
            _originY = yMin - (modelH - (yMax - yMin)) / 2;

            InvalidateVisual();
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo info)
        {
            base.OnRenderSizeChanged(info);
            FitToView();
        }
    }
}
```

- [ ] **Step 2: Собрать проект**

```
dotnet build OpenCS.sln -c Debug
```

Ожидаемый результат: прежние ошибки из Task 4 (RebarGroupEditorPage не создана).

- [ ] **Step 3: Коммит**

```
git add OpenCS/Views/RebarGroupCanvas.cs
git commit -m "feat(rebar-editor): RebarGroupCanvas rendering and FitToView"
```

---

## Task 7: RebarGroupCanvas — мышиные взаимодействия

**Files:**
- Modify: `OpenCS/Views/RebarGroupCanvas.cs` — добавить методы после `OnRenderSizeChanged`

- [ ] **Step 1: Добавить константы и методы hit-test**

Вставить перед закрывающей `}` класса `RebarGroupCanvas`:

```csharp
        const double HitBarPx    = 8;   // px, радиус захвата стержня
        const double HitHandlePx = 8;   // px, радиус захвата ручки
        const double SnapPx      = 10;  // px, радиус привязки

        BarItem? HitBar(Point sp)
        {
            if (_vm == null) return null;
            BarItem? best = null;
            double bestD = HitBarPx;
            foreach (var bar in _vm.Bars)
            {
                var bp = ToScreen(bar.X, bar.Y);
                double d = Math.Sqrt((sp.X - bp.X) * (sp.X - bp.X) + (sp.Y - bp.Y) * (sp.Y - bp.Y));
                double r = Math.Max(HitBarPx, bar.Diameter / 2 * _scale);
                if (d <= r && d < bestD) { best = bar; bestD = d; }
            }
            return best;
        }

        EdgeItem? HitHandle(Point sp)
        {
            if (_vm == null) return null;
            foreach (var edge in _vm.Edges)
            {
                var (hx, hy) = edge.HandlePoint;
                var hp = ToScreen(hx, hy);
                double d = Math.Sqrt((sp.X - hp.X) * (sp.X - hp.X) + (sp.Y - hp.Y) * (sp.Y - hp.Y));
                if (d <= HitHandlePx) return edge;
            }
            return null;
        }

        (double X, double Y) TrySnap(double mx, double my)
        {
            if (_vm == null) return (mx, my);
            double threshold = SnapPx / _scale;
            foreach (var cv in _vm.CoverLinePoints)
            {
                double dx = cv.X - mx, dy = cv.Y - my;
                if (Math.Sqrt(dx * dx + dy * dy) < threshold)
                    return (cv.X, cv.Y);
            }
            return (mx, my);
        }

        // ── MouseDown ────────────────────────────────────────────────────────

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            if (_vm == null) return;
            var sp = e.GetPosition(this);
            _hasDragged = false;

            // Fill mode: выбор первого/второго стержня
            if (_vm.FillMode)
            {
                var hit = HitBar(sp);
                if (hit != null)
                {
                    if (_fillBar1 == null)
                    {
                        _fillBar1 = hit;
                        InvalidateVisual();
                    }
                    else if (hit != _fillBar1)
                    {
                        _vm.FillBetweenCommand.Execute((_fillBar1, hit));
                        _fillBar1 = null;
                        InvalidateVisual();
                    }
                }
                else
                {
                    _fillBar1 = null;
                    InvalidateVisual();
                }
                e.Handled = true;
                return;
            }

            // Обычный режим
            var hitHandle = HitHandle(sp);
            if (hitHandle != null)
            {
                _dragEdge = hitHandle;
                _dragStartScreen = sp;
                _dragEdgeMidX = (hitHandle.StartX + hitHandle.EndX) / 2;
                _dragEdgeMidY = (hitHandle.StartY + hitHandle.EndY) / 2;
                CaptureMouse();
                e.Handled = true;
                return;
            }

            var hitBar = HitBar(sp);
            if (hitBar != null)
            {
                _dragBar = hitBar;
                _dragStartScreen = sp;
                _vm.SelectBarCommand.Execute(hitBar);
                CaptureMouse();
                e.Handled = true;
                return;
            }

            // Клик в пустое место — добавить стержень
            var (mx, my) = ToModel(sp);
            (mx, my) = TrySnap(mx, my);
            _vm.AddBarCommand.Execute((mx, my));
            e.Handled = true;
        }

        // ── MouseMove ────────────────────────────────────────────────────────

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_vm == null) return;
            var sp = e.GetPosition(this);

            // Drag стержня
            if (_dragBar != null && e.LeftButton == MouseButtonState.Pressed)
            {
                _hasDragged = true;
                var (mx, my) = ToModel(sp);
                (mx, my) = TrySnap(mx, my);
                _vm.MoveBarCommand.Execute((_dragBar, mx, my));
                InvalidateVisual();
                return;
            }

            // Drag ручки ребра
            if (_dragEdge != null && e.LeftButton == MouseButtonState.Pressed)
            {
                _hasDragged = true;
                var (mx, my) = ToModel(sp);
                // Проекция на нормаль
                double proj = (mx - _dragEdgeMidX) * _dragEdge.NormalX
                            + (my - _dragEdgeMidY) * _dragEdge.NormalY;
                _vm.MoveEdgeHandleCommand.Execute((_dragEdge, proj));
                InvalidateVisual();
                return;
            }

            // Hover над ручкой
            var hov = HitHandle(sp);
            if (hov != _hoverEdge)
            {
                _hoverEdge = hov;
                InvalidateVisual();
            }
        }

        // ── MouseUp ──────────────────────────────────────────────────────────

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            if (IsMouseCaptured) ReleaseMouseCapture();
            _dragBar  = null;
            _dragEdge = null;
        }
```

- [ ] **Step 2: Собрать проект**

```
dotnet build OpenCS.sln -c Debug
```

Ожидаемый результат: прежние ошибки из Task 4.

- [ ] **Step 3: Коммит**

```
git add OpenCS/Views/RebarGroupCanvas.cs
git commit -m "feat(rebar-editor): RebarGroupCanvas mouse interactions, snap, fill mode"
```

---

## Task 8: RebarGroupEditorPage XAML и code-behind

**Files:**
- Create: `OpenCS/Views/RebarGroupEditorPage.xaml`
- Create: `OpenCS/Views/RebarGroupEditorPage.xaml.cs`

- [ ] **Step 1: Создать RebarGroupEditorPage.xaml**

```xml
<UserControl x:Class="OpenCS.Views.RebarGroupEditorPage"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:local="clr-namespace:OpenCS.Views"
             xmlns:vm="clr-namespace:OpenCS.ViewModels">

   <Grid>
      <Grid.ColumnDefinitions>
         <ColumnDefinition Width="180"/>
         <ColumnDefinition/>
         <ColumnDefinition Width="220"/>
      </Grid.ColumnDefinitions>

      <!-- Левая панель -->
      <ScrollViewer Grid.Column="0" VerticalScrollBarVisibility="Auto">
         <StackPanel Margin="6">

            <!-- Стратегия -->
            <GroupBox Header="{DynamicResource RgStrategyGroup}" Margin="0,0,0,6">
               <StackPanel Margin="4">
                  <RadioButton Content="{DynamicResource RgStrategyRegion}"
                               IsChecked="{Binding StrategyFromRegion}"
                               Margin="0,0,0,2"/>
                  <RadioButton Content="{DynamicResource RgStrategyContour}"
                               IsChecked="{Binding StrategyFromContour}"
                               Margin="0,0,0,2"/>
                  <RadioButton Content="{DynamicResource RgStrategyFree}"
                               IsChecked="{Binding StrategyBare}"/>
               </StackPanel>
            </GroupBox>

            <!-- Опора (скрыта при Bare) -->
            <StackPanel Visibility="{Binding HasReference,
                        Converter={StaticResource BoolToVisibility}}">
               <TextBlock Text="{DynamicResource RgReference}" Margin="0,0,0,2"/>
               <!-- Регион -->
               <ComboBox ItemsSource="{Binding AvailableRegions}"
                         SelectedItem="{Binding SelectedRegion}"
                         DisplayMemberPath="Tag"
                         Margin="0,0,0,4"
                         Visibility="{Binding StrategyFromRegion,
                            Converter={StaticResource BoolToVisibility}}"/>
               <!-- Контур -->
               <ComboBox ItemsSource="{Binding AvailableContours}"
                         SelectedItem="{Binding SelectedContour}"
                         DisplayMemberPath="Tag"
                         Margin="0,0,0,4"
                         Visibility="{Binding StrategyFromContour,
                            Converter={StaticResource BoolToVisibility}}"/>
            </StackPanel>

            <!-- Линия защитного слоя (скрыта при Bare) -->
            <StackPanel Visibility="{Binding HasReference,
                        Converter={StaticResource BoolToVisibility}}"
                        Margin="0,0,0,6">
               <DockPanel Margin="0,0,0,2">
                  <TextBlock Text="{DynamicResource RgCoverOffset}" Width="60"/>
                  <TextBox Text="{Binding GlobalOffset, StringFormat='{}{0:F3}'}"
                           Width="60" Margin="0,0,4,0"/>
                  <TextBlock Text="м" VerticalAlignment="Center"/>
               </DockPanel>
               <DockPanel Margin="0,0,0,2">
                  <TextBlock Text="{DynamicResource RgOffsetStep}" Width="60"/>
                  <TextBox Text="{Binding OffsetStep, StringFormat='{}{0:F3}'}"
                           Width="60" Margin="0,0,4,0"/>
                  <TextBlock Text="м" VerticalAlignment="Center"/>
               </DockPanel>
               <Button Content="{DynamicResource RgResetOffsets}"
                       Command="{Binding ResetAllOffsetsCommand}"
                       Padding="4,2"/>
            </StackPanel>

            <Separator Margin="0,0,0,6"/>

            <!-- Диаметр активного стержня -->
            <DockPanel Margin="0,0,0,6">
               <TextBlock Text="{DynamicResource RgBarDiameter}"
                          VerticalAlignment="Center" Margin="0,0,4,0"/>
               <TextBlock Text="{DynamicResource RgBarDiameterUnit}"
                          DockPanel.Dock="Right" VerticalAlignment="Center" Margin="4,0,0,0"/>
               <TextBox Text="{Binding ActiveDiameterMm, StringFormat='{}{0:F0}',
                               UpdateSourceTrigger=PropertyChanged}"/>
            </DockPanel>

            <Separator Margin="0,0,0,6"/>

            <!-- Заполнение -->
            <GroupBox Header="{DynamicResource RgFillMode}" Margin="0,0,0,6">
               <StackPanel Margin="4">
                  <DockPanel Margin="0,0,0,2">
                     <TextBlock Text="{DynamicResource RgFillN}"
                                VerticalAlignment="Center" Margin="0,0,4,0"/>
                     <TextBox Text="{Binding FillCount, UpdateSourceTrigger=PropertyChanged}"
                              Width="40"/>
                  </DockPanel>
                  <RadioButton Content="{DynamicResource RgFillStraight}"
                               IsChecked="{Binding FillUseArc, Converter={StaticResource InverseBool}}"
                               Margin="0,0,0,2"/>
                  <RadioButton Content="{DynamicResource RgFillArc}"
                               IsChecked="{Binding FillUseArc}"
                               Margin="0,0,0,2"/>
                  <DockPanel Visibility="{Binding FillUseArc,
                             Converter={StaticResource BoolToVisibility}}">
                     <TextBlock Text="{DynamicResource RgFillArcR}"
                                VerticalAlignment="Center" Margin="0,0,4,0"/>
                     <TextBox Text="{Binding FillArcRadius, StringFormat='{}{0:F3}',
                                    UpdateSourceTrigger=PropertyChanged}"/>
                  </DockPanel>
                  <ToggleButton Content="{DynamicResource RgFillMode}"
                                IsChecked="{Binding FillMode}"
                                Margin="0,4,0,0" Padding="4,2"/>
               </StackPanel>
            </GroupBox>

            <Separator Margin="0,0,0,6"/>

            <!-- Тег -->
            <TextBlock Text="{DynamicResource RgGroupTag}" Margin="0,0,0,2"/>
            <TextBox Text="{Binding Tag, UpdateSourceTrigger=PropertyChanged}"
                     Margin="0,0,0,8"/>

            <!-- Кнопки -->
            <Button Content="{DynamicResource Save}"
                    Command="{Binding SaveCommand}"
                    Margin="0,0,0,4" Padding="4,3"/>
            <Button Content="{DynamicResource Cancel}"
                    Command="{Binding CancelCommand}"
                    Padding="4,3"/>

         </StackPanel>
      </ScrollViewer>

      <!-- Холст -->
      <local:RebarGroupCanvas x:Name="Canvas" Grid.Column="1" Margin="4"
                              Background="White"/>

      <!-- Правая панель -->
      <Grid Grid.Column="2">
         <Grid.RowDefinitions>
            <RowDefinition Height="*"/>
            <RowDefinition Height="*"/>
         </Grid.RowDefinitions>

         <!-- Таблица рёбер -->
         <GroupBox Grid.Row="0" Header="{DynamicResource RgEdgesTable}" Margin="4,4,4,2">
            <ScrollViewer VerticalScrollBarVisibility="Auto">
               <ItemsControl ItemsSource="{Binding Edges}">
                  <ItemsControl.ItemTemplate>
                     <DataTemplate DataType="{x:Type vm:EdgeItem}">
                        <DockPanel Margin="2">
                           <StackPanel DockPanel.Dock="Right" Orientation="Horizontal">
                              <Button Content="+" Width="20" Height="20" Padding="0"
                                      Command="{Binding DataContext.AdjustEdgeCommand,
                                         RelativeSource={RelativeSource AncestorType=UserControl}}"
                                      CommandParameter="{Binding}"/>
                              <Button Content="−" Width="20" Height="20" Padding="0"
                                      Command="{Binding DataContext.AdjustEdgeCommand,
                                         RelativeSource={RelativeSource AncestorType=UserControl}}"
                                      CommandParameter="{Binding}"/>
                           </StackPanel>
                           <TextBlock Text="{Binding Index}" Width="18"
                                      VerticalAlignment="Center"/>
                           <TextBox Text="{Binding Offset, StringFormat='{}{0:F3}',
                                          UpdateSourceTrigger=LostFocus}"
                                    VerticalAlignment="Center"/>
                        </DockPanel>
                     </DataTemplate>
                  </ItemsControl.ItemTemplate>
               </ItemsControl>
            </ScrollViewer>
         </GroupBox>

         <!-- Таблица стержней -->
         <GroupBox Grid.Row="1" Header="{DynamicResource RgBarsTable}" Margin="4,2,4,4">
            <ScrollViewer VerticalScrollBarVisibility="Auto">
               <ItemsControl ItemsSource="{Binding Bars}">
                  <ItemsControl.ItemTemplate>
                     <DataTemplate DataType="{x:Type vm:BarItem}">
                        <DockPanel Margin="1">
                           <Button DockPanel.Dock="Right" Content="×" Width="18" Height="18"
                                   Padding="0"
                                   Command="{Binding DataContext.DeleteBarCommand,
                                      RelativeSource={RelativeSource AncestorType=UserControl}}"
                                   CommandParameter="{Binding}"/>
                           <TextBlock Text="{Binding Index}" Width="16"
                                      VerticalAlignment="Center"/>
                           <TextBox Text="{Binding X, StringFormat='{}{0:F3}',
                                          UpdateSourceTrigger=LostFocus}"
                                    Width="46" Margin="1,0"/>
                           <TextBox Text="{Binding Y, StringFormat='{}{0:F3}',
                                          UpdateSourceTrigger=LostFocus}"
                                    Width="46" Margin="1,0"/>
                           <TextBox Text="{Binding DiameterMm, StringFormat='{}{0:F0}',
                                          UpdateSourceTrigger=LostFocus}"
                                    Width="32" Margin="1,0"/>
                        </DockPanel>
                     </DataTemplate>
                  </ItemsControl.ItemTemplate>
               </ItemsControl>
            </ScrollViewer>
         </GroupBox>

      </Grid>
   </Grid>
</UserControl>
```

- [ ] **Step 2: Создать RebarGroupEditorPage.xaml.cs**

```csharp
using CScore;
using OpenCS.ViewModels;

using System.Windows.Controls;
using System.Windows.Threading;

namespace OpenCS.Views
{
    /// <summary>Страница редактора группы арматурных стержней.</summary>
    public partial class RebarGroupEditorPage : UserControl
    {
        public RebarGroupEditorPage(MaterialArea? area, AppViewModel app)
        {
            InitializeComponent();
            var vm = new RebarGroupEditorVM(area, app);
            DataContext = vm;
            // Подключить холст после первого рендера (нужны ActualWidth/Height)
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
            {
                Canvas.SetVM(vm);
                // Пересчитать FitToView после загрузки VM
                vm.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName is nameof(RebarGroupEditorVM.CoverLinePoints)
                                       or nameof(RebarGroupEditorVM.ReferencePoints))
                        Dispatcher.BeginInvoke(Canvas.FitToView);
                };
            });
        }
    }
}
```

- [ ] **Step 3: Добавить InverseBoolConverter (если ещё не существует)**

Проверить: `grep -rn "InverseBool\|InverseBoolConverter" OpenCS/`

Если нет — создать `OpenCS/Converters/InverseBoolConverter.cs`:

```csharp
using System;
using System.Globalization;
using System.Windows.Data;

namespace OpenCS.Converters
{
    public class InverseBoolConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
            => value is bool b ? !b : false;
        public object ConvertBack(object value, Type t, object p, CultureInfo c)
            => value is bool b ? !b : false;
    }
}
```

И зарегистрировать в `App.xaml`:

```xml
<conv:InverseBoolConverter x:Key="InverseBool"/>
```

- [ ] **Step 4: Исправить + / − команды в таблице рёбер**

В XAML кнопки + и − передают `CommandParameter="{Binding}"` (EdgeItem), но `AdjustEdgeCommand` ожидает `(EdgeItem, double delta)`. Нужна кнопка с delta в CommandParameter. Заменить кнопки в `ItemTemplate` рёбер:

```xml
<Button Content="+" Width="20" Height="20" Padding="0"
        Command="{Binding DataContext.AdjustEdgeCommand,
           RelativeSource={RelativeSource AncestorType=UserControl}}"
        Tag="{Binding}">
   <Button.CommandParameter>
      <!-- Передаём через Tag + MultiBinding невозможно в стандартном WPF без доп. конвертера.
           Вместо этого используем code-behind через событие Click -->
   </Button.CommandParameter>
</Button>
```

Самый чистый способ без дополнительных конвертеров — обработать Click в code-behind. Изменить `RebarGroupEditorPage.xaml.cs`:

```csharp
// Добавить обработчики в конструктор после Canvas.SetVM(vm):
// (wire через events, т.к. CommandParameter не поддерживает tuple напрямую)
```

Вместо этого упростим: в XAML используем только Delete (×) через Command, а кнопки +/− реализуем как Button с Click-обработчиком в code-behind:

В XAML заменить кнопки + и − на:

```xml
<Button Content="+" Width="20" Height="20" Padding="0"
        Click="EdgePlus_Click" Tag="{Binding}"/>
<Button Content="−" Width="20" Height="20" Padding="0"
        Click="EdgeMinus_Click" Tag="{Binding}"/>
```

В code-behind добавить:

```csharp
        void EdgePlus_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn &&
                btn.Tag is EdgeItem edge &&
                DataContext is RebarGroupEditorVM vm)
                vm.AdjustEdgeCommand.Execute((edge, vm.OffsetStep));
        }

        void EdgeMinus_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn &&
                btn.Tag is EdgeItem edge &&
                DataContext is RebarGroupEditorVM vm)
                vm.AdjustEdgeCommand.Execute((edge, -vm.OffsetStep));
        }
```

- [ ] **Step 5: Собрать проект**

```
dotnet build OpenCS.sln -c Debug
```

Ожидаемый результат: `Ошибок: 0`

- [ ] **Step 6: Коммит**

```
git add OpenCS/Views/RebarGroupEditorPage.xaml OpenCS/Views/RebarGroupEditorPage.xaml.cs
git add OpenCS/Converters/InverseBoolConverter.cs OpenCS/App.xaml
git commit -m "feat(rebar-editor): RebarGroupEditorPage XAML and code-behind"
```

---

## Task 9: Финальная проверка и smoke-test

- [ ] **Step 1: Собрать полный solution без ошибок**

```
dotnet build OpenCS.sln -c Debug
```

Ожидаемый результат: `Ошибок: 0`, предупреждения допустимы.

- [ ] **Step 2: Проверить вручную — новая группа**

1. Запустить `dotnet run --project OpenCS`
2. ПКМ на «Группы арматуры» → «Новая область» → открылась `RebarGroupEditorPage`
3. Если есть Области в проекте — стратегия «По области» активна, опора выбрана, видна линия защ. слоя
4. Кликнуть на холст → появился стержень, добавилась строка в таблице
5. Перетащить стержень → координаты обновились в таблице
6. Кнопки + / − для ребра → линия перестроилась
7. Режим заполнения → клик на два стержня → добавились промежуточные
8. Нажать «Сохранить» → группа появилась в дереве «Группы арматуры»

- [ ] **Step 3: Проверить вручную — редактирование существующей**

1. Кликнуть на существующую группу в дереве → открылась `RebarGroupEditorPage` с уже заданными стержнями
2. Изменить координату в таблице → холст обновился

- [ ] **Step 4: Финальный коммит**

```
git add -A
git commit -m "feat(rebar-editor): complete RebarGroup editor — interactive canvas, cover line, fill-between"
```
