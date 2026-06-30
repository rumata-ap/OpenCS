using System;
using System.Collections.Generic;

namespace CScore;

/// <summary>
/// Проверка на устойчивость по СП 16.13330.2017, раздел 9.
/// </summary>
public static class SteelStabilityCheck
{
    /// <summary>
    /// Условная гибкость: λ̄ = λ / λy = (l0/i) / (π·√(E/fy)).
    /// </summary>
    public static double ConventionalSlenderness(
        double designLength, double radiusOfGyration, double E, double fy)
    {
        double lambdaY = Math.PI * Math.Sqrt(E / fy);
        double lambda = designLength / radiusOfGyration;
        return lambda / lambdaY;
    }

    /// <summary>
    /// Коэффициент продольного изгиба φ (приложение Д, таблица 8).
    /// Φ = 0.5·(1 + α·(λ̄ - 0.2) + λ̄²)
    /// φ = min(1, 1 / (Φ + √(Φ² - λ̄²)))
    /// </summary>
    public static double Chi(
        double lambdaBar,
        BucklingCurve curve = BucklingCurve.b)
    {
        if (lambdaBar <= 0) return 1.0;

        double alpha = curve switch
        {
            BucklingCurve.a0 => 0.13,
            BucklingCurve.a  => 0.21,
            BucklingCurve.b  => 0.34,
            BucklingCurve.c  => 0.49,
            BucklingCurve.d  => 0.76,
            _ => 0.34
        };

        double phi = 0.5 * (1 + alpha * (lambdaBar - 0.2) + lambdaBar * lambdaBar);
        double chi = 1.0 / (phi + Math.Sqrt(phi * phi - lambdaBar * lambdaBar));
        return Math.Min(1.0, chi);
    }

    /// <summary>
    /// Коэффициент продольного изгиба φ по ElementClass (обратная совместимость).
    /// A→кривая a, B→кривая b, C→кривая c.
    /// </summary>
    public static double Chi(
        double lambdaBar,
        SteelClassifier.ElementClass elementClass)
    {
        var curve = elementClass switch
        {
            SteelClassifier.ElementClass.A => BucklingCurve.a,
            SteelClassifier.ElementClass.B => BucklingCurve.b,
            SteelClassifier.ElementClass.C => BucklingCurve.c,
            _ => BucklingCurve.b
        };
        return Chi(lambdaBar, curve);
    }

    /// <summary>
    /// Коэффициент бокового выпучивания φb (приложение Ж).
    /// Φ = 0.5·(1 + αb·(λ̄b - 0.2) + λ̄b²)
    /// φb = min(1, 1 / (Φ + √(Φ² - λ̄b²)))
    /// </summary>
    public static double ChiB(
        double lambdaBarB,
        BucklingCurve curve = BucklingCurve.b)
    {
        if (lambdaBarB <= 0) return 1.0;

        double alphaB = curve switch
        {
            BucklingCurve.a0 => 0.13,
            BucklingCurve.a  => 0.21,
            BucklingCurve.b  => 0.34,
            BucklingCurve.c  => 0.49,
            BucklingCurve.d  => 0.76,
            _ => 0.34
        };

        double phi = 0.5 * (1 + alphaB * (lambdaBarB - 0.2) + lambdaBarB * lambdaBarB);
        double chiB = 1.0 / (phi + Math.Sqrt(phi * phi - lambdaBarB * lambdaBarB));
        return Math.Min(1.0, chiB);
    }

    /// <summary>
    /// Коэффициент бокового выпучивания φb по ElementClass (обратная совместимость).
    /// </summary>
    public static double ChiB(
        double lambdaBarB,
        SteelClassifier.ElementClass elementClass)
    {
        var curve = elementClass switch
        {
            SteelClassifier.ElementClass.A => BucklingCurve.a,
            SteelClassifier.ElementClass.B => BucklingCurve.b,
            SteelClassifier.ElementClass.C => BucklingCurve.c,
            _ => BucklingCurve.b
        };
        return ChiB(lambdaBarB, curve);
    }

    /// <summary>
    /// Критическая сила Эйлера: Ncr = π²·EI / l0².
    /// </summary>
    public static double EulerForce(double EI, double designLength)
    {
        return Math.PI * Math.PI * EI / (designLength * designLength);
    }

