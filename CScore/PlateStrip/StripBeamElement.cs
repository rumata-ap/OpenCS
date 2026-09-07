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

    /// <summary>Безразмерные координаты точек двухточечной квадратуры Гаусса на элементе.
    /// Публичны, чтобы нелинейный решатель (Срез 7) снимал деформации ровно в тех точках, по
    /// которым здесь ведётся интегрирование, а не заводил вторую сетку квадратуры.</summary>
    public static IReadOnlyList<double> GaussXi { get; } = [0.2113248654051871, 0.7886751345948129];

    /// <summary>Матрица жёсткости при касательной, различной в точках квадратуры (Срез 7).
    /// В нелинейном режиме κ линейна по xi, поэтому K(ε(xi)) в двух точках различна и
    /// предпосылка «D постоянна на элементе» основной перегрузки перестаёт выполняться.
    ///
    /// Точность решения этой матрицей <b>не</b> определяется: сходимость Ньютона проверяется по
    /// невязке внутренних сил, поэтому приближённая касательная меняет только скорость
    /// сходимости (на этом основан режим модифицированного Ньютона).</summary>
    public static double[,] Stiffness(IReadOnlyList<double[,]> tangentsAtGaussPoints, double lengthM)
    {
        ArgumentNullException.ThrowIfNull(tangentsAtGaussPoints);
        if (tangentsAtGaussPoints.Count != GaussXi.Count)
            throw new ArgumentException(
                $"Нужно {GaussXi.Count} касательных — по одной на точку квадратуры.",
                nameof(tangentsAtGaussPoints));

        double weight = lengthM / 2.0;
        var k = new double[DofPerElement, DofPerElement];
        for (int g = 0; g < GaussXi.Count; g++)
        {
            var sectionTangent = tangentsAtGaussPoints[g];
            ArgumentNullException.ThrowIfNull(sectionTangent);
            if (sectionTangent.GetLength(0) != 3 || sectionTangent.GetLength(1) != 3)
                throw new ArgumentException("Матрица сечения должна быть 3×3.", nameof(tangentsAtGaussPoints));

            var b = StrainMatrix(lengthM, GaussXi[g]);
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

    /// <summary>Вектор внутренних сил элемента f_int = ∫Bᵀ(xi)·Q(xi) dx по той же двухточечной
    /// квадратуре (Срез 7). Q — внутренние усилия [N, My, Mz] сечения в точке квадратуры.
    ///
    /// Для линейного отклика (Q = D·B·u) результат тождественно равен K·u, поэтому нелинейное
    /// извлечение эпюры через f_int − f^consistent совпадает с правилом Среза 6 в линейном
    /// случае и не является его заменой.</summary>
    public static double[] InternalForces(IReadOnlyList<double[]> resultantsAtGaussPoints, double lengthM)
    {
        ArgumentNullException.ThrowIfNull(resultantsAtGaussPoints);
        if (resultantsAtGaussPoints.Count != GaussXi.Count)
            throw new ArgumentException(
                $"Нужно {GaussXi.Count} наборов усилий — по одному на точку квадратуры.",
                nameof(resultantsAtGaussPoints));

        double weight = lengthM / 2.0;
        var f = new double[DofPerElement];
        for (int g = 0; g < GaussXi.Count; g++)
        {
            var q = resultantsAtGaussPoints[g];
            ArgumentNullException.ThrowIfNull(q);
            if (q.Length != 3)
                throw new ArgumentException("Усилия сечения должны иметь три компоненты.",
                    nameof(resultantsAtGaussPoints));

            var b = StrainMatrix(lengthM, GaussXi[g]);
            for (int j = 0; j < DofPerElement; j++)
            {
                double sum = 0.0;
                for (int m = 0; m < 3; m++)
                    sum += b[m, j] * q[m];
                f[j] += weight * sum;
            }
        }
        return f;
    }

    /// <summary>Обобщённые деформации [ε₀, κy, κz] в точке xi по перемещениям элемента.</summary>
    public static double[] Strains(double[] elementDisplacements, double lengthM, double xi)
    {
        ArgumentNullException.ThrowIfNull(elementDisplacements);
        if (elementDisplacements.Length != DofPerElement)
            throw new ArgumentException($"Нужно {DofPerElement} перемещений элемента.",
                nameof(elementDisplacements));

        var b = StrainMatrix(lengthM, xi);
        var strains = new double[3];
        for (int m = 0; m < 3; m++)
        {
            double sum = 0.0;
            for (int j = 0; j < DofPerElement; j++)
                sum += b[m, j] * elementDisplacements[j];
            strains[m] = sum;
        }
        return strains;
    }

    /// <summary>Раскладывает узловую нагрузку элемента в вектор по DOF элемента.</summary>
    public static double[] LoadVector(StripElementNodalLoad load) =>
    [
        load.N1, load.Vy1, load.Vz1, load.My1, load.Mz1,
        load.N2, load.Vy2, load.Vz2, load.My2, load.Mz2
    ];
}
