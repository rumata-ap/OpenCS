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

    /// <summary>Пункт списка в ячейке: значение перечисления и подпись на языке интерфейса.</summary>
    public sealed record Choice(int Value, string Label);

    /// <summary>Формулы релаксации (порядок — как у <see cref="RelaxFormula"/>); выбор — по значению, не по тексту.</summary>
    public static IReadOnlyList<Choice> RelaxFormulaChoices =>
        [new(0, Loc.S("PrestressRelaxA600")), new(1, Loc.S("PrestressRelaxWire")), new(2, Loc.S("PrestressRelaxStable"))];

    /// <summary>Способ натяжения на упоры (порядок — как у <see cref="TensionSubMethod"/>).</summary>
    public static IReadOnlyList<Choice> SubMethodChoices =>
        [new(0, Loc.S("PrestressSubMechanical")), new(1, Loc.S("PrestressSubElectrothermal"))];

    public double SigSp0         { get => _p.SigSp0;  set { _p.SigSp0  = value; OnPropertyChanged(); } }
    public int RelaxFormulaIndex
    {
        get => (int)_p.RelaxFormula;
        set { if (value >= 0) _p.RelaxFormula = (RelaxFormula)value; OnPropertyChanged(); }
    }
    public int SubMethodIndex
    {
        get => (int)_p.SubMethod;
        set { if (value >= 0) _p.SubMethod = (TensionSubMethod)value; OnPropertyChanged(); }
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
                : (NumberInput.TryParse(_concrClassText, out var v) ? v : 30);
            cc = Math.Clamp(cc, 10, 100);
            return $"{PrestressLossCalc.PhiBCr(h, cc):F1}";
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
                a.Category == AreaCategory.RebarGroup))
            {
                var saved = existing.Groups.FirstOrDefault(g => g.AreaId == area.Id);
                var p = saved ?? new PrestressGroupParams { AreaId = area.Id, SigSp0 = area.SigSp };
                Groups.Add(new PrestressGroupRowVM(p, area.Tag));
            }
        }

        SaveCommand   = new RelayCommand(_ => Save());
        CancelCommand = new RelayCommand(_ => _window.DialogResult = false);
    }

    /// <summary>
    /// «Сохранить»: класс бетона при ручном вводе — число 10…100 (запятая или точка); иначе сообщение и окно остаётся
    /// открытым (раньше нераспознанное молча становилось 30). Возвращает true, если записано.
    /// </summary>
    public bool Save()
    {
        double concreteClass = 30;
        if (!_concrClassAuto)
        {
            if (!NumberInput.TryParse(_concrClassText, out concreteClass) || concreteClass < 10 || concreteClass > 100)
            {
                _app.Notifier.Warning(Loc.S("PrestressErrConcreteClass"), Loc.S("Warning"));
                return false;
            }
        }
        else if (NumberInput.TryParse(_concrClassText, out var kept)) concreteClass = kept;
        var p = new PrestressLossParams
        {
            Method              = _methodIndex == 1 ? TensionMethod.OnConcrete : TensionMethod.OnSupports,
            HeatTreated         = _heatTreated,
            Humidity            = _humidityIndex switch { 0 => HumidityClass.Above75, 2 => HumidityClass.Below40, _ => HumidityClass.H40_75 },
            ConcreteClassAuto   = _concrClassAuto,
            ConcreteClassOverride = concreteClass,
            Groups              = Groups.Select(r => r.ToParams()).ToList()
        };
        _task.ParamsJson = JsonSerializer.Serialize(p);
        _app.db.SaveCalcTask(_task);
        if (_window.IsLoaded) _window.DialogResult = true;
        return true;
    }
}
