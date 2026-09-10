using CScore.Fem;

namespace CScore.Submodel;

/// <summary>Отбраковка непригодных отрезков и вычисление характерного размера набора.</summary>
public static class SegmentInputValidation
{
    /// <summary>Сумма длин, инвариантная к жёсткому движению и расположению компонент.</summary>
    public static double CharacteristicSize(IReadOnlyList<BeamSegmentInput> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);

        double sum = 0.0;
        foreach (var segment in segments)
        {
            if (!segment.IsFinite) continue;
            var length = segment.LengthM;
            if (double.IsFinite(length)) sum += length;
        }
        return sum;
    }

    public static (IReadOnlyList<BeamSegmentInput> Valid, IReadOnlyList<FemValidationDiagnostic> Diagnostics)
        Filter(IReadOnlyList<BeamSegmentInput> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);

        var valid = new List<BeamSegmentInput>();
        var diagnostics = new List<FemValidationDiagnostic>();
        foreach (var segment in segments)
        {
            if (!segment.IsFinite)
            {
                diagnostics.Add(new("chain_input_invalid",
                    $"Элемент {segment.SourceKey}: координаты или угол поворота не являются конечными числами.",
                    true, [segment.SourceKey]));
                continue;
            }

            if (segment.LengthM <= 0.0)
            {
                diagnostics.Add(new("chain_segment_degenerate",
                    $"Элемент {segment.SourceKey} имеет нулевую геометрическую длину.",
                    true, [segment.SourceKey]));
                continue;
            }

            valid.Add(segment);
        }
        return (valid, diagnostics);
    }
}
