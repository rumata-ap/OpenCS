namespace CScore.PlateStrip;

/// <summary>Формулировка производной балки полосы.</summary>
public enum StripBeamFormulation
{
    /// <summary>Эйлер–Бернулли, 5 DOF в узле (u, v, w, θy, θz), без кручения и сдвига.
    /// Поведение Срезов 1–8a, остаётся по умолчанию.</summary>
    EulerBernoulli,

    /// <summary>Тимошенко, 6 DOF в узле (u, v, w, θx, θy, θz): кручение GJ и поперечный
    /// сдвиг GAy/GAz (Срез 8b).</summary>
    Timoshenko
}
