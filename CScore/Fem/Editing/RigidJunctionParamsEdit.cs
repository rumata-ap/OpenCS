namespace CScore.Fem.Editing;

/// <summary>
/// Параметры АЖТ примыкания для набора стержневых КонЭ, как их показывает диалог: общее значение или «разные» (null).
/// </summary>
/// <param name="Off">Режим «не создавать» у всех (true), «авто» у всех (false), разный — null.</param>
/// <param name="Coupled">Вид «объединение перемещений» у всех (true), «жёсткое тело» у всех (false), разный — null.</param>
/// <param name="Dofs">Шесть DOF (X Y Z UX UY UZ): включён у всех / выключен у всех / разный (null).</param>
/// <param name="TraceCount">Сколько КонЭ имеют явный след.</param>
public sealed record RigidJunctionParamsSummary(bool? Off, bool? Coupled, bool?[] Dofs, int TraceCount)
{
    /// <summary>Все шесть DOF — маска по умолчанию (null в <see cref="FemMember.RigidJunctionMask"/>).</summary>
    public const int AllDofs = 63;

    public static RigidJunctionParamsSummary Of(IReadOnlyList<FemMember> members)
    {
        static bool? Common(IEnumerable<bool> values)
        {
            var distinct = values.Distinct().ToList();
            return distinct.Count == 1 ? distinct[0] : null;
        }

        var dofs = new bool?[6];
        for (int bit = 0; bit < 6; bit++)
        {
            int b = bit;
            dofs[b] = Common(members.Select(m => ((m.RigidJunctionMask ?? AllDofs) & (1 << b)) != 0));
        }
        return new RigidJunctionParamsSummary(
            Common(members.Select(m => m.RigidJunctionMode == FemMember.RigidJunctionOff)),
            Common(members.Select(m => m.RigidJunctionKind == FemMember.RigidJunctionCoupled)),
            dofs,
            members.Count(m => m.RigidJunctionTraceJson != null));
    }

    /// <summary>
    /// Команда правки одной отменой: заданные значения ставятся всем КонЭ, неопределённые (null) остаются у каждого
    /// свои — в том числе отдельные биты маски. <paramref name="clearTrace"/> — сбросить явный след. Null — менять нечего.
    /// </summary>
    public static IFemEditCommand? BuildCommand(IReadOnlyList<FemMember> members, bool? off, bool? coupled,
        IReadOnlyList<bool?> dofs, bool clearTrace)
    {
        var commands = new List<IFemEditCommand>();
        var groups = members
            .Select(m => (Member: m, Target: Target(m, off, coupled, dofs)))
            .Where(x => x.Target != (x.Member.RigidJunctionMode, x.Member.RigidJunctionMask, x.Member.RigidJunctionKind))
            .GroupBy(x => x.Target);
        foreach (var g in groups)
            commands.Add(new SetRigidJunctionParamsCommand(g.Select(x => x.Member).ToList(), g.Key.Mode, g.Key.Mask, g.Key.Kind));

        var traced = members.Where(m => m.RigidJunctionTraceJson != null).ToList();
        if (clearTrace && traced.Count > 0) commands.Add(new ClearRigidJunctionTraceCommand(traced));

        return commands.Count == 0 ? null : new CompositeFemCommand(commands);
    }

    static (string? Mode, int? Mask, string? Kind) Target(FemMember m, bool? off, bool? coupled, IReadOnlyList<bool?> dofs)
    {
        string? mode = off switch
        {
            true => FemMember.RigidJunctionOff,
            false => null,
            null => m.RigidJunctionMode,
        };
        string? kind = coupled switch
        {
            true => FemMember.RigidJunctionCoupled,
            false => null,
            null => m.RigidJunctionKind,
        };
        int mask = m.RigidJunctionMask ?? AllDofs;
        for (int bit = 0; bit < 6; bit++)
        {
            if (dofs[bit] == true) mask |= 1 << bit;
            else if (dofs[bit] == false) mask &= ~(1 << bit);
        }
        return (mode, mask == AllDofs ? null : mask, kind);
    }
}
