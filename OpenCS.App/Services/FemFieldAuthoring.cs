using CScore.Fem;
using CScore.Fem.Fields;
using OpenCS.Utilites;

namespace OpenCS.Services;

/// <summary>Замечание проверки поля: <paramref name="Path"/> — раздел поля (<c>name</c>, <c>columns</c>, <c>points[3]</c>…).</summary>
public sealed record FemFieldIssue(string Path, string Message);

/// <summary>
/// Поле из таблицы точек: проверка с путями (для окна и вида <c>femSchemas</c> документа), запись с пересчётом,
/// удаление с числом затронутого и сброс результатов постановок — одно для окна поля и CLI.
/// </summary>
public static class FemFieldAuthoring
{
    /// <summary>Проверки, не зависящие от сетки: имя, величина, столбцы и компоненты, точки, допуски, масштаб.</summary>
    public static List<FemFieldIssue> Validate(FemField f)
    {
        var issues = new List<FemFieldIssue>();
        if (string.IsNullOrWhiteSpace(f.Name)) issues.Add(new("name", Loc.S("FemFieldNameEmpty")));
        if (FemFieldQuantities.All.FirstOrDefault(q => q.Code == f.Quantity) is not { } quantity)
        {
            issues.Add(new("quantity", Loc.S("FemFieldBadQuantity")));
            return issues;
        }

        var columns = f.Definition.Columns;
        if (columns.Count == 0) issues.Add(new("columns", Loc.S("FemFieldNoValueColumns")));
        foreach (var c in columns)
            if (!quantity.Components.Contains(c.Component))
            {
                issues.Add(new("columns", string.Format(Loc.S("FemFieldBadComponent"), c.Component)));
                break;
            }
        if (columns.GroupBy(c => c.Component).FirstOrDefault(g => g.Count() > 1) is { } dup)
            issues.Add(new("columns", string.Format(Loc.S("FemFieldDuplicateComponent"), dup.Key)));

        if (f.Points.Count == 0) issues.Add(new("points", Loc.S("FemFieldNoPoints")));
        for (int i = 0; i < f.Points.Count; i++)
        {
            var p = f.Points[i];
            if (!Finite(p.P.X) || !Finite(p.P.Y) || !Finite(p.P.Z) || p.Values.Length != columns.Count || !p.Values.All(Finite))
            {
                issues.Add(new($"points[{i}]", string.Format(Loc.S("FemFieldPointBad"), i + 1)));
                break;
            }
        }

        var s = f.Definition.Settings;
        if (!(s.MaxDistance > 0) || !(s.PlaneTolerance > 0) || !(s.MatchTolerance > 0))
            issues.Add(new("tolerances", Loc.S("FemFieldBadTolerance")));
        if (!(f.Definition.Alignment.Scale > 0) || !Finite(f.Definition.Alignment.Scale))
            issues.Add(new("alignment", Loc.S("FemFieldBadScale")));
        return issues;
    }

    /// <summary>Цели на сетке: есть ли они вообще и нет ли отсутствующих тегов. Пустая сетка — только по <see cref="Validate"/>.</summary>
    public static List<FemFieldIssue> ValidateTargets(FemField f, FemFieldMeshContext ctx)
    {
        var set = FemFieldEvaluator.ResolveTargets(f, ctx);
        var issues = new List<FemFieldIssue>();
        bool shell = FemFieldQuantities.Get(f.Quantity).Target == FemFieldTargetKind.Shell;
        if (shell ? set.Shells.Count == 0 : set.Nodes.Count == 0)
            issues.Add(new("targets", Loc.S(shell ? "FemFieldNoShellTargets" : "FemFieldNoNodeTargets")));
        if (set.Missing.Count > 0)
            issues.Add(new("targets", string.Format(Loc.S("FemFieldTargetsMissing"), string.Join(", ", set.Missing))));
        return issues;
    }

    /// <summary>Записывает поле (кэш значений сбрасывается) и пересчитывает его на текущей сетке.</summary>
    public static FemFieldComputed Save(DatabaseService db, FemField f)
    {
        db.SaveFemField(f);
        return FemFieldService.Recompute(db, f);
    }

    /// <summary>Вопрос об удалении: имя, число точек и сколько результатов постановок будет сброшено.</summary>
    public static string DeleteQuestion(FemField f, FemSchema schema) =>
        string.Format(Loc.S("FemFieldDeleteConfirm"), f.Name, f.Points.Count, ResultsToReset(schema));

    /// <summary>Постановки схемы с результатом — их сбрасывает изменение поля.</summary>
    public static int ResultsToReset(FemSchema schema) => schema.Analyses.Count(a => a.ResultId != null);

    /// <summary>Удаляет поле и сбрасывает результаты постановок схемы; возвращает число сброшенных.</summary>
    public static int Delete(DatabaseService db, FemSchema schema, FemField f)
    {
        db.DeleteFemField(f.Id);
        return InvalidateAnalyses(db, schema);
    }

    /// <summary>Сбрасывает результаты постановок схемы (изменилось поле); возвращает число сброшенных.</summary>
    public static int InvalidateAnalyses(DatabaseService db, FemSchema schema)
    {
        int invalidated = 0;
        foreach (var analysis in schema.Analyses.Where(a => a.ResultId != null))
        {
            analysis.InvalidateResult();
            db.SaveFemAnalysis(analysis);
            invalidated++;
        }
        return invalidated;
    }

    static bool Finite(double v) => double.IsFinite(v);
}
