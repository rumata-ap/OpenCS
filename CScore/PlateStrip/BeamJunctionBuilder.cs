namespace CScore.PlateStrip;

/// <summary>Строит BeamJunction-проекции начала/конца полосы из уже сохранённого
/// EquivalentSection. Бросает ArgumentNullException при section == null;
/// InvalidOperationException — при повреждённом снимке (section.Strip либо
/// StartSupportLocus/EndSupportLocus равны null).</summary>
public static class BeamJunctionBuilder
{
    public static BeamJunction BuildStart(EquivalentSection section) =>
        Build(section, BeamJunctionEnd.Start);

    public static BeamJunction BuildEnd(EquivalentSection section) =>
        Build(section, BeamJunctionEnd.End);

    static BeamJunction Build(EquivalentSection section, BeamJunctionEnd end)
    {
        ArgumentNullException.ThrowIfNull(section);
        if (section.Strip == null)
            throw new InvalidOperationException("EquivalentSection.Strip не задан.");

        var locus = end == BeamJunctionEnd.Start
            ? section.Strip.StartSupportLocus
            : section.Strip.EndSupportLocus;
        if (locus == null)
            throw new InvalidOperationException(
                $"Геометрия полосы повреждена: " +
                $"{(end == BeamJunctionEnd.Start ? "StartSupportLocus" : "EndSupportLocus")} не задан.");

        return new BeamJunction
        {
            StripBeamId = section.Strip.Id,
            End = end,
            SupportLocus = locus
        };
    }
}
