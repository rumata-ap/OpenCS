using System.Windows;
using System.Windows.Controls;
using CScore.Fem;
using OpenCS.ViewModels;

namespace OpenCS.Views;

public partial class FemBarsView : UserControl
{
    readonly FemBarsSubNode _node;
    readonly AppViewModel   _app;

    internal FemBarsView(FemBarsSubNode node, AppViewModel app)
    {
        _node = node;
        _app  = app;
        InitializeComponent();
        liraForcesMenu.Visibility = node.Owner.Schema.SourceType == "lira" ? Visibility.Visible : Visibility.Collapsed;
        scadForcesMenu.Visibility = node.Owner.Schema.SourceType == "scad" ? Visibility.Visible : Visibility.Collapsed;
        Loaded += async (_, _) =>
        {
            var elems = await node.Owner.LoadBarsAsync();
            barsGrid.ItemsSource = elems;
        };
    }

    void NewMember_Click(object sender, RoutedEventArgs e)
    {
        var selected = barsGrid.SelectedItems.OfType<FemMember>().ToList();
        var initialRange = selected.Count > 0
            ? string.Join(" ", selected.Select(el => el.ElemTag))
            : "";
        var dlg = new FemMemberDialog(initialRange);
        if (dlg.ShowDialog() != true) return;
        var ids = LiraElemRangeDialog.ParseRange(dlg.Range);
        if (ids.Count == 0) return;
        _app.CreateFemMemberFromRange(_node.Owner.Schema, ids, dlg.MemberTag, dlg.MemberType);
    }

    void CreateGroup_Click(object sender, RoutedEventArgs e)
    {
        var selected = barsGrid.SelectedItems.OfType<FemMember>().ToList();
        if (selected.Count == 0) return;
        _app.CreateFemMemberFromSelection(_node.Owner.Schema, selected);
    }

    void AutoGroup_Click(object sender, RoutedEventArgs e)
        => _app.AutoGroupFemMembersBySection(_node.Owner.Schema);

    /// <summary>Эпюры усилий и подобранной арматуры выбранного конструктивного элемента.</summary>
    void ShowDiagrams_Click(object sender, RoutedEventArgs e)
    {
        if (barsGrid.SelectedItem is FemMember member)
            _app.ShowBarDiagrams(member);
    }

    /// <summary>Импорт усилий ЛИРЫ на выбранный конструктивный элемент (по КЭ сетки, привязанным к нему).</summary>
    void ImportLiraForces_Click(object sender, RoutedEventArgs e)
    {
        if (barsGrid.SelectedItem is FemMember member && sender is MenuItem item)
            _app.ImportLiraForcesCommand(item.Tag as string).Execute(member);
    }

    /// <summary>Импорт усилий SCAD (.SPR) на выбранный конструктивный элемент.</summary>
    void ImportScadForces_Click(object sender, RoutedEventArgs e)
    {
        if (barsGrid.SelectedItem is FemMember member && sender is MenuItem item)
            _app.ImportScadForcesCommand(item.Tag as string).Execute(member);
    }
}
