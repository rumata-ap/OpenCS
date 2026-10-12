using System.Globalization;
using System.Xml;

namespace CScore.Import;

/// <summary>Жёсткость из <c>.ald</c>: удельный (у численных — погонный) вес Ro, как в таблице жёсткостей API, и имя.</summary>
public sealed record LiraAldRigid(double? Ro, string Comment);

/// <summary>
/// Файл проекта ЛИРЫ <c>Work\&lt;Title&gt;.ald</c> (XML) — то, чего нет в текстовом файле схемы: имена и Ro жёсткостей
/// (<c>RigidArray/Rigid</c>) и блоки КЭ (<c>ElemBlockArray/ElemBlock NumFE NumBlk</c>). Упругое основание — документ 19
/// текстового файла (в <c>StrModelInfo</c> состав величин после C1 другой); координационные оси и уровни не читаются.
/// </summary>
public sealed class LiraAldFile
{
    /// <summary>Жёсткости по номеру.</summary>
    public Dictionary<int, LiraAldRigid> Rigids { get; } = [];

    /// <summary>Номер блока по номеру КЭ.</summary>
    public Dictionary<int, int> ElementBlocks { get; } = [];

    /// <summary><c>.ald</c> рядом с текстовым файлом: <c>…\Data\X.txt</c> → <c>…\Work\X.ald</c>, иначе <c>X.ald</c> в том же
    /// каталоге; null — нет.</summary>
    public static string? PathFor(string textPath)
    {
        string name = Path.GetFileNameWithoutExtension(textPath) + ".ald";
        string? dir = Path.GetDirectoryName(Path.GetFullPath(textPath));
        if (dir == null) return null;
        string work = Path.Combine(Path.GetDirectoryName(dir) ?? dir, "Work", name);
        if (File.Exists(work)) return work;
        string near = Path.Combine(dir, name);
        return File.Exists(near) ? near : null;
    }

    /// <summary>Читает файл потоком.</summary>
    public static LiraAldFile Read(string path)
    {
        using var stream = File.OpenRead(path);
        return Read(stream);
    }

    /// <summary>Читает XML <c>.ald</c> из потока.</summary>
    public static LiraAldFile Read(Stream stream)
    {
        var result = new LiraAldFile();
        using var xml = XmlReader.Create(stream, new XmlReaderSettings { IgnoreComments = true, IgnoreWhitespace = true });
        while (xml.Read())
        {
            if (xml.NodeType != XmlNodeType.Element) continue;
            switch (xml.LocalName)
            {
                case "Rigid":
                    if (Int(xml.GetAttribute("Num")) is { } num)
                        result.Rigids[num] = new LiraAldRigid(Double(xml.GetAttribute("Ro")), xml.GetAttribute("Comment")?.Trim() ?? "");
                    break;
                case "ElemBlock":
                    if (Int(xml.GetAttribute("NumFE")) is { } fe && Int(xml.GetAttribute("NumBlk")) is { } blk)
                        result.ElementBlocks[fe] = blk;
                    break;
            }
        }
        return result;
    }

    static int? Int(string? s) =>
        int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : null;

    static double? Double(string? s) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : null;
}
