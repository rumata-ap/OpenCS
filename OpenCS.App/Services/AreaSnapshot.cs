using CScore;
using OpenCS.Utilites;

namespace OpenCS.Services;

/// <summary>
/// Записанное состояние области пула для «*» и «Не сохранять» на страницах сечений. Сетка волокон (poly/tri) и её
/// параметры в снимок не входят: окно «Сетка» пишет их сразу. Контуры — ссылки (контур пула правится своей страницей)
/// и WKT области.
/// </summary>
public sealed class AreaSnapshot
{
    sealed record Bar(double X, double Y, double Area, double Diameter, double EpsP);

    string _tag = "";
    string? _description;
    int _materialId;
    Material? _material;
    DiagrammType _diagram;
    AreaCategory _category;
    int? _hostAreaId;
    MaterialArea? _hostArea;
    int? _poolContourId;
    Contour? _poolContour;
    List<Contour> _contours = [];
    string? _wkt;
    double _h;
    int _nx, _ny;
    double _sigSp, _gammaSp;
    RebarRepresentation _representation;
    IdealizedRebarAxis? _axis;
    List<Bar> _bars = [];
    List<StirrupGroup> _stirrups = [];
    string _stirrupsSignature = "";

    /// <summary>Записанное обозначение (для вопроса о сохранении, когда поле обозначения очищено).</summary>
    public string Tag => _tag;

    /// <summary>Снимок текущего состояния области.</summary>
    public static AreaSnapshot Capture(MaterialArea a) => new()
    {
        _tag = a.Tag ?? "",
        _description = a.Description,
        _materialId = a.MaterialId,
        _material = a.Material,
        _diagram = a.DiagrammType,
        _category = a.Category,
        _hostAreaId = a.HostArea?.Id is int hid and > 0 ? hid : a.HostAreaId,
        _hostArea = a.HostArea,
        _poolContourId = a.PoolContourId,
        _poolContour = a.PoolContour,
        _contours = [.. a.Contours],
        _wkt = a.WKT,
        _h = a.H,
        _nx = a.NX,
        _ny = a.NY,
        _sigSp = a.SigSp,
        _gammaSp = a.GammaSp,
        _representation = a.RebarRepresentation,
        _axis = a.IdealizedAxis,
        _bars = a.Fibers.Where(f => f.TypeFiber == FiberType.point)
                        .Select(f => new Bar(f.X, f.Y, f.Area, f.Diameter, f.Eps_p)).ToList(),
        _stirrups = a.Stirrups.Select(g => g.Clone(preserveId: true)).ToList(),
        _stirrupsSignature = StirrupsSignature(a.Stirrups),
    };

    /// <summary>Совпадает ли область с этим снимком (по записываемым полям).</summary>
    public bool Matches(MaterialArea a)
    {
        var now = Capture(a);
        return now._tag == _tag && now._description == _description && now._materialId == _materialId
            && now._diagram == _diagram && now._category == _category && now._hostAreaId == _hostAreaId
            && now._poolContourId == _poolContourId && now._contours.SequenceEqual(_contours) && now._wkt == _wkt
            && now._nx == _nx && now._ny == _ny && now._sigSp.Equals(_sigSp) && now._gammaSp.Equals(_gammaSp)
            && now._representation == _representation && now._axis == _axis && now._bars.SequenceEqual(_bars)
            && now._stirrupsSignature == _stirrupsSignature;
    }

    /// <summary>Возвращает область к снимку и пересобирает её диаграммы.</summary>
    public void RestoreTo(MaterialArea a, IReadOnlyList<Diagramm>? pool, CalcSettings calc)
    {
        a.Tag = _tag;
        a.Description = _description;
        a.MaterialId = _materialId;
        a.Material = _material;
        a.DiagrammType = _diagram;
        a.Category = _category;
        a.HostAreaId = _hostAreaId;
        a.HostArea = _hostArea;
        a.PoolContourId = _poolContourId;
        a.PoolContour = _poolContour;
        a.Contours = [.. _contours];
        a.WKT = _wkt;
        a.H = _h;
        a.NX = _nx;
        a.NY = _ny;
        a.SigSp = _sigSp;
        a.GammaSp = _gammaSp;
        a.RebarRepresentation = _representation;
        a.IdealizedAxis = _axis;
        var mesh = a.Fibers.Where(f => f.TypeFiber != FiberType.point).ToList();
        a.Fibers = [.. mesh, .. _bars.Select(b => new Fiber(b.X, b.Y)
        {
            Area = b.Area, Diameter = b.Diameter, Eps_p = b.EpsP, TypeFiber = FiberType.point
        })];
        a.Stirrups = _stirrups.Select(g => g.Clone(preserveId: true)).ToList();
        a.ResolveAndBuildDiagramms(calc.Sp63DescEtaMin, pool, calc.RebarDifferentialDiagram, calc.EkbDescEtaMin);
    }

    /// <summary>Строка-отпечаток групп хомутов: материал, шаг, отступ, элементы (Ø, площадь, WKT центровой линии).</summary>
    public static string StirrupsSignature(IEnumerable<StirrupGroup> groups) =>
        string.Join("|", groups.Select(g =>
            $"{g.MaterialId};{g.SpacingM:R};{g.OffsetM:R};" +
            string.Join(",", g.Elements.Select(e => $"{e.BarDiameterM:R}/{e.BarAreaM2:R}/{e.CenterlineContour.WKT}"))));
}
