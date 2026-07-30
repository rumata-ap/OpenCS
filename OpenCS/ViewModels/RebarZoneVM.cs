using CScore.PlateRebar;
using OpenCS.Utilites;

namespace OpenCS.ViewModels;

/// <summary>ViewModel строки зоны армирования (PlanarRegion.RebarZones) в PlanarRegionMemberDialog.
/// Переиспользует PlateRebarLayerVM для редактирования Layout (тот же тип PlateRebarLayer,
/// что и у базовой раскладки PlateSection.RebarLayers).</summary>
public class RebarZoneVM : ViewModelBase
{
    readonly RebarZone _model;
    readonly System.Action _onChanged;

    public RebarZoneVM(RebarZone model, System.Action onChanged)
    {
        _model = model;
        _onChanged = onChanged;
        Layout = new PlateRebarLayerVM(model.Layout, onChanged);
    }

    public RebarZone Model => _model;
    public PlateRebarLayerVM Layout { get; }

    public string Name
    {
        get => _model.Name;
        set { _model.Name = value; OnPropertyChanged(); }
    }

    /// <summary>Ключ для ComboBox (SelectedValuePath="Tag"), см. RebarFace.</summary>
    public string FaceKey
    {
        get => _model.Face.ToString();
        set { _model.Face = System.Enum.Parse<RebarFace>(value); OnPropertyChanged(); _onChanged(); }
    }

    /// <summary>Ключ для ComboBox (SelectedValuePath="Tag"), см. RebarZoneOperation.</summary>
    public string OperationKey
    {
        get => _model.Operation.ToString();
        set { _model.Operation = System.Enum.Parse<RebarZoneOperation>(value); OnPropertyChanged(); }
    }

    public int Priority
    {
        get => _model.Priority;
        set { _model.Priority = value; OnPropertyChanged(); }
    }

    public int PointCount => _model.Polygon.Count;

    public void SetPolygon(List<RebarZonePoint> polygon)
    {
        _model.Polygon = polygon;
        OnPropertyChanged(nameof(PointCount));
        _onChanged();
    }

    public void Translate(double du, double dv)
    {
        foreach (var p in _model.Polygon) { p.U += du; p.V += dv; }
        _onChanged();
    }

    public void Scale(double factor)
    {
        foreach (var p in _model.Polygon) { p.U *= factor; p.V *= factor; }
        _onChanged();
    }

    public void RotateDegrees(double angleDeg)
    {
        double rad = angleDeg * System.Math.PI / 180.0;
        double cos = System.Math.Cos(rad), sin = System.Math.Sin(rad);
        foreach (var p in _model.Polygon)
        {
            double u = p.U, v = p.V;
            p.U = u * cos - v * sin;
            p.V = u * sin + v * cos;
        }
        _onChanged();
    }
}
