namespace CScore.LimitSurface;

/// <summary>
/// Критерий поверхности: какие контрольные точки и пределы образуют область деформаций и какой
/// моделью материалов (CalcType, работа бетона на растяжение) отображать её в усилия.
/// </summary>
public interface IStrainSurfaceCriterion
{
    /// <summary>Название критерия для сообщений.</summary>
    string Name { get; }

    /// <summary>Набор расчётных характеристик для интегрирования.</summary>
    CalcType Calc { get; }

    /// <summary>Учитывать работу бетона на растяжение при интегрировании.</summary>
    bool Ten { get; }

    /// <summary>Строит область деформаций сечения (диаграммы уже построены вызывающим).</summary>
    StrainDomain BuildDomain(CrossSection section);
}
