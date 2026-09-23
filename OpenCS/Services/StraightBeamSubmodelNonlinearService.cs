using CScore;
using CScore.Fem;
using CScore.Submodel;
using OpenCS.OpenSees.CScore;
using OpenCS.Tasks;
using OpenCS.Utilites;

namespace OpenCS.Services;

/// <summary>Итог подготовки нелинейной постановки субмодели: постановка (null — отказ) и диагностика.</summary>
public sealed record SubmodelNonlinearPrepareOutcome(FemAnalysis? Analysis, IReadOnlyList<FemValidationDiagnostic> Diagnostics);

/// <summary>Итог нелинейной сверки: отчёт (null — сверка невозможна) и диагностика.</summary>
public sealed record SubmodelNonlinearVerificationOutcome(
    SubmodelNonlinearVerificationReport? Report, IReadOnlyList<FemValidationDiagnostic> Diagnostics);

/// <summary>Итог запуска: отчёт предварительной линейной проверки (если выполнялась), нелинейный отчёт и
/// полная диагностика цепочки.</summary>
public sealed record SubmodelNonlinearRunOutcome(
    SubmodelVerificationReport? Precheck,
    SubmodelNonlinearVerificationReport? Report,
    IReadOnlyList<FemValidationDiagnostic> Diagnostics);

/// <summary>
/// Нелинейный расчёт материализованной субмодели по пропорциональному пути <c>λ·p, λ·U</c> (одна стадия
/// до <c>λ = 1</c> на загружение «Граничный сценарий λ = 1») и сверка с линейным родителем (срез 4b).
/// Расчёт выполняет внедряемый <see cref="ISubmodelAnalysisRunner"/>, сохранение и сверку — сервис.
/// Без UI.
/// </summary>
public sealed class StraightBeamSubmodelNonlinearService
{
    public const string AnalysisTag = "Нелинейный расчёт λ = 1";

    readonly DatabaseService _database;
    readonly StraightBeamSubmodelMaterializationService _materialization;

