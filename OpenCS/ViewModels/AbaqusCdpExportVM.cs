using System.Collections.ObjectModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Input;
using CScore;
using CScore.Abaqus;
using OpenCS.Services;
using OpenCS.Utilites;

namespace OpenCS.ViewModels;

/// <summary>Подпись элемента выбора в окне экспорта Abaqus CDP.</summary>
public sealed record AbaqusCdpChoice<T>(T Value, string Label);

/// <summary>
/// ViewModel окна подготовки и копирования материала Abaqus: CDP для бетона,
/// *Elastic + *Plastic для стали и арматуры (режим выбирается по типу материала).
/// </summary>
public sealed class AbaqusCdpExportVM : ViewModelBase
{
    readonly ITextClipboardService clipboard;
    double canonicalFractureEnergyNPerMm = 0.0726;
    double canonicalElementLengthMm = 10.0;
    string materialName;
    CalcType selectedCalcType = CalcType.C;
    AbaqusCdpUnitProfile selectedUnitProfile = AbaqusCdpUnitProfile.MpaMmN;
    string customStressScaleText = "0.001";
    string customStressUnitText = "MPa";
    string customLengthUnitText = "mm";
    string customForceUnitText = "N";
    string fractureEnergyText;
    string elementLengthText;
    string dilationAngleText = "35";
    string eccentricityText = "0.1";
    string fb0Fc0Text = "1.16";
    string kcText = "0.667";
    string viscosityText = "0.0001";
    string poissonRatioText = "0.2";
    string initialCompressionStressRatioText = "0.4";
    string compressionEtaMinText = "0.05";
    string keywordText = "";
    string tsvText = "";
    string errorText = "";
    string elasticModulusText = "";
    string warningsText = "";
    bool hasYieldPlateau = true;

    /// <summary>Создаёт ViewModel для выбранного материала.</summary>
    public AbaqusCdpExportVM(Material material, ITextClipboardService clipboard)
    {
        Material = material ?? throw new ArgumentNullException(nameof(material));
        this.clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
        IsSteel = material.Type is MatType.Steel or MatType.ReSteelF or MatType.ReSteelU;
        materialName = string.IsNullOrWhiteSpace(material.Tag)
            ? IsSteel ? "Steel-Plastic" : "Concrete-CDP"
            : material.Tag;
        if (IsSteel)
            poissonRatioText = "0.3";
        SourceText = Loc.S(IsSteel ? "AbaqusSteelSource" : "AbaqusCdpSourceEkb");

        CalcTypes = new ReadOnlyCollection<AbaqusCdpChoice<CalcType>>
        ([
            new(CalcType.C, Loc.S("AbaqusCdpCalcTypeC")),
            new(CalcType.CL, Loc.S("AbaqusCdpCalcTypeCL")),
            new(CalcType.N, Loc.S("AbaqusCdpCalcTypeN")),
            new(CalcType.NL, Loc.S("AbaqusCdpCalcTypeNL"))
        ]);
        UnitProfiles = new ReadOnlyCollection<AbaqusCdpChoice<AbaqusCdpUnitProfile>>
        ([
            new(AbaqusCdpUnitProfile.MpaMmN, Loc.S("AbaqusCdpProfileMpaMmN")),
            new(AbaqusCdpUnitProfile.KpaMKN, Loc.S("AbaqusCdpProfileKpaMKN")),
            new(AbaqusCdpUnitProfile.PaMN, Loc.S("AbaqusCdpProfilePaMN")),
            new(AbaqusCdpUnitProfile.Custom, Loc.S("AbaqusCdpProfileCustom"))
        ]);

        var defaults = AbaqusCdpOptions.ForProfile(
            selectedUnitProfile, canonicalFractureEnergyNPerMm, canonicalElementLengthMm);
        fractureEnergyText = Format(defaults.FractureEnergy);
        elementLengthText = Format(defaults.ElementLength);
        CopyKeywordCommand = new RelayCommand(_ => Copy(keywordText), _ => keywordText.Length > 0);
        CopyTsvCommand = new RelayCommand(_ => Copy(tsvText), _ => tsvText.Length > 0);
        Rebuild();
    }

    /// <summary>Материал OpenCS, из которого строятся кривые.</summary>
    public Material Material { get; }

    /// <summary>Режим стали/арматуры (*Plastic) вместо бетонного CDP.</summary>
    public bool IsSteel { get; }

    /// <summary>Режим бетонного CDP.</summary>
    public bool IsConcrete => !IsSteel;

    /// <summary>Конструкционная сталь: доступен выбор диаграммы СП 16 с площадкой или без.</summary>
    public bool IsStructuralSteel => Material.Type == MatType.Steel;

