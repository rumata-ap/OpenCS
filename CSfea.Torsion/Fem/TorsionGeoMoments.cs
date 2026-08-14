namespace CSfea.Torsion;

/// <summary>
/// Геометрические моменты сечения (площадь, центроид, Ixx/Iyy/Ixy) по формулам Грина
/// напрямую от контура <see cref="TorsionBoundary"/> (внешний CCW + отверстия CW) — точно,
/// без погрешности триангуляции (в отличие от суммирования по сетке: для тонкостенных
/// симметричных профилей дискретизация вносит заметную невязку в Ixy, которая аналитически
/// должна быть строго нулевой). В отличие от <c>CScore.GeoProps</c> знак Ixy не теряется
/// (там стоит Math.Abs).
/// </summary>
public static class TorsionGeoMoments
{
    /// <summary>Центроид (Xc, Yc) и центральные моменты Ixx=∫y²dA, Iyy=∫x²dA, Ixy=∫xy dA.</summary>
    public static (double Xc, double Yc, double Ixx, double Iyy, double Ixy) Compute(TorsionBoundary boundary)
    {
        double area = 0.0, sx = 0.0, sy = 0.0;
        double ixO = 0.0, iyO = 0.0, ixyO = 0.0; // относительно начала координат

        AccumulateLoop(boundary.OuterX, boundary.OuterY, ref area, ref sx, ref sy, ref ixO, ref iyO, ref ixyO);
        if (boundary.Holes != null)
            foreach (var (hx, hy) in boundary.Holes)
                AccumulateLoop(hx, hy, ref area, ref sx, ref sy, ref ixO, ref iyO, ref ixyO);

        double xc = sy / area, yc = sx / area;
        // Перенос на центроид (теорема Штейнера).
        double ixx = ixO / 12.0 - area * yc * yc;
        double iyy = iyO / 12.0 - area * xc * xc;
        double ixy = ixyO / 24.0 - area * xc * yc;
        return (xc, yc, ixx, iyy, ixy);
    }

    static void AccumulateLoop(double[] x, double[] y,
        ref double area, ref double sx, ref double sy,
        ref double ixO, ref double iyO, ref double ixyO)
    {
        int n = x.Length;
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            Accumulate(x[i], y[i], x[j], y[j], ref area, ref sx, ref sy, ref ixO, ref iyO, ref ixyO);
        }
    }

    /// <summary>Накопление вклада ребра (x0,y0)→(x1,y1) в интегралы Грина по замкнутому контуру.</summary>
    static void Accumulate(double x0, double y0, double x1, double y1,
        ref double area, ref double sx, ref double sy,
        ref double ixO, ref double iyO, ref double ixyO)
    {
        double cross = x0 * y1 - x1 * y0;
        area += 0.5 * cross;
        sx += (y0 + y1) * cross / 6.0;
        sy += (x0 + x1) * cross / 6.0;
        iyO += (x0 * x0 + x0 * x1 + x1 * x1) * cross;
        ixO += (y0 * y0 + y0 * y1 + y1 * y1) * cross;
        // Коэффициенты (1,2,2,1), а не (1,1,1,1) — см. вывод формулы Грина для ∫xy dA
        // (проверено на контрольном треугольнике, сдвинутом от начала координат).
        ixyO += (x0 * y1 + 2.0 * x0 * y0 + 2.0 * x1 * y1 + x1 * y0) * cross;
    }
}
