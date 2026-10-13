using CScore.Fem;

namespace CScore.PlateStrip;

/// <summary>Вид нагрузки в постановке нелинейного прогона полосы.</summary>
public enum StripNonlinearLoadKind
{
    /// <summary>Равномерная нагрузка Qz, кН/м, на участке [от, до] долей пролёта.</summary>
    UniformQz,
    /// <summary>Сосредоточенная сила Pz, кН, в сечении «от» (доля пролёта).</summary>
    PointPz
}

/// <summary>Одна строка нагрузки нелинейного прогона полосы (оси полосы, Z — из плоскости).</summary>
public sealed class StripNonlinearLoadItem
{
    public StripNonlinearLoadKind Kind { get; set; } = StripNonlinearLoadKind.UniformQz;

    /// <summary>Начало участка (равномерная) либо положение силы (сосредоточенная), доли пролёта.</summary>
    public double FromFraction { get; set; }

    /// <summary>Конец участка (только равномерная), доли пролёта.</summary>
    public double ToFraction { get; set; } = 1.0;

    /// <summary>Интенсивность, кН/м, либо сила, кН. Знак — по оси Z полосы.</summary>
    public double Value { get; set; }

    /// <summary>Перевести в нагрузку балки полосы.</summary>
    public StripLoad ToStripLoad(int index) => Kind == StripNonlinearLoadKind.UniformQz
        ? new StripLoad
        {
            SourceTag = $"run-{index + 1}",
            Kind = StripLoadKind.DistributedUniform,
            StationStartFraction = FromFraction,
            StationEndFraction = ToFraction,
            QzKnM = Value
        }
        : new StripLoad
        {
            SourceTag = $"run-{index + 1}",
            Kind = StripLoadKind.Point,
            StationFraction = FromFraction,
            PzKn = Value
        };
}

/// <summary>Постановка нелинейного прогона полосы на странице эквивалентного сечения (Срез 8d):
/// опоры по концам, нагрузки, число шагов и станций, опция фибровой сверки.</summary>
public sealed class StripNonlinearRunInput
{
    public StripBeamEndCondition StartCondition { get; set; } = StripBeamEndCondition.Pinned;
    public StripBeamEndCondition EndCondition { get; set; } = StripBeamEndCondition.Pinned;
    public StripAxialRestraint AxialRestraint { get; set; } = StripAxialRestraint.StartOnly;
    public List<StripNonlinearLoadItem> Loads { get; set; } = [];

    /// <summary>Число шагов нагружения Ньютона до полной нагрузки.</summary>
    public int LoadSteps { get; set; } = 8;

    /// <summary>Число равных участков пролёта (станций на единицу больше).</summary>
    public int ElementCount { get; set; } = 8;

    /// <summary>Выполнить сверку фибровым стержнем OpenSees.</summary>
    public bool FiberCheck { get; set; }

    public StripBeamSupportScheme ToScheme() => new(StartCondition, EndCondition, AxialRestraint);

    /// <summary>Валидация: пустая строка — допустима; иначе текст первой проблемы.</summary>
    public string? Validate()
    {
        if (Loads.Count == 0) return "Не задано ни одной нагрузки.";
        if (LoadSteps < 1 || LoadSteps > 200) return "Число шагов нагружения должно быть от 1 до 200.";
        if (ElementCount < 2 || ElementCount > 200) return "Число участков пролёта должно быть от 2 до 200.";
        for (int i = 0; i < Loads.Count; i++)
        {
            var l = Loads[i];
            if (!double.IsFinite(l.Value) || !double.IsFinite(l.FromFraction) || !double.IsFinite(l.ToFraction))
                return $"Нагрузка {i + 1}: значения должны быть конечными.";
            if (l.FromFraction < 0.0 || l.FromFraction > 1.0)
                return $"Нагрузка {i + 1}: положение должно быть в долях пролёта [0; 1].";
            if (l.Kind == StripNonlinearLoadKind.UniformQz &&
                (l.ToFraction > 1.0 || l.ToFraction <= l.FromFraction))
                return $"Нагрузка {i + 1}: конец участка должен быть больше начала и не более 1.";
        }
        return null;
    }
}

/// <summary>Сохранённый результат нелинейного прогона полосы (Срез 8d). Привязан к эквивалентному
/// сечению и отпечатку его входов: изменение входов делает результат устаревшим.</summary>
public sealed class StripNonlinearRunResult
{
    public int Id { get; set; }
    public int EquivalentSectionId { get; set; }
    public DateTime CreatedUtc { get; set; }

    /// <summary>Постановка прогона — воспроизводима из результата.</summary>
    public StripNonlinearRunInput Input { get; set; } = new();

    /// <summary>InputFingerprint эквивалентного сечения в момент прогона.</summary>
    public string EquivalentInputFingerprint { get; set; } = "";

    /// <summary>Формулировка балки в прогоне.</summary>
    public StripBeamFormulation Formulation { get; set; } = StripBeamFormulation.EulerBernoulli;

    public bool IsConverged { get; set; }
    public double AchievedLoadFactor { get; set; }
    public int TotalIterations { get; set; }

    public double[] StationFractions { get; set; } = [];
    public double LengthM { get; set; }

    /// <summary>Усилия [N, My, Mz] на станциях, кН и кН·м.</summary>
    public double[][] BeamResultants { get; set; } = [];

    /// <summary>Прогибы w на станциях, м.</summary>
    public double[] BeamDeflections { get; set; } = [];

    public double MaxDeflectionM { get; set; }

    public bool FiberChecked { get; set; }
    public double[][] FiberResultants { get; set; } = [];
    public double[] FiberDeflections { get; set; } = [];
    public double FiberMaxDeflectionM { get; set; }

    /// <summary>Расхождения собственной балки и фибрового стержня; NaN не хранится — при
    /// отсутствии сверки поля равны −1.</summary>
    public double FiberMomentMismatch { get; set; } = -1.0;
    public double FiberDeflectionMismatch { get; set; } = -1.0;

    public List<FemValidationDiagnostic> Diagnostics { get; set; } = [];

    /// <summary>Результат устарел, если входы эквивалентного сечения изменились после прогона.</summary>
    public bool IsStaleFor(EquivalentSection section) =>
        !string.Equals(EquivalentInputFingerprint, section.InputFingerprint, StringComparison.Ordinal);
}
