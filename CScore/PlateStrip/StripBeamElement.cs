namespace CScore.PlateStrip;

/// <summary>Двухузловой элемент производной балки полосы (Эйлер–Бернулли, без кручения).
/// Пять степеней свободы в узле в порядке u, v, w, θy, θz — тот же порядок, что у компонент
/// <see cref="StripElementNodalLoad"/> (N, Vy, Vz, My, Mz), поэтому вектор consistent nodal
/// loads Среза 4 подставляется в правую часть без перестановок.
///
/// <b>Единственная точка правды знаковой конвенции среза:</b>
/// <code>
/// ε₀ = u′,   θz = v′,   θy = −w′,   κz = θz′ = v″,   κy = θy′ = −w″
/// </code>
/// Она не выбрана произвольно, а следует из кинематики плитного сечения: ShellStrainState
/// определяет ε_x(z) = ε₀x + κx·z, а StripKinematicEmbedding отображает κy полосы в κx плиты —
/// значит κy обязана быть кривизной, при которой продольная деформация растёт с z, то есть
/// −w″. Внутренние усилия [N, My, Mz] = D·[ε₀, κy, κz] тогда работают на тех же обобщённых
/// деформациях, что BeamStrainState в StripResultantIntegrator, и целевая эпюра сравнивается с
/// откликом балки без переходных множителей.
///
/// Внутренний расчётный примитив: некорректные входы приводят к исключению, а не к диагностике.</summary>
public static class StripBeamElement
{
    /// <summary>Число степеней свободы в узле.</summary>
    public const int DofPerNode = 5;

    /// <summary>Число степеней свободы элемента.</summary>
    public const int DofPerElement = 2 * DofPerNode;

    /// <summary>Матрица деформаций B (3×10): строки [ε₀, κy, κz], столбцы — DOF элемента.
    /// xi — безразмерная координата вдоль элемента, 0 в первом узле, 1 во втором.</summary>
    public static double[,] StrainMatrix(double lengthM, double xi)
    {
        if (!(lengthM > 0.0) || !double.IsFinite(lengthM))
            throw new ArgumentOutOfRangeException(nameof(lengthM),
                "Длина элемента должна быть конечной и положительной.");
        if (!double.IsFinite(xi))
            throw new ArgumentOutOfRangeException(nameof(xi), "Координата должна быть конечной.");

        double l2 = lengthM * lengthM;
        double h1 = (-6.0 + 12.0 * xi) / l2;
        double h2 = (-4.0 + 6.0 * xi) / lengthM;
        double h3 = (6.0 - 12.0 * xi) / l2;
        double h4 = (-2.0 + 6.0 * xi) / lengthM;

        var b = new double[3, DofPerElement];

        // ε₀ = u′
        b[0, 0] = -1.0 / lengthM;
        b[0, 5] = 1.0 / lengthM;

        // κy = −w″ при w = H1·w1 − H2·θy1 + H3·w2 − H4·θy2
        b[1, 2] = -h1;
        b[1, 3] = h2;
        b[1, 7] = -h3;
        b[1, 8] = h4;

        // κz = v″ при v = H1·v1 + H2·θz1 + H3·v2 + H4·θz2
        b[2, 1] = h1;
        b[2, 4] = h2;
        b[2, 6] = h3;
        b[2, 9] = h4;

        return b;
    }

    /// <summary>Матрица жёсткости элемента ∫ BᵀDB dx. Двухточечной квадратуры Гаусса
    /// достаточно: D постоянна на элементе, а BᵀDB не выше второй степени по xi.</summary>
    public static double[,] Stiffness(double[,] sectionTangent, double lengthM)
    {
        ArgumentNullException.ThrowIfNull(sectionTangent);
        if (sectionTangent.GetLength(0) != 3 || sectionTangent.GetLength(1) != 3)
            throw new ArgumentException("Матрица сечения должна быть 3×3.", nameof(sectionTangent));

        ReadOnlySpan<double> gaussXi = [0.2113248654051871, 0.7886751345948129]; // (1 ∓ 1/√3)/2
        double weight = lengthM / 2.0;

        var k = new double[DofPerElement, DofPerElement];
        foreach (double xi in gaussXi)
        {
            var b = StrainMatrix(lengthM, xi);
            // db = D·B (3×10)
            var db = new double[3, DofPerElement];
            for (int i = 0; i < 3; i++)
            for (int j = 0; j < DofPerElement; j++)
            {
                double sum = 0.0;
                for (int m = 0; m < 3; m++)
                    sum += sectionTangent[i, m] * b[m, j];
                db[i, j] = sum;
            }

            for (int i = 0; i < DofPerElement; i++)
            for (int j = 0; j < DofPerElement; j++)
            {
                double sum = 0.0;
                for (int m = 0; m < 3; m++)
                    sum += b[m, i] * db[m, j];
                k[i, j] += weight * sum;
            }
        }
        return k;
    }

    /// <summary>Раскладывает узловую нагрузку элемента в вектор по DOF элемента.</summary>
    public static double[] LoadVector(StripElementNodalLoad load) =>
    [
        load.N1, load.Vy1, load.Vz1, load.My1, load.Mz1,
        load.N2, load.Vy2, load.Vz2, load.My2, load.Mz2
    ];
}
