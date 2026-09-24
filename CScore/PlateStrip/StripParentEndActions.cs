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
