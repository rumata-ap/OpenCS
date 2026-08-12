using CScore.Fem;

namespace CScore.PlateStrip;

/// <summary>Результат расширенного linear-only preflight RVE-адаптера.</summary>
public sealed record ShellMeshPatchPreflightResult(
    bool IsLinear, IReadOnlyList<FemValidationDiagnostic> Diagnostics);

/// <summary>Расширяет принцип PlateSectionTangentSnapshot.Create (малая проба у нуля) пробами
/// на границах заявленного рабочего диапазона состояния (ShellMeshPatchStateBounds) — малая
/// проба у нуля не ловит материал, линейный у нуля, но с изломом диаграммы в рабочем диапазоне
/// (см. спеку, раздел «Расширенный linear-only preflight»). absoluteTolerance по умолчанию —
/// 1e-6, не 1e-8: PlateSection.ComputeTangent сам строит A/B/D через численное дифференцирование
/// (fdStep по умолчанию 1e-7), и теоретически нулевые элементы B-блока (симметричное сечение)
/// несут собственный шум порядка 1e-8, растущий линейно с E материала — при 1e-8 preflight ложно
/// отклонял заведомо линейные материалы реалистичного масштаба (эмпирически подтверждено).</summary>
public static class ShellMeshPatchPreflight
{
    public static ShellMeshPatchPreflightResult CheckLinear(
        PlateSection section,
        Diagramm concreteDiagram,
        Diagramm rebarDiagram,
        IReadOnlyList<Diagramm?>? layerDiagrams,
        ShellMeshPatchStateBounds bounds,
        double relativeTolerance = 1e-4,
        double absoluteTolerance = 1e-6,
        int pointsPerAxis = 3)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(concreteDiagram);
        ArgumentNullException.ThrowIfNull(rebarDiagram);
        if (pointsPerAxis < 2)
            throw new ArgumentOutOfRangeException(nameof(pointsPerAxis), "Нужно минимум 2 точки пробы на компонент (нуль исключён из пробы).");

        var zero = ShellStrainState.Zero;
        var baseTangent = section.ComputeTangent(zero, concreteDiagram, rebarDiagram, layerDiagrams, tensionOverride: true);

        var probeStates = new List<ShellStrainState>();
        for (int k = 1; k <= pointsPerAxis; k++)
        {
            double fractionEps = bounds.EpsGammaBoundAbs * k / pointsPerAxis;
            double fractionKappa = bounds.KappaBoundAbs * k / pointsPerAxis;
            probeStates.Add(new ShellStrainState(fractionEps, 0, 0, 0, 0, 0));
            probeStates.Add(new ShellStrainState(-fractionEps, 0, 0, 0, 0, 0));
            probeStates.Add(new ShellStrainState(0, fractionEps, 0, 0, 0, 0));
            probeStates.Add(new ShellStrainState(0, -fractionEps, 0, 0, 0, 0));
            probeStates.Add(new ShellStrainState(0, 0, fractionEps, 0, 0, 0));
            probeStates.Add(new ShellStrainState(0, 0, -fractionEps, 0, 0, 0));
            probeStates.Add(new ShellStrainState(0, 0, 0, fractionKappa, 0, 0));
            probeStates.Add(new ShellStrainState(0, 0, 0, -fractionKappa, 0, 0));
            probeStates.Add(new ShellStrainState(0, 0, 0, 0, fractionKappa, 0));
            probeStates.Add(new ShellStrainState(0, 0, 0, 0, -fractionKappa, 0));
            probeStates.Add(new ShellStrainState(0, 0, 0, 0, 0, fractionKappa));
            probeStates.Add(new ShellStrainState(0, 0, 0, 0, 0, -fractionKappa));
        }

        foreach (var probeState in probeStates)
        {
            var probe = section.ComputeTangent(probeState, concreteDiagram, rebarDiagram, layerDiagrams, tensionOverride: true);
            if (!PlateSectionResponseMath.CloseTangent(baseTangent, probe, relativeTolerance, absoluteTolerance))
            {
                return new(false, [new FemValidationDiagnostic(
                    "shell_mesh_patch_nonlinear_source",
                    "Материал не линеен в заявленном рабочем диапазоне состояния — RVE-адаптер не построен.")]);
            }
        }

        return new(true, []);
    }
}
