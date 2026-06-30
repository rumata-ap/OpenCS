using System;
using System.Collections.Generic;

namespace CScore;

/// <summary>
/// Проверка на прочность по СП 16.13330.2017, раздел 8.
/// </summary>
public static class SteelStrengthCheck
{
    /// <summary>
    /// Проверка на центральное сжатие (8.1.1).
    /// N/(φ·Aeff) ≤ Ry/γM
    /// </summary>
    public static CheckDetail CheckAxialCompression(
        double N, double chi, double Aeff, double Ry, double gammaM)
    {
        double allowable = chi * Aeff * Ry / gammaM;
        return new CheckDetail
        {
            Formula = "8.1.1",
            Description = "Центральное сжатие",
            NormReference = "СП 16.13330.2017, п. 8.1.1",
            Applied = Math.Abs(N),
            Allowable = allowable,
            Variables = new Dictionary<string, double>
            {
                ["N"] = N, ["φ"] = chi, ["Aeff"] = Aeff,
                ["Ry"] = Ry, ["γM"] = gammaM
            }
        };
    }

    /// <summary>
    /// Проверка на изгиб (8.1.2).
    /// M/(φb·Weff) ≤ Ry/γM
    /// </summary>
    public static CheckDetail CheckBending(
        double M, double chiB, double Weff, double Ry, double gammaM, string axis = "")
    {
        double allowable = chiB * Weff * Ry / gammaM;
        string desc = string.IsNullOrEmpty(axis)
            ? "Изгиб"
            : $"Изгиб ({axis})";
        return new CheckDetail
        {
            Formula = "8.1.2",
            Description = desc,
            NormReference = "СП 16.13330.2017, п. 8.1.2",
            Applied = Math.Abs(M),
            Allowable = allowable,
            Variables = new Dictionary<string, double>
            {
                ["M"] = M, ["φb"] = chiB, ["Weff"] = Weff,
                ["Ry"] = Ry, ["γM"] = gammaM
            }
        };
    }

