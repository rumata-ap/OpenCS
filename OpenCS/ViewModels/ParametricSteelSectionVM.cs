using System.Collections.ObjectModel;
using System.Globalization;
using CScore;
using CScore.ParametricSteel;
using CScore.Sp16;
using OpenCS.Utilites;

namespace OpenCS.ViewModels;

/// <summary>Вариант выбора с локализованной подписью.</summary>
public sealed record ParametricSteelOption<T>(T Value, string Label)
{
    /// <inheritdoc/>
    public override string ToString() => Label;
}

/// <summary>Порядок профилей в списке сортамента.</summary>
public enum SteelCatalogSort
{
    /// <summary>Порядок стандарта (как в базе сортаментов).</summary>
    Standard,
    /// <summary>По высоте h.</summary>
    H,
    /// <summary>По площади A (масса пропорциональна площади).</summary>
    A,
    /// <summary>По Ix.</summary>
    Ix,
    /// <summary>По Iy.</summary>
    Iy,
    /// <summary>По Wx.</summary>
    Wx,
    /// <summary>По Wy.</summary>
    Wy,
}

/// <summary>Профиль сортамента в выпадающем списке: имя и подсказка со значением параметра сортировки.</summary>
public sealed class SteelCatalogProfileOption(SteelCatalogProfileItem item, int index) : ViewModelBase
{
    string _hint = "";

    /// <summary>Строка сортамента.</summary>
    public SteelCatalogProfileItem Item { get; } = item;
    /// <summary>Позиция в порядке стандарта.</summary>
    public int Index { get; } = index;
    /// <summary>Id строки.</summary>
    public int Id => Item.Id;
    /// <summary>Обозначение профиля.</summary>
    public string Name => Item.Name;
    /// <summary>Значение параметра сортировки с единицами; пусто — порядок стандарта.</summary>
    public string Hint { get => _hint; set { if (_hint == value) return; _hint = value; OnPropertyChanged(); } }

    /// <inheritdoc/>
    public override string ToString() => Name;
}

/// <summary>
/// Модель диалога параметрического МК-сечения: вид, изготовление, сортамент, размеры (мм), ориентация,
/// материал, предпросмотр, геометрические характеристики и применимость таблиц СП 16.
/// </summary>
public sealed class ParametricSteelSectionVM : ViewModelBase
{
    readonly ISteelSortament? _sortament;
    readonly bool _materialsConfigured;
    SteelProfileKind _kind = SteelProfileKind.IBeam;
    SteelFabrication _fabrication = SteelFabrication.Rolled;
    double _hMm, _bMm, _tfMm, _b2Mm, _tf2Mm, _twMm, _r1Mm, _r2Mm, _slopePercent;
    bool _rotated90, _flipped;
    int _materialId;
    string _tag = "";
    bool _tagCustomized;
    bool _applyingCatalog;
    bool _loading;
    SteelCatalogSubtype? _selectedSubtype;
    SteelCatalogProfileOption? _selectedCatalogProfile;
    SteelCatalogSort _catalogSort = s_lastCatalogSort;
    /// <summary>Сортировка, выбранная при последнем открытии диалога (в пределах сеанса).</summary>
    static SteelCatalogSort s_lastCatalogSort = SteelCatalogSort.Standard;
    ParametricSteelCatalogRef? _catalog;
    SteelCatalogEntry? _catalogEntry;

    /// <summary>Стальные (Steel/Custom) материалы проекта.</summary>
    public ObservableCollection<Material> SteelMaterials { get; } = [];
    /// <summary>Допустимые способы изготовления выбранного вида.</summary>
    public ObservableCollection<ParametricSteelOption<SteelFabrication>> FabricationOptions { get; } = [];
    /// <summary>Подтипы сортамента для вида и изготовления.</summary>
    public ObservableCollection<SteelCatalogSubtype> CatalogSubtypes { get; } = [];
    /// <summary>Профили выбранного подтипа в порядке <see cref="CatalogSort"/>.</summary>
    public ObservableCollection<SteelCatalogProfileOption> CatalogProfiles { get; } = [];
    /// <summary>Варианты сортировки сортамента.</summary>
    public IReadOnlyList<ParametricSteelOption<SteelCatalogSort>> CatalogSortOptions { get; } =
        [.. Enum.GetValues<SteelCatalogSort>().Select(s => new ParametricSteelOption<SteelCatalogSort>(s, Loc.S("ParametricSteelSort" + s)))];

