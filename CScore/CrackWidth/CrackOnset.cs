namespace CScore;

/// <summary>
/// Состояние трещинообразования сечения для расчёта ширины раскрытия трещин (п. 8.2.18 СП 63):
/// точка трещинообразования (N, Mx,crc, My,crc), дотрещинная плоскость в ней и пост-трещинные
/// (ten = false) плоскости при тех же усилиях на длительной и кратковременной диаграммах,
/// по которым берутся σs,crc / εs,crc.
/// </summary>
/// <param name="N">Продольная сила точки трещинообразования, кН. При трещинообразовании по лучу
/// полной нагрузки (<paramref name="ByAxialForce"/>) отличается от N нагрузки.</param>
/// <param name="MxCrc">Момент трещинообразования по X, кН·м.</param>
/// <param name="MyCrc">Момент трещинообразования по Y, кН·м.</param>
/// <param name="CrackPlane">Плоскость деформаций в момент трещинообразования (бетон работает на растяжение, ten = true).</param>
/// <param name="PlaneCrcLong">Пост-трещинная плоскость при усилиях трещинообразования, длительная диаграмма.</param>
/// <param name="PlaneCrcShort">То же, кратковременная диаграмма.</param>
/// <param name="SigmaCrcLong">σs,crc наиболее растянутого стержня, длительная диаграмма, кПа.</param>
/// <param name="EpsCrcLong">Деформация того же стержня.</param>
/// <param name="SigmaCrcShort">σs,crc, кратковременная диаграмма, кПа.</param>
/// <param name="EpsCrcShort">Деформация того же стержня.</param>
/// <param name="ByAxialForce">Сечение трещиновато от продольной силы (при M = 0) — точка взята на луче полной нагрузки.</param>
/// <param name="Converged">Сошлись ли пост-трещинные плоскости (кратковременная может взять длительную как запасную).</param>
public sealed record CrackOnset(
    double N, double MxCrc, double MyCrc,
    Kurvature CrackPlane,
    Kurvature PlaneCrcLong, Kurvature PlaneCrcShort,
    double SigmaCrcLong, double EpsCrcLong, double SigmaCrcShort, double EpsCrcShort,
    bool ByAxialForce, bool Converged);
