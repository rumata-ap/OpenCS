using System.Text;
using CScore.Fem;
using CScore.Import;
using Xunit;

namespace CScore.Tests.Import;

/// <summary>
/// Импорт схемы из текстового файла ЛИРЫ без API: документ 3 + <c>{13/}</c> → строки жёсткостей в формате API, документ 19 →
/// C1 пластин, <c>.ald</c> → имена, Ro и блоки. Строки — фрагменты «Мирной» и ЗАГС; свойства КЭ сверены с импортом «Мирной»
/// из API вручную (<c>OpenCS.Tests/LiraTextImportManualTests</c>: 7 жёсткостей, C1 5154 КЭ — совпадают).
/// </summary>
public sealed class LiraTextSchemaConverterTests
{
    const double TonToKn = 9.80665, T = TonToKn * 1000;

    const string Text = """
        ( 0/ 1; Синтетика/ 33;M 1 CM 100 T 1 C 1 / )
        ( 1/
        44 1 1 2 4 3 /
        10 3 1 5 /
        10 2 2 6 /
        10 6 5 6 /
        10 7 3 4 /
        10 21 4 6 /
        57 8 1 /
        )
        ( 3/
        1 GEI 3.06e+06 0.2 0.25 RO 2.5 /
         0 WLKE 1 WLKG 1 PLKE 0.6 PLKG 0.6 /
        2 S0 0 25 47.4/
         0 Mu 0.2/
        3 459000 8262 1434.37 3005.99 0 0 /
         0 RO 0.375/
         0 S0 3.06e+06 25 60/
         0 Mu 0.2/
        6 1e+06 1e+06 1e+06 1e+06 0 0 /
         0 Y 0.57735 0.57735 Z 0.57735 0.57735 /
         0 RO 0.01/
        7 51570.1 207.331 207.331 122.842 0 0 /
        0 RO 0.01927/
        0 S8 13 16 16 0 0/
        8 5.477E+01 5.477E+01 0 0 0 0 /
        21 S2 900000 40 60 120 20/
         0 RO 1/
         )
        {13/
        [7]
        Section = Tubing
        MatId = STL
        File  = |gnkv_2003.profiles.srt|
        Shape = |160 x 4|
        }
        ( 4/ 0 0 0 /1 0 0 /0 1 0 /1 1 0 /0 0 3 /1 0 3 / )
        ( 9/ 1 15.5 / )
        ( 19/ 1 2393.08 0 0 0 0/ 2 0.6 500 0 0 0 / )
        """;

    const string Ald = """
        <?xml version="1.0" standalone="yes"?>
        <LIRA_Project Title="Синтетика">
          <RigidArray>
            <Rigid Num="1" Ro="2.5" Colour="1" Comment="Железобетон стен" />
            <Rigid Num="3" Ro="2.5" Colour="1" Comment="Железобетон колонн" />
            <Rigid Num="6" Ro="0.01" Colour="1" Comment="" />
          </RigidArray>
          <ElemBlockArray>
            <ElemBlock NumFE="1" NumBlk="19" />
            <ElemBlock NumFE="2" NumBlk="19" />
            <ElemBlock NumFE="7" NumBlk="20" />
          </ElemBlockArray>
        </LIRA_Project>
        """;

    static LiraTextSchema Schema() => LiraTextReader.Read(new StringReader(Text));

    static LiraAldFile ReadAld() => LiraAldFile.Read(new MemoryStream(Encoding.UTF8.GetBytes(Ald)));

    [Fact]
    public void Ald_RigidsBlocks()
    {
        var ald = ReadAld();
        Assert.Equal(new LiraAldRigid(2.5, "Железобетон колонн"), ald.Rigids[3]);
        Assert.Equal(new LiraAldRigid(0.01, ""), ald.Rigids[6]);
        Assert.Equal(19, ald.ElementBlocks[2]);
    }

    [Fact]
    public void Elements_LinksExcluded_StiffnessesOnlyOfMeshElements()
    {
        var report = new List<string>();
        var data = LiraTextSchemaConverter.ToSchemaData(Schema(), ReadAld(), TonToKn, report);

        Assert.Equal(6, data.Nodes.Count);
        Assert.Equal(new LiraNodeRecord(6, 1, 0, 3, 0), data.Nodes[5]);
        Assert.Equal([1, 2, 3, 4, 5, 6], data.Elements.Select(e => e.Id));          // КЭ 57 — не в сетку
        Assert.Equal([1, 2, 3, 6, 7, 21], data.Stiffnesses.Select(s => s.Id));      // 8 — только у КЭ 57
        Assert.Equal([1, 2, 4, 3], data.Elements[0].NodeIds);
        Assert.Equal(15.5, data.PlateAxisAngles[1]);
        Assert.Contains(report, r => r.Contains("21 (сечение S2)"));
        Assert.Equal(new LiraUnits(T, 1), data.Units);
    }

