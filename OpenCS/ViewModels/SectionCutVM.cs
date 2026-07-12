using CScore;
using OpenCS.Services;
using OpenCS.Utilites;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;

namespace OpenCS.ViewModels;

/// <summary>Состояние и геометрия инструмента разреза сечения — общее для вкладок «Напряжения»/«Деформации» одного результата.</summary>
public sealed class SectionCutVM : ViewModelBase
{
    readonly CrossSection _section;
    readonly Kurvature _k;
    readonly CalcType _calcType;
    readonly IFileDialogService _fileDialogService;
    readonly List<(MaterialArea Area, Kurvature Ka)> _regionAreas;
    readonly bool _ten;

    (double X, double Y)? _p1;
    (double X, double Y)? _p2;

    CutMode _mode = CutMode.GradientSnap;
    public CutMode Mode
    {
        get => _mode;
        set { if (_mode == value) return; _mode = value; ClearPoints(); OnPropertyChanged(); }
    }

    bool _isActive;
    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive == value) return;
            _isActive = value;
            if (_isActive)
            {
                _mode = CutMode.GradientSnap;
                OnPropertyChanged(nameof(Mode));
            }
            else
                Clear();
            OnPropertyChanged();
        }
    }

    bool _isHorizontal = false;
    public bool IsHorizontal
    {
        get => _isHorizontal;
        set
        {
            if (_isHorizontal == value) return;
            _isHorizontal = value;
            OnPropertyChanged();
            Changed?.Invoke();
        }
    }

    bool _fillMode = true;
    public bool FillMode
    {
        get => _fillMode;
        set { if (_fillMode == value) return; _fillMode = value; OnPropertyChanged(); Changed?.Invoke(); }
    }

    bool _hatchMode;
    public bool HatchMode
    {
        get => _hatchMode;
        set { if (_hatchMode == value) return; _hatchMode = value; OnPropertyChanged(); Changed?.Invoke(); }
    }

    bool _showRebarForce;
    /// <summary>Показывать усилие N в арматуре вместо σ/ε.</summary>
    public bool ShowRebarForce
    {
        get => _showRebarForce;
        set { if (_showRebarForce == value) return; _showRebarForce = value; OnPropertyChanged(); Changed?.Invoke(); }
    }

    double _scaleS = 1.0;
    public double ScaleS
    {
        get => _scaleS;
        set { if (Math.Abs(_scaleS - value) < 1e-9) return; _scaleS = value; OnPropertyChanged(); Changed?.Invoke(); }
    }

    double _scaleV = 1.0;
    public double ScaleV
    {
        get => _scaleV;
        set { if (Math.Abs(_scaleV - value) < 1e-9) return; _scaleV = value; OnPropertyChanged(); Changed?.Invoke(); }
    }

    /// <summary>Рисовать оси, сетку, легенду (для экспорта «с оформлением»).</summary>
    public bool ShowChrome { get; set; } = true;

    /// <summary>Предельная деформация бетона (только для задачи limit_force); null — линия-предел не рисуется.</summary>
    public double? EpsCu { get; set; }

    /// <summary>Порог проекции арматуры на линию разреза, метры (зависит от масштаба, задаётся из View).</summary>
    public double RebarThresholdM { get; set; } = 0.02;

    public string WindowTitleSuffix { get; set; } = "";

    public SectionCutResult? Result { get; private set; }

    public bool HasPendingPoint => Mode == CutMode.Free && _p1 != null && _p2 == null;

    public ICommand ExportCommand { get; }
    public ICommand FitCommand { get; }

    public event Action? Changed;
    public event Action? FitRequested;
    public event Action<SectionCutExportOptions, string>? ExportRequested;

    /// <summary>Ключ линии усилия арматуры на эпюре (номер + позиция s, мм).</summary>
    public readonly record struct RebarLineKey(int Num, int SMm);

    readonly Dictionary<RebarLineKey, double> _rebarLengthPxOverrides = new();

    public static RebarLineKey RebarKey(CutRebarMarker r) =>
        new(r.Num ?? 0, (int)Math.Round(r.S * 1000.0));

    public bool TryGetRebarLengthPxOverride(RebarLineKey key, out double lengthPx) =>
        _rebarLengthPxOverrides.TryGetValue(key, out lengthPx);

    public void SetRebarLengthPxOverride(RebarLineKey key, double lengthPx)
    {
        _rebarLengthPxOverrides[key] = Math.Max(0, lengthPx);
        Changed?.Invoke();
    }

    void ClearRebarOverrides() => _rebarLengthPxOverrides.Clear();

    public SectionCutVM(CrossSection section, Kurvature k, CalcType calcType, IFileDialogService fileDialogService, bool ten = true)
    {
        _section = section;
        _k = k;
        _calcType = calcType;
        _fileDialogService = fileDialogService;
        _ten = ten;
        _regionAreas = section.EnumerateAreas(k)
            .Where(t => t.area.Hull != null && t.area.Diagramms.ContainsKey(calcType))
            .ToList();

        ExportCommand = new RelayCommand(_ => Export());
        FitCommand = new RelayCommand(_ => RequestFit());
    }

    public MatType GetAreaMatType(int? areaIndex)
    {
        if (areaIndex is not int idx || idx < 0 || idx >= _regionAreas.Count)
            return MatType.Concrete;
        return _regionAreas[idx].Area.Material?.Type ?? MatType.Concrete;
    }

    public void RequestFit()
    {
        FitRequested?.Invoke();
        Changed?.Invoke();
    }

    public void AddPoint((double X, double Y) p)
    {
        if (Mode == CutMode.GradientSnap)
        {
            _p1 = p;
            _p2 = null;
            Recompute();
        }
        else if (_p1 == null)
        {
            _p1 = p;
            OnPropertyChanged(nameof(HasPendingPoint));
        }
        else
        {
            _p2 = p;
            Recompute();
        }
    }

    public void SetPoint(int index, (double X, double Y) p)
    {
        if (Mode == CutMode.Free)
        {
            if (index == 0) _p1 = p; else _p2 = p;
        }
        else
        {
            _p1 = p;
        }
        Recompute();
    }

    public void CancelPending()
    {
        if (Mode == CutMode.Free && _p1 != null && _p2 == null)
        {
            _p1 = null;
            OnPropertyChanged(nameof(HasPendingPoint));
            Changed?.Invoke();
        }
    }

    public void Clear()
    {
        ClearPoints();
    }

    void ClearPoints()
    {
        _p1 = null;
        _p2 = null;
        Result = null;
        ClearRebarOverrides();
        OnPropertyChanged(nameof(HasPendingPoint));
        OnPropertyChanged(nameof(Result));
        Changed?.Invoke();
    }

    void Recompute()
    {
        if (_p1 == null) return;
        ClearRebarOverrides();
        Result = SectionCutBuilder.Build(_section, _k, _calcType, Mode, _p1.Value, _p2, RebarThresholdM, tenB: _ten);
        OnPropertyChanged(nameof(HasPendingPoint));
        OnPropertyChanged(nameof(Result));
        RequestFit();
    }

    void Export()
    {
        var options = SectionCutExportDialog.Show();
        if (options == null) return;

        string filter = options.Format switch
        {
            SectionCutExportFormat.Svg => "SVG (*.svg)|*.svg",
            SectionCutExportFormat.Dxf => "DXF (*.dxf)|*.dxf",
            _ => "PNG (*.png)|*.png"
        };
        string ext = options.Format switch
        {
            SectionCutExportFormat.Svg => "svg",
            SectionCutExportFormat.Dxf => "dxf",
            _ => "png"
        };
        string? path = _fileDialogService.SaveFile(
            filter: filter,
            defaultExt: ext,
            title: Loc.S("SectionCutExportTitle"));
        if (string.IsNullOrEmpty(path)) return;
        ExportRequested?.Invoke(options, path);
    }
}