    // ─── Таблица 22, СП 16.13330.2017 ────────────────────────────────────
    // Коэффициенты продольного изгиба η для внецентренного сжатия.
    // Столбцы: [λ̄, η_сплошное, η_полое, η_составное]
    static readonly double[,] Table22 =
    {
        { 0.0,  1.00, 1.00, 1.00 },
        { 0.2,  1.00, 1.00, 1.00 },
        { 0.4,  1.00, 1.00, 1.00 },
        { 0.6,  1.00, 1.00, 1.02 },
        { 0.8,  1.00, 1.00, 1.05 },
        { 1.0,  1.00, 1.00, 1.08 },
        { 1.2,  1.00, 1.00, 1.12 },
        { 1.4,  1.02, 1.01, 1.16 },
        { 1.6,  1.04, 1.02, 1.21 },
        { 1.8,  1.08, 1.03, 1.26 },
        { 2.0,  1.12, 1.05, 1.32 },
        { 2.2,  1.16, 1.07, 1.39 },
        { 2.4,  1.21, 1.09, 1.46 },
        { 2.6,  1.27, 1.11, 1.54 },
        { 2.8,  1.33, 1.14, 1.63 },
        { 3.0,  1.40, 1.17, 1.73 },
        { 3.2,  1.47, 1.20, 1.84 },
        { 3.4,  1.55, 1.24, 1.96 },
        { 3.6,  1.64, 1.28, 2.09 },
        { 3.8,  1.74, 1.32, 2.24 },
        { 4.0,  1.84, 1.37, 2.40 },
        { 4.2,  1.95, 1.42, 2.58 },
        { 4.4,  2.07, 1.48, 2.78 },
        { 4.6,  2.20, 1.54, 3.00 },
        { 4.8,  2.34, 1.60, 3.24 },
        { 5.0,  2.49, 1.67, 3.50 },
    };

    /// <summary>
    /// Тип сечения для таблицы 22.
    /// </summary>
    public enum SectionTypeForEta { Solid = 0, Hollow = 1, BuiltUp = 2 }

    /// <summary>
    /// Коэффициент продольного изгиба η (таблица 22).
    /// Линейная интерполяция по λ̄.
    /// </summary>
    public static double Eta(double lambdaBar, SectionTypeForEta sectionType = SectionTypeForEta.Solid)
    {
        if (lambdaBar <= 0) return Table22[0, 1 + (int)sectionType];
        if (lambdaBar >= 5.0) return Table22[Table22.GetLength(0) - 1, 1 + (int)sectionType];

        int col = 1 + (int)sectionType;
        int rows = Table22.GetLength(0);
        for (int i = 0; i < rows - 1; i++)
        {
            double lo = Table22[i, 0];
            double hi = Table22[i + 1, 0];
            if (lambdaBar >= lo && lambdaBar <= hi)
            {
                double t = (lambdaBar - lo) / (hi - lo);
                return Table22[i, col] + t * (Table22[i + 1, col] - Table22[i, col]);
            }
        }
        return Table22[rows - 1, col];
    }

    /// <summary>
    /// Устойчивость при центральном сжатии (9.1.1).
    /// N/(φ·A) ≤ Ry/γM
    /// </summary>
    public static CheckDetail CheckBuckling(
        double N, double chi, double Aeff, double Ry, double gammaM,
        double lambdaBar = 0, BucklingCurve curve = BucklingCurve.b,
        string axis = "")
    {
        double allowable = chi * Aeff * Ry / gammaM;
        string desc = string.IsNullOrEmpty(axis)
            ? "Устойчивость при центральном сжатии"
            : $"Устойчивость при центральном сжатии ({axis})";
        return new CheckDetail
        {
            Formula = "9.1.1",
            Description = desc,
            NormReference = "СП 16.13330.2017, п. 9.1.1",
            Applied = Math.Abs(N),
            Allowable = allowable,
            Variables = new Dictionary<string, double>
            {
                ["N"] = N, ["φ"] = chi, ["Aeff"] = Aeff,
                ["Ry"] = Ry, ["γM"] = gammaM,
                ["λ̄"] = lambdaBar
            }
        };
    }

