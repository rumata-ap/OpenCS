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
}
