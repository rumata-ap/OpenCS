using CScore;
using CScore.Fem;
using CScore.Planar;
using CScore.PlateRebar;
using OpenCS.Utilites;
using OpenCS.Views;
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
            (existingRegion?.RebarZones ?? []).Select(z => new RebarZoneVM(z, RefreshPlot)));

        if (existingRegion != null)
        {
            Hull = existingRegion.Hull;
            foreach (var h in existingRegion.Holes) Holes.Add(h);
        }

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
        AddSectionRebarLayerCommand = new RelayCommand(_ => AddSectionRebarLayer(), _ => PlateSection != null);
        DeleteSectionRebarLayerCommand = new RelayCommand(_ => DeleteSectionRebarLayer(), _ => SelectedSectionRebarLayer != null);
        SaveCommand = new RelayCommand(_ => Save());
        DeleteCommand = new RelayCommand(_ => Delete(), _ => _existingMember != null);

        RefreshPlot();
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
        set { _plateSection = value; OnPropertyChanged(); RefreshSectionRebarLayers(); }
    }

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

    IReadOnlyList<PlotElement> _plotElements = [];
    public IReadOnlyList<PlotElement> PlotElements { get => _plotElements; private set { _plotElements = value; OnPropertyChanged(); } }

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

    void AddRebarZone()
    {
        var zone = new RebarZone { Name = $"Зона {RebarZones.Count + 1}" };
        var vm = new RebarZoneVM(zone, RefreshPlot);
        RebarZones.Add(vm);
        SelectedRebarZone = vm;
    }

    void DeleteRebarZone()
    {
        if (SelectedRebarZone == null) return;
        RebarZones.Remove(SelectedRebarZone);
        SelectedRebarZone = null;
        RefreshPlot();
    }

    void SetZonePolygonFromPool(Contour? contour)
    {
        if (contour == null || SelectedRebarZone == null) return;
        SelectedRebarZone.SetPolygon(RebarZonePolygonConverter.FromContour(contour));
        RefreshPlot();
    }

    void RefreshSectionRebarLayers()
    {
        SelectedSectionRebarLayers.Clear();
        if (_plateSection == null) return;
        foreach (var l in _plateSection.RebarLayers)
            SelectedSectionRebarLayers.Add(new PlateRebarLayerVM(l, () => { }));
    }

    void AddSectionRebarLayer()
    {
        if (PlateSection == null) return;
        var layer = new PlateRebarLayer
        {
            Name = $"Слой {SelectedSectionRebarLayers.Count + 1}",
            InputMode = "diameter_spacing",
            DiameterX = 0.012, DiameterY = 0.012,
            SpacingX = 0.2, SpacingY = 0.2,
            Zsx = -(PlateSection.H / 2.0 - 0.03),
            Zsy = -(PlateSection.H / 2.0 - 0.04),
        };
        layer.RecalcArea();
        PlateSection.RebarLayers.Add(layer);
        var vm = new PlateRebarLayerVM(layer, () => { });
        SelectedSectionRebarLayers.Add(vm);
        SelectedSectionRebarLayer = vm;
    }

    void DeleteSectionRebarLayer()
    {
        if (SelectedSectionRebarLayer == null || PlateSection == null) return;
        PlateSection.RebarLayers.Remove(SelectedSectionRebarLayer.Model);
        SelectedSectionRebarLayers.Remove(SelectedSectionRebarLayer);
        SelectedSectionRebarLayer = null;
    }

    /// <summary>Жёсткий сдвиг полигона выбранной зоны на (du, dv) в локальных координатах региона.</summary>
    public void TranslateZoneGeometry(double du, double dv)
    {
        SelectedRebarZone?.Translate(du, dv);
        RefreshPlot();
    }

    /// <summary>Жёсткое масштабирование полигона выбранной зоны вокруг начала координат (0,0).</summary>
    public void ScaleZoneGeometry(double factor)
    {
        SelectedRebarZone?.Scale(factor);
        RefreshPlot();
    }

    /// <summary>Жёсткий поворот полигона выбранной зоны вокруг начала координат (0,0), градусы.</summary>
    public void RotateZoneGeometryDegrees(double angleDeg)
    {
        SelectedRebarZone?.RotateDegrees(angleDeg);
        RefreshPlot();
    }

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
        var elements = new List<PlotElement>();
        if (Hull != null && Hull.X.Count >= 3)
            elements.Add(new PolygonElement { Xs = [.. Hull.X], Ys = [.. Hull.Y], Fill = Brushes.LightSteelBlue, Stroke = Brushes.SteelBlue });
        foreach (var hole in Holes)
            if (hole.X.Count >= 3)
                elements.Add(new PolygonElement { Xs = [.. hole.X], Ys = [.. hole.Y], Fill = Brushes.White, Stroke = Brushes.Gray });

        foreach (var zvm in RebarZones)
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
        }

        PlotElements = elements;
        RefreshGeoProps();
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
        if (Hull == null)
        {
            Diagnostics = [new FemValidationDiagnostic("planar_region_hull_missing", "Не выбран внешний контур.")];
            return;
        }

        var (region, diagnostics) = PlanarRegionCreation.TryCreate(Hull, Holes, _frame, Tag);
        Diagnostics = diagnostics;
        if (region == null) return;

        region.RebarZones = [.. RebarZones.Select(vm => vm.Model)];

        if (_existingRegion != null)
        {
            region.Id = _existingRegion.Id;
            _app.db.UpdatePlanarRegion(region, _schema.Id);
        }
        else
        {
            _app.db.AddPlanarRegion(region, _schema.Id);
        }

        var member = _existingMember ?? new FemMember { SchemaId = _schema.Id, ElemType = "shell", NodeIdsJson = "[]" };
        member.ElemTag = Tag;
        member.PlanarRegionId = region.Id;
        member.Kind = Kind;
        member.KindSource = KindSource;
        member.PlateSectionId = PlateSection?.Id;
        _app.db.SaveFemMember(member);

        if (PlateSection != null) _app.db.SavePlateSection(PlateSection);

        SaveCompleted?.Invoke(member);
    }

    void Delete()
    {
        if (_existingMember == null) return;
        _app.db.DeleteFemMember(_existingMember);
        if (_existingMember.PlanarRegionId is int prid) _app.db.DeletePlanarRegion(prid);
        DeleteCompleted?.Invoke(_existingMember);
    }
}
