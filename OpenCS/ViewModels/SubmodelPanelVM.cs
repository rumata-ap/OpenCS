using System.Globalization;
using System.Windows.Input;
using CScore.Fem;
using CScore.Submodel;
using OpenCS.Services;
using OpenCS.Tasks;
using OpenCS.Utilites;

namespace OpenCS.ViewModels;

/// <summary>
/// То, что вкладке «Субмодель» нужно от приложения: индикатор занятости, диалоги и пересоздание страницы.
/// В приложении — страница схемы поверх <see cref="AppViewModel"/>, в тестах — заглушка.
/// </summary>
public interface ISubmodelUiHost
{
    /// <summary>Включает индикатор занятости с отменой; токен источника отменяет расчёт.</summary>
    CancellationTokenSource BeginBusy(string message);
    void EndBusy(string? message = null);
    /// <summary>true, если несохранённых правок схемы нет или пользователь согласен их потерять.</summary>
    bool ConfirmDiscardUnsavedEdits();
    /// <summary>Пересоздаёт страницу схемы (новая сессия редактора видит новый конструктивный слой) и обновляет дерево.</summary>
    void ReloadSchemaPage();
    /// <summary>Параметры нелинейного расчёта субмодели; null — пользователь отказался.</summary>
    FemAnalysisParams? EditNonlinearParameters(FemAnalysis? existing, FemAnalysisParams? current);
    void Log(FemValidationDiagnostic diagnostic);
}

/// <summary>
/// Вкладка «Субмодель» дочерней схемы: происхождение и конвейер «сценарий → материализация → линейная сверка →
/// нелинейный расчёт и сверка». Состояние восстанавливается из БД без пересчёта (<see cref="Refresh"/>);
/// расчётную логику выполняют сервисы, VM только показывает их статус и диагностику.
/// </summary>
public sealed class SubmodelPanelVM : ViewModelBase
{
    readonly int _schemaId;
    readonly DatabaseService _db;
    readonly ISubmodelAnalysisRunner _runner;
    readonly Func<CalcSettings> _settings;
    readonly ISubmodelUiHost _host;
    readonly StraightBeamBoundaryScenarioService _scenarioService;
    readonly StraightBeamSubmodelMaterializationService _materializationService;
    readonly StraightBeamSubmodelNonlinearService _nonlinearService;

    bool _isRunning;
    FemAnalysisParams? _nonlinearParams;

    public SubmodelPanelVM(int schemaId, DatabaseService db, ISubmodelAnalysisRunner runner,
        Func<CalcSettings> settings, ISubmodelUiHost host)
    {
        _schemaId = schemaId;
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _scenarioService = new StraightBeamBoundaryScenarioService(db);
        _materializationService = new StraightBeamSubmodelMaterializationService(db);
        _nonlinearService = new StraightBeamSubmodelNonlinearService(db);

        BuildScenarioCommand = new RelayCommand(_ => BuildScenario(), _ => CanBuildScenario);
        MaterializeCommand = new RelayCommand(_ => Materialize(), _ => CanMaterialize);
        RunLinearCommand = new RelayCommand(async _ => await RunLinearAsync(), _ => CanVerify);
        EditNonlinearParamsCommand = new RelayCommand(_ => EditNonlinearParams(), _ => CanVerify);
        RunNonlinearCommand = new RelayCommand(async _ => await RunNonlinearAsync(), _ => CanVerify);
        Refresh();
    }

    public ICommand BuildScenarioCommand { get; }
    public ICommand MaterializeCommand { get; }
    public ICommand RunLinearCommand { get; }
    public ICommand EditNonlinearParamsCommand { get; }
    public ICommand RunNonlinearCommand { get; }

    public bool CanBuildScenario => !_isRunning && Extraction is not null;
    public bool CanMaterialize => !_isRunning && Scenario is { Status: not ScenarioStatus.Blocked };
    public bool CanVerify => !_isRunning && IsMaterializationCurrent;

    // ---------- Состояние ----------

    public SubmodelExtraction? Extraction { get; private set; }
    public SubmodelBoundaryScenario? Scenario { get; private set; }
    /// <summary>Материализация текущего сценария (перестроение сценария её удаляет).</summary>
    public SubmodelMaterialization? Materialization { get; private set; }
    public bool IsMaterializationCurrent => Materialization is not null;
    public SubmodelVerificationReport? LinearReport { get; private set; }
    public SubmodelNonlinearVerificationReport? NonlinearReport { get; private set; }
    /// <summary>Итог предварительной линейной проверки последнего нелинейного запуска в этом сеансе.
    /// Не восстанавливается из БД: сохранённый линейный результат мог быть получен и отдельной кнопкой.</summary>
    public string? PrecheckText { get; private set; }

