namespace CScore.Sp63Shear;

/// <summary>
/// Распределение усилий вдоль продольной оси элемента. Координата s отсчитывается
/// от начала области определения профиля, м.
/// </summary>
public interface IForceProfile
{
    /// <summary>Поперечная сила в сечении с координатой s, кН.</summary>
    double Q(double s);

    /// <summary>Изгибающий момент в сечении с координатой s, кН·м.</summary>
    double M(double s);

    /// <summary>Продольная сила в сечении с координатой s, кН (сжатие — «минус»).</summary>
    double N(double s);

    /// <summary>Длина области определения профиля, м.</summary>
    double Length { get; }

    /// <summary>Диапазон допустимых стоянок, м.</summary>
    (double Min, double Max) StationRange { get; }

    /// <summary>
    /// Расстояние от стоянки до опоры в заданном направлении, м;
    /// 0 — опоры нет, она не задана либо стоянка стоит точно на опоре.
    /// </summary>
    double SupportDistanceAt(double station, int direction);

    /// <summary>
    /// Есть ли в заданном направлении опора. Отличает «опора не задана» от «стоянка
    /// стоит на опоре», у которых <see cref="SupportDistanceAt"/> одинаково равно нулю.
    /// </summary>
    bool HasSupport(int direction);

    /// <summary>
    /// Наибольшее по модулю значение поперечной силы на отрезке [from; to], кН.
    /// Требование п. 8.1.33 «наиболее опасное загружение в пределах наклонного сечения»:
    /// реализация обязана возвращать точный максимум, а не выборку по сетке.
    /// Порядок концов отрезка значения не имеет.
    /// </summary>
    double MaxAbsQ(double from, double to);
}
