using System;
using System.Collections.Generic;
using System.Linq;
using CScore;

namespace CScore.Fem;

/// <summary>
/// Результат расчёта ширины раскрытия трещин по п. 8.2.9–8.2.18 СП 63 для одной
/// полосы (один армслой × одно направление X|Y) при одном состоянии усилий.
/// </summary>
public sealed class ShellCrackStripResult
{
    /// <summary>Индекс слоя в PlateSection.RebarLayers — стабильный ключ (Name не гарантирован уникальным).</summary>
    public int LayerIndex { get; init; }
    /// <summary>PlateRebarLayer.Name — только для отображения.</summary>
    public string LayerName { get; init; } = "";
    /// <summary>"x" | "y".</summary>
    public string Direction { get; init; } = "";
    /// <summary>Zsx/Zsy для этого направления, м, от срединной плоскости.</summary>
    public double Z { get; init; }
    /// <summary>Знак Z (z &gt; 0 — «верх»; z &lt; 0 — «низ»), по конвенции PlateSection.</summary>
    public bool IsTop { get; init; }
    /// <summary>Расчётный момент полосы, кН·м/м.</summary>
    public double MDes { get; init; }
    /// <summary>Расчётная продольная сила полосы, кН/м.</summary>
    public double NDes { get; init; }
    /// <summary>Момент трещинообразования (п. 8.2.11), кН·м/м. Считается всегда, не зависит от eps_s.</summary>
    public double Mcrc { get; init; }
    /// <summary>|M_des| &gt; Mcrc.</summary>
    public bool Cracked { get; init; }
    /// <summary>Напряжение в арматуре, кПа (единицы MaterialChars.E/Ft). 0, если не растрескалась
    /// или деформация арматуры не растягивающая.</summary>
    public double SigmaS { get; init; }
    /// <summary>Напряжение σs,crc в арматуре сразу после образования трещины, кПа.</summary>
    public double SigmaSCrc { get; init; }
    /// <summary>Коэффициент неравномерности деформаций арматуры ψs.</summary>
    public double PsiS { get; init; }
    public double Phi1 { get; init; }
    public double Phi2 { get; init; }
    public double Phi3 { get; init; }
    /// <summary>Расстояние между трещинами (п. 8.2.17), м.</summary>
    public double LsM { get; init; }
    /// <summary>Ширина раскрытия трещины, мм.</summary>
    public double AcrcMm { get; init; }
    /// <summary>Угол трещины относительно оси X, (-90°, 90°]. По направлению главной
    /// растягивающей деформации (не главного напряжения) — принятое упрощение,
    /// точное только для линейно-упругого изотропного бетона.</summary>
    public double CrackAngleDeg { get; init; }
}

