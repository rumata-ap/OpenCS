using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using CScore;
using CScore.PrestressLoss;
using OpenCS.Utilites;

namespace OpenCS.ViewModels;

/// <summary>ViewModel строки DataGrid — один арматурный регион.</summary>
public class PrestressGroupRowVM : ViewModelBase
{
    public int    AreaId  { get; }
    public string AreaTag { get; }

    PrestressGroupParams _p;

    public PrestressGroupRowVM(PrestressGroupParams p, string areaTag)
    {
        _p = p; AreaId = p.AreaId; AreaTag = areaTag;
    }

    static readonly string[] RelaxFormulaItems = ["A600–A1000", "Вр/К (упрочн.)", "К (стаб.)"];
    static readonly string[] SubMethodItems    = ["Механ.", "Электротерм."];

    public double SigSp0         { get => _p.SigSp0;  set { _p.SigSp0  = value; OnPropertyChanged(); } }
    public string RelaxFormulaStr
    {
        get => RelaxFormulaItems[(int)_p.RelaxFormula];
        set { int i = Array.IndexOf(RelaxFormulaItems, value); if (i >= 0) _p.RelaxFormula = (RelaxFormula)i; OnPropertyChanged(); }
    }
    public string SubMethodStr
    {
        get => SubMethodItems[(int)_p.SubMethod];
        set { int i = Array.IndexOf(SubMethodItems, value); if (i >= 0) _p.SubMethod = (TensionSubMethod)i; OnPropertyChanged(); }
    }
    public double RelaxR          { get => _p.RelaxR;         set { _p.RelaxR         = value; OnPropertyChanged(); } }
    public bool   UseDefaultDeltaT      { get => _p.UseDefaultDeltaT;      set { _p.UseDefaultDeltaT      = value; OnPropertyChanged(); } }
    public double DeltaT                { get => _p.DeltaT;                set { _p.DeltaT                = value; OnPropertyChanged(); } }
    public bool   UseDefaultFormDeform  { get => _p.UseDefaultFormDeform;  set { _p.UseDefaultFormDeform  = value; OnPropertyChanged(); } }
    public int    NForms                { get => _p.NForms;                set { _p.NForms                = value; OnPropertyChanged(); } }
    public double DeltaLForm            { get => _p.DeltaLForm;            set { _p.DeltaLForm            = value; OnPropertyChanged(); } }
    public double LForm                 { get => _p.LForm;                 set { _p.LForm                 = value; OnPropertyChanged(); } }
    public bool   UseDefaultAnchorDeform { get => _p.UseDefaultAnchorDeform; set { _p.UseDefaultAnchorDeform = value; OnPropertyChanged(); } }
    public double DeltaLAnchor          { get => _p.DeltaLAnchor;          set { _p.DeltaLAnchor          = value; OnPropertyChanged(); } }
    public double LAnchor               { get => _p.LAnchor;               set { _p.LAnchor               = value; OnPropertyChanged(); } }
    public double Omega1                { get => _p.Omega1;                set { _p.Omega1                = value; OnPropertyChanged(); } }
    public double KFriction             { get => _p.KFriction;             set { _p.KFriction             = value; OnPropertyChanged(); } }
    public double XLength               { get => _p.XLength;               set { _p.XLength               = value; OnPropertyChanged(); } }
    public double Theta                 { get => _p.Theta;                 set { _p.Theta                 = value; OnPropertyChanged(); } }
    public bool   SigmaBpAuto          { get => _p.SigmaBpAuto;           set { _p.SigmaBpAuto           = value; OnPropertyChanged(); } }
    public double SigmaBpManual        { get => _p.SigmaBpManual;         set { _p.SigmaBpManual         = value; OnPropertyChanged(); } }

    public PrestressGroupParams ToParams() => _p;
}

/// <summary>ViewModel диалога параметров задачи «Потери преднапряжения».</summary>
public class PrestressLossDlgVM : ViewModelBase
{
    readonly AppViewModel _app;
    readonly CalcTask     _task;
    readonly Window       _window;

    int    _methodIndex   = 0;   // 0=OnSupports, 1=OnConcrete
    bool   _heatTreated   = false;
    int    _humidityIndex = 1;   // 0=Above75, 1=40_75, 2=Below40
    bool   _concrClassAuto = true;
    string _concrClassText = "30";

    public string SectionTag { get; }

