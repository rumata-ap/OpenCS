namespace CScore.PlateStrip;

/// <summary>Двухузловой элемент производной балки полосы Тимошенко с кручением (Срез 8b).
/// Шесть степеней свободы в узле в порядке u, v, w, θx, θy, θz.
///
/// Конвенция та же, что у <see cref="StripBeamElement"/>: ε₀ = u′, θz = v′, θy = −w′ (при
/// нулевом сдвиге), κy = θy′, κz = θz′; добавляются θx = ∂w/∂y и кручение θx′. Поперечный
/// сдвиг: γz = w′ + θy, γy = v′ − θz. Для плоскости xz угол сечения в классической записи
/// Тимошенко ψ = −θy, для плоскости xy ψ = θz.
///
/// Поле перемещений — функции формы Фридмана–Косматки (точное решение однородного уравнения
/// Тимошенко): κ линейна по x, γ постоянна на элементе. Параметр Φ = 12·EI/(GA·L²) строится по
/// <b>начальным</b> EI и GA и дальше не перестраивается: в нелинейном режиме это
/// приближение формы, а не отклика (сходимость Ньютона определяется невязкой внутренних сил).
/// При Φ = 0 элемент совпадает с Эйлером–Бернулли.
///
/// Обобщённые деформации в точке: [ε₀, κy, κz, θx′, γy, γz]; усилия [N, My, Mz, T, Vy, Vz].
/// Внутренний расчётный примитив: некорректные входы — исключение.</summary>
public static class StripBeamElementTimoshenko
{
    public const int DofPerNode = 6;
    public const int DofPerElement = 12;
    public const int StrainCount = 6;

    /// <summary>Индексы DOF в узле.</summary>
    public const int U = 0, V = 1, W = 2, ThetaX = 3, ThetaY = 4, ThetaZ = 5;

    /// <summary>Параметр сдвига Φ = 12·EI/(GA·L²); бесконечная сдвиговая жёсткость даёт 0.</summary>
    public static double ShearParameter(double ei, double ga, double lengthM)
    {
        if (!(lengthM > 0.0) || !double.IsFinite(lengthM))
            throw new ArgumentOutOfRangeException(nameof(lengthM),
                "Длина элемента должна быть конечной и положительной.");
        if (double.IsPositiveInfinity(ga)) return 0.0;
        if (!(ga > 0.0) || !double.IsFinite(ga))
            throw new ArgumentOutOfRangeException(nameof(ga),
                "Сдвиговая жёсткость должна быть положительной.");
        return 12.0 * Math.Max(ei, 0.0) / (ga * lengthM * lengthM);
    }

    /// <summary>Матрица деформаций B (6×12): строки [ε₀, κy, κz, θx′, γy, γz].
    /// phiY — параметр плоскости xz (изгиб из плоскости, EIy/GAz), phiZ — плоскости xy.</summary>
    public static double[,] StrainMatrix(double lengthM, double xi, double phiY, double phiZ)
    {
        double l = lengthM;
        var b = new double[StrainCount, DofPerElement];

        b[0, U] = -1.0 / l;
        b[0, DofPerNode + U] = 1.0 / l;
        b[3, ThetaX] = -1.0 / l;
        b[3, DofPerNode + ThetaX] = 1.0 / l;

        // Плоскость xz: d = (w1, ψ1 = −θy1, w2, ψ2 = −θy2); κy = −ψ′, γz = w′ − ψ.
        var (dMy, gMy) = PlaneRows(xi, phiY, l);
        // d-вектор (w1, ψ1, w2, ψ2) → DOF (W, ThetaY, W', ThetaY'); ψ = −θy.
        int[] xzDof = [W, ThetaY, DofPerNode + W, DofPerNode + ThetaY];
        double[] xzSign = [1.0, -1.0, 1.0, -1.0];
        for (int i = 0; i < 4; i++)
        {
            b[1, xzDof[i]] += -dMy[i] * xzSign[i];     // κy = −ψ′
            b[5, xzDof[i]] += gMy[i] * xzSign[i];      // γz = w′ − ψ
        }

        // Плоскость xy: d = (v1, θz1, v2, θz2); κz = ψ′, γy = v′ − ψ, ψ = θz.
        var (dMz, gMz) = PlaneRows(xi, phiZ, l);
        int[] xyDof = [V, ThetaZ, DofPerNode + V, DofPerNode + ThetaZ];
        for (int i = 0; i < 4; i++)
        {
            b[2, xyDof[i]] += dMz[i];
            b[4, xyDof[i]] += gMz[i];
        }
        return b;
    }

