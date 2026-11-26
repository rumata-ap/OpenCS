using System.Globalization;
using System.Windows;
using CScore.Fem;
using OpenCS.Services;
using OpenCS.Utilites;
using OpenCS.ViewModels;

namespace OpenCS.Views;

/// <summary>
/// ГУ узла (узлов) — закрепления по шести направлениям общих осей и пружины (кН/м, кН·м/рад). Несколько узлов с
/// разными значениями — «разные» поля (<see cref="FemBoundaryDofEditor"/>): нетронутые остаются у каждого своими,
/// итог узла — <see cref="Apply"/>. Запись — у вызывающего.
/// </summary>
public partial class FemMeshNodeBoundaryDialog : Window
{
    bool?[] _fix = new bool?[6];
    double?[] _k = new double?[6];

    /// <summary>Маска закреплений — для одного узла или набора с общими значениями (без «разных»).</summary>
    public int Mask => Apply(0, new double[6]).Mask;

    /// <summary>Жёсткости X…UZ, Н/м и Н·м/рад — для одного узла или набора с общими значениями.</summary>
    public double[] Stiffnesses => Apply(0, new double[6]).K;

    public FemMeshNodeBoundaryDialog(FemMeshNodeRow row)
        : this(string.Format(CultureInfo.CurrentCulture, Loc.S("FemMeshNodeBoundaryHeader"), row.NodeTag, row.X, row.Y, row.Z),
            row.Origins.Count > 0 ? row.OriginText : null, row.Origins.Any(o => o != FemLoadOrigin.Manual),
            row.SupportMask, row.Stiffnesses)
    {
    }

    /// <summary>Одни значения у узла или типа узловой связи.</summary>
    /// <param name="stiffnesses">Текущие жёсткости X…UZ, Н/м и Н·м/рад.</param>
    public FemMeshNodeBoundaryDialog(string header, string? origin, bool imported, int mask, IReadOnlyList<double> stiffnesses)
        : this(header, origin, imported,
            Enumerable.Range(0, 6).Select(i => (bool?)((mask & (1 << i)) != 0)).ToArray(),
            stiffnesses.Select(k => (double?)k).ToArray())
    {
    }

    /// <summary>Значения набора узлов: null — «разные» (см. <see cref="FemBoundaryAuthoring.Common"/>).</summary>
    public FemMeshNodeBoundaryDialog(string header, string? origin, bool imported, IReadOnlyList<bool?> fix,
        IReadOnlyList<double?> stiffnesses)
    {
        InitializeComponent();
        headerText.Text = header;
        originText.Text = origin != null ? string.Format(Loc.S("FemMeshNodeBoundaryOrigin"), origin) : "";
        originText.Visibility = origin != null ? Visibility.Visible : Visibility.Collapsed;
        importHint.Visibility = imported ? Visibility.Visible : Visibility.Collapsed;
        editor.SetUnits(perMetre: false);
        editor.Show(fix, stiffnesses);
    }

    /// <summary>Итог для узла с прежними маской и жёсткостями (Н/м, Н·м/рад).</summary>
    public (int Mask, double[] K) Apply(int oldMask, IReadOnlyList<double> oldK) =>
        FemBoundaryAuthoring.Apply(oldMask, oldK, _fix, _k);

    void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (!editor.TryRead(out _fix, out _k)) return;
        DialogResult = true;
    }
}
