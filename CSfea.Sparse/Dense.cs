namespace CSfea.Sparse;

/// <summary>
/// Утилиты плотной линейной алгебры над <c>double[,]</c> и <c>double[]</c>.
/// Замена numpy-операций (<c>@</c>, <c>.T</c>, <c>np.outer</c>, <c>np.cross</c> и т.п.)
/// для малых матриц элементов (15x15, 20x20, 24x24).
/// Хранение матриц — построчное (row-major), индексация <c>A[i, j]</c>.
/// </summary>
public static class Dense
{
    /// <summary>Произведение матриц C = A · B.</summary>
    public static double[,] MatMul(double[,] a, double[,] b)
    {
        int n = a.GetLength(0);
        int k = a.GetLength(1);
        int m = b.GetLength(1);
        if (b.GetLength(0) != k)
            throw new ArgumentException("Несовместимые размерности при умножении матриц.");
        var c = new double[n, m];
        for (int i = 0; i < n; i++)
        {
            for (int p = 0; p < k; p++)
            {
                double aip = a[i, p];
                if (aip == 0.0) continue;
                for (int j = 0; j < m; j++)
                    c[i, j] += aip * b[p, j];
            }
        }
        return c;
    }

    /// <summary>Произведение Aᵀ · B без явного транспонирования A.</summary>
    public static double[,] MatTMul(double[,] a, double[,] b)
    {
        int k = a.GetLength(0);
        int n = a.GetLength(1);
        int m = b.GetLength(1);
        if (b.GetLength(0) != k)
            throw new ArgumentException("Несовместимые размерности при умножении Aᵀ·B.");
        var c = new double[n, m];
        for (int p = 0; p < k; p++)
        {
            for (int i = 0; i < n; i++)
            {
                double api = a[p, i];
                if (api == 0.0) continue;
                for (int j = 0; j < m; j++)
                    c[i, j] += api * b[p, j];
            }
        }
        return c;
    }

    /// <summary>Произведение матрицы на вектор: A · v.</summary>
    public static double[] MatVec(double[,] a, double[] v)
    {
        int n = a.GetLength(0);
        int m = a.GetLength(1);
        if (v.Length != m)
            throw new ArgumentException("Несовместимые размерности матрицы и вектора.");
        var r = new double[n];
        for (int i = 0; i < n; i++)
        {
            double s = 0.0;
            for (int j = 0; j < m; j++)
                s += a[i, j] * v[j];
            r[i] = s;
        }
        return r;
    }

    /// <summary>Произведение транспонированной матрицы на вектор: Aᵀ · v.</summary>
    public static double[] MatTVec(double[,] a, double[] v)
    {
        int n = a.GetLength(0);
        int m = a.GetLength(1);
        if (v.Length != n)
            throw new ArgumentException("Несовместимые размерности Aᵀ·v.");
        var r = new double[m];
        for (int i = 0; i < n; i++)
        {
            double vi = v[i];
            if (vi == 0.0) continue;
            for (int j = 0; j < m; j++)
                r[j] += a[i, j] * vi;
        }
        return r;
    }

    /// <summary>Транспонирование матрицы.</summary>
    public static double[,] Transpose(double[,] a)
    {
        int n = a.GetLength(0);
        int m = a.GetLength(1);
        var t = new double[m, n];
        for (int i = 0; i < n; i++)
            for (int j = 0; j < m; j++)
                t[j, i] = a[i, j];
        return t;
    }

    /// <summary>Поэлементная сумма матриц.</summary>
    public static double[,] Add(double[,] a, double[,] b)
    {
        int n = a.GetLength(0);
        int m = a.GetLength(1);
        var c = new double[n, m];
        for (int i = 0; i < n; i++)
            for (int j = 0; j < m; j++)
                c[i, j] = a[i, j] + b[i, j];
        return c;
    }

    /// <summary>Поэлементная разность матриц.</summary>
    public static double[,] Sub(double[,] a, double[,] b)
    {
        int n = a.GetLength(0);
        int m = a.GetLength(1);
        var c = new double[n, m];
        for (int i = 0; i < n; i++)
            for (int j = 0; j < m; j++)
                c[i, j] = a[i, j] - b[i, j];
        return c;
    }

    /// <summary>Умножение матрицы на скаляр (новый объект).</summary>
    public static double[,] Scale(double[,] a, double s)
    {
        int n = a.GetLength(0);
        int m = a.GetLength(1);
        var c = new double[n, m];
        for (int i = 0; i < n; i++)
            for (int j = 0; j < m; j++)
                c[i, j] = a[i, j] * s;
        return c;
    }

    /// <summary>В место: <paramref name="target"/> += s · <paramref name="a"/>.</summary>
    public static void AddScaledInPlace(double[,] target, double[,] a, double s)
    {
        int n = target.GetLength(0);
        int m = target.GetLength(1);
        for (int i = 0; i < n; i++)
            for (int j = 0; j < m; j++)
                target[i, j] += s * a[i, j];
    }

    /// <summary>Внешнее произведение векторов: a · bᵀ.</summary>
    public static double[,] Outer(double[] a, double[] b)
    {
        var c = new double[a.Length, b.Length];
        for (int i = 0; i < a.Length; i++)
            for (int j = 0; j < b.Length; j++)
                c[i, j] = a[i] * b[j];
        return c;
    }

    /// <summary>Векторное произведение 3D-векторов.</summary>
    public static double[] Cross(double[] a, double[] b)
    {
        return new[]
        {
            a[1] * b[2] - a[2] * b[1],
            a[2] * b[0] - a[0] * b[2],
            a[0] * b[1] - a[1] * b[0],
        };
    }

    /// <summary>Скалярное произведение.</summary>
    public static double Dot(double[] a, double[] b)
    {
        double s = 0.0;
        for (int i = 0; i < a.Length; i++)
            s += a[i] * b[i];
        return s;
    }

    /// <summary>Евклидова норма вектора.</summary>
    public static double Norm(double[] a) => Math.Sqrt(Dot(a, a));

    /// <summary>Сумма векторов.</summary>
    public static double[] AddV(double[] a, double[] b)
    {
        var c = new double[a.Length];
        for (int i = 0; i < a.Length; i++)
            c[i] = a[i] + b[i];
        return c;
    }

    /// <summary>Разность векторов.</summary>
    public static double[] SubV(double[] a, double[] b)
    {
        var c = new double[a.Length];
        for (int i = 0; i < a.Length; i++)
            c[i] = a[i] - b[i];
        return c;
    }

    /// <summary>Умножение вектора на скаляр.</summary>
    public static double[] ScaleV(double[] a, double s)
    {
        var c = new double[a.Length];
        for (int i = 0; i < a.Length; i++)
            c[i] = a[i] * s;
        return c;
    }

    /// <summary>Копия матрицы.</summary>
    public static double[,] Copy(double[,] a) => (double[,])a.Clone();

    /// <summary>Главная диагональ матрицы.</summary>
    public static double[] Diagonal(double[,] a)
    {
        int n = Math.Min(a.GetLength(0), a.GetLength(1));
        var d = new double[n];
        for (int i = 0; i < n; i++)
            d[i] = a[i, i];
        return d;
    }

    /// <summary>Максимум модулей элементов вектора.</summary>
    public static double MaxAbs(double[] a)
    {
        double m = 0.0;
        foreach (double x in a)
            m = Math.Max(m, Math.Abs(x));
        return m;
    }
}
