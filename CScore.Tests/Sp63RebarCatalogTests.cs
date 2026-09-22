using System.Globalization;

using Xunit;

namespace CScore.Tests;

/// <summary>Проверяет нормативное отображение каталога арматуры СП 63 в CSV OpenCS.</summary>
public sealed class Sp63RebarCatalogTests
{
    static readonly string DataSourceDirectory = Path.Combine(AppContext.BaseDirectory, "DataSource");

    [Fact]
    public void FirstGroupUsesTable614DesignStrengthsAndCompressionLimits()
    {
        var a400 = Read("Арматура стальная_C.csv", "A400");
        var a800Short = Read("Арматура стальная_C.csv", "A800");
        var a800Long = Read("Арматура стальная_CL.csv", "A800");

        Assert.Equal(340_000d, a400.Ft);
        Assert.Equal(-340_000d, a400.Fc);
        Assert.Equal(695_000d, a800Short.Ft);
        Assert.Equal(-400_000d, a800Short.Fc);
        Assert.Equal(-500_000d, a800Long.Fc);
    }

    [Fact]
    public void SecondGroupUsesTable613NormativeStrengths()
    {
        var a400 = Read("Арматура стальная_N.csv", "A400");

        Assert.Equal(390_000d, a400.Ft);
        Assert.Equal(-390_000d, a400.Fc);
    }

    [Fact]
    public void CatalogUsesCurrentCableClassNomenclature()
    {
        var rows = ReadRows("Арматура стальная_C.csv");
        var tags = rows.Select(row => row.Tag).ToHashSet(StringComparer.Ordinal);

        Assert.Contains(tags, tag => tag.StartsWith("К1450 ", StringComparison.Ordinal));
        Assert.Contains(tags, tag => tag.StartsWith("К1550 ", StringComparison.Ordinal));
        Assert.Contains(tags, tag => tag.StartsWith("К1650 ", StringComparison.Ordinal));
        Assert.Contains(tags, tag => tag.StartsWith("К1750 ", StringComparison.Ordinal));
        Assert.Contains(tags, tag => tag.StartsWith("К1850 ", StringComparison.Ordinal));
        Assert.DoesNotContain(tags, tag => tag.StartsWith("К1600 ", StringComparison.Ordinal));
        Assert.DoesNotContain(tags, tag => tag.StartsWith("К1700 ", StringComparison.Ordinal));
        Assert.DoesNotContain(tags, tag => tag.StartsWith("К1800 ", StringComparison.Ordinal));
    }

    [Fact]
    public void NewHighStrengthCableClassesUseConditionalYieldTrilinearProfile()
    {
        var k1450 = Read("Арматура стальная_C.csv", "К1450");

        Assert.Equal(1_200_000d, k1450.Ft);
        Assert.Equal(-400_000d, k1450.Fc);
        Assert.Equal(195_000_000d, k1450.E);
        Assert.Equal(3d, k1450.Type);
        Assert.Equal(0.015d, k1450.Et2);
        Assert.Equal(-0.00815384615384615d, k1450.Ec0, 12);
        Assert.Equal(-0.00553846153846154d, k1450.Ec1, 12);
    }

    static CsvRow Read(string fileName, string grade)
    {
        return ReadRows(fileName).Single(row =>
            row.Tag.Equals(grade, StringComparison.Ordinal) ||
            row.Tag.StartsWith(grade + " ", StringComparison.Ordinal));
    }

    static IReadOnlyList<CsvRow> ReadRows(string fileName)
    {
        var path = Path.Combine(DataSourceDirectory, fileName);
        var lines = File.ReadAllLines(path);
        Assert.NotEmpty(lines);

        var header = lines[0].Split(';');
        var index = header
            .Select((name, position) => (name, position))
            .ToDictionary(item => item.name, item => item.position, StringComparer.Ordinal);

        return lines.Skip(1)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line =>
            {
                var fields = line.Split(';');
                return new CsvRow(
                    fields[index["Tag"]],
                    Parse(fields[index["Fc"]]),
                    Parse(fields[index["Ft"]]),
                    Parse(fields[index["E"]]),
                    Parse(fields[index["Ec0"]]),
                    Parse(fields[index["Ec1"]]),
                    Parse(fields[index["Et2"]]),
                    Parse(fields[index["Type"]]));
            })
            .ToArray();
    }

    static double Parse(string value) => double.Parse(value, CultureInfo.InvariantCulture);

    sealed record CsvRow(
        string Tag,
        double Fc,
        double Ft,
        double E,
        double Ec0,
        double Ec1,
        double Et2,
        double Type);
}
