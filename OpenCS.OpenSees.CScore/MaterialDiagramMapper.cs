using CScore;
using OpenCS.OpenSees.Model;

namespace OpenCS.OpenSees.CScore;

/// <summary>Преобразует диаграмму CScore в кусочно-линейные огибающие OpenSees.</summary>
public static class MaterialDiagramMapper
{
    private const int SmoothSubdivisions = 4;

    /// <summary>Доля максимального наклона ветви, которая придаётся хвостовому сегменту огибающей
    /// для устойчивости обращения матрицы гибкости сечения (ElasticMultiLinear в OpenSees
    /// экстраполирует за пределами заданных точек наклоном последнего сегмента; ровно нулевой
    /// наклон делает касательную волокна точно нулевой). Используется только в
    /// <see cref="ApplyResidualTailSlope"/> — для плоских хвостов ветви СЖАТИЯ (плато
    /// раздавливания) и ветви растяжения материалов, отличных от бетона (сталь/арматура). Ветвь
    /// растяжения бетона (<see cref="AppendConcreteTensionRupture"/>,
    /// <see cref="CollapseConcreteTensionBranch"/>) этот нудж намеренно НЕ получает: хвост там
    /// строго горизонтален при нулевом напряжении (сечение реально растрескалось — приоритет
    /// физической корректности распределения напряжений над устойчивостью солвера; см. риск
    /// вырожденной матрицы гибкости в комментариях этих методов).</summary>
    private const double ResidualTailSlopeFraction = 1e-3;

    /// <summary>Относительный шаг деформации от предельной растяжимости бетона до точки обрыва
    /// огибающей в ноль (см. <see cref="AppendConcreteTensionRupture"/>). Должен быть достаточно
    /// мал, чтобы обрыв визуально совпадал с пределом растяжимости, но строго положителен, чтобы
    /// точки огибающей оставались в строго возрастающем порядке по деформации.</summary>
    private const double TensionRuptureStrainStepFraction = 1e-6;

    /// <summary>Ширина эталонного диапазона деформаций (в единицах деформации) для масштабирования
    /// остаточного наклона огибающей ПОСЛЕ обрыва растяжения бетона в ноль — заведомо шире
    /// реалистичного перерасхода деформации после трещинообразования, чтобы даже малая доля
    /// пиковой прочности давала пренебрежимо малое напряжение на реалистичном диапазоне.</summary>
    private const double PostRuptureReferenceStrainSpan = 1.0;

    /// <summary>
    /// Строит детерминированные положительную и отрицательную огибающие.
    /// Напряжения CScore хранятся в кПа и переводятся в Па.
    /// </summary>
    /// <param name="considerConcreteTension">Учитывать ли работу бетона на растяжение (влияет
    /// только на бетон — арматура/сталь всегда работают на растяжение независимо от этого флага,
    /// как и в <see cref="Diagramm.Sig"/>). При <c>false</c> огибающая растяжения бетона
    /// схлопывается в две точки (0,0) и малый ненулевой остаток — сечение считается уже полностью
    /// растрескавшимся. Это альтернатива явному обрыву в ноль (см.
    /// <see cref="AppendConcreteTensionRupture"/>): не требует softening-участка, который на
    /// реальных сценариях оказался численно хрупок для forceBeamColumn (не универсальная ширина
    /// обрыва — см. историю отладки кинематических нагрузок).</param>
    public static OpenSeesMaterialDefinition Map(
        Diagramm diagram,
        int tag,
        string sourceId,
        MatType sourceType,
        bool considerConcreteTension = true)
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

            double stressKPa = diagram.SigValue(strain, ten: considerConcreteTension, ca: true);
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

        List<string> warnings = ["Сохранена только монотонная огибающая исходной диаграммы."];
        // Экстраполируемый край: у positive это последняя (наибольшая ε) точка,
        // у negative — первая (наименьшая, самая отрицательная ε) точка.
        // Бетон на растяжении обрывается в ноль при ε > εbt,ult (см. Diagramm.Sig) — это
        // разрушение сечения (трещина), а не плоский хвост, устраняемый нудж-наклоном; поэтому
        // для бетона огибающая растяжения получает явную точку обрыва вместо нуджа плато.
        if (diagram.MaterialType == MatType.Concrete && !considerConcreteTension)
        {
            CollapseConcreteTensionBranch(positive);
            warnings.Add("Работа бетона на растяжение отключена настройкой расчёта — огибающая растяжения заменена малым остаточным наклоном без прочности (сечение считается уже полностью растрескавшимся).");
        }
        else if (diagram.MaterialType == MatType.Concrete)
        {
            AppendConcreteTensionRupture(positive, diagram.It.X[^1]);
            warnings.Add("К огибающей растяжения добавлен явный обрыв напряжения в ноль на пределе растяжимости бетона (трещинообразование), с малым остаточным наклоном за ним для устойчивости обращения матрицы гибкости.");
        }
        else if (ApplyResidualTailSlope(positive, extrapolateAtStart: false))
        {
            warnings.Add("Хвост огибающей растяжения был строго горизонтальным — добавлен малый ненулевой наклон для устойчивости обращения матрицы гибкости.");
        }
        if (ApplyResidualTailSlope(negative, extrapolateAtStart: true))
            warnings.Add("Хвост огибающей сжатия был строго горизонтальным — добавлен малый ненулевой наклон для устойчивости обращения матрицы гибкости.");

