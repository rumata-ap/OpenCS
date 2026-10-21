using System.Globalization;
using System.Text.RegularExpressions;
using CScore.Fem;
using CScore.Fem.Fields;
using CScore.Import;
using OpenCS.Services;
using Xunit;
using Xunit.Abstractions;

namespace OpenCS.Tests;

/// <summary>
/// Ручная сверка полей на данных примера (ГТО, не публиковать): схема — из открытой ЛИРЫ через COM (сетка тем же
/// конвертером, что и импорт), таблицы — из папки OPENCS_GTO_DIR (Cz.csv, Rz.csv). Без переменной или без открытой
/// схемы — выход.
/// </summary>
public class FemFieldGtoManualTests(ITestOutputHelper output)
{
    const double TonToKn = 9.80665;

    [Fact]
    public void GtoExample()
    {
        string? dir = Environment.GetEnvironmentVariable("OPENCS_GTO_DIR");
        if (string.IsNullOrWhiteSpace(dir)) return;
        LiraSchemaData? raw = null;
        List<LiraStiffnessRecord>? stiff = null;
        Dictionary<int, int>? elementStiffness = null;
        Exception? err = null;
        var t = new Thread(() =>
        {
            try
            {
                raw = LiraApiSchemaReader.Read(out _, out _, TonToKn);
                (stiff, elementStiffness, _) = LiraApiSchemaReader.ReadStiffnesses(TonToKn);
            }
            catch (Exception e) { err = e; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (err != null || raw == null) { output.WriteLine("ЛИРА: схема не открыта — " + err?.Message); return; }

        var nodes = LiraSchemaConverter.ToFemMeshNodes(raw, 1).ToList();
        var nodeByTag = nodes.ToDictionary(n => n.NodeTag, StringComparer.Ordinal);
        var props = LiraSchemaConverter.ToFemElementBoundaryProps(raw);
        var shells = LiraSchemaConverter.ToFemMeshShellElements(raw, 1).ToList();
        foreach (var e in shells) e.FoundationC1 = props.TryGetValue(e.ElemTag, out var p) ? p.FoundationC1 : null;
        var piles = raw.Elements.Where(e => e.FeType == 56).ToList();
        var springs = piles.Select(e => new FemSpring
        {
            NodeTag = e.NodeIds[0].ToString(CultureInfo.InvariantCulture), Kx = 1, Ky = 1, Kz = 1,
            SourceElemTag = e.Id.ToString(CultureInfo.InvariantCulture),
        }).ToList();
        var ctx = new FemFieldMeshContext(nodes, shells, [], [], springs);
        output.WriteLine($"Схема: узлов {nodes.Count}, пластин {shells.Count}, свай КЭ 56 {piles.Count}");

        // C1: таблица → совмещение по габаритам → сверка режима «ближайшая» с C1 ЛИРЫ.
        var czTable = PointTable.Parse(File.ReadAllText(Path.Combine(dir, "Cz.csv")));
        var (cz, _) = czTable.Select(0, 1, 2, [3]);
        var shellTargets = FemFieldEvaluator.ResolveTargets(new FemField { Quantity = FemFieldQuantities.FoundationC1 }, ctx).Shells;
        var targets = shellTargets.SelectMany(s => s.Nodes).ToList();
        var fit = FieldAlignmentFit.ByExtents(new FieldAlignment(), cz.Select(p => p.P).ToList(), targets, hasZ: true);
        output.WriteLine($"C1: сдвиг {fit.Alignment.Dx:0.###}; {fit.Alignment.Dy:0.###}; {fit.Alignment.Dz:0.###}; " +
                         $"невязка медиана {fit.Median:0.###}, макс {fit.Max:0.###}");
        Assert.Equal(63.6, fit.Alignment.Dx, 1);
        Assert.Equal(29.7, fit.Alignment.Dy, 1);

        FemField C1(FieldMethod m) => new()
        {
            Id = 1, Quantity = FemFieldQuantities.FoundationC1, HasZ = true, Points = cz,
            Definition = new FemFieldDefinition
            {
                Columns = [new FemFieldColumn("C1", "c1", "тс/м³", TonToKn * 1000)],
                Alignment = fit.Alignment,
                Settings = FieldDefaults.For(shellTargets, m),
            },
        };
        var nearest = FemFieldEvaluator.Compute(C1(FieldMethod.Nearest), ctx);
        var liraC1 = shells.ToDictionary(e => e.ElemTag, e => e.FoundationC1, StringComparer.Ordinal);
        int same = nearest.Values.Count(v => v.Value is double x && liraC1[v.TargetTag] is double l && Math.Abs(x - l) / l < 1e-4);
        output.WriteLine($"C1 «ближайшая»: совпало с ЛИРОЙ {same} из {nearest.Values.Count}; участков {nearest.Evaluation.Patches.Count}");
        foreach (var d in nearest.Evaluation.Diagnostics) output.WriteLine($"  {d.Code}: {d.Message} ({d.Tags.Count})");

        // Разбор несовпадений: значение ЛИРЫ — у одной из исходных (до слияния дублей) точек рядом с центром КЭ?
        var aligned = cz.Select(p => new FieldPoint(fit.Alignment.Apply(p.P), p.Values)).ToList();
        var rawTree = new KdTree3(aligned.Select(p => p.P with { Z = 0 }).ToList());
        int mismatched = 0, liraIsRawNeighbour = 0, liraNone = 0;
        foreach (var v in nearest.Values)
        {
            if (liraC1[v.TargetTag] is not double l) { liraNone++; continue; }
            if (v.Value is double x && Math.Abs(x - l) / l < 1e-4) continue;
            mismatched++;
            var c = shellTargets.First(s => s.Tag == v.TargetTag).Centroid with { Z = 0 };
            if (rawTree.KNearest(c, 4).Any(k => Math.Abs(aligned[k.Index].Values[0] * TonToKn * 1000 - l) / l < 1e-4))
                liraIsRawNeighbour++;
        }
        output.WriteLine($"  несовпадений {mismatched}: у {liraIsRawNeighbour} значение ЛИРЫ — у одной из 4 ближайших исходных точек; " +
                         $"КЭ без C1 у ЛИРЫ {liraNone}");
        int rawSame = shellTargets.Count(s => liraC1[s.Tag] is double l
            && Math.Abs(aligned[rawTree.Nearest(s.Centroid with { Z = 0 }).Index].Values[0] * TonToKn * 1000 - l) / l < 1e-4);
        output.WriteLine($"  «ближайшая» по исходным точкам без слияния дублей и без предела расстояния: совпало {rawSame}");
        // Пробник (15 994) — по исходным точкам: совмещение и поиск сверяются с ним. Поле по спеке усредняет дубли
        // (стыки плит ГТО, 754 точки) и не назначает дальше предельного расстояния — оттуда ~250 КЭ разницы.
        Assert.True(rawSame >= 15994, $"по исходным точкам совпало {rawSame}");
        Assert.True(same >= rawSame - 300, $"совпало {same} при {rawSame} по исходным точкам");

        var interp = FemFieldEvaluator.Compute(C1(FieldMethod.Interpolate), ctx);
        output.WriteLine("C1 «интерполяция»: " + string.Join(", ", interp.Evaluation.Targets.GroupBy(x => x.How).Select(g => $"{g.Key} {g.Count()}")));
        foreach (var d in interp.Evaluation.Diagnostics) output.WriteLine($"  {d.Code}: {d.Message} ({d.Tags.Count})");

        // Rz: см → м, без Z, совмещение по точкам, один к одному.
        var rzTable = PointTable.Parse(File.ReadAllText(Path.Combine(dir, "Rz.csv")));
        output.WriteLine("Rz: столбцы " + string.Join(" | ", rzTable.Headers));
        var (rz, _) = rzTable.Select(0, 1, null, [3]);
        var pileNodes = springs.Select(s => nodeByTag[s.NodeTag]).Select(n => new FieldVec3(n.X, n.Y, n.Z)).ToList();
        var rzFit = FieldAlignmentFit.ByPoints(new FieldAlignment(Scale: 0.01), rz.Select(p => p.P).ToList(), pileNodes, hasZ: false);
        output.WriteLine($"Rz: сдвиг {rzFit.Alignment.Dx:0.###}; {rzFit.Alignment.Dy:0.###}; невязка медиана {rzFit.Median:0.###}, " +
                         $"95 % {rzFit.P95:0.###}, макс {rzFit.Max:0.###}");
        var rzField = new FemField
        {
            Id = 2, Quantity = FemFieldQuantities.Spring, HasZ = false, Points = rz,
            Definition = new FemFieldDefinition
            {
                Columns = [new FemFieldColumn("Rz", "kz", "тс/см", TonToKn * 1000 * 100)],
                Alignment = rzFit.Alignment,
                Settings = new FieldSettings(FieldMethod.Match, MatchTolerance: 0.6),
            },
        };
        var rzComputed = FemFieldEvaluator.Compute(rzField, ctx);
        int matched = rzComputed.Values.Count(v => v.Value != null);
        output.WriteLine($"Rz: сопоставлено {matched} из {piles.Count}");
        foreach (var d in rzComputed.Evaluation.Diagnostics) output.WriteLine($"  {d.Code}: {d.Message} ({d.Tags.Count})");
        Assert.Equal(248, matched);

        // Сверка с жёсткостями КЭ 56 ЛИРЫ (тс/м, сгруппированы по типам): отличие ≤ 2,5 %.
        var rzLira = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var e in piles)
        {
            int sid = elementStiffness != null && elementStiffness.TryGetValue(e.Id, out int s) ? s : e.StiffnessId;
            var rec = stiff!.FirstOrDefault(x => x.Id == sid);
            var m = rec == null ? Match.Empty : Regex.Match(rec.Params, @"Rz\s*[:=]\s*([-\d.,E+]+)", RegexOptions.IgnoreCase);
            if (!m.Success)
            {
                output.WriteLine($"КЭ 56 №{e.Id}: Rz в параметрах жёсткости не найден — «{rec?.Params}»");
                continue;
            }
            rzLira[e.NodeIds[0].ToString(CultureInfo.InvariantCulture)] =
                double.Parse(m.Groups[1].Value.Replace(',', '.'), CultureInfo.InvariantCulture) * TonToKn * 1000;
        }
        var compared = rzComputed.Values.Where(v => v.Value != null && rzLira.ContainsKey(v.TargetTag)).ToList();
        Assert.NotEmpty(compared);
        double worst = compared.Max(v => Math.Abs(v.Value!.Value - rzLira[v.TargetTag]) / rzLira[v.TargetTag]);
        output.WriteLine($"Rz против ЛИРЫ: сравнено {compared.Count}, макс. отличие {worst:P2}");
        Assert.True(worst <= 0.025, $"отличие {worst:P2}");
    }
}
