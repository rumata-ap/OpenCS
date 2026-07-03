using OpenCS.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace OpenCS.Views;

/// <summary>Просмотр результата теплового расчёта огневого сечения.</summary>
public partial class FireThermalResultView : UserControl
{
    bool _fieldTabLoaded;
    bool _rebarTabLoaded;
    bool _convergenceTabLoaded;

    public FireThermalResultView()
    {
        InitializeComponent();
    }

    void InnerTab_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source != innerTabs || DataContext is not FireThermalResultVM vm)
            return;

        if (innerTabs.SelectedItem == fieldTab)
        {
            if (!_fieldTabLoaded)
            {
                _fieldTabLoaded = true;
                fieldTab.Content = new FireMeshPlotView
                {
                    DataContext = vm.EnsureTemperaturePlotLoaded()
                };
            }

            if (fieldTab.Content is FireMeshPlotView plotView)
                plotView.RequestFitToView();
        }
        else if (innerTabs.SelectedItem == rebarTab && !_rebarTabLoaded)
        {
            _rebarTabLoaded = true;
            vm.EnsureRebarChartLoaded();
            rebarTab.Content = BuildRebarTabContent(vm);
        }
        else if (innerTabs.SelectedItem == convergenceTab && !_convergenceTabLoaded)
        {
            _convergenceTabLoaded = true;
            vm.EnsureConvergenceChartsLoaded();
            convergenceTab.Content = BuildConvergenceTabContent(vm);
        }
    }

    static Grid BuildRebarTabContent(FireThermalResultVM vm)
    {
        var grid = new Grid();
        var chart = new FireChartView();
        chart.SetBinding(DataContextProperty,
            new System.Windows.Data.Binding(nameof(FireThermalResultVM.RebarChart)) { Source = vm });
        chart.SetBinding(VisibilityProperty,
            new System.Windows.Data.Binding(nameof(FireThermalResultVM.HasRebarChart))
            {
                Source = vm,
                Converter = (System.Windows.Data.IValueConverter)Application.Current.FindResource("BoolToVisibility")
            });
        grid.Children.Add(chart);

        var empty = new TextBlock
        {
            Margin = new Thickness(16),
            Foreground = System.Windows.Media.Brushes.Gray,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Text = Utilites.Loc.S("FireThermal_NoRebarHistory")
        };
        empty.SetBinding(VisibilityProperty,
            new System.Windows.Data.Binding(nameof(FireThermalResultVM.HasRebarChart))
            {
                Source = vm,
                Converter = (System.Windows.Data.IValueConverter)Application.Current.FindResource("InverseBoolToVisibility")
            });
        grid.Children.Add(empty);
        return grid;
    }

    static Grid BuildConvergenceTabContent(FireThermalResultVM vm)
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var picard = new FireChartView();
        picard.SetBinding(DataContextProperty,
            new System.Windows.Data.Binding(nameof(FireThermalResultVM.PicardChart)) { Source = vm });
        Grid.SetRow(picard, 0);
        grid.Children.Add(picard);

        var resid = new FireChartView();
        resid.SetBinding(DataContextProperty,
            new System.Windows.Data.Binding(nameof(FireThermalResultVM.ResidualChart)) { Source = vm });
        Grid.SetRow(resid, 1);
        grid.Children.Add(resid);

        var empty = new TextBlock
        {
            Margin = new Thickness(16),
            Foreground = System.Windows.Media.Brushes.Gray,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Text = vm.ConvergenceSummaryText
        };
        empty.SetBinding(VisibilityProperty,
            new System.Windows.Data.Binding(nameof(FireThermalResultVM.HasConvergenceCharts))
            {
                Source = vm,
                Converter = (System.Windows.Data.IValueConverter)Application.Current.FindResource("InverseBoolToVisibility")
            });
        Grid.SetRowSpan(empty, 2);
        grid.Children.Add(empty);
        return grid;
    }
}
