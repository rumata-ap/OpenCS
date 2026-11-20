using System.Buffers.Binary;
using System.Text;
using CScore.Import;
using Xunit;

namespace CScore.Tests.Import;

/// <summary>
/// Проверки чтения таблицы определений жёсткостей бинарного .lir: синтетическая таблица с известными
/// раскладками и сверка с документом 3 текстового экспорта ЛИРЫ на образцах.
/// </summary>
public class LiraStiffnessDefinitionsReaderTests
{
    static LiraStiffnessDefinitionsReaderTests()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    // ------------------------------------------------------------------ Синтетика

    /// <summary>Писатель записей таблицы в формате версии схемы 13000.</summary>
    sealed class TableWriter
    {
        readonly List<byte> _b = [];

        public void U16(int v) { _b.Add((byte)v); _b.Add((byte)(v >> 8)); }
        public void U32(uint v) { var t = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(t, v); _b.AddRange(t); }
        public void F32(float v) { var t = new byte[4]; BinaryPrimitives.WriteSingleLittleEndian(t, v); _b.AddRange(t); }
        public void Bytes(int n, byte value = 0) { for (int i = 0; i < n; i++) _b.Add(value); }

        /// <summary>CString: пустая — «широкая» пустая <c>FF FE FF 00</c>, иначе cp1251 с длиной.</summary>
        public void Str(string s, bool wide = false)
        {
            if (wide)
            {
                _b.AddRange([0xFF, 0xFE, 0xFF, (byte)s.Length]);
                _b.AddRange(Encoding.Unicode.GetBytes(s));
                return;
            }
            var t = Encoding.GetEncoding(1251).GetBytes(s);
            _b.Add((byte)t.Length);
            _b.AddRange(t);
        }

        void Head(int code, int number, uint flag) { U16(code); U16(number); U32(flag); }

        /// <summary>Брус S0 (код 0): ρ, E, b, h + нули, имя, хвост версии 13000.</summary>
        public void Bar(int number, uint flag, float ro, float e, float b, float h, string name)
        {
            Head(0, number, flag);
            F32(ro); F32(e); F32(b); F32(h); F32(0); F32(0);
            U16(0xFFFF); U16(0);                 // [≥8200] i16, u16
            F32(0); Str(name, wide: true); U32(0x00ABCDEF);
            U32(0); F32(0); F32(0); F32(0);      // [>12003]
            U16(0);                              // [>5020] без доп. блока
        }

        /// <summary>Пластина GEI (код 36).</summary>
        public void Plate(int number, uint flag, float ro, float e, float nu, float h)
        {
            Head(36, number, flag);
            F32(ro); F32(e); F32(nu); F32(h); F32(0); F32(0);
            U16(0xFFFF);
            Str("", wide: true); U32(0x112233);
            U16(0);                              // [>5020]
            U16(0); F32(0); F32(0); F32(0);      // [>9420] GEO
            U32(0); F32(0);                      // [>12004]
        }

        /// <summary>Связь КЭ 51 (код 17).</summary>
        public void Spring(int number, uint flag, int direction, float k)
        {
            Head(17, number, flag);
            U16(direction); F32(k);
            Str("", wide: true); U32(0);
        }

        /// <summary>Профиль сортамента (код 42) с составной частью.</summary>
        public void Profile(int number, uint flag, float ef, float eiy, float eiz, float gik, string profile, string srt)
        {
            U16(42); U16(9100); U16(14); U16(number); U32(flag);
            F32(0.01f); F32(ef); F32(eiy); F32(eiz); F32(gik);
            U16(0); F32(0); F32(0); U16(0); F32(0); F32(0); F32(0);
            U16(0xFFFF); U16(0);
            for (int k = 0; k < 7; k++) F32(0.005f);
            Str(""); U32(0);
            F32(0); F32(0);                      // [≥9408], [≥9415]
            ProfileBody(profile, srt, parts: 1);
            ProfileBody("part", srt, parts: 0);
        }

        void ProfileBody(string profile, string srt, int parts)
        {
            Str(profile);
            for (int k = 0; k < 6; k++) F32(1);
            Str("");
            U16(7);
            Bytes(46);
            U32(1);
            Str(srt);
            Str("1. " + profile);
            Str("1. " + profile + "\0AutoName\0", wide: true);
            U32(1251);
            U16(parts);
        }

        public void Unknown(int code, int number) { Head(code, number, 1); F32(1); }

