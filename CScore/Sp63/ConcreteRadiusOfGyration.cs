namespace CScore.Sp63
{
    /// <summary>
    /// Радиусы инерции бетонного сечения брутто для условия гибкости l0/i &gt; 14
    /// (п. 8.1.2 СП 63.13330.2018). Учитываются только бетонные области (внешний контур
    /// минус отверстия), арматура не приводится; моменты инерции берутся относительно
    /// центра тяжести бетонного сечения.
    /// </summary>
    public static class ConcreteRadiusOfGyration
    {
        /// <summary>Радиусы инерции бетонного сечения.</summary>
        /// <param name="RadiusX">i относительно оси X (изгиб моментом Mx), м.</param>
        /// <param name="RadiusY">i относительно оси Y (изгиб моментом My), м.</param>
        /// <param name="Area">Площадь бетона брутто, м².</param>
        public readonly record struct Result(double RadiusX, double RadiusY, double Area);

        /// <summary>Возвращает радиусы инерции или null, если в сечении нет бетонных контуров.</summary>
        public static Result? Compute(CrossSection section)
        {
            ArgumentNullException.ThrowIfNull(section);

            double a = 0, sx = 0, sy = 0, ix = 0, iy = 0;
            foreach (var area in section.Areas.Where(IsConcreteRegion))
            {
                if (area.Hull is null) continue;
                Accumulate(area.Hull, 1.0, ref a, ref sx, ref sy, ref ix, ref iy);
                foreach (var hole in area.Holes)
                    Accumulate(hole, -1.0, ref a, ref sx, ref sy, ref ix, ref iy);
            }

            if (!(a > 1e-12)) return null;

            double xc = sy / a, yc = sx / a;
            double ixc = ix - a * yc * yc;
            double iyc = iy - a * xc * xc;
            if (!(ixc > 0) || !(iyc > 0)) return null;
            return new Result(Math.Sqrt(ixc / a), Math.Sqrt(iyc / a), a);
        }

        static bool IsConcreteRegion(MaterialArea area) =>
            area.Category == AreaCategory.Region &&
            area.HostAreaId == null &&
            MaterialArea.IsCalcActive(area) &&
            area.Material is { } material &&
            (material.Type == MatType.Concrete ||
             (material.Type == MatType.Custom && material.BaseType == MatType.Concrete));

        /// <summary>
        /// Добавляет характеристики контура по формулам Грина со знаком sign
        /// (+1 — внешний контур, −1 — отверстие) независимо от обхода вершин.
        /// </summary>
        static void Accumulate(Contour contour, double sign, ref double a, ref double sx,
            ref double sy, ref double ix, ref double iy)
        {
            int count = Math.Min(contour.X.Count, contour.Y.Count);
            if (count < 3) return;

            double ca = 0, csx = 0, csy = 0, cix = 0, ciy = 0;
            for (int k = 0; k < count; k++)
            {
                double x0 = contour.X[k], y0 = contour.Y[k];
                double x1 = contour.X[(k + 1) % count], y1 = contour.Y[(k + 1) % count];
                double cross = x0 * y1 - x1 * y0;
                ca += cross / 2.0;
                csy += (x0 + x1) * cross / 6.0;
                csx += (y0 + y1) * cross / 6.0;
                ciy += (x0 * x0 + x0 * x1 + x1 * x1) * cross / 12.0;
                cix += (y0 * y0 + y0 * y1 + y1 * y1) * cross / 12.0;
            }

            // Обход по часовой стрелке даёт отрицательные интегралы — нормируем знак.
            double orientation = ca < 0 ? -1.0 : 1.0;
            double factor = sign * orientation;
            a += factor * ca;
            sx += factor * csx;
            sy += factor * csy;
            ix += factor * cix;
            iy += factor * ciy;
        }
    }
}
