using CScore;
using CScore.Fem;
using CScore.Planar;
using CScore.PlateRebar;
using OpenCS.Utilites;
using OpenCS.Views;
using OpenCS.Views.Helpers;
using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Media;

namespace OpenCS.ViewModels;

/// <summary>ViewModel редактора плоского конструктивного элемента (плита/стена) поверх
/// PlanarRegion с заранее построенным Frame3D (см. FemSchemaEditorVM.BuildPlateFrame и аналоги).
/// Контур берётся только из пула App.Contours — по образцу MaterialAreaVM.</summary>
public class PlanarRegionMemberVM : ViewModelBase
{
    readonly AppViewModel _app;
    readonly FemSchema _schema;
    readonly Frame3D _frame;
    readonly FemMember? _existingMember;
    readonly PlanarRegion? _existingRegion;

    public PlanarRegionMemberVM(AppViewModel app, FemSchema schema, Frame3D frame,
        FemMember? existingMember = null, PlanarRegion? existingRegion = null)
    {
        _app = app;
        _schema = schema;
        _frame = frame;
        _existingMember = existingMember;
        _existingRegion = existingRegion;

        Holes = [];
        RebarZones = new ObservableCollection<RebarZoneVM>(
            (existingRegion?.RebarZones ?? []).Select(z => new RebarZoneVM(z, OnZoneChanged, MarkRebarLayoutDirty, app.Armatures)));

        if (existingRegion != null)
        {
            Hull = existingRegion.Hull;
            foreach (var h in existingRegion.Holes) Holes.Add(h);
        }

        _plateSectionOwned = RebarZones.Count > 0;
        _rebarSectionGridStep = existingRegion?.RebarSectionGridStep ?? 0.3;

        string autoKind = PlanarKindClassifier.Classify(frame, out bool ambiguous);
        KindIsAmbiguous = ambiguous;
        _kind = existingMember?.Kind ?? autoKind;
        KindSource = existingMember?.KindSource ?? "auto";
        PlateSection = existingMember?.PlateSectionId is int psid
            ? app.PlateSections.FirstOrDefault(p => p.Id == psid)
            : null;
        _tag = existingMember?.ElemTag ?? NextDefaultTag();

        SetHullFromPoolCommand = new RelayCommand(o => SetHullFromPool(o as Contour));
        AddHoleCommand = new RelayCommand(o => AddHole(o as Contour));
        RemoveHoleCommand = new RelayCommand(o => RemoveHole(o as Contour));
        AddRebarZoneCommand = new RelayCommand(_ => AddRebarZone());
        DeleteRebarZoneCommand = new RelayCommand(_ => DeleteRebarZone(), _ => SelectedRebarZone != null);
        SetZonePolygonFromPoolCommand = new RelayCommand(o => SetZonePolygonFromPool(o as Contour));
        BatchAddZonesFromSetCommand = new RelayCommand(o => BatchAddZonesFromSet(o as string));
        ComputeSectionCountCommand = new RelayCommand(_ => ComputeSectionCount(), _ => Hull != null && PlateSection != null);
        AddSectionRebarLayerCommand = new RelayCommand(_ => AddSectionRebarLayer(), _ => PlateSection != null);
        DeleteSectionRebarLayerCommand = new RelayCommand(_ => DeleteSectionRebarLayer(), _ => SelectedSectionRebarLayer != null);
        SaveCommand = new RelayCommand(_ => Save());
        DeleteCommand = new RelayCommand(_ => Delete(), _ => _existingMember != null);

        RefreshPlot();
        RefreshFaceFilteredCollections();
    }

    string NextDefaultTag()
    {
        string prefix = _kind == "wall" ? "Стена" : "Плита";
        int n = _app.db.GetFemMembers(_schema.Id).Count(m => m.PlanarRegionId != null) + 1;
        return $"{prefix} {n}";
    }

    string _tag;
    public string Tag { get => _tag; set { _tag = value; OnPropertyChanged(); } }

    Contour? _hull;
    public Contour? Hull { get => _hull; set { _hull = value; OnPropertyChanged(); RefreshPlot(); } }