        public byte[] ToArray() => [.. _b];
        public int Length => _b.Count;
    }

    /// <summary>Файл: мусор, <c>[n+1][n]</c> + таблица, вторая таблица, 2 байта, «массив номеров».</summary>
    static (byte[] Bytes, int ArrayStart) Wrap(Action<TableWriter, uint> table, int count, bool secondEmpty = false)
    {
        var w = new TableWriter();
        w.Bytes(37, 0x5A);
        w.U16(count + 1);
        w.U16(count);
        table(w, 0);
        if (secondEmpty) w.U16(0);
        else { w.U16(count); table(w, 1); }
        w.U16(4);
        int arrayStart = w.Length;
        for (int i = 0; i < 10; i++) w.U16(i % count);
        return (w.ToArray(), arrayStart);
    }

    [Fact]
    public void Synthetic_KnownLayouts_ReadExactly()
    {
        var (bytes, arrayStart) = Wrap((w, flag) =>
        {
            w.Bar(1, flag, 2.75f, 1.8e6f, 30, 40, "Колонна");
            w.Plate(2, flag, 2.5f, 3.06e6f, 0.2f, 25);
            w.Spring(3, flag, 3, 1000);
            w.Profile(4, flag, 10083f, 2.35f, 2.36f, 0.034f, "50 x 50 x 5", "ug.profiles.srt");
        }, 4);

        var notes = new List<string>();
        var defs = LiraStiffnessDefinitionsReader.Read(bytes, arrayStart, 0, notes);

        Assert.Equal(4, defs.Count);
        Assert.Equal([1, 2, 3, 4], defs.Select(d => d.Number));

        var bar = defs[0];
        Assert.Equal(LiraStiffnessKind.StandardSection, bar.Kind);
        Assert.Equal("Колонна", bar.Name);
        Assert.Equal(0x00ABCDEFu, bar.Color);
        Assert.Equal(1.8e6, bar.E!.Value, 1e-3);
        Assert.Equal(2.75, bar.Density!.Value, 1e-6);
        Assert.Equal(30, bar.Width!.Value, 1e-6);
        Assert.Equal(40, bar.Height!.Value, 1e-6);

        var plate = defs[1];
        Assert.Equal(LiraStiffnessKind.Plate, plate.Kind);
        Assert.Equal(0.2, plate.Poisson!.Value, 1e-6);
        Assert.Equal(25, plate.Thickness!.Value, 1e-6);

        Assert.Equal(LiraStiffnessKind.Spring51, defs[2].Kind);
        Assert.Equal(1000f, defs[2].Values[0]);
        Assert.Equal(3, defs[2].Integers[0]);

        var profile = defs[3];
        Assert.Equal(LiraStiffnessKind.SteelProfile, profile.Kind);
        Assert.Equal(10083, profile.EF!.Value, 1e-2);
        Assert.Equal("50 x 50 x 5", profile.Profile);
        Assert.Equal("ug.profiles.srt", profile.SortamentFile);
        Assert.Equal("1. 50 x 50 x 5", profile.ProfileFullName);
    }

    [Fact]
    public void Synthetic_EmptySecondTable_Accepted()
    {
        var (bytes, arrayStart) = Wrap((w, flag) => w.Spring(1, flag, 2, 5), 1, secondEmpty: true);
        var defs = LiraStiffnessDefinitionsReader.Read(bytes, arrayStart, 0, []);
        Assert.Single(defs);
    }

    [Fact]
    public void Synthetic_UnknownCode_NotPublished()
    {
        var (bytes, arrayStart) = Wrap((w, flag) =>
        {
            w.Spring(1, flag, 2, 5);
            w.Unknown(35, 2);
        }, 2);

        var notes = new List<string>();
        var defs = LiraStiffnessDefinitionsReader.Read(bytes, arrayStart, 0, notes);

        Assert.Empty(defs);
        Assert.Contains(notes, n => n.Contains("не прочитаны") && n.Contains("код 35"));
    }

    [Fact]
    public void Synthetic_ExtraSectionBlock_NotPublished()
    {
        // Флаг дополнительного блока стандартного сечения ≠ 0 — блок не разобран, таблица не публикуется.
        var (bytes, arrayStart) = Wrap((w, flag) =>
        {
            w.Bar(1, flag, 2.5f, 3e6f, 30, 30, "");
        }, 1);
        int flagAt = arrayStart - 2 - 2;         // последнее u16 записи второй таблицы (перед «04 00»)
        bytes[flagAt] = 1;

        var defs = LiraStiffnessDefinitionsReader.Read(bytes, arrayStart, 0, []);
        Assert.Empty(defs);
    }

