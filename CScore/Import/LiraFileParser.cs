using System.Collections;

namespace CScore.Import;

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
    public static LiraSchemaData Parse(string filePath)
    {
        var bytes = File.ReadAllBytes(filePath);
        var data  = new LiraSchemaData();

        var nodeNumbers = ReadNodes(bytes, data);
        ReadElements(bytes, nodeNumbers, data);

        return data;
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