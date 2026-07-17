using CScore.Import;
using Xunit;

namespace CScore.Tests;

public class ScadShellThicknessParseTests
{
    [Fact]
    public void ParseText_GeRecord_ExtractsThicknessMeters()
    {
        const string text =
            "(0;Version=21;SubVersion=1/1;\"T\";/)" +
            "(1/44 2 1 2 3 4 /)" +
            "(3/2 GE 1.8e+06 0.2 0.22 RO 2.5 Name \"Плита\"/)" +
            "(4/0 0 0/1 0 0/1 1 0/0 1 0/)";

        var r = ScadTextParser.ParseText(text);
        Assert.True(r.Success, r.Error);
        var shell = Assert.Single(r.Data!.Stiffnesses);
        Assert.Equal(ScadStiffnessKind.Shell, shell.Kind);
        Assert.Equal(0.22, shell.ThicknessM);
    }

    [Fact]
    public void Converter_CopiesThicknessToFemMember()
    {
        var data = new ScadSchemaData();
        data.Stiffnesses.Add(new ScadStiffnessRecord(1, "Плита", ScadStiffnessKind.Shell, 0.25));
        data.Elements.Add(new ScadElementRecord(10, 44, 1, [1, 2, 3, 4]));
        var elems = ScadSchemaConverter.ToFemMembers(data, schemaId: 1);
        Assert.Equal(0.25, Assert.Single(elems).ThicknessM);
    }
}
