using System;

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
}
