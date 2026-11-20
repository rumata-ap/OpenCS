using System.Buffers.Binary;
using System.Text;

namespace CScore.Import;

/// <summary>Род определения жёсткости бинарного .lir (по коду типа записи).</summary>
public enum LiraStiffnessKind
{
    /// <summary>Параметрическое сечение стержня (коды 0–7, 47–50; код 0 — брус S0).</summary>
    StandardSection,

    /// <summary>Пластина/оболочка GEI (код 36).</summary>
    Plate,

    /// <summary>Численная жёсткость стержня EF, EIy, EIz, GIk (код 14).</summary>
    NumericBar,

    /// <summary>Стальной профиль из сортамента (код 42): численная часть + имя профиля и файл сортамента.</summary>
    SteelProfile,

    /// <summary>Грунт / объёмный КЭ 281–284 (код 43).</summary>
    Soil,

    /// <summary>Связь конечной жёсткости КЭ 51 (код 17).</summary>
    Spring51,

    /// <summary>Прочие специальные КЭ: поля прочитаны по раскладке, смысл не расшифрован.</summary>
    Other
}

/// <summary>
/// Определение жёсткости из таблицы бинарного .lir (то, что в текстовом экспорте ЛИРЫ лежит в документе 3).
/// </summary>
/// <remarks>
/// Единицы — как в файле ЛИРЫ: E — т/м², размеры сечения и толщина пластины — см, плотность сечений и пластин —
/// т/м³; у численных жёсткостей и профилей сортамента <see cref="Density"/> — погонный вес, т/м, а EF, EIy, EIz,
/// GIk — в т и м. Поле, не определённое для рода записи, равно <c>null</c>.
/// </remarks>
/// <param name="Number">Номер жёсткости (1-based, как в документе 1 и 3).</param>
/// <param name="TypeCode">Код типа записи в файле (коды 1000–1075 приведены к 42).</param>
/// <param name="Kind">Род определения.</param>
/// <param name="Name">Имя жёсткости (пусто — ЛИРА строит имя сама).</param>
/// <param name="Color">Цвет жёсткости (COLORREF).</param>
/// <param name="Values">Все поля <c>float32</c> записи в порядке файла (без заголовка).</param>
/// <param name="Integers">Все целочисленные поля <c>u16/u32</c> записи в порядке файла (без заголовка).</param>
/// <param name="E">Модуль упругости.</param>
/// <param name="Poisson">Коэффициент Пуассона.</param>
/// <param name="Density">Плотность (т/м³) или погонный вес (т/м) — см. примечание.</param>
/// <param name="Width">Ширина сечения b, см (брус S0).</param>
/// <param name="Height">Высота сечения h, см (брус S0).</param>
/// <param name="Thickness">Толщина пластины или слоя грунта, см.</param>
/// <param name="EF">Продольная жёсткость (численная жёсткость, профиль).</param>
/// <param name="EIy">Изгибная жёсткость относительно Y1.</param>
/// <param name="EIz">Изгибная жёсткость относительно Z1.</param>
/// <param name="GIk">Крутильная жёсткость (у профилей сортамента ЛИРА в TXT пересчитывает её по сортаменту).
/// У профиля с характеристиками, заданными пользователем (<c>UserProps</c> в строке свойств), EF, EIy, EIz, GIk
/// берутся оттуда — так же выводит их ЛИРА.</param>
/// <param name="Profile">Имя профиля из сортамента («50 x 50 x 5»).</param>
/// <param name="SortamentFile">Файл сортамента («ug.profiles.srt»).</param>
/// <param name="ProfileFullName">Полное имя профиля («1. Уголок равнополочный …»).</param>
/// <param name="ProfileParts">Число частей составного сечения из сортамента (0 — простое).</param>
/// <param name="BinaryOffset">Смещение записи в файле.</param>
public sealed record LiraStiffnessDefinition(
    int Number,
    int TypeCode,
    LiraStiffnessKind Kind,
    string Name,
    uint Color,
    float[] Values,
    long[] Integers,
    double? E,
    double? Poisson,
    double? Density,
    double? Width,
    double? Height,
    double? Thickness,
    double? EF,
    double? EIy,
    double? EIz,
    double? GIk,
    string? Profile,
    string? SortamentFile,
    string? ProfileFullName,
    int ProfileParts,
    long BinaryOffset);

