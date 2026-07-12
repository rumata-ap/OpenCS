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
        /// <param name="Slender">Гибкость l0/h &gt; 14 (п. 8.1.2).</param>
        /// <param name="Stable">false — потеря устойчивости.</param>
        public readonly record struct AxisDiagnostics(
            double Eta, double Ncr, double D, double L0, double H,
            bool Slender, bool Stable,
            int Iterations, bool ExtrapolationFailed)
        {
            /// <summary>Последовательность η по проходам режима B (см. <see cref="EccentricityAmplifier.EtaResult.EtaHistory"/>).</summary>
            public double[] EtaHistory { get; init; } = Array.Empty<double>();
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

            double mxEff, myEff;
            EccentricityAmplifier.EtaResult exResult, eyResult;

            if (iterative)
            {
                exResult = EccentricityAmplifier.AmplifyIterative(
                    n, mx0, l0x, hx,
                    mxTrial => jointSolve(mxTrial, my0).ky,
                    passes: 3, slendernessThreshold: slendernessThreshold);
                mxEff = exResult.MEff;

                eyResult = EccentricityAmplifier.AmplifyIterative(
                    n, my0, l0y, hy,
                    myTrial => jointSolve(mxEff, myTrial).kz,
                    passes: 3, slendernessThreshold: slendernessThreshold);
                myEff = eyResult.MEff;
            }
            else
            {
                var split = section.SplitStiffnessByMaterial();

                exResult = EccentricityAmplifier.AmplifyFormula(
                    n, mx0, l0x, hx, split.EIxConcrete, split.EIxRebar, psiX, slendernessThreshold);
                mxEff = exResult.MEff;

                eyResult = EccentricityAmplifier.AmplifyFormula(
                    n, my0, l0y, hy, split.EIyConcrete, split.EIyRebar, psiY, slendernessThreshold);
                myEff = eyResult.MEff;
            }

            return new Result(mxEff, myEff,
                new AxisDiagnostics(exResult.Eta, exResult.Ncr, exResult.D, l0x, hx, exResult.Slender, exResult.Stable,
                    exResult.Iterations, exResult.ExtrapolationFailed) { EtaHistory = exResult.EtaHistory },
                new AxisDiagnostics(eyResult.Eta, eyResult.Ncr, eyResult.D, l0y, hy, eyResult.Slender, eyResult.Stable,
                    eyResult.Iterations, eyResult.ExtrapolationFailed) { EtaHistory = eyResult.EtaHistory });
        }
    }
}
