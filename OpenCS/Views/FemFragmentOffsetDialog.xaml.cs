using System.Windows;
using OpenCS.Utilites;

namespace OpenCS.Views;

/// <summary>Смещение фрагмента схемы: вставка из буфера, «Переместить…» и «Копировать…» (с числом копий).</summary>
public partial class FemFragmentOffsetDialog : Window
{
    public double Dx { get; private set; }
    public double Dy { get; private set; }
    public double Dz { get; private set; }
    /// <summary>Число копий (≥ 1); у перемещения и вставки — 1.</summary>
    public int Copies { get; private set; } = 1;

    public FemFragmentOffsetDialog() => InitializeComponent();

    /// <param name="titleKey">Ключ строки заголовка.</param>
    /// <param name="summary">Что смещается («Узлов: 2, КонЭ: 1»); null — строки нет.</param>
    /// <param name="showCopies">Показать поле «Число копий».</param>
    public FemFragmentOffsetDialog(string titleKey, string? summary, bool showCopies) : this()
    {
        Title = Loc.S(titleKey);
        if (summary != null)
        {
            summaryText.Text = summary;
            summaryText.Visibility = Visibility.Visible;
        }
        if (showCopies) copiesPanel.Visibility = Visibility.Visible;
    }

    void Ok_Click(object sender, RoutedEventArgs e)
    {
        double Parse(System.Windows.Controls.TextBox box) => Pars.ParseAny(box.Text, out var v) ? v : 0;
        Dx = Parse(dxBox); Dy = Parse(dyBox); Dz = Parse(dzBox);
        Copies = int.TryParse(copiesBox.Text, out int n) ? Math.Clamp(n, 1, 1000) : 1;
        DialogResult = true;
    }
}
