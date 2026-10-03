using CScore.Import;
using CScore.ParametricSteel;
using Xunit;
using Xunit.Abstractions;

namespace OpenCS.Tests;

/// <summary>
/// Ручная проверка читателя сортаментов SCAD на установленном SCAD. OPENCS_SCAD_PRF_DIR — каталог PRF
/// (<c>…\SCADOffice2023\64</c>); без переменной тест сразу выходит. Файлы PRF в репозиторий не кладутся.
/// </summary>
public class ScadPrfManualTests(ITestOutputHelper output)
{
    static string? Dir() => Environment.GetEnvironmentVariable("OPENCS_SCAD_PRF_DIR") is { Length: > 0 } d ? d : null;

    [Fact]
    public void AllFiles_Read_And_ModelRowsMatch()
    {
        if (Dir() is not { } dir) return;
        foreach (string path in Directory.GetFiles(dir, "*.prf", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var file = ScadPrfReader.Read(path);
                output.WriteLine($"{Path.GetFileName(path)}: «{file.Title}», таблиц {file.Tables.Count}, строк {file.Tables.Sum(t => t.Rows.Count)}");
            }
            catch (InvalidDataException ex)
            {
                output.WriteLine($"{Path.GetFileName(path)}: {ex.Message}");
            }
        }

        // Жёсткости модели 111.SPR: STZ ASCHM d1 11 и STZ RUSSIAN okv2012 59.
        var aschm = ScadPrfReader.Read(Path.Combine(dir, "ASCHM.PRF"));
        var russian = ScadPrfReader.Read(Path.Combine(dir, "RUSSIAN.PRF"));
        Assert.Equal("25Б1", aschm.Find("d1", 11)!.Value.Row.Name);
        Assert.Equal("100x4", russian.Find("okv2012", 59)!.Value.Row.Name);
        Assert.Equal(93, russian.Tables.Count);
    }

    [Fact]
    public void Russian_AllRows_BuildSections_AreaWithinTolerance()
    {
        if (Dir() is not { } dir) return;
        var file = ScadPrfReader.Read(Path.Combine(dir, "RUSSIAN.PRF"));
        int built = 0, unsupported = 0, failed = 0, warned = 0;
        foreach (var table in file.Tables)
        {
            int tableWarned = 0, tableFailed = 0;
            string? sample = null;
            for (int i = 1; i <= table.Rows.Count; i++)
            {
                var (shape, reason) = ScadSteelProfiles.Resolve(file, table.Code, i);
                if (shape == null)
                {
                    if (reason!.Contains("не поддерживается")) { unsupported++; break; }
                    failed++; tableFailed++; sample ??= reason;
                    continue;
                }
                var profile = new ImportedBarProfile(1, ImportedBarMaterial.Steel, ImportedBarShape.SteelSection,
                    shape.B, shape.H, shape.Name, shape);
                var r = SteelSectionBuilder.Build(profile, 1, null);
                if (r.Definition == null) { failed++; tableFailed++; sample ??= r.Reason; continue; }
                Assert.Empty(ParametricSteelSectionGenerator.Generate(r.Definition).Diagnostics);
                built++;
                if (r.Warning != null) { warned++; tableWarned++; sample ??= r.Warning; }
            }
            if (tableWarned + tableFailed > 0)
                output.WriteLine($"{table.Code} «{table.Title}»: предупреждений {tableWarned}, ошибок {tableFailed} из {table.Rows.Count}; {sample}");
        }
        output.WriteLine($"Итого: построено {built}, с предупреждением {warned}, ошибок {failed}, таблиц не поддерживается {unsupported}");
        Assert.True(built > 5000);
    }
}
