using CScore;
using CScore.Fire.Entities;

namespace CScore.Fire;

/// <summary>Фасад R-проверок огнестойкости.</summary>
public static class FireRCheck
{
    /// <summary>Метод по умолчанию — фибровый.</summary>
    public static FireCheckResult Run(
        FireThermalResult thermal,
        CrossSection section,
        double n,
        double mx,
        double my,
        CalcType calc = CalcType.C,
        string method = "fiber",
        int snapshotIndex = -1,
        FireSectionDef? fireDef = null,
        int? thermalResultId = null,
        double sp63EtaMin = 0.85,
        bool rebarDifferentialDiagram = true,
        IReadOnlyList<Diagramm>? diagramPool = null)
        => method == "mvp"
            ? FireRCheckMvp.Run(thermal, section, n, mx, my, calc, snapshotIndex, fireDef, thermalResultId)
            : FireRCheckFiber.Run(thermal, section, n, mx, my, calc, snapshotIndex, fireDef, thermalResultId,
                sp63EtaMin, rebarDifferentialDiagram, diagramPool);
}
