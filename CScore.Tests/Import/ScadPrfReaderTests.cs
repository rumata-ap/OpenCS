using CScore.Import;
using CScore.Sp16;
using Xunit;

namespace CScore.Tests.Import;

/// <summary>Читатель сортамента SCAD (PRF) и профили стали по строкам PRF — на синтетических байтах.</summary>
public class ScadPrfReaderTests
{
    static ScadPrfFile File(params ScadPrfTestData.Table[] tables) =>
        ScadPrfReader.Read(ScadPrfTestData.Build("Тестовый сортамент", tables));

    [Fact]
    public void Read_TablesColumnsAndRows()
    {
        var file = File(ScadPrfTestData.IBeams(), ScadPrfTestData.SquareTubes());

        Assert.Equal("Тестовый сортамент", file.Title);
        Assert.Equal(2, file.Tables.Count);
        var d1 = file.Table("D1")!;
        Assert.Equal(1, d1.Kind);
        Assert.Equal("СТО АСЧМ 20-93", d1.Standard);
        Assert.Equal(["h", "b", "s", "t", "r1", "A", "Iy", "Iz"], d1.Columns.Select(c => c.Name));
        Assert.Equal("cm4", d1.Columns[6].Unit);
        Assert.Equal(0.2, d1.ValueSi(d1.Rows[0], "h")!.Value, 9);
        Assert.Equal(27.16, d1.Value(d1.Rows[0], "A")!.Value, 4);

        var tube = file.Table("okv2012")!;
        Assert.Equal("ГОСТ 30245-2012", tube.Standard);
        Assert.Equal(225.1, tube.Value(tube.Rows[0], "Iz")!.Value, 4); // колонка «Iy=Iz»
    }

    [Fact]
    public void Find_NumberIsOneBased()
    {
        var file = File(ScadPrfTestData.IBeams());

        Assert.Equal("20Б1", file.Find("d1", 1)!.Value.Row.Name);
        Assert.Equal("25Б1", file.Find("d1", 2)!.Value.Row.Name);
        Assert.Null(file.Find("d1", 0));
        Assert.Null(file.Find("d1", 3));
        Assert.Null(file.Find("d9", 1));
    }

    [Fact]
    public void Read_NotPrf_Throws()
    {
        Assert.Throws<InvalidDataException>(() => ScadPrfReader.Read("not a prf file"u8));
        var bytes = ScadPrfTestData.Build("x", ScadPrfTestData.IBeams());
        Assert.Throws<InvalidDataException>(() => ScadPrfReader.Read(bytes.AsSpan(0, bytes.Length - 3)));
    }

    [Theory]
    [InlineData("Двутавр с уклоном полок по ГОСТ 8239-89 ", "ГОСТ 8239-89")]
    [InlineData("Уголок неравнополочный по ГОСТ 8510-86*", "ГОСТ 8510-86*")]
    [InlineData("Двутавp колонный по ГОСТ Р 57837-2017 изменение №1", "ГОСТ Р 57837-2017")]
    [InlineData("Двутавр широкополочный по ТУ 0925-016-00186269-2016", "ТУ 0925-016-00186269-2016")]
    [InlineData("Специальные двутавры", "")]
    public void StandardOf_Title(string title, string expected) =>
        Assert.Equal(expected, ScadPrfReader.StandardOf(title));

    [Fact]
    public void Profile_RolledIBeam()
    {
        var (shape, reason) = ScadSteelProfiles.Resolve(File(ScadPrfTestData.IBeams()), "d1", 2);

        Assert.Null(reason);
        Assert.Equal(SteelProfileKind.IBeam, shape!.Kind);
        Assert.Equal(SteelFabrication.Rolled, shape.Fabrication);
        Assert.Equal(0.248, shape.H, 9);
        Assert.Equal(0.124, shape.B, 9);
        Assert.Equal(0.005, shape.Tw, 9);
        Assert.Equal(0.008, shape.Tf, 9);
        Assert.Equal(0.012, shape.R1, 9);
        Assert.Equal(0, shape.FlangeSlope);
        Assert.Equal("25Б1", shape.Name);
        Assert.Equal("СТО АСЧМ 20-93", shape.Standard);
        Assert.Equal(32.68, shape.ACm2!.Value, 4);
        Assert.Equal(3537, shape.IyCm4!.Value, 3);
        Assert.Equal(254.8, shape.IzCm4!.Value, 3);
    }

