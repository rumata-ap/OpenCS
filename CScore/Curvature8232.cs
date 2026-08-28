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

    /// <summary>Вычисляет ψs по деформациям плоскости для напрягаемой арматуры.
    /// Знак деформации в момент образования трещины не исключает стержень.</summary>
    public static double PsiSFromPlaneStrains(double epsCrc, double eps)
    {
        if (!double.IsFinite(epsCrc) || !double.IsFinite(eps) || eps <= 0.0)
            return 1.0;

        return Math.Clamp(1.0 / (1.0 + 0.8 * Math.Abs(epsCrc) / eps), 0.0, 1.0);
    }

    /// <summary>Вычисляет ψs для текущей плоскости после достижения текущей деформацией
    /// модуля деформации в момент образования трещины.</summary>
    public static double PsiSForCurrentPlane(double epsCrc, double eps)
    {
        return PsiSFromPlaneStrains(epsCrc, eps);
    }

    /// <summary>Возвращает напряжение арматуры после поправки σ/ψs.</summary>
    /// <param name="diagramStress">Напряжение по диаграмме материала.</param>
    /// <param name="psiS">Коэффициент ψs по п. 8.2.32.</param>
    public static double CorrectedStress(double diagramStress, double psiS)
    {
        if (!double.IsFinite(diagramStress)
            || !double.IsFinite(psiS)
            || psiS <= 0.0)
            return diagramStress;

        return diagramStress / psiS;
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
    /// Корректирует вклад растянутой арматуры в равновесие по п. 8.2.32 СП 63.13330.
    /// Метод вызывается после <see cref="CrossSection.Integral"/> и использует уже
    /// вычисленные поля фибр.
    /// </summary>
    /// <remarks>
    /// Плоскость <paramref name="k"/> — плоскость СРЕДНИХ деформаций, поэтому деформация
    /// стержня в трещине равна εs,crc = εs/ψs = εs + 0,8·εcrc (тождество для
    /// <see cref="PsiS"/>). Напряжение берётся С ДИАГРАММЫ материала при этой деформации,
    /// а не делением σ(εs)/ψs: на линейном участке результат совпадает, но за площадкой
    /// текучести деление дало бы σ &gt; Rs — физически невозможное напряжение, из-за которого
    /// момент кривой ψs превышал предельную несущую способность сечения.
    /// Если диаграмма для <paramref name="calc"/> недоступна (например, в модульных тестах
    /// с вручную заданными σ), используется прежнее масштабирование σ/ψs.
    /// </remarks>
    public static Load ApplyPsiCorrection(
        CrossSection section, Kurvature k, Load baseLoad,
        IReadOnlyDictionary<Fiber, double> epsCrcByFiber, CalcType calc,
        bool requireCurrentPlaneStrain = false)
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

            area.Diagramms.TryGetValue(calc, out var dgr);

            foreach (var fiber in area.Fibers)
            {
                if (fiber.TypeFiber != FiberType.point ||
                    !epsCrcByFiber.TryGetValue(fiber, out var epsCrc))
                    continue;

                // Зону растяжения и ψs определяем по плоскости от внешней нагрузки.
                // Полную деформацию сохраняем только для обращения к диаграмме: начальная
                // деформация не должна включать сжатый по плоскости стержень в ψs, но должна
                // оставаться в его фактическом напряжении.
                double eps = fiber.Eps;
                double epsFull = eps + fiber.Eps_p;
                if (requireCurrentPlaneStrain && fiber.Eps_p != 0.0
                    ? eps <= 0.0
                    : epsFull <= 0.0)
                    continue;

                double psi;
                if (requireCurrentPlaneStrain && fiber.Eps_p != 0.0)
                {
                    psi = PsiSForCurrentPlane(epsCrc, eps);
                }
                else
                {
                    psi = PsiS(epsCrc, epsFull);
                }
                if (psi <= 0.0 || psi >= 1.0 - 1e-12)
                    continue;

                double scale = 1.0 / psi;
                double sigCrc;
                double e2Crc;
                if (dgr != null)
                {
                    // εs,crc = εs/ψs — деформация стержня В ТРЕЩИНЕ; σ снимается с диаграммы,
                    // поэтому за площадкой текучести поправка сама собой затухает.
                    // dεs,crc/dεs = 1, значит касательный модуль переносится без масштаба.
                    sigCrc = dgr.Sig(fiber.Eps_p + eps * scale, out e2Crc);
                }
                else
                {
                    sigCrc = fiber.Sig * scale;
                    e2Crc = fiber.E2 * scale;
                }

                double dSig = sigCrc - fiber.Sig;
                double addN = dSig * fiber.Area;
                double addMx = addN * fiber.Y;
                double addMy = addN * fiber.X;

                fiber.Sig = sigCrc;
                fiber.E2 = e2Crc;
                fiber.E = Math.Abs(epsFull) > 1e-20 ? sigCrc / epsFull : 0.0;
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