/// <summary>
/// Расчёт ширины раскрытия трещин по п. 8.2.9–8.2.18 СП 63 для слоистой модели
/// пластины (PlateSection), для ОДНОГО состояния усилий/деформаций. Комбинация
/// длительного/непродолжительного раскрытия (п. 8.2.7) сюда не входит — она
/// остаётся логикой вызывающего кода (FemCheckRunner.RunLayeredSlsCheck).
/// </summary>
public static class ShellLayeredCrackWidth
{
    /// <summary>Полная картина: одна запись на каждый (слой × направление X|Y), где
    /// соответствующая площадь армирования &gt; 0. Не схлопывает в максимум.</summary>
    /// <param name="solveAtCrackingState">Решатель НДС для состояния образования трещин.
    /// П. 8.2.18 СП 63 определяет σs,crc как напряжение в арматуре «сразу после образования
    /// нормальных трещин, определяемое по 8.2.16, принимая в соответствующих формулах значения
    /// M = M_crc». Для слоистой модели это означает повторное решение той же задачи 6×6 при
    /// моменте рассматриваемого направления, заменённом на M_crc: делегат принимает вектор
    /// (Nx, Ny, Nxy, Mx, My, Mxy) и возвращает найденное НДС либо null, если сходимости нет.
    /// Если делегат не передан, σs,crc считается формульно (см. ComputeStrip).</param>
    public static IReadOnlyList<ShellCrackStripResult> ComputeAll(
        PlateSection section, ShellLoadItem shell, ShellStrainState st,
        MaterialChars cCh, MaterialChars rCh, double phi1, double phi2,
        SigmaSCrcMethod sigmaSCrcMethod, WplGammaMethod wplGamma,
        Func<double[], ShellStrainState?>? solveAtCrackingState = null)
    {
        double RbSer = Math.Abs(cCh.Fc);
        double Rbt = cCh.Ft;
        double Es = rCh.E;
        double RsSer = Math.Abs(rCh.Ft);
        double ebRed = RbSer / 0.0015;
        double alphaFull = Es / cCh.E;
        double alpha = Es / ebRed;
        double h = section.H;

        // П. 8.2.18: σs,crc — та же задача при M = M_crc. Для слоистой модели это не формула,
        // а повторное решение 6×6 с заменённым моментом рассматриваемого направления; знак
        // момента сохраняется, потому что M_crc — беззнаковый порог.
        Func<double, double>? SigmaSCrcSolver(bool alongX, double z)
        {
            if (solveAtCrackingState == null) return null;
            return mcrc =>
            {
                double mSelf = alongX ? shell.Mx : shell.My;
                double mCrcSigned = mSelf < 0.0 ? -mcrc : mcrc;
                var stCrc = solveAtCrackingState(alongX
                    ? [shell.Nx, shell.Ny, shell.Nxy, mCrcSigned, shell.My, shell.Mxy]
                    : [shell.Nx, shell.Ny, shell.Nxy, shell.Mx, mCrcSigned, shell.Mxy]);
                if (stCrc == null) return double.NaN;       // нет сходимости — формульный запасной путь
                double eps = alongX ? stCrc.EpsX(z) : stCrc.EpsY(z);
                return eps > 0.0 ? Math.Min(Es * eps, RsSer) : 0.0;
            };
        }

        var results = new List<ShellCrackStripResult>();
        for (int layerIndex = 0; layerIndex < section.RebarLayers.Count; layerIndex++)
        {
            var layer = section.RebarLayers[layerIndex];

            if (layer.Asx > 1e-14)
            {
                double z = layer.Zsx;
                results.Add(ComputeStrip(layerIndex, layer.Name, "x", z,
                    st.EpsX(z), shell.Mx, shell.Nx,
                    h, h / 2.0 + Math.Abs(z), h / 2.0 - Math.Abs(z),
                    layer.Asx, layer.DiameterX > 1e-9 ? layer.DiameterX : 0.012,
                    Rbt, RbSer, Es, RsSer, ebRed, alphaFull, alpha,
                    phi1, phi2, sigmaSCrcMethod, wplGamma,
                    CrackAngleDeg(st.EpsX(z), st.EpsY(z), st.GammaXY(z)),
                    SigmaSCrcSolver(alongX: true, z)));
            }

            if (layer.Asy > 1e-14)
            {
                double z = layer.Zsy;
                results.Add(ComputeStrip(layerIndex, layer.Name, "y", z,
                    st.EpsY(z), shell.My, shell.Ny,
                    h, h / 2.0 + Math.Abs(z), h / 2.0 - Math.Abs(z),
                    layer.Asy, layer.DiameterY > 1e-9 ? layer.DiameterY : 0.012,
                    Rbt, RbSer, Es, RsSer, ebRed, alphaFull, alpha,
                    phi1, phi2, sigmaSCrcMethod, wplGamma,
                    CrackAngleDeg(st.EpsX(z), st.EpsY(z), st.GammaXY(z)),
                    SigmaSCrcSolver(alongX: false, z)));
            }
        }

        return results;
    }

    /// <summary>Худшая (по AcrcMm среди Cracked=true) полоса; null, если ни одна не растрескалась
    /// или RebarLayers пуст. Тонкая обёртка над ComputeAll — не отдельная неаллоцирующая
    /// реализация (2-4 записи, риск расхождения двух путей перевешивает выигрыш).</summary>
    public static ShellCrackStripResult? ComputeWorst(
        PlateSection section, ShellLoadItem shell, ShellStrainState st,
        MaterialChars cCh, MaterialChars rCh, double phi1, double phi2,
        SigmaSCrcMethod sigmaSCrcMethod, WplGammaMethod wplGamma,
        Func<double[], ShellStrainState?>? solveAtCrackingState = null)
        => ComputeAll(section, shell, st, cCh, rCh, phi1, phi2, sigmaSCrcMethod, wplGamma,
                solveAtCrackingState)
            .Where(r => r.Cracked).MaxBy(r => r.AcrcMm);

    /// <summary>Угол трещины: перпендикулярна направлению главной деформации в точке (ex, ey, gxy).</summary>
    static double CrackAngleDeg(double ex, double ey, double gxy)
    {
        PlateSection.PrincipalStrains2D(ex, ey, gxy, out _, out _, out double theta);
        double crackRad = theta + Math.PI / 2.0;
        double degrees = crackRad * 180.0 / Math.PI;
        while (degrees <= -90.0) degrees += 180.0;
        while (degrees > 90.0) degrees -= 180.0;
        return degrees;
    }

