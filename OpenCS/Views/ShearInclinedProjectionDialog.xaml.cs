using System.Windows;
using System.Windows.Controls;
using OpenCS.ViewModels;
using OpenCS.Views.Helpers;

namespace OpenCS.Views;

/// <summary>Диалог с диаграммой несущей способности по длине проекции наклонного сечения.</summary>
public partial class ShearInclinedProjectionDialog : Window
{
    /// <summary>Создаёт диалог с диаграммами по обеим плоскостям сдвига.</summary>
    /// <param name="charts">Подготовленные данные диаграмм по плоскостям.</param>
    public ShearInclinedProjectionDialog(
        IReadOnlyList<ShearInclinedProjectionChartVM> charts)
    {
        ArgumentNullException.ThrowIfNull(charts);
        InitializeComponent();

        Configure(VyCanvas, VyNoData, charts.FirstOrDefault(c => c.Plane == "vy"));
        Configure(VxCanvas, VxNoData, charts.FirstOrDefault(c => c.Plane == "vx"));
    }

    /// <summary>Настраивает один холст либо показывает сообщение об отсутствии данных.</summary>
    static void Configure(
        ShearInclinedProjectionCanvas canvas,
        TextBlock noData,
        ShearInclinedProjectionChartVM? chart)
    {
        if (chart is not null && chart.HasCurve)
        {
            canvas.Curve = chart.Curve;
            canvas.CriticalC = chart.CriticalC;
            canvas.Visibility = Visibility.Visible;
            noData.Visibility = Visibility.Collapsed;
        }
        else
        {
            canvas.Visibility = Visibility.Collapsed;
            noData.Visibility = Visibility.Visible;
        }
    }
}
