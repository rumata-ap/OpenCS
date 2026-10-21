using CScore.Fem;
using CScore.Fem.Fields;
using OpenCS.Utilites;

namespace OpenCS.Services;

/// <summary>
/// Эффективные C1 и пружины схемы: импорт + поля из таблиц + ручные правки (<see cref="FemFieldOverlay"/>). Все
/// потребители этих величин читают их отсюда, а не из сырых <see cref="FemElement.FoundationC1"/> и пружин БД.
/// </summary>
public static class FemFieldService
{
    // Effective пишет пересчитанный кэш в БД и зовётся из фоновых задач (глифы ГУ, строки узлов, проверки) на общем
    // соединении: без замка два вызова после смены сетки пересчитали бы поле дважды и открыли вложенные транзакции.
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<DatabaseService, object> Gates = new();

    static object Gate(DatabaseService db) => Gates.GetValue(db, _ => new object());

    public static FemFieldMeshContext LoadContext(DatabaseService db, int schemaId) =>
        Context(db, schemaId, db.GetFemMeshElements(schemaId), db.GetFemSprings(schemaId));

    static FemFieldMeshContext Context(DatabaseService db, int schemaId, IReadOnlyList<FemElement> elements, IReadOnlyList<FemSpring> springs) =>
        new(db.GetFemMeshNodes(schemaId), elements,
            db.FemSchemas.FirstOrDefault(s => s.Id == schemaId)?.MemberGroups.ToList() ?? [],
            db.GetFemMembers(schemaId), springs);

    /// <summary>Пересчитывает поле на текущей сетке и записывает кэш значений с подписью целей.</summary>
    public static FemFieldComputed Recompute(DatabaseService db, FemField field, FemFieldMeshContext? ctx = null)
    {
        lock (Gate(db))
        {
            ctx ??= LoadContext(db, field.SchemaId);
            var computed = FemFieldEvaluator.Compute(field, ctx);
            db.ReplaceFemFieldValues(field.Id, computed.Values, computed.Signature);
            field.MeshSignature = computed.Signature;
            return computed;
        }
    }

    /// <summary>
    /// Эффективные КЭ и пружины. Без полей и правок — сырые данные без лишних чтений; поле, чья подпись целей не
    /// совпала с сеткой (перестроение, повторный импорт), пересчитывается с замечанием <c>field_recomputed</c>.
    /// </summary>
    public static FemFieldOverlayResult Effective(DatabaseService db, int schemaId)
    {
        // Под замком: второй вызов читает подписи уже после пересчёта первым и не пересчитывает повторно.
        lock (Gate(db)) return EffectiveCore(db, schemaId);
    }

    static FemFieldOverlayResult EffectiveCore(DatabaseService db, int schemaId)
    {
        var elements = db.GetFemMeshElements(schemaId);
        var springs = db.GetFemSprings(schemaId);
        var fields = db.GetFemFields(schemaId).Where(f => f.Enabled).ToList();
        var overrides = db.GetFemParamOverrides(schemaId);
        if (fields.Count == 0 && overrides.Count == 0) return FemFieldOverlay.Apply(elements, springs, [], []);

        var ctx = Context(db, schemaId, elements, springs);
        var layers = new List<FemFieldLayer>(fields.Count);
        var diags = new List<FieldDiagnostic>();
        foreach (var f in fields)
        {
            if (FemFieldEvaluator.Signature(f, ctx) != f.MeshSignature)
            {
                Recompute(db, f, ctx);
                diags.Add(new FieldDiagnostic("field_recomputed", $"Поле «{f.Name}» пересчитано: сетка изменилась.", []));
            }
            layers.Add(new FemFieldLayer(f, db.GetFemFieldValues(f.Id)));
        }
        var result = FemFieldOverlay.Apply(elements, springs, layers, overrides);
        return result with { Diagnostics = [.. diags, .. result.Diagnostics] };
    }

    /// <summary>Эффективные пружины; без полей и правок — прямое чтение пружин, без чтения КЭ сетки.</summary>
    public static IReadOnlyList<FemSpring> EffectiveSprings(DatabaseService db, int schemaId) =>
        db.HasFemFieldLayers(schemaId) ? Effective(db, schemaId).Springs : db.GetFemSprings(schemaId);

    /// <summary>Число КЭ с действующим C1 &gt; 0; без полей и правок — подсчёт в БД без чтения КЭ.</summary>
    public static int FoundationElementCount(DatabaseService db, int schemaId) =>
        db.HasFemFieldLayers(schemaId)
            ? Effective(db, schemaId).Elements.Count(e => e.FoundationC1 is > 0)
            : db.GetFemFoundationElementCount(schemaId);

    /// <summary>Сбрасывает результаты постановок схемы после изменения полей. Возвращает число сброшенных.</summary>
    public static int InvalidateAnalyses(DatabaseService db, int schemaId)
    {
        var schema = db.FemSchemas.FirstOrDefault(s => s.Id == schemaId);
        if (schema == null) return 0;
        int invalidated = 0;
        foreach (var analysis in schema.Analyses.Where(a => a.ResultId != null))
        {
            analysis.InvalidateResult();
            db.SaveFemAnalysis(analysis);
            invalidated++;
        }
        return invalidated;
    }
}
