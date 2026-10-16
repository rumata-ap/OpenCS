using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using CScore.Fem;
using OpenCS.Utilites;

namespace OpenCS.Views;

/// <summary>
/// Параметры загружения: имя, вид по СП 20, группа взаимоисключения, γf, ψ1/ψ2 (пустое поле — по умолчанию),
/// собственный вес. Результат — <see cref="Result"/>; проверка имени и запись — у вызывающего (<see cref="Validate"/>).
/// </summary>
public partial class FemLoadCaseDialog : Window
{
    /// <summary>Новые значения загружения (без Id и схемы).</summary>
    public FemLoadCase Result { get; private set; } = new();

    /// <summary>Проверка у вызывающего (имя занято и т. п.): текст ошибки или null — закрыть.</summary>
    public Func<FemLoadCase, string?>? Validate { get; init; }

    public FemLoadCaseDialog(FemLoadCase loadCase, string? origin)
    {
        InitializeComponent();
        tagBox.Text = loadCase.Tag;
        typeCombo.SelectedValue = loadCase.Sp20Type;
        if (typeCombo.SelectedIndex < 0) typeCombo.SelectedValue = "short_term";
        groupBox.Text = loadCase.Sp20Group ?? "";
        gammaUnfavBox.Text = Format(loadCase.GammaFUnfav);
        gammaFavBox.Text = Format(loadCase.GammaFFav);
        // ψ1/ψ2 в диалоге не правятся: коэффициенты сочетаний СП 20 назначаются по рангу нагрузки (п. 6.3–6.5).
        _psi = (loadCase.Psi1, loadCase.Psi2);
        _longTermShare = loadCase.LongTermShare;
        selfWeightCheck.IsChecked = loadCase.SelfWeightFactor is { } k && k != 0;
        selfWeightBox.Text = Format(loadCase.SelfWeightFactor ?? 1);
        selfWeightBox.IsEnabled = selfWeightCheck.IsChecked == true;
        if (origin != null)
        {
            originText.Text = string.Format(Loc.S("FemLoadCaseDialogOrigin"), origin);
            originText.Visibility = Visibility.Visible;
        }
        Loaded += (_, _) => { tagBox.Focus(); tagBox.SelectAll(); };
    }

    readonly (double? Psi1, double? Psi2) _psi;
    readonly double? _longTermShare;

    static string Format(double? value) => value?.ToString("0.####", CultureInfo.CurrentCulture) ?? "";

    void SelfWeight_Changed(object sender, RoutedEventArgs e) => selfWeightBox.IsEnabled = selfWeightCheck.IsChecked == true;

    void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(tagBox.Text)) { ShowError(Loc.S("FemLoadCaseTagTaken"), tagBox); return; }
        if (!TryParse(gammaUnfavBox, out var gu) || !TryParse(gammaFavBox, out var gf)) return;
        double? selfWeight = null;
        if (selfWeightCheck.IsChecked == true)
        {
            if (!TryParse(selfWeightBox, out selfWeight)) return;
            if (selfWeight is not { } k || k == 0) { ShowError(Loc.S("FemSelfWeightFactorInvalid"), selfWeightBox); return; }
        }
        var result = new FemLoadCase
        {
            Tag = tagBox.Text.Trim(),
            Sp20Type = typeCombo.SelectedValue as string ?? "short_term",
            Sp20Group = string.IsNullOrWhiteSpace(groupBox.Text) ? null : groupBox.Text.Trim(),
            GammaFUnfav = gu, GammaFFav = gf, Psi1 = _psi.Psi1, Psi2 = _psi.Psi2,
            SelfWeightFactor = selfWeight, LongTermShare = _longTermShare,
        };
        if (Validate?.Invoke(result) is { } error) { ShowError(error, tagBox); return; }
        Result = result;
        DialogResult = true;
    }

    /// <summary>Пустое поле — null (по умолчанию), иначе неотрицательное число.</summary>
    bool TryParse(TextBox box, out double? value)
    {
        value = null;
        string text = box.Text.Trim();
        if (text.Length == 0) return true;
        if (Pars.ParseAny(text, out double v) && double.IsFinite(v) && v >= 0) { value = v; return true; }
        ShowError(Loc.S("FemLoadCaseDialogBadNumber"), box);
        return false;
    }

    void ShowError(string message, Control focus)
    {
        errorText.Text = message;
        errorText.Visibility = Visibility.Visible;
        focus.Focus();
    }
}