        return new OpenSeesMaterialDefinition
        {
            Tag = tag,
            SourceId = sourceId ?? "",
            SourceType = sourceType.ToString(),
            PositiveEnvelope = positive,
            NegativeEnvelope = negative,
            Warnings = warnings
        };
    }

    /// <summary>Если крайний (экстраполируемый OpenSees за пределами диаграммы) сегмент огибающей
    /// идёт строго горизонтально, слегка сдвигает крайнюю точку так, чтобы наклон стал равен малой
    /// доле характерного наклона ветви. Возвращает true, если правка была применена.</summary>
    private static bool ApplyResidualTailSlope(List<EnvelopePoint> ordered, bool extrapolateAtStart)
    {
        if (ordered.Count < 2) return false;

        double referenceSlope = MaxAbsSlope(ordered);
        if (referenceSlope <= 0) return false;

        int edge = extrapolateAtStart ? 0 : ordered.Count - 1;
        int neighbor = extrapolateAtStart ? 1 : ordered.Count - 2;
        double dStrain = ordered[edge].Strain - ordered[neighbor].Strain;
        if (dStrain == 0) return false;

        double tailSlope = (ordered[edge].StressPa - ordered[neighbor].StressPa) / dStrain;
        if (Math.Abs(tailSlope) > 1e-9 * referenceSlope) return false;

        double nudgedStress = ordered[neighbor].StressPa + referenceSlope * ResidualTailSlopeFraction * dStrain;
        ordered[edge] = ordered[edge] with { StressPa = nudgedStress };
        return true;
    }

    /// <summary>Воспроизводит разрыв растяжения бетона в ноль из <see cref="Diagramm.Sig"/>
    /// (условие <c>eps &gt; It.X[^1]</c>) в огибающей OpenSees, которую <c>MaterialDiagramMapper</c>
    /// иначе никогда не сэмплирует за пределом <paramref name="ultimateTensileStrain"/>. Добавляет
    /// две точки после текущего пика прочности: обрыв в ноль сразу за пределом растяжимости и
    /// СТРОГО ГОРИЗОНТАЛЬНЫЙ хвост при нулевом напряжении на широком эталонном диапазоне
    /// деформаций — полностью растрескавшееся сечение теряет способность воспринимать растяжение
    /// НАВСЕГДА, без искусственного остаточного наклона. Осознанный выбор физической корректности
    /// распределения напряжений над устойчивостью солвера (см. <see cref="ResidualTailSlopeFraction"/>):
    /// нулевой наклон здесь может приводить к <c>ForceBeamColumn3d::update() -- could not invert
    /// flexibility</c> в OpenSees, когда все волокна сечения одновременно попадают на этот
    /// горизонтальный участок — это принято как известный риск, а не проглядели.</summary>
    private static void AppendConcreteTensionRupture(List<EnvelopePoint> positive, double ultimateTensileStrain)
    {
        if (positive.Count == 0) return;

        double strainStep = Math.Max(Math.Abs(ultimateTensileStrain), 1.0) * TensionRuptureStrainStepFraction;
        double ruptureStrain = ultimateTensileStrain + strainStep;
        double farStrain = ruptureStrain + PostRuptureReferenceStrainSpan;

        positive.Add(new EnvelopePoint(ruptureStrain, 0));
        positive.Add(new EnvelopePoint(farStrain, 0));
    }

    /// <summary>Заменяет огибающую растяжения бетона на две точки — (0,0) и (<c>farStrain</c>,0) —
    /// когда работа бетона на растяжение отключена настройкой расчёта
    /// (<c>considerConcreteTension = false</c>): сечение считается уже полностью растрескавшимся,
    /// поэтому напряжение растяжения бетона СТРОГО НОЛЬ на всём диапазоне деформаций, без
    /// остаточного наклона. Осознанный выбор физической корректности распределения напряжений над
    /// устойчивостью солвера — см. <see cref="AppendConcreteTensionRupture"/> и
    /// <see cref="ResidualTailSlopeFraction"/> про тот же риск вырожденной матрицы гибкости в
    /// OpenSees.</summary>
    private static void CollapseConcreteTensionBranch(List<EnvelopePoint> positive)
    {
        positive.Clear();
        positive.Add(new EnvelopePoint(0, 0));
        positive.Add(new EnvelopePoint(PostRuptureReferenceStrainSpan, 0));
    }

    /// <summary>Максимальный по модулю наклон среди сегментов упорядоченной по деформации
    /// огибающей.</summary>
    private static double MaxAbsSlope(IReadOnlyList<EnvelopePoint> ordered)
    {
        double referenceSlope = 0;
        for (int i = 0; i < ordered.Count - 1; i++)
        {
            double dStrain = ordered[i + 1].Strain - ordered[i].Strain;
            if (dStrain == 0) continue;
            double slope = Math.Abs((ordered[i + 1].StressPa - ordered[i].StressPa) / dStrain);
            if (slope > referenceSlope) referenceSlope = slope;
        }
        return referenceSlope;
    }

    private static void AddRange(this SortedSet<double> target, IEnumerable<double> values)
    {
        foreach (double value in values)
            target.Add(value);
    }
}
