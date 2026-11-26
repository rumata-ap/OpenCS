using System.Globalization;
using System.Windows;
using CScore.Fem;
using OpenCS.OpenSees.CScore;
using OpenCS.Tasks;
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

    /// <summary>Выбор в списке узла или направления; не выбран — сообщение и фокус на списке.</summary>
    bool Picked(System.Windows.Controls.ComboBox box, bool ok, string key)
    {
        if (ok) return true;
        MessageBox.Show(this, string.Format(Loc.S(key), _row.Tag), Title, MessageBoxButton.OK, MessageBoxImage.Warning);
        box.Focus();
        return false;
    }

    /// <summary>
    /// Правила службы постановок (те же, что у <c>apply</c>): шаги, цель, дуга. Первая ошибка — сообщением с фокусом на
    /// поле, к которому она относится.
    /// </summary>
    bool CheckRules(FemAnalysisPathControl dto, IReadOnlyDictionary<string, System.Windows.Controls.TextBox> boxes)
    {
        var errors = new List<Services.FieldError>();
        Services.FemAnalysisAuthoring.PathControl(dto, "p", _row.Tag, errors, null);
        if (errors.Count == 0) return true;
        var first = errors[0];
        string field = first.Path["p.".Length..];
        if (boxes.TryGetValue(field, out var box))
        {
            box.BorderBrush = System.Windows.Media.Brushes.Red;
            box.Focus();
            box.SelectAll();
        }
        MessageBox.Show(this, string.Join(Environment.NewLine, errors.Select(x => x.Message)), Title,
            MessageBoxButton.OK, MessageBoxImage.Warning);
        return false;
    }

    bool TryReadDisplacementControl(out FemDisplacementControlInput? result)
    {
        result = null;
        Helpers.FieldCheck.Clear(InitialIncrementBox, MinIncrementBox, MaxIncrementBox, TargetDisplacementBox, DisplacementMaxStepsBox);
        if (!Picked(ControlNodeBox, ControlNodeBox.SelectedValue is int, "FemPathControlNodeMissing")
            || !Picked(ControlDofBox, ControlDofBox.SelectedItem is DofOption, "FemPathControlDofInvalid")
            || !Helpers.FieldCheck.Read(this, InitialIncrementBox, "FemPathControlInitialIncrementLabel", out var init, Helpers.FieldCheck.Positive)
            || !Helpers.FieldCheck.Read(this, MinIncrementBox, "FemPathControlMinIncrementLabel", out var min, Helpers.FieldCheck.Positive)
            || !Helpers.FieldCheck.Read(this, MaxIncrementBox, "FemPathControlMaxIncrementLabel", out var max, Helpers.FieldCheck.Positive)
            || !Helpers.FieldCheck.Read(this, TargetDisplacementBox, "FemPathControlTargetDisplacementLabel", out var target, Helpers.FieldCheck.NonZero)
            || !Helpers.FieldCheck.ReadInt(this, DisplacementMaxStepsBox, "FemPathControlMaxStepsLabel", out var maxSteps,
                v => v >= 1 ? null : Loc.S("FieldErrPositive")))
            return false;
        int nodeId = (int)ControlNodeBox.SelectedValue!;
        var dof = (DofOption)ControlDofBox.SelectedItem!;
        var dto = new FemAnalysisPathControl
        {
            Mode = "DisplacementControl", ControlNodeId = nodeId, ControlDof = dof.Value, InitialIncrement = init,
            MinIncrement = min, MaxIncrement = max, TargetDisplacement = target, MaxSteps = maxSteps,
        };
        if (!CheckRules(dto, new Dictionary<string, System.Windows.Controls.TextBox>
            { ["initial"] = InitialIncrementBox, ["target"] = TargetDisplacementBox, ["maxSteps"] = DisplacementMaxStepsBox }))
            return false;
        result = new FemDisplacementControlInput(nodeId, dof.Value, init, min, max, target, maxSteps);
        return true;
    }

    bool TryReadArcLength(out FemArcLengthInput? result)
    {
        result = null;
        Helpers.FieldCheck.Clear(ArcLengthSBox, ArcLengthAlphaBox, ArcLengthMinSBox, ArcLengthMaxStepsBox);
        if (!Helpers.FieldCheck.Read(this, ArcLengthSBox, "FemPathControlArcLengthSLabel", out var s, Helpers.FieldCheck.Positive)
            || !Helpers.FieldCheck.Read(this, ArcLengthAlphaBox, "FemPathControlArcLengthAlphaLabel", out var alpha, Helpers.FieldCheck.NonNegative)
            || !Helpers.FieldCheck.Read(this, ArcLengthMinSBox, "FemPathControlArcLengthMinSLabel", out var minS, Helpers.FieldCheck.Positive)
            || !Helpers.FieldCheck.ReadInt(this, ArcLengthMaxStepsBox, "FemPathControlMaxStepsLabel", out var maxSteps,
                v => v >= 1 ? null : Loc.S("FieldErrPositive"))
            || !Picked(MonitorNodeBox, MonitorNodeBox.SelectedValue is int, "FemPathControlNodeMissing")
            || !Picked(MonitorDofBox, MonitorDofBox.SelectedItem is DofOption, "FemPathControlDofInvalid"))
            return false;
        int nodeId = (int)MonitorNodeBox.SelectedValue!;
        var dof = (DofOption)MonitorDofBox.SelectedItem!;
        var dto = new FemAnalysisPathControl
        {
            Mode = "ArcLength", ArcLengthS = s, ArcLengthAlpha = alpha, ArcLengthMinS = minS, MaxSteps = maxSteps,
            MonitorNodeId = nodeId, MonitorDof = dof.Value,
        };
        if (!CheckRules(dto, new Dictionary<string, System.Windows.Controls.TextBox>
            { ["s"] = ArcLengthMinSBox, ["alpha"] = ArcLengthAlphaBox, ["maxSteps"] = ArcLengthMaxStepsBox }))
            return false;
        result = new FemArcLengthInput(s, alpha, minS, maxSteps, nodeId, dof.Value);
        return true;
    }
}
