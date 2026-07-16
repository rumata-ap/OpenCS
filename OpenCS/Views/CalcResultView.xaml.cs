using CScore;
using OpenCS.Services;
using OpenCS.ViewModels;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace OpenCS.Views
{
    public partial class CalcResultView : UserControl
    {
        SectionCutWindowService? _cutWindow;

    public CalcResultView(CalcResult result, AppViewModel app)
    {
        var task = app.CalcTasks.FirstOrDefault(t => t.Id == result.TaskId);

        if (task?.Kind == "opensees_section_interaction_n_mx_my")
        {
            Content = new OpenSeesSpatialInteractionResultView(result);
            return;
        }

        if (task?.Kind == "prestress_loss")
        {
            Content = new PrestressLossResultView(result, app);
            return;
        }

        if (task?.Kind is "steel_check" or
            "steel_central_compression" or "steel_central_tension" or
            "steel_bending" or "steel_compression_bending" or
            "steel_tension_bending" or "steel_shear" or
            "steel_torsion" or "steel_constructive")
        {
            Content = new SteelCheckResultView(result.DataJson);
            return;
        }

        if (task?.Kind is "torsion_bem" or "torsion_fem")
        {
            Content = new TorsionResultView(result, app, task);
            return;
        }

        if (task?.Kind is "fire_r_check" or "fire_r_check_batch"
            or "strain_state_batch" or "two_stage_strain_batch"
            or "shell_simpl_wa_sls_batch" or "shell_simpl_wa_uls_batch"
            or "shell_simpl_capri_sls_batch" or "shell_simpl_capri_uls_batch"
            or "limit_force_batch" or "limit_moment_batch" or "limit_axial_batch"
            or "shell_strain_state_batch"
            or "shell_layered_uls_batch"
            or "strength_ndm_batch"
            or "cracking_batch" or "crack_width_batch")
        {
            Content = task.Kind switch
            {
                "fire_r_check_batch"   => new FireRCheckBatchResultView(result, app, task),
                "strain_state_batch" or "two_stage_strain_batch"
                                       => new StrainStateBatchResultView(result, app, task),
                "limit_force_batch" or "limit_moment_batch" or "limit_axial_batch"
                                       => new LimitForceBatchResultView(result, app, task),
                "strength_ndm_batch"   => new StrengthNDMBatchResultView(result, app, task),
                "shell_strain_state_batch" => new ShellStrainBatchResultView(result, app, task),
                "shell_layered_uls_batch"  => new ShellStrainBatchResultView(result, app, task),
                "cracking_batch"       => new CrackingBatchResultView(result, app, task),
                "crack_width_batch"    => new CrackWidthBatchResultView(result, app, task),
                _ when task.Kind.StartsWith("shell_simpl_") && task.Kind.EndsWith("_batch")
                                       => new ShellSimplBatchResultView(result, task, app),
                _                      => new FireRCheckResultView(result, app, task)
            };
            return;
        }

        if (task?.Kind is "cracking" or "crack_width")
        {
            Content = task.Kind == "cracking"
                ? new CrackingResultView(result, app, task)
                : new CrackWidthResultView(result, app, task);
            return;
        }

        if (task?.Kind is "limit_force" or "limit_moment" or "limit_axial")
        {
            Content = new LimitForceResultView(result, app, task);
            return;
        }

        // Поиск плоскости деформаций пластины (одиночный) и слоистая прочность
        if (task != null && (task.Kind == "shell_strain_state" || task.Kind == "shell_layered_uls"))
        {
            Content = new ShellStrainResultView(result, app, task);
            return;
        }

        // Упрощённый расчёт пластин
        if (task != null && task.Kind.StartsWith("shell_simpl_"))
        {
            Content = new ShellSimplResultView(result, task);
            return;
        }

        // Двухстадийный одиночный расчёт — отдельный view с вкладками по стадиям
        if (task?.Kind == "two_stage_strain")
        {
            var tss = app.CrossSections.FirstOrDefault(s => s.Id == task.SectionId) as TwoStageSection;
            if (tss != null)
            {
                tss.ResolveAndBuildDiagramms(app.CalcSettings.Sp63DescEtaMin,
                    pool: app.Diagrams,
                    rebarDifferentialDiagram: app.CalcSettings.RebarDifferentialDiagram);
                tss.Stage1.ResolveAndBuildDiagramms(app.CalcSettings.Sp63DescEtaMin,
                    pool: app.Diagrams,
                    rebarDifferentialDiagram: app.CalcSettings.RebarDifferentialDiagram);
                Content = new TwoStageCalcResultView(result, tss, task.CalcType, app.CalcSettings, app.FileDialogService);
                return;
            }
            // tss == null: проваливаемся в стандартный путь (покажет FallbackSummaryVM)
        }

        InitializeComponent();

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
            section.ResolveAndBuildDiagramms(app.CalcSettings.Sp63DescEtaMin,
                pool: app.Diagrams,
                rebarDifferentialDiagram: app.CalcSettings.RebarDifferentialDiagram);

            var k = ParseKurvature(result.DataJson);
            var settings = app.CalcSettings;
            bool ten = settings.ResolveConcreteTension(task.CalcType);
            section.SetEps(k, task.CalcType, ten);

            SummaryView.DataContext = new StrainSummaryVM(result, section, task.CalcType, settings, ten);
            var stressVm = new SectionPlotVM(section, k, task.CalcType, SectionPlotMode.Stress, settings, ten);
            var strainVm = new SectionPlotVM(section, k, task.CalcType, SectionPlotMode.Strain, settings, ten);

            var cutVm = new SectionCutVM(section, k, task.CalcType, app.FileDialogService, ten)
            {
                WindowTitleSuffix = $"{task.Tag} — {section.Tag}"
            };
            stressVm.CutVM = cutVm;
            strainVm.CutVM = cutVm;

            StressView.DataContext = stressVm;
            StrainView.DataContext = strainVm;

            _cutWindow = new SectionCutWindowService(settings);
            _cutWindow.Bind(cutVm, SectionPlotMode.Stress);
            Tabs.SelectionChanged += OnTabSelectionChanged;
            Unloaded += (_, _) => _cutWindow?.Dispose();
        }

        void OnTabSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Tabs.SelectedIndex == 1) _cutWindow?.UpdatePlotMode(SectionPlotMode.Stress);
            else if (Tabs.SelectedIndex == 2) _cutWindow?.UpdatePlotMode(SectionPlotMode.Strain);
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
        public bool   EtaEnabled => false;
        public string EtaModeText     => "—";
        public string MxOriginalText  => "—";
        public string MyOriginalText  => "—";
        public string L0xText => "—"; public string HxText => "—";
        public string SlendernessXText => "—"; public string DxText => "—"; public string NcrXText => "—";
        public string EtaXText  => "—";
        public string L0yText => "—"; public string HyText => "—";
        public string SlendernessYText => "—"; public string DyText => "—"; public string NcrYText => "—";
        public string EtaYText  => "—";
        public bool   EtaUnstable => false;
        public bool   EtaExtrapolationFailed => false;
        public bool   ShowEtaTrajectory => false;
        public string EtaXTrajectoryText => "—";
        public string EtaYTrajectoryText => "—";
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
        public System.Collections.ObjectModel.ObservableCollection<StrainSummaryVM.RebarRow>
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
