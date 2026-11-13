using System.ComponentModel;
using System.Threading;
using System.Windows.Controls;
using System.Windows.Data;
using CScore.Batch;
using OpenCS.ViewModels;
using OpenCS.Views;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Панель фильтра пакета на живом гриде: сортировка по Кисп и отбор строк.</summary>
public sealed class BatchVerdictFilterBarTests
{
    static ShellLayeredSlsBatchRowVM Row(int num, string status, double? util)
        => new() { Num = num, Reason = new BatchVerdictView(new BatchRowVerdict(status, util, "acrc", "", "")) };

    [Fact]
    public void SortsByUtilDescending_AndFilters()
    {
        RunSta(() =>
        {
            var rows = new List<ShellLayeredSlsBatchRowVM>
            {
                Row(1, "ok", 0.4), Row(2, "not_passed", 1.3), Row(3, "not_converged", null), Row(4, "ok", 0.9),
            };
            var grid = new DataGrid { IsReadOnly = true, CanUserAddRows = false, ItemsSource = rows };
            var bar = new BatchVerdictFilterBar { Target = grid };

            var view = CollectionViewSource.GetDefaultView(rows);
            Assert.Equal(ListSortDirection.Descending, view.SortDescriptions.Single().Direction);
            Assert.Equal(new[] { 2, 4, 1, 3 }, view.Cast<ShellLayeredSlsBatchRowVM>().Select(r => r.Num).ToArray());

            bar.FilterBox.SelectedIndex = 1;   // не проходят
            Assert.Equal(new[] { 2 }, view.Cast<ShellLayeredSlsBatchRowVM>().Select(r => r.Num).ToArray());
            bar.FilterBox.SelectedIndex = 2;   // не сошлись
            Assert.Equal(new[] { 3 }, view.Cast<ShellLayeredSlsBatchRowVM>().Select(r => r.Num).ToArray());
            bar.FilterBox.SelectedIndex = 0;
            Assert.Equal(4, view.OfType<ShellLayeredSlsBatchRowVM>().Count());
            Assert.False(string.IsNullOrEmpty(bar.CountsText.Text));
        });
    }

    [Fact]
    public void WithoutUtil_KeepsRowOrder()
    {
        RunSta(() =>
        {
            var rows = new List<ShellLayeredSlsBatchRowVM> { Row(1, "ok", null), Row(2, "not_converged", null) };
            _ = new BatchVerdictFilterBar { Target = new DataGrid { ItemsSource = rows } };
            Assert.Empty(CollectionViewSource.GetDefaultView(rows).SortDescriptions);
        });
    }

    static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null) throw new Xunit.Sdk.XunitException(error.ToString());
    }
}
