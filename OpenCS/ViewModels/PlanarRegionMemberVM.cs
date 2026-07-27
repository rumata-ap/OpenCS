using CScore;
using CScore.Fem;
using CScore.Planar;
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
    public PlateSection? PlateSection { get => _plateSection; set { _plateSection = value; OnPropertyChanged(); } }

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

    void RefreshPlot()
    {
        var elements = new List<PlotElement>();
        if (Hull != null && Hull.X.Count >= 3)
            elements.Add(new PolygonElement { Xs = [.. Hull.X], Ys = [.. Hull.Y], Fill = Brushes.LightSteelBlue, Stroke = Brushes.SteelBlue });
        foreach (var hole in Holes)
            if (hole.X.Count >= 3)
                elements.Add(new PolygonElement { Xs = [.. hole.X], Ys = [.. hole.Y], Fill = Brushes.White, Stroke = Brushes.Gray });
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
