using System.Windows;
using CScore.Sp63Shear;

namespace OpenCS.Views;

/// <summary>Диалог с диаграммой несущей способности по длине проекции наклонного сечения.</summary>
public partial class ShearInclinedProjectionDialog : Window
{
    /// <summary>Создаёт диалог по готовой кривой.</summary>
    /// <param name="curve">Точки кривой по проекции.</param>
    /// <param name="criticalC">Критическая длина проекции, м.</param>
    public ShearInclinedProjectionDialog(IReadOnlyList<ProjectionPoint> curve, double criticalC)
    {
        InitializeComponent();
        ProjectionCanvas.Curve = curve;
        ProjectionCanvas.CriticalC = criticalC;
    }
}
