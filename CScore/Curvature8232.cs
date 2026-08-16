using System;
using System.Collections.Generic;

namespace CScore;

/// <summary>Вспомогательные формулы деформационной модели по п. 8.2.32 СП 63.13330.</summary>
public static class Curvature8232
{
    /// <summary>
    /// Вычисляет коэффициент работы растянутой арматуры между трещинами:
    /// ψs = 1 / (1 + 0,8·εs,crc / εs).
    /// </summary>
    /// <param name="epsCrc">Деформация арматуры сразу после образования трещины.</param>
    /// <param name="eps">Средняя деформация арматуры на рассматриваемой стадии.</param>
    public static double PsiS(double epsCrc, double eps)
    {
        if (!double.IsFinite(epsCrc) || !double.IsFinite(eps) || eps <= 0.0 || epsCrc <= 0.0)
            return 1.0;

        return Math.Clamp(1.0 / (1.0 + 0.8 * epsCrc / eps), 0.0, 1.0);
    }

    /// <summary>
    /// Вычисляет альтернативный коэффициент по п. 8.2.18 при замене
    /// отношения напряжений соответствующим отношением деформаций:
    /// ψs = 1 - 0,8·εs,crc / εs.
    /// </summary>
    /// <param name="epsCrc">Деформация арматуры сразу после образования трещины.</param>
    /// <param name="eps">Средняя деформация арматуры на рассматриваемой стадии.</param>
    public static double PsiSFromStrainRatio(double epsCrc, double eps)
    {
        if (!double.IsFinite(epsCrc) || !double.IsFinite(eps) || eps <= 0.0 || epsCrc <= 0.0)
            return 1.0;
        if (eps < epsCrc)
            return 1.0;

        return Math.Clamp(1.0 - 0.8 * epsCrc / eps, 0.0, 1.0);
    }

    /// <summary>
    /// Корректирует вклад растянутой арматуры в равновесие делением на ψs
    /// по п. 8.2.32 СП 63.13330. Метод вызывается после <see cref="CrossSection.Integral"/>
    /// и использует уже вычисленные поля фибр.
    /// </summary>
    public static Load ApplyPsiCorrection(
        CrossSection section, Kurvature k, Load baseLoad,
        IReadOnlyDictionary<Fiber, double> epsCrcByFiber)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(epsCrcByFiber);

        double dN = 0.0;
        double dMx = 0.0;
        double dMy = 0.0;

        foreach (var (area, _) in section.EnumerateAreas(k))
        {
            if (area.Material?.Type is not (MatType.ReSteelF or MatType.ReSteelU))
                continue;

            foreach (var fiber in area.Fibers)
            {
                if (fiber.TypeFiber != FiberType.point ||
                    !epsCrcByFiber.TryGetValue(fiber, out var epsCrc))
                    continue;

                double eps = fiber.Eps + fiber.Eps_p;
                if (eps <= 0.0)
                    continue;

                double psi = PsiS(epsCrc, eps);
                if (psi <= 0.0 || psi >= 1.0 - 1e-12)
                    continue;

                double scale = 1.0 / psi;
                double addN = fiber.N * (scale - 1.0);
                double addMx = fiber.Mx * (scale - 1.0);
                double addMy = fiber.My * (scale - 1.0);

                fiber.Sig *= scale;
                fiber.E2 *= scale;
                fiber.E *= scale;
                fiber.N += addN;
                fiber.Mx += addMx;
                fiber.My += addMy;

                dN += addN;
                dMx += addMx;
                dMy += addMy;
            }
        }

        baseLoad.N += dN;
        baseLoad.Mx += dMx;
        baseLoad.My += dMy;
        return baseLoad;
    }
}
