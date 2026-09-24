using CScore.Fem;

namespace CScore.PlateStrip;

/// <summary>Концевые действия полосы из эпюры родителя (спека Среза 8a, блок B).
///
/// Вход — эпюра [N, My, Mz] родителя на станциях 0 и 1 (например, из
/// <c>ShellStripResultantSampler</c>). Её знаки и величины совпадают с конвенцией
/// <see cref="KnownEndActions"/> («значения эпюры на концах»), поэтому перенос идёт без множителей.
/// Берутся только компоненты, отмеченные в <see cref="StripEndDerivation.TransferFromParent"/>:
/// в закреплённом DOF действие — реакция, а не нагрузка.</summary>
public static class StripParentEndActions
{
    /// <summary>Доменный примитив: некорректный вход — <see cref="ArgumentException"/>.</summary>
    public static KnownEndActions From(
        double[] startDiagram, double[] endDiagram, StripSupportDerivationResult derivation)
    {
        if (!TryFrom(startDiagram, endDiagram, derivation, out var actions, out var diagnostics))
            throw new ArgumentException(string.Join(" ", diagnostics.Select(d => d.Message)));
        return actions!;
    }

    /// <summary>Граница с внешними данными: эпюра приходит из сэмплера. Ошибка — ложь и
    /// диагностика <c>plate_strip_parent_end_actions_invalid</c>, без исключения.</summary>
    public static bool TryFrom(
        double[]? startDiagram, double[]? endDiagram, StripSupportDerivationResult derivation,
        out KnownEndActions? actions, out IReadOnlyList<FemValidationDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(derivation);
        var found = new List<FemValidationDiagnostic>();
        diagnostics = found;
        actions = null;

        if (!derivation.IsCalculable || derivation.Start == null || derivation.End == null)
            found.Add(Invalid("автовывод опор не выполнен."));
        Check(startDiagram, "начале", found);
        Check(endDiagram, "конце", found);
        if (found.Count > 0) return false;

        var start = derivation.Start!.TransferFromParent;
        var end = derivation.End!.TransferFromParent;
        actions = new KnownEndActions(
            StartN: Pick(start, StripEndActionComponents.N, startDiagram![0]),
            StartMy: Pick(start, StripEndActionComponents.My, startDiagram[1]),
            StartMz: Pick(start, StripEndActionComponents.Mz, startDiagram[2]),
            EndN: Pick(end, StripEndActionComponents.N, endDiagram![0]),
            EndMy: Pick(end, StripEndActionComponents.My, endDiagram[1]),
            EndMz: Pick(end, StripEndActionComponents.Mz, endDiagram[2]));
        return true;
    }

