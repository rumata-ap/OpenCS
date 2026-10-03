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
    const int kNodesTable              = 2;   // kLiraTable_Nodes_Coordinates
    const int kElementsTable           = 3;   // kLiraTable_Elements_TypeAndNumbersOfNodes
    const int kStiffnessesTable        = 9;   // kLiraTable_Stiffnesses: жёсткости схемы
    const int kElementsStiffnessTable  = 10;  // kLiraTable_Elements_Stiffnesses: КЭ → номер жёсткости
    const int kConstructiveBlocksTable = 31;  // конструктивные блоки
    const int kLoadCasesTable          = 25;  // номер загружения + имя + тип
    const int kPlateLocalAxesTable     = 18;  // kLiraTable_Plates_LocalAxes: КЭ → угол согласования осей
    const int kElementsPRTypesTable    = 33;  // kLiraTable_Elements_PRTypes: КЭ → ТЗА (только 2025+)

    /// <summary>Первая версия ЛИРЫ с таблицей «Элементы - ТЗА».</summary>
    public const int FirstVersionWithReinforcementTypes = 2025;

    /// <summary>Имя открытой схемы: Title документа, иначе имя файла без расширения из PathName.</summary>
    static string? ReadDocumentTitle(dynamic doc)
    {
        try
        {
            string? title = doc.Title as string;
            if (!string.IsNullOrWhiteSpace(title)) return title.Trim();
        }
        catch (Exception) { }
        try
        {
            string? path = doc.PathName as string;
            if (!string.IsNullOrWhiteSpace(path)) return System.IO.Path.GetFileNameWithoutExtension(path);
        }
        catch (Exception) { }
        return null;
    }

    /// <summary>
    /// Читает имена загружений из таблицы 25.
    /// Возвращает словарь: номер загружения → имя.
    /// </summary>
    public static Dictionary<int, string> ReadLoadCaseNames(dynamic doc)
    {
        var result = new Dictionary<int, string>();
        var diag = new List<string>();
        var raw = TryReadTable(doc.AllTables.CreateNewItem(kLoadCasesTable), diag, "LoadCases");
        if (raw == null) return result;
        int rows = raw.GetLength(0);
        for (int i = 0; i < rows; i++)
        {
            if (!TryInt(raw[i, 0], out int lcNum)) continue;
            string name = raw[i, 1]?.ToString() ?? $"ЗН {lcNum}";
            if (string.IsNullOrWhiteSpace(name)) name = $"ЗН {lcNum}";
            result[lcNum] = name;
        }
        return result;
    }

    /// <summary>Прочитать схему из запущенной ЛИРЫ.</summary>
    /// <param name="liraVersion">Год версии ЛИРЫ из ProgID (null — не определена).</param>
    /// <param name="documentTitle">Имя открытой схемы (Title документа, иначе имя файла из PathName);
    /// null — ЛИРА его не отдала.</param>
    public static LiraSchemaData Read(out int? liraVersion, out string? documentTitle)
    {
        dynamic lira = LiraComConnector.ConnectApplication(out liraVersion);

        dynamic doc = lira.ActiveDocument
            ?? throw new InvalidOperationException(
                "В ЛИРЕ нет открытого документа. Откройте расчётную схему и повторите.");
        documentTitle = ReadDocumentTitle(doc);

        var data = new LiraSchemaData();
        var diag = new List<string>();
        // Таблица узлов отдаёт координаты в единицах геометрии документа (м, см или мм), схема OpenCS — в метрах.
        double geometryUnitM = GeometryUnitM((object)lira);

        // Стратегия 1: CreateNewItem + GetContents(ref data)
        var nodesRaw = TryReadTable(doc.AllTables.CreateNewItem(kNodesTable), diag, "S1-Nodes");
        if (nodesRaw != null) ParseNodes(nodesRaw, geometryUnitM, data);

        var elemsRaw = TryReadTable(doc.AllTables.CreateNewItem(kElementsTable), diag, "S1-Elems");
        if (elemsRaw != null) ParseElements(elemsRaw, data);

        // Конструктивные блоки (не критично — если таблицы нет, просто пропускаем)
        var blocksRaw = TryReadTable(doc.AllTables.CreateNewItem(kConstructiveBlocksTable), diag, "S1-Blocks");
        if (blocksRaw != null) ParseConstructiveBlocks(blocksRaw, data);

        // Стратегия 2: AllTables.Item[key] + GetContents(ref data)
        if (data.Nodes.Count == 0)
        {
            nodesRaw = TryReadTable(GetItem(doc.AllTables, kNodesTable, diag, "S2-Nodes"), diag, "S2-Nodes");
            if (nodesRaw != null) ParseNodes(nodesRaw, geometryUnitM, data);
        }
        if (data.Elements.Count == 0)
        {
            elemsRaw = TryReadTable(GetItem(doc.AllTables, kElementsTable, diag, "S2-Elems"), diag, "S2-Elems");
            if (elemsRaw != null) ParseElements(elemsRaw, data);
        }
        if (data.ConstructiveBlocks.Count == 0)
        {
            blocksRaw = TryReadTable(GetItem(doc.AllTables, kConstructiveBlocksTable, diag, "S2-Blocks"), diag, "S2-Blocks");
            if (blocksRaw != null) ParseConstructiveBlocks(blocksRaw, data);
        }

        // Жёсткости: размеры сечений стержней и толщины пластин (не критично: без таблиц группы остаются «Жёсткость 0»)
        ReadStiffnessTables((object)lira, (object)doc, data, diag);

        // Согласованные местные оси пластин — оси выдачи усилий (не критично: без таблицы оси считаются неизвестными)
        var axesRaw = TryReadTable(doc.AllTables.CreateNewItem(kPlateLocalAxesTable), diag, "S1-PlateAxes");
        if (axesRaw != null) ParsePlateAxisAngles(axesRaw, data);

        // ТЗА КЭ — только в 2025+; в старых версиях таблицы нет, не запрашиваем
        if (liraVersion >= FirstVersionWithReinforcementTypes)
        {
            var prRaw = TryReadTable(doc.AllTables.CreateNewItem(kElementsPRTypesTable), diag, "S1-PRTypes");
            if (prRaw != null) ParseElementReinforcementTypes(prRaw, data);
        }

        if (data.Nodes.Count == 0 && data.Elements.Count == 0)
        {
            var probe = ProbeDocument(doc);
            throw new InvalidOperationException(
                "Не удалось прочитать схему ЛИРЫ.\n\n" +
                "Журнал:\n" + string.Join("\n", diag) +
                "\n\nДиагностика:\n" + probe);
        }

        return data;
    }

    /// <summary>
    /// Прочитать из запущенной ЛИРЫ только таблицу «Элементы - ТЗА»: номер КЭ → номера ТЗА.
    /// Нужна схемам, импортированным до того, как номера ТЗА стали сохраняться у стержней.
    /// </summary>
    /// <param name="liraVersion">Год версии ЛИРЫ из ProgID (null — не определена).</param>
    /// <exception cref="InvalidOperationException">Нет открытого документа, версия старше 2025 или таблица не читается.</exception>
    public static Dictionary<int, int[]> ReadElementReinforcementTypes(out int? liraVersion)
    {
        dynamic lira = LiraComConnector.ConnectApplication(out liraVersion);
        dynamic doc = lira.ActiveDocument
            ?? throw new InvalidOperationException(
                "В ЛИРЕ нет открытого документа. Откройте расчётную схему и повторите.");
        if (!(liraVersion >= FirstVersionWithReinforcementTypes))
            throw new InvalidOperationException(
                $"Таблица «Элементы - ТЗА» есть только в ЛИРА-САПФИР {FirstVersionWithReinforcementTypes} и новее.");

        var diag = new List<string>();
        var raw = TryReadTable(doc.AllTables.CreateNewItem(kElementsPRTypesTable), diag, "PRTypes");
        if (raw == null)
            throw new InvalidOperationException("Не удалось прочитать таблицу «Элементы - ТЗА».\n" + string.Join("\n", diag));
        var data = new LiraSchemaData();
        ParseElementReinforcementTypes(raw, data);
        return data.ElementReinforcementTypes;
    }

    /// <summary>
    /// Прочитать из запущенной ЛИРЫ только жёсткости схемы (таблицы «Жёсткости» и «Элементы - жёсткости»).
    /// Нужна схемам, импортированным до того, как жёсткости стали сохраняться при схеме.
    /// </summary>
    /// <returns>Жёсткости и номер жёсткости по номеру КЭ.</returns>
    /// <exception cref="InvalidOperationException">Нет открытого документа или таблицы не читаются.</exception>
    public static (List<LiraStiffnessRecord> Stiffnesses, Dictionary<int, int> ElementStiffness) ReadStiffnesses()
    {
        dynamic lira = LiraComConnector.ConnectApplication();
        dynamic doc = lira.ActiveDocument
            ?? throw new InvalidOperationException(
                "В ЛИРЕ нет открытого документа. Откройте расчётную схему и повторите.");

        var diag = new List<string>();
        var data = new LiraSchemaData();
        Dictionary<int, int> byElement = ReadStiffnessTables((object)lira, (object)doc, data, diag);
        if (data.Stiffnesses.Count == 0 || byElement.Count == 0)
            throw new InvalidOperationException("Не удалось прочитать таблицы жёсткостей.\n" + string.Join("\n", diag));

        // КЭ, которые импорт в схему не переносит (одноузловые связи и т. п.), в схеме OpenCS искать бессмысленно.
        object[,]? elemsRaw = TryReadTable((object)doc.AllTables.CreateNewItem(kElementsTable), diag, "Elems");
        if (elemsRaw != null)
        {
            var elements = new LiraSchemaData();
            ParseElements(elemsRaw, elements);
            foreach (var e in elements.Elements.Where(e => !LiraSchemaData.IsImported(e)))
                byElement.Remove(e.Id);
        }
        return (data.Stiffnesses, byElement);
    }

    /// <summary>Таблицы 9 и 10: жёсткости схемы и жёсткость каждого КЭ; результат применяется к <paramref name="data"/>.</summary>
    static Dictionary<int, int> ReadStiffnessTables(object liraApp, object document, LiraSchemaData data, List<string> diag)
    {
        dynamic doc = document;
        var byElement = new Dictionary<int, int>();
        object[,]? stiffRaw = TryReadTable((object)doc.AllTables.CreateNewItem(kStiffnessesTable), diag, "Stiffnesses");
        if (stiffRaw == null) return byElement;
        ParseStiffnesses(stiffRaw, SectionUnitM(liraApp), data);

        object[,]? elemRaw = TryReadTable((object)doc.AllTables.CreateNewItem(kElementsStiffnessTable), diag, "ElemStiffness");
        if (elemRaw != null) ParseElementStiffnesses(elemRaw, byElement);
        data.ApplyStiffnesses(byElement);
        return byElement;
    }

    /// <summary>Единица координат узлов документа в метрах (LiraUnitsGeometryEnum);
    /// не читается — метры, как по умолчанию в ЛИРЕ.</summary>
    internal static double GeometryUnitM(object liraApp)
    {
        dynamic lira = liraApp;
        try { return LiraApiUnits.LengthToM((int)lira.MeasurementUnits.Geometry, 1.0); }
        catch (Exception) { return 1.0; }
    }

    /// <summary>Единица размеров сечений документа в метрах (LiraUnitsGeometryEnum);
    /// не читается — сантиметры, как по умолчанию в ЛИРЕ.</summary>
    static double SectionUnitM(object liraApp)
    {
        dynamic lira = liraApp;
        try { return LiraApiUnits.LengthToM((int)lira.MeasurementUnits.Sections, 0.01); }
        catch (Exception) { return 0.01; }
    }

    /// <summary>
    /// Прочитать из запущенной ЛИРЫ только таблицу «местные оси пластин»: номер КЭ → угол согласования
    /// местных осей, град. Нужна схемам, импортированным до того, как угол стал сохраняться у КЭ.
    /// </summary>
    /// <exception cref="InvalidOperationException">Нет открытого документа или таблица не читается.</exception>
    public static Dictionary<int, double> ReadPlateAxisAngles()
    {
        dynamic lira = LiraComConnector.ConnectApplication();
        dynamic doc = lira.ActiveDocument
            ?? throw new InvalidOperationException(
                "В ЛИРЕ нет открытого документа. Откройте расчётную схему и повторите.");

        var diag = new List<string>();
        var raw = TryReadTable(doc.AllTables.CreateNewItem(kPlateLocalAxesTable), diag, "PlateAxes");
        if (raw == null)
            throw new InvalidOperationException("Не удалось прочитать таблицу местных осей пластин.\n" + string.Join("\n", diag));
        var data = new LiraSchemaData();
        ParsePlateAxisAngles(raw, data);
        return data.PlateAxisAngles;
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

    /// <summary>Таблица узлов: номер, x, y, z (в единицах геометрии документа) и закрепления.</summary>
    /// <param name="unitM">Единица координат документа в метрах.</param>
    internal static void ParseNodes(object[,] rows, double unitM, LiraSchemaData data)
    {
        int count = rows.GetLength(0);
        for (int i = 0; i < count; i++)
        {
            if (!TryInt(rows[i, 0], out int id)) continue;
            double x = ToDouble(rows[i, 1]) * unitM;
            double y = ToDouble(rows[i, 2]) * unitM;
            double z = ToDouble(rows[i, 3]) * unitM;
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

    /// <summary>
    /// Парсит таблицу 9 «Жёсткости»: [0] = номер, [1] = код вида, [2] = цвет, [3] = имя, [4] = параметры.
    /// </summary>
    internal static void ParseStiffnesses(object[,] rows, double sectionUnitM, LiraSchemaData data)
    {
        if (rows.GetLength(1) < 5) return;
        for (int i = 0; i < rows.GetLength(0); i++)
        {
            if (!TryInt(rows[i, 0], out int id)) continue;
            TryInt(rows[i, 1], out int kind);
            data.Stiffnesses.Add(new LiraStiffnessRecord(id, kind,
                rows[i, 3]?.ToString()?.Trim() ?? "", rows[i, 4]?.ToString()?.Trim() ?? "", sectionUnitM));
        }
    }

    /// <summary>Парсит таблицу 10 «Элементы - жёсткости»: [0] = номер КЭ, [1] = номер жёсткости.</summary>
    internal static void ParseElementStiffnesses(object[,] rows, Dictionary<int, int> byElement)
    {
        if (rows.GetLength(1) < 2) return;
        for (int i = 0; i < rows.GetLength(0); i++)
            if (TryInt(rows[i, 0], out int id) && TryInt(rows[i, 1], out int num) && num > 0)
                byElement[id] = num;
    }

    /// <summary>
    /// Парсит таблицу 18 «местные оси пластин»: [0] = номер КЭ, [1] = угол согласования осей, град.
    /// </summary>
    static void ParsePlateAxisAngles(object[,] rows, LiraSchemaData data)
    {
        if (rows.GetLength(1) < 2) return;
        for (int i = 0; i < rows.GetLength(0); i++)
        {
            if (!TryInt(rows[i, 0], out int id)) continue;
            double angle = ToDouble(rows[i, 1]);
            if (double.IsFinite(angle)) data.PlateAxisAngles[id] = angle;
        }
    }

    /// <summary>
    /// Парсит таблицу 33 «Элементы - ТЗА»: [0] = номер КЭ, [1] = номера ТЗА через пробел («1 2 4»).
    /// </summary>
    static void ParseElementReinforcementTypes(object[,] rows, LiraSchemaData data)
    {
        if (rows.GetLength(1) < 2) return;
        for (int i = 0; i < rows.GetLength(0); i++)
        {
            if (!TryInt(rows[i, 0], out int id)) continue;
            IReadOnlyList<int> ids;
            try { ids = CScore.Import.LiraPlateReinforcementAssembler.ParseTypeIds(rows[i, 1]?.ToString()); }
            catch (FormatException) { continue; }
            if (ids.Count > 0) data.ElementReinforcementTypes[id] = [.. ids];
        }
    }

    /// <summary>
    /// Парсит таблицу 31 (конструктивные блоки).
    /// Колонки: [0]=Список номеров КЭ (ranges "1-10,15"), [1]=Вд КоБ, [2]=Id, [3]=Этаж, [4]=Марка, [5]=Комментарий, [6]=Цвет.
    /// </summary>
    static void ParseConstructiveBlocks(object[,] rows, LiraSchemaData data)
    {
        int count = rows.GetLength(0);
        int cols  = rows.GetLength(1);
        for (int i = 0; i < count; i++)
        {
            // Id блока — колонка 2 (если есть), иначе генерируем
            int id = cols > 2 && TryInt(rows[i, 2], out int bid) ? bid : i + 1;
            string type    = cols > 0 ? rows[i, 0]?.ToString() ?? "" : ""; // Вд КоБ
            string floor   = cols > 3 ? rows[i, 3]?.ToString() ?? "" : ""; // Этаж
            string mark    = cols > 4 ? rows[i, 4]?.ToString() ?? "" : ""; // Марка
            string comment = cols > 5 ? rows[i, 5]?.ToString() ?? "" : ""; // Комментарий

            // Номера КЭ — колонка 0 (ranges "27764-27789" или "1,2,3-10")
            // В пользовательских данных колонки 0 и 1 поменяны местами относительно
            // стандартного порядка: [0]=Список КЭ, [1]=Вд КоБ, [2]=Id...
            // Пробуем определить: если col[0] содержит цифры/дефисы — это список КЭ,
            // иначе col[0] = Вд КоБ, а col[1] = список КЭ (старый порядок).
            string elemStr;
            if (cols > 1 && LooksLikeElementRange(rows[i, 0]))
            {
                // Новый порядок: [0]=КЭ, [1]=Вд КоБ, [2]=Id...
                elemStr = rows[i, 0]?.ToString() ?? "";
                type    = rows[i, 1]?.ToString() ?? "";
            }
            else if (cols > 1)
            {
                // Старый порядок: [0]=Вд КоБ, [1]=КЭ, [2]=Id...
                type    = rows[i, 0]?.ToString() ?? "";
                elemStr = rows[i, 1]?.ToString() ?? "";
            }
            else
            {
                continue;
            }

            var elemIds = ExpandElementRanges(elemStr);
            if (elemIds.Length == 0) continue;

            data.ConstructiveBlocks.Add(new LiraConstructiveBlockRecord(
                id, type, floor, mark, comment, elemIds));
        }
    }

    /// <summary>Проверяет, похоже ли значение ячейки на диапазон номеров КЭ (содержит цифры и дефисы).</summary>
    static bool LooksLikeElementRange(object? cell)
    {
        if (cell == null) return false;
        var s = cell.ToString();
        if (string.IsNullOrWhiteSpace(s)) return false;
        // Строка вида "27764-27789" или "1,2,3-10"
        bool hasDigit = false;
        foreach (char c in s)
        {
            if (char.IsDigit(c)) hasDigit = true;
            else if (c != '-' && c != ',' && c != ' ' && c != ';') return false;
        }
        return hasDigit;
    }

    /// <summary>Разворачивает строку диапазонов КЭ ("1-5,8,10-12") в массив номеров.</summary>
    static int[] ExpandElementRanges(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return [];
        var result = new List<int>();
        var parts = s.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            var dashIdx = trimmed.IndexOf('-');
            if (dashIdx > 0 && int.TryParse(trimmed[..dashIdx], out int lo)
                && int.TryParse(trimmed[(dashIdx + 1)..], out int hi))
            {
                for (int n = lo; n <= hi; n++) result.Add(n);
            }
            else if (int.TryParse(trimmed, out int n))
            {
                result.Add(n);
            }
        }
        return [.. result];
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