    /// <summary>Создаёт модель диалога.</summary>
    /// <param name="steelMaterials">Материалы Steel/Custom; по умолчанию выбирается первый Steel.</param>
    /// <param name="sortament">Источник сортамента; null — только ввод размеров.</param>
    public ParametricSteelSectionVM(IEnumerable<Material>? steelMaterials = null, ISteelSortament? sortament = null)
    {
        _sortament = sortament;
        _materialsConfigured = steelMaterials is not null;
        if (steelMaterials is not null)
            foreach (var material in steelMaterials)
                SteelMaterials.Add(material);
        _materialId = (SteelMaterials.FirstOrDefault(m => m.Type == MatType.Steel) ?? SteelMaterials.FirstOrDefault())?.Id ?? 0;
        _loading = true;
        ApplyKindDefaults();
        _loading = false;
        RefreshPreview();
    }

    // ── Вид, изготовление ─────────────────────────────────────────────

    /// <summary>Вид сечения; смена вида сбрасывает размеры к типовым и снимает ссылку на сортамент.</summary>
    public SteelProfileKind Kind
    {
        get => _kind;
        set
        {
            if (_kind == value) return;
            _kind = value;
            OnPropertyChanged();
            ApplyKindDefaults();
            RefreshPreview();
        }
    }

    /// <summary>Способ изготовления (из допустимых для вида).</summary>
    public SteelFabrication Fabrication
    {
        get => _fabrication;
        set
        {
            if (_fabrication == value) return;
            _fabrication = value;
            OnPropertyChanged();
            DetachCatalog();
            RefreshCatalogSubtypes();
            RefreshPreview();
        }
    }

    // ── Сортамент ─────────────────────────────────────────────────────

    /// <summary>Есть ли сортамент для вида и изготовления.</summary>
    public bool HasCatalog => CatalogSubtypes.Count > 0;

    /// <summary>Выбранный подтип (стандарт) сортамента.</summary>
    public SteelCatalogSubtype? SelectedSubtype
    {
        get => _selectedSubtype;
        set
        {
            if (Equals(_selectedSubtype, value)) return;
            _selectedSubtype = value;
            OnPropertyChanged();
            CatalogProfiles.Clear();
            if (value is not null)
                foreach (var p in Safe(() => _sortament?.GetSteelCatalogProfiles(value.Id)) ?? [])
                    CatalogProfiles.Add(new(p, CatalogProfiles.Count));
            ApplyCatalogSort();
            _selectedCatalogProfile = null;
            OnPropertyChanged(nameof(SelectedCatalogProfile));
        }
    }

    /// <summary>Порядок профилей сортамента; запоминается до конца сеанса.</summary>
    public SteelCatalogSort CatalogSort
    {
        get => _catalogSort;
        set
        {
            if (_catalogSort == value) return;
            _catalogSort = value;
            s_lastCatalogSort = value;
            OnPropertyChanged();
            ApplyCatalogSort();
        }
    }

    /// <summary>Выбранный профиль сортамента; выбор заполняет размеры.</summary>
    public SteelCatalogProfileOption? SelectedCatalogProfile
    {
        get => _selectedCatalogProfile;
        set
        {
            if (Equals(_selectedCatalogProfile, value)) return;
            _selectedCatalogProfile = value;
            OnPropertyChanged();
            if (value is null || SelectedSubtype is null) return;
            var entry = Safe(() => _sortament?.GetSteelCatalogEntry(SelectedSubtype.Id, value.Id));
            if (entry is not null) ApplyCatalogEntry(entry);
        }
    }

    /// <summary>Ссылка на строку сортамента; null — сечение задано размерами.</summary>
    public ParametricSteelCatalogRef? Catalog => _catalog;

    /// <summary>Источник размеров для отображения: профиль сортамента или «по размерам».</summary>
    public string CatalogStatusText => _catalog is null
        ? Loc.S("ParametricSteelByDimensions")
        : Format("ParametricSteelFromCatalogFormat", _catalog.Name, _catalog.Standard);

    // ── Размеры, мм ───────────────────────────────────────────────────

