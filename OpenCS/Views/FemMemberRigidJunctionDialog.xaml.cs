using System.Windows;
using System.Windows.Controls;
using CScore.Fem.Editing;
using OpenCS.Utilites;

namespace OpenCS.Views;

/// <summary>
/// «АЖТ примыкания…» стержневых КонЭ: режим (авто / не создавать), вид (жёсткое тело / объединение перемещений),
/// маска DOF и явный след со сбросом. Разные значения у выбранных КонЭ — неопределённое состояние: такое поле после
/// OK остаётся у каждого своим.
/// </summary>
public partial class FemMemberRigidJunctionDialog : Window
{
    /// <summary>«Не создавать» (true), «авто» (false), не менять (null). Задан после OK.</summary>
    public bool? Off { get; private set; }
    /// <summary>«Объединение перемещений» (true), «жёсткое тело» (false), не менять (null).</summary>
    public bool? Coupled { get; private set; }
    /// <summary>Шесть DOF X Y Z UX UY UZ: включить / снять / не менять (null).</summary>
    public bool?[] Dofs { get; private set; } = new bool?[6];
    /// <summary>Сбросить явный след — след снова по сечению.</summary>
    public bool ClearTrace { get; private set; }

    readonly CheckBox[] _dofBoxes;
    readonly int _traceCount, _memberCount;

    /// <param name="target">Что меняется: «Стержень С1», «Стержней: 3».</param>
    public FemMemberRigidJunctionDialog(string target, int memberCount, RigidJunctionParamsSummary summary)
    {
        InitializeComponent();
        targetText.Text = target;
        _memberCount = memberCount;
        _traceCount = summary.TraceCount;
        autoRadio.IsChecked = summary.Off == false;
        offRadio.IsChecked = summary.Off == true;
        rigidRadio.IsChecked = summary.Coupled == false;
        coupledRadio.IsChecked = summary.Coupled == true;
        _dofBoxes = [dofX, dofY, dofZ, dofUX, dofUY, dofUZ];
        for (int i = 0; i < 6; i++)
        {
            // Третье состояние — только там, где значения разные: снять его можно, вернуть — нет.
            _dofBoxes[i].IsThreeState = summary.Dofs[i] is null;
            _dofBoxes[i].IsChecked = summary.Dofs[i];
            _dofBoxes[i].Click += (s, _) => ((CheckBox)s).IsThreeState = false;
        }
        UpdateTraceText();
    }

    void UpdateTraceText()
    {
        resetTraceButton.Visibility = _traceCount > 0 && !ClearTrace ? Visibility.Visible : Visibility.Collapsed;
        traceText.Text = ClearTrace || _traceCount == 0
            ? Loc.S("FemMemberRigidJunctionTraceSection")
            : _traceCount == _memberCount
                ? Loc.S("FemMemberRigidJunctionTraceExplicit")
                : string.Format(Loc.S("FemMemberRigidJunctionTraceExplicitSome"), _traceCount, _memberCount);
    }

    void ResetTrace_Click(object sender, RoutedEventArgs e)
    {
        ClearTrace = true;
        UpdateTraceText();
    }

    void Apply_Click(object sender, RoutedEventArgs e)
    {
        var dofs = _dofBoxes.Select(b => b.IsChecked).ToArray();
        if (dofs.All(d => d == false))
        {
            MessageBox.Show(this, Loc.S("FemMemberRigidJunctionNoDofs"), Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        Off = offRadio.IsChecked == true ? true : autoRadio.IsChecked == true ? false : null;
        Coupled = coupledRadio.IsChecked == true ? true : rigidRadio.IsChecked == true ? false : null;
        Dofs = dofs;
        DialogResult = true;
    }
}
