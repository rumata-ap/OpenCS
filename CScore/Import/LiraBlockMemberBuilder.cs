using System.Text.Json;
using CScore.Fem;
using CScore.Planar;

namespace CScore.Import;

/// <summary>Конструктивный блок ЛИРЫ в терминах сохранённой схемы: номер, тип, этаж, марка и теги КЭ сетки.</summary>
public sealed record LiraBlockInfo(int Id, string Type, string Floor, string Mark, IReadOnlyList<string> ElementTags)
{
    /// <summary>Имя блока — то же, что у группы КЭ по кБ: «{Тип} №{N} [{Этаж}] {Марка}».</summary>
    public string Tag => LiraBlockTags.Format(Id, Type, Floor, Mark);
}

/// <summary>Имена кБ ЛИРЫ: общий формат для групп КЭ и конструктивных элементов и его обратный разбор.</summary>
public static class LiraBlockTags
{
    /// <summary>«{Тип} №{N}», затем « [{Этаж}]» и « {Марка}», если заданы.</summary>
    public static string Format(int id, string type, string floor, string mark)
    {
        var tag = string.IsNullOrWhiteSpace(floor) ? $"{type} №{id}" : $"{type} №{id} [{floor}]";
        if (!string.IsNullOrWhiteSpace(mark))
            tag += $" {mark}";
        return tag;
    }

    static readonly System.Text.RegularExpressions.Regex TagPattern =
        new(@"^(?<type>.*?) №(?<id>\d+)(?: \[(?<floor>[^\]]*)\])?(?: (?<mark>.+))?$");

    /// <summary>Разбирает имя группы кБ обратно (запасной путь для схем, импортированных до сохранения
    /// таблицы кБ). False — имя не в формате <see cref="Format"/>.</summary>
    public static bool TryParse(string tag, out int id, out string type, out string floor, out string mark)
    {
        id = 0; type = floor = mark = "";
        var m = TagPattern.Match(tag ?? "");
        if (!m.Success || !int.TryParse(m.Groups["id"].Value, out id)) return false;
        type  = m.Groups["type"].Value;
        floor = m.Groups["floor"].Value;
        mark  = m.Groups["mark"].Value;
        return true;
    }
}

/// <summary>Преобразование кБ ЛИРЫ в конструктивные элементы: имя — имя кБ, вид плоских частей — по типу кБ.</summary>
public static class LiraBlockMemberBuilder
{
    public static MeshMemberBuild Build(
        LiraBlockInfo block, IReadOnlyList<FemMeshNode> meshNodes, IReadOnlyList<FemElement> meshElements)
    {
        ArgumentNullException.ThrowIfNull(block);
        return MeshMemberBuilder.Build(
            new MeshMemberRequest(block.Tag, KindByBlockType(block.Type), block.ElementTags, "import"), meshNodes, meshElements);
    }

    /// <summary>Kind по типу кБ; null — тип не говорит, плита это или стена.</summary>
    public static string? KindByBlockType(string? type)
    {
        var t = (type ?? "").ToUpperInvariant();
        if (t.Contains("СТЕН") || t.Contains("ДИАФРАГМ") || t.Contains("ПИЛОН")) return "wall";
        if (t.Contains("ПЛИТ") || t.Contains("ПЕРЕКРЫТ") || t.Contains("ПОКРЫТ") || t.Contains("РОСТВЕРК")) return "plate";
        return null;
    }
}
