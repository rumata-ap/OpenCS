using CScore.Fem;

namespace CScore.PlateStrip;

public readonly record struct StripElementNodalLoad(
    double N1, double Vy1, double Vz1, double My1, double Mz1,
    double N2, double Vy2, double Vz2, double My2, double Mz2)
{
    public static StripElementNodalLoad Zero => new();

    public static StripElementNodalLoad operator +(StripElementNodalLoad a, StripElementNodalLoad b) => new(
        a.N1 + b.N1, a.Vy1 + b.Vy1, a.Vz1 + b.Vz1, a.My1 + b.My1, a.Mz1 + b.Mz1,
        a.N2 + b.N2, a.Vy2 + b.Vy2, a.Vz2 + b.Vz2, a.My2 + b.My2, a.Mz2 + b.Mz2);
}

public sealed record StripLoadProjectionResult(
    bool IsCalculable,
    IReadOnlyList<FemValidationDiagnostic> Diagnostics,
    IReadOnlyList<StripElementNodalLoad> Elements,
    double[] TotalForceCheck,
    double[] TotalMomentCheck);

/// <summary>Переносит StripLoadSet на явно заданную дискретизацию балочных элементов через
/// consistent nodal load lumping (Эйлер–Бернулли). См.
/// docs/superpowers/specs/2026-08-13-plate-strip-loads-design.md.</summary>
public static class StripLoadConsistentNodalProjection
{
    public static StripLoadProjectionResult Project(
        StripLoadSet loads,
        double lengthM,
        IReadOnlyList<double> stationFractions,
        double torqueToleranceKnM = 1e-6)
    {
        ArgumentNullException.ThrowIfNull(loads);
        ArgumentNullException.ThrowIfNull(stationFractions);

        var diagnostics = new List<FemValidationDiagnostic>();
        if (!IsValidStationList(stationFractions))
        {
            diagnostics.Add(new("plate_strip_load_invalid_stations",
                "Список станций должен быть отсортирован, содержать не менее 2 значений, " +
                "начинаться с 0.0 и заканчиваться 1.0."));
            return new(false, diagnostics, [], [], []);
        }

        int elementCount = stationFractions.Count - 1;
        var elements = new StripElementNodalLoad[elementCount];

        foreach (StripLoad load in loads.Loads)
        {
            if (load.Kind == StripLoadKind.DistributedUniform)
                AccumulateDistributed(load, lengthM, stationFractions, elements);
        }

        var totalForce = new double[3];
        var totalMoment = new double[3];
        for (int i = 0; i < elementCount; i++)
            AccumulateTotals(elements[i], stationFractions[i] * lengthM, stationFractions[i + 1] * lengthM,
                totalForce, totalMoment);

        return new(true, diagnostics, elements, totalForce, totalMoment);
    }

    static bool IsValidStationList(IReadOnlyList<double> stations)
    {
        if (stations.Count < 2)
            return false;
        if (!double.IsFinite(stations[0]) || Math.Abs(stations[0]) > 1e-12)
            return false;
        if (!double.IsFinite(stations[^1]) || Math.Abs(stations[^1] - 1.0) > 1e-12)
            return false;
        for (int i = 1; i < stations.Count; i++)
        {
            if (!double.IsFinite(stations[i]) || stations[i] <= stations[i - 1])
                return false;
        }
        return true;
    }

    static void AccumulateDistributed(
        StripLoad load, double lengthM, IReadOnlyList<double> stations, StripElementNodalLoad[] elements)
    {
        for (int i = 0; i < elements.Length; i++)
        {
            double le = (stations[i + 1] - stations[i]) * lengthM;
            double n = load.QxKnM * le / 2.0;
            double vy = load.QyKnM * le / 2.0;
            double mz = load.QyKnM * le * le / 12.0;
            double vz = load.QzKnM * le / 2.0;
            double my = load.QzKnM * le * le / 12.0;

            elements[i] += new StripElementNodalLoad(
                n, vy, vz, my, mz,
                n, vy, vz, -my, -mz);
        }
    }

    static void AccumulateTotals(
        StripElementNodalLoad e, double s1, double s2, double[] totalForce, double[] totalMoment)
    {
        totalForce[0] += e.N1 + e.N2;
        totalForce[1] += e.Vy1 + e.Vy2;
        totalForce[2] += e.Vz1 + e.Vz2;

        // Mx всегда 0 — модель не несёт кручения (см. StripLoad, MxKnM блокируется на входе Map).
        totalMoment[1] += e.My1 + e.My2 - s1 * e.Vz1 - s2 * e.Vz2;
        totalMoment[2] += e.Mz1 + e.Mz2 + s1 * e.Vy1 + s2 * e.Vy2;
    }
}
