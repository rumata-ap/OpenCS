using CScore;
using OpenCS.Services;
using OpenCS.Utilites;
using OpenCS.ViewModels;
using System;
using System.Windows;

namespace OpenCS.Views;

public partial class SectionCutDiagramWindow : Window
{
    SectionCutVM? _vm;

    public event Action<SectionCutExportOptions, string>? ExportRequested;

    public SectionCutDiagramWindow(CalcSettings settings)
    {
        InitializeComponent();
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
                    if (_vm.Result != null)
                        SectionCutSvgExporter.Save(path, _vm.Result, DiagramCanvas.PlotMode,
                            _vm.IsHorizontal, options.AsOnScreen && _vm.FillMode, _vm.EpsCu,
                            asOnScreen: options.AsOnScreen);
                    break;
                case SectionCutExportFormat.Dxf:
                    if (_vm.Result != null)
                        SectionCutDxfExporter.Save(path, _vm.Result, DiagramCanvas.PlotMode,
                            _vm.IsHorizontal, _vm.EpsCu, asOnScreen: options.AsOnScreen);
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
