namespace CSfea.Core;

/// <summary>
/// Упругие и геометрические характеристики сечения балки.
/// Порт <c>fea/beam.py: BeamSection</c>.
/// </summary>
public sealed class BeamSection
{
    /// <summary>Модуль Юнга.</summary>
    public double E { get; }

    /// <summary>Площадь сечения.</summary>
    public double A { get; }

    /// <summary>Момент инерции относительно локальной оси y (изгиб в плоскости xz).</summary>
    public double Iy { get; }

    /// <summary>Момент инерции относительно локальной оси z (изгиб в плоскости xy).</summary>
    public double Iz { get; }

    /// <summary>Коэффициент Пуассона (используется только если G не задан).</summary>
    public double Nu { get; }

    /// <summary>Модуль сдвига.</summary>
    public double G { get; }

    /// <summary>Момент инерции при кручении (Saint-Venant).</summary>
    public double J { get; }

    /// <summary>Плотность.</summary>
    public double Rho { get; }

    public BeamSection(double e, double a, double iy, double? iz = null, double? j = null,
                       double? g = null, double nu = 0.3, double rho = 0.0)
    {
        E = e;
        A = a;
        Iy = iy;
        Iz = iz ?? iy;
        Nu = nu;
        G = g ?? e / (2.0 * (1.0 + nu));
        J = j ?? (Iy + Iz);
        Rho = rho;
    }
}
