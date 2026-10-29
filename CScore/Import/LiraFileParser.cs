using System.Collections;

namespace CScore.Import;

/// <summary>
/// Что читать из бинарного .lir дополнительно к геометрии.
/// </summary>
public enum LiraBinaryExtrasMode
{
    /// <summary>Только геометрия (узлы и элементы).</summary>
    None,

    /// <summary>Геометрия и назначение номеров жёсткостей КЭ.</summary>
    Stiffness,

    /// <summary>Геометрия, жёсткости, имена загружений и записи физических нагрузок.</summary>
    StiffnessAndLoads
}

/// <summary>
/// Парсер бинарного формата расчётной схемы ЛираСАПР (.lir), версия 2013 (13.x).
/// </summary>
/// <remarks>
/// <para>Формат изучен на эталонах <c>hostel.lir</c> (185 440 узлов / 200 769 КЭ) и
/// <c>6-k1.lir</c> (118 634 узла / 129 971 КЭ), сверенным с живой ЛИРОЙ через COM.</para>
/// <para>Структура файла:</para>
/// <list type="bullet">
///   <item><description>заголовок — три null-terminated строки («LIRA-SAPR …», кодовая страница, «Preview=…»
///   и, для изменённых схем, «MetaData1=…»);</description></item>
///   <item><description>узлы — записи по 48 байт, разбросанные блоками: <c>[i32 a][i32 b][i32 №−1][i32 flag]
///   [double x][double y][double z][i32][i32]</c>. Внутри блока записи идут вплотную (шаг 48)
///   с последовательными номерами, номер записан в самой записи — перенумерации при загрузке нет;</description></item>
///   <item><description>элементы — записи по 38 байт вплотную: маркер 1, затем 2–4 номера узлов
///   (0-based, 0 — признак отсутствия), остальное — нули. Два узла — стержень, три — треугольник,
///   четыре — четырёхугольник.</description></item>
/// </list>
/// <para>Координаты читаются как <c>double</c> (в некоторых старых реализациях использовался
/// ошибочный <c>float</c>).</para>
/// </remarks>
public static class LiraFileParser
{
    /// <summary>Размер записи узла.</summary>
    const int NodeRecordSize = 48;

    /// <summary>Размер записи элемента.</summary>
    const int ElementRecordSize = 38;

    /// <summary>Минимальная длина цепочки узлов, за которой она считается реальным блоком.</summary>
    const int MinNodeChain = 8;

    /// <summary>Минимальная длина блока элементов (записей подряд с шагом 38).</summary>
    const int MinElementBlock = 4;

    /// <summary>Верхняя граница номера узла в файле (отсекает мусорные записи).</summary>
    const int MaxNodeNumber = 5_000_000;

    /// <summary>Предел модуля координаты (отсекает мусорные записи).</summary>
    const double CoordLimit = 1e6;

    /// <summary>
    /// Читает .lir файл и возвращает контейнер сырых данных схемы.
    /// </summary>
    /// <param name="filePath">Путь к файлу <c>.lir</c>.</param>
    /// <param name="extras">Что читать дополнительно к геометрии (по умолчанию — только номера жёсткостей;
    /// записи нагрузок — явным <see cref="LiraBinaryExtrasMode.StiffnessAndLoads"/>).</param>
    public static LiraSchemaData Parse(string filePath, LiraBinaryExtrasMode extras = LiraBinaryExtrasMode.Stiffness)
    {
        var bytes = File.ReadAllBytes(filePath);
        var data  = new LiraSchemaData();

        // Основной путь — структурное чтение по заголовку; сканирование — запасной для неизвестных заголовков.
        if (TryReadStructured(bytes, data, extras)) return data;

        data = new LiraSchemaData();
        var nodeNumbers = ReadNodes(bytes, data);
        ReadElements(bytes, nodeNumbers, data);

        return data;
    }

    // ------------------------------------------------------------------ Структурное чтение

    /// <summary>Размер записи узла в структурном чтении: XYZ (3 × double) и флаг (u32).</summary>
    const int StructNodeSize = 28;

    /// <summary>Размер служебного блока между заголовком и узлами.</summary>
    const int StructMetaSize = 52;

