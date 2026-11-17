using CScore.Fem.Fragment.Masks;
using CScore.Submodel;

namespace CScore.Sp63Punching.Fem;

/// <summary>
/// Результат по сочетаниям — линейная суперпозиция загружений исходного результата (импорт ЛИРА-САПР/SCAD, линейный
/// расчёт): силы концов стержней и усилия пластин сочетания — сумма загружений с коэффициентами. Ключи — ключи
/// сочетаний (<see cref="FragmentMaskKey.OfStageTerms"/>) и, при необходимости, сами загружения.
/// </summary>
public sealed class PunchingFemCombinedResult(IFragmentParentResult inner, IReadOnlyList<FragmentMaskKey> keys) : IFragmentParentResult
{
    public IReadOnlyList<FragmentMaskKey> Keys => keys;

    /// <summary>Тот же исходный результат с другими ключами.</summary>
    public PunchingFemCombinedResult WithKeys(IReadOnlyList<FragmentMaskKey> other) => new(inner, other);

    public bool KnowsBeamLoads => inner.KnowsBeamLoads;

    public bool TryNodeDisplacement(FragmentMaskKey key, string meshNodeTag, out Dof6 displacement)
    {
        displacement = Dof6.Zero;
        foreach (var (lc, k) in key.Terms())
        {
            if (!inner.TryNodeDisplacement(FragmentMaskKey.OfLoadCase(lc), meshNodeTag, out var d)) return false;
            displacement += d.Scale(k);
        }
        return true;
    }

    public bool TryBeamEndForces(FragmentMaskKey key, string elemTag, out BeamEndForces forces)
    {
        var i = Dof6.Zero;
        var j = Dof6.Zero;
        forces = new BeamEndForces(i, j);
        foreach (var (lc, k) in key.Terms())
        {
            if (!inner.TryBeamEndForces(FragmentMaskKey.OfLoadCase(lc), elemTag, out var f)) return false;
            i += f.I.Scale(k);
            j += f.J.Scale(k);
        }
        forces = new BeamEndForces(i, j);
        return true;
    }

    public bool TryShellForces(FragmentMaskKey key, string elemTag, out FragmentShellForces forces)
    {
        forces = null!;
        double[]? sum = null;
        FragmentShellForces? first = null;
        foreach (var (lc, k) in key.Terms())
        {
            if (!inner.TryShellForces(FragmentMaskKey.OfLoadCase(lc), elemTag, out var f)) return false;
            first ??= f;
            sum ??= new double[f.Values.Length];
            for (int c = 0; c < sum.Length && c < f.Values.Length; c++) sum[c] += k * f.Values[c];
        }
        if (first == null || sum == null) return false;
        forces = new FragmentShellForces(first.AxisX, first.AxisY, sum);
        return true;
    }

    public IReadOnlyList<FragmentBeamSpanLoad> BeamLoads(FragmentMaskKey key, string elemTag) => [];
}
