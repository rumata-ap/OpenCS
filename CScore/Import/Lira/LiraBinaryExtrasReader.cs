using System.Buffers.Binary;
using System.Text;

namespace CScore.Import;

/// <summary>Тип цели низкоуровневой записи нагрузки в бинарном .lir.</summary>
public enum LiraLoadTargetKind
{
    /// <summary>Узел схемы.</summary>
    Node,

    /// <summary>Конечный элемент.</summary>
    Element
}

/// <summary>Загружение схемы: номер и имя из бинарного .lir (порядок записей — порядок загружений).</summary>
/// <param name="Id">Номер загружения (1-based, как в ЛИРЕ).</param>
/// <param name="Name">Имя загружения.</param>
public sealed record LiraLoadCaseRecord(int Id, string Name);

/// <summary>
/// Низкоуровневая (МКЭ) запись нагрузки из бинарного .lir — то, что в текстовом экспорте ЛИРЫ лежит
/// в документах 6 и 7. Параметры приведены к порядку текстового экспорта
/// (<c>P0…P3</c>), исходный порядок байтового payload сохранён отдельно (<see cref="NativeParameters"/>).
/// </summary>
/// <param name="TargetKind">Род цели: узел или элемент.</param>
/// <param name="TargetId">Номер узла или элемента ЛИРЫ (1-based).</param>
/// <param name="LoadCaseId">Номер загружения (1-based).</param>
/// <param name="LoadType">Тип нагрузки текстового экспорта: 0, 6, 7, 15, 16, 19.</param>
/// <param name="DirectionCode">Код направления (1–5) без смены знака. Для узловых — <c>код − 100</c>.</param>
/// <param name="ParameterCount">Число действительных параметров (1–4).</param>
/// <param name="P0">Параметр 1 в порядке TXT (интенсивность или локальная координата).</param>
/// <param name="P1">Параметр 2 в порядке TXT.</param>
/// <param name="P2">Параметр 3 в порядке TXT (0, если параметра нет).</param>
/// <param name="P3">Параметр 4 в порядке TXT (0, если параметра нет).</param>
/// <param name="SerializedTag">Тег записи нагрузки в файле (0 — узловая; 2, 10, 12, 14, 28 — элементные).</param>
/// <param name="SerializedCode">Код записи как в файле (для элементных <c>тип·10 + направление</c>, для узловых 101–106).</param>
/// <param name="SourceGroupRaw">Исходное поле группы записи (смысл не установлен; не является номером загружения).</param>
/// <param name="NativeParameters">Payload в порядке файла, в виде <c>float32</c> без перестановки.</param>
/// <param name="BinaryOffset">Смещение начала записи в файле (для сверки с байтами).</param>
public sealed record LiraBinaryLoad(
    LiraLoadTargetKind TargetKind,
    int TargetId,
    int LoadCaseId,
    int LoadType,
    int DirectionCode,
    int ParameterCount,
    double P0,
    double P1,
    double P2,
    double P3,
    int SerializedTag,
    int SerializedCode,
    int SourceGroupRaw,
    double[] NativeParameters,
    long BinaryOffset);

/// <summary>
/// Результат чтения дополнительных блоков бинарного .lir (жёсткости, загружения, физические нагрузки).
/// </summary>
/// <param name="StiffnessByElement">Номер жёсткости по индексу записи элемента (0 — не найден/не назначена). Пусто — массив не найден.</param>
/// <param name="LoadCases">Загружения (номер и имя). Пусто — имена не прочитаны.</param>
/// <param name="Loads">Записи физических нагрузок в порядке файла.</param>
/// <param name="Notes">Диагностические сообщения о том, какие блоки прочитать не удалось и почему.</param>
public sealed record LiraBinaryExtras(
    int[] StiffnessByElement,
    List<LiraLoadCaseRecord> LoadCases,
    List<LiraBinaryLoad> Loads,
    List<string> Notes);