    /// <summary>Высота h (D трубы, d круга, вертикальная полка уголка), мм.</summary>
    public double HMm { get => _hMm; set => SetDimension(ref _hMm, value); }
    /// <summary>Ширина b (верхнего пояса, полки, листа), мм.</summary>
    public double BMm { get => _bMm; set => SetDimension(ref _bMm, value); }
    /// <summary>Толщина (верхнего) пояса tf, мм.</summary>
    public double TfMm { get => _tfMm; set => SetDimension(ref _tfMm, value); }
    /// <summary>Ширина нижнего пояса сварного двутавра, мм.</summary>
    public double B2Mm { get => _b2Mm; set => SetDimension(ref _b2Mm, value); }
    /// <summary>Толщина нижнего пояса сварного двутавра, мм.</summary>
    public double Tf2Mm { get => _tf2Mm; set => SetDimension(ref _tf2Mm, value); }
    /// <summary>Толщина стенки tw (толщина t гнутого профиля, уголка, трубы), мм.</summary>
    public double TwMm { get => _twMm; set => SetDimension(ref _twMm, value); }
    /// <summary>Радиус у стенки/пятки (прокат) или внутренний радиус гиба (гнутый), мм.</summary>
    public double R1Mm { get => _r1Mm; set => SetDimension(ref _r1Mm, value); }
    /// <summary>Радиус закругления пера полки (прокат), мм.</summary>
    public double R2Mm { get => _r2Mm; set => SetDimension(ref _r2Mm, value); }
    /// <summary>Уклон внутренних граней полок, %.</summary>
    public double SlopePercent { get => _slopePercent; set => SetDimension(ref _slopePercent, value); }

    bool Rolled => Fabrication == SteelFabrication.Rolled;
    bool Bent => Fabrication == SteelFabrication.Bent;

    /// <summary>Показывать ширину b.</summary>
    public bool ShowB => Kind is not (SteelProfileKind.Pipe or SteelProfileKind.Round);
    /// <summary>Показывать толщину пояса tf.</summary>
    public bool ShowTf => Kind switch
    {
        SteelProfileKind.IBeam or SteelProfileKind.Tee => true,
        SteelProfileKind.Channel or SteelProfileKind.Box => !Bent,
        _ => false,
    };
    /// <summary>Показывать нижний пояс (сварной двутавр).</summary>
    public bool ShowBottomFlange => Kind == SteelProfileKind.IBeam && Fabrication == SteelFabrication.Welded;
    /// <summary>Показывать толщину стенки / профиля.</summary>
    public bool ShowTw => Kind is not (SteelProfileKind.Rect or SteelProfileKind.Round);
    /// <summary>Показывать радиус R1.</summary>
    public bool ShowR1 => (Rolled && Kind is SteelProfileKind.IBeam or SteelProfileKind.Channel or SteelProfileKind.Angle)
        || (Bent && Kind is SteelProfileKind.Channel or SteelProfileKind.Angle or SteelProfileKind.Box);
    /// <summary>Показывать радиус пера r2.</summary>
    public bool ShowR2 => Rolled && Kind is SteelProfileKind.IBeam or SteelProfileKind.Channel or SteelProfileKind.Angle;
    /// <summary>Показывать уклон полок.</summary>
    public bool ShowSlope => Rolled && Kind is SteelProfileKind.IBeam or SteelProfileKind.Channel;

    /// <summary>Подпись поля h.</summary>
    public string HLabel => Loc.S(Kind switch
    {
        SteelProfileKind.Pipe => "ParametricSteelDiameterMm",
        SteelProfileKind.Round => "ParametricSteelRoundDiameterMm",
        SteelProfileKind.Angle => "ParametricSteelAngleHMm",
        _ => "ParametricSteelHMm",
    });
    /// <summary>Подпись поля b.</summary>
    public string BLabel => Loc.S(Kind switch
    {
        SteelProfileKind.IBeam when ShowBottomFlange => "ParametricSteelBf1Mm",
        SteelProfileKind.IBeam => "ParametricSteelBfMm",
        SteelProfileKind.Angle => "ParametricSteelAngleBMm",
        _ => "ParametricSteelBMm",
    });
    /// <summary>Подпись поля tf.</summary>
    public string TfLabel => Loc.S(ShowBottomFlange ? "ParametricSteelTf1Mm" : "ParametricSteelTfMm");
    /// <summary>Подпись поля tw.</summary>
    public string TwLabel => Loc.S(Bent || Kind is SteelProfileKind.Angle or SteelProfileKind.Pipe
        ? "ParametricSteelTMm" : "ParametricSteelTwMm");
    /// <summary>Подпись поля R1.</summary>
    public string R1Label => Loc.S(Bent ? "ParametricSteelBendRadiusMm"
        : Kind == SteelProfileKind.Angle ? "ParametricSteelAngleR1Mm" : "ParametricSteelR1Mm");

