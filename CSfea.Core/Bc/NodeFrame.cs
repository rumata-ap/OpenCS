using CSfea.Sparse;

namespace CSfea.Core;

/// <summary>
/// Локальная система координат узла для пружин/закреплений. Строит матрицу
/// поворота R (n×n) под ndof_per_node ∈ {3, 6}. Порт
/// <c>boundary_conditions.py: _build_rotation_matrix</c>.
/// </summary>
public abstract class NodeFrame
{
    /// <summary>Матрица поворота (n×n) под заданное число DOF на узел.</summary>
    public abstract double[,] Rotation(int ndofPerNode);

    /// <summary>Угол поворота вокруг Z (для 2D-рам, 3 DOF/узел).</summary>
    public static NodeFrame Angle(double angle) => new AngleFrame(angle);

    /// <summary>Пара ортогональных осей e1, e2 (для 6 DOF/узел); e3 = e1×e2.</summary>
    public static NodeFrame Axes(double[] e1, double[] e2) => new AxesFrame(e1, e2);

    /// <summary>Готовая матрица поворота 2×2 (2D) или 3×3 (3D).</summary>
    public static NodeFrame Matrix(double[,] r) => new MatrixFrame(r);

    /// <summary>Единичная матрица (frame = None).</summary>
    public static double[,] Identity(int n)
    {
        var r = new double[n, n];
        for (int i = 0; i < n; i++) r[i, i] = 1.0;
        return r;
    }

    /// <summary>Построить R (n×n) для опционального фрейма (null → единичная).</summary>
    public static double[,] Build(NodeFrame? frame, int n) => frame?.Rotation(n) ?? Identity(n);

    private sealed class AngleFrame : NodeFrame
    {
        private readonly double _angle;
        public AngleFrame(double angle) => _angle = angle;

        public override double[,] Rotation(int n)
        {
            if (n != 3)
                throw new ArgumentException("Угловой frame применим только для 3 DOF/узел.");
            double c = Math.Cos(_angle), s = Math.Sin(_angle);
            var r = Identity(n);
            r[0, 0] = c; r[0, 1] = s;
            r[1, 0] = -s; r[1, 1] = c;
            return r;
        }
    }

    private sealed class AxesFrame : NodeFrame
    {
        private readonly double[] _e1;
        private readonly double[] _e2;
        public AxesFrame(double[] e1, double[] e2) { _e1 = e1; _e2 = e2; }

        public override double[,] Rotation(int n)
        {
            if (n != 6)
                throw new ArgumentException("Осевой frame применим только для 6 DOF/узел.");
            var e1 = Dense.ScaleV(_e1, 1.0 / Dense.Norm(_e1));
            var e2 = Dense.ScaleV(_e2, 1.0 / Dense.Norm(_e2));
            if (Math.Abs(Dense.Dot(e1, e2)) > 1e-8)
                throw new ArgumentException("e1 и e2 должны быть ортогональны.");
            var e3 = Dense.Cross(e1, e2);
            // R3 = столбцы [e1, e2, e3]
            var r3 = new double[3, 3];
            for (int i = 0; i < 3; i++) { r3[i, 0] = e1[i]; r3[i, 1] = e2[i]; r3[i, 2] = e3[i]; }
            return BlockDiag3(r3);
        }
    }

    private sealed class MatrixFrame : NodeFrame
    {
        private readonly double[,] _r;
        public MatrixFrame(double[,] r) => _r = r;

        public override double[,] Rotation(int n)
        {
            int m = _r.GetLength(0);
            if (n == 3 && m == 2)
            {
                var r = Identity(3);
                r[0, 0] = _r[0, 0]; r[0, 1] = _r[0, 1];
                r[1, 0] = _r[1, 0]; r[1, 1] = _r[1, 1];
                return r;
            }
            if (n == 6 && m == 3)
                return BlockDiag3(_r);
            throw new ArgumentException("Несовместимая размерность матрицы frame.");
        }
    }

    private static double[,] BlockDiag3(double[,] r3)
    {
        var r = Identity(6);
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
            {
                r[i, j] = r3[i, j];
                r[3 + i, 3 + j] = r3[i, j];
            }
        return r;
    }
}