    /// <summary>Концевые действия подгонкой статики по внутренним станциям эпюры родителя.
    ///
    /// <b>Зачем.</b> Станция на конце полосы лежит на линии опоры, а линия опоры в сетке
    /// родителя — ребро элементов. Усреднённые по элементу усилия относятся к его центру, в
    /// половине элемента от опоры, где момент меньше пикового на V·h/2; прямое снятие эпюры на
    /// конце занижает опорный момент (spike Среза 8a: 17,5 вместо 21,6 кН·м при h = 0,5 м).
    /// Внутренние станции проходят через элементы и точны.
    ///
    /// <b>Как.</b> При свободных концевых DOF эпюра полосы статически определима:
    /// <c>M(ξ) = M₀(ξ) + M_нач·(1−ξ) + M_кон·ξ</c> (то же для Mz), <c>N(ξ) = N₀(ξ) + N_кон</c>, где
    /// M₀, N₀ — эпюра шарнирной балки от нагрузок полосы (от жёсткости не зависит). Неизвестные
    /// находятся наименьшими квадратами по станциям 0 &lt; ξ &lt; 1; затем применяется маска
    /// TransferFromParent. Нужно не менее двух внутренних станций.</summary>
    public static bool TryFit(
        IReadOnlyList<double> stationFractions, IReadOnlyList<double[]> parentDiagram,
        StripLoadSet loads, double lengthM, StripSupportDerivationResult derivation,
        out KnownEndActions? actions, out IReadOnlyList<FemValidationDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(stationFractions);
        ArgumentNullException.ThrowIfNull(parentDiagram);
        ArgumentNullException.ThrowIfNull(loads);
        ArgumentNullException.ThrowIfNull(derivation);
        var found = new List<FemValidationDiagnostic>();
        diagnostics = found;
        actions = null;

        if (!derivation.IsCalculable || derivation.Start == null || derivation.End == null)
            found.Add(Invalid("автовывод опор не выполнен."));
        if (parentDiagram.Count != stationFractions.Count)
            found.Add(Invalid($"эпюра родителя содержит {parentDiagram.Count} станций, ожидалось {stationFractions.Count}."));
        else
            for (int i = 0; i < parentDiagram.Count; i++)
                Check(parentDiagram[i], $"станции {i}", found);
        var interior = Enumerable.Range(0, stationFractions.Count)
            .Where(i => stationFractions[i] > 1e-12 && stationFractions[i] < 1.0 - 1e-12)
            .ToList();
        if (interior.Count < 2)
            found.Add(Invalid("для подгонки нужно не менее двух внутренних станций."));
        if (found.Count > 0) return false;

        // Эпюра шарнирной балки от нагрузок полосы: статически определима, жёсткость — любая.
        var unit = new double[,] { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };
        var determinate = StripBeamModel.Solve(
            unit, lengthM, stationFractions, StripBeamSupportScheme.SimplySupported, loads);
        if (!determinate.IsCalculable)
        {
            found.AddRange(determinate.Diagnostics);
            found.Add(Invalid("не удалось построить эпюру шарнирной балки от нагрузок полосы."));
            return false;
        }
        var baseline = determinate.StationResultants;

        var (startMy, endMy, residualMy) = FitLinear(stationFractions, interior, parentDiagram, baseline, 1);
        var (startMz, endMz, _) = FitLinear(stationFractions, interior, parentDiagram, baseline, 2);
        double endN = interior.Average(i => parentDiagram[i][0] - baseline[i][0]);

        var start = derivation.Start!.TransferFromParent;
        var end = derivation.End!.TransferFromParent;
        actions = new KnownEndActions(
            StartN: 0.0,
            StartMy: Pick(start, StripEndActionComponents.My, startMy),
            StartMz: Pick(start, StripEndActionComponents.Mz, startMz),
            EndN: Pick(end, StripEndActionComponents.N, endN),
            EndMy: Pick(end, StripEndActionComponents.My, endMy),
            EndMz: Pick(end, StripEndActionComponents.Mz, endMz));

        found.Add(new("plate_strip_parent_end_actions_fitted",
            $"Концевые моменты подогнаны по {interior.Count} внутренним станциям эпюры родителя: " +
            $"My нач = {startMy:G5}, My кон = {endMy:G5} кН·м, наибольшая невязка подгонки {residualMy:G3} кН·м.",
            false));
        return true;
    }

    /// <summary>Наименьшие квадраты для r(ξ) = a·(1−ξ) + b·ξ по внутренним станциям, где
    /// r = эпюра родителя − эпюра шарнирной балки. Возвращает a, b и наибольшую невязку.</summary>
    static (double Start, double End, double MaxResidual) FitLinear(
        IReadOnlyList<double> stations, List<int> interior,
        IReadOnlyList<double[]> parent, double[][] baseline, int component)
    {
        double s11 = 0, s12 = 0, s22 = 0, r1 = 0, r2 = 0;
        foreach (int i in interior)
        {
            double xi = stations[i];
            double f1 = 1.0 - xi, f2 = xi;
            double r = parent[i][component] - baseline[i][component];
            s11 += f1 * f1; s12 += f1 * f2; s22 += f2 * f2;
            r1 += f1 * r; r2 += f2 * r;
        }
        double det = s11 * s22 - s12 * s12;
        double a = (r1 * s22 - r2 * s12) / det;
        double b = (s11 * r2 - s12 * r1) / det;
        double worst = interior.Max(i =>
            Math.Abs(parent[i][component] - baseline[i][component] - a * (1.0 - stations[i]) - b * stations[i]));
        return (a, b, worst);
    }

    static void Check(double[]? diagram, string end, List<FemValidationDiagnostic> found)
    {
        if (diagram == null)
            found.Add(Invalid($"эпюра родителя в {end} полосы не задана."));
        else if (diagram.Length != 3)
            found.Add(Invalid($"эпюра родителя в {end} полосы должна иметь три компоненты [N, My, Mz], получено {diagram.Length}."));
        else if (diagram.Any(v => !double.IsFinite(v)))
            found.Add(Invalid($"эпюра родителя в {end} полосы содержит нечисловое значение."));
    }

    static double Pick(StripEndActionComponents mask, StripEndActionComponents component, double value) =>
        mask.HasFlag(component) ? value : 0.0;

    static FemValidationDiagnostic Invalid(string reason) =>
        new("plate_strip_parent_end_actions_invalid", "Концевые действия родителя: " + reason);
}
