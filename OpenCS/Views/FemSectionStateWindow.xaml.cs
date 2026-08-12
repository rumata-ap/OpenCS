using System.Windows;
using OpenCS.ViewModels;

namespace OpenCS.Views;

/// <summary>Немодальное окно состояния сечения в точке интегрирования FEM-расчёта OpenSees:
/// вкладки «Сводка», «Напряжения», «Деформации» (переиспользование компонентов
/// результата одиночной задачи НДС).</summary>
public partial class FemSectionStateWindow : Window
{
    public FemSectionStateWindow()
    {
        InitializeComponent();
    }

    /// <summary>Обновляет содержимое окна под новую выбранную точку интегрирования.</summary>
    public void ShowContent(FemSectionSummaryVM summary, SectionPlotVM stress, SectionPlotVM strain, string title)
    {
        SummaryView.DataContext = summary;
        StressView.DataContext = stress;
        StrainView.DataContext = strain;
        Title = title;
    }
}
