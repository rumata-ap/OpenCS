namespace OpenCS.ViewModels;

/// <summary>Величина эпюры результата нелинейного прогона полосы.</summary>
public enum StripRunDiagramQuantity
{
    /// <summary>Изгибающий момент My, кН·м.</summary>
    My,
    /// <summary>Продольная сила N, кН.</summary>
    N,
    /// <summary>Момент в плоскости плиты Mz, кН·м.</summary>
    Mz,
    /// <summary>Прогиб w, мм.</summary>
    W
}

/// <summary>
/// Ряды эпюр результата нелинейного прогона полосы по станциям. Ординаты откладываются со стороны растянутых
/// волокон: у My положительное значение растягивает грань +Z полосы, поэтому при нормали полосы, направленной вниз
/// (<paramref name="flip"/>), знак меняется — «вверх» на эпюре всегда вверх в проекте. Прогиб w меняет знак
/// по тем же соображениям (w положителен по +Z полосы).
/// </summary>
public static class StripRunDiagramBuilder
{
    /// <summary>Ряд эпюры; пустой, если данных нет или длины рядов не сходятся.</summary>
    /// <param name="stationFractions">Станции, доли пролёта.</param>
    /// <param name="lengthM">Длина полосы, м.</param>
    /// <param name="resultants">Усилия [N, My, Mz] по станциям, кН и кН·м.</param>
    /// <param name="deflections">Прогибы по станциям, м.</param>
    /// <param name="flip">Нормаль полосы направлена вниз — My и w откладываются с обратным знаком.</param>
    public static FemDiagramSeries Build(
        IReadOnlyList<double> stationFractions, double lengthM,
        IReadOnlyList<double[]>? resultants, IReadOnlyList<double>? deflections,
        StripRunDiagramQuantity quantity, bool flip = false)
    {
        int count = stationFractions.Count;
        if (count < 2 || !(lengthM > 0.0)) return FemDiagramSeries.Empty;

        double Value(int i)
        {
            switch (quantity)
            {
                case StripRunDiagramQuantity.N: return resultants![i][0];
                case StripRunDiagramQuantity.My: return Sign(flip) * resultants![i][1];
                case StripRunDiagramQuantity.Mz: return resultants![i][2];
                default: return Sign(flip) * deflections![i] * 1000.0;
            }
        }

        bool needResultants = quantity != StripRunDiagramQuantity.W;
        if (needResultants ? resultants == null || resultants.Count != count
                           : deflections == null || deflections.Count != count)
            return FemDiagramSeries.Empty;
        if (needResultants && resultants!.Any(r => r.Length < 3)) return FemDiagramSeries.Empty;

        var segments = new List<FemDiagramSegment>(count - 1);
        for (int i = 0; i + 1 < count; i++)
        {
            double v0 = Value(i), v1 = Value(i + 1);
            if (!double.IsFinite(v0) || !double.IsFinite(v1)) continue;
            segments.Add(new FemDiagramSegment(
                i + 1, stationFractions[i] * lengthM, stationFractions[i + 1] * lengthM, v0, v1));
        }
        return new FemDiagramSeries(segments);
    }

    /// <summary>Наибольшее по модулю значение ряда (на станциях); null — ряд пуст.</summary>
    public static FemDiagramExtremum? Peak(FemDiagramSeries series) =>
        series.Extrema.Count == 0 ? null : series.Extrema.MaxBy(e => Math.Abs(e.Value));

    static double Sign(bool flip) => flip ? -1.0 : 1.0;
}
