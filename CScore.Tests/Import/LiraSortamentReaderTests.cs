using System.Buffers.Binary;
using System.Text;
using CScore.Import;
using CScore.Sp16;
using Xunit;

namespace CScore.Tests.Import;

/// <summary>Синтетические сортаменты ЛИРЫ по формату спеки §10 (настоящие *.srt в репозиторий не кладутся).</summary>
static class LiraSrtTestData
{
    /// <summary>Файл сортамента: заголовок, имена фиксированной ширины, служебный промежуток, записи по 202 байта.</summary>
    public static byte[] Build(string title, params (string Name, Dictionary<int, float> Values)[] rows)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var cp1251 = Encoding.GetEncoding(1251);
        const int width = 16, record = 202;
        byte[] titleBytes = cp1251.GetBytes(title);
        int namesOffset = 0x68 + 1 + titleBytes.Length + 20;
        int recordsOffset = namesOffset + rows.Length * width + 7;
        var data = new byte[recordsOffset + rows.Length * record + 30];

        "LiraWin steel Sortament ver. 1.6"u8.CopyTo(data);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0x20), 0x18);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0x22), (ushort)rows.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0x26), width);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0x2A), record);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x2C), (uint)namesOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x34), (uint)recordsOffset);
        data[0x68] = (byte)titleBytes.Length;
        titleBytes.CopyTo(data, 0x69);
        for (int i = 0; i < rows.Length; i++)
        {
            cp1251.GetBytes(rows[i].Name).CopyTo(data, namesOffset + i * width);
            foreach (var (k, v) in rows[i].Values)
                BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(recordsOffset + i * record + 10 + 4 * k), v);
        }
        return data;
    }

    /// <summary>Квадратная гнутая труба ГОСТ 30245-94 (как в gn-kv94.profiles.srt): см, A, Iy, Iz.</summary>
    public static (string, Dictionary<int, float>) Tube(string name, float b, float t, float a, float i) =>
        (name, new() { [0] = b, [1] = b, [2] = b, [3] = t, [4] = t, [5] = t, [9] = t, [16] = a, [19] = i, [20] = i, [40] = 2 * i });

    public const string TubesTitle =
        "Профили стальные гнутые замкнутые сварные квадратные для строительных конструкций (ГОСТ 30245-94)\rГОСТ 30245-94";
}

public class LiraSortamentReaderTests
{
    [Fact]
    public void Read_TitleStandardRowsAndValues()
    {
        var file = LiraSortamentReader.Read(LiraSrtTestData.Build(LiraSrtTestData.TubesTitle,
            LiraSrtTestData.Tube("50 x 2", 5, 0.2f, 3.9f, 14.4f),
            LiraSrtTestData.Tube("80 x 3 ", 8, 0.3f, 8.96f, 87.8f)));

        Assert.StartsWith("Профили стальные гнутые замкнутые", file.Title);
        Assert.Equal("ГОСТ 30245-94", file.Standard);
        Assert.Equal(["50 x 2", "80 x 3"], file.Rows.Select(r => r.Name));
        var row = file.Rows[1];
        Assert.Equal(8, row.H);
        Assert.Equal(0.3, row.Tw);
        Assert.Equal(0.3, row.R1);
        Assert.Equal(8.96, row.A);
        Assert.Equal(87.8, row.Iy);
        Assert.Equal(175.6, row.It);
    }

    [Theory]
    [InlineData("80 x 3")]
    [InlineData("80 x 3  ")]
    [InlineData("50 x 2.5")]
    public void Find_TrimmedNameAndDecimalSeparator(string name)
    {
        var file = LiraSortamentReader.Read(LiraSrtTestData.Build(LiraSrtTestData.TubesTitle,
            LiraSrtTestData.Tube("80 x 3", 8, 0.3f, 8.96f, 87.8f),
            LiraSrtTestData.Tube("50 x 2,5", 5, 0.25f, 4.7f, 17.2f)));

        Assert.NotNull(file.Find(name));
        Assert.Null(file.Find("90 x 3"));
    }

    [Fact]
    public void Read_TitleWithoutStandardLine_StandardFromParentheses()
    {
        var file = LiraSortamentReader.Read(LiraSrtTestData.Build("Уголки стальные горячекатаные равнополочные (ГОСТ 8509-86)"));

        Assert.Equal("ГОСТ 8509-86", file.Standard);
        Assert.Empty(file.Rows);
    }

    [Fact]
    public void Read_NotSortamentOrTruncated_InvalidData()
    {
        var good = LiraSrtTestData.Build(LiraSrtTestData.TubesTitle, LiraSrtTestData.Tube("50 x 2", 5, 0.2f, 3.9f, 14.4f));

        Assert.Throws<InvalidDataException>(() => LiraSortamentReader.Read(new byte[200]));
        Assert.Throws<InvalidDataException>(() => LiraSortamentReader.Read(good.AsSpan(0, good.Length - 100)));
    }
}

