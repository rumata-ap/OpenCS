using CScore;
using OpenCS.OpenSees.Model;

namespace OpenCS.OpenSees.CScore;

/// <summary>Преобразует диаграмму CScore в кусочно-линейные огибающие OpenSees.</summary>
public static class MaterialDiagramMapper
{
    private const int SmoothSubdivisions = 4;

    /// <summary>
    /// Строит детерминированные положительную и отрицательную огибающие.
    /// Напряжения CScore хранятся в кПа и переводятся в Па.
    /// </summary>
    public static OpenSeesMaterialDefinition Map(
        Diagramm diagram,
        int tag,
        string sourceId,
        MatType sourceType)
    {
        ArgumentNullException.ThrowIfNull(diagram);

        if (tag <= 0)
        {
            throw new CScoreMappingException($"Тег OpenSees материала должен быть положительным: {tag}.");
        }

        if (diagram.Ic is null || diagram.It is null)
        {
            throw new CScoreMappingException("У диаграммы отсутствует ветвь сжатия или растяжения.");
        }

        double[] critical = diagram.GetCriticalStrains();
        SortedSet<double> strains = new(critical);
        strains.AddRange(diagram.Ic.X);
        strains.AddRange(diagram.It.X);

        if (diagram.Type != DiagrammType.Custom)
        {
            double[] ordered = strains.ToArray();
            for (int i = 0; i < ordered.Length - 1; i++)
            {
                double start = ordered[i];
                double end = ordered[i + 1];
                for (int subdivision = 1; subdivision < SmoothSubdivisions; subdivision++)
                    strains.Add(start + (end - start) * subdivision / SmoothSubdivisions);
            }
        }

        List<EnvelopePoint> negative = [];
        List<EnvelopePoint> positive = [];
        foreach (double strain in strains)
        {
            if (!double.IsFinite(strain))
                throw new CScoreMappingException("Диаграмма содержит нечисловую деформацию.");

            double stressKPa = diagram.SigValue(strain, ten: true, ca: true);
            if (!double.IsFinite(stressKPa))
                throw new CScoreMappingException($"Диаграмма содержит нечисловое напряжение при ε={strain:G17}.");

            EnvelopePoint point = new(strain, CScoreUnitConverter.KilopascalsToPascals(stressKPa));
            if (strain <= 0)
                negative.Add(point);
            if (strain >= 0)
                positive.Add(point);
        }

        if (negative.Count == 0 || positive.Count == 0)
        {
            throw new CScoreMappingException("Диаграмма должна иметь обе ветви относительно ε=0.");
        }

        return new OpenSeesMaterialDefinition
        {
            Tag = tag,
            SourceId = sourceId ?? "",
            SourceType = sourceType.ToString(),
            PositiveEnvelope = positive,
            NegativeEnvelope = negative,
            Warnings = ["Сохранена только монотонная огибающая исходной диаграммы."]
        };
    }

    private static void AddRange(this SortedSet<double> target, IEnumerable<double> values)
    {
        foreach (double value in values)
            target.Add(value);
    }
}
