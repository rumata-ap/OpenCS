using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using CScore.Fem;
using OpenCS.Utilites;
using OpenCS.ViewModels;

namespace OpenCS.Views;

/// <summary>
/// Страница конструктивного элемента, выбранного в дереве схемы: сводка и его КЭ в 3D —
/// так же, как у группы (отдельно или подсвеченными на схеме).
/// </summary>
public partial class FemMemberPage : UserControl
{
    readonly FemMemberGroup _viewGroup;
    readonly DatabaseService _db;

    internal FemMemberPage(FemMemberTreeItem item, AppViewModel app)
    {
        _db = app.db;
        InitializeComponent();
        var scope = _db.GetFemCheckScope(item.Member);
        _viewGroup = ViewGroup(item.Member, scope);
        DataContext = new FemMemberPageVM(item, app, scope);
        // Как у групп: сначала элемент отдельно, «На схеме» — переключателем.
        rbIsolated.IsChecked = true;
    }

    /// <summary>
    /// Временная группа для 3D-вида. У элемента с импортированной сеткой (кБ ЛИРЫ, КонЭ из выбранных КЭ) —
    /// группа КЭ из его КЭ сетки: видны сами КЭ импорта. У элемента с сеткой дискретизации — группа КонЭ
    /// из него одного (КЭ дискретизации 3D-вид группы не загружает).
    /// </summary>
    static FemMemberGroup ViewGroup(FemMember member, FemCheckScope scope)
    {
        var group = new FemMemberGroup { SchemaId = member.SchemaId, Tag = member.ElemTag };
        if (member.IsMeshLocked && scope.Elements.Count > 0)
        {
            group.Kind = FemMemberGroup.KindMesh;
            group.SetTags(scope.Elements.Select(e => e.Element.ElemTag));
        }
        else
        {
            group.Kind = FemMemberGroup.KindMembers;
            group.SetTags([member.ElemTag]);
        }
        return group;
    }

    void ViewMode_Changed(object sender, RoutedEventArgs e)
    {
        view3D.DataContext = rbIsolated.IsChecked == true
            ? new Fem3DVM(_viewGroup, _db)
            : new Fem3DVM(_viewGroup, _db, highlightOnSchema: true);
    }
}

/// <summary>Сводка конструктивного элемента для <see cref="FemMemberPage"/>.</summary>
public class FemMemberPageVM
{
    public record Row(string Label, string Value);

    public string Tag { get; }
    public IReadOnlyList<Row> Rows { get; }

    internal FemMemberPageVM(FemMemberTreeItem item, AppViewModel app, FemCheckScope scope)
    {
        var m = item.Member;
        Tag = m.ElemTag;
        var rows = new List<Row>
        {
            new(Loc.S("FemMemberType"), item.IsShell
                ? Converters.FemMemberTypeOption.NameOf(item.TypeCode)
                : Loc.S("FemMemberPageBar")),
        };

        string? section = item.IsShell
            ? app.PlateSections.FirstOrDefault(s => s.Id == m.PlateSectionId)?.Tag
            : app.db.CrossSections.FirstOrDefault(s => s.Id == m.CrossSectionId)?.Tag ?? m.SectionTag;
        rows.Add(new(Loc.S("CalcTaskSection"), string.IsNullOrEmpty(section) ? "—" : section));
        if (item.IsShell && m.ThicknessM is double t)
            rows.Add(new(Loc.S("FemMemberPageThickness"), (t * 1000).ToString("0.#", CultureInfo.CurrentCulture)));

        rows.Add(new(Loc.S("FemMemberPageMesh"), m.IsMeshLocked
            ? string.Format(Loc.S("FemMemberPageMeshImported"), scope.Elements.Count)
            : string.Format(Loc.S("FemMemberPageMeshGenerated"), scope.Elements.Count)));
        if (scope.ElementNumbers.Count > 0)
            rows.Add(new(Loc.S("FemMemberPageElements"), LiraElemRangeDialog.FormatRange(
                scope.ElementNumbers.Select(n => n.ToString(CultureInfo.InvariantCulture)).ToList())));

        var groups = item.Schema.MemberGroups
            .Where(g => !g.IsMeshGroup && g.Tags.Contains(m.ElemTag))
            .Select(g => g.Tag).ToList();
        rows.Add(new(Loc.S("FemGroupsColumn"), groups.Count > 0 ? string.Join(", ", groups) : "—"));
        rows.Add(new(Loc.S("FemMemberPageChecks"), item.Checks.Count.ToString(CultureInfo.CurrentCulture)));
        Rows = rows;
    }
}