    public StraightBeamSubmodelNonlinearService(DatabaseService database)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _materialization = new StraightBeamSubmodelMaterializationService(database);
    }

    /// <summary>
    /// Создаёт или обновляет нелинейную постановку дочерней схемы: копия <paramref name="parameters"/>
    /// с одной стадией (<c>MaxLoadFactor = 1</c>, шаг — из параметров, LoadControl). При изменении
    /// постановки прежний результат удаляется.
    /// </summary>
    public SubmodelNonlinearPrepareOutcome PrepareNonlinearAnalysis(int submodelSchemaId, FemAnalysisParams parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        var diagnostics = new List<FemValidationDiagnostic>();
        if (!_materialization.TryLoadMaterialized(submodelSchemaId, diagnostics, out _, out _, out _))
            return new(null, diagnostics);

        if (parameters.CalcType is null)
            diagnostics.Add(new(SubmodelNonlinearDiagnostics.ParamsInvalid,
                "Не выбран тип расчёта (CalcType) нелинейной постановки субмодели.", true, []));
        if (!double.IsFinite(parameters.LoadFactorStep) || parameters.LoadFactorStep <= 0 || parameters.LoadFactorStep > 1)
            diagnostics.Add(new(SubmodelNonlinearDiagnostics.ParamsInvalid,
                $"Шаг коэффициента нагрузки λ должен быть в интервале (0; 1], задан {parameters.LoadFactorStep}.", true, []));
        if (diagnostics.Any(d => d.IsError)) return new(null, diagnostics);

        var loadCase = _database.GetFemLoadCases(submodelSchemaId)
            .FirstOrDefault(c => c.Tag == SubmodelMaterializationPlanner.LoadCaseTag);
        if (loadCase is null)
        {
            diagnostics.Add(new(SubmodelMaterializationDiagnostics.Stale,
                $"В субмодели нет загружения «{SubmodelMaterializationPlanner.LoadCaseTag}» — выполните материализацию.", true, []));
            return new(null, diagnostics);
        }

        string expression = new FemLoadExpression { Mode = FemLoadExpressionMode.Single, LoadCaseIds = [loadCase.Id] }.ToJson();
        var stored = FemAnalysisParams.Parse(parameters.ToJson());
        stored.Stages =
        [
            new FemAnalysisStage
            {
                Tag = AnalysisTag, LoadExpressionJson = expression,
                LoadFactorStep = parameters.LoadFactorStep, MaxLoadFactor = 1.0
            }
        ];
        stored.MaxLoadFactor = 1.0;
        string paramsJson = stored.ToJson();

        var analysis = FindAnalysis(submodelSchemaId, AnalysisTag)
                       ?? new FemAnalysis { SchemaId = submodelSchemaId, Tag = AnalysisTag };
        bool changed = analysis.Kind != "nonlinear" || analysis.ParamsJson != paramsJson || analysis.LoadExpressionJson != expression;
        if (changed)
        {
            DeleteResult(analysis);
            analysis.Kind = "nonlinear";
            analysis.ParamsJson = paramsJson;
            analysis.LoadExpressionJson = expression;
            analysis.InvalidateResult();
        }
        if (changed || analysis.Id == 0) _database.SaveFemAnalysis(analysis);
        return new(analysis, diagnostics);
    }

    /// <summary>
    /// Цепочка запуска: подготовка постановки → [линейная проверка 4a] → нелинейный расчёт → сверка.
    /// Провал проверки — предупреждение, расчёт продолжается. Отмена (<see cref="OperationCanceledException"/>)
    /// не перехватывается.
    /// </summary>
    public async Task<SubmodelNonlinearRunOutcome> RunAsync(int submodelSchemaId, FemAnalysisParams parameters,
        ISubmodelAnalysisRunner runner, bool linearPrecheck, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(runner);
        var diagnostics = new List<FemValidationDiagnostic>();
        var prepared = PrepareNonlinearAnalysis(submodelSchemaId, parameters);
        diagnostics.AddRange(prepared.Diagnostics);
        if (prepared.Analysis is not { } nonlinear) return new(null, null, diagnostics);

        var schema = _database.FemSchemas.FirstOrDefault(s => s.Id == submodelSchemaId);
        if (schema is null)
        {
            diagnostics.Add(new(SubmodelMaterializationDiagnostics.ScenarioBlocked,
                $"Схема #{submodelSchemaId} не загружена в проект.", true, []));
            return new(null, null, diagnostics);
        }

        SubmodelVerificationReport? precheck = null;
        if (linearPrecheck)
            precheck = await RunPrecheckAsync(schema, runner, diagnostics, ct);

        var result = await runner.RunAsync(schema, nonlinear, ct);
        SaveRunResult(nonlinear, result);

        var verification = VerifyNonlinear(submodelSchemaId);
        diagnostics.AddRange(verification.Diagnostics);
        return new(precheck, verification.Report, diagnostics);
    }

    /// <summary>Сверка сохранённого результата нелинейной постановки с линейным родителем.</summary>
    public SubmodelNonlinearVerificationOutcome VerifyNonlinear(int submodelSchemaId)
    {
        var diagnostics = new List<FemValidationDiagnostic>();
        if (!_materialization.TryLoadMaterialized(submodelSchemaId, diagnostics,
                out var extraction, out var scenario, out var materialization))
            return new(null, diagnostics);

        var analysis = FindAnalysis(submodelSchemaId, AnalysisTag);
        var childCalc = analysis?.ResultId is { } resultId ? _database.GetCalcResultById(resultId) : null;
        if (childCalc is null)
        {
            diagnostics.Add(new(SubmodelNonlinearDiagnostics.Info,
                $"Нет результата постановки «{AnalysisTag}» — выполните нелинейный расчёт.", false, []));
            return new(null, diagnostics);
        }
        if (_materialization.LoadParentResult(extraction, diagnostics) is not { } parent) return new(null, diagnostics);

        var states = FemNonlinearResultParentAdapter.FromCalcResult(childCalc);
        if (states.Error is not null)
        {
            diagnostics.Add(states.Error);
            return new(null, diagnostics);
        }

        var report = SubmodelNonlinearVerification.Compare(extraction, scenario.Scenario, materialization.Summary,
            parent, states.Steps!, states.MemberLoadsLumped);
        diagnostics.AddRange(report.Diagnostics);
        return new(report, diagnostics);
    }

    async Task<SubmodelVerificationReport?> RunPrecheckAsync(FemSchema schema, ISubmodelAnalysisRunner runner,
        List<FemValidationDiagnostic> diagnostics, CancellationToken ct)
    {
        var linear = FindAnalysis(schema.Id, SubmodelMaterializationPlanner.AnalysisTag);
        if (linear is null)
        {
            diagnostics.Add(new(SubmodelNonlinearDiagnostics.PrecheckFailed,
                $"Линейная проверка не выполнена: нет постановки «{SubmodelMaterializationPlanner.AnalysisTag}».", false, []));
            return null;
        }

        SaveRunResult(linear, await runner.RunAsync(schema, linear, ct));
        var verification = _materialization.Verify(schema.Id);
        if (verification.Report is { Passed: true } report) return report;

        string reason = verification.Diagnostics.FirstOrDefault(d => d.IsError || d.Code == SubmodelMaterializationDiagnostics.VerificationMismatch)?.Message
                        ?? $"статус расчёта «{linear.Status}»";
        diagnostics.Add(new(SubmodelNonlinearDiagnostics.PrecheckFailed,
            $"Линейная сверка субмодели с родителем не прошла ({reason}); нелинейный расчёт выполняется, но постановку стоит проверить.",
            false, []));
        return verification.Report;
    }

    /// <summary>Сохраняет результат расчёта постановки, удаляя прежний, и привязывает его к постановке.</summary>
    void SaveRunResult(FemAnalysis analysis, CalcResult result)
    {
        DeleteResult(analysis);
        _database.SaveCalcResult(result);
        analysis.ResultId = result.Id;
        analysis.Status = result.Status;
        _database.SaveFemAnalysis(analysis);
    }

    void DeleteResult(FemAnalysis analysis)
    {
        if (analysis.ResultId is not { } id) return;
        var stored = _database.CalcResults.FirstOrDefault(r => r.Id == id) ?? _database.GetCalcResultById(id);
        if (stored is not null) _database.DeleteCalcResult(stored);
    }

    /// <summary>
    /// Постановка по тегу. Объект берётся из кэша <c>FemSchemas[..].Analyses</c>: <c>SaveFemAnalysis</c>
    /// добавляет в кэш по ссылке, и свежий экземпляр из БД задвоил бы постановку в дереве. Схемы нет в
    /// кэше (тестовая БД без загрузки схем) — читается из БД.
    /// </summary>
    FemAnalysis? FindAnalysis(int schemaId, string tag)
    {
        var schema = _database.FemSchemas.FirstOrDefault(s => s.Id == schemaId);
        return schema is not null
            ? schema.Analyses.FirstOrDefault(a => a.Tag == tag)
            : _database.GetFemAnalyses(schemaId).FirstOrDefault(a => a.Tag == tag);
    }
}
