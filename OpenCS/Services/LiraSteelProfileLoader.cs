using System.Diagnostics;
using System.IO;
using CScore.Import;

namespace OpenCS.Services;

/// <summary>
/// Стальные профили жёсткостей вида 1018 схемы ЛИРЫ по сортаментам установленной ЛИРЫ (*.profiles.srt):
/// <c>%PUBLIC%\Documents\LIRA SAPR\DataBase</c> (ЛИРА-САПР) и <c>%PUBLIC%\Documents\LIRASERVICE\DataBase</c>
/// (ЛИРА-САПФИР); при запущенной САПФИР её каталог проверяется первым.
/// </summary>
static class LiraSteelProfileLoader
{
    /// <summary>Каталоги сортаментов ЛИРЫ, существующие на этом ПК, в порядке поиска.</summary>
    public static List<string> SortamentDirectories()
    {
        string documents = System.Environment.GetFolderPath(System.Environment.SpecialFolder.CommonDocuments);
        string sapr = Path.Combine(documents, "LIRA SAPR", "DataBase");
        string sapphire = Path.Combine(documents, "LIRASERVICE", "DataBase");
        bool sapphireRunning = Process.GetProcessesByName("LS").Length > 0;
        return (sapphireRunning ? new[] { sapphire, sapr } : new[] { sapr, sapphire })
            .Where(Directory.Exists).ToList();
    }

    /// <summary>Профили стальных жёсткостей; нет файла сортамента — у его профилей причина.</summary>
    /// <param name="stiffnesses">Жёсткости схемы ЛИРЫ.</param>
    /// <param name="directories">Каталоги сортаментов в порядке поиска.</param>
    public static List<SteelProfileEntry> Resolve(IEnumerable<LiraStiffnessRecord> stiffnesses, IReadOnlyList<string> directories) =>
        LiraSteelProfiles.ResolveAll(
            stiffnesses.Where(LiraSteelProfiles.IsSteel).Select(s => (s.Id, s.Params)),
            fileName =>
            {
                if (Path.GetFileName(fileName) != fileName) return null;
                foreach (string dir in directories)
                {
                    string path = Path.Combine(dir, fileName);
                    if (File.Exists(path)) return LiraSortamentReader.Read(path);
                }
                return null;
            });
}
