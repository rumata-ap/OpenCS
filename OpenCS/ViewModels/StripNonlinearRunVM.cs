using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using CScore.PlateStrip;
using OpenCS.Utilites;

namespace OpenCS.ViewModels;

/// <summary>Строка нагрузки в таблице постановки нелинейного прогона полосы.</summary>
public sealed class StripLoadRowVM : ViewModelBase
{
    public StripNonlinearLoadItem Item { get; }

    public StripLoadRowVM(StripNonlinearLoadItem item) => Item = item;

    public IReadOnlyList<string> KindNames { get; } =
    [
        Loc.S("StripRunLoadUniform"),
        Loc.S("StripRunLoadPoint")
    ];

    public int KindIndex
    {
        get => (int)Item.Kind;
        set
        {
            if (value < 0 || value > 1) return;
            Item.Kind = (StripNonlinearLoadKind)value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ToEnabled));
        }
    }

    public double FromFraction
    {
        get => Item.FromFraction;
        set { Item.FromFraction = value; OnPropertyChanged(); }
    }

    public double ToFraction
    {
        get => Item.ToFraction;
        set { Item.ToFraction = value; OnPropertyChanged(); }
    }

    public double Value
    {
        get => Item.Value;
        set { Item.Value = value; OnPropertyChanged(); }
    }

    /// <summary>Конец участка задаётся только для равномерной нагрузки.</summary>
    public bool ToEnabled => Item.Kind == StripNonlinearLoadKind.UniformQz;
}

/// <summary>Строка таблицы результатов нелинейного прогона по станциям.</summary>
public sealed record StripRunStationRow(
    double X, double N, double My, double Mz, double W, string FiberMy, string FiberW);

/// <summary>
/// Срез 8d: постановка и результат нелинейного прогона полосы на странице эквивалентного сечения.
/// Постановка (опоры, нагрузки, шаги, сверка) восстанавливается из последнего сохранённого результата.
/// </summary>
public sealed class StripNonlinearRunVM : ViewModelBase
{
    readonly EquivalentSection _equivalent;
    readonly AppViewModel _app;
    StripNonlinearRunResult? _latest;
    bool _isBusy;

    public StripNonlinearRunInput Input { get; private set; }
    public ObservableCollection<StripLoadRowVM> Loads { get; } = [];
    public ObservableCollection<StripRunStationRow> Rows { get; } = [];

    public IReadOnlyList<string> ConditionNames { get; } =
    [
        Loc.S("StripRunConditionPinned"),
        Loc.S("StripRunConditionFixed")
    ];

    public ICommand RunCommand { get; }
    public ICommand AddLoadCommand { get; }
    public ICommand RemoveLoadCommand { get; }

    public StripNonlinearRunVM(EquivalentSection equivalent, AppViewModel app)
    {
        _equivalent = equivalent ?? throw new ArgumentNullException(nameof(equivalent));
        _app = app ?? throw new ArgumentNullException(nameof(app));

        _latest = _app.db.LatestStripNonlinearResult(equivalent.Id);
        Input = _latest != null ? CloneInput(_latest.Input) : DefaultInput();
        foreach (var item in Input.Loads)
            Loads.Add(new StripLoadRowVM(item));

        RunCommand = new RelayCommand(async _ => await RunAsync(), _ => !IsBusy);
        AddLoadCommand = new RelayCommand(_ => AddLoad());
        RemoveLoadCommand = new RelayCommand(p =>
        {
            if (p is not StripLoadRowVM row) return;
            Input.Loads.Remove(row.Item);
            Loads.Remove(row);
        });

        ShowResult(_latest);
    }

    static StripNonlinearRunInput DefaultInput() => new()
    {
        Loads = [new StripNonlinearLoadItem
        {
            Kind = StripNonlinearLoadKind.UniformQz, FromFraction = 0.0, ToFraction = 1.0, Value = -10.0
        }]
    };

    /// <summary>Копия постановки: правка полей на странице не должна менять сохранённый результат.</summary>
    static StripNonlinearRunInput CloneInput(StripNonlinearRunInput source) => new()
    {
        StartCondition = source.StartCondition,
        EndCondition = source.EndCondition,
        AxialRestraint = source.AxialRestraint,
        LoadSteps = source.LoadSteps,
        ElementCount = source.ElementCount,
        FiberCheck = source.FiberCheck,
        Loads = source.Loads.Select(l => new StripNonlinearLoadItem
        {
            Kind = l.Kind, FromFraction = l.FromFraction, ToFraction = l.ToFraction, Value = l.Value
        }).ToList()
    };

    void AddLoad()
    {
        var item = new StripNonlinearLoadItem
        {
            Kind = StripNonlinearLoadKind.UniformQz, FromFraction = 0.0, ToFraction = 1.0, Value = -10.0
        };
        Input.Loads.Add(item);
        Loads.Add(new StripLoadRowVM(item));
    }

    // --- Постановка --------------------------------------------------------------------------

    public int StartConditionIndex
    {
        get => (int)Input.StartCondition;
        set { Input.StartCondition = (StripBeamEndCondition)value; OnPropertyChanged(); }
    }

