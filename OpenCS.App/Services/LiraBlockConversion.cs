using CScore.Fem;
using CScore.Import;
using OpenCS.Utilites;

namespace OpenCS.Services;

/// <summary>
/// кБ ЛИРЫ → конструктивные элементы схемы (стержни — по прямым цепочкам, пластины — плоскими элементами с контуром);
/// сетка ЛИРЫ не меняется, элементы получают замок сетки. Общее для окна «КонЭ из кБ» и консоли (<c>convert blocks</c>).
/// КЭ, уже принадлежащие элементу, не входящему в выбранные кБ, пропускаются с предупреждением; кБ с непредвиденной
/// геометрией — ошибка в журнал, остальные преобразуются.
/// </summary>
public static class LiraBlockConversion
{
    /// <summary>Преобразует кБ; итог — строка для журнала, null — ни один кБ не преобразован.</summary>
    /// <exception cref="InvalidOperationException">Запись отклонена (например, КЭ заняты элементом схемы).</exception>
    public static string? Convert(DatabaseService db, ILogService log, FemSchema schema, IReadOnlyList<LiraBlockInfo> selected)
    {
        var meshNodes = db.GetFemMeshNodes(schema.Id);
        var meshElements = db.GetFemMeshElements(schema.Id);
        var members = db.GetFemMembers(schema.Id);
        var replacedTags = members
           .Where(m => selected.Any(b => MeshMemberBuilder.IsPartTag(m.ElemTag, b.Tag)))
           .Select(m => m.ElemTag).ToHashSet(StringComparer.Ordinal);
        var ownerByElement = meshElements
           .Where(e => e.SourceMemberTag != null && !replacedTags.Contains(e.SourceMemberTag))
           .ToDictionary(e => e.ElemTag, e => e.SourceMemberTag!, StringComparer.Ordinal);
        var claimed = new HashSet<string>(StringComparer.Ordinal);
        var builds = new List<MeshMemberBuild>();
        var converted = new List<LiraBlockInfo>();
        foreach (var block in selected)
        {
           var free = new List<string>();
           var busy = new List<string>();
           foreach (var tag in block.ElementTags)
              (ownerByElement.ContainsKey(tag) || !claimed.Add(tag) ? busy : free).Add(tag);
           if (busy.Count > 0)
              log.Warning(string.Format(Loc.S("LiraBlocksElementsBusy"), block.Tag, busy.Count,
                 string.Join(", ", busy.Take(20))));
           MeshMemberBuild build;
           try
           {
              build = LiraBlockMemberBuilder.Build(block with { ElementTags = free }, meshNodes, meshElements);
           }
           catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
           {
              // Непредвиденная геометрия одного кБ не должна срывать остальные; его прежние элементы не трогаем.
              log.Error(string.Format(Loc.S("LiraBlocksBuildFailed"), block.Tag, ex.Message));
              continue;
           }
           foreach (var d in build.Diagnostics)
              log.Warning(d.Message);
           builds.Add(build);
           converted.Add(block);
        }
        if (converted.Count == 0) return null;
        selected = converted;

        db.ApplyLiraBlockMembers(schema.Id, selected, builds); // занятые КЭ/конфликт — InvalidOperationException

        var parts = builds.SelectMany(b => b.Parts).ToList();
        string done = string.Format(Loc.S("LiraBlocksConverted"), selected.Count,
           parts.Count(p => p.Member.ElemType == "beam"), parts.Count(p => p.Member.ElemType == "shell"),
           builds.Sum(b => b.Diagnostics.Count));
        log.Info(done);
        return done;
    }
}
