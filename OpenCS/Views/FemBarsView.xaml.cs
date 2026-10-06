using System.Windows;
using System.Windows.Controls;
using CScore.Fem;
using OpenCS.ViewModels;

namespace OpenCS.Views;

public partial class FemBarsView : UserControl
{
    readonly FemBarsSubNode _node;
    readonly AppViewModel   _app;
    readonly FemGroupTableTools _groups;

    internal FemBarsView(FemBarsSubNode node, AppViewModel app)
    {
        _node = node;
        _app  = app;
        InitializeComponent();
        _groups = new FemGroupTableTools(app, node.Owner.Schema, barsGrid, FemMemberGroup.KindMembers,
            row => ((FemMember)row).ElemTag);
        liraForcesMenu.Visibility = node.Owner.Schema.SourceType == "lira" ? Visibility.Visible : Visibility.Collapsed;
        scadForcesMenu.Visibility = node.Owner.Schema.SourceType == "scad" ? Visibility.Visible : Visibility.Collapsed;
        Loaded += async (_, _) =>
        {
            var elems = await node.Owner.LoadBarsAsync();
            barsGrid.ItemsSource = elems;
        };
    }

    void CreateGroup_Click(object sender, RoutedEventArgs e) =>
        _groups.CreateGroup(AppViewModel.FemMembersGroupType(barsGrid.SelectedItems.OfType<FemMember>().ToList()));

    void AddToGroup_Click(object sender, RoutedEventArgs e) => _groups.ShowAddMenu((Button)sender);

    void RemoveFromGroup_Click(object sender, RoutedEventArgs e) => _groups.ShowRemoveMenu((Button)sender);

    void AutoGroup_Click(object sender, RoutedEventArgs e)
    {
        _app.AutoGroupFemMembersBySection(_node.Owner.Schema);
        _groups.Refresh();
    }

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
