using System.Globalization;
using System.Windows;
using CScore.Fem;
using OpenCS.OpenSees.CScore;
using OpenCS.Utilites;

namespace OpenCS.Views;

/// <summary>Диалог параметров DisplacementControl/ArcLength для одной стадии (прямой
/// режим) или её continuation. Форма зависит от переданного FemAnalysisDialog.StageRow —
/// открывается либо на редактирование прямого режима (row.PathControlMode), либо только
/// блока continuation (когда row.PathControlMode == LoadControl).</summary>
public partial class FemPathControlDialog : Window
{
    readonly FemAnalysisDialog.StageRow _row;
    readonly IReadOnlyList<FemNode> _nodes;
    readonly bool _editingContinuation;

    sealed record DofOption(int Value, string Label);
    static readonly List<DofOption> DofOptions =
    [
        new(1, Loc.S("FemKinematicLoadUx")), new(2, Loc.S("FemKinematicLoadUy")), new(3, Loc.S("FemKinematicLoadUz")),
        new(4, Loc.S("FemKinematicLoadRx")), new(5, Loc.S("FemKinematicLoadRy")), new(6, Loc.S("FemKinematicLoadRz")),
    ];

    internal FemPathControlDialog(FemAnalysisDialog.StageRow row, IReadOnlyList<FemNode> nodes)
    {
        InitializeComponent();
        _row = row;
        _nodes = nodes;
        Title = $"{row.Tag}: {row.PathControlMode.Label}";

        ControlNodeBox.ItemsSource = nodes; MonitorNodeBox.ItemsSource = nodes;
        ControlDofBox.ItemsSource = DofOptions; ControlDofBox.DisplayMemberPath = nameof(DofOption.Label);
        MonitorDofBox.ItemsSource = DofOptions; MonitorDofBox.DisplayMemberPath = nameof(DofOption.Label);

        _editingContinuation = row.PathControlMode.Value == "LoadControl";
        ContinuationPanel.Visibility = _editingContinuation ? Visibility.Visible : Visibility.Collapsed;
        DisplacementGroup.Visibility = !_editingContinuation && row.PathControlMode.Value == "DisplacementControl" ? Visibility.Visible : Visibility.Collapsed;
        ArcLengthGroup.Visibility = !_editingContinuation && row.PathControlMode.Value == "ArcLength" ? Visibility.Visible : Visibility.Collapsed;

        if (_editingContinuation)
        {
            ContinuationModeBox.ItemsSource = new[]
            {
                new { Value = "DisplacementControl", Label = Loc.S("FemPathControlModeDisplacementControl") },
                new { Value = "ArcLength", Label = Loc.S("FemPathControlModeArcLength") },
            };
            ContinuationModeBox.DisplayMemberPath = "Label";
            ContinuationEnabledCb.IsChecked = row.ContinueWithMode != null;
            ContinuationModeBox.SelectedIndex = row.ContinueWithMode?.Value == "ArcLength" ? 1 : 0;
            ContinuationEnabledCb.Checked += (_, _) => UpdateContinuationVisibility();
            ContinuationEnabledCb.Unchecked += (_, _) => UpdateContinuationVisibility();
            ContinuationModeBox.SelectionChanged += (_, _) => UpdateContinuationVisibility();
            UpdateContinuationVisibility();
            LoadFields(row.ContinueWithDisplacementControl, row.ContinueWithArcLength);
        }
        else
        {
            LoadFields(row.DisplacementControl, row.ArcLength);
        }
    }

    void UpdateContinuationVisibility()
    {
        bool enabled = ContinuationEnabledCb.IsChecked == true;
        bool isDisp = ContinuationModeBox.SelectedIndex == 0;
        DisplacementGroup.Visibility = enabled && isDisp ? Visibility.Visible : Visibility.Collapsed;
        ArcLengthGroup.Visibility = enabled && !isDisp ? Visibility.Visible : Visibility.Collapsed;
    }