    public ObservableCollection<Contour> Holes { get; }

    public ObservableCollection<RebarZoneVM> RebarZones { get; }

    public bool HasZones => RebarZones.Count > 0;

    double _rebarSectionGridStep;
    public double RebarSectionGridStep
    {
        get => _rebarSectionGridStep;
        set { _rebarSectionGridStep = value; OnPropertyChanged(); RebarLayoutDirty = true; }
    }

    bool _rebarLayoutDirty;
    /// <summary>Есть ли несохранённые правки, влияющие на резолв раскладки армирования (зоны,
    /// базовая раскладка, шаг сетки). НЕ включает косметические правки (Name зоны).</summary>
    public bool RebarLayoutDirty { get => _rebarLayoutDirty; private set { _rebarLayoutDirty = value; OnPropertyChanged(); } }

    void MarkRebarLayoutDirty() { RebarLayoutDirty = true; RefreshRebarPlotElements(); }

    bool _showZoneRebarGlyphs = true;
    /// <summary>Показывать штриховку/подпись слоя армирования у зон доп. армирования.
    /// Состояние вида на сессию, не персистится.</summary>
    public bool ShowZoneRebarGlyphs
    {
        get => _showZoneRebarGlyphs;
        set { _showZoneRebarGlyphs = value; OnPropertyChanged(); RefreshRebarPlotElements(); }
    }

    bool _showBackgroundRebarGlyphs = true;
    /// <summary>Показывать штриховку/подпись фонового (базового) слоя армирования.
    /// Состояние вида на сессию, не персистится.</summary>
    public bool ShowBackgroundRebarGlyphs
    {
        get => _showBackgroundRebarGlyphs;
        set { _showBackgroundRebarGlyphs = value; OnPropertyChanged(); RefreshRebarPlotElements(); }
    }

    RebarFace _activeRebarFace = RebarFace.MinusN;
    public RebarFace ActiveRebarFace
    {
        get => _activeRebarFace;
        set { _activeRebarFace = value; OnPropertyChanged(); RefreshFaceFilteredCollections(); RefreshPlot(); }
    }

    public ObservableCollection<PlateRebarLayerVM> ActiveFaceSectionRebarLayers { get; } = [];
    public ObservableCollection<RebarZoneVM> ActiveFaceRebarZones { get; } = [];

    RebarZoneVM? _selectedRebarZone;
    public RebarZoneVM? SelectedRebarZone
    {
        get => _selectedRebarZone;
        set { _selectedRebarZone = value; OnPropertyChanged(); RefreshPlot(); }
    }

    string _kind;
    public string Kind
    {
        get => _kind;
        set { _kind = value; KindSource = "manual"; OnPropertyChanged(); }
    }

    string _kindSource = "auto";
    public string KindSource { get => _kindSource; private set { _kindSource = value; OnPropertyChanged(); } }

    public bool KindIsAmbiguous { get; }

    PlateSection? _plateSection;
    public PlateSection? PlateSection
    {
        get => _plateSection;
        set
        {
            _plateSection = value;
            OnPropertyChanged();
            GeneratedSectionVM = value != null ? new PlateSectionVM(value, _app) : null;
            RefreshSectionRebarLayers();
        }
    }

    PlateSectionVM? _generatedSectionVM;
    public PlateSectionVM? GeneratedSectionVM { get => _generatedSectionVM; private set { _generatedSectionVM = value; OnPropertyChanged(); } }

    public ObservableCollection<PlateRebarLayerVM> SelectedSectionRebarLayers { get; } = [];

    PlateRebarLayerVM? _selectedSectionRebarLayer;
    public PlateRebarLayerVM? SelectedSectionRebarLayer
    {
        get => _selectedSectionRebarLayer;
        set { _selectedSectionRebarLayer = value; OnPropertyChanged(); }
    }

    public InputModeOption[] InputModeOptions { get; } =
    [
        new InputModeOption("diameter_spacing", Loc.S("PlateLayerModeSpacing")),
        new InputModeOption("diameter_count",   Loc.S("PlateLayerModeCount")),
        new InputModeOption("direct",           Loc.S("PlateLayerModeDirect")),
    ];