    /// <summary>Лимит числа узлов и элементов (защита от мусорных счётчиков).</summary>
    const long StructCountLimit = 5_000_000;

    /// <summary>
    /// Число узлов КЭ по индексу определения (u16 на +32 записи): 0 — стержень (2 узла), 1 — треугольник (3),
    /// 2 — четырёхугольник (4), 3 — одноузловая связь. Остальные индексы (4 и выше) — объёмные КЭ
    /// (тетраэдр, призма, гексаэдр) и неизвестные определения: их число узлов здесь не задаётся.
    /// </summary>
    /// <remarks>
    /// Сверено с API ЛИРЫ 2024 по 11 схемам (около 200 тыс. КЭ) и на 53 файлах (689 тыс. живых КЭ) без
    /// единого нарушения. Определение задаёт число узлов, но не тип КЭ: под определением 0 бывают типы 10 и 262,
    /// под определением 2 — 41, 44 и 244. Слоты за пределами числа узлов — не «пустые»: в них остаётся мусор,
    /// поэтому длину по нулям определять нельзя.
    /// </remarks>
    static readonly int[] DefinitionNodeCount = [2, 3, 4, 1];

    /// <summary>
    /// Читает узлы и элементы по структуре файла: первый байт — длина заголовка L, затем L байт текста
    /// («LIRA-SAPR …», кодовая страница, Preview, MetaData); смещение B = 1 + L. Далее 52 байта служебного
    /// блока (число узлов <c>u32(B+12)</c>, число элементов <c>u32(B+20)</c>), записи узлов по 28 байт и
    /// записи элементов по 38 байт вплотную. Проверено на 52 схемах ЛИРА 2013–2025.
    /// </summary>
    /// <remarks>
    /// Флаг записи (u32) равен 1 у живой записи и 0 у удалённой: удалённая остаётся на месте, нумерация
    /// не сдвигается, живые элементы на удалённые узлы не ссылаются. Номера узлов и КЭ — индекс + 1.
    /// Число узлов КЭ берётся по индексу определения (<see cref="DefinitionNodeCount"/>), а не по нулям в
    /// слотах: слоты за пределами числа узлов содержат мусор. Тип КЭ ЛИРЫ по файлу не определяется
    /// (<c>FeType</c> здесь условный: 10, 42 или 44 по числу узлов). Объёмные КЭ (определения 4 и выше)
    /// пропускаются.
    /// <para>Дополнительно (<paramref name="extras"/>) читаются жёсткости КЭ, имена загружений и записи
    /// физических нагрузок — см. <see cref="LiraBinaryExtrasReader"/>. Сбой этих блоков не отменяет
    /// импорт геометрии: причина попадает в <see cref="LiraSchemaData.BinaryNotes"/>.</para>
    /// Возвращает false, если заголовок не распознан или структура не сходится с файлом.
    /// </remarks>
    static bool TryReadStructured(byte[] bytes, LiraSchemaData data, LiraBinaryExtrasMode extras)
    {
        if (bytes.Length < 1) return false;
        int headerLen = bytes[0];
        int b = 1 + headerLen;
        if (headerLen < 36 || b + StructMetaSize > bytes.Length) return false;

        ReadOnlySpan<byte> signature = "LIRA-SAPR"u8;
        if (!bytes.AsSpan(1, headerLen).StartsWith(signature)) return false;

        long nodeCount = BitConverter.ToUInt32(bytes, b + 12);
        long elemCount = BitConverter.ToUInt32(bytes, b + 20);
        if (nodeCount is 0 or > StructCountLimit || elemCount > StructCountLimit) return false;

        long nodesStart = b + StructMetaSize;
        long elemsStart = nodesStart + nodeCount * StructNodeSize;
        long elemsEnd   = elemsStart + elemCount * ElementRecordSize;
        if (elemsEnd > bytes.Length) return false;

        var live = new bool[nodeCount];
        var nodes = new List<LiraNodeRecord>((int)nodeCount);
        for (int i = 0; i < nodeCount; i++)
        {
            int off  = (int)(nodesStart + (long)i * StructNodeSize);
            uint flag = BitConverter.ToUInt32(bytes, off + 24);
            if (flag > 1) return false;
            if (flag == 0) continue;

            double x = BitConverter.ToDouble(bytes, off);
            double y = BitConverter.ToDouble(bytes, off + 8);
            double z = BitConverter.ToDouble(bytes, off + 16);
            if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(z)) return false;

            live[i] = true;
            nodes.Add(new LiraNodeRecord(Id: i + 1, X: x, Y: y, Z: z, DofMask: 0));
        }

