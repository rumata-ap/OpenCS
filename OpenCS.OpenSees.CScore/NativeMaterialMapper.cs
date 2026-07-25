using CScore;
using OpenCS.OpenSees.Model;

namespace OpenCS.OpenSees.CScore;

/// <summary>Модель стали/арматуры OpenSees для нативной параметризации.</summary>
public enum SteelModelKind { Steel01, Steel02 }

/// <summary>Источник построения диаграммы материала для нелинейного FEM-расчёта.</summary>
public enum MaterialSource { Translated, Native }

/// <summary>Строит собственные (нативные) параметрические материалы OpenSees
/// (Concrete01/02, Steel01/02) из характеристик материала CScore — в противовес
/// <see cref="MaterialDiagramMapper"/>, который транслирует диаграмму (Diagramm) в
/// ElasticMultiLinear. См. docs/superpowers/specs/2026-07-25-opensees-native-materials-design.md
/// для обоснования формул и границ применимости.</summary>
public static class NativeMaterialMapper
{
    private const double ConcreteTensionLambda = 0.1;
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
        double fpc = CScoreUnitConverter.KilopascalsToPascals(chars.Fc);
        double epsc0 = chars.Ec0;
        double fpcu = fpc;
        double epsU = chars.Ec2;

        if (!considerConcreteTension)
            return new Concrete01Spec(fpc, epsc0, fpcu, epsU);

        double ft = CScoreUnitConverter.KilopascalsToPascals(chars.Ft);
        double ets = ft / chars.Et0;
        return new Concrete02Spec(fpc, epsc0, fpcu, epsU, ConcreteTensionLambda, ft, ets);
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
