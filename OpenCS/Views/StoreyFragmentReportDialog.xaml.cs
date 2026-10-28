using System.Windows;
using OpenCS.ViewModels;

namespace OpenCS.Views;

/// <summary>Отчёт сборки фрагмента: отказы, предупреждения и «не перенесено» (ошибки — первыми).</summary>
public partial class StoreyFragmentReportDialog : Window
{
    readonly IReadOnlyList<StoreyFragmentReportRow> _rows;

    /// <param name="titleKey">Ключ строки заголовка окна; null — «Отчёт сборки фрагмента».</param>
    public StoreyFragmentReportDialog(string schemaTag, string header, IReadOnlyList<StoreyFragmentReportRow> rows,
        string? titleKey = null)
    {
        InitializeComponent();
        if (titleKey != null) SetResourceReference(TitleProperty, titleKey);
        _rows = rows;
        schemaText.Text = schemaTag;
        headerText.Text = header;
        rowsList.ItemsSource = rows;
    }

    void Copy_Click(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(StoreyFragmentReportRows.ToText(_rows)); }
        catch (System.Runtime.InteropServices.ExternalException) { }
    }
}
