using System.Reflection;
using System.Text;
using CScore.Import;

namespace OpenCS.Services;

/// <summary>
/// Читает топологию расчётной схемы из открытого документа ЛираСАПР через COM (dynamic).
/// GetContents — ByRef Sub (VBA: tbl.GetContents data), вызываем через ref или InvokeMember.
/// </summary>
static class LiraApiSchemaReader
{
    const int kNodesTable    = 2;   // kLiraTable_Nodes_Coordinates
    const int kElementsTable = 3;   // kLiraTable_Elements_TypeAndNumbersOfNodes

    public static LiraSchemaData Read()
    {
        var appType = Type.GetTypeFromProgID("LiraSapr.Application")
            ?? throw new InvalidOperationException(
                "ProgID 'LiraSapr.Application' не зарегистрирован. " +
                "Убедитесь, что ЛираСАПР установлена и зарегистрирована (/register).");

        dynamic lira = Activator.CreateInstance(appType)!;

        dynamic doc = lira.ActiveDocument
            ?? throw new InvalidOperationException(
                "В ЛираСАПР нет открытого документа. Откройте расчётную схему и повторите.");

        var data = new LiraSchemaData();
        var diag = new List<string>();

        // Стратегия 1: CreateNewItem + GetContents(ref data)
        var nodesRaw = TryReadTable(doc.AllTables.CreateNewItem(kNodesTable), diag, "S1-Nodes");
        if (nodesRaw != null) ParseNodes(nodesRaw, data);

        var elemsRaw = TryReadTable(doc.AllTables.CreateNewItem(kElementsTable), diag, "S1-Elems");
        if (elemsRaw != null) ParseElements(elemsRaw, data);

        // Стратегия 2: AllTables.Item[key] + GetContents(ref data)
        if (data.Nodes.Count == 0)
        {
            nodesRaw = TryReadTable(GetItem(doc.AllTables, kNodesTable, diag, "S2-Nodes"), diag, "S2-Nodes");
            if (nodesRaw != null) ParseNodes(nodesRaw, data);
        }
        if (data.Elements.Count == 0)
        {
            elemsRaw = TryReadTable(GetItem(doc.AllTables, kElementsTable, diag, "S2-Elems"), diag, "S2-Elems");
            if (elemsRaw != null) ParseElements(elemsRaw, data);
        }

        if (data.Nodes.Count == 0 && data.Elements.Count == 0)
        {
            var probe = ProbeDocument(doc);
            throw new InvalidOperationException(
                "Не удалось прочитать топологию ЛираСАПР.\n\n" +
                "Журнал:\n" + string.Join("\n", diag) +
                "\n\nДиагностика:\n" + probe);
        }

        return data;
    }

    // ------------------------------------------------------------------ доступ к таблице

