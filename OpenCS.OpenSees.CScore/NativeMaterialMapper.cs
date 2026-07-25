using CScore;
using OpenCS.OpenSees.Model;

namespace OpenCS.OpenSees.CScore;

/// <summary>Модель стали/арматуры OpenSees для нативной параметризации.</summary>
public enum SteelModelKind { Steel01, Steel02 }

/// <summary>Источник построения диаграммы материала для нелинейного FEM-расчёта.</summary>
public enum MaterialSource { Translated, Native }

/// <summary>Строит собственные (нативные) параметрические материалы OpenSees
/// (Concrete04, Steel01/02) из характеристик материала CScore — в противовес
/// <see cref="MaterialDiagramMapper"/>, который транслирует диаграмму (Diagramm) в
/// ElasticMultiLinear. См. docs/superpowers/specs/2026-07-25-opensees-native-materials-design.md
/// для обоснования формул и границ применимости.
///
/// Concrete04 (Попович на сжатие + экспоненциальное затухание растяжения), а не Concrete02
/// (линейное размягчение до плоского нуля) — на реальном сценарии кинематических нагрузок
/// Concrete02 не сходился (и даже падал при отключённом растяжении, Concrete01): линейная
/// огибающая растяжения после исчерпания Ets неизбежно выходит на буквально плоский нулевой
/// участок — та же вырожденная матрица гибкости, с которой начиналась вся история отладки этой
/// сессии. Экспонента Concrete04 асимптотически стремится к нулю, никогда не давая плоского
/// сегмента.</summary>
public static class NativeMaterialMapper
{
    /// <summary>Экспоненциальный параметр затухания растяжения Concrete04 (beta) — контролирует
    /// скорость спада остаточного напряжения после предельной растяжимости Et. Фиксированное
    /// умеренное значение, не выносится в UI (по аналогии с Lambda/R0/cR1/cR2).</summary>
    private const double ConcreteTensionBeta = 0.1;
    private const double Steel02R0 = 18;
    private const double Steel02CR1 = 0.925;
    private const double Steel02CR2 = 0.15;
    private const double DefaultSteelHardeningRatio = 0.01;
    private const double MinSteelHardeningRatio = 0.001;
    private const double MaxSteelHardeningRatio = 0.05;

    /// <summary>Строит <see cref="NativeMaterialSpec"/> из характеристик материала.
    /// Возвращает <c>null</c>, если параметризация невозможна (нет характеристик для данного
    /// вида расчёта, либо материал типа Custom) — в этом случае вызывающий код обязан
    /// откатиться на <see cref="MaterialDiagramMapper"/>.</summary>
    public static NativeMaterialSpec? Map(
        MaterialChars? chars,
        MatType materialType,
        bool considerConcreteTension,
        SteelModelKind steelModel,
        double? steelHardeningRatioOverride)
    {
        if (chars is null || materialType == MatType.Custom)
            return null;

        return materialType switch
        {
            MatType.Concrete => MapConcrete(chars, considerConcreteTension),
            MatType.ReSteelF or MatType.ReSteelU or MatType.Steel =>
                MapSteel(chars, materialType, steelModel, steelHardeningRatioOverride),
            _ => null
        };
    }

    private static NativeMaterialSpec MapConcrete(MaterialChars chars, bool considerConcreteTension)
    {
        double fc = CScoreUnitConverter.KilopascalsToPascals(chars.Fc);
        double ec0 = chars.Ec0;
        double ecu = chars.Ec2;
        double ec = CScoreUnitConverter.KilopascalsToPascals(chars.E);

        if (!considerConcreteTension)
            return new Concrete04Spec(fc, ec0, ecu, ec, Fct: null, Et: null, Beta: null);

        double fct = CScoreUnitConverter.KilopascalsToPascals(chars.Ft);
        double et = chars.Et2;
        return new Concrete04Spec(fc, ec0, ecu, ec, fct, et, ConcreteTensionBeta);
    }

    private static NativeMaterialSpec MapSteel(
        MaterialChars chars, MatType materialType, SteelModelKind steelModel, double? hardeningOverride)
    {
        double fyKpa = materialType == MatType.Steel ? chars.Ry : chars.Ft;
        double fy = CScoreUnitConverter.KilopascalsToPascals(fyKpa);
        double e0 = CScoreUnitConverter.KilopascalsToPascals(chars.E);
        double b = ResolveHardeningRatio(chars, fyKpa, hardeningOverride);

        return steelModel switch
        {
            SteelModelKind.Steel01 => new Steel01Spec(fy, e0, b),
            SteelModelKind.Steel02 => new Steel02Spec(fy, e0, b, Steel02R0, Steel02CR1, Steel02CR2),
            _ => throw new ArgumentOutOfRangeException(nameof(steelModel), steelModel, "Неизвестная модель стали.")
        };
    }

    private static double ResolveHardeningRatio(MaterialChars chars, double fyKpa, double? overrideValue)
    {
        if (overrideValue is { } value)
            return value;

        double eKpa = chars.E;
        double yieldStrain = fyKpa / eKpa;
        if (chars.Ru > fyKpa && fyKpa > 0 && chars.Et2 > yieldStrain)
        {
            double secantModulusKpa = (chars.Ru - fyKpa) / (chars.Et2 - yieldStrain);
            double b = secantModulusKpa / eKpa;
            return Math.Clamp(b, MinSteelHardeningRatio, MaxSteelHardeningRatio);
        }

        return DefaultSteelHardeningRatio;
    }
}
