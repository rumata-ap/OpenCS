using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using CScore.PlateStrip;
using OpenCS.Services;
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
    StripNonlinearRunResult? _shown;
    bool _isBusy;
    int _diagramQuantity;
    int _selectedStationIndex = -1;
    StripLoadSource? _selectedLoadSource;

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
    public ICommand TransferLoadsCommand { get; }

    /// <summary>Загружения и сочетания схемы, из которых переносятся нагрузки.</summary>
    public IReadOnlyList<StripLoadSource> LoadSources { get; }

    /// <summary>В схеме есть загружения — доступен перенос нагрузок.</summary>
    public Visibility TransferVisibility => LoadSources.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Выбранный источник нагрузок.</summary>
    public StripLoadSource? SelectedLoadSource
    {
        get => _selectedLoadSource;
        set { _selectedLoadSource = value; OnPropertyChanged(); CommandManager.InvalidateRequerySuggested(); }
    }

    /// <summary>Итог последнего переноса нагрузок.</summary>
    public string TransferText { get; private set; } = "";

    /// <summary>Замечания последнего переноса нагрузок.</summary>
    public IReadOnlyList<CScore.Fem.FemValidationDiagnostic> TransferDiagnostics { get; private set; } = [];

    public StripNonlinearRunVM(EquivalentSection equivalent, AppViewModel app)
    {
        _equivalent = equivalent ?? throw new ArgumentNullException(nameof(equivalent));
        _app = app ?? throw new ArgumentNullException(nameof(app));

        _latest = _app.db.LatestStripNonlinearResult(equivalent.Id);
        Input = _latest != null ? CloneInput(_latest.Input) : DefaultInput();
        foreach (var item in Input.Loads)
            Loads.Add(new StripLoadRowVM(item));

        LoadSources = StripSchemaLoadService.Sources(_app.db, equivalent.SourceSchemaId);
        _selectedLoadSource = LoadSources.FirstOrDefault();
        TransferLoadsCommand = new RelayCommand(async _ => await TransferLoadsAsync(),
            _ => !IsBusy && SelectedLoadSource != null);

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

    // --- Перенос нагрузок схемы ---------------------------------------------------------------

    async Task TransferLoadsAsync()
    {
        if (SelectedLoadSource is not { } source) return;

        IsBusy = true;
        TransferText = Loc.S("StripRunTransferring");
        TransferDiagnostics = [];
        OnPropertyChanged(nameof(TransferText));
        OnPropertyChanged(nameof(TransferDiagnostics));
        try
        {
            int intervals = Math.Clamp(Input.ElementCount, 1, 200);
            var result = await Task.Run(() => StripSchemaLoadService.Transfer(_app.db, _equivalent, source, intervals));

            if (result.Loads.Count > 0)
            {
                Input.Loads.Clear();
                Loads.Clear();
                foreach (var item in result.Loads)
                {
                    Input.Loads.Add(item);
                    Loads.Add(new StripLoadRowVM(item));
                }
                TransferText = string.Format(CultureInfo.CurrentCulture, Loc.S("StripRunTransferDone"), result.Loads.Count);
            }
            else
            {
                TransferText = Loc.S("StripRunTransferEmpty");
            }
            TransferDiagnostics = result.Diagnostics;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or System.IO.IOException)
        {
            TransferText = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
        OnPropertyChanged(nameof(TransferText));
        OnPropertyChanged(nameof(TransferDiagnostics));
    }

    // --- Эпюра -------------------------------------------------------------------------------

    /// <summary>Величины эпюры в порядке <see cref="StripRunDiagramQuantity"/>.</summary>
    public IReadOnlyList<string> DiagramQuantityNames { get; } =
    [
        Loc.S("StripRunDiagramMy"),
        Loc.S("StripRunDiagramN"),
        Loc.S("StripRunDiagramMz"),
        Loc.S("StripRunDiagramW")
    ];

    public int DiagramQuantityIndex
    {
        get => _diagramQuantity;
        set
        {
            if (value < 0 || value >= DiagramQuantityNames.Count || value == _diagramQuantity) return;
            _diagramQuantity = value;
            OnPropertyChanged();
            RefreshDiagram();
        }
    }

    /// <summary>Строка таблицы станций, выбранная в таблице или щелчком по точке эпюры.</summary>
    public int SelectedStationIndex
    {
        get => _selectedStationIndex;
        set
        {
            if (value == _selectedStationIndex) return;
            _selectedStationIndex = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Эпюра собственной балки CScore.</summary>
    public FemDiagramSeries DiagramPrimary { get; private set; } = FemDiagramSeries.Empty;

    /// <summary>Эпюра фибрового стержня OpenSees; пуста, если сверки не было.</summary>
    public FemDiagramSeries DiagramFiber { get; private set; } = FemDiagramSeries.Empty;

    /// <summary>Положения станций, м, — маркеры эпюры.</summary>
    public IReadOnlyList<double> StationPositions { get; private set; } = [];

    public string DiagramTitle { get; private set; } = "";
    public string DiagramPeakText { get; private set; } = "";
    public string DiagramNote { get; } = Loc.S("StripRunDiagramNote");

    /// <summary>Эпюра или её настройки изменились — холст перерисовывает ряды.</summary>
    public event EventHandler? DiagramChanged;

    void RefreshDiagram()
    {
        var result = _shown;
        var quantity = (StripRunDiagramQuantity)_diagramQuantity;
        bool hasData = result != null && result.BeamResultants.Length > 0;

        if (!hasData)
        {
            DiagramPrimary = DiagramFiber = FemDiagramSeries.Empty;
            StationPositions = [];
            DiagramTitle = DiagramPeakText = "";
        }
        else
        {
            // Нормаль полосы вниз — «вверх» на эпюре должен быть вверх в проекте: знак My и w меняется.
            bool flip = _equivalent.Strip.StripFrame.LocalZ.Z < -1e-9;
            double length = result!.LengthM;
            DiagramPrimary = StripRunDiagramBuilder.Build(
                result.StationFractions, length, result.BeamResultants, result.BeamDeflections, quantity, flip);

            bool fiber = result.FiberChecked && result.FiberResultants.Length == result.StationFractions.Length &&
                         result.FiberDeflections.Length == result.StationFractions.Length;
            DiagramFiber = fiber
                ? StripRunDiagramBuilder.Build(
                    result.StationFractions, length, result.FiberResultants, result.FiberDeflections, quantity, flip)
                : FemDiagramSeries.Empty;
            StationPositions = result.StationFractions.Select(f => f * length).ToList();

            var inv = CultureInfo.CurrentCulture;
            string unit = quantity switch
            {
                StripRunDiagramQuantity.N => Loc.S("UnitKN"),
                StripRunDiagramQuantity.W => Loc.S("StripRunUnitMm"),
                _ => Loc.S("UnitKNm")
            };
            string name = quantity switch
            {
                StripRunDiagramQuantity.N => "N",
                StripRunDiagramQuantity.Mz => "Mz",
                StripRunDiagramQuantity.W => "w",
                _ => "My"
            };
            DiagramTitle = string.Format(inv, Loc.S("StripRunDiagramTitle"), $"{name}, {unit}", length)
                           + (DiagramFiber.Segments.Count > 0 ? Loc.S("StripRunDiagramFiberLegend") : "");

            var min = DiagramPrimary.Extrema.FirstOrDefault(e => !e.IsMaximum);
            var max = DiagramPrimary.Extrema.FirstOrDefault(e => e.IsMaximum);
            DiagramPeakText = min != null && max != null
                ? string.Format(inv, Loc.S("StripRunDiagramPeak"), unit, min.Value, min.Position, max.Value, max.Position)
                : "";
        }

        OnPropertyChanged(nameof(DiagramPrimary));
        OnPropertyChanged(nameof(DiagramFiber));
        OnPropertyChanged(nameof(StationPositions));
        OnPropertyChanged(nameof(DiagramTitle));
        OnPropertyChanged(nameof(DiagramPeakText));
        DiagramChanged?.Invoke(this, EventArgs.Empty);
    }

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

        _shown = result;
        SelectedStationIndex = -1;
        RefreshDiagram();
    }
}
