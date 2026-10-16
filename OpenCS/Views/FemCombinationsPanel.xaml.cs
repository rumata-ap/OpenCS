using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using OpenCS.Utilites;
using OpenCS.ViewModels;

namespace OpenCS.Views;

/// <summary>
/// Вкладка «Комбинации» редактора схемы (<see cref="FemCombinationsTabVM"/>): список комбинаций, свойства выбранной и
/// перенос загружений стрелками. Внешний DataContext — редактор схемы; модель вкладки создаётся при его смене.
/// </summary>
public partial class FemCombinationsPanel : UserControl
{
    FemCombinationsTabVM? _vm;

    public FemCombinationsPanel()
    {
        InitializeComponent();
        // «1,0» или «1.0» — в той же культуре, что коэффициенты в таблице и формуле.
        resetButton.Content = 1.0.ToString("0.0", System.Globalization.CultureInfo.CurrentCulture);
        DataContextChanged += (_, _) =>
        {
            _vm = DataContext is FemSchemaEditorVM editor ? new FemCombinationsTabVM(editor) : null;
            root.DataContext = _vm;
        };
        termsGrid.CellEditEnding += (_, e) =>
        {
            // Коэффициент применяется по выходу из ячейки, а не только при уходе со строки.
            if (e.EditAction == DataGridEditAction.Commit)
                Dispatcher.BeginInvoke(() => termsGrid.CommitEdit(DataGridEditingUnit.Row, true),
                    System.Windows.Threading.DispatcherPriority.Background);
        };
    }

    void NewCombination_Click(object sender, RoutedEventArgs e) => _vm?.AddCombination();
    void CopyCombination_Click(object sender, RoutedEventArgs e) => _vm?.CopyCombination();
    void GenerateSp20_Click(object sender, RoutedEventArgs e) => _vm?.GenerateSp20();
    void RecomputeAuto_Click(object sender, RoutedEventArgs e) => _vm?.RecomputeAuto();
    async void GenerateFromEnvelope_Click(object sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        await _vm.GenerateFromEnvelopeAsync((analysis, current) =>
        {
            var dialog = new FemEnvelopeSelectionDialog(string.Format(Loc.S("FemEnvelopeSelectionSource"), analysis.Tag),
                current.Tolerance * 100, current.MaxCount) { Owner = Window.GetWindow(this) };
            return dialog.ShowDialog() == true ? dialog.Options : null;
        });
    }

    /// <summary>Поле правки коэффициента: фокус и выделение — сразу вводить число.</summary>
    void CoefficientEditor_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox box) return;
        box.Focus();
        box.SelectAll();
    }
    void DeleteCombinations_Click(object sender, RoutedEventArgs e) =>
        _vm?.DeleteCombinations(combinationsGrid.SelectedItems.OfType<FemCombinationRow>().ToList());

    void AddTerms_Click(object sender, RoutedEventArgs e) => _vm?.AddTerms(availableGrid.SelectedItems.OfType<FemCombinationCaseRow>().ToList());
    void AddAllTerms_Click(object sender, RoutedEventArgs e) => _vm?.AddAllTerms();
    void RemoveTerms_Click(object sender, RoutedEventArgs e) => _vm?.RemoveTerms(SelectedTerms());
    void RemoveAllTerms_Click(object sender, RoutedEventArgs e) => _vm?.RemoveAllTerms();
    void ApplyCoefficient_Click(object sender, RoutedEventArgs e) => _vm?.ApplyCoefficient(SelectedTerms());
    void ResetCoefficient_Click(object sender, RoutedEventArgs e) => _vm?.ResetCoefficient(SelectedTerms());

    List<FemCombinationTermRow> SelectedTerms() => termsGrid.SelectedItems.OfType<FemCombinationTermRow>().ToList();

    /// <summary>Двойной щелчок по загружению схемы — в комбинацию.</summary>
    void AvailableRow_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGridRow { DataContext: FemCombinationCaseRow row }) _vm?.AddTerms([row]);
    }

    /// <summary>Двойной щелчок по слагаемому (кроме ячейки коэффициента — она правится) — убрать из комбинации.</summary>
    void TermRow_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGridRow { DataContext: FemCombinationTermRow row }) return;
        if (FindParent<DataGridCell>(e.OriginalSource as DependencyObject) is { Column.DisplayIndex: 0 }) return;
        _vm?.RemoveTerms([row]);
    }

    /// <summary>Enter в поле имени, типа или комментария — применить (как выход из поля).</summary>
    void CommitOnEnter(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        var binding = sender switch
        {
            ComboBox combo => combo.GetBindingExpression(ComboBox.TextProperty),
            TextBox box => box.GetBindingExpression(TextBox.TextProperty),
            _ => null,
        };
        binding?.UpdateSource();
        e.Handled = true;
    }

    /// <summary>Тип, выбранный из списка, применяется сразу.</summary>
    void TypeCombo_DropDownClosed(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(() => typeCombo.GetBindingExpression(ComboBox.TextProperty)?.UpdateSource(),
            System.Windows.Threading.DispatcherPriority.Background);

    static T? FindParent<T>(DependencyObject? child) where T : DependencyObject
    {
        for (var p = child; p != null; p = p is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(p) : LogicalTreeHelper.GetParent(p))
            if (p is T t) return t;
        return null;
    }
}
