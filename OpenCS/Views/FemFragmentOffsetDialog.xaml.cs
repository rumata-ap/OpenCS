using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using OpenCS.Utilites;

namespace OpenCS.Views;

/// <summary>
/// Смещение при вставке выделения схемы из буфера (Ctrl+V в редакторе): dX, dY, dZ (м) и число копий. Неверное значение —
/// ошибка под полем, окно не закрывается.
/// </summary>
public partial class FemFragmentOffsetDialog : Window
{
    /// <summary>Наибольшее число копий за одну вставку.</summary>
    public const int MaxCopies = 1000;

    public double Dx { get; private set; }
    public double Dy { get; private set; }
    public double Dz { get; private set; }
    /// <summary>Число копий, 1…<see cref="MaxCopies"/>.</summary>
    public int Copies { get; private set; } = 1;

    public FemFragmentOffsetDialog()
    {
        InitializeComponent();
        string zero = 0.0.ToString("G", CultureInfo.CurrentCulture);
        dxBox.Text = dyBox.Text = dzBox.Text = zero;
        copiesBox.Text = "1";
        Loaded += (_, _) => { dxBox.Focus(); dxBox.SelectAll(); };
        foreach (var (box, error) in new[] { (dxBox, dxError), (dyBox, dyError), (dzBox, dzError), (copiesBox, copiesError) })
            box.TextChanged += (_, _) => error.Text = "";
    }

    void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (TryRead(dxBox.Text, dyBox.Text, dzBox.Text, copiesBox.Text, out var r, out var errors) is false)
        {
            var boxes = new[] { (dxBox, dxError), (dyBox, dyError), (dzBox, dzError), (copiesBox, copiesError) };
            TextBox? first = null;
            for (int i = 0; i < boxes.Length; i++)
            {
                boxes[i].Item2.Text = errors[i] ?? "";
                if (errors[i] != null) first ??= boxes[i].Item1;
            }
            first?.Focus();
            first?.SelectAll();
            return;
        }
        (Dx, Dy, Dz, Copies) = r;
        DialogResult = true;
    }

    /// <summary>
    /// Разбор полей: смещения — числа с точкой или запятой, копии — целое 1…<see cref="MaxCopies"/>.
    /// <paramref name="errors"/> — по полю (dX, dY, dZ, копии), null — поле годится.
    /// </summary>
    public static bool TryRead(string dx, string dy, string dz, string copies,
        out (double Dx, double Dy, double Dz, int Copies) result, out string?[] errors)
    {
        errors = new string?[4];
        double[] v = new double[3];
        string[] texts = [dx, dy, dz];
        for (int i = 0; i < 3; i++)
            if (!Pars.ParseAny((texts[i] ?? "").Trim(), out v[i]) || !double.IsFinite(v[i]))
                errors[i] = Loc.S("FemPasteOffsetInvalid");
        if (!int.TryParse((copies ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)
            || n < 1 || n > MaxCopies)
            errors[3] = string.Format(Loc.S("FemPasteCopiesInvalid"), MaxCopies);
        result = (v[0], v[1], v[2], n);
        return errors.All(x => x == null);
    }
}
