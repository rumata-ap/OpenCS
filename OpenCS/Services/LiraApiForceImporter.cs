using CScore;
using CScore.Fem;
using CScore.Import;
using LiraSaprRes;
using OpenCS.Utilites;

namespace OpenCS.Services;

/// <summary>
/// Читает усилия из открытого документа ЛираСАПР через COM (LiraResAPI.dll / LiraSaprRes interop).
/// Паттерн: CreateNewRequest → заполнить поля → вызвать метод → обойти Response.
/// </summary>
/// <remarks>
/// Маппинг LIRA → OpenCS LoadItem: BarN→N, BarMx→T, BarMy→Mx, BarMz→My, BarQz→Vy, BarQy→Vx
/// (оси сечения OpenCS: x — вдоль Y1, y — вдоль Z1; см. CScore.Import.LiraForceMapper.MapBar).
/// </remarks>
static class LiraApiForceImporter
{
    /// <summary>Читает усилия от загружений для заданных КЭ.</summary>
    public static List<ForceSet> ReadLoadCaseForces(
        FemSchema schema,
        IReadOnlyList<int> elementIds,
        LiraImportSettings settings,
        string memberTag = "")
    {
        if (elementIds.Count == 0) return [];

        var (documentName, units, lcNames) = GetDocumentInfo(settings);
        var api = CreateResultsAccessObject();

        dynamic req = api.CreateRequest("kLiraRequest_LoadCaseForces");
        req.DocumentName = documentName;
        req.Elements.AddFromString(BuildRange(elementIds));

        object resp = api.Access.LoadCaseForces(req);
        return ParseForcesResponse(resp, schema, elementIds, units, settings.InvertBarBendingMoments, settings.InvertShellBendingMoments, memberTag, lcNames);
    }

    /// <summary>Читает усилия от РСН (расчётные сочетания нагрузок) — НС и ПС.</summary>
    public static List<ForceSet> ReadLoadCombinationForces(
        FemSchema schema,
        IReadOnlyList<int> elementIds,
        LiraImportSettings settings,
        string memberTag = "",
        int combinationTable = 1)
    {
        if (elementIds.Count == 0) return [];

        var (documentName, units, _) = GetDocumentInfo(settings);
        var api = CreateResultsAccessObject();

        dynamic req = api.CreateRequest("kLiraRequest_LoadCombinationForces");
        req.DocumentName = documentName;
        req.Elements.AddFromString(BuildRange(elementIds));
        req.LoadCombinationTable = combinationTable;

        // Запрашиваем все 4 предельных состояния: C, CL, N, NL
        req.LoadCombinationLimitState.Count = 4;
        req.LoadCombinationLimitState.Item[0] = (int)LiraLimitStateForcesEnum.kLiraLimitStateForces_UltimateFull;
        req.LoadCombinationLimitState.Item[1] = (int)LiraLimitStateForcesEnum.kLiraLimitStateForces_UltimateLongTerm;
        req.LoadCombinationLimitState.Item[2] = (int)LiraLimitStateForcesEnum.kLiraLimitStateForces_ServiceabilityFull;
        req.LoadCombinationLimitState.Item[3] = (int)LiraLimitStateForcesEnum.kLiraLimitStateForces_ServiceabilityLongTerm;

        object resp = api.Access.LoadCombinationForces(req);
        return ParseCombinationForcesResponse(resp, schema, elementIds, units,
            settings.InvertBarBendingMoments, settings.InvertShellBendingMoments, memberTag);
    }

