using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CScore.Fem;
using OpenCS.ViewModels;

namespace OpenCS.Views;

public partial class FemShellsView : UserControl
{
    readonly FemShellsSubNode _node;
    readonly AppViewModel     _app;
    readonly FemGroupTableTools _groups;

    internal FemShellsView(FemShellsSubNode node, AppViewModel app)
    {
        _node = node;
        _app  = app;
        InitializeComponent();
        // Родные КонЭ — имя назначенного сечения, импортные — тег жёсткости источника.
        sectionColumn.Binding = new System.Windows.Data.Binding { Converter = new Converters.FemSectionLabelConverter(app.db.CrossSections, app.PlateSections) };
        _groups = new FemGroupTableTools(app, node.Owner.Schema, shellsGrid, FemMemberGroup.KindMembers,
            row => ((FemMember)row).ElemTag);
        // Меню — только импорт усилий из программы-источника схемы; у прочих схем его нет вовсе.
        string source = node.Owner.Schema.SourceType;
        liraForcesMenu.Visibility = source == "lira" ? Visibility.Visible : Visibility.Collapsed;
        scadForcesMenu.Visibility = source == "scad" ? Visibility.Visible : Visibility.Collapsed;
        if (source is not ("lira" or "scad")) shellsGrid.ContextMenu = null;
        Loaded += async (_, _) =>
        {
            var elems = await node.Owner.LoadShellsAsync();
            shellsGrid.ItemsSource = elems;
        };
    }

    void CreateGroup_Click(object sender, RoutedEventArgs e) =>
        _groups.CreateGroup(AppViewModel.FemMembersGroupType(shellsGrid.SelectedItems.OfType<FemMember>().ToList()));

    void AddToGroup_Click(object sender, RoutedEventArgs e) => _groups.ShowAddMenu((Button)sender);

    void RemoveFromGroup_Click(object sender, RoutedEventArgs e) => _groups.ShowRemoveMenu((Button)sender);

    async void ShellsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (shellsGrid.SelectedItem is not FemMember member || member.PlanarRegionId is null) return;
        _app.EditFemPlanarMember(_node.Owner.Schema, member);

        var elems = await _node.Owner.LoadShellsAsync();
        shellsGrid.ItemsSource = elems;
    }

    /// <summary>Импорт усилий ЛИРЫ на выбранный конструктивный элемент (по КЭ сетки, привязанным к нему).</summary>
    void ImportLiraForces_Click(object sender, RoutedEventArgs e)
    {
        if (shellsGrid.SelectedItem is FemMember member && sender is MenuItem item)
            _app.ImportLiraForcesCommand(item.Tag as string).Execute(member);
    }

    /// <summary>Импорт усилий SCAD (.SPR) на выбранный конструктивный элемент.</summary>
    void ImportScadForces_Click(object sender, RoutedEventArgs e)
    {
        if (shellsGrid.SelectedItem is FemMember member && sender is MenuItem item)
            _app.ImportScadForcesCommand(item.Tag as string).Execute(member);
    }

    async void DeleteShell_Click(object sender, RoutedEventArgs e)
    {
        if (shellsGrid.SelectedItem is not FemMember member || member.PlanarRegionId is null) return;
        if (!_app.DeleteFemPlanarMember(_node.Owner.Schema, member)) return;

        var elems = await _node.Owner.LoadShellsAsync();
        shellsGrid.ItemsSource = elems;
    }
}
