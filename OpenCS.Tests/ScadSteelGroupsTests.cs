using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using CScore.Fem;
using CScore.Import;
using Microsoft.Data.Sqlite;
using OpenCS.Services;
using OpenCS.Services.Scad;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Стальные группы SCAD: раскладка ApiSteelElem, индекс, вложение scad_steel_groups, группы КЭ «Сталь: …».</summary>
public sealed class ScadSteelGroupsTests
{
    /// <summary>Запись ApiSteelElem как у группы «Балки» модели 111.SPR (пробник 03.10), единицы длины — см.</summary>
    static byte[] BeamsRecord(bool stepLinear = true, bool lengthXoZ = false)
    {
        var b = new byte[ScadApiLayouts.SteelSize];
        Encoding.ASCII.GetBytes("C255").CopyTo(b, 0);
        b[80] = 1;                                                     // What_is: группа элементов
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(81), 2);      // балка
        void D(int o, double v) => BinaryPrimitives.WriteDoubleLittleEndian(b.AsSpan(o), v);
        void I(int o, int v) => BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(o), v);
        D(96, 1.1); D(104, 0.8); D(120, 2); D(128, 1);
        D(136, 180); D(144, 400); D(152, 50); D(160, 60); D(176, 600); D(184, 0);
        b[262] = 1; b[280] = 0;
        D(425, 0.5);
        I(633, lengthXoZ ? 1 : 0); I(637, 0); I(679, stepLinear ? 1 : 0);
        return b;
    }

    [Fact]
    public void ParseSteel_SyntheticRecord()
    {
        var g = ScadApiLayouts.ParseSteel(BeamsRecord(), 2, "Балки", [10, 11], lengthUnitM: 0.01);

        Assert.Equal(2, g.Num);
        Assert.Equal("Балки", g.Name);
        Assert.Equal([10, 11], g.ElementIds);
        Assert.Equal("C255", g.SteelMark);
        Assert.False(g.IsMember);
        Assert.Equal(2, g.ConstructionType);
        Assert.Equal(1.1, g.GammaN);
        Assert.Equal(0.8, g.GammaC);
        Assert.Equal(2, g.MuXoZ);
        Assert.Equal(1, g.MuYoZ);
        Assert.Null(g.LengthXoZ);
        Assert.Equal(180, g.CompressionLimit);
        Assert.Equal(60, g.CompressionLimitAlpha);
        Assert.Equal(400, g.TensionLimit);
        Assert.Equal(0.5, g.StepOutPlane);
        Assert.Equal(1, g.DesignType);
    }

    [Fact]
    public void ParseSteel_LengthAndRatioByTypeFlags()
    {
        var g = ScadApiLayouts.ParseSteel(BeamsRecord(stepLinear: false, lengthXoZ: true), 1, "", [], lengthUnitM: 0.01);

        Assert.Equal(6.0, g.LengthXoZ);
        Assert.Null(g.LengthYoZ);
        Assert.Null(g.StepOutPlane);
        Assert.Equal(0.5, g.StepOutPlaneRatio);
    }

    [Fact]
    public void ParseSteel_ShortBuffer_Throws() =>
        Assert.Throws<ArgumentException>(() => ScadApiLayouts.ParseSteel(new byte[100], 1, "", [], 1));

    internal static ScadSteelGroup Group(int num, string name, int[] ids, string mark = "C255", double gammaC = 1,
        double muXoZ = 1, double muYoZ = 1, double? step = null, double stepRatio = 1, bool isMember = false) =>
        new(num, name, ids, mark, "", 0, isMember, 0, 1, gammaC, muXoZ, muYoZ, null, null, 180, 60, 400,
            step, stepRatio, 0, 1);

    [Fact]
    public void Index_ElementInSeveralGroups_LowerNumber()
    {
        var index = new ScadSteelGroupIndex([Group(2, "Б", [5, 6]), Group(1, "А", [6, 7])]);

        Assert.Equal(1, index.Find(6)!.Num);
        Assert.Equal(2, index.Find(5)!.Num);
        Assert.Null(index.Find(8));
        Assert.Equal([1, 2], index.Groups.Select(g => g.Num));
    }

    [Fact]
    public void Json_RoundTrip()
    {
        var g = Group(2, "Балки", [1, 2], step: 0.5) with { LengthXoZ = 6 };
        var back = ScadSteelGroupIndex.FromJson(ScadSteelGroupIndex.ToJson([g])).Groups.Single();

        Assert.Equal(JsonSerializer.Serialize(g), JsonSerializer.Serialize(back));
        Assert.Throws<InvalidDataException>(() => ScadSteelGroupIndex.FromJson("{"));
    }

    [Fact]
    public void SaveAndLoad_ThroughSchemaData()
    {
        string path = Path.Combine(Path.GetTempPath(), $"opencs-steel-groups-{Guid.NewGuid():N}.db");
        try
        {
            using var db = new DatabaseService(path);
            var schema = new FemSchema { Tag = "SCAD", SourceType = "scad" };
            db.SaveFemSchema(schema);
            Assert.Null(FemCheckSchemaData.Load(db, schema.Id).ScadSteelGroups);

            db.SaveFemSchemaSourceFile(schema.Id, FemSchemaSourceFileKind.ScadSteelGroups, "",
                Encoding.UTF8.GetBytes(ScadSteelGroupIndex.ToJson([Group(1, "Связи", [3, 4], gammaC: 0.9)])));
            var data = FemCheckSchemaData.Load(db, schema.Id);

            Assert.Empty(data.Errors);
            Assert.Equal(0.9, data.ScadSteelGroups!.Find(4)!.GammaC);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { File.Delete(path); } catch { }
        }
    }

    [Fact]
    public void Converter_SteelMemberGroups()
    {
        var data = new ScadSchemaData();
        data.Elements.Add(new ScadElementRecord(1, 5, 1, [1, 2]));
        data.Elements.Add(new ScadElementRecord(2, 5, 1, [2, 3]));
        data.SteelGroups.Add(Group(1, "Балки", [1, 2, 99]));
        data.SteelGroups.Add(Group(2, " ", [2]));
        data.SteelGroups.Add(Group(3, "Пусто", [99]));

        var groups = ScadSchemaConverter.ToFemMemberGroupsBySteelGroups(data, 7);

        Assert.Equal(["Сталь: Балки", "Сталь: 2"], groups.Select(g => g.Tag));
        Assert.Equal(["1", "2"], groups[0].Tags);
        Assert.True(groups[0].IsMeshGroup);
        Assert.All(groups, g => Assert.Equal(7, g.SchemaId));
    }
}
