using System.Globalization;

namespace OpenCS.Services;

/// <summary>
/// Оценка объёма полей конечного состояния одного варианта пакетного расчёта CSfea в БД
/// (<see cref="FemCsfeaStepFieldsCodec"/>, GZip): по записи линейного расчёта «Мирной» (27 648 узлов, 27 535 оболочек —
/// ≈ 3,5 МБ) с запасом на состояния пластин нелинейного расчёта. Только для подсказки в диалоге.
/// </summary>
public static class FemCsfeaBatchEstimate
{
    const double BytesPerNode = 56, BytesPerShell = 110, BytesPerBeam = 110;

    public static double FieldBytes(int nodes, int shells, int beams) =>
        nodes * BytesPerNode + shells * BytesPerShell + beams * BytesPerBeam;

    /// <summary>«0,6 ГБ», «85 МБ».</summary>
    public static string Format(double bytes) => bytes >= 1e9
        ? string.Format(CultureInfo.CurrentCulture, OpenCS.Utilites.Loc.S("FemCsfeaBatchSizeGb"), bytes / 1e9)
        : string.Format(CultureInfo.CurrentCulture, OpenCS.Utilites.Loc.S("FemCsfeaBatchSizeMb"), Math.Max(1, bytes / 1e6));
}
