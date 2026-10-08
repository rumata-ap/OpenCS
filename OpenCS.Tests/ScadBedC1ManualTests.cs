using System.Globalization;
using CScore.Import;
using CSfea.CScoreBridge.Structural;
using OpenCS.OpenSees.CScore;
using OpenCS.Services.Scad;
using Xunit;
using Xunit.Abstractions;

namespace OpenCS.Tests;

/// <summary>
/// Ручная сверка единиц C1 упругого основания SCAD (срез 4 спеки 4б): схема с основанием и результатами линейного
/// расчёта в рабочем каталоге. OPENCS_SCAD_BED_SPR — модель SCAD; OPENCS_SCAD_DIR, OPENCS_SCAD_WORK — как в
/// <see cref="ScadApiReaderManualTests"/>. Без OPENCS_SCAD_BED_SPR тест сразу выходит.
/// Две проверки: (1) независимо от CSfea — реакция основания ΣC1·∫w dA по перемещениям SCAD против нагрузки загружения;
/// (2) линейный расчёт CSfea по той же схеме против перемещений SCAD.
/// </summary>
public class ScadBedC1ManualTests(ITestOutputHelper output)
{
    [Fact]
    public void BedUnitsAgainstScadDisplacements()
    {
        string? spr = Environment.GetEnvironmentVariable("OPENCS_SCAD_BED_SPR");
        if (string.IsNullOrWhiteSpace(spr)) return;
        string dir = Environment.GetEnvironmentVariable("OPENCS_SCAD_DIR") ?? ScadInstallLocator.FindDllDirectory()!;
        string? work = Environment.GetEnvironmentVariable("OPENCS_SCAD_WORK") ?? ScadInstallLocator.FindWorkDirectory();
        var native = ScadApiNative.Load(dir);
        using var s = new ScadApiSession(native);
        s.Open(spr);
        var data = ScadApiReader.Read(s, new ScadReadOptions(), null, CancellationToken.None).Data;
        var am = data.AnalysisModel!;
        output.WriteLine($"Единицы: длина {am.LengthUnitM} м, сила {am.ForceUnitN} Н; узлов {data.Nodes.Count}, КЭ {data.Elements.Count}, " +
            $"закреплений {am.Bounds.Count}, пружин {am.Springs.Count}, тел {am.RigidBodies.Count}");
        foreach (var b in am.Beds)
            output.WriteLine($"Основание: тип {b.Type}, данные [{string.Join("; ", b.Data.Select(v => v.ToString("G6", CultureInfo.InvariantCulture)))}], " +
                $"КЭ {b.Elements.Length} ({b.Elements.Min()}…{b.Elements.Max()})");
        foreach (var st in data.Stiffnesses) output.WriteLine($"Жёсткость {st.Id} «{st.Name}» {st.Kind} h={st.ThicknessM}: {st.Text}");
        foreach (var lc in am.LoadCases)
            output.WriteLine($"Загружение {lc.Num} «{lc.Name}»: " + string.Join("; ", lc.ElementLoads.Concat(lc.NodeLoads)
                .Select(r => $"Qw {r.Qw} Qn {r.Qn} ×{r.Ids.Length} [{string.Join(" ", r.Data.Take(4).Select(v => v.ToString("G4", CultureInfo.InvariantCulture)))}]")));
        foreach (var (k, v) in am.NotTransferred) output.WriteLine($"Не перенесено: {k} — {v}");
        Assert.NotEmpty(am.Beds);

        var disp = ScadApiDisplacementReader.Read(s, work, data.Nodes.Select(n => n.Id).ToList(), CancellationToken.None);

        // Упругая модель CSfea: E и ν из строки жёсткости каждой пластины («GE E ν h …», E в единицах силы проекта).
        var stiff = data.Stiffnesses.ToDictionary(x => x.Id);
        var elems = data.Elements.ToDictionary(e => e.Id);
        (double E, double Nu) Ge(int stiffId)
        {
            var p = stiff.GetValueOrDefault(stiffId)?.Text?.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries) ?? [];
            return p.Length > 2 && p[0].Equals("GE", StringComparison.OrdinalIgnoreCase)
                ? (double.Parse(p[1], CultureInfo.InvariantCulture) * am.ForceUnitN, double.Parse(p[2], CultureInfo.InvariantCulture))
                : (3e10, 0.2);
        }
        var stages = am.LoadCases.Select(l => new ScadShellStage($"L{l.Num}", [(l.Num, 1.0)], 1.0)).ToList();
        var adapted = ScadRcModelAdapter.Adapt(new ScadRcModelInput
        {
            Data = data,
            PlateSection = id =>
            {
                int sid = elems[id].StiffnessId;
                double h = stiff.GetValueOrDefault(sid)?.ThicknessM ?? 0.2;
                return new ScadShellElementSection(new CScore.PlateSection { Tag = $"ж{sid}", H = h }, $"elastic|{sid}");
            },
            ShellSection = sec =>
            {
                var (e, nu) = Ge(int.Parse(sec.Section.Tag![1..], CultureInfo.InvariantCulture));
                return ScadRcModelAdapter.ElasticShells(e, nu)(sec);
            },
            Stages = stages,
        });
        var build = RcStructuralMeshBuilder.Build(adapted.Model, new LinearRcSectionFactory());
        foreach (var line in adapted.Report.Concat(build.Report).Distinct()) output.WriteLine(line);

        var nodes = adapted.Model.Nodes.ToDictionary(n => n.Id);
        var bedShells = adapted.Model.Shells.Where(x => x.FoundationC1 > 0).ToList();
        output.WriteLine($"Пластин на основании {bedShells.Count}, C1 {bedShells.Min(x => x.FoundationC1!.Value):G6}…" +
            $"{bedShells.Max(x => x.FoundationC1!.Value):G6} Н/м³ (как прочитано)");

