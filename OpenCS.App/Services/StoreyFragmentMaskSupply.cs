using CScore.Fem;
using OpenCS.Tasks;
using OpenCS.Utilites;

namespace OpenCS.Services;

/// <summary>
/// Служебная линейная постановка родителя для масок всех загружений фрагмента. Линейные маски фрагмента — по загружению
/// (<see cref="FragmentMaskSourceKeys"/>): одна постановка родителя должна содержать каждое загружение, которое использует
/// расчёт фрагмента. Постановка «Маски фрагмента» создаётся (или обновляется) со стадией «одно загружение целиком» на
/// каждое загружение родителя, у которого есть копия во фрагменте; пользовательские постановки не меняются.
/// </summary>
public static class StoreyFragmentMaskSupply
{
    /// <summary>Имя служебной постановки родителя.</summary>
    public const string AnalysisTag = "Маски фрагмента";

    /// <summary>
    /// Найти или создать служебную постановку и записать её стадии (по одной на общее загружение). Расчёт не запускается —
    /// его делает вызывающий (<see cref="FemAnalysisExecutor.RunAndSaveAsync"/>). null — постановку не создать;
    /// <paramref name="reason"/> — текст причины для окна.
    /// </summary>
    public static FemAnalysis? Prepare(DatabaseService db, FemSchema fragment, out string? reason)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(fragment);
        reason = null;
        if (fragment.ParentSchemaId is not int parentId || fragment.FragmentZ1 is null || fragment.FragmentZ2 is null)
        {
            reason = Loc.S("StoreyFragmentMasksNotFragment");
            return null;
        }
        if (db.GetFemSchemaSourceType(parentId) is null)
        {
            reason = Loc.S("StoreyFragmentMasksParentDeleted");
            return null;
        }

        var parentLoadCases = db.GetFemLoadCases(parentId);
        var map = FragmentMaskSourceKeys.LoadCaseMap(parentLoadCases, db.GetFemLoadCases(fragment.Id), []);
        var stages = parentLoadCases.Where(c => map.ContainsKey(c.Id)).Select(c => new FemAnalysisStage
        {
            Tag = c.Tag,
            LoadExpressionJson = new FemLoadExpression { Mode = FemLoadExpressionMode.Single, LoadCaseIds = [c.Id] }.ToJson(),
            LoadFactorStep = 1.0,
            MaxLoadFactor = 1.0,
        }).ToList();
        if (stages.Count == 0)
        {
            reason = Loc.S("StoreyFragmentMasksNoCommonLoadCases");
            return null;
        }

        // Параметры — как у линейной постановки из диалога по умолчанию (тип расчёта N, настройки CSfea по умолчанию).
        string paramsJson = new FemAnalysisParams { Stages = stages, CalcType = CScore.CalcType.N, Csfea = new FemCsfeaParams() }.ToJson();
        var analysis = db.GetFemAnalyses(parentId).FirstOrDefault(a => a.Kind == FemCsfeaRunner.LinearAnalysisKind && a.Tag == AnalysisTag);
        if (analysis == null)
        {
            analysis = new FemAnalysis { SchemaId = parentId, Tag = AnalysisTag, Kind = FemCsfeaRunner.LinearAnalysisKind };
        }
        analysis.ParamsJson = paramsJson;
        analysis.LoadExpressionJson = stages[0].LoadExpressionJson;
        db.SaveFemAnalysis(analysis);
        return analysis;
    }
}
