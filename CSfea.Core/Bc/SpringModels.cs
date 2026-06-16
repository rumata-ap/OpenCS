namespace CSfea.Core;

/// <summary>Протокол скалярной нелинейной модели пружины (сила/жёсткость от смещения).</summary>
public interface ISpringModel
{
    /// <summary>Сила пружины при смещении u.</summary>
    double Force(double u);

    /// <summary>Касательная жёсткость при смещении u.</summary>
    double Stiffness(double u);

    /// <summary>Зафиксировать состояние (для stateful-моделей).</summary>
    void Commit(double u);

    /// <summary>Сбросить состояние.</summary>
    void Reset();
}

/// <summary>Базовая stateless-пружина (commit/reset — no-op).</summary>
public abstract class SpringModelBase : ISpringModel
{
    public abstract double Force(double u);
    public abstract double Stiffness(double u);
    public virtual void Commit(double u) { }
    public virtual void Reset() { }
}

/// <summary>Режим односторонней пружины.</summary>
public enum OneSidedMode { Compression, Tension }

/// <summary>
/// Односторонняя пружина: активна только при одном знаке смещения.
/// Порт <c>boundary_conditions.py: OneSidedSpring</c>.
/// </summary>
public sealed class OneSidedSpring : SpringModelBase
{
    private readonly double _k;
    private readonly OneSidedMode _mode;

    public OneSidedSpring(double k, OneSidedMode mode = OneSidedMode.Compression)
    {
        _k = k;
        _mode = mode;
    }

    public override double Force(double u)
        => _mode == OneSidedMode.Compression
            ? (u >= 0.0 ? _k * u : 0.0)
            : (u <= 0.0 ? _k * u : 0.0);

    public override double Stiffness(double u)
        => _mode == OneSidedMode.Compression
            ? (u >= 0.0 ? _k : 0.0)
            : (u <= 0.0 ? _k : 0.0);
}

/// <summary>
/// Билинейная пружина с изломом в u_break.
/// Порт <c>boundary_conditions.py: BilinearSpring</c>.
/// </summary>
public sealed class BilinearSpring : SpringModelBase
{
    private readonly double _kPos;
    private readonly double _kNeg;
    private readonly double _uBreak;

    public BilinearSpring(double kPos, double kNeg, double uBreak = 0.0)
    {
        _kPos = kPos;
        _kNeg = kNeg;
        _uBreak = uBreak;
    }

    public override double Force(double u)
    {
        double du = u - _uBreak;
        return du >= 0.0 ? _kPos * du : _kNeg * du;
    }

    public override double Stiffness(double u)
        => (u - _uBreak) >= 0.0 ? _kPos : _kNeg;
}

/// <summary>
/// Пружина с зазором: нулевая жёсткость в [gap_min, gap_max].
/// Порт <c>boundary_conditions.py: GapSpring</c>.
/// </summary>
public sealed class GapSpring : SpringModelBase
{
    private readonly double _k;
    private readonly double _gapMin;
    private readonly double _gapMax;

    public GapSpring(double k, double gapMin, double gapMax)
    {
        if (gapMin > gapMax)
            throw new ArgumentException("gap_min должен быть ≤ gap_max.");
        _k = k;
        _gapMin = gapMin;
        _gapMax = gapMax;
    }

    public override double Force(double u)
    {
        if (u > _gapMax) return _k * (u - _gapMax);
        if (u < _gapMin) return _k * (u - _gapMin);
        return 0.0;
    }

    public override double Stiffness(double u)
        => (u > _gapMax || u < _gapMin) ? _k : 0.0;
}

/// <summary>
/// Идеально-пластическая пружина (stateful): хранит закоммиченное
/// пластическое смещение u_p. Порт <c>boundary_conditions.py: ElastoplasticSpring</c>.
/// </summary>
public sealed class ElastoplasticSpring : ISpringModel
{
    private readonly double _k;
    private readonly double _fy;
    private double _up;

    public ElastoplasticSpring(double k, double fY)
    {
        if (fY <= 0.0) throw new ArgumentException("F_y должен быть > 0.");
        _k = k;
        _fy = fY;
        _up = 0.0;
    }

    public double Force(double u)
    {
        double fTrial = _k * (u - _up);
        return Math.Clamp(fTrial, -_fy, _fy);
    }

    public double Stiffness(double u)
    {
        double fTrial = _k * (u - _up);
        return Math.Abs(fTrial) < _fy ? _k : 0.0;
    }

    public void Commit(double u)
    {
        double fTrial = _k * (u - _up);
        if (Math.Abs(fTrial) > _fy)
        {
            double sign = fTrial > 0.0 ? 1.0 : -1.0;
            _up += (Math.Abs(fTrial) - _fy) / _k * sign;
        }
    }

    public void Reset() => _up = 0.0;
}

/// <summary>
/// Кусочно-линейная пружина по таблице точек (u, F).
/// Порт <c>boundary_conditions.py: PiecewiseSpring</c>.
/// </summary>
public sealed class PiecewiseSpring : SpringModelBase
{
    private readonly double[] _u;
    private readonly double[] _f;

    public PiecewiseSpring(double[] u, double[] f)
    {
        if (u.Length < 2)
            throw new ArgumentException("Таблица должна содержать минимум 2 точки.");
        for (int i = 1; i < u.Length; i++)
            if (u[i] <= u[i - 1])
                throw new ArgumentException("u должен быть строго монотонно возрастающим.");
        bool hasZero = false;
        foreach (double x in u)
            if (Math.Abs(x) < 1e-12) { hasZero = true; break; }
        if (!hasZero)
            throw new ArgumentException("u должен содержать точку u=0.");
        _u = u;
        _f = f;
    }

    public override double Force(double uVal)
    {
        int n = _u.Length;
        if (uVal <= _u[0])
        {
            double slope = (_f[1] - _f[0]) / (_u[1] - _u[0]);
            return _f[0] + slope * (uVal - _u[0]);
        }
        if (uVal >= _u[n - 1])
        {
            double slope = (_f[n - 1] - _f[n - 2]) / (_u[n - 1] - _u[n - 2]);
            return _f[n - 1] + slope * (uVal - _u[n - 1]);
        }
        return Num.Interp(uVal, _u, _f);
    }

    public override double Stiffness(double uVal)
    {
        int n = _u.Length;
        int i = 0;
        while (i < n - 1 && _u[i + 1] <= uVal) i++;
        if (i > n - 2) i = n - 2;
        double du = _u[i + 1] - _u[i];
        return du == 0.0 ? 0.0 : (_f[i + 1] - _f[i]) / du;
    }
}
