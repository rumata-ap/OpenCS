using System.Collections.Generic;
using System.IO;
using System.Linq;
using CScore.Import;

namespace OpenCS.Services.Scad;

/// <summary>
/// Стальные профили жёсткостей STZ схемы SCAD по сортаментам установленного SCAD: каталог PRF —
/// каталог SCADAPIX.dll (<c>&lt;SCAD&gt;\64</c>).
/// </summary>
static class ScadSteelProfileLoader
{
    /// <summary>Профили жёсткостей STZ; без каталога SCAD — у всех причина «нет сортамента».</summary>
    /// <param name="stiffnesses">Жёсткости схемы SCAD (строка SCAD — в <see cref="LiraStiffnessRecord.Params"/>).</param>
    /// <param name="prfDirectory">Каталог сортаментов SCAD; null — SCAD не найден.</param>
    public static List<SteelProfileEntry> Resolve(IEnumerable<LiraStiffnessRecord> stiffnesses, string? prfDirectory) =>
        ScadSteelProfiles.ResolveAll(
            stiffnesses.Where(s => s.KindCode == ScadStiffnessParams.ScadKindCode).Select(s => (s.Id, s.Params)),
            baseName =>
            {
                if (string.IsNullOrWhiteSpace(prfDirectory)) return null;
                string path = Path.Combine(prfDirectory, baseName + ".PRF");
                return File.Exists(path) ? ScadPrfReader.Read(path) : null;
            });
}
