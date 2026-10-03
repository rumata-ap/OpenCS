using System.Text.RegularExpressions;
using CScore.Sp16;

namespace CScore.Import;

/// <summary>Ссылка стальной жёсткости ЛИРЫ на сортамент.</summary>
/// <param name="Section">Вид сечения ЛИРЫ («Tubing», «DoubleT», «Angle» …).</param>
/// <param name="File">Файл сортамента («gn-kv94.profiles.srt»).</param>
/// <param name="Shape">Имя профиля в сортаменте («80 x 3»).</param>
/// <param name="SteelMark">Марка стали (<c>Steel = |…|</c>); null — не задана (только <c>MatId = STL</c>).</param>
public sealed record LiraSteelRef(string Section, string File, string Shape, string? SteelMark);

/// <summary>
/// Стальные жёсткости ЛИРЫ (вид 1018): строка <c>Section = Tubing  MatId = STL  Comment = | … |  File = |gn-kv94.profiles.srt|
/// Shape = |80 x 3| …</c> → профиль по сортаменту ЛИРЫ (<see cref="LiraSortamentFile"/>). Соответствие видов и полей
/// записи — спека <c>2026-10-02-imported-steel-sections-design.md</c>, §10.
/// </summary>
public static class LiraSteelProfiles
{
    /// <summary>Код вида жёсткости ЛИРЫ «стальной профиль из сортамента».</summary>
    public const int SteelKindCode = 1018;

    static readonly Regex SectionPattern = new(@"(?<![A-Za-z])Section\s*=\s*([A-Za-z_]+)", RegexOptions.CultureInvariant);
    static readonly Regex FilePattern = new(@"(?<![A-Za-z])File\s*=\s*\|([^|]*)\|", RegexOptions.CultureInvariant);
    static readonly Regex ShapePattern = new(@"(?<![A-Za-z])Shape\s*=\s*\|([^|]*)\|", RegexOptions.CultureInvariant);
    static readonly Regex SteelPattern = new(@"(?<![A-Za-z])Steel\s*=\s*\|([^|]*)\|", RegexOptions.CultureInvariant);

    /// <summary>Жёсткость — стальной профиль сортамента ЛИРЫ.</summary>
    public static bool IsSteel(LiraStiffnessRecord stiffness) => stiffness.KindCode == SteelKindCode;

    /// <summary>Ссылка на сортамент из строки жёсткости; null — вида, файла или имени профиля нет.</summary>
    public static LiraSteelRef? SteelRef(string stiffnessParams)
    {
        var section = SectionPattern.Match(stiffnessParams);
        var file = FilePattern.Match(stiffnessParams);
        var shape = ShapePattern.Match(stiffnessParams);
        if (!section.Success || !file.Success || !shape.Success) return null;
        string fileName = file.Groups[1].Value.Trim(), shapeName = shape.Groups[1].Value.Trim();
        if (fileName.Length == 0 || shapeName.Length == 0) return null;
        var steel = SteelPattern.Match(stiffnessParams);
        string? mark = steel.Success && steel.Groups[1].Value.Trim() is { Length: > 0 } m ? m : null;
        return new LiraSteelRef(section.Groups[1].Value, fileName, shapeName, mark);
    }

    /// <summary>
    /// Разрешает профили всех стальных жёсткостей. Каждый файл сортамента загружается один раз; не найден или
    /// повреждён — у его профилей причина.
    /// </summary>
    /// <param name="stiffnesses">Номер и строка жёсткости вида 1018.</param>
    /// <param name="loadFile">Сортамент по имени файла; null — файла нет. <see cref="InvalidDataException"/> — повреждён.</param>
    public static List<SteelProfileEntry> ResolveAll(IEnumerable<(int Num, string Params)> stiffnesses,
        Func<string, LiraSortamentFile?> loadFile)
    {
        var files = new Dictionary<string, (LiraSortamentFile? File, string? Error)>(StringComparer.OrdinalIgnoreCase);
        var result = new List<SteelProfileEntry>();
        foreach (var (num, text) in stiffnesses)
        {
            if (SteelRef(text) is not { } r)
            {
                result.Add(new(num, "", null, "в строке жёсткости ЛИРЫ нет ссылки на сортамент (File, Shape)"));
                continue;
            }
            string source = $"{r.File}: {r.Shape}";
            if (!files.TryGetValue(r.File, out var loaded))
            {
                try
                {
                    var file = loadFile(r.File);
                    loaded = (file, file == null ? $"нет сортамента ЛИРЫ {r.File}" : null);
                }
                catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
                {
                    loaded = (null, $"сортамент ЛИРЫ {r.File} не прочитан: {ex.Message}");
                }
                files[r.File] = loaded;
            }
            if (loaded.File == null)
            {
                result.Add(new(num, source, null, loaded.Error, r.SteelMark));
                continue;
            }
            if (loaded.File.Find(r.Shape) is not { } row)
            {
                result.Add(new(num, source, null, $"в сортаменте ЛИРЫ {r.File} нет профиля «{r.Shape}»", r.SteelMark));
                continue;
            }
            var (shape, reason) = FromRow(r.Section, loaded.File, row);
            result.Add(new(num, source, shape, reason, r.SteelMark));
        }
        return result;
    }

