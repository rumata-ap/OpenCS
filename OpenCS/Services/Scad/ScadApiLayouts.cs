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
/// Разбор структур SCADAPIX.dll (pack 1) из байтов — без вызова DLL, тестируется отдельно.
/// Смещения ApiConcreteElem — по ScadStructHelpAPI.hxx (сверено на модели 01.10).
/// </summary>
internal static class ScadApiLayouts
{
    /// <summary>Размер UnitsAPI: char Name[10] + float coef.</summary>
    public const int UnitsSize = 14;

    /// <summary>Размер ApiConcreteElem.</summary>
    public const int ConcreteSize = 340;

    /// <summary>Смещения CNodeApi: LPCSTR Text, double x, y, z.</summary>
    public const int NodeX = 8, NodeY = 16, NodeZ = 24, NodeSize = 32;

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
}
