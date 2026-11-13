using System.Collections;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using CScore.Batch;
using OpenCS.Utilites;
using OpenCS.ViewModels;

namespace OpenCS.Views;

/// <summary>
/// Панель над таблицей пакета: фильтр «все / не проходят / не сошлись» и счётчики по причинам.
/// Работает с представлением коллекции указанного грида (строки — <see cref="IBatchVerdictRow"/>);
/// при наличии Кисп сортирует по нему по убыванию.
/// </summary>
public partial class BatchVerdictFilterBar : UserControl
{
    /// <summary>Таблица, строки которой фильтруются.</summary>
    public static readonly DependencyProperty TargetProperty = DependencyProperty.Register(
        nameof(Target), typeof(DataGrid), typeof(BatchVerdictFilterBar),
        new PropertyMetadata(null, (d, e) => ((BatchVerdictFilterBar)d).OnTargetChanged(e.OldValue as DataGrid, e.NewValue as DataGrid)));

    static readonly DependencyPropertyDescriptor ItemsSourceDescriptor =
        DependencyPropertyDescriptor.FromProperty(ItemsControl.ItemsSourceProperty, typeof(DataGrid));

    BatchVerdictFilter _filter = BatchVerdictFilter.All;

    public BatchVerdictFilterBar() => InitializeComponent();

    /// <summary>Таблица, строки которой фильтруются.</summary>
    public DataGrid? Target
    {
        get => (DataGrid?)GetValue(TargetProperty);
        set => SetValue(TargetProperty, value);
    }

    void OnTargetChanged(DataGrid? oldGrid, DataGrid? newGrid)
    {
        if (oldGrid != null) ItemsSourceDescriptor.RemoveValueChanged(oldGrid, OnItemsSourceChanged);
        if (newGrid != null) ItemsSourceDescriptor.AddValueChanged(newGrid, OnItemsSourceChanged);
        Apply(sortByUtil: true);
    }

    void OnItemsSourceChanged(object? sender, EventArgs e) => Apply(sortByUtil: true);

    void FilterBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _filter = (BatchVerdictFilter)Math.Max(0, FilterBox.SelectedIndex);
        Apply(sortByUtil: false);
    }

    void Apply(bool sortByUtil)
    {
        if (Target?.ItemsSource is not IEnumerable source) { CountsText.Text = ""; return; }
        var rows = source.OfType<IBatchVerdictRow>().ToList();
        if (rows.Count == 0) { CountsText.Text = ""; return; }

        var view = CollectionViewSource.GetDefaultView(source);
        if (view == null) return;
        var filter = _filter;
        view.Filter = filter == BatchVerdictFilter.All ? null
            : o => o is IBatchVerdictRow r && BatchVerdictFilters.Match(r.Reason, filter);
        if (sortByUtil && rows.Any(r => r.Reason.Verdict.Util.HasValue))
        {
            using (view.DeferRefresh())
            {
                view.SortDescriptions.Clear();
                view.SortDescriptions.Add(new SortDescription(
                    nameof(IBatchVerdictRow.Reason) + "." + nameof(BatchVerdictView.SortUtil), ListSortDirection.Descending));
            }
        }

        int failed = rows.Count(r => r.Reason.Verdict.Status == BatchRowStatus.NotPassed);
        int notConverged = rows.Count(r => r.Reason.Verdict.Status == BatchRowStatus.NotConverged);
        int errors = rows.Count(r => r.Reason.Verdict.Status == BatchRowStatus.Error);
        CountsText.Text = string.Format(CultureInfo.CurrentCulture, Loc.S("BatchVerdictCounts"), failed, notConverged, errors);
    }
}
