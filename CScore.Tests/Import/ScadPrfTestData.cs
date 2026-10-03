using System.Text;

namespace CScore.Tests.Import;

/// <summary>Сборщик синтетического сортамента SCAD (формат PRF версии 3) для тестов без файлов SCAD.</summary>
sealed class ScadPrfTestData
{
    /// <summary>Таблица: вид, код, название, колонки (имя, единица), строки (имя, значения в единицах колонок).</summary>
    public sealed record Table(int Kind, string Code, string Title,
        (string Name, string Unit)[] Columns, (string Name, float[] Values)[] Rows);

    static readonly (string Unit, float Scale)[] Units =
        [("mm", 1000), ("cm2", 1e4f), ("cm4", 1e8f), ("cm3", 1e6f), ("cm", 100), ("kG/m", 1000), ("gradus", 1)];

    static Encoding Cp1251
    {
        get
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(1251);
        }
    }

    /// <summary>Байты файла с заданными таблицами.</summary>
    public static byte[] Build(string title, params Table[] tables)
    {
        var enc = Cp1251;
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        void PStr(string s) { var b = enc.GetBytes(s); w.Write((byte)b.Length); w.Write(b); }
        void Fixed(string s, int n) { var b = new byte[n]; enc.GetBytes(s).CopyTo(b, 0); w.Write(b); }

        w.Write("**PRFL**"u8.ToArray());
        w.Write((byte)3);
        w.Write((byte)Units.Length);
        w.Write((ushort)tables.Length);
        w.Write(new byte[4]);
        w.Write(2.1e7f); w.Write(0.3f); w.Write(7.85f);
        PStr(title); PStr(title); PStr(title);
        foreach (var (unit, scale) in Units) { Fixed(unit, 10); w.Write(scale); }

        foreach (var t in tables)
        {
            int nc = t.Columns.Length + 1;
            var names = t.Columns.Select(c => c.Name).Append("").ToArray();
            int namesLength = names.Sum(n => enc.GetByteCount(n) + 1);
            w.Write((byte)t.Kind);
            Fixed(t.Code, 9);
            w.Write((byte)nc);
            w.Write((byte)0);
            w.Write((ushort)t.Rows.Length);
            w.Write((ushort)namesLength);
            PStr(t.Title); PStr(t.Title); PStr(t.Title);
            w.Write((byte)0);
            foreach (var c in t.Columns)
                w.Write((byte)(Array.FindIndex(Units, u => u.Unit == c.Unit) + 1));
            foreach (var n in names) { w.Write(enc.GetBytes(n)); w.Write((byte)0); }
            w.Write(Enumerable.Repeat((byte)1, nc).ToArray());
            foreach (var (name, values) in t.Rows)
            {
                PStr(name);
                foreach (var v in values) w.Write(v);
            }
        }
        w.Flush();
        return ms.ToArray();
    }

    /// <summary>Двутавр 20Б1, 25Б1 «СТО АСЧМ 20-93» (как ASCHM d1, строки 10 и 11 — здесь 1 и 2).</summary>
    public static Table IBeams() => new(1, "d1", "Двутавр нормальный (Б) по СТО АСЧМ 20-93",
        [("h", "mm"), ("b", "mm"), ("s", "mm"), ("t", "mm"), ("r1", "mm"), ("A", "cm2"), ("Iy", "cm4"), ("Iz", "cm4")],
        [("20Б1", [200, 100, 5.5f, 8, 11, 27.16f, 1844, 133.9f]),
         ("25Б1", [248, 124, 5, 8, 12, 32.68f, 3537, 254.8f])]);

    /// <summary>Квадратная труба 100×4 ГОСТ 30245-2012 (R — наружный радиус).</summary>
    public static Table SquareTubes() => new(19, "okv2012",
        "Стальные гнутые замкнутые сварные квадратные профили по ГОСТ 30245-2012",
        [("b", "mm"), ("s", "mm"), ("R", "mm"), ("A", "cm2"), ("Iy=Iz", "cm4")],
        [("100x4", [100, 4, 8, 14.95f, 225.1f])]);

    /// <summary>Швеллер 10У ГОСТ 8240-97 с уклоном (Gamma — доля).</summary>
    public static Table SlopedChannels() => new(3, "pu_ukl97", "Швеллер с уклоном полок по ГОСТ 8240-97",
        [("h", "mm"), ("b", "mm"), ("s", "mm"), ("t", "mm"), ("r1", "mm"), ("r2", "mm"), ("A", "cm2"), ("Iy", "cm4"), ("Gamma", "gradus")],
        [("10У", [100, 46, 4.5f, 7.6f, 7, 3, 10.9f, 174, 0.1f])]);

    /// <summary>Уголок равнополочный и его зеркальная таблица (только имена).</summary>
    public static Table[] Angles() =>
    [
        new(4, "ce_equal", "Уголок равнополочный по ГОСТ 8509-93",
            [("b", "mm"), ("t", "mm"), ("r1", "mm"), ("r2", "mm"), ("A", "cm2"), ("Iy=Iz", "cm4")],
            [("L50x5", [50, 5, 5.5f, 1.8f, 4.8f, 11.2f])]),
        new(18, "cn_equal", "Уголок равнополочный по ГОСТ 8509-93, зеркальный", [], [("LN50x5", [])]),
    ];

    /// <summary>Труба электросварная ГОСТ 10704-91.</summary>
    public static Table Pipes() => new(16, "diam", "Тpубы электросварные прямошовные по ГОСТ 10704-91",
        [("D", "mm"), ("s", "mm"), ("A", "cm2")], [("114x4", [114, 4, 13.82f])]);

    /// <summary>Гнутый швеллер ГОСТ 8278-83 (колонки n1/n2 — признак гнутого профиля).</summary>
    public static Table BentChannels() => new(3, "cg", "Гнутый равнополочный швеллер по ГОСТ 8278-83 из сталей С239-С245",
        [("h", "mm"), ("b", "mm"), ("s", "mm"), ("r1", "mm"), ("n1", "mm"), ("n2", "mm"), ("A", "cm2")],
        [("100x50x4", [100, 50, 4, 6, 1, 1, 7.31f])]);
}
