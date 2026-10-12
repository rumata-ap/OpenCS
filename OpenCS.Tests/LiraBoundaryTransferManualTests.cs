using CScore.Fem;
using CScore.Import;
using OpenCS.Services;
using Xunit;
using Xunit.Abstractions;

namespace OpenCS.Tests;

/// <summary>
/// Ручной прогон переноса ГУ ЛИРЫ (срез 2 плана 4б ЛИРЫ + 4ж, 12.10): текстовый файл OPENCS_LIRA_BC_TXT (пути через
/// «;»). Если в ЛИРЕ открыта схема с тем же именем, что у файла, — сверка номеров: узлы и КЭ импорта (API) против
/// документов 1 и 4 (тип, узлы в порядке, координаты), закрепления таблицы 4 против документа 5; сетка — из импорта.
/// Иначе сетка — все узлы документа 4 и стержни/пластины документа 1. Без переменной — выход.
/// </summary>
public class LiraBoundaryTransferManualTests(ITestOutputHelper output)
{
    const double TonToKn = 9.80665;

    [Fact]
    public void TransferFromText()
    {
        string? list = Environment.GetEnvironmentVariable("OPENCS_LIRA_BC_TXT");
        if (string.IsNullOrWhiteSpace(list)) return;
        var (raw, title) = ReadOpenLira();
        output.WriteLine(raw == null ? "ЛИРА: схема не открыта" : $"ЛИРА: открыта «{title}»");

        foreach (string path in list.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            output.WriteLine($"=== {Path.GetFileName(path)}");
            var s = LiraTextReader.Read(path);
            output.WriteLine($"  узлов {s.Nodes.Count}, КЭ {s.Elements.Count}, единицы {s.Units}");

            HashSet<string> nodeTags;
            Dictionary<string, string> types;
            Dictionary<string, double>? c1 = null;
            if (raw != null && string.Equals(title, s.Title, StringComparison.OrdinalIgnoreCase))
            {
                CompareNumbering(raw, s);
                var meshNodes = LiraSchemaConverter.ToFemMeshNodes(raw, 1);
                var elements = LiraSchemaConverter.ToFemMeshBarElements(raw, 1)
                    .Concat(LiraSchemaConverter.ToFemMeshShellElements(raw, 1));
                nodeTags = meshNodes.Select(n => n.NodeTag).ToHashSet(StringComparer.Ordinal);
                types = elements.GroupBy(e => e.ElemTag).ToDictionary(g => g.Key, g => g.First().ElemType, StringComparer.Ordinal);
                c1 = LiraSchemaConverter.ToFemElementBoundaryProps(raw)
                    .ToDictionary(kv => kv.Key, kv => kv.Value.FoundationC1 ?? 0, StringComparer.Ordinal);
            }
            else
            {
                nodeTags = Enumerable.Range(1, s.Nodes.Count).Select(i => i.ToString()).ToHashSet(StringComparer.Ordinal);
                types = s.Elements.Where(e => e.Nodes.Length is 2 or 3 or 4 && e.Type is not (51 or 55 or 56 or 57))
                    .ToDictionary(e => e.Number.ToString(), e => e.Nodes.Length == 2 ? "beam" : "shell", StringComparer.Ordinal);
            }

            var r = LiraBoundaryTransfer.Transfer(s, nodeTags, types, TonToKn, c1);
            foreach (var line in r.Report) output.WriteLine("  " + line);
            foreach (var row in r.Summary)
                output.WriteLine($"  | {row.Kind} | {row.Unit} | {row.Transferred} | {row.NotTransferred} | {row.Note}");
            if (r.Supports.Count > 0)
                output.WriteLine("  связи: " + string.Join(", ", r.Supports.GroupBy(x => x.Mask).OrderByDescending(g => g.Count())
                    .Select(g => $"{FemBoundaryDofs.Describe(g.Key)} ×{g.Count()}")));
            if (r.Springs.Count > 0)
                output.WriteLine("  пружины (кН/м, кН·м/рад): " + string.Join(", ", r.Springs.Take(5).Select(x =>
                    $"{x.NodeTag}: [{string.Join(" ", x.Stiffnesses.Select(k => (k / 1e3).ToString("G5")))}]")));
            if (r.ElementProps.Values.Any(p => p.ReleaseI != null || p.ReleaseJ != null))
                output.WriteLine("  шарниры: " + string.Join(", ", r.ElementProps.Values
                    .Where(p => p.ReleaseI != null || p.ReleaseJ != null)
                    .GroupBy(p => $"I {FemBoundaryDofs.Describe(p.ReleaseI ?? 0)} / J {FemBoundaryDofs.Describe(p.ReleaseJ ?? 0)}")
                    .Select(g => $"{g.Key} ×{g.Count()}")));

            // Резолвер на импортной сетке: пересечения АЖТ, закреплений и пружин.
            var resolved = FemBoundaryResolver.Resolve([], nodeTags.Select(t => new FemMeshNode { NodeTag = t }).ToList(), [],
                r.Supports, r.Springs, r.RigidBodies);
            output.WriteLine($"  резолвер: замечаний {resolved.Diagnostics.Count}, ошибок {resolved.Diagnostics.Count(d => d.IsError)}");
            foreach (var d in resolved.Diagnostics.Take(5)) output.WriteLine("    " + d.Message);
        }
    }

