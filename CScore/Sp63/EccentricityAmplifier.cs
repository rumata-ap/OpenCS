using System;

namespace CScore.Sp63
{
    /// <summary>
    /// Коэффициент η (п. 8.1.15 СП63.13330) — учёт влияния прогиба на несущую
    /// способность внецентренно сжатых стержневых элементов при расчёте по
    /// недеформированной схеме. Два режима: буквальный (формула нормы,
    /// <see cref="AmplifyFormula"/>) и уточнённый — итерационный по фактической
    /// жёсткости из решателя НДС (<see cref="AmplifyIterative"/>), опирающийся
    /// на разрешение последнего абзаца п. 8.1.15 уточнять η с учётом влияния
    /// прогибов на момент в расчётном сечении.
    /// </summary>
    public static class EccentricityAmplifier
    {
        /// <summary>Коэффициент ks в формуле D = kb·Eb·I + ks·Es·Is (п. 8.1.15).</summary>
        public const double Ks = 0.7;

        /// <summary>Порог гибкости l0/h, выше которого поправка требуется (п. 8.1.2).</summary>
        public const double SlendernessThreshold = 14.0;

        /// <summary>Результат вычисления η для одной оси изгиба.</summary>
        /// <param name="Eta">Коэффициент η (1.0, если поправка не требуется).</param>
        /// <param name="Ncr">Условная критическая сила, кН.</param>
        /// <param name="D">Использованная жёсткость, кН·м².</param>
        /// <param name="Slender">Гибкость l0/h превышает порог 14 (п. 8.1.2).</param>
        /// <param name="Stable">false — потеря устойчивости (|N| ≥ Ncr).</param>
        /// <param name="MEff">Момент после усиления: M0·η.</param>
        /// <param name="Iterations">Число проходов решателя (0 для режима A).</param>
        /// <param name="ExtrapolationFailed">
        /// true, если экстраполяция Эйткена не применена (расходящаяся/
        /// осциллирующая последовательность) — использовано последнее η.
        /// </param>
        public readonly record struct EtaResult(
            double Eta, double Ncr, double D,
            bool Slender, bool Stable, double MEff,
            int Iterations, bool ExtrapolationFailed);

        /// <summary>Условная критическая сила: Ncr = π²·D/l0² (формула 8.1.15).</summary>
        public static double Ncr(double d, double l0) => Math.PI * Math.PI * d / (l0 * l0);

        /// <summary>
        /// φl = 1 + M1l/M1, клэмп [1; 2] (п. 8.1.15). При M1≈0 возвращает
        /// консервативное значение 2 (защита от деления на ноль).
        /// </summary>
        public static double PhiL(double m1, double m1l)
        {
            if (Math.Abs(m1) < 1e-9) return 2.0;
            return Math.Clamp(1.0 + m1l / m1, 1.0, 2.0);
        }

        /// <summary>δe = clamp(e0/h, 0.15, 1.5) (п. 8.1.15).</summary>
        public static double DeltaE(double e0, double h) => Math.Clamp(Math.Abs(e0) / h, 0.15, 1.5);

        /// <summary>kb = 0.15/(φl·(0.3+δe)) (п. 8.1.15).</summary>
        public static double Kb(double phiL, double deltaE) => 0.15 / (phiL * (0.3 + deltaE));

        /// <summary>
        /// Экстраполяция Эйткена (Δ²-процесс) по трём последовательным членам
        /// сходящейся последовательности. Возвращает <see cref="double.NaN"/>,
        /// если знаменатель вырожден (постоянное приращение — не геометрическая
        /// сходимость).
        /// </summary>
        public static double Aitken(double x0, double x1, double x2)
        {
            double d1 = x1 - x0;
            double d2 = x2 - x1;
            double denom = d2 - d1;
            if (Math.Abs(denom) < 1e-12) return double.NaN;
            return x2 - d2 * d2 / denom;
        }

        /// <summary>
        /// true — поправку η можно не считать: либо N не сжимающая (конвенция
        /// проекта: сжатие — отрицательное N), либо гибкость l0/h ≤ 14 (п. 8.1.2).
        /// </summary>
        public static bool ShouldSkip(double n, double l0, double h, out bool slender)
        {
            slender = l0 / h > SlendernessThreshold;
            return n >= -1e-9 || !slender;
        }

