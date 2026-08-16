using System;
using System.Windows;
using System.Windows.Controls;

namespace OpenCS.Views;

public partial class TotalCurvatureSummaryView : UserControl
{
    /// <summary>Событие открытия графики выбранной стадии.</summary>
    public event EventHandler<int>? StagePlotRequested;

    public TotalCurvatureSummaryView()
    {
        InitializeComponent();
    }

    private void StagePlot_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag } && int.TryParse(tag, out var stage))
            StagePlotRequested?.Invoke(this, stage);
    }
}
