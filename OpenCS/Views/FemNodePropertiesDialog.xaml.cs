using System.Windows;
using System.Windows.Controls;
using CScore.Fem;
using CScore.Fem.Editing;
using OpenCS.ViewModels;

namespace OpenCS.Views;

public partial class FemNodePropertiesDialog : Window
{
    readonly FemNode _node;
    readonly FemSchemaEditorVM _editorVm;
    bool _initializing = true;

    public event Action<string>? MemberSelected;

    public FemNodePropertiesDialog(FemNode node, FemSchemaEditorVM editorVm)
    {
        InitializeComponent();
        _node = node;
        _editorVm = editorVm;

        tagText.Text = node.NodeTag;

        var owningMembers = editorVm.Session.Members
            .Where(m => (System.Text.Json.JsonSerializer.Deserialize<int[]>(m.NodeIdsJson) ?? [])
                .Contains(int.Parse(node.NodeTag)))
            .Select(m => m.ElemTag)
            .ToList();
        membersList.ItemsSource = owningMembers;

        txCheck.IsChecked = (node.DofMask & 1)  != 0;
        tyCheck.IsChecked = (node.DofMask & 2)  != 0;
        tzCheck.IsChecked = (node.DofMask & 4)  != 0;
        rxCheck.IsChecked = (node.DofMask & 8)  != 0;
        ryCheck.IsChecked = (node.DofMask & 16) != 0;
        rzCheck.IsChecked = (node.DofMask & 32) != 0;

        loadCaseCombo.ItemsSource = editorVm.Session.LoadCases;
        loadCaseCombo.SelectedItem = editorVm.Session.LoadCases.FirstOrDefault();
        LoadLoadFields();

        _initializing = false;
    }

    void LoadLoadFields()
    {
        if (loadCaseCombo.SelectedItem is not FemLoadCase lc) return;
        var load = _editorVm.Session.NodeLoads.FirstOrDefault(l => l.LoadCaseId == lc.Id && l.NodeId == _node.Id);
        fxBox.Text = (load?.Fx ?? 0).ToString("F2");
        fyBox.Text = (load?.Fy ?? 0).ToString("F2");
        fzBox.Text = (load?.Fz ?? 0).ToString("F2");
        mxBox.Text = (load?.Mx ?? 0).ToString("F2");
        myBox.Text = (load?.My ?? 0).ToString("F2");
        mzBox.Text = (load?.Mz ?? 0).ToString("F2");
    }

    void LoadCaseCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => LoadLoadFields();

    void MembersList_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (membersList.SelectedItem is string tag) MemberSelected?.Invoke(tag);
    }

    void Dof_Changed(object sender, RoutedEventArgs e)
    {
        if (_initializing) return;
        int mask = 0;
        if (txCheck.IsChecked == true) mask |= 1;
        if (tyCheck.IsChecked == true) mask |= 2;
        if (tzCheck.IsChecked == true) mask |= 4;
        if (rxCheck.IsChecked == true) mask |= 8;
        if (ryCheck.IsChecked == true) mask |= 16;
        if (rzCheck.IsChecked == true) mask |= 32;
        _editorVm.Session.Execute(new SetDofMaskCommand(_node, mask));
        _editorVm.RefreshCollections();
    }

    void ApplyLoad_Click(object sender, RoutedEventArgs e)
    {
        if (loadCaseCombo.SelectedItem is not FemLoadCase lc) return;
        if (_node.Id == 0)
        {
            MessageBox.Show((string)Application.Current.FindResource("FemNodeLoadSkippedUnsavedTitle"));
            return;
        }
        if (!double.TryParse(fxBox.Text, out var fx)) fx = 0;
        if (!double.TryParse(fyBox.Text, out var fy)) fy = 0;
        if (!double.TryParse(fzBox.Text, out var fz)) fz = 0;
        if (!double.TryParse(mxBox.Text, out var mx)) mx = 0;
        if (!double.TryParse(myBox.Text, out var my)) my = 0;
        if (!double.TryParse(mzBox.Text, out var mz)) mz = 0;
        _editorVm.Session.Execute(new SetNodeLoadCommand(lc.Id, _node.Id, fx, fy, fz, mx, my, mz));
        _editorVm.RefreshCollections();
    }
}
