namespace CScore.ShellSimpl;

/// <summary>Ряд арматуры полосы: координата центра от срединной плоскости (+ вверх), м, и площадь, м²/м.</summary>
public sealed record StripNdmRow(double Z, double As);

/// <summary>
/// Отклик полосы на плоскость деформаций ε(z) = ε₀ + κ·z: усилия и касательная матрица
/// K = ∂(N, M)/∂(ε₀, κ) (симметрична, хранится тремя компонентами).
/// </summary>
public readonly record struct StripNdmResponse(double N, double M, double K00, double K01, double K11)
{
    /// <summary>Определитель касательной матрицы.</summary>
    public double Det => K00 * K11 - K01 * K01;
}

/// <summary>
/// Полоса плиты 1 м × h для одноосной плоской нелинейной деформационной модели (пп. 8.1.20–8.1.30,
/// 8.2.14 СП 63): бетон по толщине и ряды арматуры, ε(z) = ε₀ + κ·z.
/// Знаки — конвенция <see cref="PlateSection"/>: «+» N — растяжение, «+» M растягивает грань +Z (верх).
/// Единицы — кПа, м, кН/м, кН·м/м (как у диаграмм <see cref="MaterialChars"/>).
///
/// Бетон интегрируется кусочной квадратурой: толщина делится в точках, где ε(z) попадает в узлы
/// диаграммы (<c>Ic.X</c>, <c>It.X</c>) и в ноль, на каждом куске — Гаусс–Лежандр 3 точки. Для
/// кусочно-линейных диаграмм (L2, L3) интеграл точный, и тонкая сжатая зона не «проваливается»
/// между слоями, как у слоистой модели с фиксированным разбиением.
/// Площадь бетона под арматурой не вычитается — как в стержневом НДМ без разностной диаграммы.
/// </summary>
public sealed class StripNdmSection
{
    static readonly double GaussX = Math.Sqrt(3.0 / 5.0);
    const double GaussW0 = 8.0 / 9.0, GaussW1 = 5.0 / 9.0;

    readonly double[] _breaks;

    public StripNdmSection(double h, IReadOnlyList<StripNdmRow> rows, Diagramm concrete, Diagramm rebar)
    {
        if (!(h > 0.0)) throw new ArgumentOutOfRangeException(nameof(h), "Толщина полосы должна быть положительной");
        H = h;
        Rows = rows ?? throw new ArgumentNullException(nameof(rows));
        Concrete = concrete ?? throw new ArgumentNullException(nameof(concrete));
        Rebar = rebar ?? throw new ArgumentNullException(nameof(rebar));

        var set = new SortedSet<double> { 0.0 };
        foreach (double x in concrete.Ic.X) set.Add(x);
        foreach (double x in concrete.It.X) set.Add(x);
        _breaks = [.. set];
    }

    StripNdmSection(StripNdmSection other, IReadOnlyList<StripNdmRow> rows)
    {
        H = other.H; Rows = rows; Concrete = other.Concrete; Rebar = other.Rebar; _breaks = other._breaks;
    }

    /// <summary>Толщина полосы, м.</summary>
    public double H { get; }
    public IReadOnlyList<StripNdmRow> Rows { get; }
    public Diagramm Concrete { get; }
    public Diagramm Rebar { get; }

    /// <summary>Та же полоса с другими площадями рядов (геометрия и диаграммы — прежние).</summary>
    public StripNdmSection WithAreas(params double[] areas)
    {
        if (areas.Length != Rows.Count) throw new ArgumentException("Число площадей не совпадает с числом рядов", nameof(areas));
        var rows = new StripNdmRow[Rows.Count];
        for (int i = 0; i < rows.Length; i++) rows[i] = Rows[i] with { As = areas[i] };
        return new StripNdmSection(this, rows);
    }

    /// <summary>Усилия и касательная матрица при плоскости деформаций (ε₀, κ).</summary>
    /// <param name="tensionConcrete">Учитывать растянутый бетон: ПС1 и сечение с трещиной — нет,
    /// поиск M_crc (п. 8.2.14) — да.</param>
    public StripNdmResponse Integrate(double eps0, double kappa, bool tensionConcrete)
    {
        double half = H / 2.0;
        double n = 0, m = 0, k00 = 0, k01 = 0, k11 = 0;

        Span<double> zs = _breaks.Length + 2 <= 256 ? stackalloc double[_breaks.Length + 2] : new double[_breaks.Length + 2];
        int count = 0;
        zs[count++] = -half;
        if (Math.Abs(kappa) * H > 1e-14)
        {
            foreach (double e in _breaks)
            {
                double z = (e - eps0) / kappa;
                if (z > -half && z < half) zs[count++] = z;
            }
        }
        zs[count++] = half;
        zs[..count].Sort();

        for (int i = 0; i + 1 < count; i++)
        {
            double a = zs[i], b = zs[i + 1];
            double hl = 0.5 * (b - a);
            if (hl < 1e-15) continue;
            double mid = 0.5 * (a + b);
            AddConcrete(mid, hl * GaussW0);
            AddConcrete(mid - hl * GaussX, hl * GaussW1);
            AddConcrete(mid + hl * GaussX, hl * GaussW1);
        }

        foreach (var row in Rows)
        {
            if (row.As <= 0.0) continue;
            double sig = Rebar.Sig(eps0 + kappa * row.Z, out double e2, tenB: true, comprA: true);
            double z = row.Z, w = row.As;
            n += sig * w; m += sig * w * z;
            k00 += e2 * w; k01 += e2 * w * z; k11 += e2 * w * z * z;
        }

        return new StripNdmResponse(n, m, k00, k01, k11);

        void AddConcrete(double z, double w)
        {
            double sig = Concrete.Sig(eps0 + kappa * z, out double e2, tenB: tensionConcrete, comprA: true);
            n += sig * w; m += sig * w * z;
            k00 += e2 * w; k01 += e2 * w * z; k11 += e2 * w * z * z;
        }
    }

    /// <summary>Начальная (упругая) касательная матрица полного сечения: бетон по начальному модулю
    /// ветви сжатия, арматура по начальному модулю. Старт Ньютона и запасная матрица при вырожденной
    /// касательной.</summary>
    public StripNdmResponse ElasticStiffness()
    {
        const double probe = 1e-9;
        Concrete.Sig(-probe, out double eb, tenB: false, comprA: true);
        Rebar.Sig(probe, out double es, tenB: true, comprA: true);
        double k00 = eb * H, k01 = 0, k11 = eb * H * H * H / 12.0;
        foreach (var row in Rows)
        {
            if (row.As <= 0.0) continue;
            k00 += es * row.As; k01 += es * row.As * row.Z; k11 += es * row.As * row.Z * row.Z;
        }
        return new StripNdmResponse(0, 0, k00, k01, k11);
    }

    /// <summary>Масштаб усилий для допусков: несущая способность бетона на сжатие |σb,min|·h, кН/м.</summary>
    public double ForceScale()
    {
        double sMin = 0;
        foreach (double x in Concrete.Ic.X)
            sMin = Math.Min(sMin, Concrete.Sig(x, out _, tenB: false, comprA: true));
        return Math.Max(Math.Abs(sMin) * H, 1.0);
    }
}