    // ── Ориентация ────────────────────────────────────────────────────

    /// <summary>Поворот на 90° относительно канонического положения.</summary>
    public bool Rotated90
    {
        get => _rotated90;
        set { if (_rotated90 == value) return; _rotated90 = value; OnPropertyChanged(); RefreshPreview(); }
    }
    /// <summary>Зеркальное положение.</summary>
    public bool Flipped
    {
        get => _flipped;
        set { if (_flipped == value) return; _flipped = value; OnPropertyChanged(); RefreshPreview(); }
    }
    /// <summary>Доступен ли поворот.</summary>
    public bool CanRotate => ParametricSteelSectionDefinition.CanRotate(Kind);
    /// <summary>Доступно ли зеркало.</summary>
    public bool CanFlip => ParametricSteelSectionDefinition.CanFlip(Kind);
    /// <summary>Подпись зеркального положения для вида.</summary>
    public string FlipLabel => Loc.S(Kind switch
    {
        SteelProfileKind.Channel => "ParametricSteelFlipChannel",
        SteelProfileKind.Tee => "ParametricSteelFlipTee",
        SteelProfileKind.Angle => "ParametricSteelFlipAngle",
        _ => "ParametricSteelFlip",
    });

    // ── Материал, метка ───────────────────────────────────────────────

    /// <summary>Id стального материала.</summary>
    public int MaterialId
    {
        get => _materialId;
        set { if (_materialId == value) return; _materialId = value; OnPropertyChanged(); RefreshPreview(); }
    }
    /// <summary>Нет ни одного материала Steel/Custom.</summary>
    public bool NoSteelMaterials => _materialsConfigured && SteelMaterials.Count == 0;

