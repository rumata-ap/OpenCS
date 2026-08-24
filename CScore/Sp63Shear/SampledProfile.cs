namespace CScore.Sp63Shear;

/// <summary>Значения усилий в одном сечении вдоль элемента.</summary>
/// <param name="S">Координата вдоль оси элемента, м.</param>
/// <param name="Q">Поперечная сила, кН.</param>
/// <param name="M">Изгибающий момент, кН·м.</param>
/// <param name="N">Продольная сила, кН.</param>
public readonly record struct ForceSample(double S, double Q, double M, double N);

/// <summary>
/// Профиль усилий по табличной эпюре (результат МКЭ). Поперечная и продольная силы
/// интерполируются линейно; момент — эрмитовым кубическим полиномом с использованием
/// поперечной силы как производной (M′ = Q), что точно воспроизводит параболу
/// под равномерной нагрузкой.
/// </summary>
public sealed class SampledProfile : IForceProfile
{
    /// <summary>Допуск на совпадение координат сечений, м.</summary>
    const double CoordinateEpsilon = 1e-9;

    /// <summary>Относительный допуск проверки согласованности M′ = Q.</summary>
    const double MomentConsistencyTolerance = 0.01;

    readonly List<ForceSample> _samples;
    readonly List<int> _intervals;      // левые узлы невырожденных интервалов
    readonly bool[] _linearMoment;      // интервалы с отключённой эрмитовой интерполяцией
    readonly List<string> _warnings = [];
    readonly double _supportFromStart;
    readonly double _supportFromEnd;
    readonly bool _supportAtStart;
    readonly bool _supportAtEnd;

    /// <summary>Создаёт профиль по набору сечений; требуется не менее двух точек.</summary>
    /// <param name="samples">Сечения эпюры в произвольном порядке.</param>
    /// <param name="supportDistanceFromStart">Координата опоры со стороны начала, м.</param>
    /// <param name="supportDistanceFromEnd">Координата опоры со стороны конца, м.</param>
    /// <param name="supportAtStart">Начало участка является опорой.</param>
    /// <param name="supportAtEnd">Конец участка является опорой.</param>
    public SampledProfile(
        IReadOnlyList<ForceSample> samples,
        double supportDistanceFromStart,
        double supportDistanceFromEnd,
        bool supportAtStart = true,
        bool supportAtEnd = true)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (samples.Count < 2)
            throw new ArgumentException(
                "Для табличного профиля требуется не менее двух сечений.", nameof(samples));

        foreach (var sample in samples)
            if (!double.IsFinite(sample.S) || !double.IsFinite(sample.Q) ||
                !double.IsFinite(sample.M) || !double.IsFinite(sample.N))
                throw new ArgumentException(
                    "Эпюра содержит нечисловую координату или усилие.", nameof(samples));

        _samples = samples.OrderBy(sample => sample.S).ToList();   // OrderBy устойчив
        _intervals = [];

        for (int i = 0; i + 1 < _samples.Count; i++)
        {
            double h = _samples[i + 1].S - _samples[i].S;
            if (h > 0.0 && h < CoordinateEpsilon)
                throw new ArgumentException(
                    $"Сечения эпюры в s ≈ {_samples[i].S:F6} м почти совпадают — "
                    + "интервал вырожден; объедините их или задайте точный скачок.",
                    nameof(samples));
            if (h == 0.0 && i + 2 < _samples.Count && _samples[i + 2].S == _samples[i].S)
                throw new ArgumentException(
                    $"В s = {_samples[i].S:F6} м задано более двух сечений — "
                    + "скачок усилий неоднозначен.", nameof(samples));
            if (h > 0.0) _intervals.Add(i);
        }

        if (_intervals.Count == 0)
            throw new ArgumentException(
                "Все сечения эпюры имеют одну координату — длина профиля нулевая.",
                nameof(samples));

        _linearMoment = new bool[_samples.Count - 1];
        foreach (int i in _intervals)
        {
            var left = _samples[i];
            var right = _samples[i + 1];
            double h = right.S - left.S;
            double predicted = 0.5 * (left.Q + right.Q) * h;    // ∫Q ds при линейной Q
            double actual = right.M - left.M;
            double scale = Math.Max(
                Math.Max(Math.Abs(left.M), Math.Abs(right.M)), Math.Abs(predicted));
            if (Math.Abs(actual - predicted) > MomentConsistencyTolerance * Math.Max(scale, 1e-9))
            {
                _linearMoment[i] = true;
                _warnings.Add(
                    $"Эпюры M и Q не согласованы на участке s = {left.S:F3}…{right.S:F3} м "
                    + "(проверьте знак Q) — момент на нём интерполируется линейно.");
            }
        }

