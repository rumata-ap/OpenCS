using CScore.Import;
using LiraSaprRes;

namespace OpenCS.Services;

/// <summary>
/// Данные API ЛИРЫ для наборов продавливания (подпроект В): усилия загружений, строки РСУ (ПС1, полные) и РСН (ПС1,
/// полные) в сечениях стержней колонн у узлов. Значения — как отдаёт API (единицы документа, без смены знаков): подбор
/// коэффициентов сочетаний (<see cref="LiraCombinationRows"/>) от единиц не зависит.
/// </summary>
/// <param name="DocumentName">Имя документа ЛИРЫ.</param>
/// <param name="Cases">Сечение → номер загружения → усилия N, Mk, My, Mz, Qz, Qy.</param>
/// <param name="Rsu">Строки РСУ сечений.</param>
/// <param name="Rsn">РСН с усилиями в сечениях.</param>
/// <param name="LoadCaseNames">Имена загружений ЛИРЫ по номеру.</param>
sealed record LiraPunchingApiData(
    string DocumentName,
    IReadOnlyDictionary<LiraBarSection, IReadOnlyDictionary<int, IReadOnlyList<double>>> Cases,
    IReadOnlyList<LiraRsuRow> Rsu,
    IReadOnlyList<LiraRsnRow> Rsn,
    IReadOnlyDictionary<int, string> LoadCaseNames);

