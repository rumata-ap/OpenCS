namespace CScore.Fem.Fragment;

public static partial class StoreyFragmentBuilder
{
    /// <summary>
    /// Группы родителя с сечением пластины → группы КонЭ фрагмента (<see cref="FemMemberGroup.OriginFragment"/>): КонЭ-пластина
    /// фрагмента входит в группу, если все КЭ родителя его исходного КонЭ в полосе входят в неё (группа КЭ — по номерам, группа
    /// КонЭ — по КонЭ КЭ). Группа КонЭ переживает перестройку сетки фрагмента, и сечение берёт и расчёт, и проверки по КЭ.
    /// КонЭ с частью КЭ в группе не включается — предупреждение; КонЭ со своим сечением пластины не трогается (оно главнее).
    /// Наборы усилий групп не переносятся (это результаты родителя).
    /// </summary>
    static void TransferGroups(Ctx c, StoreyFragmentParentSnapshot parent)
    {
        var groups = parent.Groups.Where(g => g.PlateSectionId != null).ToList();
        if (groups.Count == 0) return;
        var band = BandElements(c, parent).Where(e => e.ElemType == "shell").ToList();
        var byMember = band.Where(e => e.SourceMemberTag != null).GroupBy(e => e.SourceMemberTag!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(e => e.ElemTag).ToList(), StringComparer.Ordinal);
        var bandShells = band.Select(e => e.ElemTag).ToHashSet(StringComparer.Ordinal);
        IEnumerable<string> SourceOf(string tag) =>
            (c.SourceElements.TryGetValue(tag, out var own) ? own : byMember.GetValueOrDefault(tag) ?? []).Where(bandShells.Contains);
        var shells = c.Draft.Members.Where(m => m.Member.ElemType == "shell" && m.Member.PlateSectionId == null).ToList();

        var transferred = new List<string>();
        var partial = new List<string>();
        var partialKeys = new List<string>();
        foreach (var g in groups)
        {
            var inGroup = g.IsMeshGroup
                ? g.Tags.ToHashSet(StringComparer.Ordinal)
                : band.Where(e => e.SourceMemberTag != null && g.Tags.Contains(e.SourceMemberTag)).Select(e => e.ElemTag)
                    .ToHashSet(StringComparer.Ordinal);
            var members = new List<string>();
            foreach (var m in shells)
            {
                // Часть КонЭ родителя из группы КонЭ — в группе целиком, сетка не нужна.
                if (!g.IsMeshGroup && g.Tags.Contains(m.ParentMemberTag)) { members.Add(m.Member.ElemTag); continue; }
                var source = SourceOf(m.ParentMemberTag).ToList();
                if (source.Count == 0) continue;
                int hit = source.Count(inGroup.Contains);
                if (hit == source.Count) members.Add(m.Member.ElemTag);
                else if (hit > 0)
                {
                    partial.Add($"«{g.Tag}» — «{m.Member.ElemTag}» ({hit} из {source.Count} КЭ)");
                    partialKeys.Add(m.Member.ElemTag);
                }
            }
            if (members.Count == 0) continue;
            var copy = FemGroupComposition.NewMembersGroup(0, members, g.Tag, g.MemberType, FemMemberGroup.OriginFragment);
            copy.PlateSectionId = g.PlateSectionId;
            copy.DesignParamsJson = g.DesignParamsJson;
            c.Draft.Groups.Add(copy);
            transferred.Add($"«{g.Tag}» ({members.Count} КонЭ)");
        }
        if (transferred.Count > 0)
            c.Warn("fragment_groups_transferred", "Группы родителя с сечением пластины перенесены как группы КонЭ: " +
                string.Join(", ", transferred) + ".", [.. c.Draft.Groups.SelectMany(g => g.Tags).Distinct(StringComparer.Ordinal)]);
        if (partial.Count > 0)
            c.Warn("fragment_group_partial", "КонЭ фрагмента лишь частично в группе родителя с сечением пластины — в группу не " +
                $"включены, сечение назначьте сами: {string.Join("; ", partial.Take(10))}{(partial.Count > 10 ? " …" : "")}.",
                [.. partialKeys.Distinct(StringComparer.Ordinal)]);
    }
}