        var vertexCount = new int[elemCount];
        var elements = new List<LiraElementRecord>((int)elemCount);
        for (int i = 0; i < elemCount; i++)
        {
            int off  = (int)(elemsStart + (long)i * ElementRecordSize);
            uint flag = BitConverter.ToUInt32(bytes, off + 34);
            if (flag > 1) return false;
            if (flag == 0) continue;

            // число узлов — по индексу определения; объёмные и неизвестные определения импорт пропускает
            ushort definition = BitConverter.ToUInt16(bytes, off + 32);
            int count = definition < DefinitionNodeCount.Length ? DefinitionNodeCount[definition] : 0;
            vertexCount[i] = count;
            if (count < 2) continue;   // одноузловые связи, объёмные КЭ и неизвестные определения

            var ids = new int[count];
            for (int k = 0; k < count; k++)
            {
                uint raw = BitConverter.ToUInt32(bytes, off + 4 * k);
                if (raw >= nodeCount || !live[raw]) return false;
                ids[k] = (int)raw + 1;
            }

            elements.Add(new LiraElementRecord(
                Id:           i + 1,
                FeType:       count switch { 2 => 10, 3 => 42, _ => 44 },
                SectionCount: 1,
                StiffnessId:  0,
                NodeIds:      ids));
        }

        if (extras != LiraBinaryExtrasMode.None && elemCount > 0)
        {
            var extra = LiraBinaryExtrasReader.Read(
                bytes,
                nodesStart:    (int)nodesStart,
                nodeCount:     (int)nodeCount,
                elementsStart: (int)elemsStart,
                elementCount:  (int)elemCount,
                vertexCount:   vertexCount,
                readStiffness: true,
                readLoads:     extras == LiraBinaryExtrasMode.StiffnessAndLoads);

            if (extra.StiffnessByElement.Length == elemCount)
                for (int k = 0; k < elements.Count; k++)
                {
                    int record = elements[k].Id - 1;
                    int stiffness = extra.StiffnessByElement[record];
                    if (stiffness > 0) elements[k] = elements[k] with { StiffnessId = stiffness };
                }

            data.LoadCases.AddRange(extra.LoadCases);
            data.BinaryLoads.AddRange(extra.Loads);
            data.BinaryNotes.AddRange(extra.Notes);
        }

