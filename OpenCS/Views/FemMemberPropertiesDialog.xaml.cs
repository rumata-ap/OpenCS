using System.Windows;
using CScore;
using CScore.Fem;
using CScore.Fem.Editing;
using OpenCS.ViewModels;

namespace OpenCS.Views;

public partial class FemMemberPropertiesDialog : Window
{
    readonly FemMember _member;
    readonly FemSchemaEditorVM _editorVm;
    bool _initializing = true;

    public FemMemberPropertiesDialog(FemMember member, FemSchemaEditorVM editorVm)
    {
        InitializeComponent();
        _member = member;
        _editorVm = editorVm;

        tagText.Text    = member.ElemTag;
        typeText.Text   = member.ElemType;
        var ids = System.Text.Json.JsonSerializer.Deserialize<int[]>(member.NodeIdsJson) ?? [];
        nodesText.Text  = ids.Length == 2 ? $"{ids[0]} → {ids[1]}" : "-";

        var n1 = editorVm.Session.Nodes.FirstOrDefault(n => n.NodeTag == (ids.Length > 0 ? ids[0].ToString() : null));
        var n2 = editorVm.Session.Nodes.FirstOrDefault(n => n.NodeTag == (ids.Length > 1 ? ids[1].ToString() : null));
        if (n1 != null && n2 != null)
        {
            double dx = n2.X - n1.X, dy = n2.Y - n1.Y, dz = n2.Z - n1.Z;
            lengthText.Text = Math.Sqrt(dx * dx + dy * dy + dz * dz).ToString("F3");
        }

        sectionCombo.ItemsSource = editorVm.CrossSections;
        sectionCombo.SelectedItem = editorVm.CrossSections.FirstOrDefault(s => s.Id == member.CrossSectionId);

        gjManualRadio.IsChecked = member.GjStrategy != "saint_venant";
        gjSaintVenantRadio.IsChecked = member.GjStrategy == "saint_venant";
        gjManualValueBox.Text = member.GjManualValue?.ToString("F1") ?? "";

        torsionTaskCombo.ItemsSource = editorVm.AllCalcTasks
            .Where(t => (t.Kind is "torsion_bem" or "torsion_fem") && t.SectionId == member.CrossSectionId);
        torsionTaskCombo.SelectedItem = editorVm.AllCalcTasks.FirstOrDefault(t => t.Id == member.GjTorsionTaskId);

        targetLengthBox.Text = member.TargetMeshLengthM?.ToString("F2") ?? "";
        _initializing = false;
    }

    void SectionCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_initializing) return;
        var section = sectionCombo.SelectedItem as CrossSection;
        _editorVm.Session.Execute(new SetMemberSectionCommand(_member, section?.Id));
        _editorVm.RefreshCollections();
    }

    void GjStrategy_Changed(object sender, RoutedEventArgs e)
    {
        if (_initializing) return;
        if (gjSaintVenantRadio.IsChecked == true)
            _editorVm.Session.Execute(new SetMemberGjCommand(_member, "saint_venant", null, _member.GjTorsionTaskId));
        else
            _editorVm.Session.Execute(new SetMemberGjCommand(_member, "manual", _member.GjManualValue ?? 0, null));
        _editorVm.RefreshCollections();
    }

    void GjManualValue_LostFocus(object sender, RoutedEventArgs e)
    {
        if (!double.TryParse(gjManualValueBox.Text, out var value)) return;
        _editorVm.Session.Execute(new SetMemberGjCommand(_member, "manual", value, null));
        _editorVm.RefreshCollections();
    }

    void TorsionTask_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_initializing) return;
        var task = torsionTaskCombo.SelectedItem as CalcTask;
        _editorVm.Session.Execute(new SetMemberGjCommand(_member, "saint_venant", null, task?.Id));
        _editorVm.RefreshCollections();
    }

    void TargetLength_LostFocus(object sender, RoutedEventArgs e)
    {
        _member.TargetMeshLengthM = double.TryParse(targetLengthBox.Text, out var v) ? v : null;
    }
}
