using System;
using OpenCS.OpenSees.Structural;

namespace OpenCS.OpenSees.CScore
{
    /// <summary>
    /// Поворот shell section resultants в другие оси в плоскости оболочки и конверсия единиц
    /// (Срез 7 стержневой аналогии полосы плиты).
    ///
    /// <b>Почему это отдельный примитив.</b> Nx/Ny/Nxy (и Mx/My/Mxy) — компоненты тензора
    /// второго ранга, поэтому переводятся преобразованием Мора <c>N' = R·N·Rᵀ</c>, а не как
    /// вектор. Существующие конвертеры этого не умеют: Frame3DConverter.ToShellFrame
    /// покомпонентно копирует базис, PlanarBoundaryFrameConverter работает с векторами, точками
    /// и переносом момента. Qx/Qy — наоборот, вектор, и поворачиваются как вектор.
    ///
    /// <b>Единицы.</b> OpenSees выдаёт СИ (Н/м, Н·м/м), домен полосы работает в кН — то же
    /// основание и тот же множитель, что в ShellMeshPatchPlateSectionResponse. Конверсия здесь
    /// явная и отделена от поворота, чтобы её нельзя было пропустить незаметно.
    /// </summary>
    public static class ShellResultantRotation
    {
        /// <summary>Множитель перевода из СИ (Н) в кН.</summary>
        public const double NewtonToKilonewton = 1.0 / 1000.0;

        /// <summary>Повернуть resultants на угол angleRad от исходных осей к целевым.</summary>
        public static ShellSectionResultants Rotate(ShellSectionResultants source, double angleRad)
        {
            ArgumentNullException.ThrowIfNull(source);
            if (!double.IsFinite(angleRad))
                throw new ArgumentOutOfRangeException(nameof(angleRad), "Угол поворота должен быть конечным.");

            double c = Math.Cos(angleRad), s = Math.Sin(angleRad);
            double cc = c * c, ss = s * s, cs = c * s;

            var (nx, ny, nxy) = RotateTensor(source.Nx, source.Ny, source.Nxy, cc, ss, cs);
            var (mx, my, mxy) = RotateTensor(source.Mx, source.My, source.Mxy, cc, ss, cs);

            // Поперечные силы — вектор, а не тензор.
            double qx = source.Qx * c + source.Qy * s;
            double qy = -source.Qx * s + source.Qy * c;

            return new ShellSectionResultants(
                source.ElementTag, source.IntegrationPoint, nx, ny, nxy, mx, my, mxy, qx, qy);
        }

        /// <summary>Перевести resultants из Н/м и Н·м/м в кН/м и кН·м/м.</summary>
        public static ShellSectionResultants ToKilonewton(ShellSectionResultants source)
        {
            ArgumentNullException.ThrowIfNull(source);
            const double k = NewtonToKilonewton;
            return new ShellSectionResultants(
                source.ElementTag, source.IntegrationPoint,
                source.Nx * k, source.Ny * k, source.Nxy * k,
                source.Mx * k, source.My * k, source.Mxy * k,
                source.Qx * k, source.Qy * k);
        }

        /// <summary>Угол поворота от осей оболочки к осям полосы: обе задаются своими
        /// направляющими в глобальных координатах.</summary>
        public static double AngleBetween(
            double shellXx, double shellXy, double shellXz,
            double stripXx, double stripXy, double stripXz,
            double normalX, double normalY, double normalZ)
        {
            double dot = shellXx * stripXx + shellXy * stripXy + shellXz * stripXz;
            // Знак угла задаётся нормалью плоскости: (shellX × stripX) · n.
            double crossX = shellXy * stripXz - shellXz * stripXy;
            double crossY = shellXz * stripXx - shellXx * stripXz;
            double crossZ = shellXx * stripXy - shellXy * stripXx;
            double sin = crossX * normalX + crossY * normalY + crossZ * normalZ;
            return Math.Atan2(sin, dot);
        }

        static (double A, double B, double AB) RotateTensor(
            double a, double b, double ab, double cc, double ss, double cs)
        {
            double rotatedA = a * cc + b * ss + 2.0 * ab * cs;
            double rotatedB = a * ss + b * cc - 2.0 * ab * cs;
            double rotatedAb = (b - a) * cs + ab * (cc - ss);
            return (rotatedA, rotatedB, rotatedAb);
        }
    }
}
