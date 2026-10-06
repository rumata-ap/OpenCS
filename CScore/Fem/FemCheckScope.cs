using System.Globalization;
using System.Text.Json;

namespace CScore.Fem;

/// <summary>Конечный элемент сетки, входящий в цель проверки.</summary>
/// <param name="ElemNum">Номер КЭ во внешней схеме (числовой тег КЭ); null — тег не число.</param>
/// <param name="Element">КЭ сетки.</param>
/// <param name="Member">Конструктивный элемент, которому принадлежит КЭ; null — КЭ вне элементов.</param>
public sealed record FemCheckScopeElement(int? ElemNum, FemElement Element, FemMember? Member);

/// <summary>Состав цели проверки: её конструктивные элементы и КЭ сетки.</summary>
/// <param name="Members">Конструктивные элементы цели (у группы КЭ импортированной сетки — пусто).</param>
/// <param name="Elements">КЭ сетки цели.</param>
/// <param name="RefersToMeshElements">Цель — группа КЭ (<see cref="FemMemberGroup.KindMesh"/>):
/// состав задан номерами КЭ сетки, а не конструктивными элементами.</param>
public sealed record FemCheckScope(
    IReadOnlyList<FemMember> Members,
    IReadOnlyList<FemCheckScopeElement> Elements,
    bool RefersToMeshElements)
{
    /// <summary>Номера КЭ цели во внешней схеме (по возрастанию, без повторов).</summary>
    public IReadOnlyList<int> ElementNumbers =>
        Elements.Where(e => e.ElemNum.HasValue).Select(e => e.ElemNum!.Value).Distinct().Order().ToList();

    /// <summary>Состав одиночного конструктивного элемента: КЭ сетки с его <c>SourceMemberTag</c>.</summary>
    public static FemCheckScope ForMember(FemMember member, IEnumerable<FemElement> meshElements)
    {
        ArgumentNullException.ThrowIfNull(member);
        ArgumentNullException.ThrowIfNull(meshElements);
        var elements = meshElements
            .Where(e => string.Equals(e.SourceMemberTag, member.ElemTag, StringComparison.Ordinal))
            .Select(e => new FemCheckScopeElement(ParseNum(e.ElemTag), e, member))
            .ToList();
        return new FemCheckScope([member], elements, RefersToMeshElements: false);
    }

    /// <summary>
    /// Состав группы по её виду: у группы КонЭ — её конструктивные элементы и их КЭ сетки
    /// (<c>SourceMemberTag</c>); у группы КЭ — КЭ сетки с номерами из состава.
    /// </summary>
    public static FemCheckScope ForGroup(
        FemMemberGroup group, IEnumerable<FemMember> members, IEnumerable<FemElement> meshElements)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(meshElements);

        var tags = GroupTags(group);
        var memberByTag = new Dictionary<string, FemMember>(StringComparer.Ordinal);
        foreach (var m in members) memberByTag.TryAdd(m.ElemTag, m);

        if (!group.IsMeshGroup)
        {
            // Порядок — как в составе группы; теги без конструктивного элемента пропускаются.
            var groupMembers = group.Tags.Distinct(StringComparer.Ordinal)
                .Where(memberByTag.ContainsKey).Select(t => memberByTag[t]).ToList();
            var elements = meshElements
                .Where(e => e.SourceMemberTag != null && tags.Contains(e.SourceMemberTag) && memberByTag.ContainsKey(e.SourceMemberTag))
                .Select(e => new FemCheckScopeElement(ParseNum(e.ElemTag), e, memberByTag[e.SourceMemberTag!]))
                .ToList();
            return new FemCheckScope(groupMembers, elements, RefersToMeshElements: false);
        }

        var meshElementsOfGroup = meshElements
            .Where(e => tags.Contains(e.ElemTag))
            .Select(e => new FemCheckScopeElement(
                ParseNum(e.ElemTag), e,
                e.SourceMemberTag != null && memberByTag.TryGetValue(e.SourceMemberTag, out var owner) ? owner : null))
            .ToList();
        return new FemCheckScope([], meshElementsOfGroup, RefersToMeshElements: true);
    }

    /// <summary>Теги состава группы (номера КЭ или теги КонЭ — по виду группы).</summary>
    public static HashSet<string> GroupTags(FemMemberGroup group) =>
        group.Tags.ToHashSet(StringComparer.Ordinal);

    static int? ParseNum(string tag) =>
        int.TryParse(tag, NumberStyles.None, CultureInfo.InvariantCulture, out int n) ? n : null;
}
