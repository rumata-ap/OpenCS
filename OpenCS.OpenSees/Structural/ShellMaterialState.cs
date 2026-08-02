using OpenCS.OpenSees.Model;

namespace OpenCS.OpenSees.Structural;

/// <summary>Тип material-state позиции внутри смешанной shell/beam-модели.</summary>
public enum ShellMaterialStateLocationKind
{
    /// <summary>Слой LayeredShell в точке интегрирования оболочки.</summary>
    ShellLayer,

    /// <summary>Волокно fiber-сечения нелинейного стержня.</summary>
    BeamFiber
}

/// <summary>Идентичность material state в истории нелинейного расчёта.</summary>
public sealed record RCShellMaterialStateKey
{
    /// <summary>Порядковый номер успешного шага.</summary>
    public int StepIndex { get; }

    /// <summary>Индекс стадии нагружения, начиная с нуля.</summary>
    public int StageIndex { get; }

    /// <summary>Коэффициент нагрузки в момент записи.</summary>
    public double LoadFactor { get; }

    /// <summary>Tag элемента OpenSees.</summary>
    public int ElementTag { get; }

    /// <summary>1-based точка интегрирования OpenSees.</summary>
    public int IntegrationPoint { get; }

    /// <summary>1-based индекс shell layer или 0-based индекс beam fiber.</summary>
    public int LocationIndex { get; }

    /// <summary>Тип material-state позиции.</summary>
    public ShellMaterialStateLocationKind LocationKind { get; }

    /// <summary>Создаёт и проверяет идентичность material state.</summary>
    public RCShellMaterialStateKey(
        int StepIndex,
        int StageIndex,
        double LoadFactor,
        int ElementTag,
        int IntegrationPoint,
        int LocationIndex,
        ShellMaterialStateLocationKind LocationKind)
    {
        if (StepIndex <= 0) throw new ArgumentOutOfRangeException(nameof(StepIndex));
        if (StageIndex < 0) throw new ArgumentOutOfRangeException(nameof(StageIndex));
        if (!double.IsFinite(LoadFactor)) throw new ArgumentOutOfRangeException(nameof(LoadFactor));
        if (ElementTag <= 0) throw new ArgumentOutOfRangeException(nameof(ElementTag));
        if (IntegrationPoint <= 0) throw new ArgumentOutOfRangeException(nameof(IntegrationPoint));
        if (LocationIndex < (LocationKind == ShellMaterialStateLocationKind.ShellLayer ? 1 : 0))
            throw new ArgumentOutOfRangeException(nameof(LocationIndex));

        this.StepIndex = StepIndex;
        this.StageIndex = StageIndex;
        this.LoadFactor = LoadFactor;
        this.ElementTag = ElementTag;
        this.IntegrationPoint = IntegrationPoint;
        this.LocationIndex = LocationIndex;
        this.LocationKind = LocationKind;
    }
}

/// <summary>Состояние пятикомпонентного shell-материала в одном слое.</summary>
public sealed record RCShellLayerState
{
    /// <summary>Идентичность шага, элемента, IP и слоя.</summary>
    public RCShellMaterialStateKey Key { get; }

    /// <summary>Tag материала слоя.</summary>
    public int MaterialTag { get; }

    /// <summary>Назначение слоя.</summary>
    public ShellLayerKind ShellLayerKind { get; }

    /// <summary>Пять компонент shell stress response.</summary>
    public IReadOnlyList<double> Stress { get; }

    /// <summary>Пять компонент shell strain response.</summary>
    public IReadOnlyList<double> Strain { get; }

    /// <summary>Необязательные сырые native response-векторы.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<double>> OptionalResponses { get; }

    /// <summary>Ссылка на полный catalog entry с provenance (sourceId, centerZ, thickness, fingerprint).</summary>
    public ShellLayerStateGroup? CatalogGroup { get; }

    /// <summary>Создаёт состояние shell layer и проверяет размерность response.</summary>
    public RCShellLayerState(
        RCShellMaterialStateKey key,
        int MaterialTag,
        ShellLayerKind ShellLayerKind,
        IReadOnlyList<double> Stress,
        IReadOnlyList<double> Strain,
        IReadOnlyDictionary<string, IReadOnlyList<double>>? OptionalResponses = null,
        ShellLayerStateGroup? CatalogGroup = null)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(Stress);
        ArgumentNullException.ThrowIfNull(Strain);
        if (key.LocationKind != ShellMaterialStateLocationKind.ShellLayer)
            throw new ArgumentException("Ключ состояния не относится к shell layer.", nameof(key));
        if (MaterialTag <= 0) throw new ArgumentOutOfRangeException(nameof(MaterialTag));
        ValidateComponents(Stress, nameof(Stress));
        ValidateComponents(Strain, nameof(Strain));

        this.Key = key;
        this.MaterialTag = MaterialTag;
        this.ShellLayerKind = ShellLayerKind;
        this.Stress = Stress.ToArray();
        this.Strain = Strain.ToArray();
        this.CatalogGroup = CatalogGroup;
        this.OptionalResponses = OptionalResponses is null
            ? new Dictionary<string, IReadOnlyList<double>>()
            : OptionalResponses.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyList<double>)pair.Value.ToArray());
    }

    private static void ValidateComponents(IReadOnlyList<double> values, string name)
    {
        if (values.Count != 5 || values.Any(value => !double.IsFinite(value)))
            throw new ArgumentException("Shell material response должен содержать пять конечных компонент.", name);
    }
}

