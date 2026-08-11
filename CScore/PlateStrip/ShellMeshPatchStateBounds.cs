namespace CScore.PlateStrip;

/// <summary>Границы состояния, в пределах которых RVE-адаптер (Срез 3b) считается линейным —
/// проверяются на КАЖДЫЙ вызов Forces()/Tangent(), не только при построении адаптера (см. спеку,
/// раздел «Per-call bounds enforcement»). Раздельные границы, т.к. деформации/сдвиг безразмерны,
/// а кривизны — в 1/м.</summary>
public readonly record struct ShellMeshPatchStateBounds
{
    public double EpsGammaBoundAbs { get; }
    public double KappaBoundAbs { get; }

    public ShellMeshPatchStateBounds(double EpsGammaBoundAbs, double KappaBoundAbs)
    {
        if (!(EpsGammaBoundAbs > 0.0) || !double.IsFinite(EpsGammaBoundAbs))
            throw new ArgumentOutOfRangeException(nameof(EpsGammaBoundAbs), "Граница деформаций/сдвига должна быть конечной и положительной.");
        if (!(KappaBoundAbs > 0.0) || !double.IsFinite(KappaBoundAbs))
            throw new ArgumentOutOfRangeException(nameof(KappaBoundAbs), "Граница кривизн должна быть конечной и положительной.");
        this.EpsGammaBoundAbs = EpsGammaBoundAbs;
        this.KappaBoundAbs = KappaBoundAbs;
    }

    /// <summary>Бросает ArgumentOutOfRangeException, если state выходит за границы.</summary>
    public void Validate(ShellStrainState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        CheckAbs(state.Eps0x, EpsGammaBoundAbs, nameof(state.Eps0x));
        CheckAbs(state.Eps0y, EpsGammaBoundAbs, nameof(state.Eps0y));
        CheckAbs(state.Gamma0xy, EpsGammaBoundAbs, nameof(state.Gamma0xy));
        CheckAbs(state.Kx, KappaBoundAbs, nameof(state.Kx));
        CheckAbs(state.Ky, KappaBoundAbs, nameof(state.Ky));
        CheckAbs(state.Kxy, KappaBoundAbs, nameof(state.Kxy));
    }

    static void CheckAbs(double value, double bound, string name)
    {
        if (!double.IsFinite(value) || Math.Abs(value) > bound)
            throw new ArgumentOutOfRangeException(name,
                $"Компонента {name}={value:G6} выходит за заявленную границу линейности ±{bound:G6}.");
    }
}