    static object? GetItem(dynamic collection, int key, List<string> diag, string tag)
    {
        try
        {
            dynamic item = collection.Item[key];
            diag.Add($"  OK {tag}-Item[{key}]: {item.GetType().Name}");
            return item;
        }
        catch (Exception ex)
        {
            diag.Add($"  ERR {tag}-Item[{key}]: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Вызывает GetContents через ByRef-паттерн (VBA: Sub GetContents(ByRef data As Variant)).
    /// Пробует три способа, фиксирует результат в diag.
    /// </summary>
    static object[,]? TryReadTable(object? comObj, List<string> diag, string tag)
    {
        if (comObj == null) return null;

        // Способ А: dynamic + ref object
        try
        {
            dynamic d = comObj;
            object rawData = null!;
            d.GetContents(ref rawData);
            var arr = rawData as object[,];
            int rows = arr?.GetLength(0) ?? 0;
            diag.Add($"  OK {tag} (dynref): rows={rows}, type={rawData?.GetType().Name ?? "null"}");
            return rows > 0 ? arr : null;
        }
        catch (Exception ex)
        {
            diag.Add($"  ERR {tag} (dynref): {ex.GetType().Name}: {ex.Message}");
        }

        // Способ Б: InvokeMember с by-ref массивом
        try
        {
            var args = new object[] { null! };
            comObj.GetType().InvokeMember("GetContents",
                BindingFlags.InvokeMethod, null, comObj, args);
            var arr = args[0] as object[,];
            int rows = arr?.GetLength(0) ?? 0;
            diag.Add($"  OK {tag} (InvokeMember): rows={rows}, arg0type={args[0]?.GetType().Name ?? "null"}");
            return rows > 0 ? arr : null;
        }
        catch (Exception ex)
        {
            diag.Add($"  ERR {tag} (InvokeMember): {ex.GetType().Name}: {ex.Message}");
        }

        // Способ В: InvokeMember через IDispatch-style (DISPID)
        try
        {
            var parms = new System.Runtime.InteropServices.DispatchWrapper[] {
                new System.Runtime.InteropServices.DispatchWrapper(null)
            };
            comObj.GetType().InvokeMember("GetContents",
                BindingFlags.InvokeMethod, null, comObj, parms);
            diag.Add($"  OK {tag} (DispatchWrapper): completed");
        }
        catch (Exception ex)
        {
            diag.Add($"  ERR {tag} (DispatchWrapper): {ex.GetType().Name}: {ex.Message}");
        }

        return null;
    }

    // ------------------------------------------------------------------ диагностика

    static string ProbeDocument(dynamic doc)
    {
        var sb = new StringBuilder();

        Probe(sb, "AllTables.Item[2].GetContents(ref)", () => {
            dynamic t = doc.AllTables.Item[2];
            object d = null!;
            t.GetContents(ref d);
            return $"arg0={d?.GetType().Name ?? "null"}";
        });

        Probe(sb, "AllTables.Item[2] InvokeMember", () => {
            dynamic t = doc.AllTables.Item[2];
            var args = new object[] { null! };
            ((object)t).GetType().InvokeMember("GetContents",
                BindingFlags.InvokeMethod, null, (object)t, args);
            return $"arg0={args[0]?.GetType().Name ?? "null"}";
        });

        // Перебор доступных методов через COM TypeInfo
        Probe(sb, "AllTables.Item[2] TypeInfo methods", () => {
            dynamic t = doc.AllTables.Item[2];
            object comObj = t;
            var typeInfo = comObj.GetType();
            var methods = typeInfo.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .Select(m => m.Name).Distinct().Take(30);
            return string.Join(", ", methods);
        });

        // Пробуем другие имена метода
        foreach (var mname in new[] { "ReadData", "GetData", "GetTable", "Read", "Load", "Fetch", "Items" })
        {
            string mn = mname;
            Probe(sb, $"AllTables.Item[2].{mname}()", () => {
                dynamic t = doc.AllTables.Item[2];
                var args = new object[0];
                var ret = ((object)t).GetType().InvokeMember(mn,
                    BindingFlags.InvokeMethod, null, (object)t, args);
                return ret?.ToString() ?? "null";
            });
        }

        return sb.ToString();
    }

    static void Probe(StringBuilder sb, string label, Func<string> f)
    {
        try   { sb.AppendLine($"  OK  {label} = {f()}"); }
        catch (Exception ex) { sb.AppendLine($"  ERR {label}: {ex.GetType().Name}: {ex.Message}"); }
    }

    // ------------------------------------------------------------------ парсинг

    static void ParseNodes(object[,] rows, LiraSchemaData data)
    {
        int count = rows.GetLength(0);
        for (int i = 0; i < count; i++)
        {
            if (!TryInt(rows[i, 0], out int id)) continue;
            double x = ToDouble(rows[i, 1]);
            double y = ToDouble(rows[i, 2]);
            double z = ToDouble(rows[i, 3]);
            int dofMask = ParseDofMask(rows, i, 4);
            data.Nodes.Add(new LiraNodeRecord(id, x, y, z, dofMask));
        }
    }

    static void ParseElements(object[,] rows, LiraSchemaData data)
    {
        int count = rows.GetLength(0);
        int cols  = rows.GetLength(1);
        for (int i = 0; i < count; i++)
        {
            if (!TryInt(rows[i, 0], out int id)) continue;
            if (!TryInt(rows[i, 1], out int feType)) continue;

            int secCount = 0, stiffId = 0;
            string nodeIdsStr;

            if (cols == 3)
            {
                nodeIdsStr = rows[i, 2]?.ToString() ?? "";
            }
            else
            {
                TryInt(rows[i, 2], out secCount);
                TryInt(rows[i, 3], out stiffId);
                nodeIdsStr = rows[i, cols - 1]?.ToString() ?? "";
            }

            var nodeIds = ParseNodeIds(nodeIdsStr);
            if (nodeIds.Length == 0) continue;
            data.Elements.Add(new LiraElementRecord(id, feType, secCount, stiffId, nodeIds));
        }
    }

    static int[] ParseNodeIds(string s)
    {
        var parts = s.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        var result = new List<int>();
        foreach (var p in parts)
            if (int.TryParse(p.Trim(), out int n) && n > 0)
                result.Add(n);
        return [.. result];
    }

    static int ParseDofMask(object[,] rows, int row, int startCol)
    {
        int mask = 0;
        int cols = rows.GetLength(1);
        for (int bit = 0; bit < 7 && startCol + bit < cols; bit++)
            if (rows[row, startCol + bit]?.ToString() == "1")
                mask |= (1 << bit);
        return mask;
    }

    static bool TryInt(object? cell, out int value)
    {
        value = 0;
        if (cell == null) return false;
        return int.TryParse(cell.ToString(), out value);
    }

    static double ToDouble(object? cell)
    {
        if (cell == null) return 0;
        return cell is double d ? d : double.TryParse(cell.ToString(), out double v) ? v : 0;
    }
}
