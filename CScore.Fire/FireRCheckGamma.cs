using CScore;

namespace CScore.Fire;

/// <summary>Расчёт представительных коэффициентов γ для MVP R-проверки.</summary>
public static class FireRCheckGamma
{
    /// <summary>γ_bt по средневзвешенной температуре T3-элементов.</summary>
    public static double EffectiveConcreteGamma(
        FireThermalResult thermal,
        string aggregateType,
        int snapshotIndex = -1)
    {
        int idx = ResolveSnapshotIndex(thermal, snapshotIndex);
        var mesh = thermal.MeshInfo.Mesh;
        double[] tField = thermal.Snapshots[idx];

        double totalArea = 0.0;
        double weighted = 0.0;
        for (int e = 0; e < mesh.Elements.Length; e++)
        {
            int[] tri = mesh.Elements[e];
            double x1 = mesh.X[tri[0]], y1 = mesh.Y[tri[0]];
            double x2 = mesh.X[tri[1]], y2 = mesh.Y[tri[1]];
            double x3 = mesh.X[tri[2]], y3 = mesh.Y[tri[2]];
            double area = Math.Abs(0.5 * ((x2 - x1) * (y3 - y1) - (x3 - x1) * (y2 - y1)));
            double tAvg = (tField[tri[0]] + tField[tri[1]] + tField[tri[2]]) / 3.0;
            totalArea += area;
            weighted += area * tAvg;
        }

        if (totalArea <= 0.0)
            return 1.0;

        double tRep = weighted / totalArea;
        return FireMaterials.GammaBt("", aggregateType, tRep);
    }

    /// <summary>
    /// Минимальный γ_st по стержням сечения — консервативная свёртка для
    /// диагностического MVP-пути. Класс арматуры разрешается для каждого стержня
    /// отдельно: в одном сечении могут стоять разные классы.
    /// </summary>
    public static double EffectiveRebarGammaMin(
        FireThermalResult thermal,
        CrossSection section,
        int snapshotIndex = -1)
    {
        ArgumentNullException.ThrowIfNull(section);

        int idx = ResolveSnapshotIndex(thermal, snapshotIndex);
        var fiber = FireFiberSection.FromThermalResult(thermal, section, idx);

        double min = 1.0;
        foreach (var r in fiber.RebarElements)
            min = Math.Min(min, r.GammaSt);

        return min;
    }

    static int ResolveSnapshotIndex(FireThermalResult thermal, int idx)
    {
        if (thermal.Snapshots.Length == 0)
            throw new InvalidOperationException("В FireThermalResult нет снапшотов.");
        return idx < 0 ? thermal.Snapshots.Length - 1 : idx;
    }
}
