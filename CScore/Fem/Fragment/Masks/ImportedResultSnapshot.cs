using System.IO.Compression;
using System.Security.Cryptography;

namespace CScore.Fem.Fragment.Masks;

/// <summary>
/// Снимок результатов программы-источника импортной схемы (SCAD, ЛИРА) для снятия масок фрагмента (спека 2026-10-28,
/// разд. 4.1; план среза 7): по загружениям — перемещения узлов, усилия первого и последнего сечения стержней и усилия
/// пластин в центре. Значения — <b>в соглашениях программы</b> (оси и знаки), только единицы переведены в СИ;
/// в конвенцию OpenCS переводит <see cref="ImportedParentResult"/>. Номера узлов и КЭ — номера программы (= теги сетки
/// импортной схемы). Узла или КЭ нет в снимке — данных нет, а не ноль.
/// </summary>
public sealed class ImportedResultSnapshot
{
    /// <summary>Программы-источники (<see cref="Program"/>), совпадают с видами источника масок.</summary>
    public const string ProgramScad = FragmentMaskSet.SourceScad, ProgramLira = FragmentMaskSet.SourceLira;

    public required string Program { get; init; }
    /// <summary>Имя файла или документа программы, из которого снят снимок.</summary>
    public string SourceName { get; init; } = "";
    /// <summary>Когда снят (ISO 8601).</summary>
    public string TakenAt { get; init; } = "";
    public List<ImportedResultLoad> Loads { get; init; } = [];
}

/// <summary>
/// Результаты одного загружения программы. Плоские массивы: на узел <see cref="NodeComponents"/> значений
/// (X Y Z, м; UX UY UZ, рад; общие оси), на стержень <see cref="BarComponents"/> (N Qy Qz Mx My Mz первого сечения, затем
/// последнего; Н и Н·м; местные оси и знаки программы; первое сечение — у узла 1, последнее — у узла 2), на пластину
/// <see cref="ShellComponents"/> (Nx Ny Txy, Н/м — уже умножены на толщину; Mx My Mxy, Н·м/м; Qx Qy, Н/м; оси выдачи и
/// знаки программы).
/// </summary>
public sealed class ImportedResultLoad
{
    public const int NodeComponents = 6, BarComponents = 12, ShellComponents = 8;

    /// <summary>Номер загружения в программе.</summary>
    public int Num { get; init; }
    public string Name { get; init; } = "";
    /// <summary>Номер шага нелинейного загружения SCAD (последний сохранённый); 0 — линейное.</summary>
    public int Step { get; init; }

    public int[] NodeNums { get; init; } = [];
    public double[] Displacements { get; init; } = [];
    public int[] BarNums { get; init; } = [];
    public double[] BarForces { get; init; } = [];
    public int[] ShellNums { get; init; } = [];
    public double[] ShellForces { get; init; } = [];
}

/// <summary>
/// Двоичный формат снимка (вложение схемы <c>scad_mask_results</c>/<c>lira_mask_results</c>): gzip поверх little-endian
/// блока — байт версии, строки программы, имени и даты, число загружений; на загружение — номер, имя, шаг и три раздела
/// «число, номера, значения».
/// </summary>
public static class ImportedResultSnapshotCodec
{
    const byte Version = 1;

    public static byte[] Encode(ImportedResultSnapshot snapshot)
    {
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true))
        using (var w = new BinaryWriter(gz))
        {
            w.Write(Version);
            w.Write(snapshot.Program);
            w.Write(snapshot.SourceName);
            w.Write(snapshot.TakenAt);
            w.Write(snapshot.Loads.Count);
            foreach (var l in snapshot.Loads)
            {
                w.Write(l.Num);
                w.Write(l.Name);
                w.Write(l.Step);
                WriteSection(w, l.NodeNums, l.Displacements, ImportedResultLoad.NodeComponents, "узлов");
                WriteSection(w, l.BarNums, l.BarForces, ImportedResultLoad.BarComponents, "стержней");
                WriteSection(w, l.ShellNums, l.ShellForces, ImportedResultLoad.ShellComponents, "пластин");
            }
        }
        return ms.ToArray();
    }

    /// <summary>Разбор; неизвестная версия, обрыв или не gzip — <see cref="InvalidDataException"/>.</summary>
    public static ImportedResultSnapshot Decode(byte[] data)
    {
        try
        {
            using var gz = new GZipStream(new MemoryStream(data), CompressionMode.Decompress);
            using var r = new BinaryReader(gz);
            byte version = r.ReadByte();
            if (version != Version) throw new InvalidDataException($"Неизвестная версия снимка результатов: {version}.");
            string program = r.ReadString(), source = r.ReadString(), takenAt = r.ReadString();
            int count = r.ReadInt32();
            var loads = new List<ImportedResultLoad>(count);
            for (int i = 0; i < count; i++)
            {
                int num = r.ReadInt32();
                string name = r.ReadString();
                int step = r.ReadInt32();
                var (nodes, disp) = ReadSection(r, ImportedResultLoad.NodeComponents);
                var (bars, barF) = ReadSection(r, ImportedResultLoad.BarComponents);
                var (shells, shellF) = ReadSection(r, ImportedResultLoad.ShellComponents);
                loads.Add(new ImportedResultLoad
                {
                    Num = num, Name = name, Step = step, NodeNums = nodes, Displacements = disp,
                    BarNums = bars, BarForces = barF, ShellNums = shells, ShellForces = shellF,
                });
            }
            return new ImportedResultSnapshot { Program = program, SourceName = source, TakenAt = takenAt, Loads = loads };
        }
        catch (EndOfStreamException ex)
        {
            throw new InvalidDataException("Снимок результатов оборван.", ex);
        }
    }

    /// <summary>Отпечаток вложения для сверки устаревания масок: SHA-256 байтов, hex.</summary>
    public static string Fingerprint(byte[] data) => Convert.ToHexString(SHA256.HashData(data));

    static void WriteSection(BinaryWriter w, int[] nums, double[] values, int components, string what)
    {
        if (values.Length != nums.Length * components)
            throw new ArgumentException($"Снимок: у {what} {values.Length} значений на {nums.Length} номеров (нужно по {components}).");
        w.Write(nums.Length);
        foreach (int n in nums) w.Write(n);
        foreach (double v in values) w.Write(v);
    }

    static (int[] Nums, double[] Values) ReadSection(BinaryReader r, int components)
    {
        int n = r.ReadInt32();
        if (n < 0) throw new InvalidDataException("Снимок результатов: отрицательное число записей.");
        var nums = new int[n];
        for (int i = 0; i < n; i++) nums[i] = r.ReadInt32();
        var values = new double[n * components];
        for (int i = 0; i < values.Length; i++) values[i] = r.ReadDouble();
        return (nums, values);
    }
}