        _supportFromStart = supportDistanceFromStart;
        _supportFromEnd = supportDistanceFromEnd;
        _supportAtStart = supportAtStart;
        _supportAtEnd = supportAtEnd;
    }

    /// <summary>Упорядоченные по координате сечения профиля.</summary>
    public IReadOnlyList<ForceSample> Samples => _samples;

    /// <summary>Оговорки, выявленные при разборе эпюры.</summary>
    public IReadOnlyList<string> Warnings => _warnings;

    /// <summary>Длина области определения, м.</summary>
    public double Length => _samples[^1].S - _samples[0].S;

    /// <summary>Диапазон стоянок, м.</summary>
    public (double Min, double Max) StationRange => (_samples[0].S, _samples[^1].S);

    /// <summary>
    /// Поперечная сила, интерполированная линейно, кН.
    /// В точке скачка возвращается большее по модулю из двух значений.
    /// </summary>
    public double Q(double s)
    {
        double jump = JumpQ(s);
        if (!double.IsNaN(jump)) return jump;

        var (index, t) = Locate(s);
        return _samples[index].Q + t * (_samples[index + 1].Q - _samples[index].Q);
    }

    /// <summary>Продольная сила, интерполированная линейно, кН.</summary>
    public double N(double s)
    {
        var (index, t) = Locate(s);
        return _samples[index].N + t * (_samples[index + 1].N - _samples[index].N);
    }

    /// <summary>Изгибающий момент, восстановленный эрмитовой интерполяцией, кН·м.</summary>
    public double M(double s)
    {
        var (index, t) = Locate(s);
        var left = _samples[index];
        var right = _samples[index + 1];
        double h = right.S - left.S;
        if (h <= 0.0) return left.M;
        if (_linearMoment[index]) return left.M + t * (right.M - left.M);

        double t2 = t * t;
        double t3 = t2 * t;
        double h00 = 2.0 * t3 - 3.0 * t2 + 1.0;
        double h10 = t3 - 2.0 * t2 + t;
        double h01 = -2.0 * t3 + 3.0 * t2;
        double h11 = t3 - t2;

        return h00 * left.M + h10 * h * left.Q + h01 * right.M + h11 * h * right.Q;
    }

    /// <summary>Наибольшее по модулю Q на отрезке: концы плюс все узлы внутри него.</summary>
    public double MaxAbsQ(double from, double to)
    {
        double lo = Math.Min(from, to);
        double hi = Math.Max(from, to);
        double max = Math.Max(Math.Abs(Q(lo)), Math.Abs(Q(hi)));
        foreach (var sample in _samples)
            if (sample.S > lo && sample.S < hi)
                max = Math.Max(max, Math.Abs(sample.Q));
        return max;
    }

    /// <summary>Расстояние от стоянки до опоры в заданном направлении, м.</summary>
    public double SupportDistanceAt(double station, int direction) =>
        direction >= 0
            ? _supportAtEnd ? Math.Max(_supportFromEnd - station, 0.0) : 0.0
            : _supportAtStart ? Math.Max(station - _supportFromStart, 0.0) : 0.0;

    /// <summary>Объявлена ли опора в заданном направлении.</summary>
    public bool HasSupport(int direction) => direction >= 0 ? _supportAtEnd : _supportAtStart;

    /// <summary>Большее по модулю Q, если в координате задан скачок; иначе NaN.</summary>
    double JumpQ(double s)
    {
        for (int i = 0; i + 1 < _samples.Count; i++)
            if (_samples[i].S == s && _samples[i + 1].S == s)
                return Math.Abs(_samples[i].Q) >= Math.Abs(_samples[i + 1].Q)
                    ? _samples[i].Q
                    : _samples[i + 1].Q;
        return double.NaN;
    }

    /// <summary>Находит невырожденный объемлющий интервал и координату внутри него.</summary>
    (int Index, double T) Locate(double s)
    {
        int first = _intervals[0];
        int last = _intervals[^1];
        if (s <= _samples[first].S) return (first, 0.0);
        if (s >= _samples[last + 1].S) return (last, 1.0);

        foreach (int i in _intervals)
            if (s <= _samples[i + 1].S)
                return (i, (s - _samples[i].S) / (_samples[i + 1].S - _samples[i].S));

        return (last, 1.0);
    }
}