    /// <summary>См → м без хвостов двоичной арифметики (0,7 см → 0,007 м).</summary>
    static double M(double cm) => Math.Round(cm / 100, 9);

    /// <summary>Профиль вида <paramref name="section"/> по строке сортамента либо причина.</summary>
    public static (ImportedSteelShape? Shape, string? Reason) FromRow(string section, LiraSortamentFile file, LiraSortamentRow row)
    {
        string title = file.Title;
        // Гнутые швеллеры и уголки: полка и стенка одной толщины, закругления полки нет.
        bool bent = row.R2 == 0 && row.Tf1 == row.Tw && row.Tw > 0
                    || title.Contains("гнут", StringComparison.OrdinalIgnoreCase);
        // Уклон внутренних граней полок: двутавры — 12 % (ГОСТ 8239), швеллеры — 10 % (ГОСТ 8240), как в ProfileDB.
        bool sloped = title.Contains("уклон", StringComparison.OrdinalIgnoreCase);

        ImportedSteelShape Make(SteelProfileKind kind, SteelFabrication fabrication,
            double h, double b, double tw, double tf, double r1, double r2 = 0, double slope = 0) =>
            new(kind, fabrication, M(h), M(b), M(tw), M(tf), M(r1), M(r2), slope,
                file.Standard, row.Name, row.A, row.Iy, row.Iz);

        ImportedSteelShape? shape = section switch
        {
            "DoubleT" => Make(SteelProfileKind.IBeam, SteelFabrication.Rolled,
                row.H, row.B1, row.Tw, row.Tf1, row.R1, row.R2, sloped || file.Standard.Contains("8239") ? 0.12 : 0),
            "Channel" when bent => Make(SteelProfileKind.Channel, SteelFabrication.Bent,
                row.H, row.B1, row.Tw, row.Tw, row.R1),
            "Channel" => Make(SteelProfileKind.Channel, SteelFabrication.Rolled,
                row.H, row.B1, row.Tw, row.Tf1, row.R1, row.R2,
                sloped ? 0.10 : 0),
            "Angle" => Make(SteelProfileKind.Angle, bent ? SteelFabrication.Bent : SteelFabrication.Rolled,
                row.H, row.B2, row.Tw, row.Tw, row.R1, bent ? 0 : row.R2),
            "Pipe" => Make(SteelProfileKind.Pipe,
                file.Standard.Contains("8732") || file.Standard.Contains("8731") ? SteelFabrication.Rolled : SteelFabrication.Welded,
                row.H, 0, row.Tw, 0, 0),
            "Tubing" => Make(SteelProfileKind.Box, SteelFabrication.Bent, row.H, row.B1, row.Tw, row.Tf1, row.R1),
            "Round" => Make(SteelProfileKind.Round, SteelFabrication.Rolled, row.H, 0, 0, 0, 0),
            "Square" => Make(SteelProfileKind.Rect, SteelFabrication.Rolled, row.H, row.H, 0, 0, 0),
            // Полоса: Iy сортамента = t·b³/12 — ширина b вдоль Z1 (высота сечения), толщина — вдоль Y1.
            "Rect" or "Sheet" => Make(SteelProfileKind.Rect, SteelFabrication.Rolled, row.H, row.Tw, 0, 0, 0),
            _ => null,
        };
        if (shape == null) return (null, $"вид сечения ЛИРЫ «{section}» не поддерживается");
        if (!(shape.H > 0)) return (null, $"профиль «{row.Name}» сортамента ЛИРЫ: размеры не заданы");
        return (shape, null);
    }
}