    public IReadOnlyList<FemValidationDiagnostic> ScenarioDiagnostics { get; private set; } = [];
    public IReadOnlyList<FemValidationDiagnostic> MaterializationDiagnostics { get; private set; } = [];
    public IReadOnlyList<FemValidationDiagnostic> LinearDiagnostics { get; private set; } = [];
    public IReadOnlyList<FemValidationDiagnostic> NonlinearDiagnostics { get; private set; } = [];

    // ---------- Шапка ----------

    public string ParentSchemaTag => Extraction is null ? "" :
        _db.FemSchemas.FirstOrDefault(s => s.Id == Extraction.ParentSchemaId)?.Tag ?? $"#{Extraction.ParentSchemaId}";
    public string ParentAnalysisText => Extraction is null ? "" :
        string.Format(Loc.S("SubmodelParentAnalysisValue"),
            _db.GetFemAnalyses(Extraction.ParentSchemaId).FirstOrDefault(a => a.Id == Extraction.ParentAnalysisId)?.Tag
                ?? $"#{Extraction.ParentAnalysisId}",
            Extraction.ParentResultId);
    public int SegmentCount => Extraction?.Segments.Count ?? 0;
    public string ChainLengthText => Extraction is { Segments.Count: > 0 } e
        ? Meters(e.Segments.Max(s => s.EndStationM) - e.Segments.Min(s => s.StartStationM)) : "";
    public string MaxOffsetText => Extraction?.Metrics.MaxNodeOffsetFromAxis is { } p
        ? $"{SubmodelReportRows.Number(p.Value * 1000)} {Loc.S("SubmodelUnitMm")}" : SubmodelReportRows.Dash;
    public string MaxAngleText => Extraction?.Metrics.MaxSegmentAngleDeg is { } p
        ? $"{SubmodelReportRows.Number(p.Value)}°" : SubmodelReportRows.Dash;
    public IReadOnlyList<FemValidationDiagnostic> ExtractionDiagnostics => Extraction?.Diagnostics ?? [];

    static string Meters(double value) => $"{value.ToString("0.###", CultureInfo.CurrentCulture)} {Loc.S("SubmodelUnitM")}";

    // ---------- Шаг 1. Сценарий ----------

    public string ScenarioStatusText => Scenario is null
        ? Loc.S("SubmodelScenarioNone")
        : string.Format(Loc.S("SubmodelScenarioStatus"),
            Loc.S(Scenario.Status switch
            {
                ScenarioStatus.Complete => "SubmodelScenarioComplete",
                ScenarioStatus.Incomplete => "SubmodelScenarioIncomplete",
                _ => "SubmodelScenarioBlocked"
            }),
            Loc.S(Scenario.LoadCompleteness == LoadCompleteness.Known ? "SubmodelLoadsKnown" : "SubmodelLoadsUnknown"));
    public bool ScenarioHasError => Scenario is null || Scenario.Status == ScenarioStatus.Blocked;
    public string LoadAccountingText => Scenario?.Scenario.LoadAccounting is { } a
        ? string.Format(Loc.S("SubmodelLoadAccounting"), a.Retained, a.Boundary, a.Discarded) : "";
    public IReadOnlyList<ScenarioDofRow> ScenarioRows { get; private set; } = [];
    public IReadOnlyList<ScenarioEndRow> EndRows { get; private set; } = [];
    public IReadOnlyList<DofOverrideOption> ChoiceOptions { get; } = SubmodelReportRows.ChoiceOptions();

    // ---------- Шаг 2. Материализация ----------

    public string MaterializationStatusText => Materialization is null
        ? Loc.S(Scenario is null ? "SubmodelMaterializationNeedsScenario" : "SubmodelMaterializationNone")
        : string.Format(Loc.S("SubmodelMaterializationDone"), Materialization.Created);
    public bool MaterializationHasError => Materialization is null;
    public IReadOnlyList<string> MaterializationDetails => Materialization?.Summary is { } s
        ?
        [
            string.Format(Loc.S("SubmodelMaterializationCounts"), s.NodeCount, s.MemberCount, s.NodeLoadCount, s.MemberLoadCount, s.KinematicLoadCount),
            string.Format(Loc.S("SubmodelMaterializationRank"), s.RankBefore),
            string.Format(Loc.S("SubmodelMaterializationGauge"),
                s.GaugeDofs.Count == 0 ? SubmodelReportRows.Dash : string.Join("; ", SubmodelReportRows.GaugeDofs(s)))
        ]
        : [];

    // ---------- Шаг 3. Линейная сверка ----------

