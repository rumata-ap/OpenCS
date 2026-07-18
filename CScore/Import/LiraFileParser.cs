using System.Text;

namespace CScore.Import;

/// <summary>
/// Парсер бинарного формата расчётной схемы ЛираСАПР (.lir).
/// Читает узлы (Block 1 + Block 2) и элементы (Block 4).
/// </summary>
public static class LiraFileParser
{
    static readonly Encoding Cp1251;

    static LiraFileParser()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Cp1251 = Encoding.GetEncoding(1251);
    }

    /// <summary>
    /// Читает .lir файл и возвращает контейнер сырых данных схемы.
    /// Порядок блоков: Header → Metadata → Block 1 (2D) → Block 2 (3D) → Elements
    /// </summary>
    public static LiraSchemaData Parse(string filePath)
    {
        var bytes = File.ReadAllBytes(filePath);
        var data  = new LiraSchemaData();
        var offset = SkipHeader(bytes);
        SkipMetadata(bytes, ref offset);
        ParseAllNodes(bytes, ref offset, data);
        ParseElements(bytes, ref offset, data);
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

    /// <summary>Пропускает блок метаданных (52 байта после заголовка).</summary>
    internal static void SkipMetadata(byte[] bytes, ref int offset)
    {
        offset += 52;
    }

    // ------------------------------------------------------------------ Block 1 (2D nodes)

    /// <summary>
    /// Определяет количество записей Block 1 (2D координаты).
    /// Block 1: 28 байт/запись, маркер 0xE4 0xBC на смещении +23/+24.
    /// </summary>
    internal static int FindBlock1Count(byte[] bytes, int startOffset)
    {
        const int recordSize = 28;
        int offset = startOffset;
        int count = 0;

        while (offset + recordSize <= bytes.Length)
        {
            if (bytes[offset + 22] == 0xE4 && bytes[offset + 23] == 0xBC)
            {
                count++;
                offset += recordSize;
            }
            else
                break;
        }

        return count;
    }

    // ------------------------------------------------------------------ Block 2 (3D nodes)

    /// <summary>
    /// Определяет начало Block 2 (3D координаты) и количество записей.
    /// Block 2: 28 байт/запись, [double X][double Y][double Z][int32=1].
    /// </summary>
    internal static (int offset, int count) FindBlock2(byte[] bytes, int startOffset)
    {
        const int recordSize = 28;
        int offset = startOffset;

        // Пропускаем байт-разделители (0x00) между Block 1 и Block 2
        while (offset < bytes.Length && bytes[offset] == 0x00)
            offset++;

        // Считаем записи Block 2 (int32=1 на смещении +24, координаты в разумных пределах)
        int count = 0;
        int scan = offset;
        while (scan + recordSize <= bytes.Length)
        {
            var intVal = BitConverter.ToInt32(bytes, scan + 24);
            if (intVal != 1) break;

            var z = BitConverter.ToDouble(bytes, scan + 16);
            if (z < -100 || z > 100) break;

            count++;
            scan += recordSize;
        }

        if (count == 0)
            throw new InvalidOperationException("Block 2 (3D координаты) не найден в файле");

        return (offset, count);
    }

    // ------------------------------------------------------------------ Combined nodes

    /// <summary>
    /// Читает все узлы: Block 1 (2D) + Block 2 (3D).
    /// LIRA нумерация: N1..N11748 = Block 1 (swap осей), N11749..N185440 = Block 2.
    /// </summary>
    static void ParseAllNodes(byte[] bytes, ref int offset, LiraSchemaData data)
    {
        // Block 1: 2D координаты
        int block1Count = FindBlock1Count(bytes, offset);
        int block1Start = offset;

        for (int i = 0; i < block1Count; i++)
        {
            int baseOff = block1Start + i * 28;
            double y = BitConverter.ToDouble(bytes, baseOff);      // Block1.Y → LIRA.X
            double z = BitConverter.ToDouble(bytes, baseOff + 8);  // Block1.Z → LIRA.Y

            if (double.IsNaN(y) || double.IsInfinity(y)) y = 0;
            if (double.IsNaN(z) || double.IsInfinity(z)) z = 0;

            data.Nodes.Add(new LiraNodeRecord(
                Id:      i + 1,
                X:       y,
                Y:       z,
                Z:       0,
                DofMask: 0
            ));
        }

        offset = block1Start + block1Count * 28;

        // Block 2: 3D координаты
        var (block2Offset, block2Count) = FindBlock2(bytes, offset);
        offset = block2Offset;

        for (int i = 0; i < block2Count; i++)
        {
            int baseOff = offset + i * 28;
            double x = BitConverter.ToDouble(bytes, baseOff);
            double y = BitConverter.ToDouble(bytes, baseOff + 8);
            double z = BitConverter.ToDouble(bytes, baseOff + 16);

            if (double.IsNaN(x) || double.IsInfinity(x)) x = 0;
            if (double.IsNaN(y) || double.IsInfinity(y)) y = 0;
            if (double.IsNaN(z) || double.IsInfinity(z)) z = 0;

            data.Nodes.Add(new LiraNodeRecord(
                Id:      block1Count + i + 1,
                X:       x,
                Y:       y,
                Z:       z,
                DofMask: 0
            ));
        }

        offset += block2Count * 28;
    }

    // ------------------------------------------------------------------ Elements

    /// <summary>
    /// Определяет начало блока элементов (Block 4).
    /// Ищет 12 нулевых байт + uint32 маркер (0x10000..0x10003).
    /// </summary>
    internal static (int offset, int count) FindElementBlock(byte[] bytes, int searchStart)
    {
        int offset = searchStart;
        const int subRecordSize = 38;

        // Ищем начало: 12 нулевых байт + uint32 тип (0x10000..0x10003)
        while (offset + subRecordSize <= bytes.Length)
        {
            bool allZero = true;
            for (int i = 0; i < 12; i++)
            {
                if (bytes[offset + i] != 0) { allZero = false; break; }
            }
            if (allZero)
            {
                var marker = BitConverter.ToUInt32(bytes, offset + 12);
                if (marker >= 0x10000 && marker <= 0x10003)
                {
                    // Определяем размер записи: проверяем 38 и 76 байт
                    // 76-байтные записи содержат две подзаписи по 38 байт
                    int recordSize = subRecordSize;
                    if (offset + 76 <= bytes.Length)
                    {
                        bool secondSubHasMarker = true;
                        for (int i = 0; i < 12; i++)
                        {
                            if (bytes[offset + 38 + i] != 0) { secondSubHasMarker = false; break; }
                        }
                        if (secondSubHasMarker)
                        {
                            var m2 = BitConverter.ToUInt32(bytes, offset + 38 + 12);
                            if (m2 >= 0x10000 && m2 <= 0x10003)
                                recordSize = 76;
                        }
                    }

                    // Считаем количество записей
                    int count = 0;
                    int scan = offset;
                    while (scan + recordSize <= bytes.Length)
                    {
                        bool recZero = true;
                        for (int i = 0; i < 12; i++)
                        {
                            if (bytes[scan + i] != 0) { recZero = false; break; }
                        }
                        if (!recZero) break;
                        var m = BitConverter.ToUInt32(bytes, scan + 12);
                        if (m < 0x10000 || m > 0x10003) break;
                        count++;
                        scan += recordSize;
                    }
                    if (count > 0)
                        return (offset, count);
                }
            }
            offset += 4;
        }

        throw new InvalidOperationException("Блок элементов не найден в файле");
    }

    /// <summary>
    /// Читает один элемент из подзаписи 38 байт.
    /// ID узлов — int32 (4 байта) на позициях +16, +20, +24, +28.
    /// </summary>
    static void ParseOneElement(byte[] bytes, int baseOff, int elemIndex, LiraSchemaData data)
    {
        var marker = BitConverter.ToUInt32(bytes, baseOff + 12);
        int binType = (int)(marker & 0xFFFF);

        // ID узлов — int32 (1-based LIRA ID) на позициях +18, +22, +28, +32
        var n1 = BitConverter.ToInt32(bytes, baseOff + 18);
        var n2 = BitConverter.ToInt32(bytes, baseOff + 22);
        var n3 = BitConverter.ToInt32(bytes, baseOff + 28);
        var n4 = BitConverter.ToInt32(bytes, baseOff + 32);

        int liraType = binType switch
        {
            0 => 10,   // стержень
            1 => 42,   // оболочка
            2 => 44,   // оболочка
            // 3 → 57: исключается (не является полноценной оболочкой в LIRA)
            _ => 0     // неизвестные типы пропускаются
        };

        if (liraType == 0) return; // пропуск неизвестных/исключённых типов

        int[] nodeIds = binType switch
        {
            0 => new[] { n1, n2 },
            _ => new[] { n1, n2, n3, n4 }
        };

        nodeIds = nodeIds.Where(id => id != 0).ToArray();
        if (nodeIds.Length == 0) return;

        int stiffId = binType == 0 ? BitConverter.ToInt32(bytes, baseOff + 24) : 0;

        data.Elements.Add(new LiraElementRecord(
            Id:           elemIndex,
            FeType:       liraType,
            SectionCount: 1,
            StiffnessId:  stiffId,
            NodeIds:      nodeIds
        ));
    }

    /// <summary>
    /// Читает элементы из Block 4.
    /// Записи по 76 байт (две подзаписи по 38 байт), или по 38 байт.
    /// ID узлов — 1-based LIRA ID.
    /// </summary>
    internal static void ParseElements(byte[] bytes, ref int offset, LiraSchemaData data)
    {
        var (blockOffset, count) = FindElementBlock(bytes, offset);
        offset = blockOffset;

        // Определяем размер записи
        int recordSize = 38;
        if (offset + 76 <= bytes.Length)
        {
            bool secondSubHasMarker = true;
            for (int i = 0; i < 12; i++)
            {
                if (bytes[offset + 38 + i] != 0) { secondSubHasMarker = false; break; }
            }
            if (secondSubHasMarker)
            {
                var m2 = BitConverter.ToUInt32(bytes, offset + 38 + 12);
                if (m2 >= 0x10000 && m2 <= 0x10003)
                    recordSize = 76;
            }
        }

        int elemIndex = 1;
        for (int i = 0; i < count; i++)
        {
            int baseOff = offset + i * recordSize;

            if (recordSize == 76)
            {
                // Две подзаписи по 38 байт
                ParseOneElement(bytes, baseOff, elemIndex++, data);
                ParseOneElement(bytes, baseOff + 38, elemIndex++, data);
            }
            else
            {
                ParseOneElement(bytes, baseOff, elemIndex++, data);
            }
        }

        offset += count * recordSize;
    }
}
