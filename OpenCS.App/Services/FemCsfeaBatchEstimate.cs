using System.Globalization;
using CSfea.CScoreBridge.Structural;

namespace OpenCS.Services;

/// <summary>
/// Оценка объёма полей конечного состояния одного варианта пакетного расчёта CSfea в БД
/// (<see cref="FemCsfeaStepFieldsCodec"/>, GZip): по записи линейного расчёта «Мирной» (27 648 узлов, 27 535 оболочек —
/// ≈ 3,5 МБ) с запасом на состояния пластин нелинейного расчёта; блоки жёсткостей (всегда), деформаций сечений и
/// полных секущих матриц конечного состояния — по числу float на КЭ с поправкой на сжатие. Только для подсказки в
/// диалоге.
/// </summary>
public static class FemCsfeaBatchEstimate
{
    const double BytesPerNode = 56, BytesPerShell = 110, BytesPerBeam = 110, Float = 3.5, Double = 7;

    /// <param name="strains">С деформациями сечений (<see cref="Tasks.FemCsfeaParams.RecordSectionStrains"/>).</param>
    /// <param name="matrices">С полными секущими матрицами в double (конечное состояние прогона — каждый вариант пакета).</param>
    public static double FieldBytes(int nodes, int shells, int beams, bool strains = false, bool matrices = false)
    {
        int shellFloats = RcSecantStepFields.ShellStiffnessComponents + (strains ? RcSecantStepFields.ShellStrainComponents : 0);
        int beamFloats = RcSecantStepFields.BeamStiffnessComponents + (strains ? RcSecantStepFields.BeamStrainComponents : 0);
        int shellDoubles = matrices ? RcSecantStepFields.ShellMatrixComponents : 0;
        int beamDoubles = matrices ? RcSecantStepFields.BeamMatrixComponents + RcSecantStepFields.BeamElementMatrixComponents : 0;
        return nodes * BytesPerNode + shells * (BytesPerShell + Float * shellFloats + Double * shellDoubles)
            + beams * (BytesPerBeam + Float * beamFloats + Double * beamDoubles);
    }

    /// <summary>«0,6 ГБ», «85 МБ».</summary>
    public static string Format(double bytes) => bytes >= 1e9
        ? string.Format(CultureInfo.CurrentCulture, OpenCS.Utilites.Loc.S("FemCsfeaBatchSizeGb"), bytes / 1e9)
        : string.Format(CultureInfo.CurrentCulture, OpenCS.Utilites.Loc.S("FemCsfeaBatchSizeMb"), Math.Max(1, bytes / 1e6));
}