    public int MethodIndex
    {
        get => _methodIndex;
        set { _methodIndex = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsOnSupports)); OnPropertyChanged(nameof(IsOnConcrete)); }
    }

    public bool IsOnSupports => _methodIndex == 0;
    public bool IsOnConcrete => _methodIndex == 1;

    public bool HeatTreated
    {
        get => _heatTreated;
        set { _heatTreated = value; OnPropertyChanged(); }
    }

    public int HumidityIndex
    {
        get => _humidityIndex;
        set { _humidityIndex = value; OnPropertyChanged(); OnPropertyChanged(nameof(PhiBCrDisplay)); }
    }

    public bool ConcreteClassAuto
    {
        get => _concrClassAuto;
        set { _concrClassAuto = value; OnPropertyChanged(); OnPropertyChanged(nameof(ConcreteClassManual)); OnPropertyChanged(nameof(PhiBCrDisplay)); }
    }

    public bool ConcreteClassManual
    {
        get => !_concrClassAuto;
        set { _concrClassAuto = !value; OnPropertyChanged(); OnPropertyChanged(nameof(ConcreteClassAuto)); OnPropertyChanged(nameof(PhiBCrDisplay)); }
    }

    public string ConcreteClassText
    {
        get => _concrClassText;
        set { _concrClassText = value; OnPropertyChanged(); OnPropertyChanged(nameof(PhiBCrDisplay)); }
    }

    public string PhiBCrDisplay
    {
        get
        {
            var h = _humidityIndex switch { 0 => HumidityClass.Above75, 2 => HumidityClass.Below40, _ => HumidityClass.H40_75 };
            double cc = _concrClassAuto
                ? GetConcreteClassFromSection()
                : (double.TryParse(_concrClassText, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 30);
            cc = Math.Clamp(cc, 20, 60);
            double[,] t = {
                {2.1,1.9,1.7,1.6,1.5,1.4,1.3,1.2,1.1},
                {2.7,2.4,2.2,2.0,1.9,1.8,1.6,1.5,1.4},
                {3.1,2.8,2.5,2.3,2.2,2.0,1.9,1.8,1.7}
            };
            double[] cl = {20,25,30,35,40,45,50,55,60};
            int row = (int)h; int col = 0;
            for (int i = 0; i < cl.Length; i++) if (cc >= cl[i]) col = i;
            return $"{t[row, col]:F1}";
        }
    }

    double GetConcreteClassFromSection()
    {
        var section = _app.CrossSections.FirstOrDefault(s => s.Id == _task.SectionId);
        var area = section?.Areas.FirstOrDefault(a => a.Category == AreaCategory.Region);
        if (area?.Material?.N is { } ch)
            return ch.Class;
        return 30;
    }

    public ObservableCollection<PrestressGroupRowVM> Groups { get; } = [];

    public ICommand SaveCommand   { get; }
    public ICommand CancelCommand { get; }

    public PrestressLossDlgVM(AppViewModel app, CalcTask task, Window window)
    {
        _app = app; _task = task; _window = window;

        var section = app.CrossSections.FirstOrDefault(s => s.Id == task.SectionId);
        SectionTag  = section?.Tag ?? $"Id={task.SectionId}";

        PrestressLossParams existing;
        try { existing = JsonSerializer.Deserialize<PrestressLossParams>(task.ParamsJson) ?? new(); }
        catch { existing = new(); }

        _methodIndex    = existing.Method == TensionMethod.OnConcrete ? 1 : 0;
        _heatTreated    = existing.HeatTreated;
        _humidityIndex  = existing.Humidity switch { HumidityClass.Above75 => 0, HumidityClass.Below40 => 2, _ => 1 };
        _concrClassAuto = existing.ConcreteClassAuto;
        _concrClassText = existing.ConcreteClassOverride.ToString(CultureInfo.InvariantCulture);

        if (section != null)
        {
            foreach (var area in section.Areas.Where(a =>
                a.Category is AreaCategory.RebarGroup or AreaCategory.SingleBar))
            {
                var saved = existing.Groups.FirstOrDefault(g => g.AreaId == area.Id);
                var p = saved ?? new PrestressGroupParams { AreaId = area.Id, SigSp0 = area.SigSp };
                Groups.Add(new PrestressGroupRowVM(p, area.Tag));
            }
        }

        SaveCommand   = new RelayCommand(_ => Save());
        CancelCommand = new RelayCommand(_ => _window.DialogResult = false);
    }

    void Save()
    {
        var p = new PrestressLossParams
        {
            Method              = _methodIndex == 1 ? TensionMethod.OnConcrete : TensionMethod.OnSupports,
            HeatTreated         = _heatTreated,
            Humidity            = _humidityIndex switch { 0 => HumidityClass.Above75, 2 => HumidityClass.Below40, _ => HumidityClass.H40_75 },
            ConcreteClassAuto   = _concrClassAuto,
            ConcreteClassOverride = double.TryParse(_concrClassText, NumberStyles.Float,
                CultureInfo.InvariantCulture, out var v) ? v : 30,
            Groups              = Groups.Select(r => r.ToParams()).ToList()
        };
        _task.ParamsJson = JsonSerializer.Serialize(p);
        _app.db.SaveCalcTask(_task);
        _window.DialogResult = true;
    }
}
