using System.IO;
using CScore.Import;

namespace OpenCS.Services;

/// <summary>
/// Чтение результатов открытой ЛИРЫ для снимка масок фрагмента (<see cref="LiraResultSnapshotBuilder"/>): усилия
/// загружений стержней (первое и последнее сечение) и пластин (сечение 1), затем перемещения узлов. Усилия —
/// первыми и отдельным объектом результатов: после запроса перемещений LiraResAPI 2024 отдаёт в усилиях того же
/// объекта мусор (проверено 13.10). Перемещения узлов API отдаёт в метрах и радианах независимо от единиц документа
/// (проверено 10.11 на «Мирной»: осадка ЗН1 4,3 мм при единице перемещений «мм»; ручная сверка 13.10 с CSfea — в метрах).
/// </summary>
static class LiraApiMaskSnapshotReader
{
    const double TonToKn = 9.80665;

    public sealed record Result(string DocumentName, LiraApiUnits Units, IReadOnlyList<LiraRawLoad> Loads);

    /// <param name="progress">Доля готовности 0…1 по загружениям (усилия — 90 %, перемещения — 10 %).</param>
    public static Result Read(IReadOnlyList<int> nodes, IReadOnlyList<int> bars, IReadOnlyList<int> shells,
        IProgress<double>? progress, CancellationToken ct)
    {
        dynamic lira = LiraComConnector.ConnectApplication();
        dynamic doc = lira.ActiveDocument
            ?? throw new InvalidOperationException("В ЛИРЕ нет открытого документа. Откройте расчётную схему и повторите.");
        string docName = Path.GetFileNameWithoutExtension((string)doc.PathName);
        LiraApiUnits units;
        try
        {
            dynamic mu = lira.MeasurementUnits;
            units = LiraApiUnits.FromCodes((int)mu.Forces1, (int)mu.Forces2, TonToKn);
        }
        catch { units = LiraApiUnits.Identity; }
        Dictionary<int, string> names;
        try { names = LiraApiSchemaReader.ReadLoadCaseNames(lira.ActiveDocument); }
        catch { names = []; }

        // Усилия.
        var api = LiraComConnector.CreateResultsAccess();
        dynamic req = api.CreateRequest("kLiraRequest_LoadCaseForces");
        req.DocumentName = docName;
        req.Elements.AddFromString(Range(bars.Concat(shells)));
        dynamic fr = api.Access.LoadCaseForces(req);
        var loadNums = new List<int>();
        dynamic cases = fr.LoadCases;
        for (int i = 0, n = (int)cases.Count; i < n; i++) loadNums.Add((int)cases.Item[i].Number);

        var sections = new Dictionary<int, int>(bars.Count);
        foreach (int e in bars)
        {
            int count;
            try { count = (int)fr.GetSectionCount(e); } catch { count = 0; }
            sections[e] = count;
        }
        var barsWithForces = bars.Where(e => sections[e] >= 2).ToArray();

        var raw = new List<(int Num, int[] Bars, double[] BarValues, int[] Shells, double[] ShellValues)>(loadNums.Count);
        for (int li = 0; li < loadNums.Count; li++)
        {
            ct.ThrowIfCancellationRequested();
            int lc = loadNums[li];
            var barValues = new double[barsWithForces.Length * ImportedResultLoadBarComponents];
            for (int b = 0; b < barsWithForces.Length; b++)
            {
                int e = barsWithForces[b], o = b * ImportedResultLoadBarComponents;
                foreach (int sec in new[] { 1, sections[e] })
                {
                    barValues[o++] = fr.GetBarN(e, sec, lc); barValues[o++] = fr.GetBarQy(e, sec, lc);
                    barValues[o++] = fr.GetBarQz(e, sec, lc); barValues[o++] = fr.GetBarMx(e, sec, lc);
                    barValues[o++] = fr.GetBarMy(e, sec, lc); barValues[o++] = fr.GetBarMz(e, sec, lc);
                }
            }
            var shellValues = new double[shells.Count * ImportedResultLoadShellComponents];
            for (int s = 0; s < shells.Count; s++)
            {
                int e = shells[s], o = s * ImportedResultLoadShellComponents;
                shellValues[o] = fr.GetPlateNx(e, 1, lc); shellValues[o + 1] = fr.GetPlateNy(e, 1, lc);
                shellValues[o + 2] = fr.GetPlateTxy(e, 1, lc); shellValues[o + 3] = fr.GetPlateMx(e, 1, lc);
                shellValues[o + 4] = fr.GetPlateMy(e, 1, lc); shellValues[o + 5] = fr.GetPlateMxy(e, 1, lc);
                shellValues[o + 6] = fr.GetPlateQx(e, 1, lc); shellValues[o + 7] = fr.GetPlateQy(e, 1, lc);
            }
            raw.Add((lc, barsWithForces, barValues, [.. shells], shellValues));
            progress?.Report(0.9 * (li + 1) / loadNums.Count);
        }

        // Перемещения — новым объектом результатов.
        var api2 = LiraComConnector.CreateResultsAccess();
        dynamic dreq = api2.CreateRequest("kLiraRequest_LoadCaseDisplacement");
        dreq.DocumentName = docName;
        dreq.Nodes.AddFromString(Range(nodes));
        dynamic dr = api2.Access.LoadCaseDisplacements(dreq);
        var loads = new List<LiraRawLoad>(raw.Count);
        for (int li = 0; li < raw.Count; li++)
        {
            ct.ThrowIfCancellationRequested();
            int lc = raw[li].Num;
            var d = new double[nodes.Count * 6];
            for (int i = 0; i < nodes.Count; i++)
            {
                int n = nodes[i];
                d[6 * i] = dr.GetNodeX(n, lc); d[6 * i + 1] = dr.GetNodeY(n, lc); d[6 * i + 2] = dr.GetNodeZ(n, lc);
                d[6 * i + 3] = dr.GetNodeUX(n, lc); d[6 * i + 4] = dr.GetNodeUY(n, lc); d[6 * i + 5] = dr.GetNodeUZ(n, lc);
            }
            loads.Add(new LiraRawLoad(lc, names.GetValueOrDefault(lc) ?? $"ЗН {lc}", [.. nodes], d,
                raw[li].Bars, raw[li].BarValues, raw[li].Shells, raw[li].ShellValues));
            progress?.Report(0.9 + 0.1 * (li + 1) / raw.Count);
        }
        return new Result(docName, units, loads);
    }

    const int ImportedResultLoadBarComponents = CScore.Fem.Fragment.Masks.ImportedResultLoad.BarComponents;
    const int ImportedResultLoadShellComponents = CScore.Fem.Fragment.Masks.ImportedResultLoad.ShellComponents;

    static string Range(IEnumerable<int> ids)
    {
        var sorted = ids.Distinct().Order().ToList();
        if (sorted.Count == 0) return "";
        return sorted[^1] - sorted[0] + 1 == sorted.Count ? $"{sorted[0]}-{sorted[^1]}" : string.Join(",", sorted);
    }
}
