using CScore;
using OpenCS.ViewModels;

using System.Windows.Controls;
using System.Windows.Threading;

namespace OpenCS.Views
{
    /// <summary>Страница редактора группы арматурных стержней.</summary>
    public partial class RebarGroupEditorPage : UserControl
    {
        RebarGroupEditorVM? _vm;

        public RebarGroupEditorPage(MaterialArea? area, AppViewModel app)
        {
            InitializeComponent();
            _vm = new RebarGroupEditorVM(area, app);
            DataContext = _vm;

            // Подключить холст после первого рендера (нужны ActualWidth/Height для FitToView)
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
            {
                Canvas.SetVM(_vm);
                _vm.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName is nameof(RebarGroupEditorVM.CoverLinePoints)
                                       or nameof(RebarGroupEditorVM.ReferencePoints))
                        Dispatcher.BeginInvoke(Canvas.FitToView);
                };
            });
        }

        void EdgePlus_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is EdgeItem edge && _vm != null)
                _vm.AdjustEdgeCommand.Execute((edge, _vm.OffsetStep));
        }

        void EdgeMinus_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is EdgeItem edge && _vm != null)
                _vm.AdjustEdgeCommand.Execute((edge, -_vm.OffsetStep));
        }

        void EdgeOffset_GotKeyboardFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e)
        {
            if (sender is TextBox { DataContext: EdgeItem edge })
                Canvas.SetFocusedEdge(edge);
        }

        void EdgeOffset_LostKeyboardFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e)
        {
            Canvas.SetFocusedEdge(null);
        }

        void BarField_GotKeyboardFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e)
        {
            if (sender is TextBox { DataContext: BarItem bar })
                Canvas.SetFocusedBar(bar);
        }

        void BarField_LostKeyboardFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e)
        {
            Canvas.SetFocusedBar(null);
        }

        public void RefreshPlotSettings()
        {
            Canvas.InvalidateVisual();
        }
    }
}
