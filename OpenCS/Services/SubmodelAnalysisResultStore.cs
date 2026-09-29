using CScore;
using CScore.Fem;
using OpenCS.Utilites;

namespace OpenCS.Services;

/// <summary>
/// Поиск постановок субмодели и сохранение результатов их расчёта — общий для линейной (4a) и нелинейной
/// (4b) сверки. Без UI.
/// </summary>
internal sealed class SubmodelAnalysisResultStore(DatabaseService database)
{
    /// <summary>
    /// Постановка по тегу. Объект берётся из кэша <c>FemSchemas[..].Analyses</c>: <c>SaveFemAnalysis</c>
    /// добавляет в кэш по ссылке, и свежий экземпляр из БД задвоил бы постановку в дереве. Схемы нет в
    /// кэше (тестовая БД без загрузки схем) — читается из БД.
    /// </summary>
    public FemAnalysis? FindAnalysis(int schemaId, string tag)
    {
        var schema = database.FemSchemas.FirstOrDefault(s => s.Id == schemaId);
        return schema is not null
            ? schema.Analyses.FirstOrDefault(a => a.Tag == tag)
            : database.GetFemAnalyses(schemaId).FirstOrDefault(a => a.Tag == tag);
    }

    /// <summary>Сохраняет результат расчёта постановки, удаляя прежний, и привязывает его к постановке.</summary>
    public void SaveRunResult(FemAnalysis analysis, CalcResult result)
    {
        DeleteResult(analysis);
        database.SaveCalcResult(result);
        analysis.ResultId = result.Id;
        analysis.Status = result.Status;
        database.SaveFemAnalysis(analysis);
    }

    /// <summary>Удаляет сохранённый результат постановки (если есть); сама постановка не меняется.</summary>
    public void DeleteResult(FemAnalysis analysis)
    {
        if (analysis.ResultId is not { } id) return;
        var stored = database.CalcResults.FirstOrDefault(r => r.Id == id) ?? database.GetCalcResultById(id);
        if (stored is not null) database.DeleteCalcResult(stored);
    }
}