    /// <summary>
    /// Формулы п. 8.2.9–8.2.18 СП 63 для одной полосы. Mcrc/Cracked считаются ВСЕГДА
    /// (не зависят от eps_s); SigmaS/SigmaSCrc/PsiS/LsM/Phi3/AcrcMm остаются 0, когда
    /// eps_s &lt;= 0, sigma_s &lt; 1e-3 кПа, либо |M_des| &lt;= Mcrc — те же условия, при которых
    /// сегодняшний ComputeAcrcStrip возвращает 0.0.
    /// </summary>
    static ShellCrackStripResult ComputeStrip(
        int layerIndex, string layerName, string direction, double z,
        double epsS, double mDes, double nDes,
        double h, double h0, double aPrime,
        double asT, double ds,
        double rbt, double rbSer, double es, double rsSer,
        double ebRed, double alphaFull, double alpha,
        double phi1, double phi2,
        SigmaSCrcMethod sigmaSCrcMethod, WplGammaMethod wplGamma,
        double crackAngleDeg,
        Func<double, double>? sigmaSCrcFromSolve = null)
    {
        ShellSimplSolver.FullSectionProps(h, h0, aPrime, asT, 0.0, alphaFull,
            out double aRed, out double iRed);
        double sRed = h * h / 2.0 + alphaFull * asT * h0;
        double ycFull = sRed / aRed;
        double yt = h - ycFull;
        double wRed = iRed / yt;
        double wPl = ShellSimplSolver.ResolveWplGamma(wplGamma, asT, 1.0, h) * wRed;
        double ex = wRed / aRed;
        double mcrc = Math.Max(0.0, rbt * wPl - nDes * ex);
        // mcrc — не имеющий знака порог (Math.Max(0.0, ...)); mDes может быть отрицательным
        // (момент, растягивающий нижнюю грань). Сравнение без Abs пропускало трещины при
        // любом отрицательном mDes независимо от его модуля.
        bool cracked = Math.Abs(mDes) > mcrc;

        double sigmaS = 0.0;
        double sigmaSCrc = 0.0;
        double psiS = 0.0;
        double lsM = 0.0;
        double phi3 = 0.0;
        double acrcMm = 0.0;

        if (epsS > 0.0 && cracked)
        {
            double sigma = Math.Min(es * epsS, rsSer);
            if (sigma >= 1e-3)
            {
                sigmaS = sigma;
                double hBt = Math.Min(Math.Max(yt, 2.0 * aPrime), h0 / 2.0);
                double lsRaw = 0.5 * hBt / asT * ds;
                double lsMin = Math.Max(10.0 * ds, 0.10);
                double lsMax = Math.Min(40.0 * ds, 0.40);
                lsM = Math.Clamp(lsRaw, lsMin, lsMax);

                if (sigmaSCrcMethod == SigmaSCrcMethod.CrackingMoment8138)
                {
                    // Ф. (8.138): ψs = 1 − 0,8·Mcrc/M, допускается для изгибаемых элементов.
                    double ratio = Math.Abs(mDes) > 1e-9
                        ? Math.Clamp(mcrc / Math.Abs(mDes), 0.0, 1.0) : 0.0;
                    sigmaSCrc = sigmaS * ratio;
                }
                else
                {
                    // Общее определение п. 8.2.18 — напряжение из решения НДС при M = Mcrc.
                    double fromSolve = sigmaSCrcFromSolve?.Invoke(mcrc) ?? double.NaN;
                    sigmaSCrc = double.IsNaN(fromSolve)
                        ? ShellSimplSolver.SigmaSCrcFromReleasedConcrete(
                            rbt, hBt, asT, alphaFull, sigmaS)     // запасной путь без решателя
                        : Math.Min(fromSolve, sigmaS);
                }

                psiS = sigmaS > 1e-3
                    ? Math.Clamp(1.0 - 0.8 * sigmaSCrc / sigmaS, 0.1, 1.0)
                    : 1.0;
                phi3 = nDes > 1e-3 ? 1.2 : 1.0;
                acrcMm = phi1 * phi2 * phi3 * psiS * (sigmaS / es) * lsM * 1000.0;
            }
        }

        return new ShellCrackStripResult
        {
            LayerIndex = layerIndex,
            LayerName = layerName,
            Direction = direction,
            Z = z,
            IsTop = z > 0.0,
            MDes = mDes,
            NDes = nDes,
            Mcrc = mcrc,
            Cracked = cracked,
            SigmaS = sigmaS,
            SigmaSCrc = sigmaSCrc,
            PsiS = psiS,
            Phi1 = phi1,
            Phi2 = phi2,
            Phi3 = phi3,
            LsM = lsM,
            AcrcMm = acrcMm,
            CrackAngleDeg = crackAngleDeg,
        };
    }

    /// <summary>
    /// Совместимость с существующими вызовами: та же сигнатура, что и удалённый
    /// FemCheckRunner.ComputeAcrcStrip, тот же результат (AcrcMm). Слой/направление/угол
    /// не имеют смысла на этом уровне — заполняются нейтральными значениями.
    /// </summary>
    internal static double ComputeAcrcStrip(
        double eps_s,
        double M_des, double N_des,
        double h, double h0, double aPrime,
        double As_t, double ds,
        double Rbt, double Rb_ser, double Es, double Rs_ser,
        double Eb_red, double alphaFull, double alpha,
        double phi1, double phi2,
        SigmaSCrcMethod sigmaSCrcMethod = SigmaSCrcMethod.ReleasedConcrete8137,
        WplGammaMethod wplGamma = WplGammaMethod.Sp63)
        => ComputeStrip(0, "", "x", 0.0, eps_s, M_des, N_des, h, h0, aPrime, As_t, ds,
            Rbt, Rb_ser, Es, Rs_ser, Eb_red, alphaFull, alpha,
            phi1, phi2, sigmaSCrcMethod, wplGamma, 0.0).AcrcMm;
}
