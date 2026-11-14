using CScore.LimitSurface;

namespace CScore;

/// <summary>Диагностика постановки задач предельных усилий (limit_*).</summary>
public static class LimitForceDiagnostics
{
    /// <summary>Допуск на «нет сжимающей силы», кН.</summary>
    const double NTolerance = 1e-9;

    /// <summary>
    /// Предупреждение для сечения, которому нечем воспринимать растяжение: в расчёте только бетон
    /// (арматуры и стали нет), а растяжение бетона в этом виде расчёта отключено. Тогда при изгибе
    /// или растяжении без сжимающей силы предельное усилие выходит численным нулём и не характеризует
    /// несущую способность. При сжимающей силе (N &lt; 0) расчёт без растянутого бетона — штатная
    /// постановка п. 7.1.3 СП 63 для внецентренного сжатия, предупреждения нет.
    /// </summary>
    /// <param name="section">Сечение.</param>
    /// <param name="concreteTension">Учитывается ли растяжение бетона в этом виде расчёта.</param>
    /// <param name="nTarget">Целевая продольная сила, кН (сжатие — минус).</param>
    public static bool NoTensionResistance(CrossSection section, bool concreteTension, double nTarget)
        => !concreteTension && nTarget >= -NTolerance && !ResistsTensionWithoutConcrete(section);

    /// <summary>Есть ли в сечении расчётная область не из бетона (арматура, сталь), способная к растяжению.</summary>
    public static bool ResistsTensionWithoutConcrete(CrossSection section)
        => section.Areas.Any(a => MaterialArea.IsCalcActive(a)
                                  && a.Material is not null
                                  && !StrainCriterionGeometry.IsConcrete(a)
                                  && (a.Category == AreaCategory.Region || a.Fibers.Count > 0));

    /// <summary>Текст предупреждения <see cref="NoTensionResistance"/> для сводки и отчёта.</summary>
    public const string NoTensionResistanceWarning =
        "Сечению нечем воспринимать растяжение: в нём нет арматуры, а работа бетона на растяжение при " +
        "расчётах по 1-й ГПС отключена в настройках. Без сжимающей силы предельное усилие получается " +
        "практически нулевым и не характеризует несущую способность. Бетонный элемент на изгиб по СП 63 " +
        "рассчитывают с учётом растянутого бетона (пп. 7.1.3, 7.1.12): включите «Учитывать работу бетона " +
        "на растяжение при расчётах по 1-й ГПС» (Настройки → Расчёт) или используйте задачу «Нормальное " +
        "сечение (упрощённая проверка)» с типом элемента «Бетонный».";
}
