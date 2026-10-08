using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using CScore.Fem;
using OpenCS.Utilites;
using OpenCS.ViewModels;

namespace OpenCS.Views;

/// <summary>
/// ГУ узла сетки: закрепление по шести DOF и пружины (кН/м, кН·м/рад). Результат — <see cref="Mask"/> и
/// <see cref="Stiffnesses"/> (Н/м, Н·м/рад); запись в БД — у вызывающего.
/// </summary>
public partial class FemMeshNodeBoundaryDialog : Window
{
    readonly CheckBox[] _checks;
    readonly TextBox[] _boxes;

    public int Mask { get; private set; }
    public double[] Stiffnesses { get; private set; } = new double[6];

    public FemMeshNodeBoundaryDialog(FemMeshNodeRow row)
    {
        InitializeComponent();
        _checks = [xCheck, yCheck, zCheck, uxCheck, uyCheck, uzCheck];
        _boxes = [kxBox, kyBox, kzBox, kuxBox, kuyBox, kuzBox];

        headerText.Text = string.Format(CultureInfo.CurrentCulture, Loc.S("FemMeshNodeBoundaryHeader"),
            row.NodeTag, row.X, row.Y, row.Z);
        originText.Text = row.Origins.Count > 0 ? string.Format(Loc.S("FemMeshNodeBoundaryOrigin"), row.OriginText) : "";
        importHint.Visibility = row.Origins.Any(o => o != FemLoadOrigin.Manual) ? Visibility.Visible : Visibility.Collapsed;
        for (int i = 0; i < 6; i++)
        {
            _checks[i].IsChecked = (row.SupportMask & (1 << i)) != 0;
            _boxes[i].Text = (row.Stiffnesses[i] / 1e3).ToString("0.######", CultureInfo.CurrentCulture);
        }
    }

    void Ok_Click(object sender, RoutedEventArgs e)
    {
        int mask = 0;
        var k = new double[6];
        for (int i = 0; i < 6; i++)
        {
            if (_checks[i].IsChecked == true) mask |= 1 << i;
            string text = _boxes[i].Text.Trim();
            if (text.Length == 0) continue;
            bool parsed = double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out double v) ||
                double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
            if (!parsed || !double.IsFinite(v) || v < 0)
            {
                ShowError(string.Format(Loc.S("FemMeshNodeBoundaryBadStiffness"), _checks[i].Content));
                _boxes[i].Focus();
                return;
            }
            k[i] = v * 1e3;
        }
        var conflict = Enumerable.Range(0, 6).Where(i => (mask & (1 << i)) != 0 && k[i] != 0).ToList();
        if (conflict.Count > 0)
        {
            ShowError(string.Format(Loc.S("FemMeshNodeBoundarySpringInFixed"),
                FemBoundaryDofs.Describe(conflict.Aggregate(0, (m, i) => m | 1 << i))));
            return;
        }
        Mask = mask;
        Stiffnesses = k;
        DialogResult = true;
    }

    void ShowError(string message)
    {
        errorText.Text = message;
        errorText.Visibility = Visibility.Visible;
    }
}