/// <summary>
/// Чтение таблицы определений жёсткостей бинарного .lir (имена, тип, E, плотность, размеры сечений, профили).
/// </summary>
/// <remarks>
/// <para>Раскладка восстановлена по декомпиляции <c>SapSt.dll</c> и <c>Sssc.dll</c> ЛИРА-САПР 2024 (IDA, 20.11.2026):
/// таблицу читает <c>SapSt_130</c> (<c>[u16 n]</c> + записи), запись — фабрика по коду типа и <c>Serialize</c>
/// класса жёсткости с версией схемы <c>CArchive::GetObjectSchema</c>.</para>
/// <para>В файле таблица лежит непосредственно перед массивом номеров жёсткостей:
/// <c>[u16 n+1][u16 n][n записей][u16 n2][n2 записей][2 байта][массив номеров]</c>; вторая таблица — почти копия
/// первой (отличается флаг после номера) или пуста. Версия схемы в блоке не хранится — подбирается из порогов,
/// встречающихся в раскладках; таблица принимается, только если обе таблицы читаются до байта точно
/// до начала массива. Иначе определения не публикуются, причина — в заметках.</para>
/// <para>Запись: <c>[u16 код][u16 номер][u32 флаг]</c> + поля класса; код 42 (сортамент) — особый:
/// <c>[u16 42][u16 версия 7000|9100][u16 14]</c> + тело численной жёсткости + тело профиля (рекурсивно для
/// составных сечений). Сверено с документом 3 на 8 схемах (коды 0, 6, 8, 9, 14, 17, 36, 42, 43, 56); остальные
/// простые коды заданы по декомпиляции и на образцах не встречались. Сложные коды (12, 13, 15, 18, 35, 37, 40,
/// 46, 53, 54, 59, 60, 62–77, 87, 88) и дополнительные блоки сечений/пластин (поле после версии 5020 ≠ 0)
/// не разобраны: запись без длины пропустить нельзя, поэтому такая таблица целиком не читается.</para>
/// </remarks>
public static class LiraStiffnessDefinitionsReader
{
    /// <summary>Сколько байт перед массивом номеров просматривать в поисках начала таблицы.</summary>
    const int SearchSpan = 16 * 1024 * 1024;

    /// <summary>Верхняя граница числа записей таблицы.</summary>
    const int MaxDefinitions = 60000;

    /// <summary>Верхняя граница длины строки записи.</summary>
    const int MaxStringLength = 1 << 20;

    /// <summary>Кандидаты версии схемы — пороги из раскладок (от новых к старым).</summary>
    static readonly int[] Versions = [13000, 12004, 12003, 9603, 9420, 9417, 9415, 9412, 9408, 9407, 9104, 9102, 8200, 5021, 5000];

    static readonly Encoding Cp1251 = CreateCp1251();

