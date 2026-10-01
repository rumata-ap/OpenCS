using System.Buffers.Binary;
using System.Text;
using OpenCS.Services.Scad;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Разбор структур SCADAPIX.dll из байтов (без DLL).</summary>
public class ScadConcreteLayoutTests
{
    static ScadConcreteLayoutTests() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    static void Ansi(byte[] b, int offset, string s) =>
        Encoding.GetEncoding(1251).GetBytes(s).CopyTo(b, offset);

    [Fact]
    public void ParseConcrete_SyntheticRecord()
    {
        var b = new byte[ScadApiLayouts.ConcreteSize];
        b[0] = 5;   // Modul
        b[2] = 1;   // CrackResisting
        double[] range = [3, 4, 3.5, 4.5]; // см
        for (int i = 0; i < 4; i++) BinaryPrimitives.WriteDoubleLittleEndian(b.AsSpan(8 + 8 * i), range[i]);
        Ansi(b, 124, "B25");
        Ansi(b, 196, "A500С");
        Ansi(b, 212, "A240");
        BinaryPrimitives.WriteDoubleLittleEndian(b.AsSpan(300), 0.4);
        BinaryPrimitives.WriteDoubleLittleEndian(b.AsSpan(308), 0.3);
        b[330] = 2;

        var c = ScadApiLayouts.ParseConcrete(b);

        Assert.Equal(5, c.Module);
        Assert.True(c.CrackResisting);
        Assert.Equal([0.03, 0.04, 0.035, 0.045], c.RangeM);
        Assert.Equal("B25", c.ConcreteClass);
        Assert.Equal("A500С", c.LongitudinalRebarClass);
        Assert.Equal("A240", c.TransverseRebarClass);
        Assert.Equal([0.4, 0.3], c.CrackWidthMm);
        Assert.Equal(2, c.SlaveGroup);
    }

    [Fact]
    public void ParseConcrete_ShortBuffer_Throws() =>
        Assert.Throws<ArgumentException>(() => ScadApiLayouts.ParseConcrete(new byte[100]));

    [Fact]
    public void Units_RoundTripAndToMeters()
    {
        var b = new byte[ScadApiLayouts.UnitsSize];
        ScadApiLayouts.WriteUnit(b, "см", 100);
        var u = ScadApiLayouts.ParseUnit(b);
        Assert.Equal("см", u.Name);
        Assert.Equal(0.01, u.ToMeters, 9);
        Assert.Equal(1.0, new ScadUnit("M", 1).ToMeters);
    }

    [Fact]
    public void ToAnsiZ_NullTerminatedOrNullOutsideCp1251()
    {
        var bytes = ScadApiLayouts.ToAnsiZ(@"C:\Модели\музей.SPR");
        Assert.NotNull(bytes);
        Assert.Equal(0, bytes![^1]);
        Assert.Equal(@"C:\Модели\музей.SPR", ScadApiLayouts.CString(bytes));
        Assert.Null(ScadApiLayouts.ToAnsiZ(@"C:\模型.SPR"));
    }

    [Fact]
    public void ScadApiException_FormatsResourceAndDetails()
    {
        var ex = new ScadApiException("Key", ["ApiReadProject", 3], "Файл занят");
        string text = ex.Format(_ => "Ошибка {0}: код {1}");
        Assert.Equal("Ошибка ApiReadProject: код 3" + Environment.NewLine + "Файл занят", text);
    }
}
