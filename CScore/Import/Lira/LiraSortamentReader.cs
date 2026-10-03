using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace CScore.Import;

/// <summary>
/// Строка (профиль) сортамента ЛИРЫ: имя и размеры записи, см / см² / см⁴. Позиции — спека
/// <c>2026-10-02-imported-steel-sections-design.md</c>, §10.
/// </summary>
/// <param name="Name">Имя профиля («80 x 3», «20Ш1»), без концевых пробелов.</param>
/// <param name="Values">Значения записи (float32 подряд с 10-го байта).</param>
public sealed record LiraSortamentRow(string Name, double[] Values)
{
    double At(int i) => i < Values.Length ? Values[i] : 0;

    /// <summary>Высота H (диаметр трубы и круга, ширина листа), см.</summary>
    public double H => At(0);
    /// <summary>Ширина верхней полки, см (у уголка — 0).</summary>
    public double B1 => At(1);
    /// <summary>Ширина нижней полки, см (у уголка — горизонтальная полка).</summary>
    public double B2 => At(2);
    /// <summary>Толщина верхней полки, см.</summary>
    public double Tf1 => At(3);
    /// <summary>Толщина стенки (трубы, листа; сторона квадрата), см.</summary>
    public double Tw => At(4);
    /// <summary>Толщина нижней полки, см.</summary>
    public double Tf2 => At(5);
    /// <summary>Радиус R1 (у гнутых профилей — внутренний радиус гиба), см.</summary>
    public double R1 => At(9);
    /// <summary>Радиус R2 (закругление полки), см.</summary>
    public double R2 => At(10);
    /// <summary>Площадь, см².</summary>
    public double A => At(16);
    /// <summary>Момент инерции относительно оси ‖ полкам (Y1), см⁴.</summary>
    public double Iy => At(19);
    /// <summary>Момент инерции относительно оси ‖ стенке (Z1), см⁴.</summary>
    public double Iz => At(20);
    /// <summary>Момент инерции при свободном кручении, см⁴.</summary>
    public double It => At(40);
}

/// <summary>Файл сортамента ЛИРЫ (<c>&lt;DataBase&gt;\*.profiles.srt</c>): один стандарт, один вид профиля.</summary>
/// <param name="Title">Название («Профили стальные гнутые замкнутые … (ГОСТ 30245-94)»).</param>
/// <param name="Standard">Стандарт («ГОСТ 30245-94»); пусто — не распознан.</param>
/// <param name="Rows">Профили в порядке файла.</param>
public sealed record LiraSortamentFile(string Title, string Standard, IReadOnlyList<LiraSortamentRow> Rows)
{
    /// <summary>
    /// Профиль по имени из жёсткости (<c>Shape = |80 x 3|</c>): без учёта концевых пробелов, запятая и точка
    /// десятичного разделителя равнозначны; null — нет.
    /// </summary>
    public LiraSortamentRow? Find(string name)
    {
        string key = Key(name);
        return Rows.FirstOrDefault(r => Key(r.Name) == key);
    }

    static string Key(string name) => name.Trim().Replace(',', '.');
}

/// <summary>
/// Читатель сортамента ЛИРЫ «LiraWin steel Sortament ver. 1.6». Заголовок — три описателя по 24 байта с 0x20
/// (первый — профили: число строк, ширина имени, размер записи, смещения имён и записей), затем pstr
/// «название[\r стандарт]». Имена — фиксированной ширины, cp1251 с <c>\0</c>; записи — фиксированного размера,
/// размеры — float32 с 10-го байта записи.
/// </summary>
public static class LiraSortamentReader
{
    static readonly byte[] Magic = "LiraWin steel Sortament ver. 1.6"u8.ToArray();
    const int DescriptorsEnd = 0x20 + 3 * 24;
    const int ValuesOffset = 10;

    static Encoding Cp1251
    {
        get
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(1251);
        }
    }

    /// <summary>Читает файл сортамента.</summary>
    /// <exception cref="InvalidDataException">Файл не сортамент ЛИРЫ или повреждён.</exception>
    public static LiraSortamentFile Read(string path) => Read(File.ReadAllBytes(path));

    /// <summary>Разбирает содержимое файла сортамента.</summary>
    /// <exception cref="InvalidDataException">Данные не сортамент ЛИРЫ или повреждены.</exception>
    public static LiraSortamentFile Read(ReadOnlySpan<byte> data)
    {
        if (data.Length < DescriptorsEnd + 1 || !data[..Magic.Length].SequenceEqual(Magic))
            throw new InvalidDataException("Файл не является сортаментом ЛИРЫ (нет сигнатуры «LiraWin steel Sortament ver. 1.6»).");

        int count = BinaryPrimitives.ReadUInt16LittleEndian(data[0x22..]);
        int nameWidth = BinaryPrimitives.ReadUInt16LittleEndian(data[0x26..]);
        int recordSize = BinaryPrimitives.ReadUInt16LittleEndian(data[0x2A..]);
        long namesOffset = BinaryPrimitives.ReadUInt32LittleEndian(data[0x2C..]);
        long recordsOffset = BinaryPrimitives.ReadUInt32LittleEndian(data[0x34..]);
        if (count > 0 && (nameWidth == 0 || recordSize <= ValuesOffset
                          || namesOffset + (long)count * nameWidth > recordsOffset
                          || recordsOffset + (long)count * recordSize > data.Length))
            throw new InvalidDataException("Сортамент ЛИРЫ повреждён: таблица профилей выходит за пределы файла.");

        var encoding = Cp1251;
        int titleLength = data[DescriptorsEnd];
        if (DescriptorsEnd + 1 + titleLength > data.Length)
            throw new InvalidDataException("Сортамент ЛИРЫ повреждён: название обрывается.");
        string title = encoding.GetString(data.Slice(DescriptorsEnd + 1, titleLength));
        string[] parts = title.Split('\r', 2);
        string standard = ScadPrfReader.StandardOf(title);
        if (standard.Length == 0 && parts.Length == 2) standard = parts[1].Trim();

        var rows = new List<LiraSortamentRow>(count);
        int valueCount = (recordSize - ValuesOffset) / 4;
        for (int i = 0; i < count; i++)
        {
            var nameBytes = data.Slice((int)(namesOffset + (long)i * nameWidth), nameWidth);
            int end = nameBytes.IndexOf((byte)0);
            string name = encoding.GetString(end < 0 ? nameBytes : nameBytes[..end]).TrimEnd();
            var record = data.Slice((int)(recordsOffset + (long)i * recordSize), recordSize);
            var values = new double[valueCount];
            for (int k = 0; k < valueCount; k++)
            {
                // float32 → кратчайшая десятичная запись (0,2 вместо 0,200000003), как в ScadPrfReader.
                float v = BinaryPrimitives.ReadSingleLittleEndian(record[(ValuesOffset + 4 * k)..]);
                values[k] = double.Parse(v.ToString("R", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
            }
            rows.Add(new LiraSortamentRow(name, values));
        }
        return new LiraSortamentFile(parts[0].Trim(), standard, rows);
    }
}
