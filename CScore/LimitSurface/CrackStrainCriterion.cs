namespace CScore.LimitSurface;

/// <summary>
/// ПС2, образование нормальных трещин по п. 8.2.14 СП 63: характеристики 2-й группы, бетон работает на растяжение.
/// Контрольные точки — вершины контуров и отверстий бетонных областей: растяжение до εbt,ult по п. 8.1.30
/// (εbt2, при растяжении одного знака — (8.54)), сжатие до εb,ult ((8.53)) — замыкание области. Арматура точек не
/// даёт: при бетоне в пределах εbt,ult стержни внутри контура упругие. Пределы — для полной деформации бетона,
/// включая обжатие преднапряжением.
/// </summary>
public sealed class CrackStrainCriterion : IStrainSurfaceCriterion
{
    /// <summary>Создаёт критерий ПС2; допустимы только характеристики 2-й группы (N, NL).</summary>
    public CrackStrainCriterion(CalcType calc = CalcType.N)
    {
        if (calc is not (CalcType.N or CalcType.NL))
            throw new ArgumentException(
                "Поверхность трещинообразования строится по характеристикам 2-й группы (N, NL).", nameof(calc));
        Calc = calc;
    }

    /// <inheritdoc/>
    public string Name => "ПС2, образование трещин, п. 8.2.14 СП 63";

    /// <inheritdoc/>
    public CalcType Calc { get; }

    /// <inheritdoc/>
    public bool Ten => true;

    /// <inheritdoc/>
    public StrainDomain BuildDomain(CrossSection section)
    {
        ArgumentNullException.ThrowIfNull(section);
        var pts = new List<StrainControlPoint>();
        for (int ai = 0; ai < section.Areas.Count; ai++)
        {
            var area = section.Areas[ai];
            if (!StrainCriterionGeometry.IsConcrete(area)) continue;
            var ch = area.Material!.GetChars(Calc)
                ?? throw new InvalidOperationException($"Область «{area.Tag}»: нет характеристик бетона для {Calc}.");
            if (!(ch.Et0 > 0 && ch.Et2 >= ch.Et0))
                throw new InvalidOperationException(
                    $"Область «{area.Tag}»: нет деформаций εbt0, εbt2 растянутого бетона для {Calc}.");
            foreach (var c in StrainCriterionGeometry.Contours(area))
                foreach (var (x, y) in StrainCriterionGeometry.OpenVertices(c))
                    pts.Add(new StrainControlPoint(x, y, StrainPointKind.Concrete,
                        ch.Ec2, ch.Et2, ch.Ec0, ch.Ec2, ai, ch.Et0));
        }
        return new StrainDomain(pts);
    }
}