/// <summary>Чтение усилий загружений, РСУ и РСН стержней колонн узлов продавливания из открытого документа ЛИРЫ.</summary>
static class LiraApiPunchingCombinations
{
    /// <summary>
    /// Читает данные для стержней <paramref name="bars"/>: номер КЭ ЛИРЫ и конец у плиты (true — первый узел, сечение 1;
    /// false — последнее сечение). Запрашиваются только стержни: запрос РСУ с пластинами в ЛИРЕ 2024 обрывал процесс.
    /// </summary>
    public static LiraPunchingApiData Read(IReadOnlyList<(int Element, bool AtStart)> bars, OpenCS.Utilites.LiraImportSettings settings)
    {
        ArgumentNullException.ThrowIfNull(bars);
        var (documentName, _, lcNames) = LiraApiForceImporter.GetDocumentInfo(settings);
        var elements = bars.Select(b => b.Element).Distinct().Order().ToList();
        if (elements.Count == 0) return new(documentName, new Dictionary<LiraBarSection, IReadOnlyDictionary<int, IReadOnlyList<double>>>(), [], [], lcNames);
        var api = LiraApiForceImporter.CreateResultsAccessObject();
        string range = string.Join(", ", elements);
        const int ls = (int)LiraLimitStateForcesEnum.kLiraLimitStateForces_UltimateFull;

        // Загружения: сечение у плиты определяется по числу сечений стержня.
        dynamic caseReq = api.CreateRequest("kLiraRequest_LoadCaseForces");
        caseReq.DocumentName = documentName;
        caseReq.Elements.AddFromString(range);
        dynamic caseResp = api.Access.LoadCaseForces(caseReq);
        var lcNums = new List<int>();
        for (int i = 0; i < caseResp.LoadCases.Count; i++) lcNums.Add((int)caseResp.LoadCases.Item[i].Number);
        var sections = new List<LiraBarSection>();
        foreach (var (e, atStart) in bars.Distinct())
        {
            int count;
            try { count = (int)caseResp.GetSectionCount(e); }
            catch { continue; }
            sections.Add(new LiraBarSection(e, atStart ? 1 : Math.Max(1, count)));
        }
        var cases = new Dictionary<LiraBarSection, IReadOnlyDictionary<int, IReadOnlyList<double>>>();
        foreach (var s in sections)
        {
            var byCase = new Dictionary<int, IReadOnlyList<double>>();
            foreach (int lc in lcNums)
                try { byCase[lc] = Bar(caseResp, s, lc); }
                catch { }
            cases[s] = byCase;
        }

        // РСУ, ПС1 полные. «Сторожевой» номер — обход потери сечений у последнего КЭ (см. ReadDesignCombinationForces).
        var rsu = new List<LiraRsuRow>();
        dynamic dcfReq = api.CreateRequest("kLiraRequest_DesignCombinationForces");
        dcfReq.DocumentName = documentName;
        dcfReq.Elements.AddFromString($"{range}, {elements.Max() + LiraApiForceImporter.RsuSentinelOffset}");
        dcfReq.DesignCombinationTable = 1;
        dynamic dcfResp = api.Access.DesignCombinationForces(dcfReq);
        foreach (var s in sections)
        {
            int count;
            try { count = (int)dcfResp.GetDCLCount(s.Element, s.Section, ls); }
            catch { continue; }
            for (int d = 1; d <= count; d++)
            {
                var lcs = dcfResp.GetLoadCases(s.Element, s.Section, ls, d);
                var list = new List<int>();
                for (int i = 0; i < lcs.Count; i++) list.Add((int)lcs.Item[i]);
                int crit;
                try { crit = (int)dcfResp.GetCriterionNumber(s.Element, s.Section, ls, d); }
                catch { crit = d; }
                string group;
                try { group = LiraApiForceImporter.InternalGroupName((LiraInternalGroupsEnum)(int)dcfResp.GetInternalGroup(s.Element, s.Section, ls, d)); }
                catch { group = ""; }
                rsu.Add(new LiraRsuRow(s, d, crit, group, list, Bar(dcfResp, s, ls, d)));
            }
        }

        // РСН, ПС1 полные.
        var rsn = new List<LiraRsnRow>();
        dynamic rsnReq = api.CreateRequest("kLiraRequest_LoadCombinationForces");
        rsnReq.DocumentName = documentName;
        rsnReq.Elements.AddFromString(range);
        rsnReq.LoadCombinationTable = 1;
        rsnReq.LoadCombinationLimitState.Count = 1;
        rsnReq.LoadCombinationLimitState.Item[0] = ls;
        dynamic rsnResp = api.Access.LoadCombinationForces(rsnReq);
        var combos = rsnResp.LoadCombinations;
        for (int i = 0; i < combos.Count; i++)
        {
            var info = combos.Item[i];
            int num = (int)info.Number;
            var forces = new Dictionary<LiraBarSection, IReadOnlyList<double>>();
            foreach (var s in sections)
                try { forces[s] = Bar(rsnResp, s, num, ls); }
                catch { }
            if (forces.Count > 0) rsn.Add(new LiraRsnRow(num, (string)(info.Name ?? ""), forces));
        }
        return new(documentName, cases, rsu, rsn, lcNames);
    }

    /// <summary>Усилия загружения: N, Mk, My, Mz, Qz, Qy.</summary>
    static double[] Bar(dynamic r, LiraBarSection s, int lc) =>
    [
        (double)r.GetBarN(s.Element, s.Section, lc), (double)r.GetBarMx(s.Element, s.Section, lc),
        (double)r.GetBarMy(s.Element, s.Section, lc), (double)r.GetBarMz(s.Element, s.Section, lc),
        (double)r.GetBarQz(s.Element, s.Section, lc), (double)r.GetBarQy(s.Element, s.Section, lc),
    ];

    /// <summary>Усилия строки сочетания (РСН: номер и ПС; РСУ: ПС и строка): N, Mk, My, Mz, Qz, Qy.</summary>
    static double[] Bar(dynamic r, LiraBarSection s, int a, int b) =>
    [
        (double)r.GetBarN(s.Element, s.Section, a, b), (double)r.GetBarMx(s.Element, s.Section, a, b),
        (double)r.GetBarMy(s.Element, s.Section, a, b), (double)r.GetBarMz(s.Element, s.Section, a, b),
        (double)r.GetBarQz(s.Element, s.Section, a, b), (double)r.GetBarQy(s.Element, s.Section, a, b),
    ];
}
