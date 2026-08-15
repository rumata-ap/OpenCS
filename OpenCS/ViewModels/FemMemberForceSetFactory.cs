using CScore;
using CScore.Fem;

namespace OpenCS.ViewModels;

/// <summary>Выбранные пользователем имя, описание и строки preview.</summary>
public sealed record FemMemberForceSetSelection(
    string Tag,
    string? Description,
    IReadOnlyList<FemMemberForceSetPreviewRow> Rows);

/// <summary>Создаёт сохраняемый ForceSet из подтверждённого preview.</summary>
public static class FemMemberForceSetFactory
{
    /// <summary>Создаёт стержневой набор усилий OpenSees с новой нумерацией.</summary>
    public static ForceSet Create(
        FemSchema schema,
        FemMember member,
        FemMemberForceSetPreview preview,
        FemMemberForceSetSelection selection,
        IReadOnlyCollection<ForceSet> existing)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(member);
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(existing);

        string tag = selection.Tag.Trim();
        if (tag.Length == 0)
            throw new ArgumentException("ForceSet tag is required.", nameof(selection));

        int nextNum = existing.Count == 0 ? 1 : existing.Max(item => item.Num) + 1;
        var orderedRows = selection.Rows
            .OrderBy(row => row.PositionS)
            .ToList();

        return new ForceSet
        {
            Num = nextNum,
            Tag = tag,
            Description = string.IsNullOrWhiteSpace(selection.Description)
                ? null
                : selection.Description.Trim(),
            Kind = "bar",
            SourceType = "fea",
            SourceSchemaId = schema.Id,
            SourceMemberId = member.Id,
            SourceElementTag = member.ElemTag,
            Items = orderedRows
                .Select((row, index) => row.ToLoadItem(index + 1))
                .ToList()
        };
    }
}
