using System.Windows;
using OpenCS.ViewModels;

namespace OpenCS.Views;

/// <summary>«Маски из родителя…»: выбор постановки CSfea родителя; нет источников — причина, «Снять» недоступна.</summary>
public partial class StoreyFragmentMasksDialog : Window
{
    /// <summary>Источник после «Снять».</summary>
    public FragmentMaskSourceItem? Source { get; private set; }

    /// <param name="current">Строка о действующих масках («не сняты» / «из … от …»).</param>
    public StoreyFragmentMasksDialog(string fragmentTag, IReadOnlyList<FragmentMaskSourceItem> sources,
        FragmentMaskSourceItem? selected, string? reason, string current)
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
    }

    void Take_Click(object sender, RoutedEventArgs e)
    {
        if (sourceBox.SelectedItem is not FragmentMaskSourceItem source) return;
        Source = source;
        DialogResult = true;
    }
}
