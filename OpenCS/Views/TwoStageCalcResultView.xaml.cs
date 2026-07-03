using CScore;
using OpenCS.Utilites;
using OpenCS.ViewModels;
using System.Windows.Controls;

namespace OpenCS.Views
{
    /// <summary>
    /// UserControl результата двухстадийного расчёта (single, two_stage_strain).
    /// Содержит 3 верхние вкладки: общая Сводка, Этап 1 и Этап 2 —
    /// каждая стадия имеет собственную Сводку/σ/ε.
    /// </summary>
    public partial class TwoStageCalcResultView : UserControl
    {
        public TwoStageCalcResultView(CalcResult result, TwoStageSection section,
                                       CalcType calcType, CalcSettings settings)
        {
            InitializeComponent();
            DataContext = new TwoStageSummaryVM(result, section, calcType, settings);
        }
    }
}
