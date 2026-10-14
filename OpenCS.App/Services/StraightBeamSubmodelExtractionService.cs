using CScore.Fem;
using CScore.Submodel;
using OpenCS.Utilites;

namespace OpenCS.Services;

/// <summary>Итог извлечения: запись извлечения (null — схема не создана), анализ цепочки и диагностика.</summary>
public sealed record SubmodelExtractionOutcome(SubmodelExtraction? Extraction,
    StraightBeamChainAnalysis? Chain, IReadOnlyList<FemValidationDiagnostic> Diagnostics);

/// <summary>
/// Извлечение субмодели прямой стержневой цепочки из FEM-редактора: снимок сетки →
/// <see cref="MeshBeamSegmentAdapter"/> → <see cref="StraightBeamAnalyzer"/> →
/// <see cref="StraightBeamSubmodelBuilder"/> → <see cref="DatabaseService.CreateStraightBeamSubmodel"/>.
/// Отказы сохранения (<c>submodel_persistence_*</c>) превращаются в диагностику. Без UI.
/// </summary>
public sealed class StraightBeamSubmodelExtractionService
{
    /// <summary>Родительская постановка не подходит для извлечения.</summary>
    public const string ParentAnalysisInvalid = "submodel_ui_parent_analysis_invalid";

    readonly DatabaseService _database;

    public StraightBeamSubmodelExtractionService(DatabaseService database) =>
        _database = database ?? throw new ArgumentNullException(nameof(database));

    /// <summary>Анализ выделенной цепочки по снимку сетки — чистая функция, без обращения к БД.</summary>
    public static StraightBeamChainAnalysis Analyze(IReadOnlyCollection<string> selectedTags,
        IReadOnlyList<FemElement> elements, IReadOnlyList<FemMeshNode> nodes, IReadOnlyList<FemMember> members)
    {
        var adapted = MeshBeamSegmentAdapter.Build(selectedTags, elements, nodes, members);
        return StraightBeamAnalyzer.Analyze(adapted.Segments, adapted.Environment, ChainTolerances.Default,
            adapted.PreferredDirection, new BeamLocalAxisFrameProvider(), adapted.Diagnostics);
    }

    /// <summary>Анализ выделенной цепочки по сохранённому снимку сетки схемы.</summary>
    public StraightBeamChainAnalysis Analyze(int schemaId, IReadOnlyCollection<string> selectedTags,
        IReadOnlyList<FemMember> members) =>
        Analyze(selectedTags, _database.GetFemMeshElements(schemaId), _database.GetFemMeshNodes(schemaId), members);

    /// <summary>Линейные постановки схемы с сохранённым успешным результатом — допустимые родители извлечения.</summary>
    public IReadOnlyList<FemAnalysis> EligibleParentAnalyses(int schemaId)
    {
        var schema = _database.FemSchemas.FirstOrDefault(s => s.Id == schemaId);
        IEnumerable<FemAnalysis> analyses = schema is not null ? schema.Analyses : _database.GetFemAnalyses(schemaId);
        return analyses.Where(a => a.Kind == "linear" && a.ResultId is not null && a.Status == "ok").ToList();
    }

    /// <summary>
    /// Анализирует цепочку заново по тем же тегам (извлекается ровно то, что проверено на момент вызова) и
    /// создаёт дочернюю схему. Несохранённые правки схемы сервис не видит — это проверяет вызывающий.
    /// </summary>
    public SubmodelExtractionOutcome Extract(int parentSchemaId, int parentAnalysisId,
        IReadOnlyCollection<string> selectedTags, IReadOnlyList<FemMember> members, string submodelTag)
    {
        ArgumentNullException.ThrowIfNull(selectedTags);
        ArgumentNullException.ThrowIfNull(members);
        ArgumentException.ThrowIfNullOrWhiteSpace(submodelTag);
        var diagnostics = new List<FemValidationDiagnostic>();

        var analysis = EligibleParentAnalyses(parentSchemaId).FirstOrDefault(a => a.Id == parentAnalysisId);
        if (analysis is null)
        {
            diagnostics.Add(new(ParentAnalysisInvalid,
                "Выберите линейный расчёт схемы с сохранённым успешным результатом.", true, []));
            return new(null, null, diagnostics);
        }

        var elements = _database.GetFemMeshElements(parentSchemaId);
        var nodes = _database.GetFemMeshNodes(parentSchemaId);
        var chain = Analyze(selectedTags, elements, nodes, members);
        var draft = StraightBeamSubmodelBuilder.Build(parentSchemaId, chain, elements, nodes);
        diagnostics.AddRange(draft.Diagnostics);
        if (!draft.IsSuccess) return new(null, chain, diagnostics);

        try
        {
            var extraction = _database.CreateStraightBeamSubmodel(new StraightBeamSubmodelRequest(
                submodelTag, parentSchemaId, analysis.Id, analysis.ResultId!.Value, draft.Draft!));
            return new(extraction, chain, diagnostics);
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("submodel_persistence_", StringComparison.Ordinal))
        {
            diagnostics.Add(new(ex.Message, Explain(ex.Message), true, []));
            return new(null, chain, diagnostics);
        }
    }

    static string Explain(string code) => code switch
    {
        "submodel_persistence_parent_schema_missing" => "Родительская схема не найдена в базе — сохраните схему.",
        "submodel_persistence_parent_analysis_invalid" => "Постановка родителя не найдена или относится к другой схеме.",
        "submodel_persistence_parent_result_stale" => "Результат родителя изменился — пересчитайте и повторите проверку.",
        "submodel_persistence_draft_parent_mismatch" => "Проект субмодели построен для другой схемы.",
        _ => $"Извлечение субмодели отклонено: {code}."
    };
}