    /// <summary>
    /// Устойчивость при сжатии с изгибом (9.2.2).
    /// N/(φ·A) + η·βm·M/(φb·W·(1 − N/Ncr)) ≤ Ry/γM
    /// </summary>
    public static CheckDetail CheckBucklingBending(
        double N, double M, double chi, double chiB,
        double Aeff, double Weff, double Ncr,
        double lambdaBar, double betaM, double Ry, double gammaM,
        string axis = "",
        SectionTypeForEta sectionType = SectionTypeForEta.Solid)
    {
        double eta = Eta(lambdaBar, sectionType);
        double nPart = Math.Abs(N) / (chi * Aeff);
        double denom = 1.0 - Math.Abs(N) / Ncr;
        if (denom < 0.1) denom = 0.1;
        double mPart = eta * betaM * Math.Abs(M) / (chiB * Weff * denom);
        double applied = nPart + mPart;
        double allowable = Ry / gammaM;
        string desc = string.IsNullOrEmpty(axis)
            ? "Устойчивость при сжатии с изгибом"
            : $"Устойчивость при сжатии с изгибом ({axis})";
        return new CheckDetail
        {
            Formula = "9.2.2",
            Description = desc,
            NormReference = "СП 16.13330.2017, п. 9.2.2",
            Applied = applied,
            Allowable = allowable,
            Variables = new Dictionary<string, double>
            {
                ["N"] = N, ["M"] = M,
                ["φ"] = chi, ["φb"] = chiB,
                ["Aeff"] = Aeff, ["Weff"] = Weff,
                ["Ncr"] = Ncr, ["η"] = eta, ["βm"] = betaM,
                ["Ry"] = Ry, ["γM"] = gammaM,
                ["λ̄"] = lambdaBar,
                ["N/(φ·A)"] = nPart,
                ["η·βm·M/(φb·W·(1-N/Ncr))"] = mPart
            }
        };
    }

    /// <summary>
    /// Устойчивость при продольном изгибе (9.1.2).
    /// N/(φ·A) ≤ Ry/γM
    /// </summary>
    public static CheckDetail CheckBucklingAxial(
        double N, double chi, double Aeff, double Ry, double gammaM,
        double lambdaBar = 0, BucklingCurve curve = BucklingCurve.b)
    {
        double allowable = chi * Aeff * Ry / gammaM;
        return new CheckDetail
        {
            Formula = "9.1.2",
            Description = "Устойчивость при продольном изгибе",
            NormReference = "СП 16.13330.2017, п. 9.1.2",
            Applied = Math.Abs(N),
            Allowable = allowable,
            Variables = new Dictionary<string, double>
            {
                ["N"] = N, ["φ"] = chi, ["Aeff"] = Aeff,
                ["Ry"] = Ry, ["γM"] = gammaM,
                ["λ̄"] = lambdaBar
            }
        };
    }

    /// <summary>
    /// Устойчивость плоских элементов (9.2.1).
    /// σ ≤ ρ·Ry/γM
    /// </summary>
    public static CheckDetail CheckPlateBuckling(
        double sigma, double rho, double Ry, double gammaM)
    {
        double allowable = rho * Ry / gammaM;
        return new CheckDetail
        {
            Formula = "9.2.1",
            Description = "Устойчивость плоских элементов",
            NormReference = "СП 16.13330.2017, п. 9.2.1",
            Applied = sigma,
            Allowable = allowable,
            Variables = new Dictionary<string, double>
            {
                ["σ"] = sigma, ["ρ"] = rho,
                ["Ry"] = Ry, ["γM"] = gammaM
            }
        };
    }

    /// <summary>
    /// Устойчивость при кручении (9.3.1).
    /// Mz/(φt·Wt) ≤ Ry/γM
    /// </summary>
    public static CheckDetail CheckTorsionBuckling(
        double Mz, double chiT, double Wt, double Ry, double gammaM)
    {
        double allowable = chiT * Wt * Ry / gammaM;
        return new CheckDetail
        {
            Formula = "9.3.1",
            Description = "Устойчивость при кручении",
            NormReference = "СП 16.13330.2017, п. 9.3.1",
            Applied = Math.Abs(Mz),
            Allowable = allowable,
            Variables = new Dictionary<string, double>
            {
                ["Mz"] = Mz, ["φt"] = chiT, ["Wt"] = Wt,
                ["Ry"] = Ry, ["γM"] = gammaM
            }
        };
    }

