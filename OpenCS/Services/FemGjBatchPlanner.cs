using CScore;
using CScore.Fem;
using CScore.Fem.Editing;

namespace OpenCS.Services;

/// <summary>Режим массового назначения крутильной жёсткости.</summary>
public enum FemGjBatchMode
{
    /// <summary>Назначить GJ только manual-стержням с незаданным значением.</summary>
    MissingOnly,

    /// <summary>Пересчитать GJ у manual-стержней с назначенным сечением.</summary>
    RecalculateManual
}

/// <summary>План массового изменения GJ и его диагностические счётчики.</summary>
public sealed record FemGjBatchPlan(
    IReadOnlyList<MemberGjAssignment> Assignments,
    int Fallback,
    int SkippedSaintVenant,
    int SkippedNoSection)
{
    /// <summary>Количество стержней, которые будут изменены.</summary>
    public int Assigned => Assignments.Count;
}

/// <summary>Строит assignments для массовых операций без изменения элементов.</summary>
public sealed class FemGjBatchPlanner
{
    readonly FemGjDefaultResolver _resolver;

    /// <summary>Создаёт планировщик с resolver-ом текущего FEM-редактора.</summary>
    public FemGjBatchPlanner(FemGjDefaultResolver resolver)
        => _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));

    /// <summary>Формирует атомарный план для переданного набора конструктивных элементов.</summary>
    public FemGjBatchPlan Build(
        IEnumerable<FemMember> members,
        IReadOnlyDictionary<int, CrossSection> sections,
        FemGjBatchMode mode)
    {
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(sections);
        if (mode is not FemGjBatchMode.MissingOnly and not FemGjBatchMode.RecalculateManual)
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "Неизвестный режим массового назначения GJ.");

        var assignments = new List<MemberGjAssignment>();
        int fallback = 0;
        int skippedSaintVenant = 0;
        int skippedNoSection = 0;

        foreach (var member in members)
        {
            if (member.ElemType != "beam") continue;
            if (member.GjStrategy == "saint_venant")
            {
                skippedSaintVenant++;
                continue;
            }
            if (member.GjStrategy != "manual") continue;

            CrossSection? section = null;
            if (member.CrossSectionId is { } sectionId)
                sections.TryGetValue(sectionId, out section);

            if (mode == FemGjBatchMode.RecalculateManual && section == null)
            {
                skippedNoSection++;
                continue;
            }

            if (mode == FemGjBatchMode.MissingOnly && IsValidGj(member.GjManualValue))
                continue;

            var resolution = _resolver.Resolve(section);
            assignments.Add(new MemberGjAssignment(member, "manual", resolution.GjNm2, null));
            if (UsesFallback(resolution)) fallback++;
        }

        return new FemGjBatchPlan(assignments, fallback, skippedSaintVenant, skippedNoSection);
    }

    static bool IsValidGj(double? value) => value is { } v && double.IsFinite(v) && v > 0;

    static bool UsesFallback(FemGjResolution resolution)
        => resolution.Source == FemGjValueSource.BuiltInFallback
           || (resolution.Source == FemGjValueSource.GlobalDefault
               && resolution.Diagnostic is not null
               && resolution.Diagnostic != "auto_section_disabled");
}
