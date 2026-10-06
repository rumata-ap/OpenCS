using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Media3D;
using CScore.Fem;
using OpenCS.Utilites;
using OpenCS.ViewModels;

namespace OpenCS.Views;

/// <summary>
/// Страница узла, выбранного в дереве схемы: координаты, закрепления, примыкающие КонЭ и узловые нагрузки;
/// в 3D — узел с примыкающими КонЭ отдельно или на схеме. Правка — через редактор схемы.
/// </summary>
public partial class FemNodePage : UserControl
{
    readonly FemNodeTreeItem _item;
    readonly AppViewModel _app;

    internal FemNodePage(FemNodeTreeItem item, AppViewModel app)
    {
        _item = item;
        _app = app;
        InitializeComponent();
        var adjacent = item.AdjacentMembers();
        DataContext = new FemNodePageVM(item, adjacent, app.db);

        var node = item.Node;
        _marker = new Point3D(node.X, node.Y, node.Z);
        if (adjacent.Count > 0)
        {
            _adjacent = new FemMemberGroup
            {
                SchemaId = item.Schema.Id, Tag = node.NodeTag, Kind = FemMemberGroup.KindMembers,
            };
            _adjacent.SetTags(adjacent.Select(i => i.Member.ElemTag));
        }
        // Как у групп: сначала узел с примыкающими КонЭ отдельно; свободный узел — сразу на схеме.
        rbIsolated.IsEnabled = _adjacent != null;
        (_adjacent != null ? rbIsolated : rbSchema).IsChecked = true;
    }

    /// <summary>Примыкающие КонЭ как временная группа для 3D-вида; null — узел ни к одному КонЭ не относится.</summary>
    readonly FemMemberGroup? _adjacent;
    readonly Point3D _marker;

    void ViewMode_Changed(object sender, RoutedEventArgs e)
    {
        var db = _app.db;
        view3D.DataContext = _adjacent == null
            ? new Fem3DVM(_item.Schema, db) { MarkerPoint = _marker }
            : rbIsolated.IsChecked == true
                ? new Fem3DVM(_adjacent, db) { MarkerPoint = _marker }
                : new Fem3DVM(_adjacent, db, highlightOnSchema: true) { MarkerPoint = _marker };
    }

    void Properties_Click(object sender, RoutedEventArgs e) =>
        _app.OpenFemNodeInEditor(_item.Schema, _item.Node.NodeTag);
}

/// <summary>Сводка узла для <see cref="FemNodePage"/>.</summary>
public class FemNodePageVM
{
    public string Title { get; }
    public IReadOnlyList<FemMemberPageVM.Row> Rows { get; }

    internal FemNodePageVM(FemNodeTreeItem item, IReadOnlyList<FemMemberTreeItem> adjacent, DatabaseService db)
    {
        var node = item.Node;
        var culture = CultureInfo.CurrentCulture;
        Title = string.Format(Loc.S("FemNodePageTitle"), node.NodeTag);
        string supports = FemNodeTreeItem.SupportsText(node.DofMask);
        var rows = new List<FemMemberPageVM.Row>
        {
            new("X, м", node.X.ToString("0.####", culture)),
            new("Y, м", node.Y.ToString("0.####", culture)),
            new("Z, м", node.Z.ToString("0.####", culture)),
            new(Loc.S("FemNodePageSupports"), supports.Length > 0 ? supports : Loc.S("FemNodePageFree")),
            new(Loc.S("FemNodePageMembers"), adjacent.Count > 0
                ? string.Join(", ", adjacent.Select(i => i.Member.ElemTag)) : "—"),
        };

        var cases = item.Schema.LoadCases.ToDictionary(c => c.Id, c => c.Tag);
        foreach (var load in db.GetFemNodeLoads(item.Schema.Id).Where(l => l.NodeId == node.Id))
        {
            var parts = new List<string>();
            void Add(string name, double value, string unit)
            {
                if (value != 0) parts.Add($"{name} = {value.ToString("0.##", culture)} {unit}");
            }
            Add("Fx", FemUnitConverter.NewtonsToKiloNewtons(load.Fx), "кН");
            Add("Fy", FemUnitConverter.NewtonsToKiloNewtons(load.Fy), "кН");
            Add("Fz", FemUnitConverter.NewtonsToKiloNewtons(load.Fz), "кН");
            Add("Mx", FemUnitConverter.NewtonMetersToKiloNewtonMeters(load.Mx), "кН·м");
            Add("My", FemUnitConverter.NewtonMetersToKiloNewtonMeters(load.My), "кН·м");
            Add("Mz", FemUnitConverter.NewtonMetersToKiloNewtonMeters(load.Mz), "кН·м");
            if (parts.Count == 0) continue;
            string caseTag = cases.TryGetValue(load.LoadCaseId, out var t) ? t : $"#{load.LoadCaseId}";
            rows.Add(new(caseTag, string.Join("; ", parts)));
        }
        Rows = rows;
    }
}
