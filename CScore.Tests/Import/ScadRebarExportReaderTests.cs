using System.Text;
using CScore.Import;
using Xunit;

namespace CScore.Tests.Import;

/// <summary>Чтение выгрузки плагина SCAD «Экспорт для OpenCS» и хранение ЖБ-групп SCAD.</summary>
public class ScadRebarExportReaderTests
{
    const string Units =
        "\"units\":[{\"id\":\"theResultArmSquare\",\"name\":\"см2\",\"title\":\"Площадь\",\"factor\":0.0001}," +
        "{\"id\":\"theResultForces\",\"error\":\"нет единицы\"}]";

    /// <summary>Как пишет плагин: пластина 55459, стержни 814 (4 × 4,14 см²) и 818 (два сечения, второе — ошибка).</summary>
    static string Sample(string units = Units, string format = "opencs-scad-rebar", int version = 1, string extraPlates = "") =>
        "{\"format\":\"" + format + "\",\"version\":" + version +
        ",\"project\":\"C:\\\\Проекты\\\\Музей.SPR\",\"name\":\"Музей\",\"exported\":\"Thu, 1 Oct 2026 12:00:00 UTC\"," +
        units + ",\n\"plates\":[\n" +
        "{\"e\":55459,\"as\":[0.000049,0.000168,0.000042,0.000158],\"asw\":[0,null]}" + extraPlates + "],\n" +
        "\"bars\":[\n" +
        "{\"e\":814,\"sections\":[{\"as\":[0.000414,0.000414,0.000414,0.000414],\"iw\":[0.0001,0.00005]}]},\n" +
        "{\"e\":818,\"sections\":[{\"as\":[0.000456,0.000356,0.000406,0.000406],\"iw\":[0,0]},null," +
        "{\"as\":[0.0005,0.0001,null,0.0002],\"iw\":[0.0002,0]}]}]\n}\n";

    static byte[] Utf16(string s) => [0xFF, 0xFE, .. Encoding.Unicode.GetBytes(s)];

