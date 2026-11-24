using CScore;
using CScore.Fire;
using CScore.Fire.Entities;
using OpenCS.Services;
using OpenCS.Tasks;
using OpenCS.Utilites;
using System.Collections;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace OpenCS.ViewModels;

/// <summary>
/// Страница огневого сечения: правка черновика (в модель и базу — только по «Сохранить» через
/// <see cref="FireSectionAuthoring"/>), «*» и вопрос при уходе, числа с запятой или точкой (нераспознанное — красная
/// рамка и отказ «Сохранить», а не молчаливое умолчание), шаг сетки в мм, ручные ГУ рёбер на черновике, тепловой
/// расчёт через <see cref="FireThermalRunner"/> (сначала сохранение).
/// </summary>
public sealed class FireSectionVM : ViewModelBase, IUnsavedChanges, INotifyDataErrorInfo
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    readonly FireSectionDef _model;
    readonly AppViewModel _app;
    FireSectionDef _draft;
    FireSectionDef _saved;

    /// <summary>Ошибки разбора текста полей: свойство → сообщение (поле не меняет черновик).</summary>
    readonly Dictionary<string, string> _parseErrors = [];
    /// <summary>Ошибки проверки службой после «Сохранить»: свойство → сообщения.</summary>
    readonly Dictionary<string, List<string>> _validationErrors = [];

    string _durationText = "", _meshStepText = "", _smoothingText = "", _timeStepText = "", _snapshotStepText = "",
           _thetaText = "", _picardTolText = "", _picardIterText = "";
    string _statusText = "", _errorText = "", _thermalStatusText = "", _meshWarningText = "";
    bool _thermalStatusIsProblem, _thermalRunning, _hasThermalResult;
    int _thermalLoadToken;

    public FireSectionVM(FireSectionDef model, AppViewModel app)
    {
        _model = model;
        _app = app;
        _draft = FireSectionAuthoring.Clone(model);
        _saved = FireSectionAuthoring.Clone(model);
        // Пригодные для теплового расчёта сечения; текущее — всегда в списке (ошибка покажется у поля при сохранении).
        Sections = app.CrossSections.Where(s => s.Id == model.SectionId
            || FireSectionAuthoring.HasConcreteContour(s, app.Materials)).ToList();
        Preview = new FirePreviewVM();
        Preview.EdgeBcChanged += () => { Touch(); NotifyEdges(); };
        ThermalResult = FireThermalResultVM.CreateLoading(_model);
        SaveCommand = new RelayCommand(_ => Save());
        RunThermalCommand = new RelayCommand(_ => _ = RunThermalAsync(), _ => !ThermalRunning);
        DeleteThermalCommand = new RelayCommand(_ => DeleteThermal(), _ => _hasThermalResult && !ThermalRunning);
        ResetEdgesCommand = new RelayCommand(_ => ResetEdges(), _ => _draft.Edges.Count > 0);
        LoadTexts();
        ReloadThermalState();
        StartLoadThermalResult();
    }

    // ── Данные ──────────────────────────────────────────────────────────────────────────────

    /// <summary>Записанное огневое сечение проекта.</summary>
    public FireSectionDef Model => _model;
    public AppViewModel App => _app;
    /// <summary>Черновик (для превью и тестов).</summary>
    internal FireSectionDef Draft => _draft;

    public FirePreviewVM Preview { get; }
    public FireThermalResultVM ThermalResult { get; private set; }

    /// <summary>Сечения, пригодные для теплового расчёта (без стальных).</summary>
    public IReadOnlyList<CrossSection> Sections { get; }

    public ICommand SaveCommand { get; }
    public ICommand RunThermalCommand { get; }
    public ICommand DeleteThermalCommand { get; }
    public ICommand ResetEdgesCommand { get; }

    /// <summary>Вопрос «Да/Нет» (через <see cref="IUserNotifier"/> приложения — в тестах записывающий).</summary>
    bool Confirm(string text) => _app.Notifier.Confirm(text, Loc.S("Confirmation"));

    public string Tag
    {
        get => _draft.Tag;
        set { _draft.Tag = value ?? ""; OnPropertyChanged(); Touch(); }
    }

    public CrossSection? SelectedSection
    {
        get => Sections.FirstOrDefault(s => s.Id == _draft.SectionId);
        set
        {
            if (value == null || value.Id == _draft.SectionId) return;
            _draft.SectionId = value.Id;
            if (_draft.Edges.Count > 0)
            {
                // Индексы ручных ГУ относятся к контуру прежнего сечения.
                _draft.Edges = [];
                StatusText = Loc.S("FireSection_EdgesResetOnSection");
            }
            OnPropertyChanged();
            OnPropertyChanged(nameof(ManualEdgeCount));
            OnPropertyChanged(nameof(HasManualEdges));
            OnPropertyChanged(nameof(AggregateFromSectionText));
            Touch();
            RefreshPreview();
            // Габарит другого сечения — вписать превью заново.
            Preview.FitAllCommand.Execute(null);
        }
    }

    /// <summary>Смена языка: тексты, собранные в коде (состояние расчёта, счётчик, подсказка заполнителя).</summary>
    public void OnLanguageChanged()
    {
        ReloadThermalState();
        NotifyEdges();
        OnPropertyChanged(nameof(AggregateFromSectionText));
        RefreshPreview();
    }

    public string FireDurationText { get => _durationText; set => SetNumber(ref _durationText, value, v => _draft.FireDurationMin = v); }
    public string MeshStepMmText
    {
        get => _meshStepText;
        set { SetNumber(ref _meshStepText, value, v => _draft.MeshStepM = v / 1000.0); RefreshPreview(); }
    }
    public string SmoothingText { get => _smoothingText; set => SetInt(ref _smoothingText, value, v => _draft.SmoothIterTri = v); }
    public string TimeStepText { get => _timeStepText; set => SetNumber(ref _timeStepText, value, v => _draft.TimeStepS = v); }
    public string SnapshotStepText { get => _snapshotStepText; set => SetNumber(ref _snapshotStepText, value, v => _draft.SnapshotStepMin = v); }
    public string ThetaText { get => _thetaText; set => SetNumber(ref _thetaText, value, v => _draft.Theta = v); }
    public string PicardTolText { get => _picardTolText; set => SetNumber(ref _picardTolText, value, v => _draft.PicardTolCelsius = v); }
    public string PicardMaxIterText { get => _picardIterText; set => SetInt(ref _picardIterText, value, v => _draft.PicardMaxIter = v); }

    public string FireCurve
    {
        get => _draft.FireCurve;
        set { if (value == null) return; _draft.FireCurve = value; OnPropertyChanged(); Touch(); }
    }

    /// <summary>Тип заполнителя; пусто — из бетона сечения.</summary>
    public string AggregateType
    {
        get => _draft.AggregateType ?? "";
        set { _draft.AggregateType = value ?? ""; OnPropertyChanged(); Touch(); }
    }

    /// <summary>Подсказка: какой заполнитель возьмётся из бетона сечения.</summary>
    public string AggregateFromSectionText
    {
        get
        {
            var s = _app.CrossSections.FirstOrDefault(x => x.Id == _draft.SectionId);
            string agg = s == null ? "silicate" : FireSectionAuthoring.SectionAggregate(s, _app.Materials);
            return string.Format(Loc.S("FireSection_AggregateFromSection"), Loc.S(AggregateKey(agg)));
        }
    }

    static string AggregateKey(string agg) => agg switch
    {
        "carbonate" => "Material_AggregateType_Carbonate",
        "lightweight" => "Material_AggregateType_Lightweight",
        _ => "Material_AggregateType_Silicate",
    };

    public string BcPreset
    {
        get => _draft.BcPreset;
        set { if (value == null) return; _draft.BcPreset = value; OnPropertyChanged(); Touch(); RefreshPreview(); }
    }

    public string HoleBcPreset
    {
        get => _draft.HoleBcPreset;
        set { if (value == null) return; _draft.HoleBcPreset = value; OnPropertyChanged(); Touch(); RefreshPreview(); }
    }

    public string Algorithm
    {
        get => _draft.Algorithm;
        set { if (value == null) return; _draft.Algorithm = value; OnPropertyChanged(); Touch(); RefreshPreview(); }
    }

    /// <summary>Число рёбер с ручными ГУ (действуют поверх шаблона).</summary>
    public int ManualEdgeCount => _draft.Edges.Count;
    public bool HasManualEdges => _draft.Edges.Count > 0;
    public string ManualEdgesText => string.Format(Loc.S("FireSection_ManualEdgesCount"), _draft.Edges.Count);

    void NotifyEdges()
    {
        OnPropertyChanged(nameof(ManualEdgeCount));
        OnPropertyChanged(nameof(HasManualEdges));
        OnPropertyChanged(nameof(ManualEdgesText));
    }

    /// <summary>Строка состояния: «Сохранено», «Ручные ГУ сброшены», результат запуска.</summary>
    public string StatusText { get => _statusText; private set { _statusText = value; OnPropertyChanged(); } }

    /// <summary>Ошибки ввода и проверки (под формой).</summary>
    public string ErrorText
    {
        get => _errorText;
        private set { _errorText = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasErrorText)); }
    }
    public bool HasErrorText => _errorText.Length > 0;

    public bool ThermalRunning
    {
        get => _thermalRunning;
        private set { _thermalRunning = value; OnPropertyChanged(); CommandManager.InvalidateRequerySuggested(); }
    }

    public string ThermalStatusText { get => _thermalStatusText; private set { _thermalStatusText = value; OnPropertyChanged(); } }
    public bool ThermalStatusIsProblem { get => _thermalStatusIsProblem; private set { _thermalStatusIsProblem = value; OnPropertyChanged(); } }

    public string MeshWarningText
    {
        get => _meshWarningText;
        private set { _meshWarningText = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasMeshWarning)); }
    }
    public bool HasMeshWarning => _meshWarningText.Length > 0;

    // ── Тексты полей ────────────────────────────────────────────────────────────────────────

    static string F(double v) => v.ToString("G6", Inv);

    void LoadTexts()
    {
        _parseErrors.Clear();
        _validationErrors.Clear();
        _durationText = F(_draft.FireDurationMin);
        _meshStepText = F(Math.Round(_draft.MeshStepM * 1000.0, 6));
        _smoothingText = _draft.SmoothIterTri.ToString(Inv);
        _timeStepText = F(_draft.TimeStepS);
        _snapshotStepText = F(_draft.SnapshotStepMin);
        _thetaText = F(_draft.Theta);
        _picardTolText = F(_draft.PicardTolCelsius);
        _picardIterText = _draft.PicardMaxIter.ToString(Inv);
        ErrorText = "";
        foreach (var p in ErrorProps) ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(p));
    }

    static readonly string[] ErrorProps =
    [
        nameof(Tag), nameof(SelectedSection), nameof(FireDurationText), nameof(FireCurve), nameof(AggregateType),
        nameof(MeshStepMmText), nameof(Algorithm), nameof(SmoothingText), nameof(TimeStepText), nameof(SnapshotStepText),
        nameof(ThetaText), nameof(PicardTolText), nameof(PicardMaxIterText), nameof(BcPreset), nameof(HoleBcPreset),
    ];

    void SetNumber(ref string field, string? value, Action<double> apply, [System.Runtime.CompilerServices.CallerMemberName] string prop = "")
    {
        field = value ?? "";
        if (NumberInput.TryParse(field, out double v)) { _parseErrors.Remove(prop); apply(v); }
        else _parseErrors[prop] = Loc.S("FireErrNotNumber");
        ClearValidation(prop);
        OnPropertyChanged(prop);
        Touch();
    }

    void SetInt(ref string field, string? value, Action<int> apply, [System.Runtime.CompilerServices.CallerMemberName] string prop = "")
    {
        field = value ?? "";
        if (int.TryParse(field.Trim(), NumberStyles.Integer, Inv, out int v)) { _parseErrors.Remove(prop); apply(v); }
        else _parseErrors[prop] = Loc.S("FireErrNotInteger");
        ClearValidation(prop);
        OnPropertyChanged(prop);
        Touch();
    }

    void ClearValidation(string prop)
    {
        _validationErrors.Remove(prop);
        ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(prop));
    }

    /// <summary>Путь ошибки службы → свойство формы.</summary>
    static string PropOf(string path) => path switch
    {
        "tag" => nameof(Tag),
        "section" => nameof(SelectedSection),
        "fire.durationMin" => nameof(FireDurationText),
        "fire.curve" => nameof(FireCurve),
        "fire.aggregate" => nameof(AggregateType),
        "mesh.stepMm" => nameof(MeshStepMmText),
        "mesh.algorithm" => nameof(Algorithm),
        "mesh.smoothing" => nameof(SmoothingText),
        "time.stepS" => nameof(TimeStepText),
        "time.snapshotStepMin" => nameof(SnapshotStepText),
        "time.theta" => nameof(ThetaText),
        "time.picardTol" => nameof(PicardTolText),
        "time.picardMaxIter" => nameof(PicardMaxIterText),
        "bc.preset" => nameof(BcPreset),
        "bc.holes" => nameof(HoleBcPreset),
        _ => nameof(ManualEdgeCount),
    };

    /// <summary>Подпись поля для сообщения об ошибке разбора.</summary>
    static string LabelOf(string prop) => Loc.S(prop switch
    {
        nameof(FireDurationText) => "FireSection_Duration",
        nameof(MeshStepMmText) => "FireSection_MeshStep",
        nameof(SmoothingText) => "FireSection_Smoothing",
        nameof(TimeStepText) => "FireSection_TimeStep",
        nameof(SnapshotStepText) => "FireSection_SnapshotStep",
        nameof(ThetaText) => "FireSection_Theta",
        nameof(PicardTolText) => "FireSection_PicardTol",
        nameof(PicardMaxIterText) => "FireSection_PicardMaxIter",
        _ => "Tag",
    });

    // ── INotifyDataErrorInfo ────────────────────────────────────────────────────────────────

    public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;
    public bool HasErrors => _parseErrors.Count > 0 || _validationErrors.Count > 0;

    public IEnumerable GetErrors(string? propertyName)
    {
        if (propertyName == null) return Array.Empty<string>();
        var list = new List<string>();
        if (_parseErrors.TryGetValue(propertyName, out var p)) list.Add(p);
        if (_validationErrors.TryGetValue(propertyName, out var v)) list.AddRange(v);
        return list;
    }

    // ── IUnsavedChanges ─────────────────────────────────────────────────────────────────────

    public bool IsDirty => _parseErrors.Count > 0 || !FireSectionAuthoring.SameContent(_draft, _saved);

    public string DisplayName => !string.IsNullOrWhiteSpace(_draft.Tag) ? _draft.Tag : _model.Tag;

    void Touch() => OnPropertyChanged(nameof(IsDirty));

    bool IUnsavedChanges.SaveChanges() => Save();

    /// <summary>Черновик — заново из записанного огневого сечения.</summary>
    public void DiscardChanges()
    {
        _draft = FireSectionAuthoring.Clone(_model);
        _saved = FireSectionAuthoring.Clone(_model);
        LoadTexts();
        StatusText = "";
        OnPropertyChanged(string.Empty);
        RefreshPreview();
    }

    // ── Сохранение ──────────────────────────────────────────────────────────────────────────

    /// <summary>Проверяет черновик и записывает его; ошибки — у полей и под формой, модель и база не меняются.</summary>
    public bool Save()
    {
        _validationErrors.Clear();
        foreach (var p in ErrorProps) ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(p));
        var messages = _parseErrors.Select(kv => $"{LabelOf(kv.Key)}: {kv.Value}").ToList();
        if (messages.Count == 0)
        {
            foreach (var e in FireSectionAuthoring.Validate(_app.db, _draft))
            {
                string prop = PropOf(e.Path);
                if (!_validationErrors.TryGetValue(prop, out var list)) _validationErrors[prop] = list = [];
                list.Add(e.Message);
                ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(prop));
                messages.Add(e.Message);
            }
        }
        if (messages.Count > 0)
        {
            ErrorText = string.Join("\n", messages.Distinct());
            foreach (var m in messages.Distinct())
                _app.LogService.Error(string.Format(Loc.S("FireSection_SaveErrorLog"), DisplayName, m));
            return false;
        }

        if (_draft.SectionId != _model.SectionId)
        {
            var (tasks, results) = FireSectionAuthoring.SectionChangeImpact(_app.db, _model);
            var section = _app.CrossSections.FirstOrDefault(s => s.Id == _draft.SectionId);
            if (tasks.Count > 0 && !Confirm(string.Format(Loc.S("FireSection_ConfirmSectionChange"),
                    tasks.Count, section?.Tag ?? "", results)))
                return false;
        }

        var errors = FireSectionAuthoring.Update(_app.db, _model, _draft);
        if (errors.Count > 0)   // не должно случиться: черновик проверен тем же Validate
        {
            ErrorText = string.Join("\n", errors.Select(e => e.Message));
            return false;
        }
        _saved = FireSectionAuthoring.Clone(_model);
        ErrorText = "";
        StatusText = Loc.S("FireSection_Saved");
        _app.RefreshFireSectionsView(_model);
        _app.LogService.Info(string.Format(Loc.S("FireSection_SavedLog"), _model.Tag));
        Touch();
        ReloadThermalState();
        return true;
    }

    // ── Превью ──────────────────────────────────────────────────────────────────────────────

    public void RefreshPreview()
    {
        var section = _app.CrossSections.FirstOrDefault(s => s.Id == _draft.SectionId);
        Preview.Configure(_draft, section, _draft.BcPreset, _draft.MeshStepM > 0 ? _draft.MeshStepM : 0.01,
            _draft.Algorithm, _draft.SmoothIterTri, _draft.MeshElementType);
        OnPropertyChanged(nameof(ManualEdgesText));
        MeshWarningText = section != null && _draft.MeshStepM > 0
            ? FireThermalRunner.MeshRangeWarning(section, _draft.MeshStepM) ?? "" : "";
    }

    void ResetEdges()
    {
        _draft.Edges = [];
        StatusText = Loc.S("FireSection_EdgesReset");
        OnPropertyChanged(nameof(ManualEdgeCount));
        OnPropertyChanged(nameof(HasManualEdges));
        Touch();
        RefreshPreview();
    }

    // ── Тепловой расчёт ─────────────────────────────────────────────────────────────────────

    /// <summary>Сохраняет (при правках) и выполняет тепловой расчёт; ошибки — в строке состояния и журнале.</summary>
    public async Task RunThermalAsync()
    {
        if (IsDirty && !Save()) return;
        var section = _app.CrossSections.FirstOrDefault(s => s.Id == _model.SectionId);
        if (section == null)
        {
            StatusText = Loc.S("FireSection_SectionNotFound");
            _app.LogService.Error(StatusText);
            return;
        }
        var check = FireThermalRunner.Check(_model, section);
        foreach (var w in check.Warnings) _app.LogService.Warning(w);
        if (check.Error != null)
        {
            StatusText = check.Error;
            _app.LogService.Error(check.Error);
            return;
        }

        ThermalRunning = true;
        StatusText = Loc.S("FireThermal_Running");
        try
        {
            await Task.Run(() => FireThermalRunner.Run(_app.db, _model, section)).ConfigureAwait(true);
            ReloadThermalState();
            RefreshThermalResult();
            StatusText = Loc.S("FireSection_RunOk");
            _app.LogService.Info(string.Format(Loc.S("FireSection_RunOkLog"), _model.Tag));
        }
        catch (Exception ex)
        {
            StatusText = string.Format(Loc.S("FireSection_RunError"), ex.Message);
            _app.LogService.Error(StatusText);
        }
        finally
        {
            ThermalRunning = false;
        }
    }

    /// <summary>Перечитать состояние теплового расчёта: нет, актуален, устарел (по записанным данным).</summary>
    internal void ReloadThermalState()
    {
        var section = _app.CrossSections.FirstOrDefault(s => s.Id == _model.SectionId);
        var info = _model.Id > 0 ? _app.db.GetFireThermalResultInfo(_model.Id) : null;
        _hasThermalResult = info is not null;
        CommandManager.InvalidateRequerySuggested();
        if (info is null)
        {
            ThermalStatusText = Loc.S("FireThermal_StatusNone");
            ThermalStatusIsProblem = true;
            return;
        }
        string? stale = FireThermalRunner.State(_app.db, _model, section).StaleReason;
        string duration = info.DurationMin?.ToString("F0", Inv) ?? "—";
        string snapshots = info.SnapshotCount?.ToString(Inv) ?? "—";
        ThermalStatusText = stale is null
            ? string.Format(Loc.S("FireThermal_StatusCurrent"), info.Created, duration, snapshots)
            : string.Format(Loc.S("FireThermal_StatusStale"), info.Created, duration, snapshots, stale);
        ThermalStatusIsProblem = stale is not null;
    }

    void DeleteThermal()
    {
        _app.db.DeleteFireThermalResultForSection(_model.Id);
        ReloadThermalState();
        RefreshThermalResult();
    }

    void RefreshThermalResult()
    {
        ThermalResult = FireThermalResultVM.CreateLoading(_model);
        OnPropertyChanged(nameof(ThermalResult));
        StartLoadThermalResult();
    }

    void StartLoadThermalResult()
    {
        if (_model.Id <= 0 || Application.Current == null)
        {
            ThermalResult = new FireThermalResultVM(_model, null, null);
            OnPropertyChanged(nameof(ThermalResult));
            return;
        }
        int token = ++_thermalLoadToken;
        Task.Run<(FireThermalResult? thermal, int? rid)>(() =>
        {
            try
            {
                int? rid = _app.db.GetFireThermalResultInfo(_model.Id)?.Id;
                return rid is null ? (null, null) : (_app.db.LoadFireThermalResult(rid.Value), rid);
            }
            catch
            {
                return (null, null);
            }
        }).ContinueWith(t =>
        {
            Application.Current?.Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
            {
                if (token != _thermalLoadToken) return;
                var (thermal, rid) = t.Result;
                ThermalResult = new FireThermalResultVM(_model, thermal, rid);
                OnPropertyChanged(nameof(ThermalResult));
            });
        });
    }
}