    /// <summary>Строки (ψ′, w′ − ψ) по DOF (w1, ψ1, w2, ψ2) для функций формы Фридмана–Косматки.</summary>
    static (double[] PsiPrime, double[] Shear) PlaneRows(double xi, double phi, double l)
    {
        double d = 1.0 + phi;
        // ψ′ = M′(ξ)/L
        double[] psiPrime =
        [
            -6.0 * (1.0 - 2.0 * xi) / (l * l * d),
            (-4.0 + 6.0 * xi - phi) / (l * d),
            6.0 * (1.0 - 2.0 * xi) / (l * l * d),
            (-2.0 + 6.0 * xi + phi) / (l * d)
        ];
        // γ = w′ − ψ постоянна: берём значение в любой точке — формулы Φ/(…)
        double[] shear =
        [
            -phi / (l * d),
            -phi / (2.0 * d),
            phi / (l * d),
            -phi / (2.0 * d)
        ];
        return (psiPrime, shear);
    }

    /// <summary>Вектор внутренних сил f_int = ∫Bᵀ·[Q; Vy; Vz] dx по двухточечной квадратуре.
    /// Q — [N, My, Mz, T] в точке квадратуры, shear — [Vy, Vz].</summary>
    public static double[] InternalForces(
        IReadOnlyList<double[]> resultants, IReadOnlyList<double[]> shears,
        double lengthM, double phiY, double phiZ)
    {
        var gauss = StripBeamElement.GaussXi;
        double weight = lengthM / 2.0;
        var f = new double[DofPerElement];
        for (int g = 0; g < gauss.Count; g++)
        {
            var b = StrainMatrix(lengthM, gauss[g], phiY, phiZ);
            double[] s = [resultants[g][0], resultants[g][1], resultants[g][2], resultants[g][3],
                          shears[g][0], shears[g][1]];
            for (int j = 0; j < DofPerElement; j++)
            {
                double sum = 0.0;
                for (int m = 0; m < StrainCount; m++)
                    sum += b[m, j] * s[m];
                f[j] += weight * sum;
            }
        }
        return f;
    }

    /// <summary>Касательная ∫Bᵀ·diag(K4, GAy, GAz)·B dx; k4 — 4×4 по [ε₀, κy, κz, θ′].</summary>
    public static double[,] Stiffness(
        IReadOnlyList<double[,]> k4, IReadOnlyList<double> gaY, IReadOnlyList<double> gaZ,
        double lengthM, double phiY, double phiZ)
    {
        var gauss = StripBeamElement.GaussXi;
        double weight = lengthM / 2.0;
        var k = new double[DofPerElement, DofPerElement];
        for (int g = 0; g < gauss.Count; g++)
        {
            var b = StrainMatrix(lengthM, gauss[g], phiY, phiZ);
            var d = new double[StrainCount, StrainCount];
            for (int i = 0; i < 4; i++)
            for (int j = 0; j < 4; j++)
                d[i, j] = k4[g][i, j];
            d[4, 4] = double.IsPositiveInfinity(gaY[g]) ? 0.0 : gaY[g];
            d[5, 5] = double.IsPositiveInfinity(gaZ[g]) ? 0.0 : gaZ[g];

            var db = new double[StrainCount, DofPerElement];
            for (int i = 0; i < StrainCount; i++)
            for (int j = 0; j < DofPerElement; j++)
            {
                double sum = 0.0;
                for (int m = 0; m < StrainCount; m++)
                    sum += d[i, m] * b[m, j];
                db[i, j] = sum;
            }
            for (int i = 0; i < DofPerElement; i++)
            for (int j = 0; j < DofPerElement; j++)
            {
                double sum = 0.0;
                for (int m = 0; m < StrainCount; m++)
                    sum += b[m, i] * db[m, j];
                k[i, j] += weight * sum;
            }
        }
        return k;
    }

    /// <summary>Деформации [ε₀, κy, κz, θx′, γy, γz] по перемещениям элемента.</summary>
    public static double[] Strains(
        double[] elementDisplacements, double lengthM, double xi, double phiY, double phiZ)
    {
        var b = StrainMatrix(lengthM, xi, phiY, phiZ);
        var strains = new double[StrainCount];
        for (int m = 0; m < StrainCount; m++)
        {
            double sum = 0.0;
            for (int j = 0; j < DofPerElement; j++)
                sum += b[m, j] * elementDisplacements[j];
            strains[m] = sum;
        }
        return strains;
    }

    /// <summary>Вектор узловой нагрузки элемента в порядке DOF; t1, t2 — крутящие моменты узлов.</summary>
    public static double[] LoadVector(StripElementNodalLoad load, double t1 = 0.0, double t2 = 0.0) =>
    [
        load.N1, load.Vy1, load.Vz1, t1, load.My1, load.Mz1,
        load.N2, load.Vy2, load.Vz2, t2, load.My2, load.Mz2
    ];
}
