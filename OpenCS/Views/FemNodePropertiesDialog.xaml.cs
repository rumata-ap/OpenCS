using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using CScore.Fem;
using CScore.Fem.Editing;
using OpenCS.Utilites;
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

        // NodeIdsJson хранит теги узлов (числа); тег узла — строка, сравнение по строке (нечисловой тег не роняет окно).
        var owningMembers = editorVm.Session.Members
            .Where(m => (System.Text.Json.JsonSerializer.Deserialize<int[]>(m.NodeIdsJson) ?? [])
                .Any(id => id.ToString(CultureInfo.InvariantCulture) == node.NodeTag))
            .Select(m => m.ElemTag)
            .ToList();
        membersList.ItemsSource = owningMembers;

        ShowBoundary();

        loadCaseCombo.ItemsSource = editorVm.Session.LoadCases;
        loadCaseCombo.SelectedItem = editorVm.Session.LoadCases.FirstOrDefault();
        LoadLoadFields();

        _initializing = false;
    }

    /// <summary>Закрепления и пружины узла — в флажки и строку пружин.</summary>
    void ShowBoundary()
    {
        bool initializing = _initializing;
        _initializing = true;
        txCheck.IsChecked = (_node.DofMask & 1)  != 0;
        tyCheck.IsChecked = (_node.DofMask & 2)  != 0;
        tzCheck.IsChecked = (_node.DofMask & 4)  != 0;
        rxCheck.IsChecked = (_node.DofMask & 8)  != 0;
        ryCheck.IsChecked = (_node.DofMask & 16) != 0;
        rzCheck.IsChecked = (_node.DofMask & 32) != 0;
        _initializing = initializing;
        var k = Stiffnesses();
        string[] names = ["Kx", "Ky", "Kz", "Kux", "Kuy", "Kuz"];
        var parts = Enumerable.Range(0, 6).Where(i => k[i] != 0)
            .Select(i => string.Format(CultureInfo.CurrentCulture, "{0} = {1:0.###}", names[i], k[i] / 1e3)).ToList();
        springsText.Text = parts.Count == 0 ? Loc.S("FemNodePropsNoSprings")
            : string.Format(Loc.S("FemNodePropsSprings"), string.Join("; ", parts));
    }

    /// <summary>Жёсткости пружин узла в сессии, Н/м и Н·м/рад (сумма записей узла).</summary>
    double[] Stiffnesses()
    {
        var k = new double[6];
        foreach (var spring in _editorVm.Session.NodeSprings.Where(s => s.NodeTag == _node.NodeTag))
            for (int i = 0; i < 6; i++) k[i] += spring.Stiffnesses[i];
        return k;
    }

    void Boundary_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new FemMeshNodeBoundaryDialog(
            string.Format(CultureInfo.CurrentCulture, Loc.S("FemMeshNodeBoundaryHeader"), _node.NodeTag, _node.X, _node.Y, _node.Z),
            null, imported: false, _node.DofMask & FemBoundaryDofs.All, Stiffnesses(), allowSprings: true)
        { Owner = this };
        if (dialog.ShowDialog() != true) return;
        _editorVm.Session.Execute(new SetNodeBoundaryCommand([_node], dialog.Mask, dialog.Stiffnesses));
        _editorVm.RefreshCollections();
        ShowBoundary();
    }

    void LoadLoadFields()
    {
        if (loadCaseCombo.SelectedItem is not FemLoadCase lc) return;
        var load = _editorVm.Session.NodeLoads.FirstOrDefault(l => l.LoadCaseId == lc.Id && l.NodeId == _node.Id);
        fxBox.Text = FemUnitConverter.NewtonsToKiloNewtons(load?.Fx ?? 0).ToString("F2", CultureInfo.CurrentCulture);
        fyBox.Text = FemUnitConverter.NewtonsToKiloNewtons(load?.Fy ?? 0).ToString("F2", CultureInfo.CurrentCulture);
        fzBox.Text = FemUnitConverter.NewtonsToKiloNewtons(load?.Fz ?? 0).ToString("F2", CultureInfo.CurrentCulture);
        mxBox.Text = FemUnitConverter.NewtonMetersToKiloNewtonMeters(load?.Mx ?? 0).ToString("F2", CultureInfo.CurrentCulture);
        myBox.Text = FemUnitConverter.NewtonMetersToKiloNewtonMeters(load?.My ?? 0).ToString("F2", CultureInfo.CurrentCulture);
        mzBox.Text = FemUnitConverter.NewtonMetersToKiloNewtonMeters(load?.Mz ?? 0).ToString("F2", CultureInfo.CurrentCulture);
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
        // Нераспознанное значение — ошибка у поля, а не молчаливый 0 (нагрузка пропадала без сообщения).
        Helpers.FieldCheck.Clear(fxBox, fyBox, fzBox, mxBox, myBox, mzBox);
        if (!Helpers.FieldCheck.Read(this, fxBox, "FemNodePropsFx", out var fxKilo)
            || !Helpers.FieldCheck.Read(this, fyBox, "FemNodePropsFy", out var fyKilo)
            || !Helpers.FieldCheck.Read(this, fzBox, "FemNodePropsFz", out var fzKilo)
            || !Helpers.FieldCheck.Read(this, mxBox, "FemNodePropsMx", out var mxKilo)
            || !Helpers.FieldCheck.Read(this, myBox, "FemNodePropsMy", out var myKilo)
            || !Helpers.FieldCheck.Read(this, mzBox, "FemNodePropsMz", out var mzKilo)) return;
        var fx = FemUnitConverter.KiloNewtonsToNewtons(fxKilo);
        var fy = FemUnitConverter.KiloNewtonsToNewtons(fyKilo);
        var fz = FemUnitConverter.KiloNewtonsToNewtons(fzKilo);
        var mx = FemUnitConverter.KiloNewtonMetersToNewtonMeters(mxKilo);
        var my = FemUnitConverter.KiloNewtonMetersToNewtonMeters(myKilo);
        var mz = FemUnitConverter.KiloNewtonMetersToNewtonMeters(mzKilo);
        _editorVm.Session.Execute(new SetNodeLoadCommand(lc.Id, _node.Id, fx, fy, fz, mx, my, mz));
        _editorVm.RefreshCollections();
    }

    void Close_Click(object sender, RoutedEventArgs e) => Close();
}
