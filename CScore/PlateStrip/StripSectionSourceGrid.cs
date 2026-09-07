using CScore.Fem;

namespace CScore.PlateStrip;

/// <summary>
/// Источники плитного отклика полосы, разложенные по элементам производной балки и точкам
/// квадратуры по ширине.
///
/// <b>Измерение — элементы, а не станции.</b> Сечение считается постоянным на элементе
/// (StripBeamElement построен на постоянной D, и двухточечной квадратуры достаточно именно
/// поэтому), поэтому строк ровно <c>stationFractions.Count - 1</c>. Точка резолва раскладки
/// армирования для элемента — середина его пролёта. Резолв (PlateRebarFieldResolver с
/// синтетическими уникальными ElementId) выполняет вызывающая сторона в слое OpenCS — домен
/// остаётся без зависимости от DatabaseService.
///
/// Постоянство сечения на элементе — осознанное упрощение первой нелинейной версии; при
/// заметном градиенте вдоль пролёта выдаётся предупреждение
/// (см. <see cref="SectionGradientDiagnostics"/>).
/// </summary>
public sealed class StripSectionSourceGrid
{
    readonly IReadOnlyList<IReadOnlyList<IPlateSectionResponse>> _rows;

    public StripSectionSourceGrid(IReadOnlyList<IReadOnlyList<IPlateSectionResponse>> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        _rows = rows;
    }

    /// <summary>Число элементов производной балки.</summary>
    public int ElementCount => _rows.Count;

    /// <summary>Число точек квадратуры по ширине; 0 для пустой решётки.</summary>
    public int WidthPointCount => _rows.Count > 0 ? _rows[0]?.Count ?? 0 : 0;

    /// <summary>Источники по ширине для одного элемента.</summary>
    public IReadOnlyList<IPlateSectionResponse> this[int element] => _rows[element];

    /// <summary>Одинаковое сечение на всех элементах — случай однородного армирования и
    /// мост к фикстурам предыдущих срезов.</summary>
    public static StripSectionSourceGrid Uniform(
        IReadOnlyList<IPlateSectionResponse> widthSources, int elementCount)
    {
        ArgumentNullException.ThrowIfNull(widthSources);
        if (elementCount < 1)
            throw new ArgumentOutOfRangeException(nameof(elementCount),
                "Число элементов должно быть положительным.");

        var rows = new IReadOnlyList<IPlateSectionResponse>[elementCount];
        for (int i = 0; i < elementCount; i++)
            rows[i] = widthSources;
        return new(rows);
    }

    /// <summary>Проверить форму решётки. Ошибки формы блокирующие: молча посчитать полосу
    /// с рваной решёткой нельзя.</summary>
    public IReadOnlyList<FemValidationDiagnostic> Validate()
    {
        var diagnostics = new List<FemValidationDiagnostic>();
        if (_rows.Count == 0)
        {
            diagnostics.Add(new("plate_strip_source_grid_shape_mismatch",
                "Решётка источников пуста: нужен хотя бы один элемент."));
            return diagnostics;
        }

        int expected = WidthPointCount;
        if (expected < 1)
        {
            diagnostics.Add(new("plate_strip_source_grid_shape_mismatch",
                "В решётке источников нет ни одной точки квадратуры по ширине."));
            return diagnostics;
        }

        for (int e = 0; e < _rows.Count; e++)
        {
            var row = _rows[e];
            if (row == null || row.Count != expected)
            {
                diagnostics.Add(new("plate_strip_source_grid_shape_mismatch",
                    $"Элемент {e + 1}: ожидалось {expected} источников по ширине, получено " +
                    $"{row?.Count ?? 0}."));
                continue;
            }
            for (int w = 0; w < row.Count; w++)
                if (row[w] == null)
                    diagnostics.Add(new("plate_strip_source_grid_shape_mismatch",
                        $"Элемент {e + 1}, точка ширины {w + 1}: источник не задан."));
        }
        return diagnostics;
    }

    /// <summary>Проверить, что число элементов согласовано со станциями балки.</summary>
    public IReadOnlyList<FemValidationDiagnostic> ValidateAgainstStations(int stationCount)
    {
        if (ElementCount == stationCount - 1)
            return [];
        return
        [
            new("plate_strip_source_grid_shape_mismatch",
                $"Решётка источников описывает {ElementCount} элементов, а станций задано " +
                $"{stationCount} (ожидалось {stationCount - 1} элементов).")
        ];
    }

    /// <summary>Предупредить о заметном градиенте сечения вдоль пролёта: постоянная D на
    /// элементе перестаёт быть оправданной. Сравниваются начальные касательные соседних
    /// элементов в нулевом состоянии.</summary>
    public IReadOnlyList<FemValidationDiagnostic> SectionGradientDiagnostics(
        double widthM, double relativeThreshold = 0.25)
    {
        if (!(relativeThreshold > 0.0) || !double.IsFinite(relativeThreshold))
            throw new ArgumentOutOfRangeException(nameof(relativeThreshold),
                "Порог должен быть конечным и положительным.");
        if (ElementCount < 2)
            return [];

        var diagnostics = new List<FemValidationDiagnostic>();
        var previous = NonlinearStripSection.IntegrateTangent(
            _rows[0], widthM, WidthPointCount, BeamStrainState.Zero);
        for (int e = 1; e < ElementCount; e++)
        {
            var current = NonlinearStripSection.IntegrateTangent(
                _rows[e], widthM, WidthPointCount, BeamStrainState.Zero);
            double worst = 0.0;
            for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
            {
                double scale = Math.Max(Math.Abs(previous[i, j]), Math.Abs(current[i, j]));
                if (scale <= 0.0) continue;
                worst = Math.Max(worst, Math.Abs(current[i, j] - previous[i, j]) / scale);
            }
            if (worst > relativeThreshold)
                diagnostics.Add(new("plate_strip_section_gradient_along_span",
                    $"Сечения элементов {e} и {e + 1} различаются на {worst:P0} — постоянная " +
                    "жёсткость на элементе перестаёт быть оправданной, требуется измельчение.",
                    false));
            previous = current;
        }
        return diagnostics;
    }
}