    /// <summary>Читает усилия от РСУ (расчётные сочетания усилий) — 4 предельных состояния.</summary>
    public static List<ForceSet> ReadDesignCombinationForces(
        FemSchema schema,
        IReadOnlyList<int> elementIds,
        LiraImportSettings settings,
        string memberTag = "",
        int combinationTable = 1)
    {
        if (elementIds.Count == 0) return [];

        var (documentName, units, _) = GetDocumentInfo(settings);
        var api = CreateResultsAccessObject();

        dynamic req = api.CreateRequest("kLiraRequest_DesignCombinationForces");
        req.DocumentName = documentName;
        // Обход бага COM ЛИРА (проверено на ЛИРА-САПР 2024): в ответе РСУ у элемента с наибольшим
        // номером в запросе остаётся только сечение 1 (GetDCLCount = 0 для сечений 2..n).
        // Несуществующий «сторожевой» номер после всех КЭ ЛИРА молча отбрасывает, а реальные КЭ
        // приходят со всеми сечениями. ЗН и РСН этой ошибкой не затронуты.
        req.Elements.AddFromString($"{BuildRange(elementIds)}, {elementIds.Max() + RsuSentinelOffset}");
        req.DesignCombinationTable = combinationTable;

        object resp = api.Access.DesignCombinationForces(req);
        return ParseDesignForcesResponse(resp, schema, elementIds, units,
            settings.InvertBarBendingMoments, settings.InvertShellBendingMoments, memberTag);
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>Смещение «сторожевого» номера КЭ в запросе РСУ (см. ReadDesignCombinationForces).
    /// Большое, чтобы не попасть в реальный КЭ схемы.</summary>
    const int RsuSentinelOffset = 10_000_000;

    /// <summary>Метка строки РСУ в терминах таблицы РСУ ЛИРА: элемент, сечение, критерий, категория
    /// (например «э.1 с2 к2 Б2»).</summary>
    static string DesignRowLabel(dynamic resp, int elemId, int sec, int ls, int dcf)
    {
        int crit;
        try { crit = resp.GetCriterionNumber(elemId, sec, ls, dcf); }
        catch { crit = dcf; }
        string group;
        try { group = InternalGroupName((LiraInternalGroupsEnum)(int)resp.GetInternalGroup(elemId, sec, ls, dcf)); }
        catch { group = ""; }
        return string.IsNullOrEmpty(group)
            ? $"э.{elemId} с{sec} к{crit}"
            : $"э.{elemId} с{sec} к{crit} {group}";
    }

    /// <summary>kLiraInternalGroup_A2 → «A2» (столбец «Г» таблицы РСУ ЛИРА); Undefined → пусто.</summary>
    static string InternalGroupName(LiraInternalGroupsEnum group)
    {
        const string prefix = "kLiraInternalGroup_";
        string name = group.ToString();
        return name.StartsWith(prefix, StringComparison.Ordinal) && group != LiraInternalGroupsEnum.kLiraInternalGroup_Undefined
            ? name[prefix.Length..]
            : "";
    }

    /// <summary>
    /// Получает имя активного документа и единицы его усилий (LiraMeasurementUnits): сила — Forces1,
    /// длина в моментах и погонных усилиях — Forces2; напряжения пластин API отдаёт в тех же единицах (сила/длина²).
    /// Единица координат (Geometry) к усилиям отношения не имеет. Если единицы не читаются —
    /// считаются кН, м и кПа.
    /// </summary>
    static (string docName, LiraApiUnits units, Dictionary<int,string> lcNames) GetDocumentInfo(LiraImportSettings settings)
    {
        dynamic lira = LiraComConnector.ConnectApplication();

        dynamic doc = lira.ActiveDocument
            ?? throw new InvalidOperationException(
                "В ЛираСАПР нет открытого документа. Откройте расчётную схему и повторите.");

        string path = (string)doc.PathName;
        string docName = System.IO.Path.GetFileNameWithoutExtension(path);

        LiraApiUnits units;
        try
        {
            dynamic u = lira.MeasurementUnits;
            units = LiraApiUnits.FromCodes((int)u.Forces1, (int)u.Forces2, settings.TonToKnFactor);
        }
        catch { units = LiraApiUnits.Identity; }

        Dictionary<int, string> lcNames;
        try { lcNames = LiraApiSchemaReader.ReadLoadCaseNames(lira.ActiveDocument); }
        catch { lcNames = []; }

        return (docName, units, lcNames);
    }

    static LiraResultsApi CreateResultsAccessObject() => LiraComConnector.CreateResultsAccess();

    static List<ForceSet> ParseForcesResponse(
        dynamic resp,
        FemSchema schema,
        IReadOnlyList<int> elementIds,
        LiraApiUnits units,
        bool invertBarMoments,
        bool invertShellMoments,
        string memberTag,
        Dictionary<int, string> lcNames)
    {
        var result = new List<ForceSet>();
        var loadCases = resp.LoadCases;
        int lcCount = loadCases.Count;
        if (lcCount == 0) return result;

        for (int lcIdx = 0; lcIdx < lcCount; lcIdx++)
        {
            int lcNum = loadCases.Item[lcIdx].Number;

            string lcName = lcNames.TryGetValue(lcNum, out var lcN) ? lcN : $"ЗН {lcNum}";
            string tag = string.IsNullOrEmpty(memberTag)
                ? lcName
                : $"{memberTag} — {lcName}";

            var fs = new ForceSet
            {
                Tag            = tag,
                SourceType     = "fea",
                SourceSchemaId = schema.Id,
            };

            int itemNum = 1;
            foreach (int elemId in elementIds)
            {
                int sectionCount;
                try { sectionCount = resp.GetSectionCount(elemId); }
                catch { sectionCount = 1; }

                LiraElementFamilyEnum family;
                try { family = (LiraElementFamilyEnum)(int)resp.GetFamily(elemId); }
                catch { family = LiraElementFamilyEnum.kLiraFamily_Bar; }

                var barRows = new List<LoadItem>();
                for (int sec = 1; sec <= sectionCount; sec++)
                {
                    try
                    {
                        if (family == LiraElementFamilyEnum.kLiraFamily_Plate)
                        {
                            // σx/σy/τxy — напряжения (сила/длина² единиц усилий), Mx/My/Mxy — погонные моменты
                            // (длины сокращаются), Qx/Qy — погонные силы (единицы Forces1/Forces2).
                            // Mx/My/Mxy — «сила·длина/длина», длина сокращается — без коррекции.
                            double sigmaX = units.Stress(resp.GetPlateNx (elemId, sec, lcNum));
                            double sigmaY = units.Stress(resp.GetPlateNy (elemId, sec, lcNum));
                            double tauXy  = units.Stress(resp.GetPlateTxy(elemId, sec, lcNum));
                            double mx  = units.MomentPerLength(resp.GetPlateMx (elemId, sec, lcNum));
                            double my  = units.MomentPerLength(resp.GetPlateMy (elemId, sec, lcNum));
                            double mxy = units.MomentPerLength(resp.GetPlateMxy(elemId, sec, lcNum));
                            double qx  = units.PerLength(resp.GetPlateQx (elemId, sec, lcNum));
                            double qy  = units.PerLength(resp.GetPlateQy (elemId, sec, lcNum));
                            // Фильтр: элементы без результатов ЛИРА возвращает как точные нули
                            if (sigmaX == 0 && sigmaY == 0 && tauXy == 0 && mx == 0 && my == 0 && mxy == 0 && qx == 0 && qy == 0)
                                continue;
                            double shellSign = invertShellMoments ? -1.0 : 1.0;
                            fs.ShellItems.Add(new ShellLoadItem
                            {
                                Num = itemNum++, Label = $"э.{elemId} с{sec}",
                                SourceElementNum = elemId, SourceSectionNum = sec,
                                SigmaX = sigmaX, SigmaY = sigmaY, TauXY = tauXy,
                                Mx = mx * shellSign, My = my * shellSign, Mxy = mxy * shellSign,
                                Qx = qx, Qy = qy,
                            });
                        }
                        else
                        {
                            double n  = units.Force(resp.GetBarN (elemId, sec, lcNum));
                            double t  = units.Moment(resp.GetBarMx(elemId, sec, lcNum));
                            double mx = units.Moment(resp.GetBarMy(elemId, sec, lcNum));
                            double my = units.Moment(resp.GetBarMz(elemId, sec, lcNum));
                            double vy = units.Force(resp.GetBarQz(elemId, sec, lcNum));
                            double vx = units.Force(resp.GetBarQy(elemId, sec, lcNum));
                            if (invertBarMoments) { my = -my; mx = -mx; }
                            barRows.Add(new LoadItem
                            {
                                Label = $"э.{elemId} с{sec}",
                                SourceElementNum = elemId, SourceSectionNum = sec,
                                N = n, T = t, My = my, Mx = mx, Vx = vx, Vy = vy,
                            });
                        }
                    }
                    catch { }
                }
                itemNum = AddBarRows(fs, barRows, itemNum);
            }

            fs.Kind = fs.ShellItems.Count > 0 ? "shell" : "bar";
            if (fs.ShellItems.Count > 0 || fs.Items.Count > 0)
                result.Add(fs);
        }

        return result;
    }

    static List<ForceSet> ParseCombinationForcesResponse(
        dynamic resp,
        FemSchema schema,
        IReadOnlyList<int> elementIds,
        LiraApiUnits units,
        bool invertBarMoments,
        bool invertShellMoments,
        string memberTag)
    {
        var result = new List<ForceSet>();
        var combinations = resp.LoadCombinations;
        int lcCount = combinations.Count;
        if (lcCount == 0) return result;

        // Все 4 предельных состояния для ЖБ: C, CL, N, NL
        var limitStates = new[]
        {
            ((int)LiraLimitStateForcesEnum.kLiraLimitStateForces_UltimateFull,           "(C)"),
            ((int)LiraLimitStateForcesEnum.kLiraLimitStateForces_UltimateLongTerm,       "(CL)"),
            ((int)LiraLimitStateForcesEnum.kLiraLimitStateForces_ServiceabilityFull,     "(N)"),
            ((int)LiraLimitStateForcesEnum.kLiraLimitStateForces_ServiceabilityLongTerm, "(NL)"),
        };

        foreach (var (ls, lsSuffix) in limitStates)
        {
            for (int lcIdx = 0; lcIdx < lcCount; lcIdx++)
            {
                int lcNum = combinations.Item[lcIdx].Number;
                string tag = string.IsNullOrEmpty(memberTag)
                    ? $"РСН {lcNum} {lsSuffix}"
                    : $"{memberTag} — РСН {lcNum} {lsSuffix}";

                var fs = new ForceSet
                {
                    Tag            = tag,
                    SourceType     = "fea",
                    SourceSchemaId = schema.Id,
                };

                int itemNum = 1;
                foreach (int elemId in elementIds)
                {
                    int sectionCount;
                    try { sectionCount = resp.GetSectionCount(elemId); }
                    catch { sectionCount = 1; }

                    LiraElementFamilyEnum family;
                    try { family = (LiraElementFamilyEnum)(int)resp.GetFamily(elemId); }
                    catch { family = LiraElementFamilyEnum.kLiraFamily_Bar; }

                    var barRows = new List<LoadItem>();
                    for (int sec = 1; sec <= sectionCount; sec++)
                    {
                        try
                        {
                            if (family == LiraElementFamilyEnum.kLiraFamily_Plate)
                            {
                                double sigmaX = units.Stress(resp.GetPlateNx (elemId, sec, lcNum, ls));
                                double sigmaY = units.Stress(resp.GetPlateNy (elemId, sec, lcNum, ls));
                                double tauXy  = units.Stress(resp.GetPlateTxy(elemId, sec, lcNum, ls));
                                double mx  = units.MomentPerLength(resp.GetPlateMx (elemId, sec, lcNum, ls));
                                double my  = units.MomentPerLength(resp.GetPlateMy (elemId, sec, lcNum, ls));
                                double mxy = units.MomentPerLength(resp.GetPlateMxy(elemId, sec, lcNum, ls));
                                double qx  = units.PerLength(resp.GetPlateQx (elemId, sec, lcNum, ls));
                                double qy  = units.PerLength(resp.GetPlateQy (elemId, sec, lcNum, ls));
                                if (sigmaX == 0 && sigmaY == 0 && tauXy == 0 && mx == 0 && my == 0 && mxy == 0 && qx == 0 && qy == 0)
                                    continue;
                                double shellSign = invertShellMoments ? -1.0 : 1.0;
                                fs.ShellItems.Add(new ShellLoadItem
                                {
                                    Num = itemNum++, Label = $"э.{elemId} с{sec}",
                                SourceElementNum = elemId, SourceSectionNum = sec,
                                    SigmaX = sigmaX, SigmaY = sigmaY, TauXY = tauXy,
                                    Mx = mx * shellSign, My = my * shellSign, Mxy = mxy * shellSign,
                                    Qx = qx, Qy = qy,
                                });
                            }
                            else
                            {
                                double n  = units.Force(resp.GetBarN (elemId, sec, lcNum, ls));
                                double t  = units.Moment(resp.GetBarMx(elemId, sec, lcNum, ls));
                                double mx = units.Moment(resp.GetBarMy(elemId, sec, lcNum, ls));
                                double my = units.Moment(resp.GetBarMz(elemId, sec, lcNum, ls));
                                double vy = units.Force(resp.GetBarQz(elemId, sec, lcNum, ls));
                                double vx = units.Force(resp.GetBarQy(elemId, sec, lcNum, ls));
                                if (invertBarMoments) { my = -my; mx = -mx; }
                                barRows.Add(new LoadItem
                                {
                                    Label = $"э.{elemId} с{sec}",
                                SourceElementNum = elemId, SourceSectionNum = sec,
                                    N = n, T = t, My = my, Mx = mx, Vx = vx, Vy = vy,
                                });
                            }
                        }
                        catch { }
                    }
                    itemNum = AddBarRows(fs, barRows, itemNum);
                }

                fs.Kind = fs.ShellItems.Count > 0 ? "shell" : "bar";
                if (fs.ShellItems.Count > 0 || fs.Items.Count > 0)
                    result.Add(fs);
            }
        }

        return result;
    }

    static List<ForceSet> ParseDesignForcesResponse(
        dynamic resp,
        FemSchema schema,
        IReadOnlyList<int> elementIds,
        LiraApiUnits units,
        bool invertBarMoments,
        bool invertShellMoments,
        string memberTag)
    {
        var result = new List<ForceSet>();

        // Все 4 предельных состояния для ЖБ
        var limitStates = new[]
        {
            ((int)LiraLimitStateForcesEnum.kLiraLimitStateForces_UltimateFull,           "(C)"),
            ((int)LiraLimitStateForcesEnum.kLiraLimitStateForces_UltimateLongTerm,       "(CL)"),
            ((int)LiraLimitStateForcesEnum.kLiraLimitStateForces_ServiceabilityFull,     "(N)"),
            ((int)LiraLimitStateForcesEnum.kLiraLimitStateForces_ServiceabilityLongTerm, "(NL)"),
        };

        foreach (var (ls, lsSuffix) in limitStates)
        {
            string tag = string.IsNullOrEmpty(memberTag)
                ? $"РСУ {lsSuffix}"
                : $"{memberTag} — РСУ {lsSuffix}";

            var fs = new ForceSet
            {
                Tag            = tag,
                SourceType     = "fea",
                SourceSchemaId = schema.Id,
            };

            int itemNum = 1;
            foreach (int elemId in elementIds)
            {
                int sectionCount;
                try { sectionCount = resp.GetSectionCount(elemId); }
                catch { sectionCount = 1; }

                LiraElementFamilyEnum family;
                try { family = (LiraElementFamilyEnum)(int)resp.GetFamily(elemId); }
                catch { family = LiraElementFamilyEnum.kLiraFamily_Bar; }

                var barRows = new List<LoadItem>();
                for (int sec = 1; sec <= sectionCount; sec++)
                {
                    int dcfCount;
                    try { dcfCount = resp.GetDCLCount(elemId, sec, ls); }
                    catch { continue; }

                    for (int dcf = 1; dcf <= dcfCount; dcf++)
                    {
                        try
                        {
                            if (family == LiraElementFamilyEnum.kLiraFamily_Plate)
                            {
                                double sigmaX = units.Stress(resp.GetPlateNx (elemId, sec, ls, dcf));
                                double sigmaY = units.Stress(resp.GetPlateNy (elemId, sec, ls, dcf));
                                double tauXy  = units.Stress(resp.GetPlateTxy(elemId, sec, ls, dcf));
                                double mx  = units.MomentPerLength(resp.GetPlateMx (elemId, sec, ls, dcf));
                                double my  = units.MomentPerLength(resp.GetPlateMy (elemId, sec, ls, dcf));
                                double mxy = units.MomentPerLength(resp.GetPlateMxy(elemId, sec, ls, dcf));
                                double qx  = units.PerLength(resp.GetPlateQx (elemId, sec, ls, dcf));
                                double qy  = units.PerLength(resp.GetPlateQy (elemId, sec, ls, dcf));
                                if (sigmaX == 0 && sigmaY == 0 && tauXy == 0 && mx == 0 && my == 0 && mxy == 0 && qx == 0 && qy == 0)
                                    continue;
                                double shellSign = invertShellMoments ? -1.0 : 1.0;
                                fs.ShellItems.Add(new ShellLoadItem
                                {
                                    Num = itemNum++, Label = DesignRowLabel(resp, elemId, sec, ls, dcf),
                                    SourceElementNum = elemId, SourceSectionNum = sec,
                                    SigmaX = sigmaX, SigmaY = sigmaY, TauXY = tauXy,
                                    Mx = mx * shellSign, My = my * shellSign, Mxy = mxy * shellSign,
                                    Qx = qx, Qy = qy,
                                });
                            }
                            else
                            {
                                double n  = units.Force(resp.GetBarN (elemId, sec, ls, dcf));
                                double t  = units.Moment(resp.GetBarMx(elemId, sec, ls, dcf));
                                double mx = units.Moment(resp.GetBarMy(elemId, sec, ls, dcf));
                                double my = units.Moment(resp.GetBarMz(elemId, sec, ls, dcf));
                                double vy = units.Force(resp.GetBarQz(elemId, sec, ls, dcf));
                                double vx = units.Force(resp.GetBarQy(elemId, sec, ls, dcf));
                                if (invertBarMoments) { my = -my; mx = -mx; }
                                barRows.Add(new LoadItem
                                {
                                    Label = DesignRowLabel(resp, elemId, sec, ls, dcf),
                                    SourceElementNum = elemId, SourceSectionNum = sec,
                                    N = n, T = t, My = my, Mx = mx, Vx = vx, Vy = vy,
                                });
                            }
                        }
                        catch { }
                    }
                }
                itemNum = AddBarRows(fs, barRows, itemNum);
            }

            fs.Kind = fs.ShellItems.Count > 0 ? "shell" : "bar";
            if (fs.ShellItems.Count > 0 || fs.Items.Count > 0)
                result.Add(fs);
        }

        return result;
    }

    /// <summary>Добавить строки стержневого КЭ в набор. КЭ без результатов ЛИРА возвращает точными нулями —
    /// такой КЭ пропускается целиком; нулевое сечение нагруженного КЭ (свободный конец консоли) остаётся:
    /// без него сбивается счёт сечений в эпюрах.</summary>
    static int AddBarRows(ForceSet fs, List<LoadItem> rows, int itemNum)
    {
        if (rows.All(r => r.N == 0 && r.T == 0 && r.My == 0 && r.Mx == 0 && r.Vx == 0 && r.Vy == 0))
            return itemNum;
        foreach (var row in rows)
        {
            row.Num = itemNum++;
            fs.Items.Add(row);
        }
        return itemNum;
    }

    static string BuildRange(IReadOnlyList<int> ids)
    {
        if (ids.Count == 0) return "";
        var sorted = ids.OrderBy(x => x).ToList();
        int min = sorted[0], max = sorted[^1];
        bool contiguous = (max - min + 1 == sorted.Count);
        return contiguous ? $"{min}-{max}" : string.Join(", ", sorted);
    }
}