    public int EndConditionIndex
    {
        get => (int)Input.EndCondition;
        set { Input.EndCondition = (StripBeamEndCondition)value; OnPropertyChanged(); }
    }

    public bool AxialBothEnds
    {
        get => Input.AxialRestraint == StripAxialRestraint.BothEnds;
        set
        {
            Input.AxialRestraint = value ? StripAxialRestraint.BothEnds : StripAxialRestraint.StartOnly;
            OnPropertyChanged();
        }
    }

    public int LoadSteps
    {
        get => Input.LoadSteps;
        set { Input.LoadSteps = value; OnPropertyChanged(); }
    }

    public int ElementCount
    {
        get => Input.ElementCount;
        set { Input.ElementCount = value; OnPropertyChanged(); }
    }

    public bool FiberCheck
    {
        get => Input.FiberCheck;
        set { Input.FiberCheck = value; OnPropertyChanged(); }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            _isBusy = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(BusyVisibility));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public Visibility BusyVisibility => IsBusy ? Visibility.Visible : Visibility.Collapsed;

    // --- Результат ---------------------------------------------------------------------------

    public string SummaryText { get; private set; } = Loc.S("StripRunNoResult");
    public string FiberText { get; private set; } = "";
    public string StatusText { get; private set; } = "";
    public Visibility StaleVisibility { get; private set; } = Visibility.Collapsed;
    public Visibility FiberVisibility { get; private set; } = Visibility.Collapsed;
    public Visibility ResultVisibility { get; private set; } = Visibility.Collapsed;
    public IReadOnlyList<CScore.Fem.FemValidationDiagnostic> Diagnostics { get; private set; } = [];

    async Task RunAsync()
    {
        string? invalid = Input.Validate();
        if (invalid != null)
        {
            StatusText = invalid;
            OnPropertyChanged(nameof(StatusText));
            return;
        }

        IsBusy = true;
        StatusText = Loc.S("StripRunRunning");
        OnPropertyChanged(nameof(StatusText));
        try
        {
            var result = await _app.RunStripNonlinearAsync(_equivalent, CloneInput(Input));
            if (result.Id != 0)
                _latest = result;
            ShowResult(result);
        }
        finally
        {
            IsBusy = false;
        }
    }

    void ShowResult(StripNonlinearRunResult? result)
    {
        Rows.Clear();
        var inv = CultureInfo.CurrentCulture;

        if (result == null)
        {
            SummaryText = Loc.S("StripRunNoResult");
            FiberText = "";
            StatusText = "";
            StaleVisibility = FiberVisibility = ResultVisibility = Visibility.Collapsed;
            Diagnostics = [];
        }
        else
        {
            bool hasData = result.BeamResultants.Length > 0;
            ResultVisibility = hasData ? Visibility.Visible : Visibility.Collapsed;
            Diagnostics = result.Diagnostics;

            if (hasData)
            {
                string state = result.IsConverged
                    ? Loc.S("StripRunConverged")
                    : string.Format(inv, Loc.S("StripRunNotConverged"), result.AchievedLoadFactor);
                double mMax = result.BeamResultants.Max(r => Math.Abs(r[1]));
                SummaryText = string.Format(
                    inv, Loc.S("StripRunSummary"),
                    state, result.TotalIterations, result.MaxDeflectionM * 1000.0, mMax);

                for (int i = 0; i < result.StationFractions.Length; i++)
                {
                    bool fiber = result.FiberChecked && i < result.FiberResultants.Length;
                    Rows.Add(new StripRunStationRow(
                        result.StationFractions[i] * result.LengthM,
                        result.BeamResultants[i][0], result.BeamResultants[i][1], result.BeamResultants[i][2],
                        result.BeamDeflections[i] * 1000.0,
                        fiber ? result.FiberResultants[i][1].ToString("G6", inv) : "—",
                        fiber ? (result.FiberDeflections[i] * 1000.0).ToString("G6", inv) : "—"));
                }
            }
            else
            {
                SummaryText = Loc.S("StripRunFailed");
            }

            bool fiberDone = result.FiberChecked && result.FiberMomentMismatch >= 0.0;
            FiberVisibility = fiberDone ? Visibility.Visible : Visibility.Collapsed;
            FiberText = fiberDone
                ? string.Format(inv, Loc.S("StripRunFiberSummary"),
                    result.FiberMomentMismatch * 100.0, result.FiberDeflectionMismatch * 100.0)
                : "";
            StaleVisibility = result.Id != 0 && result.IsStaleFor(_equivalent)
                ? Visibility.Visible : Visibility.Collapsed;
            StatusText = result.CreatedUtc == default
                ? ""
                : string.Format(inv, Loc.S("StripRunTimestamp"), result.CreatedUtc.ToLocalTime());
        }

        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(FiberText));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StaleVisibility));
        OnPropertyChanged(nameof(FiberVisibility));
        OnPropertyChanged(nameof(ResultVisibility));
        OnPropertyChanged(nameof(Diagnostics));
    }
}
