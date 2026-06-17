using CSmath.Geometry;

namespace CSmath.Geometry
{
    /// <summary>
    /// Плоскость в трёхмерном пространстве, заданная тремя точками.
    /// Уравнение плоскости: Ax + By + Cz + D = 0.
    /// </summary>
    [Serializable]
    public class Plane
    {
        Vector3D? p1;
        Vector3D? p2;
        Vector3D? p3;

        /// <summary>
        /// Коэффициент A уравнения плоскости Ax + By + Cz + D = 0.
        /// </summary>
        public double A { get; private set; }

        /// <summary>
        /// Коэффициент B уравнения плоскости Ax + By + Cz + D = 0.
        /// </summary>
        public double B { get; private set; }

        /// <summary>
        /// Коэффициент C уравнения плоскости Ax + By + Cz + D = 0.
        /// </summary>
        public double C { get; private set; }

        /// <summary>
        /// Свободный член D уравнения плоскости Ax + By + Cz + D = 0.
        /// </summary>
        public double D { get; private set; }

        /// <summary>
        /// Единичный вектор нормали к плоскости.
        /// </summary>
        public Vector3D Normal { get; private set; } = null!;

        /// <summary>
        /// Первая определяющая точка плоскости. При изменении автоматически пересчитываются коэффициенты.
        /// </summary>
        public Vector3D? P1 { get => p1; set { p1 = value; Update(); } }

        /// <summary>
        /// Вторая определяющая точка плоскости. При изменении автоматически пересчитываются коэффициенты.
        /// </summary>
        public Vector3D? P2 { get => p2; set { p2 = value; Update(); } }

        /// <summary>
        /// Третья определяющая точка плоскости. При изменении автоматически пересчитываются коэффициенты.
        /// </summary>
        public Vector3D? P3 { get => p3; set { p3 = value; Update(); } }

        /// <summary>
        /// Вектор направления V1 = P2 - P1.
        /// </summary>
        public Vector3D V1 { get; private set; }

        /// <summary>
        /// Вектор направления V2 = P3 - P1.
        /// </summary>
        public Vector3D V2 { get; private set; }

        /// <summary>
        /// Вектор кривизн: компоненты содержат коэффициенты -A/C, -B/C, -D/C,
        /// позволяющие выразить z через x и y: z = Kurvature.X * x + Kurvature.Y * y + Kurvature.Z.
        /// </summary>
        public Vector3D Kurvature { get; private set; } = null!;

        /// <summary>
        /// Создаёт плоскость, проходящую через три заданные точки.
        /// </summary>
        /// <param name="pt1">Первая точка, определяющая плоскость.</param>
        /// <param name="pt2">Вторая точка, определяющая плоскость.</param>
        /// <param name="pt3">Третья точка, определяющая плоскость.</param>
        public Plane(Vector3D pt1, Vector3D pt2, Vector3D pt3)
        {
            p1 = pt1;
            p2 = pt2;
            p3 = pt3;
            V1 = pt2 - pt1;
            V2 = pt3 - pt1;
            CalcPlane();
        }

        /// <summary>
        /// Вычисляет значение Z по заданным координатам X и Y на плоскости.
        /// </summary>
        /// <param name="x">Координата X.</param>
        /// <param name="y">Координата Y.</param>
        /// <returns>Значение координаты Z на плоскости.</returns>
        public double Interpolation(double x, double y)
        {
            return (-A * x - B * y - D) / C;
        }

        /// <summary>
        /// Вычисляет значения Z для массивов координат X и Y по уравнению плоскости.
        /// </summary>
        /// <param name="x">Массив координат X.</param>
        /// <param name="y">Массив координат Y.</param>
        /// <returns>Массив значений координаты Z на плоскости.</returns>
        public double[] Interpolation(IList<double> x, IList<double> y)
        {
            double[] z = new double[x.Count];
            for (int i = 0; i < z.Length; i++)
            {
                z[i] = (-A * x[i] - B * y[i] - D) / C;
            }
            return z;
        }

        /// <summary>
        /// Вычисляет значения Z для векторов координат X и Y по уравнению плоскости.
        /// </summary>
        /// <param name="x">Вектор координат X.</param>
        /// <param name="y">Вектор координат Y.</param>
        /// <returns>Вектор значений координаты Z на плоскости.</returns>
        public Vector Interpolation(Vector x, Vector y)
        {
            double[] z = new double[x.N];
            for (int i = 0; i < z.Length; i++)
            {
                z[i] = (-A * x[i] - B * y[i] - D) / C;
            }
            return new Vector(z);
        }

        /// <summary>
        /// Вычисляет значение Z для заданной 2D-точки по уравнению плоскости.
        /// </summary>
        /// <param name="point">Точка с координатами X и Y.</param>
        /// <returns>Значение координаты Z на плоскости.</returns>
        public double Interpolation(Vector2D point)
        {
            return (-A * point.X - B * point.Y - D) / C;
        }

        /// <summary>
        /// Обновляет Z-координаты определяющих точек плоскости и пересчитывает коэффициенты.
        /// Компоненты вектора eZ задают новые значения Z: P1.Z = eZ.X, P2.Z = eZ.Y, P3.Z = eZ.Z.
        /// </summary>
        /// <param name="eZ">Вектор с новыми Z-координатами для точек P1 (X), P2 (Y), P3 (Z).</param>
        public void Update(Vector3D eZ)
        {
            if (p1 == null || p2 == null || p3 == null) return;

            p1.Z = eZ.X;
            p2.Z = eZ.Y;
            p3.Z = eZ.Z;

            V1.Z = eZ.Y - eZ.X;
            V2.Z = eZ.Z - eZ.Y;

            CalcPlane();
        }

        void Update()
        {
            if (p1 == null || p2 == null || p3 == null) return;
            V1 = p2 - p1;
            V2 = p3 - p1;
            CalcPlane();
        }

        protected void CalcPlane()
        {
            Vector3D n = V1 ^ V2;
            Normal = n.Unit;
            A = Normal.X;
            B = Normal.Y;
            C = Normal.Z;
            D = -A * p1!.X - B * p1.Y - C * p1.Z;
            Kurvature = new Vector3D() { X = -A / C, Y = -B / C, Z = -D / C };
        }
    }
}