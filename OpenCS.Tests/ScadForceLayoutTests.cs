using System.Buffers.Binary;
using OpenCS.Services.Scad;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Разбор ApiElemEffors / ApiElemRsu / ApiElemRsuStr из байтов и выборка первого слоя (без DLL).</summary>
public class ScadForceLayoutTests
{
    [Fact]
    public void ParseEffors_SyntheticHeader()
    {
        var b = new byte[ScadApiLayouts.EfforsSize];
        BinaryPrimitives.WriteUInt32LittleEndian(b, 814);
        b[4] = 6;
        BinaryPrimitives.WriteInt64LittleEndian(b.AsSpan(5), 0x1111);
        b[13] = 3;
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(14), 2);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(16), 9);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(18), 4);
        BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(29), 324);
        BinaryPrimitives.WriteInt64LittleEndian(b.AsSpan(37), 0x2222);
        BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(53), 144);
        BinaryPrimitives.WriteInt64LittleEndian(b.AsSpan(61), 0x3333);

        var e = ScadApiLayouts.ParseEffors(b);

        Assert.Equal(new ScadEfforsHeader(814, 6, 0x1111, 3, 2, 9, 4, 324, 0x2222, 144, 0x3333), e);
    }

    [Fact]
    public void ParseRsuAndRow_SyntheticRecords()
    {
        var h = new byte[ScadApiLayouts.RsuSize];
        BinaryPrimitives.WriteUInt32LittleEndian(h, 55459);
        h[9] = 8;
        BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(10), 0); // LengthData у РСУ = 0
        BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(14), 360);
        BinaryPrimitives.WriteInt64LittleEndian(h.AsSpan(18), 0x4444);
        Assert.Equal(new ScadRsuHeader(55459, 8, 360, 0x4444), ScadApiLayouts.ParseRsu(h));

        var r = new byte[ScadApiLayouts.RsuRowSize];
        r[4] = 2;  // NumPoint
        r[5] = 2;  // NumPointElem
        r[6] = 5;  // NumColumn
        r[7] = 3;  // GroupRsu
        BinaryPrimitives.WriteUInt16LittleEndian(r.AsSpan(8), 17);
        BinaryPrimitives.WriteInt64LittleEndian(r.AsSpan(20), 0x5555);
        Assert.Equal(new ScadRsuRowHeader(2, 3, 17, 0x5555), ScadApiLayouts.ParseRsuRow(r));
    }

    [Fact]
    public void FirstLayer_TakesLayerZeroOfEachPointAndRow()
    {
        // 2 точки × 2 строки × 2 слоя × 3 усилия: значение = 1000·точка + 100·строка + 10·слой + усилие.
        var src = new double[2 * 2 * 2 * 3];
        for (int p = 0; p < 2; p++)
            for (int r = 0; r < 2; r++)
                for (int l = 0; l < 2; l++)
                    for (int k = 0; k < 3; k++)
                        src[((p * 2 + r) * 2 + l) * 3 + k] = 1000 * p + 100 * r + 10 * l + k;

        var dst = ScadApiLayouts.FirstLayer(src, points: 2, rows: 2, layers: 2, quantityUs: 3);

        Assert.Equal([0, 1, 2, 100, 101, 102, 1000, 1001, 1002, 1100, 1101, 1102], dst);
        Assert.Equal(src, ScadApiLayouts.FirstLayer(src, 2, 4, 0, 3)); // слоёв 0 → как один
    }

    [Fact]
    public void ParseEffors_ShortBuffer_Throws() =>
        Assert.Throws<ArgumentException>(() => ScadApiLayouts.ParseEffors(new byte[40]));
}
