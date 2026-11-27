using System.Windows;

namespace OpenCS.Views;

/// <summary>
/// Сводка переноса ГУ из программы-источника: таблица «вид — перенесено — не перенесено» и сообщения (что пропущено
/// и почему, проверки резолвера). Немодальное: открывается после импорта и команды «Дочитать граничные условия».
/// </summary>
public partial class FemBoundarySummaryWindow : Window
{
    readonly string _text;

    public FemBoundarySummaryWindow(string header, IReadOnlyList<CScore.Import.FemBoundaryTransferRow> rows,
        IReadOnlyList<string> messages)
    {
        InitializeComponent();
        _text = Services.FemBoundarySummaryText.Format(header, rows, messages);
        headerText.Text = header;
        summaryGrid.ItemsSource = rows;
        messagesList.ItemsSource = messages;
        if (messages.Count == 0) messagesList.Visibility = Visibility.Collapsed;
    }

    void Close_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>Сводка и сообщения — в буфер обмена текстом (таблица через табуляцию).</summary>
    void Copy_Click(object sender, RoutedEventArgs e)
    {
        Utilites.UiClipboard.Copy(_text, this);
    }
}