    /// <summary>Площадка текучести в диаграмме СП 16.</summary>
    public bool HasYieldPlateau
    {
        get => hasYieldPlateau;
        set
        {
            if (hasYieldPlateau == value) return;
            hasYieldPlateau = value;
            OnPropertyChanged();
            Rebuild();
        }
    }

    /// <summary>Предупреждения об упрощениях при переносе диаграммы стали.</summary>
    public string WarningsText { get => warningsText; private set { warningsText = value; OnPropertyChanged(); } }

    /// <summary>Доступные виды расчёта.</summary>
    public IReadOnlyList<AbaqusCdpChoice<CalcType>> CalcTypes { get; }

    /// <summary>Доступные профили единиц.</summary>
    public IReadOnlyList<AbaqusCdpChoice<AbaqusCdpUnitProfile>> UnitProfiles { get; }

    /// <summary>Выбранный вид расчёта.</summary>
    public CalcType SelectedCalcType
    {
        get => selectedCalcType;
        set
        {
            if (selectedCalcType == value) return;
            selectedCalcType = value;
            OnPropertyChanged();
            Rebuild();
        }
    }

    /// <summary>Выбранный профиль единиц Abaqus.</summary>
    public AbaqusCdpUnitProfile SelectedUnitProfile
    {
        get => selectedUnitProfile;
        set
        {
            if (selectedUnitProfile == value) return;
            CaptureCanonicalValues();
            selectedUnitProfile = value;
            OnPropertyChanged();
            ApplyUnitProfileFields();
            Rebuild();
        }
    }

    /// <summary>Имя материала.</summary>
    public string MaterialName
    {
        get => materialName;
        set => SetText(ref materialName, value, nameof(MaterialName));
    }

    /// <summary>Пользовательский масштаб напряжений из кПа OpenCS.</summary>
    public string CustomStressScaleText
    {
        get => customStressScaleText;
        set => SetText(ref customStressScaleText, value, nameof(CustomStressScaleText));
    }

    /// <summary>Пользовательская единица напряжения.</summary>
    public string CustomStressUnitText
    {
        get => customStressUnitText;
        set => SetText(ref customStressUnitText, value, nameof(CustomStressUnitText));
    }