    [Fact]
    public void Profile_SquareTube_InnerRadius()
    {
        var (shape, _) = ScadSteelProfiles.Resolve(File(ScadPrfTestData.SquareTubes()), "okv2012", 1);

        Assert.Equal(SteelProfileKind.Box, shape!.Kind);
        Assert.Equal(SteelFabrication.Bent, shape.Fabrication);
        Assert.Equal(0.1, shape.H, 9);
        Assert.Equal(0.1, shape.B, 9);
        Assert.Equal(0.004, shape.Tw, 9);
        Assert.Equal(0.004, shape.R1, 9); // R = 8 мм наружный → 4 мм внутренний
        Assert.Equal(225.1, shape.IyCm4!.Value, 3);
    }

    [Fact]
    public void Profile_SlopedChannel_GammaIsSlope()
    {
        var (shape, _) = ScadSteelProfiles.Resolve(File(ScadPrfTestData.SlopedChannels()), "pu_ukl97", 1);

        Assert.Equal(SteelProfileKind.Channel, shape!.Kind);
        Assert.Equal(SteelFabrication.Rolled, shape.Fabrication);
        Assert.Equal(0.1, shape.FlangeSlope, 6);
        Assert.Equal(0.003, shape.R2, 9);
    }

    [Fact]
    public void Profile_BentChannel()
    {
        var (shape, _) = ScadSteelProfiles.Resolve(File(ScadPrfTestData.BentChannels()), "cg", 1);

        Assert.Equal(SteelFabrication.Bent, shape!.Fabrication);
        Assert.Equal(0.004, shape.Tw, 9);
        Assert.Equal(0.004, shape.Tf, 9);
        Assert.Equal(0.006, shape.R1, 9);
    }

    [Fact]
    public void Profile_MirroredAngle_FromPreviousTable()
    {
        var file = File(ScadPrfTestData.Angles());

        var (plain, _) = ScadSteelProfiles.Resolve(file, "ce_equal", 1);
        var (mirrored, reason) = ScadSteelProfiles.Resolve(file, "cn_equal", 1);

        Assert.Null(reason);
        Assert.False(plain!.Flipped);
        Assert.True(mirrored!.Flipped);
        Assert.Equal("LN50x5", mirrored.Name);
        Assert.Equal(SteelProfileKind.Angle, mirrored.Kind);
        Assert.Equal(0.05, mirrored.H, 9);
        Assert.Equal(0.005, mirrored.Tw, 9);
    }

    [Fact]
    public void Profile_Pipe()
    {
        var (shape, _) = ScadSteelProfiles.Resolve(File(ScadPrfTestData.Pipes()), "diam", 1);

        Assert.Equal(SteelProfileKind.Pipe, shape!.Kind);
        Assert.Equal(SteelFabrication.Welded, shape.Fabrication);
        Assert.Equal(0.114, shape.H, 9);
        Assert.Equal(0.004, shape.Tw, 9);
    }

    [Fact]
    public void Profile_UnknownKindOrRow_Reason()
    {
        var file = File(new ScadPrfTestData.Table(99, "x", "Сквозное сечение", [("h", "mm")], [("a", [100])]),
            ScadPrfTestData.IBeams());

        Assert.Contains("не поддерживается", ScadSteelProfiles.Resolve(file, "x", 1).Reason);
        Assert.Contains("нет профиля № 5", ScadSteelProfiles.Resolve(file, "d1", 5).Reason);
        Assert.Contains("таблицы «zz» нет", ScadSteelProfiles.Resolve(file, "zz", 1).Reason);
    }
}
