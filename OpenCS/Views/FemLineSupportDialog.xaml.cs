using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using CScore.Fem;
using OpenCS.Utilites;

namespace OpenCS.Views;

/// <summary>
/// Линейная связь на гранях пластин: закрепления по шести DOF в общих осях, поворот кромки и погонные пружины
/// (кН/м², кН·м/рад/м). Результат — <see cref="Result"/> (Н/м², Н·м/рад/м) — шаблон для команды редактора.
/// </summary>
public partial class FemLineSupportDialog : Window
{
    readonly CheckBox[] _checks;
    readonly TextBox[] _boxes;

    /// <summary>Параметры связи (без грани).</summary>
    public FemLineSupport Result { get; private set; } = new();

    /// <param name="header">Что правится: выбранные грани или тип связи из дерева.</param>
    /// <param name="edgesText">Перечень граней (КонЭ, сторона) и их общая длина.</param>
    /// <param name="current">Текущие параметры; null — шарнирное опирание пластины.</param>
    public FemLineSupportDialog(string header, string edgesText, FemLineSupport? current)
    {
        InitializeComponent();
        _checks = [xCheck, yCheck, zCheck, uxCheck, uyCheck, uzCheck];
        _boxes = [kxBox, kyBox, kzBox, kuxBox, kuyBox, kuzBox];
        headerText.Text = header;
        this.edgesText.Text = edgesText;
        Show(current ?? new FemLineSupport { Mask = 0b111, FixEdgeRotation = true });
    }

    void Show(FemLineSupport s)
    {
        var k = s.Stiffnesses;
        for (int i = 0; i < 6; i++)
        {
            _checks[i].IsChecked = (s.Mask & (1 << i)) != 0;
            _boxes[i].Text = k[i] == 0 ? "" : (k[i] / 1e3).ToString("0.######", CultureInfo.CurrentCulture);
        }
        edgeRotationCheck.IsChecked = s.FixEdgeRotation;
        errorText.Visibility = Visibility.Collapsed;
    }

    // Край плиты / низ стены: X Y Z и поворот кромки — жёсткое шарнирное опирание пластины Миндлина.
    void PresetPlate_Click(object sender, RoutedEventArgs e) => Show(new FemLineSupport { Mask = 0b111, FixEdgeRotation = true });
    void PresetHinge_Click(object sender, RoutedEventArgs e) => Show(new FemLineSupport { Mask = 0b111 });
    void PresetFixed_Click(object sender, RoutedEventArgs e) => Show(new FemLineSupport { Mask = FemBoundaryDofs.All });
    void PresetNone_Click(object sender, RoutedEventArgs e) => Show(new FemLineSupport());

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
        var result = new FemLineSupport { Mask = mask, FixEdgeRotation = edgeRotationCheck.IsChecked == true };
        result.SetStiffnesses(k);
        Result = result;
        DialogResult = true;
    }

    void ShowError(string message)
    {
        errorText.Text = message;
        errorText.Visibility = Visibility.Visible;
    }
}