public class LiraSteelProfilesTests
{
    const string Turkestan = "Section = Tubing  MatId = STL  Comment = | TCAR 80x3.2 | File  = |gn-kv94.profiles.srt|  "
                             + "Shape = |80 x 3|   NEL:0 Iter:0 Uli:0 DS1:1";

    [Fact]
    public void SteelRef_TurkestanStiffness()
    {
        Assert.Equal(new LiraSteelRef("Tubing", "gn-kv94.profiles.srt", "80 x 3", null), LiraSteelProfiles.SteelRef(Turkestan));
        Assert.Equal(new LiraSteelRef("Tubing", "gn-kv94.profiles.srt", "50 x 4", null), LiraSteelProfiles.SteelRef(
            "Section = Tubing  MatId = STL  File  = |gn-kv94.profiles.srt|  Shape = |50 x 4|   NEL:0 Iter:0 Uli:0 DS1:1"));
    }

    [Fact]
    public void SteelRef_SteelMarkAndSteelFileAreDistinguished()
    {
        var r = LiraSteelProfiles.SteelRef(
            "Section = Pipe File = |Truba.profiles.srt| SteelFile = |SpTruba.steels.srt| Shape = |83 x 2| Steel = |С245|");

        Assert.Equal(new LiraSteelRef("Pipe", "Truba.profiles.srt", "83 x 2", "С245"), r);
    }

    [Fact]
    public void SteelRef_NotSteelString_Null()
    {
        Assert.Null(LiraSteelProfiles.SteelRef("Ro:2.5 E:3e+06 B:30 H:50 BAR_END"));
        Assert.Null(LiraSteelProfiles.SteelRef("Section = Tubing  File  = ||  Shape = |80 x 3|"));
    }

    static LiraSortamentFile File(string title, string standard, params (string Name, Dictionary<int, double> Values)[] rows) =>
        new(title, standard, rows.Select(r =>
        {
            var values = new double[48];
            foreach (var (k, v) in r.Values) values[k] = v;
            return new LiraSortamentRow(r.Name, values);
        }).ToList());

    [Fact]
    public void Tubing_BentBoxWithInnerRadius_InMeters()
    {
        var file = File("Профили … квадратные", "ГОСТ 30245-94",
            ("80 x 3", new() { [0] = 8, [1] = 8, [2] = 8, [3] = 0.3, [4] = 0.3, [5] = 0.3, [9] = 0.3, [16] = 8.96, [19] = 87.8, [20] = 87.8 }));

        var (shape, reason) = LiraSteelProfiles.FromRow("Tubing", file, file.Rows[0]);

        Assert.Null(reason);
        Assert.Equal(new ImportedSteelShape(SteelProfileKind.Box, SteelFabrication.Bent, 0.08, 0.08, 0.003, 0.003, 0.003, 0, 0,
            "ГОСТ 30245-94", "80 x 3", 8.96, 87.8, 87.8), shape);
    }

    [Fact]
    public void DoubleT_8239_RolledWithSlope()
    {
        var file = File("Двутавры … (ГОСТ 8239-89)", "ГОСТ 8239-89",
            ("10", new() { [0] = 10, [1] = 5.5, [2] = 5.5, [3] = 0.72, [4] = 0.45, [5] = 0.72, [9] = 0.7, [10] = 0.25, [16] = 12, [19] = 198, [20] = 17.9 }));

        var (shape, _) = LiraSteelProfiles.FromRow("DoubleT", file, file.Rows[0]);

        Assert.Equal(SteelProfileKind.IBeam, shape!.Kind);
        Assert.Equal(SteelFabrication.Rolled, shape.Fabrication);
        Assert.Equal(0.1, shape.H, 9);
        Assert.Equal(0.055, shape.B, 9);
        Assert.Equal(0.0045, shape.Tw, 9);
        Assert.Equal(0.0072, shape.Tf, 9);
        Assert.Equal(0.007, shape.R1, 9);
        Assert.Equal(0.0025, shape.R2, 9);
        Assert.Equal(0.12, shape.FlangeSlope);
    }

    [Fact]
    public void Channel_RolledSlopedOrBent()
    {
        var rolled = File("Швеллеры с уклоном внутренних граней полок (ГОСТ 8240-72)", "ГОСТ 8240-72",
            ("5", new() { [0] = 5, [1] = 3.2, [2] = 3.2, [3] = 0.7, [4] = 0.44, [5] = 0.7, [9] = 0.6, [10] = 0.25 }));
        var bent = File("Швеллеры", "ГОСТ 8278-83",
            ("400 x 95 x 8", new() { [0] = 40, [1] = 9.5, [2] = 9.5, [3] = 0.8, [4] = 0.8, [5] = 0.8, [9] = 1.2 }));

        var (r, _) = LiraSteelProfiles.FromRow("Channel", rolled, rolled.Rows[0]);
        var (b, _) = LiraSteelProfiles.FromRow("Channel", bent, bent.Rows[0]);

        Assert.Equal((SteelFabrication.Rolled, 0.10, 0.0044, 0.007), (r!.Fabrication, r.FlangeSlope, r.Tw, r.Tf));
        Assert.Equal((SteelFabrication.Bent, 0.0, 0.008, 0.012), (b!.Fabrication, b.FlangeSlope, b.Tf, b.R1));
    }

