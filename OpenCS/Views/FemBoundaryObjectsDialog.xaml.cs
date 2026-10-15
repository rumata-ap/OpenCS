using System.Windows;
using System.Windows.Controls;

namespace OpenCS.Views;

/// <summary>
/// Объекты типа ГУ текстом — как поле «Узлы №» / «Линии №» типа опоры RFEM: убранные из списка теряют связь, добавленные
/// получают параметры типа. Проверка списков — у вызывающего (<see cref="Validate"/>), ошибка показывается в диалоге.
/// </summary>
public partial class FemBoundaryObjectsDialog : Window
{
    readonly List<TextBox> _boxes = [];

    /// <summary>Тексты полей после «OK» — в порядке их добавления.</summary>
    public IReadOnlyList<string> Texts => _boxes.Select(b => b.Text).ToList();

    /// <summary>Проверка текстов полей: null — верно, иначе сообщение.</summary>
    public Func<IReadOnlyList<string>, string?>? Validate { get; init; }

    public FemBoundaryObjectsDialog(string header, string? origin)
    {
        InitializeComponent();
        headerText.Text = header;
        originText.Text = origin ?? "";
        originText.Visibility = string.IsNullOrEmpty(origin) ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Добавляет поле списка с подписью.</summary>
    public FemBoundaryObjectsDialog Field(string label, string text)
    {
        fieldsPanel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 10, 0, 2) });
        var box = new TextBox
        {
            Text = text, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, MinHeight = 44, MaxHeight = 160,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        fieldsPanel.Children.Add(box);
        _boxes.Add(box);
        if (_boxes.Count == 1) Loaded += (_, _) => { box.Focus(); box.CaretIndex = box.Text.Length; };
        return this;
    }

    void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (Validate?.Invoke(Texts) is { } error)
        {
            errorText.Text = error;
            errorText.Visibility = Visibility.Visible;
            return;
        }
        DialogResult = true;
    }
}