/// <summary>
/// Чтение дополнительных блоков бинарного .lir после разбора геометрии: назначение жёсткостей КЭ,
/// имена загружений и записи физических нагрузок.
/// </summary>
/// <remarks>
/// <para>Формат изучен на контрольном файле <c>vtr_stlb_…_2026-10-09.lir</c>
/// (114 309 узлов / 161 823 КЭ / 1 007 757 записей нагрузок), сверенном с текстовым экспортом ЛИРЫ.
/// Описание блоков — <c>docs/superpowers/specs/2026-10-29-lira-binary-loads-design.md</c>.</para>
/// <para>Принцип: смещения блоков заранее неизвестны, поэтому каждый блок ищется по
/// <b>самопроверяющейся сигнатуре</b>, а прочитанное проверяется по уже разобранной геометрии.
/// Если сигнатура не найдена или проверка не сошлась — блок не читается, а причина попадает
/// в <see cref="LiraBinaryExtras.Notes"/>; частично прочитанные данные не публикуются.</para>
/// <list type="bullet">
///   <item><description><b>Объекты узлов</b> (узловые нагрузки): цепочка записей
///   <c>[u32 номер−1][u32 флаг][3×f64 XYZ][…][u16 число нагрузок]</c>, где XYZ поштучно совпадают
///   с координатами узлов из геометрического блока;</description></item>
///   <item><description><b>Объекты элементов</b> (элементные нагрузки): записи
///   <c>[u16 класс][u32 номер−1][u32 флаги][u16 тип КЭ][u16 число вершин][u32][CString][f32 множитель]
///   [u16 число нагрузок][записи нагрузок][хвост][n×u32 номера узлов]</c>; связность в конце
///   объекта сверяется с геометрией, а следующий объект ищется по последовательному номеру;</description></item>
///   <item><description><b>Жёсткости</b>: массив <c>n×u16</c> номеров жёсткостей, который в контрольном
///   файле хранится двумя подряд идущими одинаковыми копиями — это и служит сигнатурой;</description></item>
///   <item><description><b>Загружения</b>: серия строк <c>[u8 длина][текст cp1251]</c>; число строк равно
///   максимальному номеру загружения из прочитанных нагрузок.</description></item>
/// </list>
/// </remarks>
public static class LiraBinaryExtrasReader
{
    /// <summary>Размер записи узла в геометрическом блоке.</summary>
    const int NodeRecordSize = 28;

    /// <summary>Размер записи элемента в геометрическом блоке.</summary>
    const int ElementRecordSize = 38;

    /// <summary>Верхняя граница числа нагрузок одного узла (защита от мусорного счётчика).</summary>
    const int MaxNodalLoads = 512;

    /// <summary>Верхняя граница числа нагрузок одного элемента (защита от мусорного счётчика).</summary>
    const int MaxElementLoads = 256;

    /// <summary>Верхняя граница номера жёсткости при поиске массива назначений (номер хранится в u16).</summary>
    const int MaxStiffnessNumber = 255;

    /// <summary>Верхняя граница длины строки (метка элемента, имя загружения).</summary>
    const int MaxStringLength = 4096;

    /// <summary>Размер записи нагрузки по её тегу (всего байт, включая 8 байт заголовка).</summary>
    static readonly Dictionary<int, int> LoadTagSize = new()
    {
        [2]  = 20,
        [10] = 20,
        [12] = 16,
        [14] = 24,
        [28] = 24
    };

    static readonly Encoding Cp1251 = CreateCp1251();

