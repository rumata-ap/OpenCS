namespace CSfea.Core;

/// <summary>Усилия балочного сечения: N, M_y, M_z.</summary>
public readonly record struct BeamForces(double N, double My, double Mz);

/// <summary>
/// Полиморфное балочное сечение под кинематикой Эйлера–Бернулли.
/// Конвенция: ось x — вдоль балки; eps0 — осевая деформация центра;
/// kappa_y — кривизна вокруг локальной y (момент M_y); kappa_z — вокруг z (M_z).
/// Порт протокола <c>fea/section_response.py: BeamSectionResponse</c>.
/// </summary>
public interface IBeamSectionResponse
{
    /// <summary>Усилия (N, M_y, M_z) по деформациям.</summary>
    BeamForces Forces(double eps0, double kappaY, double kappaZ);

    /// <summary>Касательная жёсткость 3×3: J[i,j] = ∂F_i/∂x_j.</summary>
    double[,] Tangent(double eps0, double kappaY, double kappaZ);

    /// <summary>Секущие жёсткости (EA, EI_y, EI_z).</summary>
    (double EA, double EIy, double EIz) Secant(double eps0, double kappaY, double kappaZ);

    /// <summary>Касательная жёсткость при кручении GJ.</summary>
    double TorsionalStiffness(double twist = 0.0);

    /// <summary>Зафиксировать состояние (обычно no-op).</summary>
    void Commit();

    /// <summary>Сбросить состояние.</summary>
    void Reset();
}
