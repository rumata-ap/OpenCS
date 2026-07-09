using CScore;
using OpenCS.Utilites;
using OpenCS.Views.Dialogs;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;

namespace OpenCS.ViewModels
{
    public enum RebarPlacementStrategy { FromRegion, FromContour, Bare, FromCircleSet }

    /// <summary>ViewModel страницы задания группы арматурных стержней.</summary>
    public class RebarGroupEditorVM : ViewModelBase
    {
        public AppViewModel App { get; }
        public MaterialArea? EditedArea { get; }

        RebarPlacementStrategy _strategy = RebarPlacementStrategy.Bare;
        MaterialArea? _selectedRegion;
        Contour? _selectedContour;
        Material? _selectedMaterial;
        string? _selectedGeometrySet;
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
            TranslateCommand      = new RelayCommand(_ => Translate());
            ShowPropertiesCommand = new RelayCommand(_ => ShowProperties());
            ImportFromCircleSetCommand = new RelayCommand(_ => ImportFromCircleSet());

            // Определить начальную стратегию
            if (app.AreasLive.Any())     _strategy = RebarPlacementStrategy.FromRegion;
            else if (app.Contours.Any()) _strategy = RebarPlacementStrategy.FromContour;

            // Загрузить данные существующей области
            if (area != null)
            {
                _tag = area.Tag;
                _selectedMaterial = area.Material;
                foreach (var f in area.Fibers.Where(f => f.TypeFiber == FiberType.point))
                    Bars.Add(new BarItem { X = f.X, Y = f.Y, Diameter = f.Diameter, Index = Bars.Count + 1 });
            }

            InitStrategyReference();

            app.MaterialAreaSigSpChanged += OnSigSpChangedExternally;
        }

        void OnSigSpChangedExternally(object? sender, int areaId)
        {
            if (EditedArea?.Id == areaId)
            {
                OnPropertyChanged(nameof(SigSp));
                OnPropertyChanged(nameof(EpsSp));
            }
        }

        // ── Стратегия ────────────────────────────────────────────────────────

        public RebarPlacementStrategy Strategy
        {
            get => _strategy;
            set { _strategy = value; OnPropertyChanged(); InitStrategyReference(); }
        }

        public bool StrategyFromRegion    { get => _strategy == RebarPlacementStrategy.FromRegion;    set { if (value) Strategy = RebarPlacementStrategy.FromRegion; } }
        public bool StrategyFromContour   { get => _strategy == RebarPlacementStrategy.FromContour;   set { if (value) Strategy = RebarPlacementStrategy.FromContour; } }
        public bool StrategyBare          { get => _strategy == RebarPlacementStrategy.Bare;          set { if (value) Strategy = RebarPlacementStrategy.Bare; } }
        public bool StrategyFromCircleSet { get => _strategy == RebarPlacementStrategy.FromCircleSet; set { if (value) Strategy = RebarPlacementStrategy.FromCircleSet; } }

        /// <summary>Линия защитного слоя и таблица рёбер актуальны только при опоре на область/контур.</summary>
        public bool HasReference => _strategy is RebarPlacementStrategy.FromRegion or RebarPlacementStrategy.FromContour;

        public IReadOnlyList<MaterialArea> AvailableRegions  => App.AreasLive;
        public IReadOnlyList<Contour>      AvailableContours => App.Contours;
        public IReadOnlyList<Material>     AvailableMaterials => App.Materials;

        /// <summary>
        /// Значения GeometrySet окружностей проекта, доступные для импорта в стержни.
        /// Служебные теги вида "RebarGroup#{id}" (проставляются при сохранении любой
        /// группы арматуры, чтобы её стержни были видны в узле Геометрия/Окружности)
        /// исключаются — иначе можно было бы «импортировать» группу саму в себя.
        /// </summary>
        public IReadOnlyList<string> AvailableGeometrySets =>
            App.Circles
               .Select(c => c.GeometrySet)
               .Where(g => !string.IsNullOrWhiteSpace(g) && !g!.StartsWith("RebarGroup#"))
               .Distinct()
               .OrderBy(g => g)
               .ToList()!;