    /// <summary>
    /// Сжатие с изгибом в одной плоскости (8.1.3).
    /// N/(φ·Aeff) + M/(φb·Weff) ≤ Ry/γM
    /// </summary>
    public static CheckDetail CheckCompressionBendingSinglePlane(
        double N, double M,
        double chi, double chiB,
        double Aeff, double Weff,
        double Ry, double gammaM,
        string axis = "")
    {
        double nPart = Math.Abs(N) / (chi * Aeff);
        double mPart = Math.Abs(M) / (chiB * Weff);
        double applied = nPart + mPart;
        double allowable = Ry / gammaM;
        string desc = string.IsNullOrEmpty(axis)
            ? "Сжатие с изгибом (одна плоскость)"
            : $"Сжатие с изгибом ({axis})";
        return new CheckDetail
        {
            Formula = "8.1.3",
            Description = desc,
            NormReference = "СП 16.13330.2017, п. 8.1.3",
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
    /// Сжатие с изгибом в двух плоскостях (8.1.4).
    /// N/(φ·Aeff) + Mx/(φbX·Wx) + My/(φbY·Wy) ≤ Ry/γM
    /// </summary>
    public static CheckDetail CheckCompressionBendingDoublePlane(
        double N, double Mx, double My,
        double chiX, double chiBX, double chiBY,
        double Aeff, double Wx, double Wy,
        double Ry, double gammaM)
    {
        double nPart = Math.Abs(N) / (chiX * Aeff);
        double mxPart = Math.Abs(Mx) / (chiBX * Wx);
        double myPart = Math.Abs(My) / (chiBY * Wy);
        double applied = nPart + mxPart + myPart;
        double allowable = Ry / gammaM;
        return new CheckDetail
        {
            Formula = "8.1.4",
            Description = "Сжатие с изгибом (две плоскости)",
            NormReference = "СП 16.13330.2017, п. 8.1.4",
            Applied = applied,
            Allowable = allowable,
            Variables = new Dictionary<string, double>
            {
                ["N"] = N, ["Mx"] = Mx, ["My"] = My,
                ["φX"] = chiX, ["φbX"] = chiBX, ["φbY"] = chiBY,
                ["Aeff"] = Aeff, ["Wx"] = Wx, ["Wy"] = Wy,
                ["Ry"] = Ry, ["γM"] = gammaM,
                ["N/(φ·Aeff)"] = nPart,
                ["Mx/(φbX·Wx)"] = mxPart,
                ["My/(φbY·Wy)"] = myPart
            }
        };
    }

    /// <summary>
    /// Сжатие с изгибом (8.1.3/8.1.4) — обёртка для обратной совместимости.
    /// </summary>
    public static CheckDetail CheckCompressionBending(
        double N, double Mx, double My,
        double chiX, double chiBX, double chiBY,
        double Aeff, double Wx, double Wy, double Ry, double gammaM)
    {
        if (Math.Abs(My) > 1e-10)
            return CheckCompressionBendingDoublePlane(
                N, Mx, My, chiX, chiBX, chiBY, Aeff, Wx, Wy, Ry, gammaM);
        return CheckCompressionBendingSinglePlane(
            N, Mx, chiX, chiBX, Aeff, Wx, Ry, gammaM);
    }

    /// <summary>
    /// Поперечный изгиб (срез) (8.6).
    /// τ = Q/Aw ≤ Ry/(γM·√3)
    /// </summary>
    public static CheckDetail CheckShear(
        double Q, double Aweb, double Ry, double gammaM)
    {
        double allowable = Aweb * Ry / (gammaM * Math.Sqrt(3));
        return new CheckDetail
        {
            Formula = "8.6",
            Description = "Поперечный изгиб (срез)",
            NormReference = "СП 16.13330.2017, п. 8.6",
            Applied = Math.Abs(Q),
            Allowable = allowable,
            Variables = new Dictionary<string, double>
            {
                ["Q"] = Q, ["Aw"] = Aweb,
                ["Ry"] = Ry, ["γM"] = gammaM,
                ["τ"] = Math.Abs(Q) / Aweb,
                ["τR"] = Ry / (gammaM * Math.Sqrt(3))
            }
        };
    }

    /// <summary>
    /// Местное выпучивание стенки (8.4).
    /// λ̄w ≤ λ̄w,lim
    /// </summary>
    public static CheckDetail CheckWebBuckling(
        double lambdaBarW, double lambdaBarWLim)
    {
        return new CheckDetail
        {
            Formula = "8.4",
            Description = "Местное выпучивание стенки",
            NormReference = "СП 16.13330.2017, п. 8.4",
            Applied = lambdaBarW,
            Allowable = lambdaBarWLim,
            Variables = new Dictionary<string, double>
            {
                ["λ̄w"] = lambdaBarW,
                ["λ̄w,lim"] = lambdaBarWLim
            }
        };
    }

    /// <summary>
    /// Местное выпучивание полки (8.4).
    /// λ̄f ≤ λ̄f,lim
    /// </summary>
    public static CheckDetail CheckFlangeBuckling(
        double lambdaBarF, double lambdaBarFLim)
    {
        return new CheckDetail
        {
            Formula = "8.4",
            Description = "Местное выпучивание полки",
            NormReference = "СП 16.13330.2017, п. 8.4",
            Applied = lambdaBarF,
            Allowable = lambdaBarFLim,
            Variables = new Dictionary<string, double>
            {
                ["λ̄f"] = lambdaBarF,
                ["λ̄f,lim"] = lambdaBarFLim
            }
        };
    }

    /// <summary>
    /// Проверка на центральное растяжение (8.1.1).
    /// N/An ≤ Ry/γM
    /// </summary>
    public static CheckDetail CheckAxialTension(
        double N, double Aeff, double Ry, double gammaM)
    {
        double allowable = Aeff * Ry / gammaM;
        return new CheckDetail
        {
            Formula = "8.1.1",
            Description = "Центральное растяжение",
            NormReference = "СП 16.13330.2017, п. 8.1.1",
            Applied = Math.Abs(N),
            Allowable = allowable,
            Variables = new Dictionary<string, double>
            {
                ["N"] = N, ["An"] = Aeff,
                ["Ry"] = Ry, ["γM"] = gammaM
            }
        };
    }

    /// <summary>
    /// Проверка на растяжение с изгибом (8.1.5).
    /// N/At + Mx/Wx + My/Wy ≤ Ry/γM
    /// </summary>
    public static CheckDetail CheckTensionBending(
        double N, double Mx, double My,
        double Aeff, double Wx, double Wy, double Ry, double gammaM)
    {
        double nPart = Math.Abs(N) / Aeff;
        double mxPart = Math.Abs(Mx) / Wx;
        double myPart = Math.Abs(My) / Wy;
        double applied = nPart + mxPart + myPart;
        double allowable = Ry / gammaM;

        string desc = Math.Abs(My) > 1e-10
            ? "Растяжение с изгибом (две плоскости)"
            : "Растяжение с изгибом (одна плоскость)";

        return new CheckDetail
        {
            Formula = "8.1.5",
            Description = desc,
            NormReference = "СП 16.13330.2017, п. 8.1.5",
            Applied = applied,
            Allowable = allowable,
            Variables = new Dictionary<string, double>
            {
                ["N"] = N, ["Mx"] = Mx, ["My"] = My,
                ["Aeff"] = Aeff, ["Wx"] = Wx, ["Wy"] = Wy,
                ["Ry"] = Ry, ["γM"] = gammaM,
                ["N/Aeff"] = nPart,
                ["Mx/Wx"] = mxPart,
                ["My/Wy"] = myPart
            }
        };
    }

    /// <summary>
    /// Проверка на кручение (8.8.1).
    /// Mz/Wt ≤ Ry/γM
    /// </summary>
    public static CheckDetail CheckTorsion(
        double Mz, double Wt, double Ry, double gammaM)
    {
        double allowable = Wt * Ry / gammaM;
        return new CheckDetail
        {
            Formula = "8.8.1",
            Description = "Кручение",
            NormReference = "СП 16.13330.2017, п. 8.8.1",
            Applied = Math.Abs(Mz),
            Allowable = allowable,
            Variables = new Dictionary<string, double>
            {
                ["Mz"] = Mz, ["Wt"] = Wt,
                ["Ry"] = Ry, ["γM"] = gammaM
            }
        };
    }

    /// <summary>
    /// Проверка при совместном действии изгиба и кручения (8.9.1).
    /// √(σ² + 3τ²) ≤ Ry/γM
    /// σ = M/W, τ = Mz/Wt
    /// </summary>
    public static CheckDetail CheckBendingTorsion(
        double M, double Mz, double W, double Wt, double Ry, double gammaM)
    {
        double sigma = Math.Abs(M) / W;
        double tau = Math.Abs(Mz) / Wt;
        double applied = Math.Sqrt(sigma * sigma + 3 * tau * tau);
        double allowable = Ry / gammaM;
        return new CheckDetail
        {
            Formula = "8.9.1",
            Description = "Изгиб + кручение",
            NormReference = "СП 16.13330.2017, п. 8.9.1",
            Applied = applied,
            Allowable = allowable,
            Variables = new Dictionary<string, double>
            {
                ["M"] = M, ["Mz"] = Mz, ["W"] = W, ["Wt"] = Wt,
                ["Ry"] = Ry, ["γM"] = gammaM,
                ["σ"] = sigma, ["τ"] = tau,
                ["√(σ²+3τ²)"] = applied
            }
        };
    }

    /// <summary>
    /// Проверка при продольном изгибе (8.7.1).
    /// N/A + M/(φb·W) ≤ Ry/γM
    /// </summary>
    public static CheckDetail CheckLateralBending(
        double N, double M, double chiB,
        double Aeff, double Weff, double Ry, double gammaM, string axis = "")
    {
        double nPart = Math.Abs(N) / Aeff;
        double mPart = Math.Abs(M) / (chiB * Weff);
        double applied = nPart + mPart;
        double allowable = Ry / gammaM;
        string desc = string.IsNullOrEmpty(axis)
            ? "Продольный изгиб"
            : $"Продольный изгиб ({axis})";
        return new CheckDetail
        {
            Formula = "8.7.1",
            Description = desc,
            NormReference = "СП 16.13330.2017, п. 8.7.1",
            Applied = applied,
            Allowable = allowable,
            Variables = new Dictionary<string, double>
            {
                ["N"] = N, ["M"] = M, ["φb"] = chiB,
                ["Aeff"] = Aeff, ["Weff"] = Weff,
                ["Ry"] = Ry, ["γM"] = gammaM,
                ["N/Aeff"] = nPart,
                ["M/(φb·Weff)"] = mPart
            }
        };
    }

    /// <summary>
    /// Местное смятие стенки (8.2.1).
    /// σ = F / (tw·(a + 5·h0)) ≤ Ry/γM
    /// F — сосредоточенная сила [Н]
    /// a — длина распределения нагрузки [м]
    /// h0 — высота стенки [м]
    /// tw — толщина стенки [м]
    /// </summary>
    public static CheckDetail CheckWebCrippling(
        double F, double a, double h0, double tw, double Ry, double gammaM)
    {
        double bearingLength = a + 5 * h0;
        double allowable = bearingLength * tw * Ry / gammaM;
        return new CheckDetail
        {
            Formula = "8.2.1",
            Description = "Местное смятие стенки",
            NormReference = "СП 16.13330.2017, п. 8.2.1",
            Applied = Math.Abs(F),
            Allowable = allowable,
            Variables = new Dictionary<string, double>
            {
                ["F"] = F, ["a"] = a, ["h0"] = h0, ["tw"] = tw,
                ["Ry"] = Ry, ["γM"] = gammaM,
                ["a+5h0"] = bearingLength
            }
        };
    }

    /// <summary>
    /// Поперечный изгиб с учётом выпучивания стенки (8.5).
    /// τ ≤ τcr = k·π²·E / (12·(1-ν²)) · (tw/h)²
    /// k = 5.34 для длинной неподкреплённой пластины
    /// </summary>
    public static CheckDetail CheckShearBuckling(
        double Q, double Aweb, double h, double tw, double E, double Ry, double gammaM)
    {
        double nu = 0.3;
        double k = 5.34;
        double tauCr = k * Math.PI * Math.PI * E / (12 * (1 - nu * nu)) * (tw / h) * (tw / h);
        double tauAllow = Math.Min(tauCr, Ry / Math.Sqrt(3)) / gammaM;
        double tau = Math.Abs(Q) / Aweb;
        return new CheckDetail
        {
            Formula = "8.5",
            Description = "Срез с учётом выпучивания",
            NormReference = "СП 16.13330.2017, п. 8.5",
            Applied = tau,
            Allowable = tauAllow,
            Variables = new Dictionary<string, double>
            {
                ["Q"] = Q, ["Aw"] = Aweb, ["h"] = h, ["tw"] = tw,
                ["E"] = E, ["Ry"] = Ry, ["γM"] = gammaM,
                ["k"] = k, ["ν"] = nu,
                ["τ"] = tau, ["τcr"] = tauCr
            }
        };
    }

    /// <summary>
    /// Вычисляет площадь стенки для двутавра: Aw = (h - 2·tf)·tw.
    /// Для произвольного сечения — приближённо Aw ≈ A/2.
    /// </summary>
    public static double EstimateWebArea(SteelSection section)
    {
        var pts = section.OuterContour;
        if (pts.Count < 4) return section.Area * 0.5;

        double yMin = double.MaxValue, yMax = double.MinValue;
        double xMin = double.MaxValue, xMax = double.MinValue;
        foreach (var (x, y) in pts)
        {
            if (y < yMin) yMin = y;
            if (y > yMax) yMax = y;
            if (x < xMin) xMin = x;
            if (x > xMax) xMax = x;
        }

        double h = yMax - yMin;
        double midY = (yMin + yMax) / 2;
        double bandH = h * 0.3;

        double xMinMid = double.MaxValue, xMaxMid = double.MinValue;
        foreach (var (x, y) in pts)
        {
            if (Math.Abs(y - midY) < bandH)
            {
                if (x < xMinMid) xMinMid = x;
                if (x > xMaxMid) xMaxMid = x;
            }
        }

        if (xMaxMid > xMinMid + 1e-10)
        {
            double tw = xMaxMid - xMinMid;
            return h * tw;
        }

        return section.Area * 0.5;
    }

    /// <summary>
    /// Полная проверка на прочность.
    /// </summary>
    public static List<CheckDetail> CheckAll(
        SteelSection section, InternalForces forces, DesignContext context,
        double chiX = 1.0, double chiY = 1.0,
        double chiBX = 1.0, double chiBY = 1.0)
    {
        var details = new List<CheckDetail>();
        var fy = (section.Steel.C?.Ry ?? 235e6); // Па
        var gammaM = context.GammaM;

        // Растяжение (N > 0)
        if (forces.N > 1e-10)
        {
            // Растяжение с изгибом
            if (Math.Abs(forces.Mx) > 1e-10 || Math.Abs(forces.My) > 1e-10)
            {
                details.Add(CheckTensionBending(
                    forces.N, forces.Mx, forces.My,
                    section.Area, section.Wx, section.Wy, fy, gammaM));
            }
            // Чистое растяжение
            else
            {
                details.Add(CheckAxialTension(forces.N, section.Area, fy, gammaM));
            }
        }
        // Сжатие (N < 0)
        else if (forces.N < -1e-10)
        {
            // Центральное сжатие
            details.Add(CheckAxialCompression(
                forces.N, chiX, section.Area, fy, gammaM));

            // Сжатие с изгибом
            if (Math.Abs(forces.Mx) > 1e-10 || Math.Abs(forces.My) > 1e-10)
            {
                details.Add(CheckCompressionBending(
                    forces.N, forces.Mx, forces.My,
                    chiX, chiBX, chiBY,
                    section.Area, section.Wx, section.Wy, fy, gammaM));

                // Продольный изгиб (8.7.1)
                if (Math.Abs(forces.Mx) > 1e-10)
                    details.Add(CheckLateralBending(
                        forces.N, forces.Mx, chiBX,
                        section.Area, section.Wx, fy, gammaM, "X"));
                if (Math.Abs(forces.My) > 1e-10)
                    details.Add(CheckLateralBending(
                        forces.N, forces.My, chiBY,
                        section.Area, section.Wy, fy, gammaM, "Y"));
            }
        }
        // Без осевой силы — чистый изгиб
        else
        {
            if (Math.Abs(forces.Mx) > 1e-10)
                details.Add(CheckBending(forces.Mx, chiBX, section.Wx, fy, gammaM, "X"));
            if (Math.Abs(forces.My) > 1e-10)
                details.Add(CheckBending(forces.My, chiBY, section.Wy, fy, gammaM, "Y"));
        }

        // Срез
        double Q = Math.Max(Math.Abs(forces.Qy), Math.Abs(forces.Qz));
        if (Q > 1e-10)
        {
            double Aw = EstimateWebArea(section);
            details.Add(CheckShear(Q, Aw, fy, gammaM));

            // Срез с учётом выпучивания (8.5)
            double yMin = double.MaxValue, yMax = double.MinValue;
            foreach (var (px, py) in section.OuterContour)
            {
                if (py < yMin) yMin = py;
                if (py > yMax) yMax = py;
            }
            double h = yMax - yMin;
            if (h > 1e-6)
            {
                double tw = Aw / h;
                details.Add(CheckShearBuckling(Q, Aw, h, tw, section.Steel.E, fy, gammaM));
            }
        }

        // Кручение
        if (Math.Abs(forces.Mz) > 1e-10)
        {
            // Проверка на кручение (8.8.1)
            details.Add(CheckTorsion(forces.Mz, section.Wt, fy, gammaM));

            // Изгиб + кручение (8.9.1)
            if (Math.Abs(forces.Mx) > 1e-10 || Math.Abs(forces.My) > 1e-10)
            {
                double M = Math.Max(Math.Abs(forces.Mx), Math.Abs(forces.My));
                double W = Math.Abs(forces.Mx) > Math.Abs(forces.My) ? section.Wx : section.Wy;
                details.Add(CheckBendingTorsion(M, forces.Mz, W, section.Wt, fy, gammaM));
            }
        }

        return details;
    }
}
