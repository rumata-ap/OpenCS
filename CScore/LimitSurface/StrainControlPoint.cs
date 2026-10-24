namespace CScore.LimitSurface;

/// <summary>Вид контрольной точки области предельных деформаций.</summary>
public enum StrainPointKind { Concrete, Rebar }

/// <summary>
/// Контрольная точка области допустимых плоскостей деформаций. Пределы — для плоскостной
/// деформации ε = e0 + ky·y + kz·x (у напрягаемого стержня предел уже уменьшен на εp);
/// бесконечность — предела нет. У бетона нижний предел при двузначной эпюре — εb2, а при
/// однозначной пересчитывается по (8.53) из <see cref="EpsB0"/> и <see cref="EpsB2"/>; верхний
/// (<see cref="EpsMax"/>) при двузначной эпюре — εbt2 (ПС2) или +∞ (ПС1: трещиноватый бетон), при
/// растяжении одного знака — по (8.54) из <see cref="EpsBt0"/> и <see cref="EpsMax"/>.
/// </summary>
public sealed record StrainControlPoint(
    double X, double Y, StrainPointKind Kind,
    double EpsMin, double EpsMax,
    double EpsB0, double EpsB2,
    int AreaIndex, double EpsBt0 = double.NaN);

/// <summary>Вид активного ограничения границы поверхности.</summary>
public enum ConstraintKind : byte
{
    /// <summary>Предел деформации контрольной точки (ПС1, ПС2 — образование трещин).</summary>
    Strain,
    /// <summary>Продолжительное раскрытие трещин, (8.119).</summary>
    AcrcLong,
    /// <summary>Непродолжительное раскрытие трещин, (8.120).</summary>
    AcrcShort,
    /// <summary>σs ≤ Rs,ser, п. 8.2.16.</summary>
    SteelStress,
    /// <summary>Сжатие бетона в сечении с трещиной, п. 8.1.30.</summary>
    ConcreteCompression,
    /// <summary>Граница совпадает с телом образования трещин (ПС2): сжатие раньше трещины или раскрытие сразу превышает предел.</summary>
    CrackBody,
}

/// <summary>
/// Активное ограничение границы: точка, сторона (−1 сжатие, +1 растяжение), режим п. 8.1.30 и вид
/// ограничения (<see cref="ConstraintKind.Strain"/> — прежние поверхности по деформациям).
/// </summary>
public readonly record struct ActiveConstraint(int PointIndex, int Side, bool SingleSign,
    ConstraintKind Kind = ConstraintKind.Strain);

/// <summary>Излом на меридиане — смена активного ограничения (ребро многогранника деформаций).</summary>
public readonly record struct KinkTransition(ActiveConstraint From, ActiveConstraint To);
