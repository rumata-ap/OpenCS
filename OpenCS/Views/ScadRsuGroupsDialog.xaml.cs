using System.Windows;
using System.Windows.Controls;

namespace OpenCS.Views;

/// <summary>
/// Выбор групп РСУ SCAD для импорта (0 — C, 1 — CL, 2 — N, 3 — NL): РСУ большой группы КЭ — миллионы строк,
/// лишние группы лучше не тянуть.
/// </summary>
public partial class ScadRsuGroupsDialog : Window
{
    readonly CheckBox[] _boxes;

    public ScadRsuGroupsDialog(IEnumerable<int> selected)
    {
        InitializeComponent();
        Owner = Application.Current.MainWindow;
        _boxes = [GroupC, GroupCL, GroupN, GroupNL];
        var set = selected.ToHashSet();
        for (int i = 0; i < _boxes.Length; i++)
        {
            _boxes[i].IsChecked = set.Contains(i);
            _boxes[i].Click += (_, _) => UpdateOk();
        }
        UpdateOk();
    }

    /// <summary>Отмеченные группы РСУ.</summary>
    public int[] SelectedGroups =>
        Enumerable.Range(0, _boxes.Length).Where(i => _boxes[i].IsChecked == true).ToArray();

    void UpdateOk() => OkButton.IsEnabled = SelectedGroups.Length > 0;

    void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
