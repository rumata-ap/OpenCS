using CScore;

namespace CScore.Abaqus;

/// <summary>
/// Строит положительные CDP-таблицы сжатия из ЕКБ-диаграммы OpenCS.
/// Формулы кривой адаптированы из ConcreteToCDPCompressionConvertStrategy.cs
/// проекта StructureHelper; правила Abaqus добавлены в OpenCS.
/// </summary>
public static class AbaqusCdpCompressionBuilder
{
    /// <summary>Строит таблицу сжатия для выбранного вида расчёта.</summary>
    public static IReadOnlyList<AbaqusCdpCurvePoint> Build(
        Material material,
        AbaqusCdpOptions options)
    {
        AbaqusCdpValidator.ValidateOptions(options);
        var chars = AbaqusCdpValidator.ValidateMaterial(material, options);
        double elasticModulus = 1.05 * chars.E * options.UnitSystem.StressScaleFromOpenCsKpa;
        return Build(chars, options, elasticModulus);
    }

    /// <summary>Строит таблицу сжатия из характеристик в единицах Abaqus.</summary>
    internal static IReadOnlyList<AbaqusCdpCurvePoint> Build(
        MaterialChars chars,
        AbaqusCdpOptions options,
        double elasticModulus)
    {
        double scale = options.UnitSystem.StressScaleFromOpenCsKpa;
        double fc = Math.Abs(chars.Fc) * scale;
        if (!double.IsFinite(elasticModulus) || elasticModulus <= 0.0 ||
            !double.IsFinite(fc) || fc <= 0.0)
            throw new ArgumentException("Модуль и прочность после пересчёта должны быть конечными и положительными.",
                nameof(chars));
        var source = chars.DEKB(options.CompressionEtaMin).Ic;
        var raw = source.X.Zip(source.Y, (strain, stress) =>
                (Strain: Math.Abs(strain), Stress: Math.Abs(stress) * scale))
            .Where(point => point.Stress > 0.0 && point.Strain > 0.0)
            .OrderBy(point => point.Strain)
            .ToList();

        if (raw.Count < 2)
            throw new ArgumentException("ЕКБ-диаграмма не содержит достаточного числа точек сжатия.",
                nameof(chars));

        var sourcePoints = new List<(double Strain, double Stress)>();
        foreach (var point in raw)
        {
            if (sourcePoints.Count == 0 ||
                Math.Abs(point.Strain - sourcePoints[^1].Strain) > 1e-14)
                sourcePoints.Add(point);
            else if (point.Stress > sourcePoints[^1].Stress)
                sourcePoints[^1] = point;
        }

        int peakIndex = sourcePoints
            .Select((point, index) => (point, index))
            .MaxBy(item => item.point.Stress).index;
        double peakStress = fc;
        double peakStrain = sourcePoints[peakIndex].Strain;
        if (peakStress <= 0.0 || peakStrain <= 0.0)
            throw new ArgumentException("ЕКБ-диаграмма имеет некорректную вершину.", nameof(chars));

        double onsetStress = options.InitialCompressionStressRatio * peakStress;
        int onsetRight = FindFirstCrossing(sourcePoints, onsetStress, peakIndex);
        var onsetLeftPoint = sourcePoints[Math.Max(0, onsetRight - 1)];
        var onsetRightPoint = sourcePoints[onsetRight];
        double onsetStrain = InterpolateStrain(onsetLeftPoint, onsetRightPoint, onsetStress);

        var selected = new List<(double Strain, double Stress)>
        {
            (onsetStrain, onsetStress)
        };
        for (int i = onsetRight; i <= peakIndex; i++)
            AddUnique(selected, sourcePoints[i]);

        // ЕКБ строится с тем же ηmin, поэтому нисходящая ветвь сама доходит до ηmin·fc;
        // уровень достигается с погрешностью округления, отсюда относительный допуск.
        double targetStress = options.CompressionEtaMin * peakStress;
        int postPeakStart = Math.Max(peakIndex + 1, onsetRight);
        for (int i = postPeakStart; i < sourcePoints.Count; i++)
        {
            AddUnique(selected, sourcePoints[i]);
            if (sourcePoints[i].Stress <= targetStress * (1.0 + 1e-9))
                break;
        }

        var result = selected.Select((point, index) =>
        {
            double stress = index == 0 && point.Stress <= onsetStress + 1e-12
                ? onsetStress
                : Math.Min(peakStress, point.Stress);
            double totalStrain = index == 0
                ? stress / elasticModulus
                : point.Strain;
            double elasticStrain = stress / elasticModulus;
            double inelasticStrain = Math.Max(0.0, totalStrain - elasticStrain);
            double damage = totalStrain <= peakStrain + 1e-14
                ? 0.0
                : Math.Clamp(1.0 - stress / peakStress, 0.0, 0.999);
            double plasticStrain = damage <= 0.0
                ? inelasticStrain
                : Math.Max(0.0, inelasticStrain -
                    damage * stress / ((1.0 - damage) * elasticModulus));

            return new AbaqusCdpCurvePoint(
                stress,
                totalStrain,
                inelasticStrain,
                damage,
                plasticStrain,
                elasticStrain);
        }).ToList();

        // У точки начала повреждения полная деформация совпадает с упругой,
        // поэтому явно фиксируем нулевую неупругую деформацию без накопления ошибки.
        result[0] = result[0] with { AbaqusStrain = 0.0, PlasticStrain = 0.0 };
        AbaqusCdpValidator.ValidateCurve(result, nameof(chars));
        return result;
    }

    static int FindFirstCrossing(
        IReadOnlyList<(double Strain, double Stress)> points,
        double stress,
        int peakIndex)
    {
        for (int i = 1; i <= peakIndex; i++)
            if (points[i].Stress >= stress)
                return i;
        return peakIndex;
    }

    static double InterpolateStrain(
        (double Strain, double Stress) left,
        (double Strain, double Stress) right,
        double stress)
    {
        if (Math.Abs(right.Stress - left.Stress) <= 1e-14)
            return left.Strain;
        double t = (stress - left.Stress) / (right.Stress - left.Stress);
        return left.Strain + t * (right.Strain - left.Strain);
    }

    static void AddUnique(
        List<(double Strain, double Stress)> points,
        (double Strain, double Stress) point)
    {
        if (points.Count == 0 || Math.Abs(point.Strain - points[^1].Strain) > 1e-14)
            points.Add(point);
        else if (point.Stress > points[^1].Stress)
            points[^1] = point;
    }
}
