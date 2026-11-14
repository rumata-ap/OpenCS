using System.IO.Compression;
using System.Text;
using CScore.Import;
using Xunit;

namespace CScore.Tests;

/// <summary>Разбор таблиц усилий стержней (консоль opencs import forces): заголовки, оси ЛИРА-САПР, единицы, строки.</summary>
public class ForceTableParserTests
{
    static ForceTable Table(params string[][] rows) => new("Т", rows.Select(r => (IReadOnlyList<string>)r).ToList());

    static ForceTableResult Parse(ForceTable table, ForceTableOptions? options = null) =>
        ForceTableParser.Parse([table], options ?? new ForceTableOptions());

    /// <summary>Как «РСУ в стержнях.docx» ЛИРА-САПР 10: «НC» с латинской C, Qz пустая ячейка.</summary>
    static ForceTable LiraRsu() => Table(
        ["Таблица 1"],
        ["Элем", "НC", "Ст.", "Гр.", "Крит.", "N (кН)", "My (кН*м)", "Qz (кН)"],
        ["1", "1", "1", "А", "2", "-176", "-48.313", "24.202"],
        ["", "3", "1", "А", "6", "-176", "24,294", "24.202"],
        ["3", "3", "1", "А", "1", "-24.202", "114.6", ""]);

    [Fact]
    public void LiraHeaders_AutoAxes_MapToOpenCs()
    {
        var r = Parse(LiraRsu());

        Assert.True(r.Success, r.Error);
        Assert.Equal(ForceTableAxes.Lira, r.Axes);
        var items = r.Set!.Items;
        Assert.Equal(3, items.Count);
        Assert.Equal(-176, items[0].N);
        Assert.Equal(48.313, items[0].Mx, 9);   // Mx ← −My ЛИРЫ
        Assert.Equal(24.202, items[0].Vy, 9);   // Vy ← Qz
        Assert.Equal(0, items[0].My);
        Assert.Equal((1, 1), (items[0].SourceElementNum, items[0].SourceSectionNum));
        Assert.Equal("Эл. 1, сеч. 1, Ст. 1, Гр. А, Крит. 2", items[0].Label);
        Assert.Equal(-24.294, items[1].Mx, 9);   // десятичная запятая
        Assert.Equal((1, 3), (items[1].SourceElementNum, items[1].SourceSectionNum));   // элемент сверху
        Assert.Equal(0, items[2].Vy);   // пустая ячейка — 0
        Assert.Equal([1, 2, 3], items.Select(i => i.Num));
        Assert.Contains("My (кН*м) → Mx (−)", r.Columns);
    }

    [Fact]
    public void OpenCsAxes_ByHeaders_AndTonnes()
    {
        var r = Parse(Table(
            ["КЭ", "Сечение", "N, т", "Mx (т*м)", "My (тм)", "Vx", "Vy", "T"],
            ["5", "1", "-10", "2", "1", "0.5", "-0.5", "0.1"]),
            new ForceTableOptions { DefaultScale = 1 });

        Assert.True(r.Success, r.Error);
        Assert.Equal(ForceTableAxes.OpenCs, r.Axes);
        var item = Assert.Single(r.Set!.Items);
        Assert.Equal(-10 * 9.80665, item.N, 9);   // «N, т» — единицы после запятой
        Assert.Equal(2 * 9.80665, item.Mx, 9);
        Assert.Equal(9.80665, item.My, 9);
        Assert.Equal(0.5, item.Vx);
        Assert.Equal(-0.5, item.Vy);
        Assert.Equal(0.1, item.T);
    }

    [Fact]
    public void LiraFullSet_TorsionAndBothPlanes()
    {
        var r = Parse(Table(
            ["Элемент", "N", "Mk", "My", "Mz", "Qy", "Qz"],
            ["7", "1", "2", "3", "4", "5", "6"]));

        var item = Assert.Single(r.Set!.Items);
        Assert.Equal((1.0, 2.0, -3.0, -4.0, 5.0, 6.0), (item.N, item.T, item.Mx, item.My, item.Vx, item.Vy));
    }