/// <summary>Скалярное состояние beam fiber в одной точке интегрирования.</summary>
public sealed record RCShellBeamFiberState
{
    /// <summary>Идентичность шага, элемента, IP и fiber.</summary>
    public RCShellMaterialStateKey Key { get; }

    /// <summary>Напряжение волокна, Па.</summary>
    public double StressPa { get; }

    /// <summary>Деформация волокна.</summary>
    public double Strain { get; }

    /// <summary>Создаёт состояние beam fiber.</summary>
    public RCShellBeamFiberState(RCShellMaterialStateKey key, double StressPa, double Strain)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.LocationKind != ShellMaterialStateLocationKind.BeamFiber)
            throw new ArgumentException("Ключ состояния не относится к beam fiber.", nameof(key));
        if (!double.IsFinite(StressPa)) throw new ArgumentOutOfRangeException(nameof(StressPa));
        if (!double.IsFinite(Strain)) throw new ArgumentOutOfRangeException(nameof(Strain));

        this.Key = key;
        this.StressPa = StressPa;
        this.Strain = Strain;
    }
}

/// <summary>Происхождение metadata material-state catalog: v2 — полное, v1 — legacy без provenance.</summary>
public enum ShellStateCatalogProvenanceKind
{
    /// <summary>Catalog v2 с полной identity metadata (MaterialTag, LayerKind, SourceId, ...).</summary>
    V2WithProvenance,

    /// <summary>Legacy v1 без metadata — строгий audit и typed mapping не принимают.</summary>
    V1LegacyMissing
}

/// <summary>Группа shell recorder для одной секции, IP, слоя и response (state_order v2).</summary>
public sealed record ShellLayerStateGroup(
    int IntegrationPoint,
    int LayerIndex,
    string ResponseKind,
    IReadOnlyList<int> ElementTags,
    string FileName,
    int ComponentCount)
{
    /// <summary>Tag слоистой секции OpenSees (v2, обязателен).</summary>
    public int? SectionTag { get; init; }

    /// <summary>Tag материала слоя (v2, обязателен; v1 — null, defaults запрещены).</summary>
    public int? MaterialTag { get; init; }

    /// <summary>Назначение слоя (v2, обязательно; v1 — null).</summary>
    public ShellLayerKind? LayerKind { get; init; }

    /// <summary>Стабильный SourceId исходного слоя (v2).</summary>
    public string? SourceId { get; init; }

    /// <summary>Координата центра слоя, м (v2).</summary>
    public double? CenterZ { get; init; }

    /// <summary>Эквивалентная толщина слоя, м (v2; у арматуры — smeared толщина из As).</summary>
    public double? Thickness { get; init; }

    /// <summary>Fingerprint исходного PlateSection/layout (v2).</summary>
    public string? SectionFingerprint { get; init; }

    /// <summary>Единицы response (v2).</summary>
    public string? Unit { get; init; }
}

/// <summary>Положение beam fiber, описанное в material-state catalog.</summary>
public sealed record ShellBeamFiberLocation(
    int ElementTag,
    int IntegrationPoint,
    int FiberIndex,
    int SectionTag,
    double Y,
    double Z,
    int MaterialTag);

/// <summary>Лёгкий каталог material-state файлов и их колонок.</summary>
public sealed record ShellStateCatalog(
    int Version,
    IReadOnlyList<ShellLayerStateGroup> ShellLayerGroups,
    IReadOnlyList<ShellBeamFiberLocation> BeamFiberLocations,
    IReadOnlyList<string> OptionalResponses)
{
    /// <summary>Происхождение metadata: v2 — полное, v1 — legacy missing.</summary>
    public ShellStateCatalogProvenanceKind ProvenanceKind =>
        Version >= 2
            ? ShellStateCatalogProvenanceKind.V2WithProvenance
            : ShellStateCatalogProvenanceKind.V1LegacyMissing;
}

/// <summary>Политика записи shell-layer и beam-fiber material states.</summary>
public sealed record ShellStateRecordingPolicy
{
    /// <summary>Записывать состояния слоёв LayeredShell.</summary>
    public bool RecordShellLayers { get; init; } = true;

    /// <summary>Записывать состояния волокон nonlinear beam-сечений.</summary>
    public bool RecordBeamFibers { get; init; } = true;

    /// <summary>Выбранные 1-based IP оболочки; null означает все доступные IP.</summary>
    public IReadOnlyList<int>? ShellIntegrationPoints { get; init; }

    /// <summary>Выбранные 1-based IP стержней; null означает все доступные IP.</summary>
    public IReadOnlyList<int>? BeamIntegrationPoints { get; init; }

    /// <summary>Выбранные 0-based индексы fiber; null означает все fibers.</summary>
    public IReadOnlyList<int>? BeamFiberIndices { get; init; }

    /// <summary>Opt-in optional response-имена native shell material (например tangent).
    /// Пустое значение означает, что записываются только обязательные stress/strain.</summary>
    public IReadOnlyList<string>? OptionalResponses { get; init; }

    /// <summary>Проверяет индексы policy.</summary>
    public void Validate()
    {
        ValidatePositive(ShellIntegrationPoints, "ShellIntegrationPoints");
        ValidatePositive(BeamIntegrationPoints, "BeamIntegrationPoints");
        if (BeamFiberIndices is not null && BeamFiberIndices.Any(index => index < 0))
            throw new InvalidOperationException("material-state: индексы beam fibers должны быть неотрицательными.");
    }

    private static void ValidatePositive(IReadOnlyList<int>? values, string name)
    {
        if (values is not null && values.Any(value => value <= 0))
            throw new InvalidOperationException($"material-state: {name} должны быть положительными.");
    }
}
