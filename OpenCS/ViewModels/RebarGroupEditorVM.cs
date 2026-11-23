using CScore;
using OpenCS.Services;
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

    /// <summary>
    /// ViewModel страницы группы арматурных стержней. Обозначение, материал, стержни, σsp, γsp и носитель — черновик
    /// страницы; в область пула и базу — только по «Сохранить» (через <see cref="CrossSectionAuthoring"/>), «*» и вопрос
    /// при уходе — по сравнению черновика с записанным.
    /// </summary>
    public class RebarGroupEditorVM : ViewModelBase, IUnsavedChanges
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
        string _tag = "";
        double _sigSp;
        double _gammaSp = 1.0;
        string _savedKey = "";
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
            TranslateCommand      = new RelayCommand(_ => Translate());
            ShowPropertiesCommand = new RelayCommand(_ => ShowProperties());
            ImportFromCircleSetCommand = new RelayCommand(_ => ImportFromCircleSet());

            Bars.CollectionChanged += (_, e) =>
            {
                if (e.NewItems != null)
                    foreach (BarItem b in e.NewItems) b.PropertyChanged += OnBarChanged;
                Touch();
            };

            // Загрузить данные существующей области
            MaterialArea? host = null;
            if (area != null)
            {
                _tag = area.Tag;
                _selectedMaterial = area.Material;
                _sigSp = area.SigSp;
                _gammaSp = area.GammaSp;
                host = area.HostArea ?? (area.HostAreaId is int hid ? app.MaterialAreas.FirstOrDefault(a => a.Id == hid) : null);
                foreach (var f in area.Fibers.Where(f => f.TypeFiber == FiberType.point))
                    Bars.Add(new BarItem { X = f.X, Y = f.Y, Diameter = f.Diameter, Index = Bars.Count + 1 });
            }

            // Начальная стратегия: у группы с носителем — «от области» с её носителем; у записанной группы без
            // носителя — без опоры (иначе «Сохранить» назначила бы носителем первую область пула).
            if (host != null) _strategy = RebarPlacementStrategy.FromRegion;
            else if (area is { Id: > 0 }) _strategy = RebarPlacementStrategy.Bare;
            else if (app.AreasLive.Any()) _strategy = RebarPlacementStrategy.FromRegion;
            else if (app.Contours.Any()) _strategy = RebarPlacementStrategy.FromContour;

            if (host != null)
            {
                OnPropertyChanged(nameof(HasReference));
                SelectedRegion = host;
            }
            else
                InitStrategyReference();
            _savedKey = DraftKey();

            app.MaterialAreaSigSpChanged += OnSigSpChangedExternally;
        }

        /// <summary>Окно потерь преднапряжения записало σsp в область — подхватить в черновик.</summary>
        void OnSigSpChangedExternally(object? sender, int areaId)
        {
            if (EditedArea?.Id == areaId)
            {
                bool wasClean = !IsDirty;
                _sigSp = EditedArea.SigSp;
                if (wasClean) _savedKey = DraftKey();
                OnPropertyChanged(nameof(SigSp));
                OnPropertyChanged(nameof(EpsSp));
                Touch();
            }
        }

        void OnBarChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(BarItem.X) or nameof(BarItem.Y) or nameof(BarItem.Diameter)) Touch();
        }

        // ── «*» и вопрос при уходе ───────────────────────────────────────────

        /// <summary>Носитель, который получит группа при сохранении: выбранная область при опоре «от области».</summary>
        MaterialArea? DraftHost => _strategy == RebarPlacementStrategy.FromRegion ? _selectedRegion : null;

        string DraftKey() =>
            $"{_tag}|{_selectedMaterial?.Id}|{DraftHost?.Id}:{(DraftHost == null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(DraftHost))}|{_sigSp:R}|{_gammaSp:R}|" +
            string.Join(";", Bars.Select(b => $"{b.X:R},{b.Y:R},{b.Diameter:R}"));

        /// <summary>Есть правки, не записанные в проект.</summary>
        public bool IsDirty => DraftKey() != _savedKey;

        /// <summary>Обозначение для вопроса о сохранении.</summary>
        public string DisplayName => string.IsNullOrWhiteSpace(_tag) ? Loc.S("NewRebarGroup") : _tag;

        void Touch() => OnPropertyChanged(nameof(IsDirty));

        bool IUnsavedChanges.SaveChanges() => Save();

        /// <summary>Черновик к записанному состоянию области.</summary>
        void IUnsavedChanges.DiscardChanges()
        {
            var area = EditedArea;
            _tag = area?.Tag ?? "";
            _selectedMaterial = area?.Material;
            _sigSp = area?.SigSp ?? 0;
            _gammaSp = area?.GammaSp ?? 1;
            Bars.Clear();
            if (area != null)
                foreach (var f in area.Fibers.Where(f => f.TypeFiber == FiberType.point))
                    Bars.Add(new BarItem { X = f.X, Y = f.Y, Diameter = f.Diameter, Index = Bars.Count + 1 });
            var host = area?.HostArea;
            if (host != null) { _strategy = RebarPlacementStrategy.FromRegion; SelectedRegion = host; }
            _savedKey = DraftKey();
            OnPropertyChanged(string.Empty);
        }

        // ── Стратегия ────────────────────────────────────────────────────────

        public RebarPlacementStrategy Strategy
        {
            get => _strategy;
            set { _strategy = value; OnPropertyChanged(); InitStrategyReference(); Touch(); }
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
            set { _selectedMaterial = value; OnPropertyChanged(); OnPropertyChanged(nameof(EpsSp)); Touch(); }
        }

        /// <summary>Предварительное напряжение после потерь [МПа].</summary>
        public double SigSp
        {
            get => _sigSp;
            set
            {
                _sigSp = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(EpsSp));
                Touch();
            }
        }

        /// <summary>Индекс γ_sp для ComboBox: 0→1.0, 1→0.9, 2→1.1.</summary>
        public int GammaSpIndex
        {
            get => _gammaSp switch { 0.9 => 1, 1.1 => 2, _ => 0 };
            set
            {
                _gammaSp = value switch { 1 => 0.9, 2 => 1.1, _ => 1.0 };
                OnPropertyChanged();
                OnPropertyChanged(nameof(EpsSp));
                Touch();
            }
        }

        /// <summary>Вычисленная ε_sp в промиллях (только чтение).</summary>
        public string EpsSp
        {
            get
            {
                double e = _selectedMaterial?.E ?? 0;
                if (e < 1) return "—";
                return $"{_sigSp * 1000.0 * _gammaSp / e:F6}";
            }
        }

        public MaterialArea? SelectedRegion
        {
            get => _selectedRegion;
            set { _selectedRegion = value; OnPropertyChanged(); if (value != null) BuildEdgesFromContour(GetHullPoints(value.Hull)); Touch(); }
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
            set
            {
                _globalOffset = value;
                OnPropertyChanged(nameof(GlobalOffset));
                OnPropertyChanged(nameof(GlobalOffsetMm));
            }
        }

        /// <summary>Защитный слой в миллиметрах для отображения в UI.</summary>
        public double GlobalOffsetMm
        {
            get => _globalOffset * 1000;
            set => GlobalOffset = value / 1000;
        }

        public double OffsetStep
        {
            get => _offsetStep;
            set
            {
                _offsetStep = value;
                OnPropertyChanged(nameof(OffsetStep));
                OnPropertyChanged(nameof(OffsetStepMm));
            }
        }

        /// <summary>Шаг изменения отступа в миллиметрах для отображения в UI.</summary>
        public double OffsetStepMm
        {
            get => _offsetStep * 1000;
            set => OffsetStep = value / 1000;
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
            set
            {
                _fillArcRadius = value;
                OnPropertyChanged(nameof(FillArcRadius));
                OnPropertyChanged(nameof(FillArcRadiusMm));
            }
        }

        /// <summary>Радиус дуги в миллиметрах для отображения в UI.</summary>
        public double FillArcRadiusMm
        {
            get => _fillArcRadius * 1000;
            set => FillArcRadius = value / 1000;
        }

        // ── Трансформация ────────────────────────────────────────────────────

        void Translate()
        {
            if (Bars.Count == 0) return;

            var dlg = new DoubleInputDialog(
                Loc.S("RgTranslateTitle"),
                Loc.S("AreaTranslateDxMm"),
                Loc.S("AreaTranslateDyMm"));
            if (dlg.ShowDialog() != true) return;

            double dx = dlg.Value1 / 1000, dy = dlg.Value2 / 1000;
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
            set { _tag = value; OnPropertyChanged(); Touch(); }
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
        /// ввод значения в таблице рёбер, у которой TextBox привязан к EdgeItem.OffsetMm
        /// напрямую, в обход команд VM).
        /// </summary>
        void OnEdgeOffsetChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(EdgeItem.Offset))
                RecomputeCoverLine();
        }

        public void RecomputeCoverLine()
        {
            var edges = Edges.Select(edge => new OffsetEdge(
                edge.StartX, edge.StartY, edge.EndX, edge.EndY,
                edge.NormalX, edge.NormalY, edge.Offset)).ToList();
            CoverLinePoints = ContourOffset.Offset(edges);
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

        /// <summary>
        /// Переносит черновик в область и записывает её через службу (проверка, носитель, диаграммы, круги стержней).
        /// Проверка не прошла — ошибки в журнал, область возвращается к записанному состоянию, черновик остаётся.
        /// </summary>
        bool Save()
        {
            var area = EditedArea ?? new MaterialArea();
            var before = AreaSnapshot.Capture(area);
            bool isNew = area.Id == 0;
            area.Tag      = _tag;
            area.Category = AreaCategory.RebarGroup;
            area.HostArea = DraftHost;
            area.HostAreaId = DraftHost?.Id;
            area.Material   = _selectedMaterial;
            area.MaterialId = _selectedMaterial?.Id ?? 0;
            area.SigSp = _sigSp;
            area.GammaSp = _gammaSp;
            var mesh = area.Fibers.Where(f => f.TypeFiber != FiberType.point).ToList();
            area.Fibers = [.. mesh, .. Bars.Select(b => Fiber.CreatePoint(b.Diameter, b.X, b.Y))];
            var errors = CrossSectionAuthoring.SaveArea(App.db, area, App.CalcSettings);
            if (errors.Count > 0)
            {
                before.RestoreTo(area, App.db.Diagrams, App.CalcSettings);
                foreach (var e in errors)
                    App.LogService.Error(string.Format(Loc.S("AreaSaveErrorLog"), DisplayName, e.Message));
                return false;
            }
            App.CirclesRenumber();
            App.RefreshMaterialAreaLiveCollections();
            App.MarkDirty(SaveCategory.CrossSections);
            _savedKey = DraftKey();
            Touch();
            App.LogService.Info(string.Format(Loc.S(isNew ? "RgSavedNewLog" : "RgSavedLog"), area.Tag));
            return true;
        }
    }
}