    [Fact]
    public void ElementsFilter_LabelColumn_SubheaderRowsSkipped()
    {
        var r = Parse(Table(
            ["Метка", "Элем", "N", "My", "Qz"],
            ["Стержни", "", "", "", ""],
            ["а", "1", "1", "1", "1"],
            ["б", "2", "2", "2", "2"],
            ["в", "3", "3", "3", "3"]),
            new ForceTableOptions { Elements = new HashSet<int> { 1, 3 } });

        Assert.True(r.Success, r.Error);
        Assert.Equal(["а", "в"], r.Set!.Items.Select(i => i.Label));
    }

    [Fact]
    public void Errors_NoHeader_BadNumber_WrongAxesColumn_BadUnits()
    {
        Assert.False(Parse(Table(["a", "b"], ["1", "2"])).Success);
        Assert.Contains("не число", Parse(Table(["Элем", "N", "My"], ["1", "1", "1"], ["2", "x", "1"])).Error);
        Assert.False(Parse(Table(["Элем", "N", "Mz"], ["1", "1", "1"]), new ForceTableOptions { Axes = ForceTableAxes.OpenCs }).Success);
        Assert.Contains("единицы", Parse(Table(["Элем", "N (кгс)"], ["1", "1"])).Error);
        Assert.False(Parse(Table(["Элем", "N"], ["1", "1"]), new ForceTableOptions { Elements = new HashSet<int> { 9 } }).Success);
    }

    [Fact]
    public void Delimited_SemicolonWithDecimalComma_AndTabs()
    {
        var semi = ForceTableReader.ReadDelimited("Элем;N (кН);My\r\n1;-1,5;2\r\n", "a.csv");
        var r = Parse(semi);
        Assert.True(r.Success, r.Error);
        Assert.Equal(-1.5, r.Set!.Items[0].N);

        var tab = ForceTableReader.ReadDelimited("Элем\tN\n\"1\"\t3\n", "a.tsv");
        Assert.Equal(3, Parse(tab).Set!.Items[0].N);
    }

    [Fact]
    public void Decode_Utf8OrWindows1251()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Assert.Equal("Элем", ForceTableReader.DecodeText(Encoding.GetEncoding(1251).GetBytes("Элем")));
        Assert.Equal("Элем", ForceTableReader.DecodeText([0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("Элем")]));
    }

    [Fact]
    public void Docx_TablesWithGridSpanAndVMerge()
    {
        const string w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        static string Tc(string text, string props = "") =>
            $"<w:tc><w:tcPr>{props}</w:tcPr><w:p><w:r><w:t>{text}</w:t></w:r></w:p></w:tc>";
        string xml = $"""
            <w:document xmlns:w="{w}"><w:body>
            <w:p><w:r><w:t>Таблица 1 - РСУ в стержнях</w:t></w:r></w:p>
            <w:tbl>
              <w:tr>{Tc("Элем", "<w:vMerge w:val=\"restart\"/>")}{Tc("НC")}{Tc("N (кН)", "<w:gridSpan w:val=\"2\"/>")}{Tc("Qz (кН)")}</w:tr>
              <w:tr>{Tc("", "<w:vMerge/>")}{Tc("1")}{Tc("-176")}{Tc("")}{Tc("24.202")}</w:tr>
            </w:tbl></w:body></w:document>
            """;
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        using (var writer = new StreamWriter(zip.CreateEntry("word/document.xml").Open()))
            writer.Write(xml);
        stream.Position = 0;

        var table = Assert.Single(ForceTableReader.ReadDocx(stream));
        Assert.Equal(["Элем", "НC", "N (кН)", "", "Qz (кН)"], table.Rows[0]);
        Assert.Equal(["Элем", "1", "-176", "", "24.202"], table.Rows[1]);
    }
}
