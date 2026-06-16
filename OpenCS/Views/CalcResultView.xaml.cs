using CScore;
using OpenCS.ViewModels;
using System.Linq;
using System.Text.Json;
using System.Windows.Controls;

namespace OpenCS.Views
{
    public partial class CalcResultView : UserControl
    {
        public CalcResultView(CalcResult result, AppViewModel app)
        {
            InitializeComponent();

            // Найти задачу и сечение
            var task    = app.CalcTasks.FirstOrDefault(t => t.Id == result.TaskId);
            var section = task != null
                ? app.CrossSections.FirstOrDefault(s => s.Id == task.SectionId)
                : null;

            if (section == null || task == null)
            {
                // Fallback: только сводка без графиков
                SummaryView.DataContext = new FallbackSummaryVM(result);
                Tabs.Items.RemoveAt(2);
                Tabs.Items.RemoveAt(1);
                return;
            }

            // Подготовить сечение: диаграммы + SetEps по плоскости из результата
            section.ResolveAndBuildDiagramms();

            var k = ParseKurvature(result.DataJson);
            section.SetEps(k, task.CalcType);

            SummaryView.DataContext = new StrainSummaryVM(result, section, task.CalcType);
            StressView.DataContext  = new SectionPlotVM(section, k, task.CalcType, SectionPlotMode.Stress);
            StrainView.DataContext  = new SectionPlotVM(section, k, task.CalcType, SectionPlotMode.Strain);
        }

        static Kurvature ParseKurvature(string dataJson)
        {
            try
            {
                if (string.IsNullOrEmpty(dataJson)) return new Kurvature();
                var doc  = JsonDocument.Parse(dataJson);
                var root = doc.RootElement;
                return new Kurvature
                {
                    e0 = root.TryGetProperty("e0", out var v) ? v.GetDouble() : 0,
                    ky = root.TryGetProperty("ky", out v)     ? v.GetDouble() : 0,
                    kz = root.TryGetProperty("kz", out v)     ? v.GetDouble() : 0,
                };
            }
            catch { return new Kurvature(); } // защита от повреждённого JSON
        }
    }

    /// <summary>Минимальный VM для случая когда сечение не найдено.</summary>
    class FallbackSummaryVM
    {
        public string TaskTag     { get; }
        public string CreatedText { get; }
        public string StatusText  { get; }
        public System.Windows.Media.Brush StatusBrush { get; }
        public string Eps0Text  => "—";
        public string KyText    => "—";
        public string KzText    => "—";
        public string NText     => "—";
        public string MxText    => "—";
        public string MyText    => "—";
        public bool   HasExtremes    => false;
        public string EpsMinText     => "—";
        public string EpsMaxText     => "—";
        public bool   HasStiffness   => false;
        public string XcText  => "—"; public string YcText  => "—";
        public string EAText  => "—"; public string EIy0Text => "—";
        public string EIz0Text => "—"; public string EIycText => "—";
        public string EIzcText => "—";
        public string EAelText => "—"; public string EIyelText => "—";
        public string EIzelText => "—";
        public string PhiEAText => "—"; public string PhiEIyText => "—";
        public string PhiEIzText => "—";
        public bool   HasRebar => false;
        public System.Collections.ObjectModel.ObservableCollection<object>
            RebarRows { get; } = [];
        public string IterationsText => "—";
        public string ResidualText   => "—";

        public FallbackSummaryVM(CalcResult r)
        {
            TaskTag     = r.TaskTag;
            CreatedText = r.Created;
            StatusText  = r.Status;
            StatusBrush = System.Windows.Media.Brushes.Gray;
        }
    }
}
