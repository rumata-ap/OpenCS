using System;
using CScore;

namespace OpenCS.Tasks;

/// <summary>Итог проверки образования трещин центрально-растянутого элемента (ф. 8.117 СП 63).</summary>
/// <param name="NCrc">Усилие образования трещин N_crc по НДМ, кН (0 — не найдено).</param>
/// <param name="Converged">Найдено ли N_crc.</param>
/// <param name="Cracked">N &gt; N_crc — трещины образуются.</param>
/// <param name="Utilization">N / N_crc (null, если N_crc не найдено).</param>
public sealed record CentralTensionCrackingResult(double NCrc, bool Converged, bool Cracked, double? Utilization);

/// <summary>
/// Общий для одиночной и пакетной задачи «Трещинообразование» расчёт N_crc при нагрузке без
/// момента: «момент трещинообразования при заданной N» для неё вырожден, проверяется N &gt; N_crc.
/// </summary>
public static class CentralTensionCracking
{
    /// <summary>
    /// N_crc по лучу λ·(N, 0, 0), если нагрузка — центральное растяжение
    /// (<see cref="CrackingSolver.IsCentralTension"/>), иначе null.
    /// Требует уже построенных диаграмм сечения.
    /// </summary>
    public static CentralTensionCrackingResult? TryRun(CrossSection section, CalcType calcCrc,
        double n, double mx, double my)
    {
        if (!CrackingSolver.IsCentralTension(n, mx, my)) return null;

        // Зона догружения — от ненагруженного состояния (N растёт от нуля вместе с λ).
        var solver = new CrackingSolver(section, calcCrc,
            tensionZone: CrackingSolver.LoadedTensionZone(section, n, 0.0, 0.0, nAtZeroMoment: 0.0));
        var lf = solver.CrackingAxialForce(n);
        if (!lf.Converged || !(lf.N > 0))
            return new CentralTensionCrackingResult(0.0, false, false, null);

        double nCrc = Math.Round(lf.N, 4);
        return new CentralTensionCrackingResult(nCrc, true, n > nCrc, Math.Round(n / nCrc, 4));
    }
}
