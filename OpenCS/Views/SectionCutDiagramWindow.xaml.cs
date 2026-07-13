using CScore;
using OpenCS.Services;
using OpenCS.Utilites;
using OpenCS.ViewModels;
using OpenCS.Views.Helpers;
using System;
using System.Windows;
using System.Windows.Input;

namespace OpenCS.Views;

public partial class SectionCutDiagramWindow : Window
{
    SectionCutVM? _vm;

    public event Action<SectionCutExportOptions, string>? ExportRequested;

    public SectionCutDiagramWindow(CalcSettings settings)
    {
        InitializeComponent();
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Copy, OnCopyExecuted, OnCopyCanExecute));
    }

    public void Attach(SectionCutVM vm, SectionPlotMode plotMode)
    {
        _vm = vm;
        DataContext = vm;
        DiagramCanvas.PlotMode = plotMode;
        vm.ExportRequested -= OnVmExportRequested;
        vm.ExportRequested += OnVmExportRequested;
    }

    public void SetPlotMode(SectionPlotMode plotMode) => DiagramCanvas.PlotMode = plotMode;

    void OnCopyCanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = _vm?.Result != null
            && DiagramCanvas.ActualWidth > 1
            && DiagramCanvas.ActualHeight > 1;
        e.Handled = true;
    }

    void OnCopyExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        if (!DiagramCanvas.CopyEmfToClipboard())
            MessageBox.Show(Loc.S("SectionCutCopyFailed"), Loc.S("Error"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }

    public void PerformExport(string path, SectionCutExportOptions options)
    {
        if (_vm == null) return;

        bool prevChrome = DiagramCanvas.ShowChrome;
        bool prevPlain = DiagramCanvas.ExportPlainMode;
        try
        {
            DiagramCanvas.ShowChrome = options.AsOnScreen;
            DiagramCanvas.ExportPlainMode = !options.AsOnScreen;
            DiagramCanvas.InvalidateVisual();
            DiagramCanvas.UpdateLayout();

            switch (options.Format)
            {
                case SectionCutExportFormat.Png:
                    SectionCutExporter.ExportPng(DiagramCanvas, path);
                    break;
                case SectionCutExportFormat.Svg:
                case SectionCutExportFormat.Dxf:
                    if (_vm.Result == null) break;
                    var exportArgs = new SectionCutExportArgs
                    {
                        Result = _vm.Result,
                        Mode = DiagramCanvas.PlotMode,
                        Horizontal = _vm.IsHorizontal,
                        AsOnScreen = options.AsOnScreen,
                        FillMode = _vm.FillMode,
                        HatchMode = _vm.HatchMode,
                        ShowRebarForce = _vm.ShowRebarForce,
                        EpsCu = _vm.EpsCu,
                        GetAreaMatType = _vm.GetAreaMatType,
                        ResolveRebarLengthPx = r =>
                        {
                            var key = SectionCutVM.RebarKey(r);
                            return _vm.TryGetRebarLengthPxOverride(key, out double px)
                                ? px
                                : SectionCutExportArgs.DefaultRebarLinePx;
                        },
                        View = DiagramCanvas.CaptureViewTransform()
                    };
                    if (options.Format == SectionCutExportFormat.Svg)
                        SectionCutSvgExporter.Save(path, exportArgs);
                    else
                        SectionCutDxfExporter.Save(path, exportArgs);
                    break;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, Loc.S("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            DiagramCanvas.ShowChrome = prevChrome;
            DiagramCanvas.ExportPlainMode = prevPlain;
            DiagramCanvas.InvalidateVisual();
        }
    }

    void OnVmExportRequested(SectionCutExportOptions options, string path) =>
        ExportRequested?.Invoke(options, path);

    void OnOrientHorizontalChecked(object sender, RoutedEventArgs e)
    {
        if (_vm != null) _vm.IsHorizontal = true;
    }

    void OnOrientVerticalChecked(object sender, RoutedEventArgs e)
    {
        if (_vm != null) _vm.IsHorizontal = false;
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_vm != null)
            _vm.ExportRequested -= OnVmExportRequested;
        base.OnClosed(e);
    }
}
