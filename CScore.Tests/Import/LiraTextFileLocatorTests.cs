using CScore.Fem;
using CScore.Import;
using Xunit;

namespace CScore.Tests.Import;

/// <summary>Поиск текстового файла схемы ЛИРЫ по версиям и сверка сетки схемы с файлом (срез 5 плана 4б ЛИРЫ + 4ж).</summary>
public sealed class LiraTextFileLocatorTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), $"opencs-lira-data-{Guid.NewGuid():N}");

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    string Put(string versionDir, string file, DateTime written)
    {
        string dir = Path.Combine(_root, versionDir, "Data");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, file);
        File.WriteAllText(path, "( 0/ )");
        File.SetLastWriteTimeUtc(path, written);
        return path;
    }

    [Fact]
    public void DataDirectories_AllVersionsByMask_IgnoringOtherProducts()
    {
        Put(@"LIRA SAPR\LIRA SAPR 2013", "a.txt", DateTime.UtcNow);
        Put(@"LIRA SAPR\LIRA SAPR 2024", "a.txt", DateTime.UtcNow);
        Put(@"LIRASERVICE\LIRA-SAPPHIRE 2025", "a.txt", DateTime.UtcNow);
        Put(@"LIRASERVICE\KS SAPPHIRE 2025", "a.txt", DateTime.UtcNow);
        Directory.CreateDirectory(Path.Combine(_root, "LIRA SAPR", "LIRA SAPR 2022")); // без Data

        var dirs = LiraTextFileLocator.DataDirectories(_root);

        Assert.Equal([2013, 2024, 2025], dirs.Select(d => d.Version ?? 0));
        Assert.All(dirs, d => Assert.EndsWith("Data", d.Path));
        Assert.Empty(LiraTextFileLocator.DataDirectories(Path.Combine(_root, "нет")));
    }

    [Fact]
    public void Find_PrefersOpenLiraVersion_ElseNewest()
    {
        var t0 = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        string v2013 = Put(@"LIRA SAPR\LIRA SAPR 2013", "Схема.txt", t0);
        string v2024 = Put(@"LIRA SAPR\LIRA SAPR 2024", "Схема.txt", t0.AddDays(2));
        string v2025 = Put(@"LIRASERVICE\LIRA-SAPPHIRE 2025", "Схема.txt", t0.AddDays(1));
        var dirs = LiraTextFileLocator.DataDirectories(_root);

        Assert.Equal(v2013, LiraTextFileLocator.Find("Схема", dirs, 2013));
        Assert.Equal(v2025, LiraTextFileLocator.Find("Схема", dirs, 2025));
        Assert.Equal(v2024, LiraTextFileLocator.Find("Схема", dirs, null));
        Assert.Equal(v2024, LiraTextFileLocator.Find("Схема", dirs, 2022)); // версии без файла — самый свежий
        Assert.Equal(v2024, LiraTextFileLocator.Find(" Схема.lir ", dirs, null));
        Assert.Null(LiraTextFileLocator.Find("Другая", dirs, 2025));
        Assert.Null(LiraTextFileLocator.Find("", dirs, 2025));
        Assert.Null(LiraTextFileLocator.Find("a|b", dirs, 2025));
    }

    [Theory]
    [InlineData("LIRA SAPR 2013", 2013)]
    [InlineData("LIRA-SAPPHIRE 2025", 2025)]
    [InlineData("LIRA SAPR 2013 R5", 2013)]
    [InlineData("LIRA SAPR", null)]
    public void Version_FromDirectoryName(string name, int? version) =>
        Assert.Equal(version, LiraTextFileLocator.Version(name));

    static (List<FemMeshNode> Nodes, List<FemElement> Elements) Mesh(LiraTextSchema s)
    {
        double lu = s.Units.LengthUnitM;
        var nodes = s.Nodes.Select((p, i) => new FemMeshNode
        {
            NodeTag = (i + 1).ToString(), X = p.X * lu, Y = p.Y * lu, Z = p.Z * lu, Origin = FemMember.MeshSourceImported,
        }).ToList();
        var elements = s.Elements.Where(e => e.Nodes.Length is 2 or 3 or 4).Select(e => new FemElement
        {
            ElemTag = e.Number.ToString(), ElemType = e.Nodes.Length == 2 ? "beam" : "shell",
            NodeIdsJson = System.Text.Json.JsonSerializer.Serialize(e.Nodes), Origin = FemMember.MeshSourceImported,
        }).ToList();
        return (nodes, elements);
    }

    static LiraTextSchema Wall() =>
        LiraTextReader.Read(Path.Combine(AppContext.BaseDirectory, "Import", "Fixtures", "lira-text-wall.txt"));

    [Fact]
    public void MeshMatch_SameSchema_Ok_ManualObjectsIgnored()
    {
        var s = Wall();
        var (nodes, elements) = Mesh(s);
        nodes.Add(new FemMeshNode { NodeTag = "m1", X = 100, Origin = "generated" });
        elements.Add(new FemElement { ElemTag = "999999", ElemType = "beam", NodeIdsJson = "[1,2]", Origin = "generated" });

        var m = LiraTextMeshMatch.Compare(s, nodes, elements);

        Assert.True(m.Ok, m.Example);
        Assert.Equal(s.Nodes.Count, m.Nodes);
        Assert.True(m.Elements > 0);
    }

    [Fact]
    public void MeshMatch_OtherSchema_Reported()
    {
        var s = Wall();
        var (nodes, elements) = Mesh(s);
        nodes[4].Z += 0.01;
        elements[0].NodeIdsJson = "[1,2,3,4]";
        nodes.Add(new FemMeshNode { NodeTag = (s.Nodes.Count + 1).ToString(), Origin = FemMember.MeshSourceImported });

        var m = LiraTextMeshMatch.Compare(s, nodes, elements);

        Assert.False(m.Ok);
        Assert.Equal(1, m.Missing);
        Assert.Equal(2, m.Different);
        Assert.StartsWith("узел 5", m.Example);
    }
}
