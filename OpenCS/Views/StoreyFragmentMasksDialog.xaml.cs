using System.Windows;
using OpenCS.ViewModels;

namespace OpenCS.Views;

/// <summary>
/// «Маски из родителя…» — два блока: «Из готового результата» (источник и «Снять» рядом; нет источников — причина,
/// «Снять» недоступна) и «Посчитать родителя для масок» (служебные постановки; выбранный источник там не используется,
/// у секущей — выбор постановки фрагмента).
/// </summary>
public partial class StoreyFragmentMasksDialog : Window
{
    /// <summary>Источник после «Снять».</summary>
    public FragmentMaskSourceItem? Source { get; private set; }

    /// <summary>«Маски для всех загружений»: служебная постановка родителя (источник не выбирается).</summary>
    public bool AllLoadCases { get; private set; }

    /// <summary>«Маски для секущего расчёта»: служебная секущая постановка родителя (источник не выбирается).</summary>
    public bool SecantAll { get; private set; }

    /// <summary>Постановка фрагмента для служебного секущего расчёта; null — все секущие постановки.</summary>
    public int? SecantAnalysisId { get; private set; }

    /// <param name="current">Строка о действующих масках («не сняты» / «из … от …»).</param>
    /// <param name="secantChoices">Варианты «По постановке фрагмента»; пусто — секущих постановок у фрагмента нет.</param>
    public StoreyFragmentMasksDialog(string fragmentTag, IReadOnlyList<FragmentMaskSourceItem> sources,
        FragmentMaskSourceItem? selected, string? reason, string current,
        IReadOnlyList<FragmentSecantChoice>? secantChoices = null, FragmentSecantChoice? secantSelected = null)
    {
        InitializeComponent();
        schemaText.Text = fragmentTag;
        sourceBox.ItemsSource = sources;
        sourceBox.SelectedItem = selected;
        sourceBox.IsEnabled = sources.Count > 0;
        takeButton.IsEnabled = sources.Count > 0;
        if (reason != null)
        {
            reasonText.Text = reason;
            reasonText.Visibility = Visibility.Visible;
        }
        currentText.Text = current;
        secantChoices ??= [];
        secantBox.ItemsSource = secantChoices;
        secantBox.SelectedItem = secantSelected ?? secantChoices.FirstOrDefault();
        bool hasSecant = secantChoices.Count > 0;
        secantBox.IsEnabled = hasSecant;
        secantButton.IsEnabled = hasSecant;
        secantNoneText.Visibility = hasSecant ? Visibility.Collapsed : Visibility.Visible;
    }

    void Take_Click(object sender, RoutedEventArgs e)
    {
        if (sourceBox.SelectedItem is not FragmentMaskSourceItem source) return;
        Source = source;
        DialogResult = true;
    }

    void All_Click(object sender, RoutedEventArgs e)
    {
        AllLoadCases = true;
        DialogResult = true;
    }

    void Secant_Click(object sender, RoutedEventArgs e)
    {
        if (secantBox.SelectedItem is not FragmentSecantChoice choice) return;
        SecantAll = true;
        SecantAnalysisId = choice.AnalysisId;
        DialogResult = true;
    }
}