    public IReadOnlyList<DeviationRow> LinearRows => LinearReport is { } r ? SubmodelReportRows.FromReport(r) : [];
    public string LinearStatusText => LinearReport is null
        ? Loc.S("SubmodelLinearNone")
        : Loc.S(LinearReport.Passed ? "SubmodelLinearPassed" : "SubmodelLinearFailed");
    public bool LinearHasError => LinearReport is { Passed: false };

    // ---------- Шаг 4. Нелинейный расчёт ----------

    public IReadOnlyList<DeviationRow> NonlinearRows => NonlinearReport is { } r ? SubmodelReportRows.FromReport(r.AtReached) : [];
    public string NonlinearStatusText => NonlinearReport is null
        ? Loc.S("SubmodelNonlinearNone")
        : string.Format(Loc.S("SubmodelNonlinearReached"), NonlinearReport.ReachedLambda.ToString("0.###", CultureInfo.CurrentCulture));
    public bool NonlinearHasWarning => NonlinearReport is { LambdaReached: false };
    public IReadOnlyList<string> NonlinearNotes
    {
        get
        {
            if (NonlinearReport is not { } r) return [];
            var notes = new List<string>();
            if (!r.LambdaReached) notes.Add(Loc.S("SubmodelNonlinearLambdaNotReached"));
            if (r.MemberLoadsLumped) notes.Add(Loc.S("SubmodelMemberLoadsLumpedNote"));
            notes.Add(Loc.S("SubmodelNonlinearNoVerdict"));
            if (SubmodelReportRows.GaugeResidual(r.MaxGaugeForce, force: true) is { } f)
                notes.Add(string.Format(Loc.S("SubmodelGaugeResidualForce"), f));
            if (SubmodelReportRows.GaugeResidual(r.MaxGaugeMoment, force: false) is { } m)
                notes.Add(string.Format(Loc.S("SubmodelGaugeResidualMoment"), m));
            if (PrecheckText is not null) notes.Add(PrecheckText);
            return notes;
        }
    }

    // ---------- Действия ----------

    /// <summary>Перечитывает извлечение, сценарий, материализацию и отчёты сверки из БД (только чтение).</summary>
    public void Refresh()
    {
        Extraction = _db.GetSubmodelExtractionBySubmodelSchema(_schemaId);
        Scenario = Extraction is null ? null : _db.GetSubmodelBoundaryScenarios(Extraction.Id).FirstOrDefault(s => s.Ordinal == 0);
        Materialization = Scenario is null ? null : _db.GetSubmodelMaterialization(Scenario.Id);
        ScenarioRows = Scenario is null ? [] : SubmodelReportRows.FromScenario(Scenario.Scenario);
        EndRows = Scenario is null ? [] : SubmodelReportRows.Ends(Scenario.Scenario);
        ScenarioDiagnostics = Scenario?.Scenario.Diagnostics ?? [];
        MaterializationDiagnostics = Materialization?.Summary.Diagnostics ?? [];

        if (Materialization is not null)
        {
            var linear = _materializationService.Verify(_schemaId);
            LinearReport = linear.Report;
            LinearDiagnostics = linear.Diagnostics;
            var nonlinear = _nonlinearService.VerifyNonlinear(_schemaId);
            NonlinearReport = nonlinear.Report;
            NonlinearDiagnostics = nonlinear.Diagnostics;
        }
        else
        {
            LinearReport = null;
            NonlinearReport = null;
            LinearDiagnostics = NonlinearDiagnostics = [];
        }
        PrecheckText = null;
        NotifyAll();
    }

    void BuildScenario()
    {
        if (!CanBuildScenario) return;
        var overrides = SubmodelReportRows.Overrides(ScenarioRows);
        IReadOnlyList<FemValidationDiagnostic> failure = [];
        try
        {
            _scenarioService.BuildAndSave(_schemaId, overrides);
        }
        catch (InvalidOperationException ex)
        {
            failure = [new(ex.Message.StartsWith("submodel_", StringComparison.Ordinal) ? ex.Message : "submodel_ui_scenario_failed",
                ExplainScenarioFailure(ex.Message), true, [])];
        }
        Refresh();
        if (failure.Count > 0)
        {
            ScenarioDiagnostics = [.. failure, .. ScenarioDiagnostics];
            OnPropertyChanged(nameof(ScenarioDiagnostics));
        }
        LogErrors(ScenarioDiagnostics);
    }

    static string ExplainScenarioFailure(string message) => message == BoundaryScenarioDiagnostics.ExtractionStale
        ? Loc.S("SubmodelScenarioExtractionStale")
        : message;