    /// <summary>
    /// Устойчивость при изгибе с кручением (9.4.1).
    /// N/(φ·A) + M/(φb·W) + Mz/(φt·Wt) ≤ Ry/γM
    /// </summary>
    public static CheckDetail CheckBucklingBendingTorsion(
        double N, double M, double Mz,
        double chi, double chiB, double chiT,
        double Aeff, double Weff, double Wt,
        double Ry, double gammaM)
    {
        double nPart = Math.Abs(N) / (chi * Aeff);
        double mPart = Math.Abs(M) / (chiB * Weff);
        double tPart = Math.Abs(Mz) / (chiT * Wt);
        double applied = nPart + mPart + tPart;
        double allowable = Ry / gammaM;
        return new CheckDetail
        {
            Formula = "9.4.1",
            Description = "Устойчивость: изгиб + кручение",
            NormReference = "СП 16.13330.2017, п. 9.4.1",
            Applied = applied,
            Allowable = allowable,
            Variables = new Dictionary<string, double>
            {
                ["N"] = N, ["M"] = M, ["Mz"] = Mz,
                ["φ"] = chi, ["φb"] = chiB, ["φt"] = chiT,
                ["Aeff"] = Aeff, ["Weff"] = Weff, ["Wt"] = Wt,
                ["Ry"] = Ry, ["γM"] = gammaM,
                ["N/(φ·A)"] = nPart,
                ["M/(φb·Weff)"] = mPart,
                ["Mz/(φt·Wt)"] = tPart
            }
        };
    }

    /// <summary>
    /// Устойчивость при местном выпучивании (9.5).
    /// Для сечений класса C: σ ≤ ρ·Ry/γM
    /// </summary>
    public static CheckDetail CheckLocalBuckling(
        double sigma, double lambdaBarLocal, double Ry, double gammaM)
    {
        double rho = 1.0;
        if (lambdaBarLocal > 0.2)
        {
            double phi = 0.5 * (1 + 0.34 * (lambdaBarLocal - 0.2) + lambdaBarLocal * lambdaBarLocal);
            rho = Math.Min(1.0, 1.0 / (phi + Math.Sqrt(phi * phi - lambdaBarLocal * lambdaBarLocal)));
        }
        double allowable = rho * Ry / gammaM;
        return new CheckDetail
        {
            Formula = "9.5",
            Description = "Местное выпучивание (класс C)",
            NormReference = "СП 16.13330.2017, п. 9.5",
            Applied = sigma,
            Allowable = allowable,
            Variables = new Dictionary<string, double>
            {
                ["σ"] = sigma, ["λ̄local"] = lambdaBarLocal,
                ["ρ"] = rho, ["Ry"] = Ry, ["γM"] = gammaM
            }
        };
    }

    /// <summary>
    /// Устойчивость при продольном изгибе с учётом местного выпучивания (9.6).
    /// Для сечений класса C: N/(φ·Aeff) + M/(φb·Weff) ≤ Ry/γM
    /// </summary>
    public static CheckDetail CheckBucklingReduced(
        double N, double M, double chi, double chiB,
        double Aeff, double Weff, double Ry, double gammaM, string axis = "")
    {
        double nPart = Math.Abs(N) / (chi * Aeff);
        double mPart = Math.Abs(M) / (chiB * Weff);
        double applied = nPart + mPart;
        double allowable = Ry / gammaM;
        string desc = string.IsNullOrEmpty(axis)
            ? "Продольный изгиб (эффективные площади)"
            : $"Продольный изгиб (эффективные площади, {axis})";
        return new CheckDetail
        {
            Formula = "9.6",
            Description = desc,
            NormReference = "СП 16.13330.2017, п. 9.6",
            Applied = applied,
            Allowable = allowable,
            Variables = new Dictionary<string, double>
            {
                ["N"] = N, ["M"] = M,
                ["φ"] = chi, ["φb"] = chiB,
                ["Aeff"] = Aeff, ["Weff"] = Weff,
                ["Ry"] = Ry, ["γM"] = gammaM,
                ["N/(φ·Aeff)"] = nPart,
                ["M/(φb·Weff)"] = mPart
            }
        };
    }

    /// <summary>
    /// Оценка условной гибкости местного выпучивания для класса C.
    /// λ̄_local = (d/tw) / (33·ε̄)
    /// </summary>
    public static double EstimateLocalSlenderness(SteelSection section)
    {
        var pts = section.OuterContour;
        if (pts.Count < 4) return 0.8;

        double yMin = double.MaxValue, yMax = double.MinValue;
        foreach (var (x, y) in pts)
        {
            if (y < yMin) yMin = y;
            if (y > yMax) yMax = y;
        }
        double h = yMax - yMin;

        double midY = (yMin + yMax) / 2;
        double bandH = h * 0.25;
        double xMinMid = double.MaxValue, xMaxMid = double.MinValue;
        foreach (var (x, y) in pts)
        {
            if (Math.Abs(y - midY) < bandH)
            {
                if (x < xMinMid) xMinMid = x;
                if (x > xMaxMid) xMaxMid = x;
            }
        }
        double tw = xMaxMid > xMinMid + 1e-10 ? xMaxMid - xMinMid : h * 0.05;

        double d = h * 0.7;
        double ratio = d / tw;

        var fyMpa = (section.Steel.C?.Ry ?? 235e6) / 1e6;
        var epsBar = SteelClassifier.EpsilonBar(fyMpa);
        return ratio / (33 * epsBar);
    }