    [Fact]
    public void Utf16_ConvertsToCm2()
    {
        var f = ScadRebarExportReader.Read(Utf16(Sample()));

        Assert.Equal(@"C:\Проекты\Музей.SPR", f.Project);
        Assert.Equal("Музей", f.Name);
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero), f.Exported);

        var p = f.Plates[55459];
        Assert.Equal(0.49, p.As1!.Value, 6);
        Assert.Equal(1.68, p.As2!.Value, 6);
        Assert.Equal(0.42, p.As3!.Value, 6);
        Assert.Equal(1.58, p.As4!.Value, 6);
        Assert.Equal(0, p.AswX!.Value, 9);
        Assert.Null(p.AswY);

        var s = f.Bars[814].Sections.Single()!;
        Assert.Equal(4.14, s.As1!.Value, 6);
        Assert.Equal(4 * 4.14, s.LongitudinalSum!.Value, 6);
        Assert.Equal(1.0, s.IwZ!.Value, 6);
        Assert.Equal(0.5, s.IwY!.Value, 6);
    }

    [Fact]
    public void Utf8_WithAndWithoutBom()
    {
        byte[] plain = Encoding.UTF8.GetBytes(Sample());
        var a = ScadRebarExportReader.Read(plain);
        var b = ScadRebarExportReader.Read([0xEF, 0xBB, 0xBF, .. plain]);
        Assert.Equal(a.Plates.Count, b.Plates.Count);
        Assert.Equal("Музей", b.Name);
        Assert.Equal(2, b.Bars.Count);
    }

    [Fact]
    public void NullSection_AndEnvelope()
    {
        var bar = ScadRebarExportReader.Read(Utf16(Sample())).Bars[818];

        Assert.Equal(3, bar.Sections.Count);
        Assert.Null(bar.Sections[1]);
        Assert.Null(bar.Sections[2]!.As3);
        Assert.False(bar.Sections[2]!.IsComplete);

        var env = bar.Envelope!;
        Assert.Equal(5.0, env.As1!.Value, 6);
        Assert.Equal(3.56, env.As2!.Value, 6);
        Assert.Equal(4.06, env.As3!.Value, 6);
        Assert.Equal(4.06, env.As4!.Value, 6);
        Assert.Equal(2.0, env.IwZ!.Value, 6);
    }

    [Fact]
    public void UnitError_IsWarning()
    {
        var f = ScadRebarExportReader.Read(Utf16(Sample()));
        Assert.Contains(f.Warnings, w => w.Contains("theResultForces"));
        Assert.Equal(1e-4, f.Units["theResultArmSquare"].Factor);
        Assert.DoesNotContain(f.Warnings, w => w.Contains("нестандартные"));
    }

    [Fact]
    public void NonStandardAreaFactor_IsWarning()
    {
        const string mm2 = "\"units\":[{\"id\":\"theResultArmSquare\",\"name\":\"мм2\",\"title\":\"\",\"factor\":1e-6}]";
        var f = ScadRebarExportReader.Read(Utf16(Sample(mm2)));
        Assert.Contains(f.Warnings, w => w.Contains("нестандартные"));
        // Значения те же — в СИ.
        Assert.Equal(0.49, f.Plates[55459].As1!.Value, 6);
    }

    [Fact]
    public void ForeignFormat_Throws()
    {
        Assert.Throws<InvalidDataException>(() => ScadRebarExportReader.Read(Utf16(Sample(format: "other"))));
        Assert.Throws<InvalidDataException>(() => ScadRebarExportReader.Read(Encoding.UTF8.GetBytes("не json")));
    }

    [Fact]
    public void NewerVersion_Throws()
    {
        var ex = Assert.Throws<InvalidDataException>(() => ScadRebarExportReader.Read(Utf16(Sample(version: 2))));
        Assert.Contains("2", ex.Message);
    }

    [Fact]
    public void DuplicateAndNegative_AreWarnings()
    {
        var f = ScadRebarExportReader.Read(Utf16(Sample(extraPlates:
            ",\n{\"e\":55459,\"as\":[1,1,1,1],\"asw\":[0,0]},\n{\"e\":7,\"as\":[-0.0001,0,0,0],\"asw\":[0,0]}")));

        Assert.Equal(0.49, f.Plates[55459].As1!.Value, 6);
        Assert.Contains(f.Warnings, w => w.Contains("дважды") && w.Contains("55459"));
        Assert.Contains(f.Warnings, w => w.Contains("Отрицательные") && w.Contains("7"));
    }

    [Fact]
    public void SchemaMatch_MissingAndKindMismatch()
    {
        var f = ScadRebarExportReader.Read(Utf16(Sample()));
        var m = f.MatchSchema([(55459, true), (814, false), (818, true)]);

        Assert.Equal(1, m.PlatesMatched);
        Assert.Equal(1, m.BarsMatched);
        Assert.Empty(m.Missing);
        Assert.Equal([818], m.KindMismatch);

        var m2 = f.MatchSchema([(814, false)]);
        Assert.Equal([818, 55459], m2.Missing);
    }

    static ScadConcreteGroup Group(int num, double[] range, params int[] ids) =>
        new(num, "Г" + num, 1, range, "B25", "A500", "A240", false, [0.4, 0.3], ids);

    [Fact]
    public void ConcreteGroups_JsonRoundTrip_AndIndex()
    {
        var groups = new[] { Group(2, [0.04, 0.05, 0, 0], 1, 2), Group(1, [0.03, 0.03, 0, 0], 2, 3) };
        string json = ScadConcreteGroupIndex.ToJson(groups);
        var index = ScadConcreteGroupIndex.FromJson(json);

        Assert.Equal([1, 2], index.Groups.Select(g => g.Num));
        Assert.Equal("A500", index.Groups[0].LongitudinalRebarClass);
        Assert.Equal([0.4, 0.3], index.Groups[0].CrackWidthMm);
        Assert.Equal(1, index.MultiGroupElements);
        Assert.Equal(1, index.Find(2)!.Num);
        Assert.Equal(2, index.Find(1)!.Num);
        Assert.Null(index.Find(99));
    }

    [Fact]
    public void PlateCovers_ZeroA3A4_TakeA1A2()
    {
        var c = ScadConcreteGroupIndex.PlateCovers(Group(1, [0.03, 0.025, 0, 0]));
        Assert.Equal((0.03, 0.025, 0.03, 0.025), c);
        var d = ScadConcreteGroupIndex.PlateCovers(Group(1, [0.03, 0.025, 0.04, 0.045]));
        Assert.Equal((0.03, 0.025, 0.04, 0.045), d);
    }
}
