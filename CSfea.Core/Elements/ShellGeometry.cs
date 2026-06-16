using CSfea.Sparse;

namespace CSfea.Core;

/// <summary>
/// Геометрия оболочечного элемента: локальный базис, проекция в плоскость
/// элемента и блочное преобразование локальных 5-DOF/узел в глобальные
/// 6-DOF/узел. Порт геометрической части <c>fea/core.py</c>
/// (<c>local_frame</c>, <c>project_to_local</c>, <c>build_T_matrix</c>,
/// <c>assemble_global</c>).
/// Координаты узлов — массив векторов <c>double[][]</c> (n строк по 3).
/// </summary>
public static class ShellGeometry
{
    /// <summary>Параметр малой жёсткости drilling (θz) по умолчанию.</summary>
    public const double DefaultDrilling = 1.0e-6;

    /// <summary>
    /// Ортонормированный базис элемента (3x3): строки — [ex, ey, ez] в
    /// глобальных осях.
    /// </summary>
    public static double[,] LocalFrame(double[][] coords)
    {
        int n = coords.Length;
        double[] v1, v2;
        if (n == 3)
        {
            v1 = Dense.SubV(coords[1], coords[0]);
            v2 = Dense.SubV(coords[2], coords[0]);
        }
        else if (n == 4)
        {
            v1 = Dense.SubV(coords[2], coords[0]); // диагональ 1
            v2 = Dense.SubV(coords[3], coords[1]); // диагональ 2
        }
        else
        {
            throw new ArgumentException("Поддерживаются только 3 или 4 узла.");
        }

        var vn = Dense.Cross(v1, v2);
        double nn = Dense.Norm(vn);
        if (nn < 1e-14)
            throw new ArgumentException("Вырожденный элемент (нулевая площадь).");
        vn = Dense.ScaleV(vn, 1.0 / nn);

        var vx = Dense.SubV(coords[1], coords[0]);
        double proj = Dense.Dot(vx, vn);
        vx = Dense.SubV(vx, Dense.ScaleV(vn, proj));
        vx = Dense.ScaleV(vx, 1.0 / Dense.Norm(vx));
        var vy = Dense.Cross(vn, vx);

        return new[,]
        {
            { vx[0], vx[1], vx[2] },
            { vy[0], vy[1], vy[2] },
            { vn[0], vn[1], vn[2] },
        };
    }

    /// <summary>Проекция узлов в локальную плоскость.</summary>
    /// <returns>xy (n,2), R (3,3), origin (3,).</returns>
    public static (double[,] Xy, double[,] R, double[] Origin) ProjectToLocal(double[][] coords)
    {
        var r = LocalFrame(coords);
        var origin = (double[])coords[0].Clone();
        int n = coords.Length;
        var xy = new double[n, 2];
        for (int i = 0; i < n; i++)
        {
            var d = Dense.SubV(coords[i], origin);
            // xy[i,k] = dot(d, R[k]) для k=0,1
            for (int k = 0; k < 2; k++)
                xy[i, k] = d[0] * r[k, 0] + d[1] * r[k, 1] + d[2] * r[k, 2];
        }
        return (xy, r, origin);
    }

    /// <summary>
    /// Блочная матрица T (6n x 5n): u_global = T · u_local_extended, где
    /// локальный вектор содержит [u, v, w, θx, θy] на узел.
    /// </summary>
    public static double[,] BuildTMatrix(double[,] r, int nnodes)
    {
        // Rt = R^T: столбцы — локальные оси в глобальной системе.
        // T56 (6x5)
        var t56 = new double[6, 5];
        // перемещения: T56[0:3,0:3] = Rt
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
                t56[i, j] = r[j, i];        // Rt[i,j] = R[j,i]
        // повороты: T56[3:6,3:5] = Rt[:,0:2]
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 2; j++)
                t56[3 + i, 3 + j] = r[j, i];

        var t = new double[6 * nnodes, 5 * nnodes];
        for (int node = 0; node < nnodes; node++)
            for (int i = 0; i < 6; i++)
                for (int j = 0; j < 5; j++)
                    t[6 * node + i, 5 * node + j] = t56[i, j];
        return t;
    }

    /// <summary>Результат преобразования локальной матрицы в глобальную.</summary>
    public readonly record struct GlobalAssembly(double[,] KGlobal, double[,] R, double[,] T);

    /// <summary>
    /// Преобразовать локальную K (5n x 5n) в глобальную (6n x 6n) с
    /// drilling-стабилизацией по θz.
    /// </summary>
    public static GlobalAssembly AssembleGlobal(double[,] kLoc, double[][] coords,
                                                double drilling = DefaultDrilling)
    {
        int n = coords.Length;
        var r = LocalFrame(coords);
        var t = BuildTMatrix(r, n);
        // K_global = T · Kloc · T^T
        var kGlobal = Dense.MatMul(Dense.MatMul(t, kLoc), Dense.Transpose(t));

        double kRef = Dense.MaxAbs(Dense.Diagonal(kLoc)) * drilling;
        AddDrilling(kGlobal, r, n, kRef);
        return new GlobalAssembly(kGlobal, r, t);
    }

    /// <summary>
    /// Добавить drilling-жёсткость k_ref·(ez ⊗ ez) на угловые DOF каждого узла.
    /// </summary>
    public static void AddDrilling(double[,] kGlobal, double[,] r, int n, double kRef)
    {
        var ez = new[] { r[2, 0], r[2, 1], r[2, 2] };
        for (int node = 0; node < n; node++)
        {
            int b = 6 * node + 3;
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                    kGlobal[b + i, b + j] += kRef * ez[i] * ez[j];
        }
    }
}