        public string? SelectedGeometrySet
        {
            get => _selectedGeometrySet;
            set { _selectedGeometrySet = value; OnPropertyChanged(); }
        }

        public Material? SelectedMaterial
        {
            get => _selectedMaterial;
            set { _selectedMaterial = value; OnPropertyChanged(); OnPropertyChanged(nameof(EpsSp)); }
        }

        /// <summary>Предварительное напряжение после потерь [МПа].</summary>
        public double SigSp
        {
            get => EditedArea?.SigSp ?? 0.0;
            set
            {
                if (EditedArea != null) { EditedArea.SigSp = value; EditedArea.PropagateEps_p(); }
                OnPropertyChanged();
                OnPropertyChanged(nameof(EpsSp));
            }
        }

        /// <summary>Индекс γ_sp для ComboBox: 0→1.0, 1→0.9, 2→1.1.</summary>
        public int GammaSpIndex
        {
            get => EditedArea == null ? 0 : EditedArea.GammaSp switch { 0.9 => 1, 1.1 => 2, _ => 0 };
            set
            {
                if (EditedArea != null)
                {
                    EditedArea.GammaSp = value switch { 1 => 0.9, 2 => 1.1, _ => 1.0 };
                    EditedArea.PropagateEps_p();
                }
                OnPropertyChanged();
                OnPropertyChanged(nameof(EpsSp));
            }
        }

        /// <summary>Вычисленная ε_sp в промиллях (только чтение).</summary>
        public string EpsSp
        {
            get
            {
                double e = _selectedMaterial?.E ?? 0;
                if (e < 1) return "—";
                return $"{(EditedArea?.SigSp ?? 0) * 1000.0 * (EditedArea?.GammaSp ?? 1) / e:F6}";
            }
        }

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

        // ── Трансформация ────────────────────────────────────────────────────

        void Translate()
        {
            if (Bars.Count == 0) return;

            var dlg = new DoubleInputDialog(
                "Сдвиг стержней",
                "Смещение по X (м):",
                "Смещение по Y (м):");
            if (dlg.ShowDialog() != true) return;

            double dx = dlg.Value1, dy = dlg.Value2;
            if (dx == 0 && dy == 0) return;

            foreach (var bar in Bars)
            {
                bar.X += dx;
                bar.Y += dy;
            }

            if (_referencePoints.Count > 0)
            {
                var moved = _referencePoints.Select(p => (p.X + dx, p.Y + dy)).ToList();
                BuildEdgesFromContour(moved);
            }
        }

        void ShowProperties()
        {
            if (Bars.Count == 0) return;
            var dlg = new RebarGroupPropsWindow([.. Bars], _tag);
            dlg.ShowDialog();
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
        public ICommand TranslateCommand       { get; }
        public ICommand ShowPropertiesCommand  { get; }
        public ICommand ImportFromCircleSetCommand { get; }

        // ── Инициализация ────────────────────────────────────────────────────

        void InitStrategyReference()
        {
            OnPropertyChanged(nameof(StrategyFromRegion));
            OnPropertyChanged(nameof(StrategyFromContour));
            OnPropertyChanged(nameof(StrategyBare));
            OnPropertyChanged(nameof(StrategyFromCircleSet));
            OnPropertyChanged(nameof(HasReference));

            if (_strategy == RebarPlacementStrategy.FromRegion && AvailableRegions.Any())
                SelectedRegion = AvailableRegions[0];
            else if (_strategy == RebarPlacementStrategy.FromContour && AvailableContours.Any())
                SelectedContour = AvailableContours[0];
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
                var edge = new EdgeItem
                {
                    Index   = Edges.Count + 1,
                    Offset  = _globalOffset,
                    StartX  = sx, StartY = sy,
                    EndX    = ex, EndY   = ey,
                    NormalX = nx, NormalY = ny
                };
                edge.PropertyChanged += OnEdgeOffsetChanged;
                Edges.Add(edge);
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

        /// <summary>
        /// Единая точка пересчёта линии защитного слоя при изменении отступа любого
        /// ребра — не важно, каким путём (перетаскивание ручки, кнопки +/− или прямой
        /// ввод значения в таблице рёбер, у которой TextBox привязан к EdgeItem.Offset
        /// напрямую, в обход команд VM).
        /// </summary>
        void OnEdgeOffsetChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(EdgeItem.Offset))
                RecomputeCoverLine();
        }

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

