using CScore.Import;
using CScore.ParametricSteel;
using Xunit;
using Xunit.Abstractions;

namespace OpenCS.Tests;

/// <summary>
/// Ручная проверка читателя сортаментов ЛИРЫ на установленной ЛИРЕ. OPENCS_LIRA_SRT_DIR — каталог *.srt
/// (<c>%PUBLIC%\Documents\LIRA SAPR\DataBase</c>); без переменной тест сразу выходит. Файлы *.srt в репозиторий не
/// кладутся. Вид профиля в файле не записан (он — в строке жёсткости), поэтому здесь он угадывается по названию.
/// </summary>
public class LiraSortamentManualTests(ITestOutputHelper output)
{
    static string? Dir() => Environment.GetEnvironmentVariable("OPENCS_LIRA_SRT_DIR") is { Length: > 0 } d ? d : null;

    /// <summary>Вид сечения ЛИРЫ по названию сортамента; null — составные, гнутые С/Z, тавры и прочее.</summary>
    static string? SectionOf(string title)
    {
        string t = title.ToLowerInvariant();
        if (t.Contains("тавр") && !t.Contains("двутавр")) return null;
        if (t.Contains("с-образн") || t.Contains("z-образн") || t.Contains("профиль") && !t.Contains("профили стальные")) return null;
        if (t.Contains("двутавр")) return "DoubleT";
        if (t.Contains("швеллер")) return "Channel";
        if (t.Contains("уголк") || t.Contains("углов")) return "Angle";
        if (t.Contains("замкнут") || t.Contains("квадратн") && t.Contains("труб") || t.Contains("прямоугольн") && t.Contains("труб")) return "Tubing";
        if (t.Contains("труб")) return "Pipe";
        if (t.Contains("кругл")) return "Round";
        if (t.Contains("квадрат")) return "Square";
        if (t.Contains("лист") || t.Contains("полос")) return "Sheet";
        return null;
    }

    [Fact]
    public void AllFiles_Read_BuildSections_AreaAndInertiaWithinTolerance()
    {
        if (Dir() is not { } dir) return;
        int files = 0, built = 0, areaWarned = 0, inertiaWarned = 0, failed = 0, skipped = 0;
        foreach (string path in Directory.GetFiles(dir, "*.srt", SearchOption.TopDirectoryOnly))
        {
            LiraSortamentFile file;
            try { file = LiraSortamentReader.Read(path); }
            catch (InvalidDataException ex) { output.WriteLine($"{Path.GetFileName(path)}: {ex.Message}"); failed++; continue; }
            files++;
            if (Path.GetFileName(path).Contains(".steels.", StringComparison.OrdinalIgnoreCase)
                || Path.GetFileName(path).Contains(".aluminium.", StringComparison.OrdinalIgnoreCase)
                || SectionOf(file.Title) is not { } section)
            {
                skipped++;
                continue;
            }
            int fileArea = 0, fileInertia = 0, fileFailed = 0;
            string? sample = null;
            foreach (var row in file.Rows)
            {
                var (shape, reason) = LiraSteelProfiles.FromRow(section, file, row);
                if (shape == null) { fileFailed++; sample ??= reason; continue; }
                var profile = new ImportedBarProfile(1, ImportedBarMaterial.Steel, ImportedBarShape.SteelSection, shape.B, shape.H, row.Name, shape);
                var r = SteelSectionBuilder.Build(profile, 1, null);
                if (r.Definition == null) { fileFailed++; sample ??= r.Reason; continue; }
                built++;
                if (r.Warning != null) { fileArea++; sample ??= r.Warning; }
                if (ParametricSteelSectionGenerator.BuildCanonicalContour(r.Definition, 8) is var (outer, holes) && row.Iy > 0 && row.Iz > 0)
                {
                    var (ix, iy) = Inertia(outer, holes);
                    if (Math.Abs(ix - row.Iy) > 0.03 * row.Iy || Math.Abs(iy - row.Iz) > 0.03 * row.Iz)
                    {
                        fileInertia++;
                        sample ??= $"«{row.Name}»: Iy {ix:0.###} / {row.Iy:0.###}, Iz {iy:0.###} / {row.Iz:0.###} см⁴";
                    }
                }
            }
            areaWarned += fileArea; inertiaWarned += fileInertia; failed += fileFailed;
            if (fileArea + fileInertia + fileFailed > 0)
                output.WriteLine($"{Path.GetFileName(path)} [{section}] «{file.Title}»: A {fileArea}, I {fileInertia}, ошибок {fileFailed} из {file.Rows.Count}; {sample}");
        }
        output.WriteLine($"Итого: файлов {files} (пропущено {skipped}), построено {built}, расхождение A {areaWarned}, I {inertiaWarned}, ошибок {failed}");
        Assert.True(built > 3000);
    }

    [Fact]
    public void Turkestan_GnKv94_Rows()
    {
        if (Dir() is not { } dir) return;
        var file = LiraSortamentReader.Read(Path.Combine(dir, "GN-KV94.profiles.srt"));
        Assert.Equal("ГОСТ 30245-94", file.Standard);
        foreach (string name in new[] { "80 x 3", "80 x 5", "50 x 4" })
        {
            var row = file.Find(name)!;
            var (shape, reason) = LiraSteelProfiles.FromRow("Tubing", file, row);
            Assert.Null(reason);
            output.WriteLine($"{name}: H {shape!.H} t {shape.Tw} R1 {shape.R1} A {row.A} Iy {row.Iy} It {row.It}");
        }
    }

    /// <summary>Моменты инерции контура относительно центральных осей x, y, см⁴ (контур — в м).</summary>
    static (double Ix, double Iy) Inertia(IReadOnlyList<(double X, double Y)> outer, IReadOnlyList<IReadOnlyList<(double X, double Y)>> holes)
    {
        double a = 0, sx = 0, sy = 0, ixx = 0, iyy = 0;
        void Add(IReadOnlyList<(double X, double Y)> ring, double sign)
        {
            double ra = 0, rsx = 0, rsy = 0, rixx = 0, riyy = 0;
            for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
            {
                var (x0, y0) = ring[j]; var (x1, y1) = ring[i];
                double c = x0 * y1 - x1 * y0;
                ra += c / 2;
                rsx += (x0 + x1) * c / 6;
                rsy += (y0 + y1) * c / 6;
                rixx += (y0 * y0 + y0 * y1 + y1 * y1) * c / 12;
                riyy += (x0 * x0 + x0 * x1 + x1 * x1) * c / 12;
            }
            double s = sign * Math.Sign(ra);
            a += s * ra; sx += s * rsx; sy += s * rsy; ixx += s * rixx; iyy += s * riyy;
        }
        Add(outer, 1);
        foreach (var h in holes) Add(h, -1);
        double xc = sx / a, yc = sy / a;
        return ((ixx - a * yc * yc) * 1e8, (iyy - a * xc * xc) * 1e8);
    }
}
