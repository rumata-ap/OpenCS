using CScore;
using OpenCS.OpenSees.Model;

namespace OpenCS.OpenSees.CScore;

/// <summary>Модель бетона OpenSees для нативной параметризации.</summary>
public enum ConcreteModelKind { Concrete0102, Concrete04 }

/// <summary>Модель стали/арматуры OpenSees для нативной параметризации.</summary>
public enum SteelModelKind { Steel01, Steel02 }

/// <summary>Нативная модель, выбираемая для основной полигональной области.
/// Concrete-модели применимы к бетону, Steel-модели — к основной стальной области.</summary>
public enum MainMaterialModelKind { Concrete0102, Concrete04, Steel01, Steel02 }

/// <summary>Источник построения диаграммы материала для нелинейного FEM-расчёта.</summary>
public enum MaterialSource { Translated, Native }

/// <summary>Строит собственные (нативные) параметрические материалы OpenSees
/// (Concrete01/02/04, Steel01/02) из характеристик материала CScore — в противовес
/// <see cref="MaterialDiagramMapper"/>, который транслирует диаграмму (Diagramm) в
/// ElasticMultiLinear. См. docs/superpowers/specs/2026-07-25-opensees-native-materials-design.md
/// для обоснования формул и границ применимости.
///
/// Для бетона доступны две модели (<see cref="ConcreteModelKind"/>), выбираемые на постановке:
/// <see cref="ConcreteModelKind.Concrete0102"/> (Kent-Scott-Park, линейное размягчение
/// растяжения до плоского нуля — Concrete02, либо без растяжения — Concrete01) и
/// <see cref="ConcreteModelKind.Concrete04"/> (Popovics на сжатие + экспоненциальное затухание
/// растяжения, без плоского нулевого участка). На реальном сценарии кинематических нагрузок
/// Concrete02/01 не сходились (Concrete01 — даже падал с access violation), Concrete04 сходится
/// заметно устойчивее на умеренных деформациях, но полный сценарий с сильным трещинообразованием
/// не сходится ни с одной из моделей (найден snap-through, требует integrator ArcLength — см.
/// заметку сессии). Обе модели оставлены как выбор пользователя, а не одна «правильная».</summary>
public static class NativeMaterialMapper
{
    /// <summary>Доля пиковой прочности бетона, сохраняющаяся на предельной деформации (fpcu/fpc)
    /// для Concrete01/02. Concrete01/02 линейно снижают прочность между epsc0 и epsU — если
    /// fpcu==fpc, этот участок становится плоским (нулевой наклон), что вырождает матрицу
    /// гибкости сечения в forceBeamColumn. 0.2 — типовое значение из примеров OpenSees для
    /// Kent-Park-подобной модели бетона.</summary>
    private const double ConcreteResidualStrengthFraction = 0.2;
    /// <summary>Наклон размягчения растяжения Concrete02 (Ets = Ft / ConcreteTensionSofteningStrainSpan)
    /// — растянут на широкий диапазон деформаций (не на узкий упругий Et0), иначе наклон
    /// получается таким же крутым, как восходящая упругая ветвь.</summary>
    private const double ConcreteTensionSofteningStrainSpan = 0.002;
    private const double ConcreteTensionLambda = 0.1;
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
        ConcreteModelKind concreteModel,
        SteelModelKind steelModel,
        double? steelHardeningRatioOverride)
    {
        MainMaterialModelKind mainMaterialModel = materialType == MatType.Concrete
            ? concreteModel == ConcreteModelKind.Concrete0102
                ? MainMaterialModelKind.Concrete0102
                : MainMaterialModelKind.Concrete04
            : steelModel == SteelModelKind.Steel01
                ? MainMaterialModelKind.Steel01
                : MainMaterialModelKind.Steel02;

        return Map(
            chars,
            materialType,
            considerConcreteTension,
            mainMaterialModel,
            steelModel,
            isReinforcement: materialType != MatType.Concrete,
            steelHardeningRatioOverride);
    }

    /// <summary>Строит нативную модель с учётом роли области в fiber-сечении.
    /// Основная область использует MainMaterialModel, вложенная область — SteelModel.</summary>
    public static NativeMaterialSpec? Map(
        MaterialChars? chars,
        MatType materialType,
        bool considerConcreteTension,
        MainMaterialModelKind mainMaterialModel,
        SteelModelKind steelModel,
        bool isReinforcement,
        double? steelHardeningRatioOverride)
    {
        if (chars is null || materialType == MatType.Custom)
            return null;

        if (materialType == MatType.Concrete)
        {
            if (mainMaterialModel is not
                (MainMaterialModelKind.Concrete0102 or MainMaterialModelKind.Concrete04))
            {
                throw new CScoreMappingException(
                    "Для бетонной основной области выбрана стальная нативная модель.");
            }

            var concreteModel = mainMaterialModel == MainMaterialModelKind.Concrete0102
                ? ConcreteModelKind.Concrete0102
                : ConcreteModelKind.Concrete04;
            return MapConcrete(chars, considerConcreteTension, concreteModel);
        }

        if (materialType is MatType.ReSteelF or MatType.ReSteelU or MatType.Steel)
        {
            SteelModelKind selected = isReinforcement
                ? steelModel
                : mainMaterialModel switch
                {
                    MainMaterialModelKind.Steel01 => SteelModelKind.Steel01,
                    MainMaterialModelKind.Steel02 => SteelModelKind.Steel02,
                    _ => throw new CScoreMappingException(
                        "Для стальной основной области выбрана бетонная нативная модель.")
                };

            return MapSteel(
                chars,
                materialType,
                selected,
                isReinforcement ? steelHardeningRatioOverride : null);
        }

        return null;
    }

    private static NativeMaterialSpec MapConcrete(
        MaterialChars chars, bool considerConcreteTension, ConcreteModelKind concreteModel)
    {
        return concreteModel switch
        {
            ConcreteModelKind.Concrete0102 => MapConcrete0102(chars, considerConcreteTension),
            ConcreteModelKind.Concrete04 => MapConcrete04(chars, considerConcreteTension),
            _ => throw new ArgumentOutOfRangeException(nameof(concreteModel), concreteModel, "Неизвестная модель бетона.")
        };
    }

    private static NativeMaterialSpec MapConcrete0102(MaterialChars chars, bool considerConcreteTension)
    {
        double fpc = CScoreUnitConverter.KilopascalsToPascals(chars.Fc);
        double epsc0 = chars.Ec0;
        double fpcu = fpc * ConcreteResidualStrengthFraction;
        double epsU = chars.Ec2;

        if (!considerConcreteTension)
            return new Concrete01Spec(fpc, epsc0, fpcu, epsU);

        double ft = CScoreUnitConverter.KilopascalsToPascals(chars.Ft);
        double ets = ft / ConcreteTensionSofteningStrainSpan;
        return new Concrete02Spec(fpc, epsc0, fpcu, epsU, ConcreteTensionLambda, ft, ets);
    }

    private static NativeMaterialSpec MapConcrete04(MaterialChars chars, bool considerConcreteTension)
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