    /// <summary>Метка сечения; по умолчанию — имя профиля сортамента или описание профиля.</summary>
    public string Tag
    {
        get => _tag;
        set
        {
            _tag = value ?? "";
            _tagCustomized = true;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Diagnostics));
            OnPropertyChanged(nameof(DiagnosticsText));
            OnPropertyChanged(nameof(CanSave));
        }
    }

    // ── Предпросмотр и характеристики ─────────────────────────────────

    /// <summary>Результат генератора для текущих полей.</summary>
    public ParametricSteelGenerationResult Preview { get; private set; }
        = new(new CrossSection(), null, []);
    /// <summary>Характеристики сформированного контура в осях сечения; null — сечение не построено.</summary>
    public PolygonSection? Polygon { get; private set; }
    /// <summary>Сечение СП 16 с явным профилем; null — сечение не построено.</summary>
    public Sp16Section? Sp16 { get; private set; }

    /// <summary>Площадь, см².</summary>
    public double? AreaCm2 => Polygon?.A * 1e4;
    /// <summary>Площадь по сортаменту и отклонение сформированного контура, текстом; пусто — не из сортамента.</summary>
    public string CatalogAreaText => _catalogEntry?.ACm2 is double a && a > 0 && AreaCm2 is double ac
        ? Format("ParametricSteelCatalogAreaFormat", a.ToString("0.###", CultureInfo.CurrentCulture),
            ((ac / a - 1) * 100).ToString("+0.0;-0.0;0.0", CultureInfo.CurrentCulture))
        : "";
    /// <summary>Момент инерции относительно оси x сечения, см⁴.</summary>
    public double? IxCm4 => Polygon?.Ix * 1e8;
    /// <summary>Момент инерции относительно оси y сечения, см⁴.</summary>
    public double? IyCm4 => Polygon?.Iy * 1e8;
    /// <summary>Наименьший момент сопротивления относительно оси x, см³.</summary>
    public double? WxCm3 => Polygon is { } p ? p.Ix / Math.Max(p.YMax - p.Yc, p.Yc - p.YMin) * 1e6 : null;
    /// <summary>Наименьший момент сопротивления относительно оси y, см³.</summary>
    public double? WyCm3 => Polygon is { } p ? p.Iy / Math.Max(p.XMax - p.Xc, p.Xc - p.XMin) * 1e6 : null;
    /// <summary>Радиус инерции относительно оси x, см.</summary>
    public double? IxRadiusCm => Polygon is { } p ? Math.Sqrt(p.Ix / p.A) * 100 : null;
    /// <summary>Радиус инерции относительно оси y, см.</summary>
    public double? IyRadiusCm => Polygon is { } p ? Math.Sqrt(p.Iy / p.A) * 100 : null;

    /// <summary>Есть ли It для вида (двутавр, швеллер, тавр — прил. Д, прил. Ж).</summary>
    public bool ShowIt => Sp16 is { It: > 0 };
    /// <summary>Момент инерции при свободном кручении, передаваемый в расчёт, см⁴.</summary>
    public double? ItCm4 => Sp16 is { It: > 0 } s ? s.It * 1e8 : null;
    /// <summary>Источник It: сортамент (со значением по прил. Д и отклонением) или формула прил. Д.</summary>
    public string ItSourceText => Sp16 is not { It: > 0 } s ? ""
        : s.ItFromCatalog
            ? Format("ParametricSteelItCatalogFormat", (s.ItFormula * 1e8).ToString("0.##", CultureInfo.CurrentCulture),
                ((s.ItFormula / s.It - 1) * 100).ToString("+0.0;-0.0;0.0", CultureInfo.CurrentCulture))
            : Loc.S("ParametricSteelItFormula");

    /// <summary>Тип сечения по табл. 7 при потере устойчивости относительно оси x сечения.</summary>
    public string CurveXText => CurveText(aboutSectionX: true);
    /// <summary>Тип сечения по табл. 7 относительно оси y сечения.</summary>
    public string CurveYText => CurveText(aboutSectionX: false);
    /// <summary>Тип сечения по табл. Е.1 (пластические коэффициенты).</summary>
    public string TableE1Text => Sp16 is null ? ""
        : Sp16Tables.TableE1(Sp16) is { } e ? Format("ParametricSteelTableE1Format", e.TableType)
        : Loc.S("ParametricSteelNotInTable");
    /// <summary>Проверки местной устойчивости п. 7.3 для вида.</summary>
    public string LocalStabilityText => Sp16 is null ? "" : Loc.S(Kind switch
    {
        SteelProfileKind.IBeam or SteelProfileKind.Channel or SteelProfileKind.Tee => "ParametricSteelLocalWebFlange",
        SteelProfileKind.Box => "ParametricSteelLocalBox",
        SteelProfileKind.Angle => "ParametricSteelLocalAngle",
        SteelProfileKind.Pipe => "ParametricSteelLocalPipe",
        _ => "ParametricSteelLocalNone",
    });

    /// <summary>Описание профиля СП 16, передаваемого в расчёт.</summary>
    public string ProfileText => Preview.Profile?.Describe() ?? "";

    /// <summary>Диагностика текущего ввода.</summary>
    public IReadOnlyList<string> Diagnostics => [.. Preview.Diagnostics, .. OwnDiagnostics()];
    /// <summary>Диагностика одной строкой на сообщение.</summary>
    public string DiagnosticsText => string.Join(Environment.NewLine, Diagnostics);
    /// <summary>Можно ли сохранить.</summary>
    public bool CanSave => Diagnostics.Count == 0;

    // ── Определение ───────────────────────────────────────────────────

    /// <summary>Создаёт исходное описание с переводом мм в м (поля, не относящиеся к виду, — нули).</summary>
    public ParametricSteelSectionDefinition BuildDefinition()
    {
        const double k = 0.001;
        double h = HMm * k, b = ShowB ? BMm * k : 0, tw = ShowTw ? TwMm * k : 0;
        double tf = ShowTf ? TfMm * k : Kind is SteelProfileKind.Angle || Bent ? tw : 0;
        return new ParametricSteelSectionDefinition
        {
            Kind = Kind,
            Fabrication = Fabrication,
            H = h, Bf1 = b, Tf1 = tf, Tw = tw,
            Bf2 = ShowBottomFlange ? B2Mm * k : 0,
            Tf2 = ShowBottomFlange ? Tf2Mm * k : 0,
            R1 = ShowR1 ? R1Mm * k : 0,
            R2 = ShowR2 ? R2Mm * k : 0,
            FlangeSlope = ShowSlope ? SlopePercent / 100 : 0,
            Rotated90 = CanRotate && Rotated90,
            Flipped = CanFlip && Flipped,
            Catalog = _catalog,
            MaterialId = MaterialId,
            Tag = Tag.Trim(),
        };
    }

    /// <summary>Заполняет поля из сохранённого описания; ссылка на сортамент восстанавливается по стандарту и имени.</summary>
    public void LoadDefinition(ParametricSteelSectionDefinition d)
    {
        ArgumentNullException.ThrowIfNull(d);
        _loading = true;
        try
        {
            _kind = d.Kind;
            OnPropertyChanged(nameof(Kind));
            RefreshFabricationOptions();
            _fabrication = d.Fabrication;
            OnPropertyChanged(nameof(Fabrication));
            RefreshCatalogSubtypes();
            _applyingCatalog = true;
            HMm = Mm(d.H); BMm = Mm(d.Bf1); TfMm = Mm(d.Tf1); TwMm = Mm(d.Tw);
            B2Mm = Mm(d.BfBottom); Tf2Mm = Mm(d.TfBottom);
            R1Mm = Mm(d.R1); R2Mm = Mm(d.R2); SlopePercent = Percent(d.FlangeSlope);
            _applyingCatalog = false;
            _rotated90 = d.Rotated90; _flipped = d.Flipped;
            OnPropertyChanged(nameof(Rotated90));
            OnPropertyChanged(nameof(Flipped));
            if (d.MaterialId > 0) _materialId = d.MaterialId;
            OnPropertyChanged(nameof(MaterialId));
            RestoreCatalog(d.Catalog);
            _tag = d.Tag ?? "";
            _tagCustomized = !string.IsNullOrWhiteSpace(_tag);
            OnPropertyChanged(nameof(Tag));
        }
        finally
        {
            _applyingCatalog = false;
            _loading = false;
        }
        RefreshPreview();
        // Метка совпадает с автоматической — продолжаем обновлять её вместе с размерами.
        if (string.Equals(_tag, BuildDefaultTag(), StringComparison.Ordinal)) _tagCustomized = false;
    }

    // ── Внутреннее ────────────────────────────────────────────────────

    void SetDimension(ref double field, double value, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (field.Equals(value)) return;
        field = value;
        OnPropertyChanged(name);
        if (!_applyingCatalog) DetachCatalog();
        if (!_loading && !_applyingCatalog) RefreshPreview();
    }

    void ApplyKindDefaults()
    {
        RefreshFabricationOptions();
        var fabrication = ParametricSteelSectionDefinition.AllowedFabrications(Kind)[0];
        _fabrication = fabrication;
        OnPropertyChanged(nameof(Fabrication));
        var d = Kind switch
        {
            SteelProfileKind.IBeam => ParametricSteelSectionDefinition.RolledIBeam(0.3, 0.15, 0.006, 0.01, 0.013),
            SteelProfileKind.Channel => ParametricSteelSectionDefinition.RolledChannel(0.2, 0.08, 0.0052, 0.009, 0.0095, 0.004),
            SteelProfileKind.Tee => ParametricSteelSectionDefinition.WeldedTee(0.2, 0.15, 0.008, 0.012),
            SteelProfileKind.Angle => ParametricSteelSectionDefinition.RolledAngle(0.1, 0.1, 0.008, 0.012, 0.004),
            SteelProfileKind.Box => ParametricSteelSectionDefinition.BentBox(0.2, 0.1, 0.006, 0.006),
            SteelProfileKind.Pipe => ParametricSteelSectionDefinition.Pipe(0.159, 0.006),
            SteelProfileKind.Rect => ParametricSteelSectionDefinition.Plate(0.2, 0.02),
            _ => ParametricSteelSectionDefinition.RoundBar(0.05),
        };
        bool wasApplying = _applyingCatalog;
        _applyingCatalog = true;
        HMm = Mm(d.H); BMm = Mm(d.Bf1); TfMm = Mm(d.Tf1); TwMm = Mm(d.Tw);
        B2Mm = Mm(d.Bf1); Tf2Mm = Mm(d.Tf1);
        R1Mm = Mm(d.R1); R2Mm = Mm(d.R2); SlopePercent = 0;
        _applyingCatalog = wasApplying;
        if (!CanRotate && _rotated90) { _rotated90 = false; OnPropertyChanged(nameof(Rotated90)); }
        if (!CanFlip && _flipped) { _flipped = false; OnPropertyChanged(nameof(Flipped)); }
        DetachCatalog();
        RefreshCatalogSubtypes();
    }

    void RefreshFabricationOptions()
    {
        FabricationOptions.Clear();
        foreach (var f in ParametricSteelSectionDefinition.AllowedFabrications(Kind))
            FabricationOptions.Add(new(f, Loc.S("ParametricSteelFabrication" + f)));
    }

    void RefreshCatalogSubtypes()
    {
        CatalogSubtypes.Clear();
        foreach (var s in Safe(() => _sortament?.GetSteelCatalogSubtypes(Kind, Fabrication)) ?? [])
            CatalogSubtypes.Add(s);
        _selectedSubtype = null;
        SelectedSubtype = CatalogSubtypes.FirstOrDefault();
        OnPropertyChanged(nameof(HasCatalog));
    }

    void ApplyCatalogEntry(SteelCatalogEntry entry)
    {
        _applyingCatalog = true;
        try
        {
            HMm = Mm(entry.H); BMm = Mm(entry.B); TwMm = Mm(entry.Tw); TfMm = Mm(entry.Tf);
            B2Mm = Mm(entry.B); Tf2Mm = Mm(entry.Tf);
            R1Mm = Mm(entry.R1); R2Mm = Mm(entry.R2); SlopePercent = Percent(entry.FlangeSlope);
        }
        finally { _applyingCatalog = false; }
        _catalog = entry.ToCatalogRef();
        _catalogEntry = entry;
        OnPropertyChanged(nameof(Catalog));
        OnPropertyChanged(nameof(CatalogStatusText));
        RefreshPreview();
    }

    void RestoreCatalog(ParametricSteelCatalogRef? catalog)
    {
        _catalog = catalog;
        _catalogEntry = null;
        if (catalog is not null)
        {
            var subtype = CatalogSubtypes.FirstOrDefault(s => s.Name == catalog.Standard && s.Group == catalog.Group);
            if (subtype is not null)
            {
                SelectedSubtype = subtype;
                var item = CatalogProfiles.FirstOrDefault(p => p.Name == catalog.Name);
                if (item is not null)
                {
                    _selectedCatalogProfile = item;
                    _catalogEntry = Safe(() => _sortament?.GetSteelCatalogEntry(subtype.Id, item.Id));
                    // Сохранения до появления It в ссылке — дополняем справочными данными строки.
                    if (_catalogEntry is not null) _catalog = _catalogEntry.ToCatalogRef();
                    OnPropertyChanged(nameof(SelectedCatalogProfile));
                }
            }
        }
        OnPropertyChanged(nameof(Catalog));
        OnPropertyChanged(nameof(CatalogStatusText));
    }

    /// <summary>
    /// Переставляет профили по <see cref="CatalogSort"/> перемещением элементов (выбор в списке сохраняется)
    /// и обновляет подсказки; профили без значения параметра — в конце, в порядке стандарта.
    /// </summary>
    void ApplyCatalogSort()
    {
        var ordered = CatalogProfiles
            .OrderBy(o => CatalogSort == SteelCatalogSort.Standard ? 0 : SortValue(o.Item) ?? double.MaxValue)
            .ThenBy(o => o.Index).ToList();
        for (int i = 0; i < ordered.Count; i++)
        {
            int current = CatalogProfiles.IndexOf(ordered[i]);
            if (current != i) CatalogProfiles.Move(current, i);
        }
        foreach (var option in CatalogProfiles)
            option.Hint = SortHint(option.Item);
    }

    double? SortValue(SteelCatalogProfileItem p) => CatalogSort switch
    {
        SteelCatalogSort.H => p.HMm,
        SteelCatalogSort.A => p.ACm2,
        SteelCatalogSort.Ix => p.IxCm4,
        SteelCatalogSort.Iy => p.IyCm4,
        SteelCatalogSort.Wx => p.WxCm3,
        SteelCatalogSort.Wy => p.WyCm3,
        _ => null,
    };

    string SortHint(SteelCatalogProfileItem p)
    {
        if (CatalogSort == SteelCatalogSort.Standard || SortValue(p) is not double v) return "";
        static string N(double x, string format) => x.ToString(format, CultureInfo.CurrentCulture);
        // Масса 1 м профиля: A (см²) · 10⁻⁴ м² · 7850 кг/м³ = 0,785·A кг/м.
        return CatalogSort == SteelCatalogSort.A
            ? Format("ParametricSteelSortHintA", N(v, "0.##"), N(0.785 * v, "0.#"))
            : Format("ParametricSteelSortHint" + CatalogSort, N(v, "0.#"));
    }

    void DetachCatalog()
    {
        if (_catalog is null && _selectedCatalogProfile is null) return;
        _catalog = null;
        _catalogEntry = null;
        _selectedCatalogProfile = null;
        OnPropertyChanged(nameof(SelectedCatalogProfile));
        OnPropertyChanged(nameof(Catalog));
        OnPropertyChanged(nameof(CatalogStatusText));
    }

    IEnumerable<string> OwnDiagnostics()
    {
        if (string.IsNullOrWhiteSpace(Tag))
            yield return Loc.S("ParametricSteelMissingTag");
        if (_materialsConfigured && (MaterialId <= 0 || SteelMaterials.All(m => m.Id != MaterialId)))
            yield return Loc.S("ParametricSteelMissingMaterial");
    }

    /// <summary>Перестраивает предпросмотр и зависимые показатели.</summary>
    public void RefreshPreview()
    {
        if (_loading) return;
        try { Preview = ParametricSteelSectionGenerator.Generate(BuildDefinition()); }
        catch (Exception ex) { Preview = new(new CrossSection(), null, [ex.Message]); }
        Polygon = null;
        Sp16 = null;
        if (Preview.Diagnostics.Count == 0 && Preview.Section.Areas.FirstOrDefault() is { Hull: { } hull } area)
        {
            Polygon = new PolygonSection(hull.Points.Select(p => (p.X, p.Y)),
                area.Holes.Select(h => h.Points.Select(p => (p.X, p.Y))));
            // Материал на геометрию и табл. 7/Е.1 не влияет.
            Sp16 = Sp16Section.FromContour(Polygon, Preview.Profile,
                new SteelMaterialProps(1, 1, null, null, SteelMaterialProps.DefaultE));
        }
        UpdateDefaultTag();
        foreach (var name in DependentProperties)
            OnPropertyChanged(name);
    }

    static readonly string[] DependentProperties =
    [
        nameof(Preview), nameof(Polygon), nameof(Sp16), nameof(AreaCm2), nameof(CatalogAreaText),
        nameof(IxCm4), nameof(IyCm4), nameof(WxCm3), nameof(WyCm3), nameof(IxRadiusCm), nameof(IyRadiusCm),
        nameof(ShowIt), nameof(ItCm4), nameof(ItSourceText),
        nameof(CurveXText), nameof(CurveYText), nameof(TableE1Text), nameof(LocalStabilityText), nameof(ProfileText),
        nameof(Diagnostics), nameof(DiagnosticsText), nameof(CanSave),
        nameof(ShowB), nameof(ShowTf), nameof(ShowBottomFlange), nameof(ShowTw), nameof(ShowR1), nameof(ShowR2),
        nameof(ShowSlope), nameof(HLabel), nameof(BLabel), nameof(TfLabel), nameof(TwLabel), nameof(R1Label),
        nameof(CanRotate), nameof(CanFlip), nameof(FlipLabel), nameof(NoSteelMaterials),
    ];

    void UpdateDefaultTag()
    {
        if (_tagCustomized) return;
        string generated = BuildDefaultTag();
        if (string.Equals(_tag, generated, StringComparison.Ordinal)) return;
        _tag = generated;
        OnPropertyChanged(nameof(Tag));
    }

    string BuildDefaultTag()
    {
        string kind = Loc.S("ParametricSteelKind" + Kind);
        if (_catalog is not null) return Format("ParametricSteelTagCatalogFormat", kind, _catalog.Name);
        return Preview.Profile?.Describe() ?? kind;
    }

    string CurveText(bool aboutSectionX)
    {
        if (Sp16 is null) return "";
        // Оси Sp16Section — канонические; при повороте ось x сечения — каноническая y.
        bool canonicalX = aboutSectionX != Sp16.Profile.Rotated90;
        return Sp16.CurveFor(canonicalX) is { } c ? c.ToString() : Loc.S("ParametricSteelNotInTable");
    }

    /// <summary>
    /// Метры в миллиметры для полей ввода с округлением до 10⁻⁶ мм: убирает хвосты двоичного
    /// представления (0,009 м → 9,000000000000002 мм), не меняя значимых цифр размера.
    /// </summary>
    static double Mm(double meters) => Math.Round(meters * 1000, 6);

    /// <summary>Доля в проценты для поля уклона с тем же округлением.</summary>
    static double Percent(double fraction) => Math.Round(fraction * 100, 6);

    static string Format(string key, params object[] args)
    {
        string format = Loc.S(key);
        return format == key ? string.Join(" ", args) : string.Format(CultureInfo.CurrentCulture, format, args);
    }

    static T? Safe<T>(Func<T?> read) where T : class
    {
        try { return read(); }
        catch (Exception) { return null; }
    }
}
