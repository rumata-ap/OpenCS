using System.Buffers.Binary;
using System.Text;

namespace OpenCS.Services.Scad;

/// <summary>Единица измерения SCAD (UnitsAPI): имя и коэффициент «единиц в метре» (в тонне — для сил).</summary>
internal readonly record struct ScadUnit(string Name, float Coef)
{
    /// <summary>Величина единицы в метрах: v_м = v / coef (см → 100 → 0,01).</summary>
    public double ToMeters => Coef > 0 ? 1.0 / Coef : 1.0;
}

/// <summary>Параметры ЖБ-группы из ApiConcreteElem (без номера, имени и списка КЭ).</summary>
internal sealed record ScadConcreteParams(int Module, bool CrackResisting, double[] RangeM,
    string ConcreteClass, string LongitudinalRebarClass, string TransverseRebarClass,
    double[] CrackWidthMm, int SlaveGroup);

/// <summary>
/// Заголовок ApiElemEffors (указатели — на память DLL, действительны до следующего вызова ApiGetEffors).
/// </summary>
internal readonly record struct ScadEfforsHeader(int ElemId, int QuantityUs, nint TypeUs, int Points, int Layers,
    int Loads, int Combinations, long DataUs, nint Us, long DataUsComb, nint UsComb);

/// <summary>Заголовок ApiElemRsu: число усилий в строке, число строк и указатель на строки (память DLL).</summary>
internal readonly record struct ScadRsuHeader(int ElemId, int QuantityUs, int Rows, nint Str);

/// <summary>Строка ApiElemRsuStr без коэффициентов: точка (с 1), группа РСУ, критерий, указатель на усилия (float).</summary>
internal readonly record struct ScadRsuRowHeader(int Point, int Group, int Criterion, nint Us);

/// <summary>
/// Разбор структур SCADAPIX.dll (pack 1) из байтов — без вызова DLL, тестируется отдельно.
/// Смещения ApiConcreteElem — по ScadStructHelpAPI.hxx (сверено на модели 01.10); ApiElemEffors,
/// ApiElemRsu, ApiLoadingData — пробником p9–p11 (01.10).
/// </summary>
internal static class ScadApiLayouts
{
    /// <summary>Размер UnitsAPI: char Name[10] + float coef.</summary>
    public const int UnitsSize = 14;

    /// <summary>Размер ApiConcreteElem.</summary>
    public const int ConcreteSize = 340;

    /// <summary>Смещения CNodeApi: LPCSTR Text, double x, y, z.</summary>
    public const int NodeX = 8, NodeY = 16, NodeZ = 24, NodeSize = 32;

    /// <summary>Размер ApiElemEffors.</summary>
    public const int EfforsSize = 69;

    /// <summary>Размер ApiElemRsu.</summary>
    public const int RsuSize = 26;

    /// <summary>Размер строки ApiElemRsuStr.</summary>
    public const int RsuRowSize = 48;

    /// <summary>Размер ApiLoadingData и смещение указателя на имя (LPCSTR Name).</summary>
    public const int LoadingDataSize = 58, LoadingDataName = 42;

    /// <summary>API_RESULT_LOAD_COMB — результаты от комбинаций загружений.</summary>
    public const uint ResultLoadComb = 12;

    const int ConcreteModule = 0, ConcreteCrack = 2, ConcreteRange = 8, ConcreteClassBeton = 124,
        ConcreteClassArm = 196, ConcreteWidthCrack = 300, ConcreteSlave = 330;

    /// <summary>Единица длины SCAD Range ЖБ-группы — см (при единицах проекта «м»).</summary>
    const double RangeUnitM = 0.01;

    static ScadApiLayouts() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    /// <summary>Кодировка строк SCAD (LPCSTR).</summary>
    public static Encoding Ansi => Encoding.GetEncoding(1251);

    /// <summary>UnitsAPI: имя и коэффициент.</summary>
    public static ScadUnit ParseUnit(ReadOnlySpan<byte> b) =>
        new(CString(b[..10]), BinaryPrimitives.ReadSingleLittleEndian(b.Slice(10, 4)));

    /// <summary>Записать UnitsAPI (для ApiInitResult).</summary>
    public static void WriteUnit(Span<byte> b, string name, float coef)
    {
        b[..UnitsSize].Clear();
        var bytes = Ansi.GetBytes(name);
        bytes.AsSpan(0, Math.Min(bytes.Length, 9)).CopyTo(b);
        BinaryPrimitives.WriteSingleLittleEndian(b.Slice(10, 4), coef);
    }

