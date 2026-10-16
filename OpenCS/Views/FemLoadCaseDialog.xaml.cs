using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using CScore.Combinations;
using CScore.Fem;
using OpenCS.Utilites;
using OpenCS.ViewModels;

namespace OpenCS.Views;

/// <summary>
/// Параметры загружения: имя, атрибуты нагрузки СП 20 (<see cref="Sp20LoadAttributesVM"/>: вид, группа, γf, γf п. 7.3
/// у постоянных, доля длительной части у кратковременных), собственный вес. Результат — <see cref="Result"/>; проверка
/// имени и запись — у вызывающего (<see cref="Validate"/>).
/// </summary>
public partial class FemLoadCaseDialog : Window
{
    /// <summary>Новые значения загружения (без Id и схемы).</summary>
    public FemLoadCase Result { get; private set; } = new();

    /// <summary>Проверка у вызывающего (имя занято и т. п.): текст ошибки или null — закрыть.</summary>
    public Func<FemLoadCase, string?>? Validate { get; init; }

    readonly FemLoadCase _source;
    readonly Sp20LoadAttributesVM _attributes;

    public FemLoadCaseDialog(FemLoadCase loadCase, string? origin, Sp20GammaDefaults defaults)
    {
        InitializeComponent();
        _source = loadCase;
        _attributes = new Sp20LoadAttributesVM(loadCase, defaults);
        DataContext = _attributes;
        tagBox.Text = loadCase.Tag;
        selfWeightCheck.IsChecked = loadCase.SelfWeightFactor is { } k && k != 0;
        selfWeightBox.Text = (loadCase.SelfWeightFactor ?? 1).ToString("0.####", CultureInfo.CurrentCulture);
        selfWeightBox.IsEnabled = selfWeightCheck.IsChecked == true;
        if (origin != null)
        {
            originText.Text = string.Format(Loc.S("FemLoadCaseDialogOrigin"), origin);
            originText.Visibility = Visibility.Visible;
        }
        Loaded += (_, _) => { tagBox.Focus(); tagBox.SelectAll(); };
    }

    void SelfWeight_Changed(object sender, RoutedEventArgs e) => selfWeightBox.IsEnabled = selfWeightCheck.IsChecked == true;

    void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(tagBox.Text)) { ShowError(Loc.S("FemLoadCaseTagTaken"), tagBox); return; }
        double? selfWeight = null;
        if (selfWeightCheck.IsChecked == true)
        {
            if (!Pars.ParseAny(selfWeightBox.Text.Trim(), out double k) || !double.IsFinite(k) || k == 0)
            {
                ShowError(Loc.S("FemSelfWeightFactorInvalid"), selfWeightBox);
                return;
            }
            selfWeight = k;
        }
        // ψ1/ψ2 в диалоге не правятся: коэффициенты сочетаний СП 20 назначаются по рангу нагрузки (п. 6.3–6.5).
        var result = new FemLoadCase
        {
            Tag = tagBox.Text.Trim(), Sp20Type = _source.Sp20Type, Sp20Group = _source.Sp20Group,
            GammaFUnfav = _source.GammaFUnfav, GammaFFav = _source.GammaFFav, Psi1 = _source.Psi1, Psi2 = _source.Psi2,
            SelfWeightFactor = selfWeight, LongTermShare = _source.LongTermShare,
        };
        if (_attributes.ApplyTo(result) is { } bad) { ShowError(bad, tagBox); return; }
        if (Validate?.Invoke(result) is { } error) { ShowError(error, tagBox); return; }
        Result = result;
        DialogResult = true;
    }

    void ShowError(string message, Control focus)
    {
        errorText.Text = message;
        errorText.Visibility = Visibility.Visible;
        focus.Focus();
    }
}
