using CSfea.Sparse;

namespace CSfea.Core;

/// <summary>Слой ламината: материал, угол (рад) и толщина.</summary>
public readonly record struct Ply(OrthotropicMaterial Material, double Angle, double Thickness);

/// <summary>
/// Ламинат (или однослойная оболочка) — стопка слоёв снизу вверх.
/// Даёт жёсткости классической теории ламината (A, B, D, A_s).
/// Порт <c>fea/core.py: Laminate</c>.
/// </summary>
public sealed class Laminate
{
    /// <summary>Слои ламината.</summary>
    public IReadOnlyList<Ply> Plies { get; }

    /// <summary>Полная толщина.</summary>
    public double H { get; }

    /// <summary>Коэффициент сдвиговой коррекции (по умолчанию 5/6).</summary>
    public double KShear { get; }

    public Laminate(IEnumerable<Ply> plies, double kShear = 5.0 / 6.0)
    {
        Plies = plies.ToList();
        H = Plies.Sum(p => p.Thickness);
        KShear = kShear;
    }

    /// <summary>Результат интегрирования ламината: A, B, D (3x3) и A_s (2x2).</summary>
    public readonly record struct Abd(double[,] A, double[,] B, double[,] D, double[,] As);

    /// <summary>Возвращает (A, B, D, A_s) ламината (теория классического ламината).</summary>
    public Abd ABDAs()
    {
        double z = -H / 2.0;
        var zs = new List<double> { z };
        foreach (var p in Plies)
        {
            z += p.Thickness;
            zs.Add(z);
        }
        var a = new double[3, 3];
        var b = new double[3, 3];
        var d = new double[3, 3];
        var aSh = new double[2, 2];
        for (int i = 0; i < Plies.Count; i++)
        {
            var ply = Plies[i];
            var qk = ply.Material.QMembraneRotated(ply.Angle);
            var qs = ply.Material.QShearRotated(ply.Angle);
            double z1 = zs[i], z2 = zs[i + 1];
            Dense.AddScaledInPlace(a, qk, z2 - z1);
            Dense.AddScaledInPlace(b, qk, 0.5 * (z2 * z2 - z1 * z1));
            Dense.AddScaledInPlace(d, qk, (z2 * z2 * z2 - z1 * z1 * z1) / 3.0);
            Dense.AddScaledInPlace(aSh, qs, z2 - z1);
        }
        for (int i = 0; i < 2; i++)
            for (int j = 0; j < 2; j++)
                aSh[i, j] *= KShear;
        return new Abd(a, b, d, aSh);
    }

    /// <summary>Поверхностная плотность (ρ·h, просуммированная по слоям).</summary>
    public double DensityTimesH() => Plies.Sum(p => p.Material.Rho * p.Thickness);
}