    /// <summary>Пользовательская единица длины.</summary>
    public string CustomLengthUnitText
    {
        get => customLengthUnitText;
        set
        {
            if (customLengthUnitText == value) return;
            customLengthUnitText = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CustomEnergyUnitText));
            Rebuild();
        }
    }

    /// <summary>Пользовательская единица силы.</summary>
    public string CustomForceUnitText
    {
        get => customForceUnitText;
        set
        {
            if (customForceUnitText == value) return;
            customForceUnitText = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CustomEnergyUnitText));
            Rebuild();
        }
    }

    /// <summary>Производная пользовательская единица энергии.</summary>
    public string CustomEnergyUnitText =>
        $"{customForceUnitText}/{customLengthUnitText}";

    /// <summary>Единица напряжения текущего профиля.</summary>
    public string StressUnitText { get; private set; } = "MPa";

    /// <summary>Единица длины текущего профиля.</summary>
    public string LengthUnitText { get; private set; } = "mm";

    /// <summary>Единица силы текущего профиля.</summary>
    public string ForceUnitText { get; private set; } = "N";

    /// <summary>Единица энергии текущего профиля.</summary>
    public string EnergyUnitText { get; private set; } = "N/mm";

    /// <summary>Угол дилатации.</summary>
    public string DilationAngleText { get => dilationAngleText; set => SetNumeric(ref dilationAngleText, value, nameof(DilationAngleText)); }

    /// <summary>Эксцентриситет поверхности текучести.</summary>
    public string EccentricityText { get => eccentricityText; set => SetNumeric(ref eccentricityText, value, nameof(EccentricityText)); }

    /// <summary>Отношение fb0/fc0.</summary>
    public string Fb0Fc0Text { get => fb0Fc0Text; set => SetNumeric(ref fb0Fc0Text, value, nameof(Fb0Fc0Text)); }

    /// <summary>Параметр Kc.</summary>
    public string KcText { get => kcText; set => SetNumeric(ref kcText, value, nameof(KcText)); }

    /// <summary>Вязкостная регуляризация.</summary>
    public string ViscosityText { get => viscosityText; set => SetNumeric(ref viscosityText, value, nameof(ViscosityText)); }

    /// <summary>Коэффициент Пуассона.</summary>
    public string PoissonRatioText { get => poissonRatioText; set => SetNumeric(ref poissonRatioText, value, nameof(PoissonRatioText)); }

    /// <summary>Энергия разрушения.</summary>
    public string FractureEnergyText
    {
        get => fractureEnergyText;
        set => SetNumeric(ref fractureEnergyText, value, nameof(FractureEnergyText));
    }

    /// <summary>Размер конечного элемента.</summary>
    public string ElementLengthText
    {
        get => elementLengthText;
        set => SetNumeric(ref elementLengthText, value, nameof(ElementLengthText));
    }

    /// <summary>Начальный уровень напряжения сжатия.</summary>
    public string InitialCompressionStressRatioText
    {
        get => initialCompressionStressRatioText;
        set => SetNumeric(ref initialCompressionStressRatioText, value, nameof(InitialCompressionStressRatioText));
    }

    /// <summary>Минимальный уровень нисходящей ветви сжатия.</summary>
    public string CompressionEtaMinText
    {
        get => compressionEtaMinText;
        set => SetNumeric(ref compressionEtaMinText, value, nameof(CompressionEtaMinText));
    }

    /// <summary>Готовый keyword-блок Abaqus.</summary>
    public string KeywordText { get => keywordText; private set { keywordText = value; OnPropertyChanged(); } }

    /// <summary>Готовые табличные данные TSV.</summary>
    public string TsvText { get => tsvText; private set { tsvText = value; OnPropertyChanged(); } }

    /// <summary>Сообщение об ошибке входных данных или Clipboard.</summary>
    public string ErrorText { get => errorText; private set { errorText = value; OnPropertyChanged(); } }

    /// <summary>Источник кривых материала.</summary>
    public string SourceText { get; }

    /// <summary>Начальный модуль в единицах выбранного профиля.</summary>
    public string ElasticModulusText { get => elasticModulusText; private set { elasticModulusText = value; OnPropertyChanged(); } }

    /// <summary>Команда копирования keyword.</summary>
    public RelayCommand CopyKeywordCommand { get; }

    /// <summary>Команда копирования TSV.</summary>
    public RelayCommand CopyTsvCommand { get; }

    void Copy(string text)
    {
        try
        {
            clipboard.SetText(text);
            ErrorText = "";
        }
        catch (ExternalException)
        {
            ErrorText = Loc.S("AbaqusCdpCopyFailed");
        }
    }

    void Rebuild()
    {
        if (SelectedUnitProfile != AbaqusCdpUnitProfile.Custom)
            CaptureCanonicalValues();

        try
        {
            var unitSystem = BuildUnitSystem();
            if (IsSteel)
            {
                var data = AbaqusSteelCurveGenerator.Generate(Material, BuildSteelOptions(unitSystem));
                KeywordText = AbaqusSteelKeywordSerializer.ToKeyword(data);
                TsvText = AbaqusSteelKeywordSerializer.ToTsv(data);
                ElasticModulusText = Format(data.ElasticModulus);
                WarningsText = string.Join(Environment.NewLine, data.Warnings);
            }
            else
            {
                var data = AbaqusCdpCurveGenerator.Generate(Material, BuildOptions(unitSystem));
                KeywordText = AbaqusCdpKeywordSerializer.ToKeyword(data);
                TsvText = AbaqusCdpKeywordSerializer.ToTsv(data);
                ElasticModulusText = Format(data.ElasticModulus);
            }
            ErrorText = "";
            UpdateUnitLabels(unitSystem);
        }
        catch (ArgumentException)
        {
            KeywordText = "";
            TsvText = "";
            ElasticModulusText = "";
            WarningsText = "";
            ErrorText = Loc.S("AbaqusCdpInvalidInput");
        }
        CommandManager.InvalidateRequerySuggested();
    }

    AbaqusCdpUnitSystem BuildUnitSystem() => SelectedUnitProfile == AbaqusCdpUnitProfile.Custom
        ? AbaqusCdpUnitSystem.Custom(
            customStressUnitText,
            customLengthUnitText,
            customForceUnitText,
            Parse(CustomStressScaleText, nameof(CustomStressScaleText)))
        : AbaqusCdpUnitSystem.ForProfile(SelectedUnitProfile);

    AbaqusSteelOptions BuildSteelOptions(AbaqusCdpUnitSystem unitSystem) => new()
    {
        CalcType = SelectedCalcType,
        UnitSystem = unitSystem,
        PoissonRatio = Parse(PoissonRatioText, nameof(PoissonRatioText)),
        MaterialName = MaterialName,
        HasYieldPlateau = HasYieldPlateau
    };

    AbaqusCdpOptions BuildOptions(AbaqusCdpUnitSystem unitSystem)
    {
        return new AbaqusCdpOptions
        {
            CalcType = SelectedCalcType,
            UnitSystem = unitSystem,
            DilationAngleDegrees = Parse(DilationAngleText, nameof(DilationAngleText)),
            Eccentricity = Parse(EccentricityText, nameof(EccentricityText)),
            Fb0Fc0 = Parse(Fb0Fc0Text, nameof(Fb0Fc0Text)),
            Kc = Parse(KcText, nameof(KcText)),
            Viscosity = Parse(ViscosityText, nameof(ViscosityText)),
            PoissonRatio = Parse(PoissonRatioText, nameof(PoissonRatioText)),
            FractureEnergy = SelectedUnitProfile == AbaqusCdpUnitProfile.Custom
                ? Parse(FractureEnergyText, nameof(FractureEnergyText))
                : AbaqusCdpOptions.ForProfile(
                    SelectedUnitProfile, canonicalFractureEnergyNPerMm, canonicalElementLengthMm).FractureEnergy,
            ElementLength = SelectedUnitProfile == AbaqusCdpUnitProfile.Custom
                ? Parse(ElementLengthText, nameof(ElementLengthText))
                : AbaqusCdpOptions.ForProfile(
                    SelectedUnitProfile, canonicalFractureEnergyNPerMm, canonicalElementLengthMm).ElementLength,
            InitialCompressionStressRatio = Parse(
                InitialCompressionStressRatioText, nameof(InitialCompressionStressRatioText)),
            CompressionEtaMin = Parse(CompressionEtaMinText, nameof(CompressionEtaMinText)),
            MaterialName = MaterialName
        };
    }

    void CaptureCanonicalValues()
    {
        if (SelectedUnitProfile == AbaqusCdpUnitProfile.Custom)
            return;
        if (TryParse(FractureEnergyText, out double energy) &&
            TryParse(ElementLengthText, out double length))
        {
            var oneUnit = AbaqusCdpOptions.ForProfile(SelectedUnitProfile, 1.0, 1.0);
            canonicalFractureEnergyNPerMm = energy / oneUnit.FractureEnergy;
            canonicalElementLengthMm = length / oneUnit.ElementLength;
        }
    }

    void ApplyUnitProfileFields()
    {
        if (SelectedUnitProfile == AbaqusCdpUnitProfile.Custom)
        {
            var defaults = AbaqusCdpUnitSystem.ForProfile(AbaqusCdpUnitProfile.MpaMmN);
            CustomStressScaleText = Format(defaults.StressScaleFromOpenCsKpa);
            CustomStressUnitText = defaults.StressUnit;
            CustomLengthUnitText = defaults.LengthUnit;
            CustomForceUnitText = defaults.ForceUnit;
            FractureEnergyText = Format(canonicalFractureEnergyNPerMm);
            ElementLengthText = Format(canonicalElementLengthMm);
            return;
        }

        var options = AbaqusCdpOptions.ForProfile(
            SelectedUnitProfile, canonicalFractureEnergyNPerMm, canonicalElementLengthMm);
        fractureEnergyText = Format(options.FractureEnergy);
        elementLengthText = Format(options.ElementLength);
        OnPropertyChanged(nameof(FractureEnergyText));
        OnPropertyChanged(nameof(ElementLengthText));
    }

    void UpdateUnitLabels(AbaqusCdpUnitSystem units)
    {
        StressUnitText = units.StressUnit;
        LengthUnitText = units.LengthUnit;
        ForceUnitText = units.ForceUnit;
        EnergyUnitText = units.EnergyUnit;
        OnPropertyChanged(nameof(StressUnitText));
        OnPropertyChanged(nameof(LengthUnitText));
        OnPropertyChanged(nameof(ForceUnitText));
        OnPropertyChanged(nameof(EnergyUnitText));
    }

    void SetNumeric(ref string field, string value, string propertyName)
    {
        if (field == value) return;
        field = value;
        OnPropertyChanged(propertyName);
        Rebuild();
    }

    void SetText(ref string field, string value, string propertyName)
    {
        if (field == value) return;
        field = value;
        OnPropertyChanged(propertyName);
        Rebuild();
    }

    static double Parse(string text, string parameterName)
    {
        if (!TryParse(text, out double result))
            throw new ArgumentException("Ожидалось конечное числовое значение.", parameterName);
        return result;
    }

    static bool TryParse(string text, out double result) =>
        Pars.ParseAny(text ?? "", out result) && double.IsFinite(result);

    static string Format(double value) => value.ToString("G", CultureInfo.CurrentCulture);
}
