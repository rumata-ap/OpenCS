using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;

namespace CScore.Import;

/// <summary>Колонка таблицы сортамента SCAD.</summary>
/// <param name="Name">Короткое имя («h», «A», «Iy=Iz»).</param>
/// <param name="Unit">Единица («mm», «cm2», «cm4»).</param>
/// <param name="Scale">Множитель перевода из СИ в единицу колонки (мм — 1000, см⁴ — 1e8).</param>
public sealed record ScadPrfColumn(string Name, string Unit, double Scale)
{
    /// <summary>Колонка отвечает имени: совпадение целиком или одной из частей «Iy=Iz» (регистр важен).</summary>
    public bool Is(string name) => Name == name || Name.Split('=').Contains(name);
}

/// <summary>Строка (профиль) таблицы сортамента SCAD: имя и значения в единицах колонок.</summary>
public sealed record ScadPrfRow(string Name, double[] Values);

/// <summary>Таблица сортамента SCAD (один стандарт, один вид профиля).</summary>
/// <param name="Kind">Код вида SCAD: 1 двутавр, 2 тавр, 3 швеллер, 4/5 уголок равно-/неравнополочный,
/// 13 квадрат, 15 лист, 16 труба, 17/18 зеркальный уголок (только имена), 19/20 труба квадратная/прямоугольная,
/// 25 круг.</param>
/// <param name="Code">Код таблицы в строке жёсткости STZ («d1», «okv2012»).</param>
/// <param name="Title">Название таблицы (рус.).</param>
/// <param name="Standard">Стандарт из названия («ГОСТ 8240-89», «СТО АСЧМ 20-93»); пусто — не распознан.</param>
/// <param name="Columns">Числовые колонки (без колонки имени).</param>
/// <param name="Rows">Профили в порядке файла (номер в STZ — с 1).</param>
public sealed record ScadPrfTable(int Kind, string Code, string Title, string Standard,
    IReadOnlyList<ScadPrfColumn> Columns, IReadOnlyList<ScadPrfRow> Rows)
{
    /// <summary>Индекс колонки по имени; −1 — нет.</summary>
    public int ColumnIndex(string name)
    {
        for (int i = 0; i < Columns.Count; i++)
            if (Columns[i].Is(name)) return i;
        return -1;
    }

    /// <summary>Значение колонки строки в единицах колонки; null — колонки нет.</summary>
    public double? Value(ScadPrfRow row, string name)
    {
        int i = ColumnIndex(name);
        return i >= 0 && i < row.Values.Length ? row.Values[i] : null;
    }

    /// <summary>Значение колонки строки в единицах СИ (м, м², м⁴); null — колонки нет.</summary>
    public double? ValueSi(ScadPrfRow row, string name)
    {
        int i = ColumnIndex(name);
        return i >= 0 && i < row.Values.Length && Columns[i].Scale > 0 ? row.Values[i] / Columns[i].Scale : null;
    }
}

/// <summary>Файл сортамента SCAD (<c>&lt;каталог SCAD&gt;\64\&lt;база&gt;.PRF</c>).</summary>
/// <param name="Title">Название базы.</param>
/// <param name="Tables">Таблицы в порядке файла.</param>
public sealed record ScadPrfFile(string Title, IReadOnlyList<ScadPrfTable> Tables)
{
    /// <summary>Таблица по коду (без учёта регистра); null — нет.</summary>
    public ScadPrfTable? Table(string code) =>
        Tables.FirstOrDefault(t => string.Equals(t.Code, code, StringComparison.OrdinalIgnoreCase));

    /// <summary>Строка таблицы по номеру из STZ (с 1); null — нет таблицы или номера.</summary>
    public (ScadPrfTable Table, ScadPrfRow Row)? Find(string tableCode, int rowNumber)
    {
        var table = Table(tableCode);
        if (table == null || rowNumber < 1 || rowNumber > table.Rows.Count) return null;
        return (table, table.Rows[rowNumber - 1]);
    }

    /// <summary>
    /// Таблица, предшествующая <paramref name="table"/> в файле: у зеркальных уголков (вид 17/18) — базовая
    /// таблица с размерами; null — первой таблицы.
    /// </summary>
    public ScadPrfTable? Previous(ScadPrfTable table)
    {
        int i = Tables.ToList().IndexOf(table);
        return i > 0 ? Tables[i - 1] : null;
    }
}

/// <summary>
/// Читатель двоичного сортамента SCAD (<c>**PRFL**</c>, версия 3). Формат — спека
/// <c>2026-10-02-imported-steel-sections-design.md</c>, §9: заголовок с единицами колонок, затем таблицы
/// «вид, код, колонки, строки (имя + float32)». Строки — cp1251.
/// </summary>
public static class ScadPrfReader
{
    static readonly byte[] Magic = "**PRFL**"u8.ToArray();

    static readonly Regex StandardPattern = new(
        @"(?:ГОСТ(?:\s+Р)?|СТО\s+АСЧМ|ТУ)\s+[0-9][0-9.\-]*(?:\*)?(?:-[0-9]+)*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    static Encoding Cp1251
    {
        get
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(1251);
        }
    }

