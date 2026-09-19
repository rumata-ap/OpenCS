using System.Collections.ObjectModel;
using System.Globalization;
using CScore;
using CScore.ParametricRc;
using CScore.Sp63Shear;
using OpenCS.Utilites;

namespace OpenCS.ViewModels;

/// <summary>Модель единого мастера параметрического ЖБ-сечения; размеры UI заданы в мм.</summary>
public sealed class ParametricRcSectionVM : ViewModelBase
{
    readonly bool _materialsConfigured;
    ParametricRcShape _shape = ParametricRcShape.Rectangle;
    string _tag = "";
    bool _tagCustomized;
    double _widthMm = 300, _heightMm = 500, _webMm = 160, _flangeMm = 100;
    double _innerDiameterMm = 150;
    bool _lowerRebarEnabled, _upperRebarEnabled;
    bool _lowerRebarIdealized, _upperRebarIdealized;
    int _lowerRebarCount = 2, _upperRebarCount = 2;
    double _lowerRebarDiameterMm = 16, _upperRebarDiameterMm = 12;
    double _lowerRebarAreaMm2 = 200, _upperRebarAreaMm2 = 120;
    double _lowerRebarOffsetMm = 40, _upperRebarOffsetMm = 40;
    IdealizedRebarAxis _lowerRebarAxis = IdealizedRebarAxis.Mx, _upperRebarAxis = IdealizedRebarAxis.Mx;
    int _polarRebarCount = 8;
    double _polarRebarDiameterMm = 16, _polarRebarRadiusMm = 110;
    int _stirrupCount;
    bool _stirrupsEnabled;
    double _stirrupDiameterMm = 8, _stirrupStepMm = 200, _stirrupCoverMm = 30;
    int _stirrupMaterialId;
    int _concreteMaterialId, _longitudinalMaterialId;
    double _stirrupRswMpa = 280;
    ParametricStirrupZone _stirrupZone = ParametricStirrupZone.Body;
    ParametricStirrupDirection _stirrupDirection = ParametricStirrupDirection.Vertical;

    /// <summary>Дополнительные наборы срезов; основной набор задаётся полями мастера.</summary>
    public ObservableCollection<ParametricStirrupCutSet> AdditionalStirrupCuts { get; } = [];
    /// <summary>Доступные материалы бетона проекта.</summary>
    public ObservableCollection<Material> ConcreteMaterials { get; } = [];
    /// <summary>Доступные материалы продольной и поперечной арматуры проекта.</summary>
    public ObservableCollection<Material> ReinforcementMaterials { get; } = [];