        // ── Методы стержней ───────────────────────────────────────────────────

        void AddBar(double x, double y)
        {
            var bar = new BarItem { X = x, Y = y, Diameter = _activeDiameter, Index = Bars.Count + 1 };
            Bars.Add(bar);
            RenumberBars();
        }

        /// <summary>
        /// Копирует окружности проекта с выбранным GeometrySet в список стержней как
        /// обычные BarItem (разовое копирование, без последующей связи с исходными
        /// окружностями). Диаметр берётся из диаметра каждой окружности, а не из
        /// ActiveDiameter — набор может содержать окружности разных размеров.
        /// </summary>
        void ImportFromCircleSet()
        {
            if (string.IsNullOrEmpty(_selectedGeometrySet)) return;

            foreach (var c in App.Circles.Where(c => c.GeometrySet == _selectedGeometrySet)
                                          .OrderBy(c => c.Num))
                Bars.Add(new BarItem { X = c.X, Y = c.Y, Diameter = c.Diameter, Index = Bars.Count + 1 });

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
        }

        void SetEdgeOffset(EdgeItem edge, double newOffset)
        {
            double step = _offsetStep > 0 ? _offsetStep : 0.001;
            edge.Offset = Math.Max(0, Math.Round(newOffset / step) * step);
        }

        void ResetAllOffsets()
        {
            foreach (var e in Edges)
                e.Offset = _globalOffset;
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

            double chordDx = b2.X - b1.X, chordDy = b2.Y - b1.Y;
            double len = Math.Sqrt(chordDx * chordDx + chordDy * chordDy);
            double perpX = -chordDy / len, perpY = chordDx / len;

            (double cx, double cy) = ChooseArcCenter(midX, midY, perpX, perpY, h);

            double angle1 = Math.Atan2(b1.Y - cy, b1.X - cx);
            double angle2 = Math.Atan2(b2.Y - cy, b2.X - cx);
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
            area.Material   = _selectedMaterial;
            area.MaterialId = _selectedMaterial?.Id ?? 0;
            area.PropagateEps_p();
            area.Fibers.Clear();
            foreach (var b in Bars)
                area.Fibers.Add(Fiber.CreatePoint(b.Diameter, b.X, b.Y));
            App.db.SaveMaterialArea(area);
            if (!App.MaterialAreas.Contains(area))
                App.MaterialAreas.Add(area);
            else
            {
                App.RefreshMaterialAreaLiveCollections();
            }
            SyncBarCircles(area);
            App.LogService.Info($"Группа арматуры «{area.Tag}» сохранена");
        }

        /// <summary>
        /// Отражает стержни группы в коллекции App.Circles (узел Геометрия/Окружности),
        /// как это делают импорты из AutoCAD/DXF. GeometrySet хранит ключ вида
        /// "RebarGroup#{id}" — по нему при повторном сохранении удаляются старые
        /// окружности этой группы перед вставкой актуального набора.
        /// </summary>
        void SyncBarCircles(MaterialArea area)
        {
            string geometrySet = $"RebarGroup#{area.Id}";
            foreach (var stale in App.Circles.Where(c => c.GeometrySet == geometrySet).ToList())
            {
                App.Circles.Remove(stale);
                App.db.DeleteCircle(stale);
            }

            int nextNum = App.Circles.Count > 0 ? App.Circles.Max(c => c.Num) + 1 : 1;
            foreach (var b in Bars)
            {
                var cp = new CircleP(b.X, b.Y, b.Diameter / 2)
                {
                    Tag         = _tag,
                    GeometrySet = geometrySet,
                    Num         = nextNum++
                };
                App.db.SaveCircle(cp);
                App.Circles.Add(cp);
            }
            App.CirclesRenumber();
        }
    }
}
