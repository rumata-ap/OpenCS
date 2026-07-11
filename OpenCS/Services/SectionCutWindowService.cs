using OpenCS.Utilites;
using OpenCS.ViewModels;
using OpenCS.Views;
using System;
using System.ComponentModel;

namespace OpenCS.Services;

/// <summary>Управляет немодальным окном эпюры разреза: открытие, закрытие, смена вкладки σ/ε.</summary>
public sealed class SectionCutWindowService : IDisposable
{
    SectionCutDiagramWindow? _window;
    SectionCutVM? _boundVm;
    SectionPlotMode _plotMode;

    readonly CalcSettings _settings;

    public SectionCutWindowService(CalcSettings settings)
    {
        _settings = settings;
    }

    public void Bind(SectionCutVM? cutVm, SectionPlotMode plotMode)
    {
        if (_boundVm != null)
            _boundVm.PropertyChanged -= OnVmPropertyChanged;

        _boundVm = cutVm;
        _plotMode = plotMode;

        if (cutVm != null)
        {
            cutVm.ScaleS = _settings.SectionCutWindow.ScaleS;
            cutVm.ScaleV = _settings.SectionCutWindow.ScaleV;
            cutVm.PropertyChanged += OnVmPropertyChanged;
        }

        SyncWindow();
    }

    public void UpdatePlotMode(SectionPlotMode plotMode)
    {
        _plotMode = plotMode;
        _window?.SetPlotMode(plotMode);
        UpdateTitle();
    }

    void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_boundVm == null) return;
        if (e.PropertyName is nameof(SectionCutVM.IsActive) or nameof(SectionCutVM.Result))
        {
            SyncWindow();
            if (_boundVm.Result != null && _window != null)
                _boundVm.RequestFit();
        }
        else if (e.PropertyName is nameof(SectionCutVM.WindowTitleSuffix))
        {
            UpdateTitle();
        }
    }

    /// <summary>
    /// Окно эпюры открывается только когда инструмент активен и уже есть результат разреза
    /// (после указания точки/точек на карте сечения).
    /// </summary>
    void SyncWindow()
    {
        if (_boundVm != null && _boundVm.IsActive && _boundVm.Result != null)
            EnsureWindow();
        else
            CloseWindow();
    }

    void EnsureWindow()
    {
        if (_boundVm == null) return;

        if (_window == null)
        {
            _window = new SectionCutDiagramWindow(_settings);
            _window.Closed += OnWindowClosed;
            _window.ExportRequested += OnExportRequested;
        }

        _window.Attach(_boundVm, _plotMode);
        ApplyWindowGeometry();
        UpdateTitle();
        _boundVm.RequestFit();
        _window.Show();
        _window.Activate();
    }

    void CloseWindow()
    {
        if (_window == null) return;
        SaveWindowGeometry();
        _window.Closed -= OnWindowClosed;
        _window.ExportRequested -= OnExportRequested;
        _window.Close();
        _window = null;
    }

    void OnWindowClosed(object? sender, EventArgs e)
    {
        SaveWindowGeometry();
        if (_window != null)
        {
            _window.Closed -= OnWindowClosed;
            _window.ExportRequested -= OnExportRequested;
        }
        _window = null;
        if (_boundVm != null && _boundVm.IsActive)
            _boundVm.IsActive = false;
    }

    void OnExportRequested(SectionCutExportOptions options, string path) =>
        _window?.PerformExport(path, options);

    void UpdateTitle()
    {
        if (_window == null || _boundVm == null) return;
        string mode = _plotMode == SectionPlotMode.Stress
            ? Loc.S("SectionCutWindowTitleStress")
            : Loc.S("SectionCutWindowTitleStrain");
        string suffix = string.IsNullOrWhiteSpace(_boundVm.WindowTitleSuffix)
            ? "" : $" — {_boundVm.WindowTitleSuffix}";
        _window.Title = mode + suffix;
    }

    void ApplyWindowGeometry()
    {
        if (_window == null) return;
        var ws = _settings.SectionCutWindow;
        _window.Width = ws.Width > 100 ? ws.Width : 900;
        _window.Height = ws.Height > 100 ? ws.Height : 500;
        if (ws.Left is double l && ws.Top is double t)
        {
            _window.WindowStartupLocation = System.Windows.WindowStartupLocation.Manual;
            _window.Left = l;
            _window.Top = t;
        }
    }

    void SaveWindowGeometry()
    {
        if (_window == null || _boundVm == null) return;
        var ws = _settings.SectionCutWindow;
        ws.Width = _window.Width;
        ws.Height = _window.Height;
        ws.Left = _window.Left;
        ws.Top = _window.Top;
        ws.ScaleS = _boundVm.ScaleS;
        ws.ScaleV = _boundVm.ScaleV;
    }

    public void Dispose()
    {
        if (_boundVm != null)
            _boundVm.PropertyChanged -= OnVmPropertyChanged;
        CloseWindow();
    }
}
