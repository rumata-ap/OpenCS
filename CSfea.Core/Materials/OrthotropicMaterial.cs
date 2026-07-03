using CSfea.Sparse;

namespace CSfea.Core;

/// <summary>
/// Ортотропный материал под плоское напряжённое состояние.
/// Поворот главных осей через <see cref="QMembraneRotated"/>.
/// Порт <c>fea/core.py: OrthotropicMaterial</c>.
/// </summary>
public sealed class OrthotropicMaterial
{
    /// <summary>Модуль Юнга вдоль волокон (ось 1).</summary>
    public double E1 { get; }

    /// <summary>Модуль Юнга поперёк волокон (ось 2).</summary>
    public double E2 { get; }

    /// <summary>Коэффициент Пуассона ν12 (ε2 от σ1).</summary>
    public double Nu12 { get; }

    /// <summary>Коэффициент Пуассона ν21 = ν12·E2/E1.</summary>
    public double Nu21 { get; }

    /// <summary>Сдвиговый модуль в плоскости.</summary>
    public double G12 { get; }

    /// <summary>Трансверсальный сдвиговый модуль G13.</summary>
    public double G13 { get; }

    /// <summary>Трансверсальный сдвиговый модуль G23.</summary>
    public double G23 { get; }

    /// <summary>Плотность.</summary>
    public double Rho { get; }

    public OrthotropicMaterial(double e1, double e2, double nu12, double g12,
                               double? g13 = null, double? g23 = null, double rho = 0.0)
    {
        E1 = e1;
        E2 = e2;
        Nu12 = nu12;
        Nu21 = nu12 * e2 / e1;
        G12 = g12;
        G13 = g13 ?? g12;
        G23 = g23 ?? g12;
        Rho = rho;
    }

    /// <summary>Мембранная матрица жёсткости Q (3x3) в главных осях.</summary>
    public double[,] QMembrane()
    {
        double d = 1.0 - Nu12 * Nu21;
        return new[,]
        {
            { E1 / d,        Nu21 * E1 / d, 0.0 },
            { Nu12 * E2 / d, E2 / d,        0.0 },
            { 0.0,           0.0,           G12 },
        };
    }

    /// <summary>Сдвиговая матрица жёсткости Q_s (2x2) в главных осях.</summary>
    public double[,] QShear() => new[,] { { G13, 0.0 }, { 0.0, G23 } };

    /// <summary>Преобразование напряжений σ_x = T_σ σ_1.</summary>
    private static double[,] TSigma(double theta)
    {
        double c = Math.Cos(theta), s = Math.Sin(theta);
        return new[,]
        {
            {  c * c,  s * s,  2.0 * c * s },
            {  s * s,  c * c, -2.0 * c * s },
            { -c * s,  c * s,  c * c - s * s },
        };
    }

    /// <summary>Преобразование инженерных деформаций ε_x = T_ε ε_1.</summary>
    private static double[,] TEps(double theta)
    {
        double c = Math.Cos(theta), s = Math.Sin(theta);
        return new[,]
        {
            {  c * c,      s * s,      c * s },
            {  s * s,      c * c,     -c * s },
            { -2.0 * c * s, 2.0 * c * s, c * c - s * s },
        };
    }

    /// <summary>Приведённая Q в системе, повёрнутой относительно материала на theta (рад).</summary>
    public double[,] QMembraneRotated(double theta)
    {
        var ts = TSigma(theta);
        var te = TEps(theta);
        // Q_x = T_σ^{-1} (Q_1 · T_ε)
        return DenseLinAlg.Solve(ts, Dense.MatMul(QMembrane(), te));
    }

    /// <summary>Приведённая сдвиговая Q_s в повёрнутой системе.</summary>
    public double[,] QShearRotated(double theta)
    {
        double c = Math.Cos(theta), s = Math.Sin(theta);
        var r = new[,] { { c, s }, { -s, c } };
        // R^T · Q_s · R
        return Dense.MatMul(Dense.MatTMul(r, QShear()), r);
    }
}