    /// <summary>
    /// Полная проверка на устойчивость.
    /// </summary>
    public static List<CheckDetail> CheckAll(
        SteelSection section, InternalForces forces, DesignContext context)
    {
        var details = new List<CheckDetail>();
        var fy = section.Steel.C?.Ry ?? 235e6;
        var E = section.Steel.E;
        var gammaM = context.GammaM;

        // Условные гибкости
        double lambdaBarX = ConventionalSlenderness(
            context.DesignLengthX * context.MuX, section.ix, E, fy);
        double lambdaBarY = ConventionalSlenderness(
            context.DesignLengthY * context.MuY, section.iy, E, fy);

        // Коэффициенты продольного изгиба (по кривым из контекста)
        double chiX = Chi(lambdaBarX, context.BucklingCurveX);
        double chiY = Chi(lambdaBarY, context.BucklingCurveY);

        // Коэффициенты бокового выпучивания (приложение Ж)
        double chiBX, chiBY;
        if (context.DesignLengthBit > 1e-10)
        {
            double lambdaBarBX = ConventionalSlenderness(
                context.DesignLengthBit, section.iy, E, fy);
            chiBX = ChiB(lambdaBarBX, context.BucklingCurveX);
            chiBY = chiBX;
        }
        else
        {
            chiBX = chiX;
            chiBY = chiY;
        }

        // Критические силы
        double NcrX = EulerForce(E * section.Ix, context.DesignLengthX * context.MuX);
        double NcrY = EulerForce(E * section.Iy, context.DesignLengthY * context.MuY);

        // 9.1.1 — Устойчивость при центральном сжатии
        if (Math.Abs(forces.N) > 1e-10)
            details.Add(CheckBuckling(
                forces.N, chiX, section.Area, fy, gammaM,
                lambdaBarX, context.BucklingCurveX));

        // 9.2.2 — Сжатие с изгибом
        if (Math.Abs(forces.N) > 1e-10 && Math.Abs(forces.Mx) > 1e-10)
            details.Add(CheckBucklingBending(
                forces.N, forces.Mx, chiX, chiBX,
                section.Area, section.Wx, NcrX,
                lambdaBarX, context.BetaM, fy, gammaM, "X",
                context.SectionType));

        if (Math.Abs(forces.N) > 1e-10 && Math.Abs(forces.My) > 1e-10)
            details.Add(CheckBucklingBending(
                forces.N, forces.My, chiY, chiBY,
                section.Area, section.Wy, NcrY,
                lambdaBarY, context.BetaM, fy, gammaM, "Y",
                context.SectionType));

        // Класс C — проверки 9.5, 9.6
        var sectionClass = section.Classification.SectionClass;
        if (sectionClass == SteelClassifier.ElementClass.C)
        {
            double effA = section.Classification.EffectiveArea;
            if (effA < 1e-15) effA = section.Area;

            // 9.5 — местное выпучивание
            double lambdaBarLocal = EstimateLocalSlenderness(section);
            if (Math.Abs(forces.N) > 1e-10)
            {
                double sigma = Math.Abs(forces.N) / effA;
                details.Add(CheckLocalBuckling(sigma, lambdaBarLocal, fy, gammaM));
            }

            // 9.6 — продольный изгиб с эффективными площадями
            if (Math.Abs(forces.N) > 1e-10 && Math.Abs(forces.Mx) > 1e-10)
                details.Add(CheckBucklingReduced(
                    forces.N, forces.Mx, chiX, chiBX,
                    effA, section.Wx * effA / section.Area, fy, gammaM, "X"));

            if (Math.Abs(forces.N) > 1e-10 && Math.Abs(forces.My) > 1e-10)
                details.Add(CheckBucklingReduced(
                    forces.N, forces.My, chiY, chiBY,
                    effA, section.Wy * effA / section.Area, fy, gammaM, "Y"));
        }

        return details;
    }
}
