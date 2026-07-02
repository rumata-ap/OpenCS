using CScore;

namespace OpenCS.Tasks;

/// <summary>Модуль сдвига для задачи кручения из E материала сечения.</summary>
internal static class TorsionMaterialHelper
{
    /// <summary>ν по умолчанию: бетон 0,2 (СП 63), сталь 0,3.</summary>
    internal static double PoissonRatio(MatType type) => type switch
    {
        MatType.Steel => 0.3,
        MatType.Concrete => 0.2,
        _ => 0.2
    };

    /// <summary>G = E / (2(1+ν)), МПа.</summary>
    internal static double ShearModulusMpa(Material? mat)
    {
        if (mat is not { E: > 0 }) return 0;
        double nu = PoissonRatio(mat.Type);
        return mat.E / (2.0 * (1.0 + nu));
    }

    /// <summary>Базовый материал сечения: крупнейшая бетонная область, иначе первая область.</summary>
    internal static Material? ResolveBaseMaterial(CrossSection section)
    {
        Material? best = null;
        double bestA = 0;
        foreach (var area in section.Areas)
        {
            if (area.Category != AreaCategory.Region || area.Material is not { E: > 0 } mat)
                continue;
            double a = HullAreaMm2(area);
            if (a > bestA) { bestA = a; best = mat; }
        }
        return best ?? section.Areas.FirstOrDefault(a => a.Material is { E: > 0 })?.Material;
    }

    static double HullAreaMm2(MaterialArea area)
    {
        var hull = area.Hull;
        if (hull?.X == null || hull.X.Count < 3) return 0;
        double s = 0;
        int n = hull.X.Count;
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            s += hull.X[i] * hull.Y[j] - hull.X[j] * hull.Y[i];
        }
        return Math.Abs(s) * 0.5;
    }
}
