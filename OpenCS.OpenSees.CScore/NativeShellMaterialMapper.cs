using CScore;
using OpenCS.OpenSees.Model;

namespace OpenCS.OpenSees.CScore;

/// <summary>Строит нелинейные native shell-материалы (LayeredShell-совместимые) из
/// характеристик материала CScore — shell-аналог NativeMaterialMapper (beam fiber). Для
/// арматуры переиспользует формулы NativeMaterialMapper (та же Fy/E0/B-параметризация, тот же
/// hardening ratio) — не дублирует их. Для бетона использует
/// PlasticDamageConcretePlaneStressShellMaterialSpec + обязательную обёртку
/// PlateFromPlaneStressShellMaterialSpec (подтверждено вручную через реальный OpenSees.exe —
/// см. docs/superpowers/specs/2026-07-31-nonlinear-rc-shell-slice1-native-materials-design.md).
///
/// Beta/Ap/An/Bn — калиброванные литературные значения damage-plasticity модели (Lee &amp;
/// Fenves), НЕ выводятся из диаграммы СП63 — требуют независимой верификации на эталонной
/// задаче (следующие срезы дорожной карты nonlinear RC-shell).</summary>
public static class NativeShellMaterialMapper
{
    /// <summary>Коэффициент Пуассона бетона — совпадает с дефолтом CScore/PlateSection.cs
    /// (nu = 0.2) для собственной линейной механики плиты; единый Poisson ratio во всём
    /// shell-стеке.</summary>
    private const double ConcretePoissonRatio = 0.2;
    private const double ConcreteDamageBeta = 0.6;
    private const double ConcreteDamageAp = 0.5;
    private const double ConcreteDamageAn = 2.0;
    private const double ConcreteDamageBn = 0.14;

    /// <summary>Строит цепочку [PlasticDamageConcretePlaneStress, PlateFromPlaneStress] из
    /// характеристик бетона. Fc/Ft берутся по модулю — CScore хранит Fc отрицательным
    /// (конвенция сжатия), а PlasticDamageConcretePlaneStress ожидает положительные величины.</summary>
    public static IReadOnlyList<NativeShellMaterialDefinition> MapConcrete(MaterialChars chars, string sourceId)
    {
        ArgumentNullException.ThrowIfNull(chars);

        double e = CScoreUnitConverter.KilopascalsToPascals(chars.E);
        double ft = Math.Abs(CScoreUnitConverter.KilopascalsToPascals(chars.Ft));
        double fc = Math.Abs(CScoreUnitConverter.KilopascalsToPascals(chars.Fc));
        double g = e / (2 * (1 + ConcretePoissonRatio));

        var damage = new NativeShellMaterialDefinition(1, $"{sourceId}:damage",
            new PlasticDamageConcretePlaneStressShellMaterialSpec(
                e, ConcretePoissonRatio, ft, fc, ConcreteDamageBeta, ConcreteDamageAp, ConcreteDamageAn, ConcreteDamageBn));
        var wrapped = new NativeShellMaterialDefinition(2, $"{sourceId}:plate",
            new PlateFromPlaneStressShellMaterialSpec(1, g));

        return [damage, wrapped];
    }

    /// <summary>Строит цепочку [uniaxial сталь, PlateRebar] из характеристик арматуры,
    /// переиспользуя формулы NativeMaterialMapper.Map (та же Fy/E0/B-параметризация и
    /// hardening ratio, что и для стержневых КЭ). AngleDegrees заполняется placeholder-ом (0) —
    /// реальная ориентация 0°/90° выполняется PlateSectionOpenSeesMapper.</summary>
    public static IReadOnlyList<NativeShellMaterialDefinition> MapRebar(
        MaterialChars chars,
        MatType materialType,
        SteelModelKind steelModel,
        double? steelHardeningRatioOverride,
        string sourceId)
    {
        ArgumentNullException.ThrowIfNull(chars);

        MainMaterialModelKind mainModel = steelModel == SteelModelKind.Steel01
            ? MainMaterialModelKind.Steel01
            : MainMaterialModelKind.Steel02;

        NativeMaterialSpec? beamSpec = NativeMaterialMapper.Map(
            chars, materialType, considerConcreteTension: false,
            mainModel, steelModel, isReinforcement: true, steelHardeningRatioOverride);

        NativeShellMaterialSpec uniaxial = beamSpec switch
        {
            Steel01Spec s => new Steel01UniaxialShellMaterialSpec(s.Fy, s.E0, s.B),
            Steel02Spec s => new Steel02UniaxialShellMaterialSpec(s.Fy, s.E0, s.B, s.R0, s.CR1, s.CR2),
            _ => throw new CScoreMappingException(
                $"NativeMaterialMapper не вернул модель стали для арматуры shell (material {sourceId}).")
        };

        var steelDefinition = new NativeShellMaterialDefinition(1, $"{sourceId}:uniaxial", uniaxial);
        var plateRebar = new NativeShellMaterialDefinition(2, $"{sourceId}:plate",
            new PlateRebarShellMaterialSpec(1, 0));

        return [steelDefinition, plateRebar];
    }
}
