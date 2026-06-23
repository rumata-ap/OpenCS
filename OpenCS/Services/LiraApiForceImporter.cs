using CScore;
using CScore.Fem;

namespace OpenCS.Services;

/// <summary>
/// Читает усилия из открытого документа ЛираСАПР через COM (dynamic).
/// ProgID объекта результатов: "LiraSaprRes.LiraResultsAccess".
/// Паттерн: CreateNewRequest → заполнить поля → вызвать метод → обойти Response.
/// </summary>
/// <remarks>
/// Перечень методов получения усилий:
///   LoadCaseForces         — усилия от загружений (ЗН)
///   LoadCombinationForces  — усилия от РСН
///   DesignCombinationForces — усилия от РСУ (для подбора арматуры / стальных сечений)
/// Константы LiraRequestEnum:
///   kLiraRequest_LoadCaseForces           = 4
///   kLiraRequest_LoadCombinationForces    = 6
///   kLiraRequest_DesignCombinationForces  = 10
/// Маппинг LIRA → OpenCS LoadItem: BarN→N, BarMx→T, BarMy→My, BarMz→Mx, BarQz→Vx, BarQy→Vy
/// (уточнить при тестировании в зависимости от ориентации осей).
/// </remarks>
static class LiraApiForceImporter
{
    const int kRequestLoadCaseForces  = 4;
    const int kRequestDesignComboForces = 10;

    /// <summary>
    /// Читает усилия от загружений для заданных КЭ и возвращает наборы усилий
    /// (один <see cref="ForceSet"/> на каждое загружение × элемент).
    /// </summary>
    /// <param name="documentName">Имя документа ЛираСАПР (без расширения .lir).</param>
    /// <param name="schema">МКЭ-схема (нужна для source_schema_id и списка элементов).</param>
    /// <param name="elementIds">ID КЭ Лиры, для которых читать усилия.</param>
    public static List<ForceSet> ReadLoadCaseForces(
        string    documentName,
        FemSchema schema,
        IReadOnlyList<int> elementIds)
    {
        if (elementIds.Count == 0) return [];

        dynamic result = CreateResultsAccessObject();

        dynamic req = result.CreateNewRequest(kRequestLoadCaseForces);
        req.DocumentName = documentName;
        req.Elements.AddFromString(BuildRange(elementIds));

        dynamic resp = result.LoadCaseForces(req);
        return ParseForcesResponse(resp, schema, elementIds, "ЗН");
    }

    /// <summary>
    /// Читает усилия от РСУ (расчётные сочетания усилий) и возвращает наборы усилий.
    /// </summary>
    public static List<ForceSet> ReadDesignCombinationForces(
        string    documentName,
        FemSchema schema,
        IReadOnlyList<int> elementIds,
        int combinationTable = 1)
    {
        if (elementIds.Count == 0) return [];

        dynamic result = CreateResultsAccessObject();

        dynamic req = result.CreateNewRequest(kRequestDesignComboForces);
        req.DocumentName = documentName;
        req.Elements.AddFromString(BuildRange(elementIds));
        req.LoadCombinationTable = combinationTable;

        dynamic resp = result.DesignCombinationForces(req);
        return ParseForcesResponse(resp, schema, elementIds, "РСУ");
    }

    // ------------------------------------------------------------------ helpers

    static dynamic CreateResultsAccessObject()
    {
        var type = Type.GetTypeFromProgID("LiraSaprRes.LiraResultsAccess")
            ?? throw new InvalidOperationException(
                "ProgID 'LiraSaprRes.LiraResultsAccess' не зарегистрирован. " +
                "Убедитесь, что ЛираСАПР установлена и зарегистрирована (/register).");
        return Activator.CreateInstance(type)!;
    }

    static List<ForceSet> ParseForcesResponse(
        dynamic   resp,
        FemSchema schema,
        IReadOnlyList<int> elementIds,
        string    seriesPrefix)
    {
        var result = new List<ForceSet>();

        // перечень загружений/комбинаций доступен через resp.LoadCases или resp.LoadCombinations
        // для LoadCaseForces — resp.LoadCases (LiraLoadCaseInfos)
        // для DesignCombinationForces — resp.DesignCombinations (или resp.LoadCombinations)
        dynamic loadCases;
        try { loadCases = resp.LoadCases; }
        catch { loadCases = resp.LoadCombinations; }

        int lcCount = (int)loadCases.Count;

        foreach (int elemId in elementIds)
        {
            int sectionCount;
            try { sectionCount = (int)resp.GetSectionCount(elemId); }
            catch { sectionCount = 2; }  // стержень по умолчанию — 2 сечения

            for (int lcIdx = 0; lcIdx < lcCount; lcIdx++)
            {
                dynamic lcInfo = loadCases.Item[lcIdx];
                int lcNum = (int)lcInfo.Number;

                var fs = new ForceSet
                {
                    Tag              = $"{seriesPrefix}_{lcNum:D2} / э.{elemId}",
                    Kind             = "bar",
                    SourceType       = "fea",
                    SourceSchemaId   = schema.Id,
                    SourceElementTag = elemId.ToString(),
                };

                for (int sec = 1; sec <= sectionCount; sec++)
                {
                    var item = new LoadItem
                    {
                        Num   = sec,
                        Label = $"sec {sec}",
                    };
                    try
                    {
                        // Маппинг LIRA → OpenCS: BarN→N, BarMx→T, BarMy→My, BarMz→Mx
                        // BarQz — поперечная вдоль Z → Vx; BarQy — вдоль Y → Vy
                        item.N  = (double)resp.GetBarN (elemId, sec, lcNum);
                        item.T  = (double)resp.GetBarMx(elemId, sec, lcNum);
                        item.My = (double)resp.GetBarMy(elemId, sec, lcNum);
                        item.Mx = (double)resp.GetBarMz(elemId, sec, lcNum);
                        item.Vx = (double)resp.GetBarQz(elemId, sec, lcNum);
                        item.Vy = (double)resp.GetBarQy(elemId, sec, lcNum);
                    }
                    catch
                    {
                        // элемент или сечение могут отсутствовать — пропускаем
                        continue;
                    }
                    fs.Items.Add(item);
                }

                if (fs.Items.Count > 0)
                    result.Add(fs);
            }
        }

        return result;
    }

    // "1-N" строка диапазона элементов для AddFromString
    static string BuildRange(IReadOnlyList<int> ids)
    {
        if (ids.Count == 0) return "";
        var sorted = ids.OrderBy(x => x).ToList();
        int min = sorted[0], max = sorted[^1];
        // если все идут подряд — используем диапазон; иначе — список через запятую
        bool contiguous = (max - min + 1 == sorted.Count);
        return contiguous
            ? $"{min}-{max}"
            : string.Join(", ", sorted);
    }
}