    /// <summary>Номера импорта против файла: узлы (координаты), КЭ (тип и узлы по порядку), закрепления.</summary>
    void CompareNumbering(LiraSchemaData raw, LiraTextSchema s)
    {
        double lm = s.Units.LengthUnitM, maxDx = 0;
        int nodeMissing = 0;
        foreach (var n in raw.Nodes)
        {
            if (n.Id < 1 || n.Id > s.Nodes.Count) { nodeMissing++; continue; }
            var p = s.Nodes[n.Id - 1];
            maxDx = Math.Max(maxDx, Math.Max(Math.Abs(p.X * lm - n.X), Math.Max(Math.Abs(p.Y * lm - n.Y), Math.Abs(p.Z * lm - n.Z))));
        }
        output.WriteLine($"  узлы: API {raw.Nodes.Count}, файл {s.Nodes.Count}, нет в файле {nodeMissing}, max |Δ| = {maxDx:G3} м");

        int elemMissing = 0, typeDiff = 0, nodesDiff = 0;
        string? firstDiff = null;
        foreach (var e in raw.Elements)
        {
            if (e.Id < 1 || e.Id > s.Elements.Count) { elemMissing++; continue; }
            var t = s.Elements[e.Id - 1];
            if (t.Type != e.FeType) typeDiff++;
            if (!t.Nodes.SequenceEqual(e.NodeIds))
            {
                nodesDiff++;
                firstDiff ??= $"КЭ {e.Id}: API [{string.Join(" ", e.NodeIds)}], файл [{string.Join(" ", t.Nodes)}]";
            }
        }
        output.WriteLine($"  КЭ: API {raw.Elements.Count}, файл {s.Elements.Count}, нет в файле {elemMissing}, тип расходится {typeDiff}, " +
                         $"узлы расходятся {nodesDiff}{(firstDiff != null ? " (" + firstDiff + ")" : "")}");
        output.WriteLine("  типы КЭ API: " + string.Join(", ", raw.Elements.GroupBy(e => e.FeType).OrderBy(g => g.Key)
            .Select(g => $"{g.Key} ×{g.Count()}")) + "; файл: " + string.Join(", ", s.Elements.GroupBy(e => e.Type)
            .OrderBy(g => g.Key).Select(g => $"{g.Key} ×{g.Count()}")));

        var api = LiraSchemaConverter.ToFemMeshNodeSupports(raw).ToDictionary(x => x.NodeTag, x => x.Mask);
        var txt = s.Supports.GroupBy(x => x.Node).ToDictionary(g => g.Key.ToString(),
            g => g.SelectMany(x => x.Dofs).Aggregate(0, (m, d) => m | 1 << (d - 1)));
        bool same = api.Count == txt.Count && txt.All(kv => api.GetValueOrDefault(kv.Key) == kv.Value);
        output.WriteLine($"  закрепления: API {api.Count}, файл {txt.Count}, совпадают: {same}");
    }

    static (LiraSchemaData? Raw, string? Title) ReadOpenLira()
    {
        LiraSchemaData? raw = null;
        string? title = null;
        var thread = new Thread(() =>
        {
            try { raw = LiraApiSchemaReader.Read(out _, out title, TonToKn); }
            catch { raw = null; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return (raw, title);
    }
}
