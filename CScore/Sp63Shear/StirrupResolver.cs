namespace CScore.Sp63Shear;

/// <summary>Характеристики поперечной арматуры для одной плоскости сдвига.</summary>
/// <param name="Qsw">Погонное усилие в хомутах Rsw·Asw/sw, кН/м.</param>
/// <param name="Sw">Расчётный шаг хомутов (максимальный среди групп), м.</param>
/// <param name="Rsw">Расчётное сопротивление поперечной арматуры, кПа.</param>
/// <param name="Asw">Приведённая площадь ветвей, работающих в плоскости сдвига, м².</param>
/// <param name="Warnings">Оговорки для отчёта.</param>
public sealed record StirrupData(
    double Qsw, double Sw, double Rsw, double Asw, IReadOnlyList<string> Warnings);

/// <summary>
/// Приводит замкнутые хомуты сечения к погонному усилию qsw для заданной плоскости сдвига.
/// Вклад ветви пропорционален проекции её длины на направление поперечной силы.
/// </summary>
public static class StirrupResolver
{
    /// <summary>Коэффициент перехода от Rs к Rsw по табл. 6.15 СП 63.13330.</summary>
    public const double RswFactor = 0.8;

    /// <summary>Вычисляет qsw, sw и Rsw по всем группам хомутов сечения.</summary>
    public static StirrupData Resolve(CrossSection section, ShearPlane plane, CalcType calc)
    {
        ArgumentNullException.ThrowIfNull(section);
        var warnings = new List<string>();

        double qsw = 0.0, maxSpacing = 0.0, aswTotal = 0.0, rswReported = 0.0;
        foreach (var area in section.Areas.Where(a => a.Stirrups.Count > 0))
        {
            foreach (var group in area.Stirrups)
            {
                double rsw = ResolveRsw(section, group, calc, warnings);
                double asw = group.Elements.Sum(element => BranchArea(element, plane));
                if (rsw <= 0.0 || asw <= 0.0 || group.SpacingM <= 0.0) continue;

                qsw += rsw * asw / group.SpacingM;
                aswTotal += asw;
                maxSpacing = Math.Max(maxSpacing, group.SpacingM);
                rswReported = Math.Max(rswReported, rsw);
            }
        }

        if (qsw <= 0.0)
            warnings.Add("Поперечная арматура в сечении не задана — расчёт ведётся по одному бетону.");

        return new StirrupData(qsw, maxSpacing, rswReported, aswTotal, warnings);
    }

    /// <summary>
    /// Приведённая площадь ветвей элемента для заданной плоскости сдвига.
    /// Вклад участка пропорционален проекции его длины на направление поперечной силы.
    /// </summary>
    public static double BranchArea(StirrupElement element, ShearPlane plane)
    {
        var x = element.CenterlineContour.X;
        var y = element.CenterlineContour.Y;
        int count = Math.Min(x.Count, y.Count);
        double total = 0.0;
        for (int i = 0; i < count - 1; i++)
        {
            double dx = x[i + 1] - x[i];
            double dy = y[i + 1] - y[i];
            double length = Math.Sqrt(dx * dx + dy * dy);
            if (length < 1e-12) continue;
            double projection = plane == ShearPlane.Vy ? Math.Abs(dy) : Math.Abs(dx);
            total += element.BarAreaM2 * projection / length;
        }
        return total;
    }

    /// <summary>Возвращает вклад элемента сразу в обе плоскости сдвига.</summary>
    public static (double Vy, double Vx) BranchAreas(StirrupElement element) =>
        (BranchArea(element, ShearPlane.Vy), BranchArea(element, ShearPlane.Vx));

    /// <summary>
    /// Расчётное сопротивление поперечной арматуры по материалу группы.
    /// Прочность арматуры берётся из Ft: Ry в CScore заполняется только для стали по СП 16.
    /// </summary>
    static double ResolveRsw(
        CrossSection section, StirrupGroup group, CalcType calc, List<string> warnings)
    {
        var material = section.Areas
            .Select(a => a.Material)
            .FirstOrDefault(m => m is not null && m.Id == group.MaterialId);
        var chars = material?.GetChars(calc);
        if (chars is null)
        {
            warnings.Add(
                $"Материал поперечной арматуры id={group.MaterialId} не найден — группа не учтена.");
            return 0.0;
        }
        return RswFactor * Math.Abs(chars.Ft);
    }
}
