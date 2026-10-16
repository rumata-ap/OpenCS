using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using OpenCS.Utilites;
using OpenCS.ViewModels;

namespace OpenCS.Views;

/// <summary>
/// Выбор нагрузки стадии: комбинация (фильтр типов, поиск, формула) или загружение. Двойной щелчок/Enter — выбрать;
/// Ctrl+F — к поиску, ↓ из поиска — к списку. Окно открывается на вкладке текущего источника с выделенной строкой.
/// </summary>
public partial class FemLoadSourcePickerDialog : Window
{
    readonly FemLoadSourcePickerVM _vm;

    /// <summary>Выбранный источник (валиден после DialogResult == true).</summary>
    public FemLoadSource? Result { get; private set; }

    /// <param name="stageTag">Имя стадии для заголовка; null — нагрузка одиночного расчёта.</param>
    /// <param name="title">Заголовок окна вместо заголовка по стадии.</param>
    public FemLoadSourcePickerDialog(IReadOnlyList<FemLoadSource> sources, FemLoadSource? current, string? stageTag, string? title = null)
    {
        InitializeComponent();
        Title = title ?? (stageTag is null ? Loc.S("FemLoadSourcePickerTitleSingle") : string.Format(Loc.S("FemLoadSourcePickerTitle"), stageTag));
        _vm = new FemLoadSourcePickerVM(sources, current);
        _vm.PropertyChanged += Vm_PropertyChanged;
        DataContext = _vm;
        UpdateEmptyHints();
        Loaded += (_, _) => FocusInitial();
        InputBindings.Add(new KeyBinding(new RelayCommand(_ => FocusSearch()), Key.F, ModifierKeys.Control));
    }

    void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FemLoadSourcePickerVM.VisibleCombinations) or nameof(FemLoadSourcePickerVM.VisibleCases))
            UpdateEmptyHints();
    }

    void UpdateEmptyHints()
    {
        NoCombinationsText.Visibility = _vm.HasCombinations ? Visibility.Collapsed : Visibility.Visible;
        CombinationsNoMatches.Visibility = _vm.HasCombinations && _vm.VisibleCombinations.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        CasesNoMatches.Visibility = _vm.VisibleCases.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    DataGrid ActiveGrid => _vm.SelectedTab == FemLoadSourcePickerVM.CombinationsTab ? CombinationsGrid : CasesGrid;
    TextBox ActiveSearch => _vm.SelectedTab == FemLoadSourcePickerVM.CombinationsTab ? CombinationSearchBox : CaseSearchBox;

    /// <summary>Есть выбор — строка в видимой области и фокус на ней (Enter сразу принимает); нет — фокус в поиск.</summary>
    void FocusInitial()
    {
        var grid = ActiveGrid;
        if (grid.SelectedItem is { } item)
        {
            grid.UpdateLayout();
            grid.ScrollIntoView(item);
            if (grid.ItemContainerGenerator.ContainerFromItem(item) is DataGridRow row)
                row.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            else grid.Focus();
        }
        else FocusSearch();
    }

    void FocusSearch()
    {
        var box = ActiveSearch;
        if (!box.IsVisible) { ActiveGrid.Focus(); return; }
        box.Focus();
        box.SelectAll();
    }

    void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Down)
        {
            var grid = ActiveGrid;
            if (grid.Items.Count == 0) return;
            if (grid.SelectedIndex < 0) grid.SelectedIndex = 0;
            grid.ScrollIntoView(grid.SelectedItem);
            grid.UpdateLayout();
            if (grid.ItemContainerGenerator.ContainerFromIndex(grid.SelectedIndex) is DataGridRow row)
                row.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            // Единственная строка под поиском выбирается сама; кнопка «Выбрать» — IsDefault.
            ((TextBox)sender).GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            _vm.SelectSingleVisible();
        }
    }

    void AllTypes_Click(object sender, RoutedEventArgs e) => _vm.AllTypes();

    void Grid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Только по строке, не по заголовку или полосе прокрутки.
        for (var d = e.OriginalSource as DependencyObject; d != null && d != sender; d = VisualTreeHelper.GetParent(d))
        {
            if (d is DataGridRow)
            {
                Accept();
                return;
            }
            if (d is System.Windows.Controls.Primitives.DataGridColumnHeader or System.Windows.Controls.Primitives.ScrollBar) return;
        }
    }

    /// <summary>Enter в списке — выбрать (DataGrid иначе переходит на следующую строку и кнопка по умолчанию не срабатывает).</summary>
    void Grid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        Accept();
    }

    void Select_Click(object sender, RoutedEventArgs e) => Accept();

    void Accept()
    {
        if (!_vm.CanAccept || _vm.Selected is not { } selected) return;
        Result = selected;
        DialogResult = true;
    }
}