        data.Nodes.AddRange(nodes);
        data.Elements.AddRange(elements);
        return true;
    }

    /// <summary>Пропускает 3 null-terminated строки заголовка. Возвращает смещение после заголовка.</summary>
    internal static int SkipHeader(byte[] bytes)
    {
        int offset = 0;
        for (int skip = 0; skip < 3; skip++)
        {
            while (offset < bytes.Length && bytes[offset] != 0) offset++;
            offset++; // пропустить null-терминатор
        }
        return offset;
    }

    /// <summary>Смещение первого блока данных (после заголовка и служебной области).</summary>
    internal static void SkipMetadata(byte[] bytes, ref int offset)
    {
        offset += 52;
    }

    // ------------------------------------------------------------------ Узлы

    /// <summary>
    /// Читает узлы схемы. Возвращает множество 0-based номеров узлов, пригодных для ссылок из элементов.
    /// </summary>
    /// <remarks>
    /// Записи узлов разбросаны по файлу блоками. Одиночные «похожие» записи (мусор при
    /// сканировании произвольных байтов) отбрасываются: принимаются только цепочки длиной
    /// не менее <see cref="MinNodeChain"/> записей с шагом ровно 48 байт.
    /// </remarks>
    static HashSet<int> ReadNodes(byte[] bytes, LiraSchemaData data)
    {
        var picked = new Dictionary<int, (double X, double Y, double Z)>();
        int last = bytes.Length - NodeRecordSize;
        if (last < 0) return [];

        // 1. Битовая маска записей-кандидатов.
        var good = new BitArray(last + 1);
        for (int off = 0; off <= last; off++)
        {
            int num  = BitConverter.ToInt32(bytes, off + 8);
            int flag = BitConverter.ToInt32(bytes, off + 12);
            if (num < 0 || num > MaxNodeNumber || flag < 0) continue;

            double x = BitConverter.ToDouble(bytes, off + 16);
            double y = BitConverter.ToDouble(bytes, off + 24);
            double z = BitConverter.ToDouble(bytes, off + 32);
            if (!IsPlausible(x) || !IsPlausible(y) || !IsPlausible(z)) continue;

            good[off] = true;
        }

        // 2. Кандидаты — записи, у которых есть соседняя запись через 48 байт:
        //    это отсекает случайные совпадения полей.
        var offsets = new List<int>(1024);
        for (int off = 0; off + NodeRecordSize <= last; off++)
            if (good[off] && good[off + NodeRecordSize])
                offsets.Add(off);

        // 3. Индекс для переходов по цепочкам (начало — запись без предшественника).
        var index = new Dictionary<int, int>(offsets.Count);
        for (int i = 0; i < offsets.Count; i++) index[offsets[i]] = i;

        var chain = new List<int>(256);
        foreach (int start in offsets)
        {
            if (index.ContainsKey(start - NodeRecordSize)) continue;   // не начало цепочки

            chain.Clear();
            int cur = start;
            int prevNum = BitConverter.ToInt32(bytes, start + 8);
            while (index.TryGetValue(cur, out _))
            {
                int num = BitConverter.ToInt32(bytes, cur + 8);
                // внутри блока номера идут подряд — это отсекает служебные структуры
                if (chain.Count > 0 && num != prevNum + 1) break;
                chain.Add(cur);
                prevNum = num;
                cur += NodeRecordSize;
            }

            if (chain.Count < MinNodeChain) continue;

            // Цепочка из «нулевых» записей — это не узлы (служебные структуры с похожими полями).
            int meaningful = 0;
            foreach (int off in chain)
            {
                double x = BitConverter.ToDouble(bytes, off + 16);
                double y = BitConverter.ToDouble(bytes, off + 24);
                double z = BitConverter.ToDouble(bytes, off + 32);
                if (x != 0.0 || y != 0.0 || z != 0.0) meaningful++;
            }
            if (meaningful * 2 < chain.Count) continue;

            foreach (int off in chain)
            {
                int num = BitConverter.ToInt32(bytes, off + 8);
                double x = BitConverter.ToDouble(bytes, off + 16);
                double y = BitConverter.ToDouble(bytes, off + 24);
                double z = BitConverter.ToDouble(bytes, off + 32);

                // Номера могут встречаться и в служебных структурах; при коллизии
                // предпочитаем запись с ненулевыми координатами.
                if (picked.TryGetValue(num, out var old))
                {
                    bool oldEmpty = old.X == 0.0 && old.Y == 0.0 && old.Z == 0.0;
                    bool newEmpty = x == 0.0 && y == 0.0 && z == 0.0;
                    if (!oldEmpty || newEmpty) continue;
                }

                picked[num] = (x, y, z);
            }
        }

        // Реальные узлы образуют плотный диапазон номеров, начиная с 0; всё, что найдено
        // за его пределами, — совпадения полей в других структурах файла. Граница ищется
        // по кластерам тысячных долей диапазона (устойчиво к «дырам» в нумерации).
        int limit = ClusterLimit(picked.Keys);

        var result = new HashSet<int>(limit);
        foreach (var (num, xyz) in picked)
        {
            if (num >= limit) continue;

            result.Add(num);
            data.Nodes.Add(new LiraNodeRecord(
                Id:      num + 1,          // в файле номера 0-based, в ЛИРЕ 1-based
                X:       Sanitize(xyz.X),
                Y:       Sanitize(xyz.Y),
                Z:       Sanitize(xyz.Z),
                DofMask: 0
            ));
        }

        data.Nodes.Sort((a, b) => a.Id.CompareTo(b.Id));
        return result;
    }

    /// <summary>
    /// Возвращает верхнюю границу (исключительно) реального диапазона номеров узлов:
    /// это конец самого длинного кластера «тысячных долей», начинающегося с нуля.
    /// Устойчиво к единичным пропускам в нумерации.
    /// </summary>
    static int ClusterLimit(Dictionary<int, (double X, double Y, double Z)>.KeyCollection numbers)
    {
        const int bucketSize = 1000;
        int max = 0;
        foreach (var n in numbers) if (n > max) max = n;
        if (max == 0) return 0;

        var used = new bool[max / bucketSize + 2];
        foreach (var n in numbers) used[n / bucketSize] = true;

        int b = 0;
        while (b + 1 < used.Length && used[b + 1]) b++;   // кластер, начинающийся с нуля
        return (b + 1) * bucketSize;
    }

    static bool IsPlausible(double v) =>
        !double.IsNaN(v) && !double.IsInfinity(v) && Math.Abs(v) < CoordLimit;

    static double Sanitize(double v) => IsPlausible(v) ? v : 0.0;

    // ------------------------------------------------------------------ Элементы

    /// <summary>
    /// Читает элементы (стержни и оболочки). Записи идут блоками вплотную с шагом 38 байт;
    /// принимаются только блоки длиной не менее <see cref="MinElementBlock"/> записей, у которых
    /// все ссылки указывают на найденные узлы.
    /// </summary>
    static void ReadElements(byte[] bytes, HashSet<int> nodeNumbers, LiraSchemaData data)
    {
        if (nodeNumbers.Count == 0) return;

        int last = bytes.Length - ElementRecordSize;
        var block = new List<int[]>(256);
        Span<int> refs = stackalloc int[4];

        for (int off = 0; off <= last; off++)
        {
            if (BitConverter.ToInt32(bytes, off + 4) != 1) continue;

            // Блок элементов: записи идут вплотную с шагом 38 байт.
            block.Clear();
            int cur = off;
            while (cur <= last && BitConverter.ToInt32(bytes, cur + 4) == 1)
            {
                if (!TryReadElement(bytes, cur, nodeNumbers, ref refs, block)) break;
                cur += ElementRecordSize;
            }

            if (block.Count >= MinElementBlock)
                foreach (var ids in block)
                {
                    data.Elements.Add(new LiraElementRecord(
                        Id:           data.Elements.Count + 1,
                        FeType:       ids.Length switch { 2 => 10, 3 => 42, _ => 44 },
                        SectionCount: 1,
                        StiffnessId:  0,
                        NodeIds:      ids
                    ));
                }

            off = Math.Max(cur - 1, off);   // начало поиска — за следующей записью блока
        }
    }

    /// <summary>
    /// Читает запись элемента: маркер 1, затем 2–4 номера узлов (0-based, 0 — признак отсутствия).
    /// Все номера должны указывать на найденные узлы.
    /// </summary>
    static bool TryReadElement(byte[] bytes, int off, HashSet<int> nodeNumbers, ref Span<int> refs, List<int[]> block)
    {
        int n1 = BitConverter.ToInt32(bytes, off + 8);
        int n2 = BitConverter.ToInt32(bytes, off + 12);
        int n3 = BitConverter.ToInt32(bytes, off + 16);
        int n4 = BitConverter.ToInt32(bytes, off + 20);

        int count = 0;
        if (n1 != 0) count = 1;
        if (n2 != 0) count = 2;
        if (n3 != 0) count = 3;
        if (n4 != 0) count = 4;
        if (count < 2 || count > 4) return false;

        refs[0] = n1; refs[1] = n2; refs[2] = n3; refs[3] = n4;
        for (int i = 0; i < count; i++)
            if (!nodeNumbers.Contains(refs[i])) return false;

        // хвост записи (после номеров узлов) — преимущественно нули
        int tailFrom = 8 + 4 * count;
        int nonZero = 0;
        for (int i = tailFrom; i < ElementRecordSize; i++)
            if (bytes[off + i] != 0) nonZero++;
        if (nonZero > 2) return false;

        var ids = new int[count];
        for (int i = 0; i < count; i++) ids[i] = refs[i] + 1;   // 0-based → 1-based
        block.Add(ids);
        return true;
    }
}