    static Encoding CreateCp1251()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1251);
    }

    // ------------------------------------------------------------------ Раскладки

    /// <summary>
    /// Раскладки простых кодов после заголовка <c>[u16 код][u16 номер][u32 флаг]</c>. Мини-язык:
    /// <c>f</c> — f32, <c>h</c> — u16, <c>i</c> — u32, <c>N</c> — имя и цвет (строка + u32),
    /// <c>S</c> — строка, <c>Z</c> — предыдущее u16 обязано быть 0 (иначе — неразобранный доп. блок),
    /// <c>T</c> — две строки, если предыдущее u16 &gt; 2; <c>{&gt;=9104 …}</c>, <c>{&gt;12003 …}</c>,
    /// <c>{&lt;9104 …}</c> — поля, присутствующие только при такой версии схемы.
    /// </summary>
    static readonly Dictionary<int, string> Layouts = BuildLayouts();

    static Dictionary<int, string> BuildLayouts()
    {
        // Стандартные сечения: k×f32, [≥8200] u16, u16, [≥9104] f32 + имя, [>12003] m×4 байта, [>5020] u16 (= 0).
        static string Std(int k, int m, bool g8200, bool g5020) =>
            Rep("f", k) + (g8200 ? " {>=8200 h}" : " h") + " h {>=9104 f N} {>12003 " + Rep("i", m) + "}" +
            (g5020 ? " {>5020 h Z}" : "");

        var d = new Dictionary<int, string>
        {
            [0]  = Std(6, 4, true, true),
            [1]  = Std(8, 6, true, true),
            [2]  = Std(8, 6, true, true),
            [3]  = Std(10, 8, true, true),
            [4]  = Std(8, 6, true, true),
            [5]  = Std(8, 6, true, true),
            [6]  = Std(6, 4, true, true),
            [7]  = Std(10, 8, true, false),
            [47] = Std(10, 8, false, false),
            [48] = Std(8, 6, false, false),
            [49] = Std(9, 7, false, false),
            [50] = Std(9, 7, false, false),

            // Пластина (SapSt sub_1008560D): ρ, E, ν, h, f, f, [≥8200] u16, имя, [>5020] u16 (= 0),
            // [>9420] u16 GEO + 3 f32, [>12004] u32 + f32.
            [36] = "f f f f f f {>=8200 h} {>=9104 N} {>5020 h Z} {>9420 h f f f} {>12004 i f}",

            // Численная жёсткость стержня (SapSt_80, M_CDDSect10).
            [14] = NumericBarLayout,

            [8]  = "f f f f {>=9104 N}",                                         // M_CDDSect1
            [9]  = "f f f {>=9417 h f} f f {>=9104 N} {>=9408 f {>=9415 f}}",     // M_CDDSect2
            [10] = "f f f {>=9417 h f} f f {>=9104 N}",                           // M_CDDSect3
            [11] = "f f f f f f {>=9104 N}",                                     // M_CDDSect4
            [17] = "h f {>=9104 N}",                                             // M_CDDSect51: направление, жёсткость
            [19] = "f f {>=9104 N}",                                             // M_CDDSect54
            [20] = "f f f f f f f {>=9104 N}",                                   // M_CDDSect55
            [21] = "f f f f f f f {>=9104 N}",                                   // M_CDDSect154
            [38] = "h h f f {>=9104 N}",                                         // M_CDDSect261
            [39] = "h f f {>=9104 N}",                                           // M_CDDSect262
            // M_CDDSect281 (грунт): u16, E, ν, h, ρ, 3×f32, [≥9102] 2×f32 [≥9603] f32, u16, [≥9407] f32.
            [43] = "h f f f f f f f {>=9102 f f {>=9603 f} h {>=9407 f}} {>=9104 N}",
            [44] = "h f f f f {>=9104 N}",                                       // M_CDDSect341
            [45] = "h f f f f f {>=9104 N}",                                     // M_CDDSect521
            [51] = "h h f f f f f {>=9104 N}",                                   // M_CDDSect263
            [52] = "h f f f f f {>=9104 N}",                                     // M_CDDSect264
            [55] = "f f f f f f f {>=9104 N}",                                   // M_CDDSect56
            [56] = "h f f f f T N",                                              // M_CDDSect208
            [57] = "h h f f {>=9104 N}",                                         // M_CDDSect251
            [58] = "h f f {>=9104 N}",                                           // M_CDDSect252
            [61] = "h f f f f T N",                                              // M_CDDSect207
            [78] = "f f f f {>=9104 N}",                                         // M_CDDSect67
            [79] = "f f f f {>=9104 N}",                                         // M_CDDSect84
            [80] = "f f f {>=9104 N}",                                           // M_CDDSect68_69
            [81] = "f f f f {>=9104 N}",
            [82] = "f f f {>=9104 N}",
            [83] = "f f f f {>=9104 N}",
            [84] = "f f {>=9104 N}",                                             // M_CDDSect1551
            [85] = "f f {>=9104 N}",                                             // M_CDDSect1555
            [86] = "f {>=9104 N}",                                               // M_CDDSect1558_1559
        };
        return d;
    }

    /// <summary>Тело численной жёсткости (код 14) — оно же базовая часть профиля сортамента (код 42).</summary>
    const string NumericBarLayout =
        "f f f f f h f f h f f f {>=8200 h} h f f f f f f f {>=9104 N} {>=9408 f {>=9415 f}}";

    static string Rep(string token, int count) => string.Join(" ", Enumerable.Repeat(token, count));

    // ------------------------------------------------------------------ Поиск и чтение

    /// <summary>
    /// Ищет и читает таблицу определений перед массивом номеров жёсткостей.
    /// </summary>
    /// <param name="bytes">Содержимое .lir.</param>
    /// <param name="arrayStart">Смещение массива номеров жёсткостей.</param>
    /// <param name="searchFrom">Нижняя граница поиска (конец геометрического блока).</param>
    /// <param name="notes">Диагностика.</param>
    /// <returns>Определения первой таблицы в порядке файла; пусто — таблица не найдена или не разобрана.</returns>
    public static List<LiraStiffnessDefinition> Read(byte[] bytes, int arrayStart, int searchFrom, List<string> notes)
    {
        int end   = arrayStart - 2;
        int lower = Math.Max(Math.Max(2, searchFrom), arrayStart - SearchSpan);
        var unsupported = new Dictionary<string, int>();

        for (int p = arrayStart - 6; p >= lower; p--)
        {
            int n = ReadU16(bytes, p);
            if (n == 0 || n > MaxDefinitions || ReadU16(bytes, p - 2) != n + 1) continue;

            foreach (int version in Versions)
            {
                try
                {
                    var reader = new Cursor(bytes, p + 2, end);
                    var first  = ReadRecords(ref reader, n, version);
                    int n2     = reader.U16();
                    ReadRecords(ref reader, n2, version);
                    if (reader.Position != end) continue;

                    notes.Add($"определения жёсткостей прочитаны: {first.Count} записей по смещению {p} " +
                              $"(версия схемы {version}, вторая таблица {n2})");
                    return first;
                }
                catch (UnsupportedLayoutException e)
                {
                    unsupported[e.Message] = unsupported.GetValueOrDefault(e.Message) + 1;
                }
                catch (FormatException) { }
            }
        }

        // Неразобранный код в «правильной» таблице всплывает как самая частая причина рядом с массивом,
        // но шум от случайных кандидатов тоже есть — поэтому в заметку попадают несколько первых причин.
        string reasons = unsupported.Count == 0 ? "" :
            "; встречено: " + string.Join(", ", unsupported.OrderByDescending(x => x.Value).Take(3).Select(x => x.Key));
        notes.Add("определения жёсткостей не прочитаны: таблица перед массивом номеров не найдена" + reasons);
        return [];
    }

    static List<LiraStiffnessDefinition> ReadRecords(ref Cursor r, int count, int version)
    {
        var list = new List<LiraStiffnessDefinition>(count);
        for (int i = 0; i < count; i++)
        {
            long offset = r.Position;
            int code = r.U16();
            if (code is >= 1000 and <= 1075) code = 42;

            var rec = new Record();
            int number;
            if (code == 42)
            {
                int own = r.U16();
                if (own != 7000 && own != 9100) throw new UnsupportedLayoutException($"код 42: версия профиля {own}");
                r.U16();                         // код базовой части (14)
                number = r.U16();
                r.U32();                         // флаг записи
                Run(ref r, NumericBarLayout, version, rec);
                ReadProfile(ref r, own, rec, top: true);
            }
            else
            {
                if (!Layouts.TryGetValue(code, out string? layout))
                    throw new UnsupportedLayoutException($"код {code}");
                number = r.U16();
                r.U32();                         // флаг записи (в первой и второй таблицах бывает разным)
                Run(ref r, layout, version, rec);
            }
            list.Add(Build(number, code, rec, offset));
        }
        return list;
    }

    /// <summary>Тело профиля сортамента (Sssc_109): рекурсивно для составных сечений.</summary>
    static void ReadProfile(ref Cursor r, int own, Record rec, bool top)
    {
        string profile = r.CString();
        for (int k = 0; k < 6; k++) r.F32();
        r.CString();
        r.U16();                                 // тип профиля
        r.Skip(46);
        r.U32();
        string srt  = r.CString();
        string full = r.CString();
        string props = "";
        if (own >= 9100)
        {
            props = r.CString();                 // свойства: AutoName/UserName, EIw=, UserProps, NEL, MATS
            r.U32();                             // кодовая страница
        }
        int parts = r.U16();
        if (top)
        {
            rec.Parts = parts;
            rec.UserProps = ParseUserProps(props);
            rec.Profile = profile;
            rec.Sortament = srt;
            rec.FullName = full;
        }
        for (int k = 0; k < parts; k++) ReadProfile(ref r, own, rec, top: false);
    }

    /// <summary>Разобранные на токены раскладки.</summary>
    static readonly Dictionary<string, string[]> TokenCache = [];

    /// <summary>
    /// Характеристики профиля, заданные пользователем: элемент строки свойств
    /// <c>UserProps EF EIy EIz GIk …</c> (разделитель элементов — <c>\0</c>). Null — элемента нет.
    /// </summary>
    static double[]? ParseUserProps(string props)
    {
        foreach (string item in props.Split('\0'))
        {
            if (!item.StartsWith("UserProps ", StringComparison.Ordinal)) continue;
            var parts = item["UserProps ".Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var values = new double[4];
            for (int k = 0; k < values.Length; k++)
                if (k >= parts.Length || !double.TryParse(parts[k], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out values[k]))
                    return null;
            return values;
        }
        return null;
    }

    /// <summary>Исполняет раскладку мини-языка (см. <see cref="Layouts"/>).</summary>
    static void Run(ref Cursor r, string layout, int version, Record rec)
    {
        string[] tokens;
        lock (TokenCache)
            if (!TokenCache.TryGetValue(layout, out tokens!))
                TokenCache[layout] = tokens = layout.Replace("}", " } ").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int pos = 0;
        Exec(ref r, tokens, ref pos, version, rec, active: true);
    }

    static void Exec(ref Cursor r, string[] t, ref int pos, int version, Record rec, bool active)
    {
        while (pos < t.Length)
        {
            string tok = t[pos++];
            if (tok == "}") return;
            if (tok.StartsWith('{'))
            {
                bool cond = Gate(tok, version);
                Exec(ref r, t, ref pos, version, rec, active && cond);
                continue;
            }
            if (!active) continue;
            switch (tok)
            {
                case "f": rec.Values.Add(r.F32()); break;
                case "h": rec.LastShort = r.U16(); rec.Integers.Add(rec.LastShort); break;
                case "i": rec.Integers.Add(r.U32()); break;
                case "S": r.CString(); break;
                case "N": rec.Name = r.CString(); rec.Color = r.U32(); break;
                case "Z":
                    if (rec.LastShort != 0) throw new UnsupportedLayoutException("дополнительный блок сечения/пластины");
                    break;
                case "T":
                    if ((short)rec.LastShort > 2) { r.CString(); r.CString(); }
                    break;
                default: throw new InvalidOperationException($"неизвестный токен раскладки «{tok}»");
            }
        }
    }

    static bool Gate(string token, int version)
    {
        // {>=9104  {>12003  {<9104
        string op = token[1..];
        if (op.StartsWith(">=")) return version >= int.Parse(op[2..]);
        if (op.StartsWith(">"))  return version >  int.Parse(op[1..]);
        if (op.StartsWith("<"))  return version <  int.Parse(op[1..]);
        throw new InvalidOperationException($"неизвестное условие «{token}»");
    }

    static LiraStiffnessDefinition Build(int number, int code, Record rec, long offset)
    {
        var v = rec.Values;
        double? At(int i) => i < v.Count ? v[i] : null;

        LiraStiffnessKind kind = code switch
        {
            0 or 1 or 2 or 3 or 4 or 5 or 6 or 7 or 47 or 48 or 49 or 50 => LiraStiffnessKind.StandardSection,
            36 => LiraStiffnessKind.Plate,
            14 => LiraStiffnessKind.NumericBar,
            42 => LiraStiffnessKind.SteelProfile,
            43 => LiraStiffnessKind.Soil,
            17 => LiraStiffnessKind.Spring51,
            _  => LiraStiffnessKind.Other
        };

        double? e = null, nu = null, ro = null, b = null, h = null, t = null, ef = null, eiy = null, eiz = null, gik = null;
        switch (kind)
        {
            case LiraStiffnessKind.StandardSection:
                ro = At(0); e = At(1);
                if (code is 0 or 6) { b = At(2); h = At(3); }
                break;
            case LiraStiffnessKind.Plate:
                ro = At(0); e = At(1); nu = At(2); t = At(3);
                break;
            case LiraStiffnessKind.NumericBar:
            case LiraStiffnessKind.SteelProfile:
                ro = At(0); ef = At(1); eiy = At(2); eiz = At(3); gik = At(4);
                if (rec.UserProps is { } user) { ef = user[0]; eiy = user[1]; eiz = user[2]; gik = user[3]; }
                break;
            case LiraStiffnessKind.Soil:
                e = At(0); nu = At(1); t = At(2); ro = At(3);
                break;
        }

        return new LiraStiffnessDefinition(
            number, code, kind, rec.Name, rec.Color,
            [.. v], [.. rec.Integers],
            e, nu, ro, b, h, t, ef, eiy, eiz, gik,
            rec.Profile, rec.Sortament, rec.FullName, rec.Parts, offset);
    }

    // ------------------------------------------------------------------ Примитивы

    static int ReadU16(byte[] b, int offset) =>
        offset >= 0 && offset + 2 <= b.Length ? BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(offset, 2)) : -1;

    sealed class Record
    {
        public readonly List<float> Values = [];
        public readonly List<long> Integers = [];
        public int LastShort;
        public string Name = "";
        public uint Color;
        public string? Profile, Sortament, FullName;
        public int Parts;
        public double[]? UserProps;
    }

    sealed class UnsupportedLayoutException(string message) : Exception(message);

    /// <summary>Чтение с границей; выход за границу — <see cref="FormatException"/>.</summary>
    ref struct Cursor(byte[] bytes, int position, int end)
    {
        readonly byte[] _b = bytes;
        readonly int _end = end;
        public int Position { get; private set; } = position;

        void Need(int n)
        {
            if (Position + n > _end) throw new FormatException();
        }

        public int U16() { Need(2); int v = BinaryPrimitives.ReadUInt16LittleEndian(_b.AsSpan(Position)); Position += 2; return v; }
        public uint U32() { Need(4); uint v = BinaryPrimitives.ReadUInt32LittleEndian(_b.AsSpan(Position)); Position += 4; return v; }
        public float F32() { Need(4); float v = BinaryPrimitives.ReadSingleLittleEndian(_b.AsSpan(Position)); Position += 4; return v; }
        public int U8() { Need(1); return _b[Position++]; }
        public void Skip(int n) { Need(n); Position += n; }

        /// <summary>MFC <c>CString</c>: длина 1/2/4 байта (<c>FF</c>, <c>FF FF</c>), <c>FF FE FF</c> — UTF-16.</summary>
        public string CString()
        {
            bool wide = false;
            long n = U8();
            if (n == 0xFF)
            {
                n = U16();
                if (n == 0xFFFE)
                {
                    wide = true;
                    n = U8();
                    if (n == 0xFF) n = U16();
                }
                if (n == 0xFFFF) n = U32();
            }
            if (n > MaxStringLength) throw new FormatException();
            int len = (int)(wide ? 2 * n : n);
            Need(len);
            string s = wide ? Encoding.Unicode.GetString(_b, Position, len) : Cp1251.GetString(_b, Position, len);
            Position += len;
            return s;
        }
    }
}
