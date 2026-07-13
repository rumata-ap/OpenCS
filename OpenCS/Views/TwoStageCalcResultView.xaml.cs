using CScore;
using OpenCS.Services;
using OpenCS.Utilites;
using OpenCS.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace OpenCS.Views
{
    public partial class TwoStageCalcResultView : UserControl
    {
        readonly TwoStageSummaryVM _vm;
        readonly SectionCutWindowService _cutWindow;
        int _stageTabIndex;
        int _plotTabIndex;

        public TwoStageCalcResultView(CalcResult result, TwoStageSection section,
                                       CalcType calcType, CalcSettings settings,
                                       IFileDialogService fileDialogService)
        {
            InitializeComponent();
            _vm = new TwoStageSummaryVM(result, section, calcType, settings, fileDialogService);
            DataContext = _vm;

            _cutWindow = new SectionCutWindowService(settings);
            MainTabs.SelectionChanged += OnMainTabChanged;
            Unloaded += (_, _) => _cutWindow.Dispose();
            RebindCutWindow();
        }

        void OnMainTabChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.Source != MainTabs) return;
            _stageTabIndex = MainTabs.SelectedIndex;
            if (_stageTabIndex is 1 or 2)
            {
                var inner = _stageTabIndex == 1 ? Stage1InnerTabs : Stage2InnerTabs;
                _plotTabIndex = inner.SelectedIndex;
            }
            RebindCutWindow();
        }

        void OnStageInnerTabChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is TabControl tc)
                _plotTabIndex = tc.SelectedIndex;
            RebindCutWindow();
        }

        void RebindCutWindow()
        {
            SectionCutVM? cut = _stageTabIndex switch
            {
                1 => _vm.Stage1CutVM,
                2 => _vm.Stage2CutVM,
                _ => null
            };
            var mode = _plotTabIndex switch
            {
                1 => SectionPlotMode.Stress,
                2 => SectionPlotMode.Strain,
                _ => SectionPlotMode.Stress
            };
            _cutWindow.Bind(cut, mode);
        }
    }
}