    IReadOnlyList<FemValidationDiagnostic> _diagnostics = [];
    public IReadOnlyList<FemValidationDiagnostic> Diagnostics
    {
        get => _diagnostics;
        private set { _diagnostics = value; OnPropertyChanged(); }
    }

    public ObservableCollection<Contour> ProjectContours => _app.Contours;
    public ObservableCollection<PlateSection> ProjectPlateSections => _app.PlateSections;
    public ObservableCollection<Material> ProjectArmatures => _app.Armatures;

    IReadOnlyList<PlotElement> _geometryPlotElements = [];
    public IReadOnlyList<PlotElement> GeometryPlotElements { get => _geometryPlotElements; private set { _geometryPlotElements = value; OnPropertyChanged(); } }

    IReadOnlyList<PlotElement> _rebarPlotElements = [];
    public IReadOnlyList<PlotElement> RebarPlotElements { get => _rebarPlotElements; private set { _rebarPlotElements = value; OnPropertyChanged(); } }

    double _geoArea, _geoCentroidX, _geoCentroidY, _geoIx, _geoIy, _geoIxy;
    public double GeoArea { get => _geoArea; private set { _geoArea = value; OnPropertyChanged(); } }
    public double GeoCentroidX { get => _geoCentroidX; private set { _geoCentroidX = value; OnPropertyChanged(); } }
    public double GeoCentroidY { get => _geoCentroidY; private set { _geoCentroidY = value; OnPropertyChanged(); } }
    public double GeoIx { get => _geoIx; private set { _geoIx = value; OnPropertyChanged(); } }
    public double GeoIy { get => _geoIy; private set { _geoIy = value; OnPropertyChanged(); } }
    public double GeoIxy { get => _geoIxy; private set { _geoIxy = value; OnPropertyChanged(); } }

    public ICommand SetHullFromPoolCommand { get; }
    public ICommand AddHoleCommand { get; }
    public ICommand RemoveHoleCommand { get; }
    public ICommand AddRebarZoneCommand { get; }
    public ICommand DeleteRebarZoneCommand { get; }
    public ICommand SetZonePolygonFromPoolCommand { get; }
    public ICommand BatchAddZonesFromSetCommand { get; }
    public ICommand ComputeSectionCountCommand { get; }
    public ICommand AddSectionRebarLayerCommand { get; }
    public ICommand DeleteSectionRebarLayerCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand DeleteCommand { get; }

    public event Action<FemMember>? SaveCompleted;
    public event Action<FemMember>? DeleteCompleted;

    void SetHullFromPool(Contour? contour)
    {
        if (contour == null) return;
        Hull = contour;
    }

    void AddHole(Contour? contour)
    {
        if (contour == null || Holes.Contains(contour)) return;
        Holes.Add(contour);
        RefreshPlot();
    }

    void RemoveHole(Contour? contour)
    {
        if (contour == null) return;
        Holes.Remove(contour);
        RefreshPlot();
    }

    bool _plateSectionOwned;

    /// <summary>Клонирует текущее PlateSection в новую (Id=0) запись при первом появлении зоны —
    /// по аналогии с EnsureGeometryOwned(): пока зон нет, PlateSection может быть общим, разделяемым
    /// с другими элементами сечением; как только регион получает зону, ему нужна собственная копия,
    /// которую можно свободно редактировать не задевая чужие элементы.</summary>
    void EnsurePlateSectionOwned()
    {
        if (_plateSectionOwned) return;
        PlateSection = PlateSection != null ? CloneAsNewSection(PlateSection) : new PlateSection();
        _plateSectionOwned = true;
    }

    static PlateSection CloneAsNewSection(PlateSection source)
    {
        var clone = source.CloneForCalc();
        clone.Id = 0;
        return clone;
    }

