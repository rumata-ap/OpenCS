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

/// <summary>Активное ограничение границы: точка, сторона (−1 сжатие, +1 растяжение), режим п. 8.1.30.</summary>
public readonly record struct ActiveConstraint(int PointIndex, int Side, bool SingleSign);

/// <summary>Излом на меридиане — смена активного ограничения (ребро многогранника деформаций).</summary>
public readonly record struct KinkTransition(ActiveConstraint From, ActiveConstraint To);