    /// <summary>Читает файл сортамента.</summary>
    /// <exception cref="InvalidDataException">Файл не PRF SCAD или повреждён.</exception>
    public static ScadPrfFile Read(string path) => Read(File.ReadAllBytes(path));

    /// <summary>Разбирает содержимое файла сортамента.</summary>
    /// <exception cref="InvalidDataException">Данные не PRF SCAD или повреждены.</exception>
    public static ScadPrfFile Read(ReadOnlySpan<byte> data)
    {
        var r = new Cursor(data.ToArray(), Cp1251);
        if (data.Length < 8 || !data[..8].SequenceEqual(Magic))
            throw new InvalidDataException("Файл не является сортаментом SCAD (нет сигнатуры **PRFL**).");
        r.Skip(8);
        int version = r.U8();
        if (version != 3)
            throw new InvalidDataException($"Версия сортамента SCAD {version} не поддерживается (ожидается 3).");
        int unitCount = r.U8();
        int tableCount = r.U16();
        r.Skip(4);
        r.Skip(12); // E, ν, ρ базы
        string title = r.PString();
        r.PString();
        r.PString();

        var units = new (string Name, double Scale)[unitCount];
        for (int i = 0; i < unitCount; i++)
            units[i] = (r.FixedString(10), r.F32());

        var tables = new List<ScadPrfTable>(tableCount);
        for (int t = 0; t < tableCount; t++)
        {
            int kind = r.U8();
            string code = r.FixedString(9);
            int nc = r.U8();
            r.U8(); // признак таблицы, на формат строк не влияет
            int nr = r.U16();
            int namesLength = r.U16();
            string tableTitle = r.PString().Trim();
            r.PString();
            r.PString();
            if (nc < 1)
                throw new InvalidDataException($"Таблица «{code}» сортамента SCAD: нет колонок.");

            var ids = r.Bytes(nc);
            int namesEnd = r.Position + namesLength;
            var names = new List<string>(nc);
            for (int i = 0; i < nc; i++) names.Add(r.CString());
            if (r.Position != namesEnd)
                throw new InvalidDataException($"Таблица «{code}» сортамента SCAD: длина имён колонок не сходится.");
            r.Skip(nc); // видимость колонок

            var columns = new List<ScadPrfColumn>(nc - 1);
            for (int i = 1; i < nc; i++)
            {
                int id = ids[i] - 1;
                if (id < 0 || id >= units.Length)
                    throw new InvalidDataException($"Таблица «{code}» сортамента SCAD: неизвестная единица колонки {i}.");
                columns.Add(new ScadPrfColumn(names[i - 1], units[id].Name, units[id].Scale));
            }

            var rows = new List<ScadPrfRow>(nr);
            for (int i = 0; i < nr; i++)
            {
                string name = r.PString().Trim();
                var values = new double[nc - 1];
                for (int k = 0; k < values.Length; k++) values[k] = r.F32();
                rows.Add(new ScadPrfRow(name, values));
            }
            tables.Add(new ScadPrfTable(kind, code, tableTitle, StandardOf(tableTitle), columns, rows));
        }
        return new ScadPrfFile(title.Trim(), tables);
    }

    /// <summary>Стандарт из названия таблицы («… по ГОСТ 8240-89» → «ГОСТ 8240-89»); пусто — не найден.</summary>
    public static string StandardOf(string title)
    {
        var m = StandardPattern.Match(title);
        return m.Success ? Regex.Replace(m.Value, @"\s+", " ").Trim() : "";
    }

    sealed class Cursor(byte[] data, Encoding encoding)
    {
        public int Position { get; private set; }

        void Need(int count)
        {
            if (Position + count > data.Length)
                throw new InvalidDataException("Сортамент SCAD обрывается раньше конца данных.");
        }

        public void Skip(int count) { Need(count); Position += count; }
        public int U8() { Need(1); return data[Position++]; }
        public int U16() { Need(2); int v = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(Position)); Position += 2; return v; }
        /// <summary>float32 как double с кратчайшим десятичным представлением (254.8, а не 254.800003).</summary>
        public double F32()
        {
            Need(4);
            float v = BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(Position));
            Position += 4;
            return double.Parse(v.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                System.Globalization.CultureInfo.InvariantCulture);
        }
        public byte[] Bytes(int count) { Need(count); var v = data.AsSpan(Position, count).ToArray(); Position += count; return v; }

        public string PString()
        {
            int n = U8();
            Need(n);
            string s = encoding.GetString(data, Position, n);
            Position += n;
            return s;
        }

        public string FixedString(int length)
        {
            Need(length);
            var span = data.AsSpan(Position, length);
            int end = span.IndexOf((byte)0);
            string s = encoding.GetString(end < 0 ? span : span[..end]);
            Position += length;
            return s;
        }

        public string CString()
        {
            int end = Array.IndexOf(data, (byte)0, Position);
            if (end < 0) throw new InvalidDataException("Сортамент SCAD: не завершено имя колонки.");
            string s = encoding.GetString(data, Position, end - Position);
            Position = end + 1;
            return s;
        }
    }
}