    void Materialize()
    {
        if (!CanMaterialize || !_host.ConfirmDiscardUnsavedEdits()) return;
        var outcome = _materializationService.Materialize(_schemaId);
        if (outcome.Materialization is not null)
        {
            _host.ReloadSchemaPage();
            return;
        }
        MaterializationDiagnostics = outcome.Diagnostics;
        OnPropertyChanged(nameof(MaterializationDiagnostics));
        LogErrors(outcome.Diagnostics);
    }

    /// <summary>Расчёт линейной постановки сверки и сверка с родителем под индикатором занятости.</summary>
    public async Task RunLinearAsync()
    {
        if (!CanVerify) return;
        await RunBusyAsync(Loc.S("SubmodelLinearRunning"), async ct =>
        {
            var outcome = await _materializationService.RunAndVerifyAsync(_schemaId, _runner, ct);
            LinearReport = outcome.Report;
            LinearDiagnostics = outcome.Diagnostics;
            LogErrors(outcome.Diagnostics);
            return LinearStatusText;
        });
    }

    void EditNonlinearParams()
    {
        if (!CanVerify) return;
        if (_host.EditNonlinearParameters(FindNonlinearAnalysis(), _nonlinearParams) is { } edited)
            _nonlinearParams = edited;
    }

    /// <summary>
    /// Нелинейный расчёт и сверка под индикатором занятости с отменой. Параметры — заданные в диалоге, иначе из
    /// существующей постановки, иначе диалог открывается перед расчётом.
    /// </summary>
    public async Task RunNonlinearAsync()
    {
        if (!CanVerify) return;
        var parameters = _nonlinearParams
                         ?? (FindNonlinearAnalysis() is { } existing ? FemAnalysisParams.Parse(existing.ParamsJson) : null)
                         ?? _host.EditNonlinearParameters(null, null);
        if (parameters is null) return;
        _nonlinearParams = parameters;

        bool precheck = _settings().SubmodelLinearPrecheck;
        await RunBusyAsync(Loc.S("SubmodelNonlinearRunning"), async ct =>
        {
            var outcome = await _nonlinearService.RunAsync(_schemaId, parameters, _runner, precheck, ct);
            NonlinearReport = outcome.Report;
            NonlinearDiagnostics = outcome.Diagnostics;
            if (precheck && outcome.Precheck is { } report)
            {
                LinearReport = report;
                LinearDiagnostics = [];
            }
            PrecheckText = !precheck
                ? Loc.S("SubmodelPrecheckDisabled")
                : outcome.Precheck is null
                    ? Loc.S("SubmodelPrecheckNotRun")
                    : Loc.S(outcome.Precheck.Passed ? "SubmodelPrecheckPassed" : "SubmodelPrecheckFailed");
            LogErrors(outcome.Diagnostics);
            return NonlinearStatusText;
        });
    }

    /// <summary>
    /// Общий жизненный цикл расчёта: индикатор занятости с отменой, <c>EndBusy</c> при успехе, отмене и
    /// исключении (как <c>RunFemAnalysis</c>); команды заблокированы на время расчёта.
    /// </summary>
    async Task RunBusyAsync(string message, Func<CancellationToken, Task<string>> body)
    {
        _isRunning = true;
        NotifyCommands();
        string? endMessage = null;
        var cts = _host.BeginBusy(message);
        try
        {
            endMessage = await body(cts.Token);
        }
        catch (OperationCanceledException)
        {
            endMessage = Loc.S("CalcTaskCancelled");
        }
        catch (Exception ex)
        {
            _host.Log(new("submodel_ui_run_failed", ex.Message, true, []));
        }
        finally
        {
            _isRunning = false;
            _host.EndBusy(endMessage);
            NotifyAll();
        }
    }

    FemAnalysis? FindNonlinearAnalysis()
    {
        var schema = _db.FemSchemas.FirstOrDefault(s => s.Id == _schemaId);
        var analyses = schema is not null ? schema.Analyses.ToList() : _db.GetFemAnalyses(_schemaId);
        return analyses.FirstOrDefault(a => a.Tag == StraightBeamSubmodelNonlinearService.AnalysisTag);
    }

    void LogErrors(IEnumerable<FemValidationDiagnostic> diagnostics)
    {
        foreach (var d in diagnostics.Where(d => d.IsError)) _host.Log(d);
    }

    void NotifyCommands()
    {
        OnPropertyChanged(nameof(CanBuildScenario));
        OnPropertyChanged(nameof(CanMaterialize));
        OnPropertyChanged(nameof(CanVerify));
        CommandManager.InvalidateRequerySuggested();
    }

    void NotifyAll()
    {
        OnPropertyChanged(string.Empty);
        CommandManager.InvalidateRequerySuggested();
    }
}
