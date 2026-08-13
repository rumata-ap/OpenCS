using System.Windows;
using OpenCS.Services;
using OpenCS.Utilites;
using OpenCS.ViewModels;

namespace OpenCS.Views;

/// <summary>Немодальное окно состояния сечения в точке интегрирования FEM-расчёта OpenSees:
/// вкладки «Сводка», «Напряжения», «Деформации» (переиспользование компонентов
/// результата одиночной задачи НДС, включая инструмент разреза).</summary>
public partial class FemSectionStateWindow : Window
{
    SectionCutWindowService? _cutWindow;

    public FemSectionStateWindow()
    {
        InitializeComponent();
        Tabs.SelectionChanged += OnTabSelectionChanged;
        Closed += (_, _) => _cutWindow?.Dispose();
    }

    /// <summary>Обновляет содержимое окна под новую выбранную точку интегрирования.</summary>
    public void ShowContent(FemSectionSummaryVM summary, SectionPlotVM stress, SectionPlotVM strain,
        SectionCutVM cutVm, CalcSettings settings, string title)
    {
        SummaryView.DataContext = summary;
        StressView.DataContext = stress;
        StrainView.DataContext = strain;

        _cutWindow?.Dispose();
        _cutWindow = new SectionCutWindowService(settings);
        _cutWindow.Bind(cutVm, SectionPlotMode.Stress);

        Title = title;
    }

    void OnTabSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_cutWindow == null) return;
        if (Tabs.SelectedIndex == 1) _cutWindow.UpdatePlotMode(SectionPlotMode.Stress);
        else if (Tabs.SelectedIndex == 2) _cutWindow.UpdatePlotMode(SectionPlotMode.Strain);
    }
}