    void AddRebarZone()
    {
        RebarLayoutDirty = true;
        bool wasEmpty = RebarZones.Count == 0;
        var zone = new RebarZone { Name = $"Зона {RebarZones.Count + 1}", Face = ActiveRebarFace };
        var vm = new RebarZoneVM(zone, OnZoneChanged, MarkRebarLayoutDirty, _app.Armatures);
        RebarZones.Add(vm);
        if (wasEmpty) EnsurePlateSectionOwned();
        RefreshFaceFilteredCollections();
        SelectedRebarZone = vm;
        OnPropertyChanged(nameof(HasZones));
    }

    void DeleteRebarZone()
    {
        if (SelectedRebarZone == null) return;
        RebarLayoutDirty = true;
        RebarZones.Remove(SelectedRebarZone);
        if (RebarZones.Count == 0) _plateSectionOwned = false;
        RefreshFaceFilteredCollections();
        SelectedRebarZone = null;
        OnPropertyChanged(nameof(HasZones));
    }

    void SetZonePolygonFromPool(Contour? contour)
    {
        if (contour == null || SelectedRebarZone == null) return;
        SelectedRebarZone.SetPolygon(RebarZonePolygonConverter.FromContour(contour));
    }

    public IReadOnlyList<string> AvailableGeometrySets =>
        [.. ProjectContours
            .Select(c => c.GeometrySet)
            .Where(s => !string.IsNullOrEmpty(s))
            .Select(s => s!)
            .Distinct()
            .OrderBy(s => s)];

    void BatchAddZonesFromSet(string? geometrySet)
    {
        if (string.IsNullOrEmpty(geometrySet)) return;
        var matches = ProjectContours.Where(c => c.GeometrySet == geometrySet).ToList();
        if (matches.Count == 0) return;
        RebarLayoutDirty = true;
        bool wasEmpty = RebarZones.Count == 0;
        foreach (var contour in matches)
        {
            var zone = new RebarZone
            {
                Name = contour.Tag,
                Face = ActiveRebarFace,
                Polygon = RebarZonePolygonConverter.FromContour(contour),
            };
            RebarZones.Add(new RebarZoneVM(zone, OnZoneChanged, MarkRebarLayoutDirty, _app.Armatures));
        }
        if (wasEmpty) EnsurePlateSectionOwned();
        RefreshFaceFilteredCollections();
        RefreshPlot();
        OnPropertyChanged(nameof(HasZones));
    }

    public void SelectZoneAtPoint(double x, double y)
    {
        for (int i = ActiveFaceRebarZones.Count - 1; i >= 0; i--)
        {
            var zone = ActiveFaceRebarZones[i];
            if (zone.Model.Polygon.Count < 3) continue;
            var pts = zone.Model.Polygon.Select(p => (p.U, p.V)).ToList();
            if (CSTriangulation.GeometryUtils.PointInPolygon(x, y, pts)) { SelectedRebarZone = zone; return; }
        }
        SelectedRebarZone = null;
    }

    public void ApplyMaterialToSelectedZones(IEnumerable<RebarZoneVM> zones, Material? material)
    {
        foreach (var z in zones) z.Layout.RebarMaterial = material;
    }

    PlateRebarField BuildRebarField() => new(PlateSection!.RebarLayers, [.. RebarZones.Select(z => z.Model)]);