    void LoadFields(FemDisplacementControlInput? dc, FemArcLengthInput? al)
    {
        if (dc != null)
        {
            ControlNodeBox.SelectedValue = dc.ControlNodeId; ControlDofBox.SelectedItem = DofOptions.FirstOrDefault(d => d.Value == dc.ControlDof);
            InitialIncrementBox.Text = dc.InitialIncrement.ToString("G15", CultureInfo.CurrentCulture);
            MinIncrementBox.Text = dc.MinIncrement.ToString("G15", CultureInfo.CurrentCulture);
            MaxIncrementBox.Text = dc.MaxIncrement.ToString("G15", CultureInfo.CurrentCulture);
            TargetDisplacementBox.Text = dc.TargetDisplacement.ToString("G15", CultureInfo.CurrentCulture);
            DisplacementMaxStepsBox.Text = dc.MaxSteps.ToString(CultureInfo.CurrentCulture);
        }
        if (al != null)
        {
            ArcLengthSBox.Text = al.S.ToString("G15", CultureInfo.CurrentCulture);
            ArcLengthAlphaBox.Text = al.Alpha.ToString("G15", CultureInfo.CurrentCulture);
            ArcLengthMinSBox.Text = al.MinS.ToString("G15", CultureInfo.CurrentCulture);
            ArcLengthMaxStepsBox.Text = al.MaxSteps.ToString(CultureInfo.CurrentCulture);
            MonitorNodeBox.SelectedValue = al.MonitorNodeId; MonitorDofBox.SelectedItem = DofOptions.FirstOrDefault(d => d.Value == al.MonitorDof);
        }
    }

    void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (_editingContinuation)
        {
            if (ContinuationEnabledCb.IsChecked != true)
            {
                _row.ContinueWithMode = null; _row.ContinueWithDisplacementControl = null; _row.ContinueWithArcLength = null;
                DialogResult = true; return;
            }
            bool isDisp = ContinuationModeBox.SelectedIndex == 0;
            if (isDisp)
            {
                if (!TryReadDisplacementControl(out var dc)) return;
                _row.ContinueWithMode = new("DisplacementControl", Loc.S("FemPathControlModeDisplacementControl"));
                _row.ContinueWithDisplacementControl = dc; _row.ContinueWithArcLength = null;
            }
            else
            {
                if (!TryReadArcLength(out var al)) return;
                _row.ContinueWithMode = new("ArcLength", Loc.S("FemPathControlModeArcLength"));
                _row.ContinueWithArcLength = al; _row.ContinueWithDisplacementControl = null;
            }
        }
        else if (_row.PathControlMode.Value == "DisplacementControl")
        {
            if (!TryReadDisplacementControl(out var dc)) return;
            _row.DisplacementControl = dc;
        }
        else if (_row.PathControlMode.Value == "ArcLength")
        {
            if (!TryReadArcLength(out var al)) return;
            _row.ArcLength = al;
        }
        DialogResult = true;
    }

    bool TryReadDisplacementControl(out FemDisplacementControlInput? result)
    {
        result = null;
        if (ControlNodeBox.SelectedValue is not int nodeId || ControlDofBox.SelectedItem is not DofOption dof ||
            !Pars.ParseAny(InitialIncrementBox.Text, out var init) || !double.IsFinite(init) ||
            !Pars.ParseAny(MinIncrementBox.Text, out var min) || !double.IsFinite(min) ||
            !Pars.ParseAny(MaxIncrementBox.Text, out var max) || !double.IsFinite(max) ||
            !Pars.ParseAny(TargetDisplacementBox.Text, out var target) || !double.IsFinite(target) ||
            !int.TryParse(DisplacementMaxStepsBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxSteps) || maxSteps <= 0)
        {
            MessageBox.Show(Loc.S("FemPathControlInvalidFields"), Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        result = new FemDisplacementControlInput(nodeId, dof.Value, init, min, max, target, maxSteps);
        return true;
    }

    bool TryReadArcLength(out FemArcLengthInput? result)
    {
        result = null;
        if (!Pars.ParseAny(ArcLengthSBox.Text, out var s) || !double.IsFinite(s) ||
            !Pars.ParseAny(ArcLengthAlphaBox.Text, out var alpha) || !double.IsFinite(alpha) ||
            !Pars.ParseAny(ArcLengthMinSBox.Text, out var minS) || !double.IsFinite(minS) ||
            !int.TryParse(ArcLengthMaxStepsBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxSteps) || maxSteps <= 0 ||
            MonitorNodeBox.SelectedValue is not int nodeId || MonitorDofBox.SelectedItem is not DofOption dof)
        {
            MessageBox.Show(Loc.S("FemPathControlInvalidFields"), Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        result = new FemArcLengthInput(s, alpha, minS, maxSteps, nodeId, dof.Value);
        return true;
    }

    void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