    [Fact]
    public void Stiffnesses_InApiFormat_SamePropertiesAsApi()
    {
        var data = LiraTextSchemaConverter.ToSchemaData(Schema(), ReadAld(), TonToKn, []);
        var byId = data.Stiffnesses.ToDictionary(s => s.Id);

        Assert.Equal((36, "Железобетон стен"), (byId[1].KindCode, byId[1].Name));
        Assert.StartsWith("Ro:2.5 E:3060000 V:0.2 H:25 WLKE:1", byId[1].Params);
        Assert.EndsWith("PLATE_END", byId[1].Params);
        Assert.True(LiraStiffnessParams.IsBarAnalog(byId[2]));
        Assert.Equal((0, "Ro:2.5 E:3060000 B:25 H:60 BAR_END Mu:0.2 EF:459000 EIy:8262 EIz:1434.37 GIk:3005.99 STD_END"),
            (byId[3].KindCode, byId[3].Params));
        Assert.Equal("Ro:0.01 EF:1000000 EIy:1000000 EIz:1000000 GIk:1000000 DD10_END", byId[6].Params);
        Assert.Equal(LiraSteelProfiles.SteelKindCode, byId[7].KindCode);
        Assert.Equal("160 x 4", LiraSteelProfiles.SteelRef(byId[7].Params)!.Shape);

        // Свойства КЭ — как по строкам API «Мирной» (LiraElementStiffnessSourceTests).
        var src = new LiraElementStiffnessSource(byId, data.Units!);
        var shell = src.Shell(new FemElement { ElemType = "shell", StiffnessNum = 1 })!;
        Assert.Equal((3.06e6 * T, 0.2, 0.25), (shell.E, shell.Nu, shell.H));
        var bar = src.Bar(new FemElement { ElemType = "beam", StiffnessNum = 3 })!;
        Assert.Equal(0.15, bar.A, 12);
        Assert.Equal(2.5 * T, src.UnitWeight(new FemElement { ElemType = "beam", StiffnessNum = 3 })!.Value, 6);
        Assert.Null(src.Bar(new FemElement { ElemType = "beam", StiffnessNum = 21 }));
    }

    [Fact]
    public void WithoutAld_RoFromText_NoNamesBlocksFoundation()
    {
        var data = LiraTextSchemaConverter.ToSchemaData(Schema(), null, TonToKn, []);
        var byId = data.Stiffnesses.ToDictionary(s => s.Id);

        Assert.Equal("Пластина H 25", byId[1].Name);
        Assert.StartsWith("Ro:2.5 ", byId[1].Params);
        Assert.StartsWith("Ro:2.5 E:3060000 B:25 H:60", byId[3].Params);   // RO 0,375 т/м ÷ 0,15 м²
        Assert.Empty(data.ConstructiveBlocks);
        Assert.Equal(2393.08 * T, data.PlateFoundationC1[1], 3);              // документ 19 — и без .ald
    }

    [Fact]
    public void BlocksOfImportedElements_FoundationOnlyOfPlates()
    {
        var data = LiraTextSchemaConverter.ToSchemaData(Schema(), ReadAld(), TonToKn, []);

        Assert.Equal([(19, new[] { 1, 2 })], data.ConstructiveBlocks.Select(b => (b.Id, b.ElementIds)));  // КЭ 7 — нет в сетке
        Assert.Equal([1], data.PlateFoundationC1.Keys);                     // КЭ 2 — стержень на основании
        Assert.Equal(0, data.PlateFoundationBeyondC1);
    }

    [Fact]
    public void AldPath_WorkNextToData()
    {
        string root = Path.Combine(Path.GetTempPath(), $"opencs-ald-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "Data"));
            Directory.CreateDirectory(Path.Combine(root, "Work"));
            string txt = Path.Combine(root, "Data", "Схема.txt");
            File.WriteAllText(txt, "");
            Assert.Null(LiraAldFile.PathFor(txt));
            string ald = Path.Combine(root, "Work", "Схема.ald");
            File.WriteAllText(ald, "");
            Assert.Equal(ald, LiraAldFile.PathFor(txt));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData("1 GEO 3.05915e+006 3.05915e+006 0.2 0.2 1.22366e+006 0.2 RO 2.4983 /", "Ro:2.4983 E:3059150 V:0.2 H:20 PLATE_END", null)]
    [InlineData("1 GEO 2.75e+006 0.01 0.2 0.2 1 0.2 RO 1.5 /", null, "ортотропная пластина GEO")]
    [InlineData("1 S100 60 80/ 0 NSG 48/ 0 Mu 0.2/", null, "сечение S100")]
    public void Stiffness_OtherKinds(string record, string? expectedParams, string? expectedProblem)
    {
        var s = LiraTextReader.Read(new StringReader($"( 3/ {record} )"));
        var rec = LiraTextSchemaConverter.Stiffness(s.Stiffnesses[0], LiraTextUnits.Default, null, null, out string? problem);
        if (expectedParams != null) Assert.Equal(expectedParams, rec.Params);
        Assert.Equal(expectedProblem, problem);
    }
}
