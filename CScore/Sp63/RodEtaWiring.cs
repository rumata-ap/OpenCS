using System;

namespace CScore.Sp63
{
    /// <summary>
    /// Решателе-независимая обвязка коэффициента η (п. 8.1.15) для сечения с
    /// двумя плоскостями изгиба (Mx/My). Определяет высоту сечения в каждой
    /// плоскости автоматически (ограничивающий прямоугольник), выбирает режим
    /// (буквальный/итерационный) и усиливает моменты последовательно: сначала
    /// ось X (при исходном My), затем ось Y (уже при усиленном Mx) — решатель
    /// НДС трёхпараметрический и совместное решение по обеим осям всё равно
    /// требуется на каждом проходе итеративного режима.
    /// </summary>
    public static class RodEtaWiring
    {
        /// <summary>Диагностика η для одной оси изгиба.</summary>
        /// <param name="Eta">Коэффициент η.</param>
        /// <param name="Ncr">Условная критическая сила, кН.</param>
        /// <param name="D">Использованная жёсткость (по формуле — режим A, из решателя — режим B), кН·м².</param>
        /// <param name="L0">Расчётная длина, м (как задана пользователем).</param>
        /// <param name="H">Высота сечения в этой плоскости изгиба, м (авто — ограничивающий прямоугольник).</param>
        /// <param name="Slender">Гибкость l0/i превышает порог (по умолчанию 14, п. 8.1.2).</param>
        /// <param name="Stable">false — потеря устойчивости.</param>
        public readonly record struct AxisDiagnostics(
            double Eta, double Ncr, double D, double L0, double H,
            bool Slender, bool Stable,
            int Iterations, bool ExtrapolationFailed)
        {
            /// <summary>Радиус инерции бетонного сечения брутто в этой плоскости изгиба, м.</summary>
            public double I { get; init; }

            /// <summary>
            /// true — бетонных контуров в сечении не нашлось (или I/A неположительно),
            /// поэтому радиус инерции принят по габариту как h/√12. Это приближение:
            /// гибкость l0/i считается по нему, и признак выносится наружу.
            /// </summary>
            public bool RadiusFromBoundingBox { get; init; }

            /// <summary>Последовательность η по проходам режима B (см. <see cref="EccentricityAmplifier.EtaResult.EtaHistory"/>).</summary>
            public double[] EtaHistory { get; init; } = Array.Empty<double>();
        }

        /// <summary>
        /// Радиусы инерции бетонного сечения брутто в плоскостях изгиба Mx/My (м).
        /// Если бетонных контуров нет (или момент инерции неположителен), радиусы
        /// оцениваются по габариту как для прямоугольника (h/√12), а
        /// <c>FromBoundingBox</c> получает true — это приближение, которое выводится
        /// пользователю (JSON-поле radiusFallbackX/Y и предупреждение в отчёте).
        /// </summary>
        /// <param name="section">Сечение.</param>
        /// <param name="hx">Габарит в плоскости изгиба Mx (размер по Y), м.</param>
        /// <param name="hy">Габарит в плоскости изгиба My (размер по X), м.</param>
        public static (double Ix, double Iy, bool FromBoundingBox) ResolveRadii(
            CrossSection section, double hx, double hy)
        {
            var gyration = ConcreteRadiusOfGyration.Compute(section);
            if (gyration is { } g) return (g.RadiusX, g.RadiusY, false);

            return (hx / Math.Sqrt(12.0), hy / Math.Sqrt(12.0), true);
        }

        /// <summary>Результат усиления моментов по обеим осям.</summary>
        public readonly record struct Result(
            double MxEff, double MyEff, AxisDiagnostics X, AxisDiagnostics Y);

        /// <summary>
        /// Усиливает Mx/My по п. 8.1.15. <paramref name="jointSolve"/> — обёртка
        /// над решателем НДС: при пробных (mx,my) и неизменном n возвращает
        /// достигнутую плоскость деформаций (используется только в режиме
        /// <paramref name="iterative"/> = true; в буквальном режиме не вызывается).
        /// </summary>
        public static Result Apply(
            CrossSection section, double n, double mx0, double my0,
            double l0x, double l0y, double psiX, double psiY,
            bool iterative, Func<double, double, Kurvature> jointSolve,
            double slendernessThreshold = EccentricityAmplifier.SlendernessThreshold)
        {
            var (minX, maxX, minY, maxY) = section.SectionBoundingBox();
            double hx = maxY - minY; // высота в плоскости изгиба Mx (варьируется по Y)
            double hy = maxX - minX; // высота в плоскости изгиба My (варьируется по X)

            // Гибкость по п. 8.1.2 — l0/i по бетонному сечению брутто; если бетонных
            // контуров нет, радиус инерции оценивается по габариту как для прямоугольника
            // (см. ResolveRadii): признак подмены уходит в AxisDiagnostics.
            var (ix, iy, radiusFallback) = ResolveRadii(section, hx, hy);

            double mxEff, myEff;
            EccentricityAmplifier.EtaResult exResult, eyResult;

            if (iterative)
            {
                exResult = EccentricityAmplifier.AmplifyIterative(
                    n, mx0, l0x, h: hx, i: ix,
                    solveCurvature: mxTrial => jointSolve(mxTrial, my0).ky,
                    passes: 3, slendernessThreshold: slendernessThreshold);
                mxEff = exResult.MEff;

                eyResult = EccentricityAmplifier.AmplifyIterative(
                    n, my0, l0y, h: hy, i: iy,
                    solveCurvature: myTrial => jointSolve(mxEff, myTrial).kz,
                    passes: 3, slendernessThreshold: slendernessThreshold);
                myEff = eyResult.MEff;
            }
            else
            {
                var split = section.SplitStiffnessByMaterial();

                exResult = EccentricityAmplifier.AmplifyFormula(
                    n, mx0, l0x, h: hx, i: ix,
                    eiConcrete: split.EIxConcrete, eiRebar: split.EIxRebar,
                    psi: psiX, slendernessThreshold: slendernessThreshold);
                mxEff = exResult.MEff;

                eyResult = EccentricityAmplifier.AmplifyFormula(
                    n, my0, l0y, h: hy, i: iy,
                    eiConcrete: split.EIyConcrete, eiRebar: split.EIyRebar,
                    psi: psiY, slendernessThreshold: slendernessThreshold);
                myEff = eyResult.MEff;
            }

            return new Result(mxEff, myEff,
                new AxisDiagnostics(exResult.Eta, exResult.Ncr, exResult.D, l0x, hx, exResult.Slender, exResult.Stable,
                    exResult.Iterations, exResult.ExtrapolationFailed)
                { EtaHistory = exResult.EtaHistory, I = ix, RadiusFromBoundingBox = radiusFallback },
                new AxisDiagnostics(eyResult.Eta, eyResult.Ncr, eyResult.D, l0y, hy, eyResult.Slender, eyResult.Stable,
                    eyResult.Iterations, eyResult.ExtrapolationFailed)
                { EtaHistory = eyResult.EtaHistory, I = iy, RadiusFromBoundingBox = radiusFallback });
        }
    }
}