        static EtaResult FromD(double n, double m, double l0, double d, bool slender, int iterations)
        {
            double ncr = Ncr(d, l0);
            if (Math.Abs(n) >= ncr)
                return new EtaResult(double.PositiveInfinity, ncr, d, slender, false, m, iterations, false);

            double eta = 1.0 / (1.0 - Math.Abs(n) / ncr);
            return new EtaResult(eta, ncr, d, slender, true, m * eta, iterations, false);
        }

        /// <summary>
        /// Режим A (буквальный): D по формуле D = kb·Eb·I + ks·Es·Is, kb — из
        /// φl (длительность нагрузки) и δe (относительный эксцентриситет).
        /// </summary>
        public static EtaResult AmplifyFormula(
            double n, double m0, double l0, double h,
            double eiConcrete, double eiRebar, double m1, double m1l)
        {
            if (l0 <= 0 || h <= 0)
                throw new ArgumentException("l0 и h должны быть положительны");

            if (ShouldSkip(n, l0, h, out var slender) || Math.Abs(m0) < 1e-9)
                return new EtaResult(1.0, double.PositiveInfinity, double.NaN, slender, true, m0, 0, false);

            double phiL = PhiL(m1, m1l);
            double e0 = Math.Abs(m0) / Math.Abs(n);
            double deltaE = DeltaE(e0, h);
            double kb = Kb(phiL, deltaE);
            double d = kb * eiConcrete + Ks * eiRebar;

            return FromD(n, m0, l0, d, slender, 0);
        }

        /// <summary>
        /// Режим B (уточнённый): D берётся из фактической жёсткости, получаемой
        /// через <paramref name="solveCurvature"/> (обычно — обёртка над
        /// решателем НДС: даёт кривизну κ при пробном моменте на этой оси).
        /// Выполняет <paramref name="passes"/> проходов, строит последовательность
        /// η, применяет экстраполяцию Эйткена и делает финальный проход при
        /// уточнённом моменте.
        /// </summary>
        public static EtaResult AmplifyIterative(
            double n, double m0, double l0, double h,
            Func<double, double> solveCurvature, int passes = 3)
        {
            if (l0 <= 0 || h <= 0)
                throw new ArgumentException("l0 и h должны быть положительны");

            if (ShouldSkip(n, l0, h, out var slender) || Math.Abs(m0) < 1e-9)
                return new EtaResult(1.0, double.PositiveInfinity, double.NaN, slender, true, m0, 0, false);

            var etas = new double[passes];
            double mCurrent = m0;
            for (int i = 0; i < passes; i++)
            {
                double kappa = solveCurvature(mCurrent);
                if (Math.Abs(kappa) < 1e-12)
                    return new EtaResult(1.0, double.PositiveInfinity, double.NaN, slender, true, m0, i, false);

                double d = mCurrent / kappa;
                double ncr = Ncr(d, l0);
                if (Math.Abs(n) >= ncr)
                    return new EtaResult(double.PositiveInfinity, ncr, d, slender, false, m0, i + 1, false);

                etas[i] = 1.0 / (1.0 - Math.Abs(n) / ncr);
                mCurrent = m0 * etas[i];
            }

            double etaFinal = etas[passes - 1];
            bool extrapolationFailed = true;
            if (passes >= 3)
            {
                double d1 = etas[passes - 2] - etas[passes - 3];
                double d2 = etas[passes - 1] - etas[passes - 2];
                double extrapolated = Aitken(etas[passes - 3], etas[passes - 2], etas[passes - 1]);
                if (!double.IsNaN(extrapolated) && extrapolated > 0 && Math.Abs(d2) < Math.Abs(d1))
                {
                    etaFinal = extrapolated;
                    extrapolationFailed = false;
                }
            }

            double mFinal = m0 * etaFinal;
            double kappaFinal = solveCurvature(mFinal);
            if (Math.Abs(kappaFinal) < 1e-12)
                return new EtaResult(etaFinal, double.PositiveInfinity, double.NaN, slender, true, mFinal, passes + 1, extrapolationFailed);

            double dFinal = mFinal / kappaFinal;
            double ncrFinal = Ncr(dFinal, l0);
            bool stable = Math.Abs(n) < ncrFinal;
            return new EtaResult(etaFinal, ncrFinal, dFinal, slender, stable, mFinal, passes + 1, extrapolationFailed);
        }
    }
}