    /// <summary>ApiConcreteElem (340 байт).</summary>
    public static ScadConcreteParams ParseConcrete(ReadOnlySpan<byte> b)
    {
        if (b.Length < ConcreteSize)
            throw new ArgumentException($"ApiConcreteElem: {b.Length} байт вместо {ConcreteSize}", nameof(b));
        var range = new double[4];
        for (int i = 0; i < 4; i++)
            range[i] = Math.Round(Double(b, ConcreteRange + 8 * i) * RangeUnitM, 6);
        return new ScadConcreteParams(
            Module: b[ConcreteModule],
            CrackResisting: b[ConcreteCrack] != 0,
            RangeM: range,
            ConcreteClass: CString(b.Slice(ConcreteClassBeton, 16)),
            LongitudinalRebarClass: CString(b.Slice(ConcreteClassArm, 16)),
            TransverseRebarClass: CString(b.Slice(ConcreteClassArm + 16, 16)),
            CrackWidthMm: [Double(b, ConcreteWidthCrack), Double(b, ConcreteWidthCrack + 8)],
            SlaveGroup: b[ConcreteSlave]);
    }

    /// <summary>ApiElemEffors (69 байт).</summary>
    public static ScadEfforsHeader ParseEffors(ReadOnlySpan<byte> b)
    {
        if (b.Length < EfforsSize)
            throw new ArgumentException($"ApiElemEffors: {b.Length} байт вместо {EfforsSize}", nameof(b));
        return new ScadEfforsHeader(
            ElemId: (int)UInt32(b, 0),
            QuantityUs: b[4],
            TypeUs: Ptr(b, 5),
            Points: b[13],
            Layers: UInt16(b, 14),
            Loads: UInt16(b, 16),
            Combinations: UInt16(b, 18),
            DataUs: (long)UInt64(b, 29),
            Us: Ptr(b, 37),
            DataUsComb: (long)UInt64(b, 53),
            UsComb: Ptr(b, 61));
    }

    /// <summary>ApiElemRsu (26 байт); число строк — Quantity (LengthData у РСУ = 0).</summary>
    public static ScadRsuHeader ParseRsu(ReadOnlySpan<byte> b)
    {
        if (b.Length < RsuSize)
            throw new ArgumentException($"ApiElemRsu: {b.Length} байт вместо {RsuSize}", nameof(b));
        return new ScadRsuHeader((int)UInt32(b, 0), b[9], (int)UInt32(b, 14), Ptr(b, 18));
    }

    /// <summary>ApiElemRsuStr (48 байт).</summary>
    public static ScadRsuRowHeader ParseRsuRow(ReadOnlySpan<byte> b)
    {
        if (b.Length < RsuRowSize)
            throw new ArgumentException($"ApiElemRsuStr: {b.Length} байт вместо {RsuRowSize}", nameof(b));
        return new ScadRsuRowHeader(b[4], b[7], UInt16(b, 8), Ptr(b, 20));
    }

    /// <summary>
    /// Первый слой из массива усилий [точка][строка][слой][усилие] (строка — загружение или комбинация):
    /// результат [точка][строка][усилие].
    /// </summary>
    public static double[] FirstLayer(ReadOnlySpan<double> src, int points, int rows, int layers, int quantityUs)
    {
        layers = Math.Max(layers, 1);
        var dst = new double[points * rows * quantityUs];
        for (int p = 0; p < points; p++)
            for (int r = 0; r < rows; r++)
                src.Slice((p * rows + r) * layers * quantityUs, quantityUs)
                    .CopyTo(dst.AsSpan((p * rows + r) * quantityUs, quantityUs));
        return dst;
    }

    /// <summary>Строка cp1251 до первого нуля.</summary>
    public static string CString(ReadOnlySpan<byte> b)
    {
        int end = b.IndexOf((byte)0);
        return Ansi.GetString(end >= 0 ? b[..end] : b).Trim();
    }

    /// <summary>
    /// Путь или имя для LPCSTR: cp1251 + завершающий ноль; null — в строке есть символы вне cp1251.
    /// </summary>
    public static byte[]? ToAnsiZ(string text)
    {
        var strict = Encoding.GetEncoding(1251, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        try
        {
            var bytes = strict.GetBytes(text);
            Array.Resize(ref bytes, bytes.Length + 1);
            return bytes;
        }
        catch (EncoderFallbackException) { return null; }
    }

    static double Double(ReadOnlySpan<byte> b, int offset) =>
        BinaryPrimitives.ReadDoubleLittleEndian(b.Slice(offset, 8));

    static ushort UInt16(ReadOnlySpan<byte> b, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(b.Slice(offset, 2));

    static uint UInt32(ReadOnlySpan<byte> b, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(b.Slice(offset, 4));

    static ulong UInt64(ReadOnlySpan<byte> b, int offset) => BinaryPrimitives.ReadUInt64LittleEndian(b.Slice(offset, 8));

    static nint Ptr(ReadOnlySpan<byte> b, int offset) => (nint)BinaryPrimitives.ReadInt64LittleEndian(b.Slice(offset, 8));
}