    // ------------------------------------------------------------------ Образцы

    /// <summary>
    /// Сверка с документом 3 TXT на парах «.lir + .txt» каталога <c>OPENCES_LIR_SAMPLES_DIR</c>
    /// (без переменной тест пропускается): число определений равно числу записей документа 3 за вычетом
    /// жёсткостей, которые ЛИРА заводит одноузловым КЭ 57 (они идут после), а хранимые величины совпадают
    /// с первой строкой записи TXT.
    /// </summary>
    [Fact]
    public void Extra_SamplePairs_MatchDocument3()
    {
        string? dir = Environment.GetEnvironmentVariable("OPENCES_LIR_SAMPLES_DIR");
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;

        var failures = new List<string>();
        foreach (string lir in Directory.GetFiles(dir, "*.lir"))
        {
            string txt = Path.ChangeExtension(lir, ".txt");
            if (!File.Exists(txt)) continue;
            string name = Path.GetFileName(lir);

            var text   = LiraTextReader.Read(txt).Stiffnesses.ToDictionary(s => s.Number);
            var binary = LiraFileParser.Parse(lir, LiraBinaryExtrasMode.StiffnessAndLoads);
            var defs   = binary.StiffnessDefinitions;

            if (defs.Count == 0)
            {
                failures.Add($"{name}: определения не прочитаны ({string.Join("; ", binary.BinaryNotes.Where(n => n.Contains("определения")))})");
                continue;
            }

            for (int i = 0; i < defs.Count; i++)
            {
                var d = defs[i];
                if (d.Number != i + 1) failures.Add($"{name}: запись {i + 1} имеет номер {d.Number}");
                if (!text.TryGetValue(d.Number, out var t))
                {
                    failures.Add($"{name}: жёсткости {d.Number} нет в документе 3");
                    continue;
                }

                string first = t.Lines.Count > 0 ? t.Lines[0] : "";
                double[]? expected = d.Kind switch
                {
                    LiraStiffnessKind.StandardSection when d.TypeCode == 0 && first.StartsWith("S0") =>
                        [d.E!.Value, d.Width!.Value, d.Height!.Value],
                    LiraStiffnessKind.Plate when first.StartsWith("GEI") =>
                        [d.E!.Value, d.Poisson!.Value, d.Thickness!.Value / 100],
                    LiraStiffnessKind.NumericBar => [d.EF!.Value, d.EIy!.Value, d.EIz!.Value, d.GIk!.Value],
                    LiraStiffnessKind.SteelProfile => [d.EF!.Value, d.EIy!.Value, d.EIz!.Value],
                    LiraStiffnessKind.Soil => [d.E!.Value, d.Poisson!.Value, d.Thickness!.Value / 100],
                    LiraStiffnessKind.Spring51 => [d.Integers[0], d.Values[0]],
                    _ => null
                };
                if (expected is null) continue;

                var numbers = LiraTextTokens.TryNumbers(StripKeyword(first));
                if (numbers is null || numbers.Length < expected.Length)
                {
                    failures.Add($"{name}: жёсткость {d.Number} ({d.Kind}): строка TXT «{first}» не разобрана");
                    continue;
                }
                for (int k = 0; k < expected.Length; k++)
                    if (Math.Abs(numbers[k] - expected[k]) > 1e-4 * Math.Max(1e-6, Math.Abs(numbers[k])))
                    {
                        failures.Add($"{name}: жёсткость {d.Number} ({d.Kind}) поле {k + 1}: TXT {numbers[k]}, .lir {expected[k]}");
                        break;
                    }
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures.Take(40)));
    }

    /// <summary>Убирает ключевое слово сечения (<c>S0</c>, <c>GEI</c>) и всё после <c>RO</c>.</summary>
    static string StripKeyword(string line)
    {
        string s = line.Trim();
        foreach (string key in new[] { "S0 ", "GEI " })
            if (s.StartsWith(key)) s = s[key.Length..];
        int ro = s.IndexOf(" RO", StringComparison.Ordinal);
        if (ro >= 0) s = s[..ro];
        return s;
    }
}