    [Fact]
    public void Angle_UnequalLegs_VerticalLegIsH()
    {
        var file = File("Сталь прокатная угловая неравнополочная (ГОСТ 8510-72)", "ГОСТ 8510-72",
            ("25 x 16 x 3", new() { [0] = 2.5, [2] = 1.6, [3] = 0.3, [4] = 0.3, [9] = 0.35, [10] = 0.12, [16] = 1.16, [19] = 0.7, [20] = 0.22 }));

        var (shape, _) = LiraSteelProfiles.FromRow("Angle", file, file.Rows[0]);

        Assert.Equal(SteelProfileKind.Angle, shape!.Kind);
        Assert.Equal(SteelFabrication.Rolled, shape.Fabrication);
        Assert.Equal((0.025, 0.016, 0.003), (shape.H, shape.B, shape.Tw));
    }

    [Theory]
    [InlineData("Pipe", "ГОСТ 10704-91", SteelProfileKind.Pipe, SteelFabrication.Welded, 0.083, 0.0)]
    [InlineData("Pipe", "ГОСТ 8732-78", SteelProfileKind.Pipe, SteelFabrication.Rolled, 0.083, 0.0)]
    [InlineData("Round", "ГОСТ 2590-2006", SteelProfileKind.Round, SteelFabrication.Rolled, 0.083, 0.0)]
    [InlineData("Square", "ГОСТ 2591-2006", SteelProfileKind.Rect, SteelFabrication.Rolled, 0.083, 0.083)]
    [InlineData("Sheet", "ГОСТ 19903-2015", SteelProfileKind.Rect, SteelFabrication.Rolled, 0.083, 0.002)]
    public void PipeRoundSquareSheet(string section, string standard, SteelProfileKind kind, SteelFabrication fabrication,
        double h, double b)
    {
        double side = section == "Square" ? 8.3 : 0.2;
        var file = File("…", standard, ("x", new() { [0] = 8.3, [4] = side }));

        var (shape, _) = LiraSteelProfiles.FromRow(section, file, file.Rows[0]);

        Assert.Equal(kind, shape!.Kind);
        Assert.Equal(fabrication, shape.Fabrication);
        Assert.Equal(h, shape.H, 9);
        Assert.Equal(b, shape.B, 9);
    }

    [Fact]
    public void UnsupportedSectionOrEmptyRow_Reason()
    {
        var file = File("…", "", ("x", new() { [0] = 5 }), ("0", new()));

        Assert.Equal("вид сечения ЛИРЫ «Tee» не поддерживается", LiraSteelProfiles.FromRow("Tee", file, file.Rows[0]).Reason);
        Assert.Contains("размеры не заданы", LiraSteelProfiles.FromRow("Round", file, file.Rows[1]).Reason);
    }

    [Fact]
    public void ResolveAll_FileLoadedOnce_ReasonsForMissingFileAndShape()
    {
        var tubes = LiraSortamentReader.Read(LiraSrtTestData.Build(LiraSrtTestData.TubesTitle,
            LiraSrtTestData.Tube("80 x 3", 8, 0.3f, 8.96f, 87.8f)));
        var loads = new List<string>();

        var entries = LiraSteelProfiles.ResolveAll(
            [
                (14, Turkestan),
                (22, Turkestan.Replace("80 x 3", "80 x 5")),
                (28, Turkestan.Replace("gn-kv94", "other")),
                (30, "Section = Tubing"),
            ],
            name => { loads.Add(name); return name == "gn-kv94.profiles.srt" ? tubes : null; });

        Assert.Equal(["gn-kv94.profiles.srt", "other.profiles.srt"], loads);
        Assert.Equal(SteelProfileKind.Box, entries[0].Shape!.Kind);
        Assert.Equal("gn-kv94.profiles.srt: 80 x 3", entries[0].Source);
        Assert.Equal("в сортаменте ЛИРЫ gn-kv94.profiles.srt нет профиля «80 x 5»", entries[1].Reason);
        Assert.Equal("нет сортамента ЛИРЫ other.profiles.srt", entries[2].Reason);
        Assert.Contains("нет ссылки на сортамент", entries[3].Reason);
    }

    [Fact]
    public void Entries_JsonRoundTripWithSteelMark()
    {
        var entries = LiraSteelProfiles.ResolveAll(
            [(14, Turkestan + " Steel = |С255|")],
            _ => LiraSortamentReader.Read(LiraSrtTestData.Build(LiraSrtTestData.TubesTitle,
                LiraSrtTestData.Tube("80 x 3", 8, 0.3f, 8.96f, 87.8f))));

        var index = SteelProfileIndex.FromJson(SteelProfileIndex.ToJson(entries));

        Assert.Equal(entries[0], index.Find(14));
        Assert.Equal("С255", index.Find(14)!.SteelMark);
    }
}