        for (int k = 0; k < stages.Count; k++)
        {
            int load = am.LoadCases[k].Num;
            var row = disp.Rows.FirstOrDefault(r => r.Load == k + 1 && r.Step == 0);
            if (row == null) { output.WriteLine($"L{load}: нет перемещений SCAD"); continue; }

            // (1) Реакция основания по перемещениям SCAD: Σ C1·Σ wᵢ·(∫Nᵢ dA) вдоль нормали КЭ.
            double bedR = 0;
            foreach (var sh in bedShells)
            {
                var p = sh.NodeIds.Select(id => new[] { nodes[id].X, nodes[id].Y, nodes[id].Z }).ToArray();
                var w = RcStructuralMeshBuilder.AreaWeights(p);
                var nrm = Normal(p);
                for (int i = 0; i < p.Length; i++)
                {
                    var d = row.Of(sh.NodeIds[i])!;
                    bedR += sh.FoundationC1!.Value * w[i] * (d[0] * nrm[0] + d[1] * nrm[1] + d[2] * nrm[2]) * nrm[2];
                }
            }

            // (2) CSfea линейно.
            var f = build.Combination(adapted.Model.Stages[k].Loads);
            var u = build.Mesh.SolveLinear(f, build.Bc);
            var bedNodes = bedShells.SelectMany(x => x.NodeIds).Distinct().ToList();
            double maxScad = bedNodes.Max(id => Math.Abs(row.Of(id)![2]));
            double maxCs = bedNodes.Max(id => Math.Abs(u[build.Dof(id, 2)]));
            int peak = bedNodes.MaxBy(id => Math.Abs(row.Of(id)![2]));
            double allScad = data.Nodes.Max(n => row.Of(n.Id) is { } d ? Math.Sqrt(d[0] * d[0] + d[1] * d[1] + d[2] * d[2]) : 0);
            double allCs = data.Nodes.Where(n => nodes.ContainsKey(n.Id)).Max(n =>
                Math.Sqrt(Enumerable.Range(0, 3).Sum(c => Math.Pow(u[build.Dof(n.Id, c)], 2))));
            output.WriteLine($"L{load} «{am.LoadCases[k].Name}»: нагрузка вниз {adapted.StageTotalDownN[k] / 1e3:0.###} кН; " +
                $"реакция основания по w SCAD {bedR / 1e3:0.###} кН ({bedR / adapted.StageTotalDownN[k]:0.####}); " +
                $"max|uz| основания SCAD {maxScad * 1e3:0.###} / CSfea {maxCs * 1e3:0.###} мм ({maxCs / maxScad:0.####}), " +
                $"узел {peak}: {row.Of(peak)![2] * 1e3:0.###} / {u[build.Dof(peak, 2)] * 1e3:0.###} мм; " +
                $"max|u| схемы {allScad * 1e3:0.###} / {allCs * 1e3:0.###} мм");

            // Единицы C1: при C1 в Н/м³ реакция основания по перемещениям SCAD уравновешивает вертикальную нагрузку
            // (подпорная стена на Пионеров, 08.10: 0,9999 и 1,0000). Ошибка в т/м³ или кН/м³ дала бы ×9810 или ×1000.
            Assert.InRange(-bedR / adapted.StageTotalDownN[k], 0.995, 1.005);

            // Нагрузка по осям и сравнение полей перемещений: регрессия CSfea = a·SCAD по каждой компоненте.
            // ΣF — узловые силы и концевые силы шарнирных стержней.
            var rcCase = adapted.Model.LoadCases.Single(l => l.Id == load);
            double Sum(int c) => rcCase.Nodal.Sum(p => p.Force[c]) + rcCase.BeamEnds.Sum(p => p.Forces[c] + p.Forces[c + 6]);
            output.WriteLine($"  ΣF = ({string.Join("; ", Enumerable.Range(0, 3).Select(c => (Sum(c) / 1e3).ToString("0.###")))}) кН");
            foreach (int c in new[] { 0, 1, 2 })
            {
                var pairs = data.Nodes.Where(n => nodes.ContainsKey(n.Id) && row.Of(n.Id) != null)
                    .Select(n => (S: row.Of(n.Id)![c], C: u[build.Dof(n.Id, c)])).ToList();
                double a = pairs.Sum(p => p.S * p.C) / pairs.Sum(p => p.S * p.S);
                double res = Math.Sqrt(pairs.Sum(p => Math.Pow(p.C - a * p.S, 2)) / pairs.Sum(p => p.C * p.C));
                output.WriteLine($"  u{"xyz"[c]}: max|SCAD| {pairs.Max(p => Math.Abs(p.S)) * 1e3:0.###} мм, max|CSfea| " +
                    $"{pairs.Max(p => Math.Abs(p.C)) * 1e3:0.###} мм, CSfea ≈ {a:0.###}·SCAD, остаток {res:P1}");
            }
        }
    }

    static double[] Normal(double[][] p)
    {
        // Q4 — произведение диагоналей, T3 — сторон; знак нормали не важен (входит в реакцию дважды).
        var (a, b) = p.Length == 3 ? (Sub(p[1], p[0]), Sub(p[2], p[0])) : (Sub(p[2], p[0]), Sub(p[3], p[1]));
        var n = new[] { a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0] };
        double l = Math.Sqrt(n.Sum(x => x * x));
        return n.Select(x => x / l).ToArray();
    }

    static double[] Sub(double[] a, double[] b) => [a[0] - b[0], a[1] - b[1], a[2] - b[2]];
}
