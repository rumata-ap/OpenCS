using System.Windows;
using OpenCS.ViewModels;

namespace OpenCS.Views;

/// <summary>Отчёт сборки фрагмента: отказы, предупреждения и «не перенесено» (ошибки — первыми).</summary>
public partial class StoreyFragmentReportDialog : Window
{
    readonly IReadOnlyList<StoreyFragmentReportRow> _rows;

    public StoreyFragmentReportDialog(string schemaTag, string header, IReadOnlyList<StoreyFragmentReportRow> rows)
    {
        InitializeComponent();
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