    void ComputeSectionCount()
    {
        if (Hull == null || PlateSection == null) return;
        var combos = PlateRebarFieldGridResolver.Resolve(BuildRebarField(), Hull, Holes, RebarSectionGridStep);
        System.Windows.MessageBox.Show(
            string.Format(Loc.S("PlanarRegionRebarSectionCountResult"), combos.Count),
            Loc.S("PlanarRegionRebarSectionCountTitle"),
            System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
    }

    void OnZoneChanged()
    {
        RebarLayoutDirty = true;
        RefreshFaceFilteredCollections();
        RefreshPlot();
    }

    void RefreshFaceFilteredCollections()
    {
        ActiveFaceSectionRebarLayers.Clear();
        foreach (var l in SelectedSectionRebarLayers)
            if (l.Model.Face == ActiveRebarFace) ActiveFaceSectionRebarLayers.Add(l);

        ActiveFaceRebarZones.Clear();
        foreach (var z in RebarZones)
            if (z.Model.Face == ActiveRebarFace) ActiveFaceRebarZones.Add(z);

        if (SelectedSectionRebarLayer != null && SelectedSectionRebarLayer.Model.Face != ActiveRebarFace)
            SelectedSectionRebarLayer = null;
        if (SelectedRebarZone != null && SelectedRebarZone.Model.Face != ActiveRebarFace)
            SelectedRebarZone = null;
    }

    void RefreshSectionRebarLayers()
    {
        SelectedSectionRebarLayers.Clear();
        if (_plateSection != null)
            foreach (var l in _plateSection.RebarLayers)
                SelectedSectionRebarLayers.Add(new PlateRebarLayerVM(l, MarkRebarLayoutDirty, _app.Armatures));
        RefreshFaceFilteredCollections();
    }

    void AddSectionRebarLayer()
    {
        if (PlateSection == null) return;
        RebarLayoutDirty = true;
        var layer = new PlateRebarLayer
        {
            Name = $"Слой {SelectedSectionRebarLayers.Count + 1}",
            InputMode = "diameter_spacing",
            DiameterX = 0.012, DiameterY = 0.012,
            SpacingX = 0.2, SpacingY = 0.2,
            Zsx = -(PlateSection.H / 2.0 - 0.03),
            Zsy = -(PlateSection.H / 2.0 - 0.04),
            Face = ActiveRebarFace,
        };
        layer.RecalcArea();
        PlateSection.RebarLayers.Add(layer);
        var vm = new PlateRebarLayerVM(layer, MarkRebarLayoutDirty, _app.Armatures);
        SelectedSectionRebarLayers.Add(vm);
        RefreshFaceFilteredCollections();
        SelectedSectionRebarLayer = vm;
    }

    void DeleteSectionRebarLayer()
    {
        if (SelectedSectionRebarLayer == null || PlateSection == null) return;
        RebarLayoutDirty = true;
        PlateSection.RebarLayers.Remove(SelectedSectionRebarLayer.Model);
        SelectedSectionRebarLayers.Remove(SelectedSectionRebarLayer);
        RefreshFaceFilteredCollections();
        SelectedSectionRebarLayer = null;
    }

    /// <summary>Жёсткий сдвиг полигона выбранной зоны на (du, dv) в локальных координатах региона.</summary>
    public void TranslateZoneGeometry(double du, double dv) => SelectedRebarZone?.Translate(du, dv);

    /// <summary>Жёсткое масштабирование полигона выбранной зоны вокруг начала координат (0,0).</summary>
    public void ScaleZoneGeometry(double factor) => SelectedRebarZone?.Scale(factor);

    /// <summary>Жёсткий поворот полигона выбранной зоны вокруг начала координат (0,0), градусы.</summary>
    public void RotateZoneGeometryDegrees(double angleDeg) => SelectedRebarZone?.RotateDegrees(angleDeg);

    bool _geometryOwned;

    /// <summary>Hull/Holes до первой трансформации — те же ссылки, что лежат в пуле App.Contours
    /// (см. SetHullFromPool/AddHole). Клонируем один раз, при первом вызове трансформации,
    /// чтобы не портить контуры пула геометрическими правками, сделанными в этом диалоге.</summary>
    void EnsureGeometryOwned()
    {
        if (_geometryOwned) return;
        if (Hull != null) Hull = CloneContour(Hull);
        for (int i = 0; i < Holes.Count; i++) Holes[i] = CloneContour(Holes[i]);
        _geometryOwned = true;
    }

    static Contour CloneContour(Contour c)
    {
        var clone = new Contour
        {
            Tag = c.Tag,
            Type = c.Type,
            Num = c.Num,
            GeometrySet = c.GeometrySet,
            X = new List<double>(c.X),
            Y = new List<double>(c.Y)
        };
        clone.SetWKT();
        return clone;
    }

    /// <summary>Пересинхронизирует Contour.Points из текущих X/Y сразу после прямой мутации
    /// координат. Обязательно после любой правки X/Y напрямую: EnsurePoints (см. RefreshGeoProps)
    /// пересинхронизирует Points только когда Points.Count не совпадает с X.Count — если Points уже
    /// был построен один раз (например, побочным эффектом сеттера Hull при первом клонировании в
    /// EnsureGeometryOwned, ещё до применения сдвига/масштаба/поворота), количество не меняется, и
    /// GeoProps(Contour) (её конструктор безусловно вызывает contour.PointsToXYs()) молча
    /// перезатирает X/Y устаревшими координатами из Points — сдвиг/масштаб/поворот откатывается
    /// к моменту показа геометрических свойств.</summary>
    static void SyncPointsFromXY(Contour c) => c.Points = c.XYsToPoints();

    /// <summary>Жёсткий сдвиг Hull+Holes на (dx, dy) в локальных координатах контура.</summary>
    public void TranslateGeometry(double dx, double dy)
    {
        EnsureGeometryOwned();
        void Apply(Contour c)
        {
            for (int i = 0; i < c.X.Count; i++) c.X[i] += dx;
            for (int i = 0; i < c.Y.Count; i++) c.Y[i] += dy;
            c.SetWKT();
            SyncPointsFromXY(c);
        }
        if (Hull != null) Apply(Hull);
        foreach (var hole in Holes) Apply(hole);
        RefreshPlot();
    }

    /// <summary>Жёсткое масштабирование Hull+Holes вокруг начала локальных координат (0,0).</summary>
    public void ScaleGeometry(double factor)
    {
        EnsureGeometryOwned();
        void Apply(Contour c)
        {
            for (int i = 0; i < c.X.Count; i++) c.X[i] *= factor;
            for (int i = 0; i < c.Y.Count; i++) c.Y[i] *= factor;
            c.SetWKT();
            SyncPointsFromXY(c);
        }
        if (Hull != null) Apply(Hull);
        foreach (var hole in Holes) Apply(hole);
        RefreshPlot();
    }

    /// <summary>Жёсткий поворот Hull+Holes вокруг начала локальных координат (0,0)
    /// на угол в градусах (против часовой стрелки — стандартная математическая конвенция).</summary>
    public void RotateGeometryDegrees(double angleDeg)
    {
        EnsureGeometryOwned();
        double rad = angleDeg * Math.PI / 180.0;
        double cos = Math.Cos(rad), sin = Math.Sin(rad);
        void Apply(Contour c)
        {
            for (int i = 0; i < c.X.Count; i++)
            {
                double x = c.X[i], y = c.Y[i];
                c.X[i] = x * cos - y * sin;
                c.Y[i] = x * sin + y * cos;
            }
            c.SetWKT();
            SyncPointsFromXY(c);
        }
        if (Hull != null) Apply(Hull);
        foreach (var hole in Holes) Apply(hole);
        RefreshPlot();
    }

    void RefreshPlot()
    {
        RefreshGeometryPlotElements();
        RefreshRebarPlotElements();
        RefreshGeoProps();
    }

    void RefreshGeometryPlotElements()
    {
        var elements = new List<PlotElement>();
        if (Hull != null && Hull.X.Count >= 3)
            elements.Add(new PolygonElement { Xs = [.. Hull.X], Ys = [.. Hull.Y], Fill = Brushes.LightSteelBlue, Stroke = Brushes.SteelBlue });
        foreach (var hole in Holes)
            if (hole.X.Count >= 3)
                elements.Add(new PolygonElement { Xs = [.. hole.X], Ys = [.. hole.Y], Fill = Brushes.White, Stroke = Brushes.Gray });
        GeometryPlotElements = elements;
    }

    void RefreshRebarPlotElements()
    {
        var elements = new List<PlotElement>();
        if (Hull != null && Hull.X.Count >= 3)
            elements.Add(new PolygonElement { Xs = [.. Hull.X], Ys = [.. Hull.Y], Fill = null, Stroke = Brushes.Gray, StrokeDashArray = [4, 2] });
        foreach (var hole in Holes)
            if (hole.X.Count >= 3)
                elements.Add(new PolygonElement { Xs = [.. hole.X], Ys = [.. hole.Y], Fill = null, Stroke = Brushes.Gray, StrokeDashArray = [4, 2] });

        if (ShowBackgroundRebarGlyphs && Hull != null && Hull.X.Count >= 3)
        {
            double uMin = Hull.X.Min(), uMax = Hull.X.Max();
            double vMin = Hull.Y.Min(), vMax = Hull.Y.Max();
            var bboxPoly = new List<(double U, double V)>
            {
                (uMin, vMin), (uMax, vMin), (uMax, vMax), (uMin, vMax)
            };
            foreach (var lvm in ActiveFaceSectionRebarLayers)
            {
                var glyphs = RebarGlyphBuilder.Build(bboxPoly, lvm.Model, lvm.RebarMaterial);
                // Подпись фона — в угол bbox (со сдвигом внутрь), а не в центр,
                // чтобы не перекрываться с подписями зон.
                for (int i = 0; i < glyphs.Count; i++)
                    if (glyphs[i] is TextElement t)
                        glyphs[i] = t with { X = uMin + (uMax - uMin) * 0.03, Y = vMax - (vMax - vMin) * 0.03 };
                elements.AddRange(glyphs);
            }
        }

        foreach (var zvm in ActiveFaceRebarZones)
        {
            var poly = zvm.Model.Polygon;
            if (poly.Count < 3) continue;
            bool selected = zvm == SelectedRebarZone;
            var fill = zvm.Model.Face == RebarFace.PlusN
                ? new SolidColorBrush(Color.FromArgb(70, 255, 140, 0))
                : new SolidColorBrush(Color.FromArgb(70, 0, 120, 255));
            elements.Add(new PolygonElement
            {
                Xs = [.. poly.Select(p => p.U)],
                Ys = [.. poly.Select(p => p.V)],
                Fill = fill,
                Stroke = selected ? Brushes.Red : Brushes.DimGray,
                StrokeThickness = selected ? 2 : 1
            });

            if (ShowZoneRebarGlyphs)
            {
                var zonePoly = poly.Select(p => (p.U, p.V)).ToList();
                elements.AddRange(RebarGlyphBuilder.Build(zonePoly, zvm.Model.Layout, zvm.Layout.RebarMaterial));
            }
        }
        RebarPlotElements = elements;
    }

    void RefreshGeoProps()
    {
        if (Hull == null || Hull.X.Count < 3)
        {
            GeoArea = GeoCentroidX = GeoCentroidY = GeoIx = GeoIy = GeoIxy = 0;
            return;
        }

        // Contour, загруженный из БД (GetPlanarRegions), несёт только X/Y — Points пуст, а
        // GeoProps(Contour) требует Points (внутренний PointsToXYs()). Синхронизируем перед
        // вычислением — тот же приём, что уже используется в PlanarRegion.BuildClosedContour.
        EnsurePoints(Hull);
        foreach (var hole in Holes) EnsurePoints(hole);

        var net = new GeoProps(Hull);
        foreach (var hole in Holes)
            net -= new GeoProps(hole);

        // ВАЖНО: net.Centroid — NaN при e=0 (баг GeoProps: EA=0 у обоих операндов → 0/0).
        // Центроид считаем вручную из Sx/Sy/A.
        GeoArea = net.A;
        GeoCentroidX = net.A > 1e-12 ? net.Sy / net.A : 0;
        GeoCentroidY = net.A > 1e-12 ? net.Sx / net.A : 0;
        GeoIx = net.Ix;
        GeoIy = net.Iy;
        GeoIxy = net.Ixy;
    }

    static void EnsurePoints(Contour contour)
    {
        if (contour.Points.Count != contour.X.Count)
            contour.Points = contour.XYsToPoints();
    }

    void Save()
    {
        if (TrySave(out var member))
            SaveCompleted?.Invoke(member!);
    }

    /// <summary>Сохраняет диалог без обязательного вызова SaveCompleted (которое закрывает окно) —
    /// нужно отдельно от Save() для обработчика Window.Closing, где закрытие уже идёт своим
    /// чередом и повторный Close() не требуется.</summary>
    public bool TrySave(out FemMember? member)
    {
        member = null;
        if (Hull == null)
        {
            Diagnostics = [new FemValidationDiagnostic("planar_region_hull_missing", "Не выбран внешний контур.")];
            return false;
        }

        var (region, diagnostics) = PlanarRegionCreation.TryCreate(Hull, Holes, _frame, Tag);
        Diagnostics = diagnostics;
        if (region == null) return false;

        region.RebarZones = [.. RebarZones.Select(vm => vm.Model)];
        region.RebarSectionGridStep = RebarSectionGridStep;

        if (_existingRegion != null)
        {
            region.Id = _existingRegion.Id;
            _app.db.UpdatePlanarRegion(region, _schema.Id);
        }
        else
        {
            _app.db.AddPlanarRegion(region, _schema.Id);
        }

        if (PlateSection != null)
        {
            if (_plateSectionOwned)
            {
                PlateSection.Tag = Tag;
                if (PlateSection.Num == 0)
                    PlateSection.Num = _app.PlateSections.Count > 0 ? _app.PlateSections.Max(p => p.Num) + 1 : 1;
                PlateSection.GeneratedForRegionId = region.Id;
            }
            _app.db.SavePlateSection(PlateSection);

            if (_plateSectionOwned)
                GenerateRebarSectionCombinations(region, Hull);
        }

        var builtMember = _existingMember ?? new FemMember { SchemaId = _schema.Id, ElemType = "shell", NodeIdsJson = "[]" };
        builtMember.ElemTag = Tag;
        builtMember.PlanarRegionId = region.Id;
        builtMember.Kind = Kind;
        builtMember.KindSource = KindSource;
        builtMember.PlateSectionId = PlateSection?.Id;
        _app.db.SaveFemMember(builtMember);

        RebarLayoutDirty = false;
        member = builtMember;
        return true;
    }

    /// <summary>Генерирует/обновляет секции для всех уникальных комбинаций слоёв, кроме фоновой
    /// (та уже сохранена выше как PlateSection среза 3). Matching — по fingerprint только среди
    /// PlateSection этого же региона (GeneratedForRegionId == region.Id), без кросс-региональной
    /// дедупликации. Секции, чья комбинация больше не встречается в текущей сетке, не удаляются —
    /// остаются в дереве как есть.</summary>
    void GenerateRebarSectionCombinations(PlanarRegion region, Contour hull)
    {
        var combos = PlateRebarFieldGridResolver.Resolve(BuildRebarField(), hull, Holes, RebarSectionGridStep);

        string backgroundFingerprint = PlateRebarLayoutFingerprint.Compute(PlateSection!.RebarLayers);
        var others = combos
            .Where(c => c.Fingerprint != backgroundFingerprint)
            .OrderBy(c => c.Fingerprint, StringComparer.Ordinal)
            .ToList();

        var existingForRegion = _app.PlateSections
            .Where(ps => ps.GeneratedForRegionId == region.Id && ps.Id != PlateSection.Id)
            .ToDictionary(ps => PlateRebarLayoutFingerprint.Compute(ps.RebarLayers));

        for (int i = 0; i < others.Count; i++)
        {
            var combo = others[i];
            string tag = $"{Tag} — комбинация {i + 1}";

            if (existingForRegion.TryGetValue(combo.Fingerprint, out var existing))
            {
                existing.RebarLayers = [.. combo.Layers];
                existing.Tag = tag;
                _app.db.SavePlateSection(existing);
            }
            else
            {
                var generated = PlateSection.CloneForCalc();
                generated.Id = 0;
                generated.Tag = tag;
                generated.Num = _app.PlateSections.Count > 0 ? _app.PlateSections.Max(p => p.Num) + 1 : 1;
                generated.RebarLayers = [.. combo.Layers];
                generated.GeneratedForRegionId = region.Id;
                _app.db.SavePlateSection(generated);
            }
        }
    }

    void Delete()
    {
        if (_existingMember == null) return;
        RebarLayoutDirty = false;
        _app.db.DeleteFemMember(_existingMember);
        if (_existingMember.PlanarRegionId is int prid) _app.db.DeletePlanarRegion(prid);
        DeleteCompleted?.Invoke(_existingMember);
    }
}