    static Encoding CreateCp1251()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1251);
    }

    /// <summary>
    /// Читает дополнительные блоки файла.
    /// </summary>
    /// <param name="bytes">Содержимое .lir.</param>
    /// <param name="nodesStart">Смещение массива узлов.</param>
    /// <param name="nodeCount">Число записей узлов (включая удалённые).</param>
    /// <param name="elementsStart">Смещение массива элементов.</param>
    /// <param name="elementCount">Число записей элементов (включая удалённые).</param>
    /// <param name="vertexCount">Число узлов по записи элемента; 0 — запись удалена или определение неизвестно.</param>
    /// <param name="readStiffness">Читать назначение жёсткостей.</param>
    /// <param name="readLoads">Читать нагрузки и имена загружений.</param>
    public static LiraBinaryExtras Read(
        byte[] bytes,
        int    nodesStart,
        int    nodeCount,
        int    elementsStart,
        int    elementCount,
        int[]  vertexCount,
        bool   readStiffness = true,
        bool   readLoads     = true)
    {
        var notes = new List<string>();
        int elementsEnd = elementsStart + elementCount * ElementRecordSize;

        int[] stiffness = readStiffness
            ? LocateStiffness(bytes, elementsEnd, elementCount, notes)
            : [];

        var loads     = new List<LiraBinaryLoad>();
        var loadCases = new List<LiraLoadCaseRecord>();

        if (readLoads && elementCount > 0)
        {
            var nodal = new List<LiraBinaryLoad>();
            if (TryReadNodalLoads(bytes, nodesStart, nodeCount, elementsEnd, nodal, notes))
                loads.AddRange(nodal);

            var elementLoads = new List<LiraBinaryLoad>();
            if (TryReadElementLoads(bytes, elementsStart, elementCount, vertexCount, elementsEnd, elementLoads, notes))
            {
                loads.AddRange(elementLoads);
                if (loads.Count > 0)
                {
                    int count = loads.Max(l => l.LoadCaseId);
                    if (count is > 0 and <= 4096)
                        loadCases = FindLoadCases(bytes, elementsEnd, count, notes);
                }
            }
        }

        return new LiraBinaryExtras(stiffness, loadCases, loads, notes);
    }

    // ------------------------------------------------------------------ Жёсткости

    /// <summary>
    /// Ищет массив номеров жёсткостей: <c>n</c> значений <c>u16</c>, за которыми идёт побитовая копия
    /// того же массива. Возвращает номера по индексу записи элемента (0 — не найдено).
    /// </summary>
    /// <remarks>
    /// Копия массива — единственная найденная в контрольном файле сигнатура. Если значения массива
    /// периодичны, та же сигнатура видна и со сдвигом внутри периода, поэтому кандидаты, отстоящие
    /// друг от друга меньше чем на длину массива, считаются одним кластером (берётся самый ранний).
    /// Если кластеров несколько и их массивы различаются — номер жёсткости не назначается.
    /// </remarks>
    static int[] LocateStiffness(byte[] bytes, int searchFrom, int elementCount, List<string> notes)
    {
        if (elementCount <= 0) return [];

        var raw = new List<int>();
        for (int parity = 0; parity < 2 && raw.Count == 0; parity++)
        {
            int positions = (bytes.Length - parity) / 2;   // позиций u16, начиная со смещения parity
            int need      = 2 * elementCount;              // две копии подряд
            int last      = positions - need;
            int first     = Math.Max(0, (searchFrom - parity + 1) / 2);
            for (int s = first; s <= last; s++)
            {
                int a = parity + 2 * s;
                if (ReadU16(bytes, a) > MaxStiffnessNumber) continue;
                if (LooksLikeDuplicatedArray(bytes, a, elementCount)) raw.Add(a);
            }
        }

        if (raw.Count == 0)
        {
            notes.Add("массив номеров жёсткостей не найден (нет пары одинаковых массивов n×u16 после блока элементов)");
            return [];
        }

        raw.Sort();
        var clusters = new List<int>();
        foreach (int a in raw)
            if (clusters.Count == 0 || a - clusters[^1] >= 2 * elementCount) clusters.Add(a);

        if (clusters.Count > 1)
        {
            bool same = true;
            for (int i = 1; i < clusters.Count && same; i++)
                for (int k = 0; k < elementCount && same; k++)
                    if (ReadU16(bytes, clusters[0] + 2 * k) != ReadU16(bytes, clusters[i] + 2 * k)) same = false;

            if (!same)
            {
                notes.Add($"массив номеров жёсткостей неоднозначен: найдено кластеров {clusters.Count} с разными значениями — жёсткости не назначены");
                return [];
            }
        }

        int start = clusters[0];
        var result = new int[elementCount];
        for (int i = 0; i < elementCount; i++) result[i] = ReadU16(bytes, start + 2 * i) + 1;
        notes.Add($"жёсткости прочитаны: массив по смещению {start}, {elementCount} назначений");
        return result;
    }

    /// <summary>
    /// Проверяет сигнатуру массива: небольшие значения, не менее трёх различных, и побитовая копия
    /// того же массива сразу после него.
    /// </summary>
    static bool LooksLikeDuplicatedArray(byte[] bytes, int start, int count)
    {
        // 8 контрольных значений по длине массива: отсекает участки нулей и произвольные данные
        int step = Math.Max(1, count / 8);
        Span<int> sample = stackalloc int[8];
        int distinct = 0;
        for (int k = 0; k < 8; k++)
        {
            int index = Math.Min(count - 1, k * step);
            int value = ReadU16(bytes, start + 2 * index);
            if (value > MaxStiffnessNumber) return false;
            bool seen = false;
            for (int j = 0; j < k; j++) if (sample[j] == value) { seen = true; break; }
            sample[k] = value;
            if (!seen) distinct++;
        }
        if (distinct < 2) return false;

        if (!bytes.AsSpan(start, 2 * count).SequenceEqual(bytes.AsSpan(start + 2 * count, 2 * count)))
            return false;

        for (int i = 0; i < count; i++)
            if (ReadU16(bytes, start + 2 * i) > MaxStiffnessNumber) return false;

        return true;
    }

    // ------------------------------------------------------------------ Узловые нагрузки

    /// <summary>
    /// Читает объекты узлов и узловые нагрузки. Начало массива определяется по записи узла №1:
    /// номер 0, флаг 1 и координаты, побитово совпадающие с геометрическим блоком.
    /// </summary>
    static bool TryReadNodalLoads(byte[] bytes, int nodesStart, int nodeCount, int searchFrom,
                                  List<LiraBinaryLoad> loads, List<string> notes)
    {
        if (nodeCount <= 0)
        {
            notes.Add("узловые нагрузки не читались: в схеме нет узлов");
            return false;
        }

        int start = FindNodeObjectArray(bytes, nodesStart, nodeCount, searchFrom, notes);
        if (start < 0) return false;

        long o = start;
        for (int i = 0; i < nodeCount; i++)
        {
            uint flag = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan((int)o + 4));
            int count = ReadU16(bytes, (int)o + 38);
            for (int j = 0; j < count; j++)
            {
                int p     = (int)o + 40 + 10 * j;
                int code  = ReadU16(bytes, p);
                int loadCase = ReadU16(bytes, p + 2);
                double value = ReadF32(bytes, p + 4);
                int group    = ReadU16(bytes, p + 8);

                if (flag != 1) continue;
                int direction = code is >= 101 and <= 106 ? code - 100 : code;
                loads.Add(new LiraBinaryLoad(
                    LiraLoadTargetKind.Node, i + 1, loadCase,
                    LoadType: 0, DirectionCode: direction, ParameterCount: 2,
                    P0: value, P1: 0, P2: 0, P3: 0,
                    SerializedTag: 0, SerializedCode: code, SourceGroupRaw: group,
                    NativeParameters: [value], BinaryOffset: p));
            }
            o += 48 + 10L * count;
        }

        notes.Add($"узловые нагрузки прочитаны: массив объектов по смещению {start}, записей {loads.Count}");
        return true;
    }

    /// <summary>Ищет начало массива объектов узлов; принимает только единственную полностью сошедшуюся сигнатуру.</summary>
    static int FindNodeObjectArray(byte[] bytes, int nodesStart, int nodeCount, int searchFrom, List<string> notes)
    {
        var pattern = new byte[32];
        BinaryPrimitives.WriteUInt32LittleEndian(pattern, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(pattern.AsSpan(4), 1);
        for (int k = 0; k < 3; k++)
            BinaryPrimitives.WriteDoubleLittleEndian(pattern.AsSpan(8 + 8 * k), ReadF64(bytes, nodesStart + 8 * k));

        int found = -1, hits = 0, search = Math.Max(0, searchFrom);
        while (search < bytes.Length)
        {
            int hit = bytes.AsSpan(search).IndexOf(pattern);
            if (hit < 0) break;
            int pos = search + hit;
            if (ValidateNodeObjects(bytes, pos, nodesStart, nodeCount)) { found = pos; hits++; }
            search = pos + 1;
        }

        if (hits == 1) return found;

        notes.Add(hits == 0
            ? "узловые нагрузки не прочитаны: массив объектов узлов не найден"
            : $"узловые нагрузки не прочитаны: сигнатура массива объектов узлов неоднозначна ({hits} совпадений)");
        return -1;
    }

    /// <summary>Проверяет всю цепочку объектов узлов по геометрии.</summary>
    static bool ValidateNodeObjects(byte[] bytes, int start, int nodesStart, int nodeCount)
    {
        long o = start;
        for (int i = 0; i < nodeCount; i++)
        {
            if (o + 48 > bytes.Length) return false;
            if (BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan((int)o)) != (uint)i) return false;

            uint flag = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan((int)o + 4));
            if (flag > 1) return false;

            int count = ReadU16(bytes, (int)o + 38);
            if (count > MaxNodalLoads) return false;
            if (flag == 0 && count != 0) return false;

            if (flag == 1)
            {
                int geometry = nodesStart + NodeRecordSize * i;
                for (int k = 0; k < 3; k++)
                    if (ReadF64(bytes, (int)o + 8 + 8 * k) != ReadF64(bytes, geometry + 8 * k))
                        return false;
            }

            o += 48 + 10L * count;
        }
        return o <= bytes.Length;
    }

    // ------------------------------------------------------------------ Элементные нагрузки

    /// <summary>
    /// Читает объекты элементов и элементные нагрузки. Принимается только полный проход по всем
    /// элементам: заголовок каждого объекта сверяется по номеру и числу вершин, а хвостовая связность —
    /// по геометрическому блоку.
    /// </summary>
    static bool TryReadElementLoads(byte[] bytes, int elementsStart, int elementCount, int[] vertexCount,
                                    int searchFrom, List<LiraBinaryLoad> loads, List<string> notes)
    {
        for (int i = 0; i < elementCount; i++)
        {
            if (vertexCount[i] is < 1 or > 4)
            {
                notes.Add($"элементные нагрузки не прочитаны: у записи КЭ №{i + 1} удалена или неизвестна связность");
                return false;
            }
        }

        var starts = FindFirstElementObjectCandidates(bytes, vertexCount[0], searchFrom);
        if (starts.Count == 0)
        {
            notes.Add("элементные нагрузки не прочитаны: объекты элементов не найдены");
            return false;
        }

        List<LiraBinaryLoad>? accepted = null;
        int end = 0;
        foreach (int start in starts)
        {
            var trial = new List<LiraBinaryLoad>(loads.Count + 1024);
            if (TryWalkElementObjects(bytes, start, elementsStart, elementCount, vertexCount, trial, out end))
            {
                accepted = trial;
                break;
            }
        }

        if (accepted is null)
        {
            notes.Add($"элементные нагрузки не прочитаны: ни одна из {starts.Count} сигнатур не прошла сверку связности");
            return false;
        }

        loads.AddRange(accepted);
        notes.Add($"элементные нагрузки прочитаны: объектов {elementCount}, записей {accepted.Count}");
        return true;
    }

    /// <summary>Ищет возможные начала массива объектов элементов по заголовку записи КЭ №1.</summary>
    static List<int> FindFirstElementObjectCandidates(byte[] bytes, int vertexCount, int searchFrom)
    {
        var result = new List<int>();
        int limit = bytes.Length - 18;
        for (int p = Math.Max(0, searchFrom); p <= limit; p++)
        {
            if (bytes[p + 2] != 0 || bytes[p + 3] != 0 || bytes[p + 4] != 0 || bytes[p + 5] != 0) continue;   // номер 0
            if ((bytes[p + 6] & 1) == 0) continue;                                                            // флаг объекта
            if (ReadU16(bytes, p + 12) != vertexCount) continue;                                               // число вершин
            result.Add(p);
        }
        return result;
    }

    /// <summary>Проходит все объекты элементов; при любом расхождении возвращает false.</summary>
    static bool TryWalkElementObjects(byte[] bytes, int start, int elementsStart, int elementCount,
                                      int[] vertexCount, List<LiraBinaryLoad> loads, out int end)
    {
        end  = start;
        int o = start;

        for (int i = 0; i < elementCount; i++)
        {
            int nv = vertexCount[i];
            if (o + 18 > bytes.Length) return false;
            if (BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(o + 2)) != (uint)i) return false;
            if ((BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(o + 6)) & 1) == 0) return false;
            if (ReadU16(bytes, o + 12) != nv) return false;

            if (!TryReadPrefixedString(bytes, o + 18, out _, out int p)) return false;
            if (p + 6 > bytes.Length) return false;
            if (!double.IsFinite(ReadF32(bytes, p))) return false;

            int count = ReadU16(bytes, p + 4);
            if (count > MaxElementLoads) return false;
            p += 6;

            for (int j = 0; j < count; j++)
            {
                if (p + 8 > bytes.Length) return false;
                int tag      = ReadU16(bytes, p);
                int code     = ReadU16(bytes, p + 2);
                int loadCase = ReadU16(bytes, p + 4);
                int group    = ReadU16(bytes, p + 6);
                if (!LoadTagSize.TryGetValue(tag, out int size)) return false;
                if (p + size > bytes.Length) return false;

                int nativeCount = (size - 8) / 4;
                var native = new double[nativeCount];
                for (int k = 0; k < nativeCount; k++) native[k] = ReadF32(bytes, p + 8 + 4 * k);
                if (!TryMapLoad(tag, code, native, out int loadType, out int direction, out var parameters)) return false;

                loads.Add(new LiraBinaryLoad(
                    LiraLoadTargetKind.Element, i + 1, loadCase,
                    loadType, direction, parameters.Length,
                    parameters[0], parameters.Length > 1 ? parameters[1] : 0,
                    parameters.Length > 2 ? parameters[2] : 0,
                    parameters.Length > 3 ? parameters[3] : 0,
                    tag, code, group, native, p));
                p += size;
            }

            // Конец объекта: nv номеров узлов (0-based, как в геометрическом блоке). Следующий объект
            // начинается сразу после них, поэтому положение проверяется по связности, а не по длине хвоста.
            int slotsAt = elementsStart + ElementRecordSize * i;
            var slots   = bytes.AsSpan(slotsAt, 4 * nv);
            if (i + 1 < elementCount)
            {
                if (!TryFindNextElementObject(bytes, p, i + 1, slots, out int nextStart)) return false;
                o = nextStart;
            }
            else
            {
                int tail = FindSequence(bytes, p, slots);
                if (tail < 0) return false;
                o = tail + slots.Length;
            }
        }

        end = o;
        return true;
    }

    /// <summary>
    /// Ищет заголовок следующего объекта элемента по последовательному номеру; принимается только
    /// позиция, перед которой лежит связность текущего элемента.
    /// </summary>
    static bool TryFindNextElementObject(byte[] bytes, int from, int number, ReadOnlySpan<byte> slots, out int start)
    {
        start = -1;
        Span<byte> key = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(key, (uint)number);

        int search = from;
        int limit  = Math.Min(bytes.Length, from + 65536);
        while (search < limit)
        {
            int hit = bytes.AsSpan(search, limit - search).IndexOf(key);
            if (hit < 0) break;
            int candidate = search + hit - 2;
            if (candidate >= from + slots.Length && candidate + 18 <= bytes.Length)
            {
                if (bytes.AsSpan(candidate - slots.Length, slots.Length).SequenceEqual(slots) &&
                    (BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(candidate + 6)) & 1) != 0)
                {
                    start = candidate;
                    return true;
                }
            }
            search += hit + 1;
        }
        return false;
    }

    /// <summary>Приводит payload записи нагрузки к параметрам текстового экспорта.</summary>
    static bool TryMapLoad(int tag, int code, double[] native, out int loadType, out int direction, out double[] parameters)
    {
        loadType  = code / 10;
        direction = code % 10;
        parameters = [];

        switch (tag)
        {
            case 10 when loadType == 15 && native.Length == 3:
                parameters = [native[0], native[1], native[2], 0];
                return true;
            case 12 when loadType is 6 or 16 && native.Length == 2:
                parameters = [native[0], 0, 0, 0];
                return true;
            case 2 when loadType == 16 && native.Length == 3:
                parameters = [native[0], 0, 0, 0];
                return true;
            case 14 when loadType == 7 && native.Length == 4:
                parameters = [native[0], native[1], native[2], native[3]];
                return true;
            case 28 when loadType == 19 && native.Length == 4:
                parameters = [native[2], native[3], native[0], native[1]];
                return true;
            default:
                return false;
        }
    }

    // ------------------------------------------------------------------ Загружения

    /// <summary>
    /// Ищет серию из <paramref name="count"/> строк «длина + текст» — имена загружений.
    /// Принимается только единственная серия во всём файле.
    /// </summary>
    static List<LiraLoadCaseRecord> FindLoadCases(byte[] bytes, int searchFrom, int count, List<string> notes)
    {
        var candidates = new List<(int Start, List<string> Names)>();

        for (int p = Math.Max(0, searchFrom); p < bytes.Length; p++)
        {
            if (!IsPlausibleStringAt(bytes, p, out _)) continue;

            int o = p;
            var names = new List<string>(count);
            bool ok = true;
            for (int k = 0; k < count; k++)
            {
                if (!IsPlausibleStringAt(bytes, o, out int next)) { ok = false; break; }
                if (!TryReadPrefixedString(bytes, o, out string name, out next) || name.Length == 0) { ok = false; break; }
                names.Add(name);
                o = next;
            }
            if (!ok) continue;

            candidates.Add((p, names));
            p = o - 1;
        }

        if (candidates.Count == 0)
        {
            notes.Add($"имена загружений не найдены (ожидалось строк: {count})");
            return [];
        }
        if (candidates.Count > 1)
        {
            notes.Add($"имена загружений неоднозначны: найдено серий {candidates.Count} по {count} строк");
            return [];
        }

        var result = new List<LiraLoadCaseRecord>(count);
        for (int i = 0; i < count; i++) result.Add(new LiraLoadCaseRecord(i + 1, candidates[0].Names[i]));
        return result;
    }

    /// <summary>Быстрая проверка «похоже на строку с префиксом длины» без выделения памяти.</summary>
    static bool IsPlausibleStringAt(byte[] bytes, int offset, out int next)
    {
        next = offset;
        if (offset < 0 || offset >= bytes.Length) return false;

        int length = bytes[offset];
        int p      = offset + 1;
        if (length == 255)
        {
            if (p + 2 > bytes.Length) return false;
            length = ReadU16(bytes, p);
            p += 2;
            if (length >= 0xFFFE) return false;
        }
        if (length == 0 || length > 200 || p + length > bytes.Length) return false;

        for (int i = 0; i < length; i++)
        {
            byte c = bytes[p + i];
            if (c < 0x20 || c == 0x7F) return false;
        }

        next = p + length;
        return true;
    }

    /// <summary>Читает строку «длина + текст cp1251». Пустая строка допустима.</summary>
    static bool TryReadPrefixedString(byte[] bytes, int offset, out string value, out int next)
    {
        value = string.Empty;
        next  = offset;
        if (offset < 0 || offset >= bytes.Length) return false;

        int length = bytes[offset];
        int p      = offset + 1;
        if (length == 255)
        {
            if (p + 2 > bytes.Length) return false;
            length = ReadU16(bytes, p);
            p += 2;
            if (length >= 0xFFFE) return false;   // длинные/Unicode строки этим чтением не поддержаны
        }
        if (length < 0 || length > MaxStringLength || p + length > bytes.Length) return false;

        value = Cp1251.GetString(bytes, p, length);
        next  = p + length;
        return true;
    }

    // ------------------------------------------------------------------ Примитивы

    static int ReadU16(byte[] b, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(offset, 2));

    static double ReadF64(byte[] b, int offset) => BinaryPrimitives.ReadDoubleLittleEndian(b.AsSpan(offset, 8));

    static double ReadF32(byte[] b, int offset) => BinaryPrimitives.ReadSingleLittleEndian(b.AsSpan(offset, 4));

    static int FindSequence(byte[] b, int from, ReadOnlySpan<byte> pattern)
    {
        if (pattern.Length == 0 || from >= b.Length) return -1;
        int hit = b.AsSpan(from).IndexOf(pattern);
        return hit < 0 ? -1 : from + hit;
    }
}
