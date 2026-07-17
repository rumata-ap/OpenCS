using System.Text;

namespace CScore.Import;

/// <summary>
/// Парсер бинарного формата расчётной схемы ЛираСАПР (.lir).
/// Читает заголовок (3 null-terminated строки) и пропускает метаданные (52 байта).
/// Парсинг узлов и элементов реализуется в следующих задачах.
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
    /// </summary>
    public static LiraSchemaData Parse(string filePath)
    {
        var bytes = File.ReadAllBytes(filePath);
        var data  = new LiraSchemaData();
        var offset = SkipHeader(bytes);
        SkipMetadata(bytes, ref offset);
        ParseNodes3D(bytes, ref offset, data);
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

    /// <summary>
    /// Определяет начало Block 2 (3D координаты) и количество записей.
    /// Block 1: 2D координаты, 28 байт/запись, маркер 0xE4 0xBC на смещении +23/+24.
    /// После Block 1 следует 1 байт-разделитель (0x00), затем Block 2.
    /// Block 2: 3D координаты, 28 байт/запись, [double X][double Y][double Z][int32=1].
    /// </summary>
    internal static (int offset, int count) FindBlock2(byte[] bytes, int startOffset)
    {
        const int recordSize = 28;
        int offset = startOffset;

        // Шаг 1: пропускаем записи Block 1 (маркер 0xE4 0xBC на +23/+24)
        while (offset + recordSize <= bytes.Length)
        {
            if (bytes[offset + 23] == 0xE4 && bytes[offset + 24] == 0xBC)
            {
                offset += recordSize;
                continue;
            }
            break;
        }

        // Шаг 2: пропускаем байт-разделитель между Block 1 и Block 2
        while (offset < bytes.Length && bytes[offset] == 0x00)
            offset++;

        // Шаг 3: считаем записи Block 2 (int32=1 на смещении +24, координаты в разумных пределах)
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

    /// <summary>Читает 3D координаты узлов из Block 2.</summary>
    static void ParseNodes3D(byte[] bytes, ref int offset, LiraSchemaData data)
    {
        var (blockOffset, count) = FindBlock2(bytes, offset);
        offset = blockOffset;

        for (int i = 0; i < count; i++)
        {
            int baseOff = offset + i * 28;
            double x = BitConverter.ToDouble(bytes, baseOff);
            double y = BitConverter.ToDouble(bytes, baseOff + 8);
            double z = BitConverter.ToDouble(bytes, baseOff + 16);

            data.Nodes.Add(new LiraNodeRecord(
                Id:     i,
                X:      x,
                Y:      y,
                Z:      z,
                DofMask: 0 // в .lir не кодируется в этой секции
            ));
        }

        offset += count * 28;
    }

    /// <summary>
    /// Определяет начало блока элементов (Block 4).
    /// Block 4 начинается после Block 2. Первые 12 байт — нули, затем uint32 тип.
    /// </summary>
    internal static (int offset, int count) FindElementBlock(byte[] bytes, int afterBlock2)
    {
        int offset = afterBlock2;
        int recordSize = 38;

        // Ищем начало: 12 нулевых байт + uint32 тип (0x10000..0x10003)
        while (offset + recordSize <= bytes.Length)
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
                    return (offset, count);
                }
            }
            offset += 4;
        }

        throw new InvalidOperationException("Блок элементов не найден в файле");
    }

    /// <summary>Читает элементы из Block 4.</summary>
    internal static void ParseElements(byte[] bytes, ref int offset, LiraSchemaData data)
    {
        var (blockOffset, count) = FindElementBlock(bytes, offset);
        offset = blockOffset;
        int recordSize = 38;

        for (int i = 0; i < count; i++)
        {
            int baseOff = offset + i * recordSize;
            var marker = BitConverter.ToUInt32(bytes, baseOff + 12);
            int elemType = (int)(marker & 0xFFFF); // младшие 16 бит = тип

            // ID узлов — uint16 на позициях +18, +22, +26, +30
            var n1 = BitConverter.ToUInt16(bytes, baseOff + 18);
            var n2 = BitConverter.ToUInt16(bytes, baseOff + 22);
            var n3 = BitConverter.ToUInt16(bytes, baseOff + 26);
            var n4 = BitConverter.ToUInt16(bytes, baseOff + 30);

            int[] nodeIds = elemType switch
            {
                0 => new[] { (int)n1, (int)n2 },                         // стержень: 2 узла
                1 => n4 == 0
                    ? new[] { (int)n1, (int)n2, (int)n3 }                // треугольник: 3 узла
                    : new[] { (int)n1, (int)n2, (int)n3, (int)n4 },     // quad: 4 узла
                2 => new[] { (int)n1, (int)n2, (int)n3, (int)n4 },      // quad: 4 узла
                3 => n4 == 0
                    ? new[] { (int)n1, (int)n2, (int)n3 }                // треугольник
                    : new[] { (int)n1, (int)n2, (int)n3, (int)n4 },     // quad
                _ => Array.Empty<int>()
            };

            if (nodeIds.Length == 0) continue;

            // Stiffness ID из поля +24 (int32), только для стержней
            int stiffId = elemType == 0 ? BitConverter.ToInt32(bytes, baseOff + 24) : 0;

            data.Elements.Add(new LiraElementRecord(
                Id:            i,
                FeType:        elemType,
                SectionCount:  1,
                StiffnessId:   stiffId,
                NodeIds:       nodeIds
            ));
        }

        offset += count * recordSize;
    }
}
