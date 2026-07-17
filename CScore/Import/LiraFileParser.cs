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
        // ParseNodes3D и ParseElements реализуются в следующих задачах
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
}
