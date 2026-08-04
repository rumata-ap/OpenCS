using CScore;
using CScore.Fem;
using LiraSaprRes;
using OpenCS.Utilites;

namespace OpenCS.Services;

/// <summary>
/// Читает усилия из открытого документа ЛираСАПР через COM (LiraResAPI.dll / LiraSaprRes interop).
/// Паттерн: CreateNewRequest → заполнить поля → вызвать метод → обойти Response.
/// </summary>
/// <remarks>
/// Маппинг LIRA → OpenCS LoadItem: BarN→N, BarMx→T, BarMy→My, BarMz→Mx, BarQz→Vx, BarQy→Vy
/// (уточнить при тестировании в зависимости от ориентации осей).
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

        var (documentName, toKn, lengthToM, lcNames) = GetDocumentInfo(settings);
        var result = CreateResultsAccessObject();

        var req = (LiraLoadCaseForcesRequest)result.CreateNewRequest(
            LiraRequestEnum.kLiraRequest_LoadCaseForces);
        req.DocumentName = documentName;
        req.Elements.AddFromString(BuildRange(elementIds));

        var resp = result.LoadCaseForces((LiraLoadCaseForcesRequest)req);
        return ParseForcesResponse(resp, schema, elementIds, toKn, lengthToM, settings.InvertBarBendingMoments, settings.InvertShellBendingMoments, memberTag, lcNames);
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

        var (documentName, toKn, lengthToM, _) = GetDocumentInfo(settings);
        var result = CreateResultsAccessObject();

        var req = (LiraLoadCombinationForcesRequest)result.CreateNewRequest(
            LiraRequestEnum.kLiraRequest_LoadCombinationForces);
        req.DocumentName = documentName;
        req.Elements.AddFromString(BuildRange(elementIds));
        req.LoadCombinationTable = combinationTable;

        // Запрашиваем все 4 предельных состояния: C, CL, N, NL
        req.LoadCombinationLimitState.Count = 4;
        req.LoadCombinationLimitState.Item[0] = (int)LiraLimitStateForcesEnum.kLiraLimitStateForces_UltimateFull;
        req.LoadCombinationLimitState.Item[1] = (int)LiraLimitStateForcesEnum.kLiraLimitStateForces_UltimateLongTerm;
        req.LoadCombinationLimitState.Item[2] = (int)LiraLimitStateForcesEnum.kLiraLimitStateForces_ServiceabilityFull;
        req.LoadCombinationLimitState.Item[3] = (int)LiraLimitStateForcesEnum.kLiraLimitStateForces_ServiceabilityLongTerm;

        var resp = result.LoadCombinationForces((LiraLoadCombinationForcesRequest)req);
        return ParseCombinationForcesResponse(resp, schema, elementIds, toKn, lengthToM,
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

        var (documentName, toKn, lengthToM, _) = GetDocumentInfo(settings);
        var result = CreateResultsAccessObject();

        var req = (LiraDesignCombinationForcesRequest)result.CreateNewRequest(
            LiraRequestEnum.kLiraRequest_DesignCombinationForces);
        req.DocumentName = documentName;
        req.Elements.AddFromString(BuildRange(elementIds));
        req.DesignCombinationTable = combinationTable;

        var resp = result.DesignCombinationForces((LiraDesignCombinationForcesRequest)req);
        return ParseDesignForcesResponse(resp, schema, elementIds, toKn, lengthToM,
            settings.InvertBarBendingMoments, settings.InvertShellBendingMoments, memberTag);
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>
    /// Получает имя активного документа, коэффициент пересчёта усилий в кН (кН/м) и коэффициент
    /// длины в метры (для домножения/деления погонных величин по правилам ниже).
    /// LiraUnitsForceEnum (справочное имя из комментариев ЛИРА COM API, не типизированный C#-enum в этом
    /// проекте): 0=г, 1=кг, 2=тс, 3=Н, 4=кН, 5=МН, 6=фунт, 7=kips.
    /// LiraUnitsGeometryEnum: 0=м, 1=см, 2=мм (эмпирически подтверждено по реальным координатам узлов).
    /// </summary>
    static (string docName, double toKn, double lengthToM, Dictionary<int,string> lcNames) GetDocumentInfo(LiraImportSettings settings)
    {
        var appType = Type.GetTypeFromProgID("LiraSapr.Application")
            ?? throw new InvalidOperationException(
                "ЛираСАПР не запущена. Откройте расчётную схему в ЛираСАПР и повторите.");

        dynamic lira = Activator.CreateInstance(appType)!;

        dynamic doc = lira.ActiveDocument
            ?? throw new InvalidOperationException(
                "В ЛираСАПР нет открытого документа. Откройте расчётную схему и повторите.");

        string path = (string)doc.PathName;
        string docName = System.IO.Path.GetFileNameWithoutExtension(path);

        // Коэффициент: ЛИРА → кН
        double forceToKn;
        try
        {
            int f1 = (int)lira.MeasurementUnits.Forces1;
            forceToKn = f1 switch
            {
                0 => settings.TonToKnFactor / 1e6,  // г → кН (через g; не показывается как выбор ни в одном UI, но COM-документ теоретически может быть так настроен)
                1 => settings.TonToKnFactor / 1e3,  // кг → кН (через g, как тс)
                2 => settings.TonToKnFactor,         // тс → кН
                3 => 1e-3,                            // Н → кН
                4 => 1.0,                             // кН → кН
                5 => 1000.0,                          // МН → кН
                6 => 0.0044482216,                    // фунт → кН
                7 => 4.4482216,                       // kips → кН
                _ => 1.0
            };
        }
        catch { forceToKn = 1.0; }

        // Коэффициент: единица длины ЛИРА → метры
        double lengthToM;
        try
        {
            int g = (int)lira.MeasurementUnits.Geometry;
            lengthToM = g switch { 0 => 1.0, 1 => 0.01, 2 => 0.001, _ => 1.0 };
        }
        catch { lengthToM = 1.0; }

        Dictionary<int, string> lcNames;
        try { lcNames = LiraApiSchemaReader.ReadLoadCaseNames(lira.ActiveDocument); }
        catch { lcNames = []; }

        return (docName, forceToKn, lengthToM, lcNames);
    }

    static LiraResultsAccess CreateResultsAccessObject() => new LiraResultsAccessClass();

    static List<ForceSet> ParseForcesResponse(
        LiraLoadCaseForcesResponse resp,
        FemSchema schema,
        IReadOnlyList<int> elementIds,
        double toKn,
        double lengthToM,
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
                try { family = resp.GetFamily(elemId); }
                catch { family = LiraElementFamilyEnum.kLiraFamily_Bar; }

                for (int sec = 1; sec <= sectionCount; sec++)
                {
                    try
                    {
                        if (family == LiraElementFamilyEnum.kLiraFamily_Plate)
                        {
                            // σx/σy/τxy — «сила/длина²»: делим на lengthToM в квадрате.
                            // Qx/Qy — «сила/длина»: делим на lengthToM в первой степени.
                            // Mx/My/Mxy — «сила·длина/длина», длина сокращается — без коррекции.
                            double sigmaX = resp.GetPlateNx (elemId, sec, lcNum) * toKn / (lengthToM * lengthToM);
                            double sigmaY = resp.GetPlateNy (elemId, sec, lcNum) * toKn / (lengthToM * lengthToM);
                            double tauXy  = resp.GetPlateTxy(elemId, sec, lcNum) * toKn / (lengthToM * lengthToM);
                            double mx  = resp.GetPlateMx (elemId, sec, lcNum) * toKn;
                            double my  = resp.GetPlateMy (elemId, sec, lcNum) * toKn;
                            double mxy = resp.GetPlateMxy(elemId, sec, lcNum) * toKn;
                            double qx  = resp.GetPlateQx (elemId, sec, lcNum) * toKn / lengthToM;
                            double qy  = resp.GetPlateQy (elemId, sec, lcNum) * toKn / lengthToM;
                            // Фильтр: элементы без результатов ЛИРА возвращает как точные нули
                            if (sigmaX == 0 && sigmaY == 0 && tauXy == 0 && mx == 0 && my == 0 && mxy == 0 && qx == 0 && qy == 0)
                                continue;
                            double shellSign = invertShellMoments ? -1.0 : 1.0;
                            fs.ShellItems.Add(new ShellLoadItem
                            {
                                Num = itemNum++, Label = $"э.{elemId}",
                                SigmaX = sigmaX, SigmaY = sigmaY, TauXY = tauXy,
                                Mx = mx * shellSign, My = my * shellSign, Mxy = mxy * shellSign,
                                Qx = qx, Qy = qy,
                            });
                        }
                        else
                        {
                            double n  = resp.GetBarN (elemId, sec, lcNum) * toKn;
                            double t  = resp.GetBarMx(elemId, sec, lcNum) * toKn;
                            double my = resp.GetBarMy(elemId, sec, lcNum) * toKn;
                            double mx = resp.GetBarMz(elemId, sec, lcNum) * toKn;
                            double vx = resp.GetBarQz(elemId, sec, lcNum) * toKn;
                            double vy = resp.GetBarQy(elemId, sec, lcNum) * toKn;
                            if (n == 0 && t == 0 && my == 0 && mx == 0 && vx == 0 && vy == 0)
                                continue;
                            if (invertBarMoments) { my = -my; mx = -mx; }
                            fs.Items.Add(new LoadItem
                            {
                                Num = itemNum++, Label = $"э.{elemId}",
                                N = n, T = t, My = my, Mx = mx, Vx = vx, Vy = vy,
                            });
                        }
                    }
                    catch { }
                }
            }

            fs.Kind = fs.ShellItems.Count > 0 ? "shell" : "bar";
            if (fs.ShellItems.Count > 0 || fs.Items.Count > 0)
                result.Add(fs);
        }

        return result;
    }

    static List<ForceSet> ParseCombinationForcesResponse(
        LiraLoadCombinationForcesResponse resp,
        FemSchema schema,
        IReadOnlyList<int> elementIds,
        double toKn,
        double lengthToM,
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
                    try { family = resp.GetFamily(elemId); }
                    catch { family = LiraElementFamilyEnum.kLiraFamily_Bar; }

                    for (int sec = 1; sec <= sectionCount; sec++)
                    {
                        try
                        {
                            if (family == LiraElementFamilyEnum.kLiraFamily_Plate)
                            {
                                double sigmaX = resp.GetPlateNx (elemId, sec, lcNum, ls) * toKn / (lengthToM * lengthToM);
                                double sigmaY = resp.GetPlateNy (elemId, sec, lcNum, ls) * toKn / (lengthToM * lengthToM);
                                double tauXy  = resp.GetPlateTxy(elemId, sec, lcNum, ls) * toKn / (lengthToM * lengthToM);
                                double mx  = resp.GetPlateMx (elemId, sec, lcNum, ls) * toKn;
                                double my  = resp.GetPlateMy (elemId, sec, lcNum, ls) * toKn;
                                double mxy = resp.GetPlateMxy(elemId, sec, lcNum, ls) * toKn;
                                double qx  = resp.GetPlateQx (elemId, sec, lcNum, ls) * toKn / lengthToM;
                                double qy  = resp.GetPlateQy (elemId, sec, lcNum, ls) * toKn / lengthToM;
                                if (sigmaX == 0 && sigmaY == 0 && tauXy == 0 && mx == 0 && my == 0 && mxy == 0 && qx == 0 && qy == 0)
                                    continue;
                                double shellSign = invertShellMoments ? -1.0 : 1.0;
                                fs.ShellItems.Add(new ShellLoadItem
                                {
                                    Num = itemNum++, Label = $"э.{elemId}",
                                    SigmaX = sigmaX, SigmaY = sigmaY, TauXY = tauXy,
                                    Mx = mx * shellSign, My = my * shellSign, Mxy = mxy * shellSign,
                                    Qx = qx, Qy = qy,
                                });
                            }
                            else
                            {
                                double n  = resp.GetBarN (elemId, sec, lcNum, ls) * toKn;
                                double t  = resp.GetBarMx(elemId, sec, lcNum, ls) * toKn;
                                double my = resp.GetBarMy(elemId, sec, lcNum, ls) * toKn;
                                double mx = resp.GetBarMz(elemId, sec, lcNum, ls) * toKn;
                                double vx = resp.GetBarQz(elemId, sec, lcNum, ls) * toKn;
                                double vy = resp.GetBarQy(elemId, sec, lcNum, ls) * toKn;
                                if (n == 0 && t == 0 && my == 0 && mx == 0 && vx == 0 && vy == 0)
                                    continue;
                                if (invertBarMoments) { my = -my; mx = -mx; }
                                fs.Items.Add(new LoadItem
                                {
                                    Num = itemNum++, Label = $"э.{elemId}",
                                    N = n, T = t, My = my, Mx = mx, Vx = vx, Vy = vy,
                                });
                            }
                        }
                        catch { }
                    }
                }

                fs.Kind = fs.ShellItems.Count > 0 ? "shell" : "bar";
                if (fs.ShellItems.Count > 0 || fs.Items.Count > 0)
                    result.Add(fs);
            }
        }

        return result;
    }

    static List<ForceSet> ParseDesignForcesResponse(
        LiraDesignCombinationForcesResponse resp,
        FemSchema schema,
        IReadOnlyList<int> elementIds,
        double toKn,
        double lengthToM,
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
                try { family = resp.GetFamily(elemId); }
                catch { family = LiraElementFamilyEnum.kLiraFamily_Bar; }

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
                                double sigmaX = resp.GetPlateNx (elemId, sec, ls, dcf) * toKn / (lengthToM * lengthToM);
                                double sigmaY = resp.GetPlateNy (elemId, sec, ls, dcf) * toKn / (lengthToM * lengthToM);
                                double tauXy  = resp.GetPlateTxy(elemId, sec, ls, dcf) * toKn / (lengthToM * lengthToM);
                                double mx  = resp.GetPlateMx (elemId, sec, ls, dcf) * toKn;
                                double my  = resp.GetPlateMy (elemId, sec, ls, dcf) * toKn;
                                double mxy = resp.GetPlateMxy(elemId, sec, ls, dcf) * toKn;
                                double qx  = resp.GetPlateQx (elemId, sec, ls, dcf) * toKn / lengthToM;
                                double qy  = resp.GetPlateQy (elemId, sec, ls, dcf) * toKn / lengthToM;
                                if (sigmaX == 0 && sigmaY == 0 && tauXy == 0 && mx == 0 && my == 0 && mxy == 0 && qx == 0 && qy == 0)
                                    continue;
                                double shellSign = invertShellMoments ? -1.0 : 1.0;
                                fs.ShellItems.Add(new ShellLoadItem
                                {
                                    Num = itemNum++, Label = $"э.{elemId} к{dcf}",
                                    SigmaX = sigmaX, SigmaY = sigmaY, TauXY = tauXy,
                                    Mx = mx * shellSign, My = my * shellSign, Mxy = mxy * shellSign,
                                    Qx = qx, Qy = qy,
                                });
                            }
                            else
                            {
                                double n  = resp.GetBarN (elemId, sec, ls, dcf) * toKn;
                                double t  = resp.GetBarMx(elemId, sec, ls, dcf) * toKn;
                                double my = resp.GetBarMy(elemId, sec, ls, dcf) * toKn;
                                double mx = resp.GetBarMz(elemId, sec, ls, dcf) * toKn;
                                double vx = resp.GetBarQz(elemId, sec, ls, dcf) * toKn;
                                double vy = resp.GetBarQy(elemId, sec, ls, dcf) * toKn;
                                if (n == 0 && t == 0 && my == 0 && mx == 0 && vx == 0 && vy == 0)
                                    continue;
                                if (invertBarMoments) { my = -my; mx = -mx; }
                                fs.Items.Add(new LoadItem
                                {
                                    Num = itemNum++, Label = $"э.{elemId} к{dcf}",
                                    N = n, T = t, My = my, Mx = mx, Vx = vx, Vy = vy,
                                });
                            }
                        }
                        catch { }
                    }
                }
            }

            fs.Kind = fs.ShellItems.Count > 0 ? "shell" : "bar";
            if (fs.ShellItems.Count > 0 || fs.Items.Count > 0)
                result.Add(fs);
        }

        return result;
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
