using System.Windows;
using System.Windows.Controls;
using OpenCS.Utilites;

namespace OpenCS.Views;

/// <summary>
/// Импорт усилий SCAD из XLS: какие КЭ читать (список или все КЭ схемы). Наборы привязываются к выбранному
/// элементу схемы, только если список взят из него кнопкой «Из выбранного элемента» и не правился — иначе без
/// привязки (строка под полем говорит, что будет).
/// </summary>
public partial class ScadForceImportDialog : Window
{
    readonly string? _memberElements;
    readonly string? _memberTag;
    bool _fromMember;

    /// <summary>Номера КЭ (пусто при «все КЭ»).</summary>
    public IReadOnlySet<int> ElementIds { get; private set; } = new HashSet<int>();
    public bool ImportAllElements { get; private set; }
    /// <summary>Привязать наборы к элементу, из которого взят список.</summary>
    public bool BindToMember { get; private set; }

    /// <param name="memberTag">Элемент схемы, выбранный в дереве; null — нет.</param>
    /// <param name="memberElements">Его КЭ строкой; null — нет.</param>
    /// <param name="allElements">Запомненный выбор «все КЭ».</param>
    public ScadForceImportDialog(string? memberTag = null, string? memberElements = null, bool allElements = false)
    {
        InitializeComponent();
        Owner = Application.Current?.MainWindow;
        _memberTag = memberTag;
        _memberElements = string.IsNullOrWhiteSpace(memberElements) ? null : memberElements;
        FromMemberButton.IsEnabled = _memberElements != null;
        if (_memberElements != null && !allElements)
        {
            ElementsBox.Text = _memberElements;
            _fromMember = true;
        }
        AllElementsCheck.IsChecked = allElements;
        Refresh();
        ElementsBox.Focus();
    }

    void ElementsBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (OkButton == null) return; // TextChanged во время InitializeComponent
        if (_fromMember && ElementsBox.Text != _memberElements) _fromMember = false;
        Refresh();
    }

    void AllElements_Changed(object sender, RoutedEventArgs e) => Refresh();

    void FromMember_Click(object sender, RoutedEventArgs e)
    {
        if (_memberElements == null) return;
        AllElementsCheck.IsChecked = false;
        ElementsBox.Text = _memberElements;
        _fromMember = true;
        Refresh();
    }

    void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (!Validate(out var ids)) return;
        ImportAllElements = AllElementsCheck.IsChecked == true;
        ElementIds = ids;
        BindToMember = !ImportAllElements && _fromMember;
        DialogResult = true;
    }

    bool Validate(out SortedSet<int> ids)
    {
        ids = [];
        if (AllElementsCheck.IsChecked == true) return true;
        return ElementRanges.TryParse(ElementsBox.Text, out ids, out _);
    }

    void Refresh()
    {
        if (OkButton == null) return;
        bool all = AllElementsCheck.IsChecked == true;
        ElementsBox.IsEnabled = !all;
        FromMemberButton.IsEnabled = !all && _memberElements != null;

        string? error = null;
        bool ok = all || ElementRanges.TryParse(ElementsBox.Text, out _, out error);
        OkButton.IsEnabled = ok;
        // Пустое поле — без красной строки: кнопка и так недоступна, ошибку показываем на введённом.
        bool showError = !ok && !string.IsNullOrWhiteSpace(ElementsBox.Text);
        ElementsErrorText.Text = showError ? error : "";
        ElementsErrorText.Visibility = showError ? Visibility.Visible : Visibility.Collapsed;

        BindHintText.Text = !all && _fromMember && _memberTag != null
            ? string.Format(Loc.S("ScadForceImportBindHint"), _memberTag)
            : Loc.S("ScadForceImportNoBindHint");
    }
}