    /// <summary>Создаёт модель мастера.</summary>
    public ParametricRcSectionVM(IEnumerable<Material>? concreteMaterials = null,
        IEnumerable<Material>? reinforcementMaterials = null)
    {
        _materialsConfigured = concreteMaterials is not null || reinforcementMaterials is not null;
        if (concreteMaterials is not null)
            foreach (var material in concreteMaterials)
                ConcreteMaterials.Add(material);
        if (reinforcementMaterials is not null)
            foreach (var material in reinforcementMaterials)
                ReinforcementMaterials.Add(material);
        _concreteMaterialId = ConcreteMaterials.FirstOrDefault()?.Id ?? 0;
        _longitudinalMaterialId = ReinforcementMaterials.FirstOrDefault()?.Id ?? 0;
        _stirrupMaterialId = _longitudinalMaterialId;
        AdditionalStirrupCuts.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(StirrupStepWarning));
            RefreshPreview();
        };
        _tag = BuildDefaultTag();
    }

    /// <summary>Выбранная форма.</summary>
    public ParametricRcShape Shape { get => _shape; set { _shape = value; OnPropertyChanged(); RefreshPreview(); } }
    /// <summary>Наименование (тэг) параметрического сечения.</summary>
    public string Tag
    {
        get => _tag;
        set
        {
            _tag = value ?? "";
            _tagCustomized = true;
            OnPropertyChanged();
            RefreshPreview();
        }
    }
    /// <summary>Id выбранного материала бетона.</summary>
    public int ConcreteMaterialId
    {
        get => _concreteMaterialId;
        set { _concreteMaterialId = value; OnPropertyChanged(); RefreshPreview(); }
    }
    /// <summary>Id выбранного материала продольной арматуры.</summary>
    public int LongitudinalMaterialId
    {
        get => _longitudinalMaterialId;
        set { _longitudinalMaterialId = value; OnPropertyChanged(); RefreshPreview(); }
    }
    /// <summary>Ширина или наружный диаметр, мм.</summary>
    public double WidthMm { get => _widthMm; set { _widthMm = value; OnPropertyChanged(); RefreshPreview(); } }
    /// <summary>Высота, мм.</summary>
    public double HeightMm { get => _heightMm; set { _heightMm = value; OnPropertyChanged(); RefreshPreview(); } }
    /// <summary>Толщина стенки или внутренний диаметр кольца, мм.</summary>
    public double WebMm { get => _webMm; set { _webMm = value; OnPropertyChanged(); RefreshPreview(); } }
    /// <summary>Толщина полки, мм.</summary>
    public double FlangeMm { get => _flangeMm; set { _flangeMm = value; OnPropertyChanged(); RefreshPreview(); } }
    /// <summary>Внутренний диаметр кольца, мм.</summary>
    public double InnerDiameterMm { get => _innerDiameterMm; set { _innerDiameterMm = value; OnPropertyChanged(); RefreshPreview(); } }
    /// <summary>Число равномерных стержней по окружности.</summary>
    public int PolarRebarCount { get => _polarRebarCount; set { _polarRebarCount = value; OnPropertyChanged(); RefreshPreview(); } }
    /// <summary>Диаметр полярных стержней, мм.</summary>
    public double PolarRebarDiameterMm { get => _polarRebarDiameterMm; set { _polarRebarDiameterMm = value; OnPropertyChanged(); RefreshPreview(); } }
    /// <summary>Радиус центра полярных стержней, мм.</summary>
    public double PolarRebarRadiusMm { get => _polarRebarRadiusMm; set { _polarRebarRadiusMm = value; OnPropertyChanged(); RefreshPreview(); } }

    /// <summary>Включает нижний продольный слой.</summary>
    public bool LowerRebarEnabled { get => _lowerRebarEnabled; set { _lowerRebarEnabled = value; OnPropertyChanged(); RefreshPreview(); } }
    /// <summary>Представляет нижний слой одной расчётной площадью.</summary>
    public bool LowerRebarIdealized { get => _lowerRebarIdealized; set { _lowerRebarIdealized = value; OnPropertyChanged(); RefreshPreview(); } }
    /// <summary>Число физических нижних стержней.</summary>
    public int LowerRebarCount { get => _lowerRebarCount; set { _lowerRebarCount = value; OnPropertyChanged(); RefreshPreview(); } }
    /// <summary>Диаметр нижнего слоя, мм.</summary>
    public double LowerRebarDiameterMm { get => _lowerRebarDiameterMm; set { _lowerRebarDiameterMm = value; OnPropertyChanged(); RefreshPreview(); } }
    /// <summary>Площадь нижнего расчётного слоя, мм².</summary>
    public double LowerRebarAreaMm2 { get => _lowerRebarAreaMm2; set { _lowerRebarAreaMm2 = value; OnPropertyChanged(); RefreshPreview(); } }
    /// <summary>Привязка центра нижнего слоя от нижней грани бетона, мм.</summary>
    public double LowerRebarOffsetMm { get => _lowerRebarOffsetMm; set { _lowerRebarOffsetMm = value; OnPropertyChanged(); RefreshPreview(); } }
    /// <summary>Ось изгиба нижнего расчётного слоя.</summary>
    public IdealizedRebarAxis LowerRebarAxis { get => _lowerRebarAxis; set { _lowerRebarAxis = value; OnPropertyChanged(); RefreshPreview(); } }

    /// <summary>Включает верхний продольный слой.</summary>
    public bool UpperRebarEnabled { get => _upperRebarEnabled; set { _upperRebarEnabled = value; OnPropertyChanged(); RefreshPreview(); } }
    /// <summary>Представляет верхний слой одной расчётной площадью.</summary>
    public bool UpperRebarIdealized { get => _upperRebarIdealized; set { _upperRebarIdealized = value; OnPropertyChanged(); RefreshPreview(); } }
    /// <summary>Число физических верхних стержней.</summary>
    public int UpperRebarCount { get => _upperRebarCount; set { _upperRebarCount = value; OnPropertyChanged(); RefreshPreview(); } }
    /// <summary>Диаметр верхнего слоя, мм.</summary>
    public double UpperRebarDiameterMm { get => _upperRebarDiameterMm; set { _upperRebarDiameterMm = value; OnPropertyChanged(); RefreshPreview(); } }
    /// <summary>Площадь верхнего расчётного слоя, мм².</summary>
    public double UpperRebarAreaMm2 { get => _upperRebarAreaMm2; set { _upperRebarAreaMm2 = value; OnPropertyChanged(); RefreshPreview(); } }
    /// <summary>Привязка центра верхнего слоя от верхней грани бетона, мм.</summary>
    public double UpperRebarOffsetMm { get => _upperRebarOffsetMm; set { _upperRebarOffsetMm = value; OnPropertyChanged(); RefreshPreview(); } }
    /// <summary>Ось изгиба верхнего расчётного слоя.</summary>
    public IdealizedRebarAxis UpperRebarAxis { get => _upperRebarAxis; set { _upperRebarAxis = value; OnPropertyChanged(); RefreshPreview(); } }

    /// <summary>Зона набора открытых срезов.</summary>
    public ParametricStirrupZone StirrupZone { get => _stirrupZone; set { _stirrupZone = value; OnPropertyChanged(); RefreshPreview(); } }
    /// <summary>Направление открытого среза.</summary>
    public ParametricStirrupDirection StirrupDirection { get => _stirrupDirection; set { _stirrupDirection = value; OnPropertyChanged(); RefreshPreview(); } }
    /// <summary>Включает учёт поперечной арматуры.</summary>
    public bool StirrupsEnabled
    {
        get => _stirrupsEnabled;
        set { _stirrupsEnabled = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsStirrupFieldsEnabled)); RefreshPreview(); }
    }
    /// <summary>Доступны ли поля поперечной арматуры.</summary>
    public bool IsStirrupFieldsEnabled => StirrupsEnabled && CanUseStirrups;
    /// <summary>Число срезов.</summary>
    public int StirrupCount { get => _stirrupCount; set { _stirrupCount = value; OnPropertyChanged(); RefreshPreview(); } }
    /// <summary>Диаметр срезов, мм.</summary>
    public double StirrupDiameterMm { get => _stirrupDiameterMm; set { _stirrupDiameterMm = value; OnPropertyChanged(); RefreshPreview(); } }
    /// <summary>Шаг поперечной арматуры, мм.</summary>
    public double StirrupStepMm { get => _stirrupStepMm; set { _stirrupStepMm = value; OnPropertyChanged(); RefreshPreview(); } }
    /// <summary>Защитный слой срезов, мм.</summary>
    public double StirrupCoverMm { get => _stirrupCoverMm; set { _stirrupCoverMm = value; OnPropertyChanged(); RefreshPreview(); } }
    /// <summary>Id материала поперечной арматуры.</summary>
    public int StirrupMaterialId { get => _stirrupMaterialId; set { _stirrupMaterialId = value; OnPropertyChanged(); RefreshPreview(); } }
    /// <summary>Расчётное сопротивление поперечной арматуры для preview, МПа.</summary>
    public double StirrupRswMpa { get => _stirrupRswMpa; set { _stirrupRswMpa = value; OnPropertyChanged(); OnPropertyChanged(nameof(Qsw)); } }

    /// <summary>Результат материализации текущих полей.</summary>
    public ParametricRcGenerationResult Preview { get; private set; }
        = new(new CrossSection(), ["Введите размеры сечения."]);
    /// <summary>Можно ли сохранить preview.</summary>
    public bool CanSave => Diagnostics.Count == 0;
    /// <summary>Доступны ли поля стенки.</summary>
    public bool ShowWebFields => Shape is ParametricRcShape.Tee or ParametricRcShape.IBeam;
    /// <summary>Показывает ли форма слои нижней и верхней арматуры.</summary>
    public bool ShowLayerRebarFields => Shape is not (ParametricRcShape.Circle or ParametricRcShape.Annulus);
    /// <summary>Доступно ли редактирование высоты сечения.</summary>
    public bool IsHeightEnabled => ShowLayerRebarFields;
    /// <summary>Доступно ли поле внутреннего диаметра.</summary>
    public bool ShowInnerDiameter => Shape == ParametricRcShape.Annulus;
    /// <summary>Доступна ли поперечная арматура.</summary>
    public bool CanUseStirrups => Shape is not (ParametricRcShape.Circle or ParametricRcShape.Annulus);
    /// <summary>Показывает поля полярной арматуры.</summary>
    public bool ShowPolarFields => Shape is ParametricRcShape.Circle or ParametricRcShape.Annulus;
    /// <summary>Показывает справочное ограничение нормальной проверки для тавра и двутавра.</summary>
    public bool ShowTeeApplicability => Shape is ParametricRcShape.Tee or ParametricRcShape.IBeam;
    /// <summary>Показывает справочное ограничение приложения Д для круглых форм.</summary>
    public bool ShowRoundApplicability => Shape is ParametricRcShape.Circle or ParametricRcShape.Annulus;
    /// <summary>Предпросмотр площади ветвей срезов, м².</summary>
    public double Asw => Preview.Section.Areas
        .Where(a => a.Category == AreaCategory.Stirrups)
        .SelectMany(a => a.Stirrups)
        .SelectMany(g => g.Elements)
        .Sum(e => StirrupResolver.BranchArea(e,
            e.Source?.Direction == StirrupCutDirection.Horizontal
                ? ShearPlane.Vx : ShearPlane.Vy));
    /// <summary>Погонное усилие поперечной арматуры preview, кН/м.</summary>
    public double Qsw => Preview.Section.Areas
        .Where(a => a.Category == AreaCategory.Stirrups)
        .SelectMany(a => a.Stirrups)
        .Where(g => g.SpacingM > 0)
        .Sum(g => StirrupRswMpa * 1000.0 * g.Elements.Sum(e =>
            StirrupResolver.BranchArea(e, e.Source?.Direction == StirrupCutDirection.Horizontal
                ? ShearPlane.Vx : ShearPlane.Vy)) / g.SpacingM);
    /// <summary>Предупреждение о разных шагах наборов.</summary>
    public string StirrupStepWarning => BuildDefinition().StirrupCuts
        .Where(c => c.Count > 0)
        .Select(c => c.SpacingM)
        .Distinct()
        .Count() > 1 ? Loc.S("ParametricRcDifferentStepWarning") : "";
    /// <summary>Диагностика текущего ввода.</summary>
    public IReadOnlyList<string> Diagnostics => Preview.Diagnostics
        .Concat(MaterialDiagnostics())
        .ToArray();
    /// <summary>Диагностика для вывода в диалоге одной строкой на сообщение.</summary>
    public string DiagnosticsText => string.Join(Environment.NewLine, Diagnostics);

    /// <summary>Заполняет поля мастера из сохранённого определения без пересохранения source.</summary>
    public void LoadDefinition(ParametricRcSectionDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        Shape = definition.Shape;
        if (definition.ConcreteMaterialId > 0)
            ConcreteMaterialId = definition.ConcreteMaterialId;
        if (definition.LongitudinalMaterialId > 0)
            LongitudinalMaterialId = definition.LongitudinalMaterialId;
        WidthMm = definition.WidthM * 1000.0;
        HeightMm = definition.HeightM * 1000.0;
        WebMm = definition.WebThicknessM * 1000.0;
        FlangeMm = definition.FlangeThicknessM * 1000.0;
        InnerDiameterMm = definition.InnerDiameterM * 1000.0;
        if (definition.PolarRebar is { } polar)
        {
            PolarRebarCount = polar.Count;
            PolarRebarDiameterMm = polar.DiameterM * 1000.0;
            PolarRebarRadiusMm = polar.RadiusM * 1000.0;
        }
        LoadLayer(definition.LowerRebar, true);
        LoadLayer(definition.UpperRebar, false);
        var cut = definition.StirrupCuts.FirstOrDefault();
        StirrupsEnabled = definition.StirrupCuts.Any(c => c.Count > 0);
        if (cut is not null)
        {
            StirrupZone = cut.Zone;
            StirrupDirection = cut.Direction;
            StirrupCount = cut.Count;
            StirrupDiameterMm = cut.DiameterM * 1000.0;
            StirrupStepMm = cut.SpacingM * 1000.0;
            StirrupCoverMm = cut.CoverM * 1000.0;
            StirrupMaterialId = cut.MaterialId;
        }
        else StirrupCount = 0;
        AdditionalStirrupCuts.Clear();
        foreach (var additional in definition.StirrupCuts.Skip(1))
            AdditionalStirrupCuts.Add(additional);
        string generatedTag = BuildDefaultTag();
        string legacyTag = ParametricRcSectionDefinition.Rectangle(1, 1).Tag;
        bool useGeneratedTag = string.IsNullOrWhiteSpace(definition.Tag) ||
            string.Equals(definition.Tag, legacyTag, StringComparison.Ordinal);
        _tag = useGeneratedTag ? generatedTag : definition.Tag;
        _tagCustomized = !useGeneratedTag && !string.Equals(_tag, generatedTag, StringComparison.Ordinal);
        OnPropertyChanged(nameof(Tag));
        RefreshPreview();
    }

    /// <summary>Создаёт доменное определение с переводом мм в м.</summary>
    public ParametricRcSectionDefinition BuildDefinition()
    {
        ParametricRcSectionDefinition definition = Shape switch
        {
            ParametricRcShape.Rectangle => ParametricRcSectionDefinition.Rectangle(WidthMm / 1000, HeightMm / 1000),
            ParametricRcShape.Tee => ParametricRcSectionDefinition.Tee(WidthMm / 1000, HeightMm / 1000, WebMm / 1000, FlangeMm / 1000),
            ParametricRcShape.IBeam => ParametricRcSectionDefinition.IBeam(WidthMm / 1000, HeightMm / 1000, WebMm / 1000, FlangeMm / 1000),
            ParametricRcShape.Circle => ParametricRcSectionDefinition.Circle(WidthMm / 1000),
            ParametricRcShape.Annulus => ParametricRcSectionDefinition.Annulus(WidthMm / 1000, InnerDiameterMm / 1000),
            _ => throw new ArgumentOutOfRangeException()
        };

        var bounds = GetGeometryBounds(definition);
        definition = definition with
        {
            Tag = Tag.Trim(),
            ConcreteMaterialId = ConcreteMaterialId,
            LongitudinalMaterialId = LongitudinalMaterialId,
            LowerRebar = BuildLayer(LowerRebarEnabled, LowerRebarIdealized, LowerRebarCount,
                LowerRebarDiameterMm, LowerRebarAreaMm2,
                LayerCoordinateM(bounds, true, LowerRebarIdealized, LowerRebarAxis, LowerRebarOffsetMm), LowerRebarAxis),
            UpperRebar = BuildLayer(UpperRebarEnabled, UpperRebarIdealized, UpperRebarCount,
                UpperRebarDiameterMm, UpperRebarAreaMm2,
                LayerCoordinateM(bounds, false, UpperRebarIdealized, UpperRebarAxis, UpperRebarOffsetMm), UpperRebarAxis),
            PolarRebar = ShowPolarFields
                ? new ParametricPolarRebar(PolarRebarCount, PolarRebarDiameterMm / 1000.0,
                    PolarRebarRadiusMm / 1000.0)
                : null
        };

        var cuts = new List<ParametricStirrupCutSet>();
        if (CanUseStirrups && StirrupsEnabled && StirrupCount != 0)
            cuts.Add(new ParametricStirrupCutSet(
                StirrupZone, StirrupDirection, StirrupCount,
                StirrupDiameterMm / 1000.0, StirrupStepMm / 1000.0,
                StirrupCoverMm / 1000.0, StirrupMaterialId));
        if (CanUseStirrups && StirrupsEnabled)
            cuts.AddRange(AdditionalStirrupCuts);
        if (cuts.Count > 0)
        definition = definition with
        {
            StirrupCuts = cuts
        };
        return definition;
    }

    static ParametricLongitudinalLayer? BuildLayer(bool enabled, bool idealized, int count,
        double diameterMm, double areaMm2, double coordinateM, IdealizedRebarAxis axis)
    {
        if (!enabled) return null;
        return idealized
            ? ParametricLongitudinalLayer.Idealized(areaMm2 / 1_000_000.0,
                diameterMm / 1000.0, coordinateM, axis)
            : ParametricLongitudinalLayer.Physical(count, diameterMm / 1000.0, coordinateM);
    }

    /// <summary>Возвращает фактическую координату центра слоя в мм для preview.</summary>
    public double GetRebarCentroidCoordinateMm(bool lower)
    {
        var definition = Shape switch
        {
            ParametricRcShape.Rectangle => ParametricRcSectionDefinition.Rectangle(WidthMm / 1000, HeightMm / 1000),
            ParametricRcShape.Tee => ParametricRcSectionDefinition.Tee(WidthMm / 1000, HeightMm / 1000, WebMm / 1000, FlangeMm / 1000),
            ParametricRcShape.IBeam => ParametricRcSectionDefinition.IBeam(WidthMm / 1000, HeightMm / 1000, WebMm / 1000, FlangeMm / 1000),
            ParametricRcShape.Circle => ParametricRcSectionDefinition.Circle(WidthMm / 1000),
            ParametricRcShape.Annulus => ParametricRcSectionDefinition.Annulus(WidthMm / 1000, InnerDiameterMm / 1000),
            _ => throw new ArgumentOutOfRangeException()
        };
        var bounds = GetGeometryBounds(definition);
        bool idealized = lower ? LowerRebarIdealized : UpperRebarIdealized;
        var axis = lower ? LowerRebarAxis : UpperRebarAxis;
        double offsetMm = lower ? LowerRebarOffsetMm : UpperRebarOffsetMm;
        return LayerCoordinateM(bounds, lower, idealized, axis, offsetMm) * 1000.0;
    }

    static double LayerCoordinateM((double MinX, double MaxX, double MinY, double MaxY) bounds,
        bool lower, bool idealized, IdealizedRebarAxis axis, double offsetMm)
    {
        bool normalToX = idealized && axis == IdealizedRebarAxis.My;
        double face = normalToX
            ? (lower ? bounds.MinX : bounds.MaxX)
            : (lower ? bounds.MinY : bounds.MaxY);
        double offsetM = offsetMm / 1000.0;
        return lower ? face + offsetM : face - offsetM;
    }

    static (double MinX, double MaxX, double MinY, double MaxY) GetGeometryBounds(
        ParametricRcSectionDefinition definition)
    {
        double width = definition.WidthM;
        double height = definition.HeightM;
        var points = definition.Shape switch
        {
            ParametricRcShape.Rectangle => TemplatePoints.RectPoints(width, height),
            ParametricRcShape.Tee => TemplatePoints.TeePoints(width, height,
                definition.WebThicknessM, definition.FlangeThicknessM),
            ParametricRcShape.IBeam => TemplatePoints.IBeamPoints(height, width,
                definition.WebThicknessM, definition.FlangeThicknessM),
            ParametricRcShape.Circle or ParametricRcShape.Annulus => TemplatePoints.CirclePoints(width),
            _ => []
        };
        return (points.Min(p => p.X), points.Max(p => p.X), points.Min(p => p.Y), points.Max(p => p.Y));
    }

    void LoadLayer(ParametricLongitudinalLayer? layer, bool lower)
    {
        bool enabled = layer?.Enabled == true;
        bool idealized = layer?.IsIdealized == true;
        if (lower)
        {
            LowerRebarEnabled = enabled;
            LowerRebarIdealized = idealized;
            LowerRebarCount = layer?.Count ?? LowerRebarCount;
            LowerRebarDiameterMm = (layer?.DiameterM ?? LowerRebarDiameterMm / 1000.0) * 1000.0;
            LowerRebarAreaMm2 = (layer?.AreaM2 ?? LowerRebarAreaMm2 / 1_000_000.0) * 1_000_000.0;
            LowerRebarAxis = layer?.Axis ?? LowerRebarAxis;
            LowerRebarOffsetMm = layer is null
                ? LowerRebarOffsetMm
                : LayerOffsetMm(layer.CoordinateM, true, idealized, LowerRebarAxis);
        }
        else
        {
            UpperRebarEnabled = enabled;
            UpperRebarIdealized = idealized;
            UpperRebarCount = layer?.Count ?? UpperRebarCount;
            UpperRebarDiameterMm = (layer?.DiameterM ?? UpperRebarDiameterMm / 1000.0) * 1000.0;
            UpperRebarAreaMm2 = (layer?.AreaM2 ?? UpperRebarAreaMm2 / 1_000_000.0) * 1_000_000.0;
            UpperRebarAxis = layer?.Axis ?? UpperRebarAxis;
            UpperRebarOffsetMm = layer is null
                ? UpperRebarOffsetMm
                : LayerOffsetMm(layer.CoordinateM, false, idealized, UpperRebarAxis);
        }
    }

    double LayerOffsetMm(double coordinateM, bool lower, bool idealized, IdealizedRebarAxis axis)
    {
        var definition = Shape switch
        {
            ParametricRcShape.Rectangle => ParametricRcSectionDefinition.Rectangle(WidthMm / 1000, HeightMm / 1000),
            ParametricRcShape.Tee => ParametricRcSectionDefinition.Tee(WidthMm / 1000, HeightMm / 1000, WebMm / 1000, FlangeMm / 1000),
            ParametricRcShape.IBeam => ParametricRcSectionDefinition.IBeam(WidthMm / 1000, HeightMm / 1000, WebMm / 1000, FlangeMm / 1000),
            ParametricRcShape.Circle => ParametricRcSectionDefinition.Circle(WidthMm / 1000),
            ParametricRcShape.Annulus => ParametricRcSectionDefinition.Annulus(WidthMm / 1000, InnerDiameterMm / 1000),
            _ => throw new ArgumentOutOfRangeException()
        };
        var bounds = GetGeometryBounds(definition);
        bool normalToX = idealized && axis == IdealizedRebarAxis.My;
        double face = normalToX
            ? (lower ? bounds.MinX : bounds.MaxX)
            : (lower ? bounds.MinY : bounds.MaxY);
        return Math.Abs((coordinateM - face) * 1000.0);
    }

    IReadOnlyList<string> MaterialDiagnostics()
    {
        var diagnostics = new List<string>();
        if (string.IsNullOrWhiteSpace(Tag))
            diagnostics.Add(Loc.S("ParametricRcMissingTag"));
        if (!_materialsConfigured) return diagnostics;
        if (ConcreteMaterialId <= 0 || !ConcreteMaterials.Any(m => m.Id == ConcreteMaterialId))
            diagnostics.Add(Loc.S("ParametricRcMissingConcreteMaterial"));
        if (LongitudinalMaterialId <= 0 || !ReinforcementMaterials.Any(m => m.Id == LongitudinalMaterialId))
            diagnostics.Add(Loc.S("ParametricRcMissingLongitudinalMaterial"));
        if (IsStirrupFieldsEnabled &&
            (StirrupCount <= 0 || StirrupMaterialId <= 0 || !ReinforcementMaterials.Any(m => m.Id == StirrupMaterialId)))
            diagnostics.Add(Loc.S("ParametricRcMissingStirrupMaterial"));
        return diagnostics;
    }

    /// <summary>Обновляет preview и зависимые показатели.</summary>
    public void RefreshPreview()
    {
        UpdateDefaultTag();
        try { Preview = ParametricRcSectionGenerator.Generate(BuildDefinition()); }
        catch (Exception ex) { Preview = new(new CrossSection(), [ex.Message]); }
        OnPropertyChanged(nameof(Preview));
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(Diagnostics));
        OnPropertyChanged(nameof(DiagnosticsText));
        OnPropertyChanged(nameof(Asw));
        OnPropertyChanged(nameof(Qsw));
        OnPropertyChanged(nameof(StirrupStepWarning));
        OnPropertyChanged(nameof(ShowWebFields));
        OnPropertyChanged(nameof(ShowLayerRebarFields));
        OnPropertyChanged(nameof(IsHeightEnabled));
        OnPropertyChanged(nameof(ShowInnerDiameter));
        OnPropertyChanged(nameof(CanUseStirrups));
        OnPropertyChanged(nameof(IsStirrupFieldsEnabled));
        OnPropertyChanged(nameof(ShowPolarFields));
        OnPropertyChanged(nameof(ShowTeeApplicability));
        OnPropertyChanged(nameof(ShowRoundApplicability));
    }

    void UpdateDefaultTag()
    {
        if (_tagCustomized) return;
        string generated = BuildDefaultTag();
        if (string.Equals(_tag, generated, StringComparison.Ordinal)) return;
        _tag = generated;
        OnPropertyChanged(nameof(Tag));
    }

    string BuildDefaultTag() => Shape switch
    {
        ParametricRcShape.Rectangle => FormatTag("ParametricRcTagRectangleFormat",
            Loc.S("ParametricRcShapeRectangle"), Number(WidthMm), Number(HeightMm)),
        ParametricRcShape.Tee => FormatTag("ParametricRcTagTeeFormat",
            Loc.S("ParametricRcShapeTee"), Number(WidthMm), Number(HeightMm), Number(WebMm), Number(FlangeMm)),
        ParametricRcShape.IBeam => FormatTag("ParametricRcTagIBeamFormat",
            Loc.S("ParametricRcShapeIBeam"), Number(WidthMm), Number(HeightMm), Number(WebMm), Number(FlangeMm)),
        ParametricRcShape.Circle => FormatTag("ParametricRcTagCircleFormat",
            Loc.S("ParametricRcShapeCircle"), Number(WidthMm)),
        ParametricRcShape.Annulus => FormatTag("ParametricRcTagAnnulusFormat",
            Loc.S("ParametricRcShapeAnnulus"), Number(WidthMm), Number(InnerDiameterMm)),
        _ => ""
    };

    static string FormatTag(string resourceKey, params object[] args)
    {
        string format = Loc.S(resourceKey);
        return format == resourceKey
            ? string.Join(" ", args)
            : string.Format(CultureInfo.CurrentCulture, format, args);
    }

    static string Number(double value) => value.ToString("G5", CultureInfo.CurrentCulture);
}
