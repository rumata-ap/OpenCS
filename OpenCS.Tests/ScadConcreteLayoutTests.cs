using System.Buffers.Binary;
using System.Text;
using CScore.Import;
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

    static void U32(byte[] b, int offset, uint v) => BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(offset), v);
    static void F64(byte[] b, int offset, double v) => BinaryPrimitives.WriteDoubleLittleEndian(b.AsSpan(offset), v);

    [Fact]
    public void ParseArmPlate_SyntheticRecord_FlagsZeroFaces()
    {
        var b = new byte[ScadApiLayouts.ArmElemPlateSize];
        for (int i = 0; i < 4; i++) { U32(b, 12 * i, (uint)(10 + 2 * i)); F64(b, 12 * i + 4, 0.1 + 0.05 * i); }
        U32(b, 48, 8);      // dW
        F64(b, 52, 0.4);    // StepWx
        F64(b, 60, 0.3);    // StepWy
        b[68] = 1;          // NoUp — флаги по байту, не UINT

        var p = ScadApiLayouts.ParseArmPlate(b, 3, "плиты", [7, 8]);

        Assert.Equal([10, 0, 14, 0], p.DiametersMm);
        Assert.Equal(0.15, p.StepsM[1], 9);
        Assert.Equal(8, p.TransverseDiameterMm);
        Assert.Equal(0.3, p.TransverseStepYM);
        Assert.Equal(3, p.Num);
        Assert.Equal([7, 8], p.ElementIds);
    }

    /// <summary>Байты флагов из модели 111.SPR: отмечены все три «Отсутствует» — у группы нет арматуры.</summary>
    [Fact]
    public void ParseArmPlate_AllAbsentFlags_ZeroEverything()
    {
        var b = new byte[ScadApiLayouts.ArmElemPlateSize];
        for (int i = 0; i < 4; i++) { U32(b, 12 * i, 10); F64(b, 12 * i + 4, 0.2); }
        U32(b, 48, 10);
        F64(b, 52, 0.3);
        F64(b, 60, 0.3);
        b[68] = b[69] = b[70] = 1;

        var p = ScadApiLayouts.ParseArmPlate(b, 1, "", [1]);

        Assert.Equal([0, 0, 0, 0], p.DiametersMm);
        Assert.Equal(0, p.TransverseDiameterMm);
        Assert.Equal(0, p.TransverseArea);
    }

    [Fact]
    public void ParseArmRodPart_SyntheticRecord()
    {
        var b = new byte[ScadApiLayouts.ArmElemRodSize];
        U32(b, 0, 2);  F64(b, 4, 40);                     // участок 2, 40 %
        U32(b, 12, 1); U32(b, 20, 1); U32(b, 24, 1);      // IsS1D2, IsSw, IsS34 (IsS2D2 = 0)
        U32(b, 28, 20); U32(b, 32, 3);                    // S1 3⌀20
        U32(b, 36, 16); U32(b, 40, 2);                    // S2 2⌀16
        U32(b, 44, 12); U32(b, 48, 1);                    // S1 второй диаметр 1⌀12
        U32(b, 52, 25); U32(b, 56, 9);                    // S2 второй диаметр — выключен флагом
        U32(b, 60, 14); U32(b, 64, 2);                    // S3 2⌀14
        U32(b, 68, 10); U32(b, 72, 1);                    // S4 1⌀10
        U32(b, 76, 8); U32(b, 80, 2); F64(b, 84, 0.15);   // хомуты Z
        U32(b, 92, 6); U32(b, 96, 4); F64(b, 100, 0.2);   // хомуты Y
        b[108] = 1;                                       // IsS1L2
        F64(b, 110, 0.05);                                // DeltaS1
        U32(b, 126, 18); U32(b, 130, 2);                  // второй ряд S1 2⌀18

        var p = ScadApiLayouts.ParseArmRodPart(b);

        Assert.Equal(2, p.PartNo);
        Assert.Equal(40, p.LengthPercent);
        Assert.Equal(new ScadBarSet(3, 20), p.S1.First);
        Assert.Equal(new ScadBarSet(1, 12), p.S1.Second);
        Assert.Equal(new ScadBarSet(2, 18), p.S1.Row2);
        Assert.Equal(0.05, p.S1.Row2DeltaM);
        Assert.Null(p.S2.Second);
        Assert.Null(p.S2.Row2);
        Assert.Equal(new ScadBarSet(2, 14), p.S3);
        Assert.Equal(new ScadBarSet(1, 10), p.S4);
        Assert.Equal(new ScadRodStirrups(8, 2, 0.15), p.StirrupsZ);
        Assert.Equal(new ScadRodStirrups(6, 4, 0.2), p.StirrupsY);
    }

    [Fact]
    public void ScadApiException_FormatsResourceAndDetails()
    {
        var ex = new ScadApiException("Key", ["ApiReadProject", 3], "Файл занят");
        string text = ex.Format(_ => "Ошибка {0}: код {1}");
        Assert.Equal("Ошибка ApiReadProject: код 3" + Environment.NewLine + "Файл занят", text);
    }
}
