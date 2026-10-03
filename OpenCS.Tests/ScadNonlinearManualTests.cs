using CScore.Import;
using OpenCS.Services.Scad;
using Xunit;
using Xunit.Abstractions;

namespace OpenCS.Tests;

/// <summary>
/// Ручной прогон импорта физически нелинейных КЭ SCAD (тип = линейный аналог + 400) на модели
/// «Перекрытие_Дорфмана_нелин.SPR» (срез 4а, 03.10). OPENCS_SCAD_NL_SPR — путь к модели; без переменной тест
/// сразу выходит. OPENCS_SCAD_DIR, OPENCS_SCAD_WORK — как в <see cref="ScadApiReaderManualTests"/>.
/// </summary>
public class ScadNonlinearManualTests(ITestOutputHelper output)
{
    [Fact]
    public void ProbeNonlinearModel()
    {
        string? spr = Environment.GetEnvironmentVariable("OPENCS_SCAD_NL_SPR");
        if (string.IsNullOrWhiteSpace(spr)) return;
        string dir = Environment.GetEnvironmentVariable("OPENCS_SCAD_DIR") ?? ScadInstallLocator.FindDllDirectory()!;
        string? work = Environment.GetEnvironmentVariable("OPENCS_SCAD_WORK") ?? ScadInstallLocator.FindWorkDirectory();
        var native = ScadApiNative.Load(dir);
        using var s = new ScadApiSession(native);
        s.Open(spr);

        var r = ScadApiReader.Read(s, new ScadReadOptions(), null, CancellationToken.None);
        output.WriteLine($"Прочитано КЭ {r.Data.Elements.Count}, пропущено: " +
            string.Join(", ", r.SkippedByType.Select(kv => $"{kv.Key}×{kv.Value}")));
        var types = r.Data.Elements.GroupBy(e => (e.TypeCode, Nodes: e.NodeIds.Length)).OrderBy(g => g.Key)
            .ToDictionary(g => g.Key, g => g.ToList());
        foreach (var (key, list) in types)
            output.WriteLine($"тип {key.TypeCode}, узлов {key.Nodes}: КЭ {list.Count} (первый {list[0].Id}), " +
                $"жёсткости {string.Join(",", list.Select(e => e.StiffnessId).Distinct().Order())}");
        Assert.Empty(r.SkippedByType);   // КЭ 100 — абсолютно жёсткие тела (расчётная модель)
        Assert.Equal(12, types[(405, 2)].Count);
        Assert.Equal(1073, types[(444, 4)].Count);
        Assert.Equal(new LiraBarRect(0.3, 0.3), r.Data.Stiffnesses.Single(x => x.Id == 2).BarRect);
        Assert.Equal(0.2, r.Data.Stiffnesses.Single(x => x.Id == 5).ThicknessM);
        foreach (var st in r.Data.Stiffnesses)
            output.WriteLine($"Жёсткость {st.Id} «{st.Name}» {st.Kind} h={st.ThicknessM} rect={st.BarRect}: {st.Text}");

        // Усилия первого КЭ каждого типа.
        var targets = types.ToDictionary(kv => kv.Value[0].Id, kv => ScadElementKinds.Classify(kv.Key.TypeCode, kv.Key.Nodes));
        if (work != null && targets.Count > 0)
        {
            try
            {
                var lc = ScadApiForceReader.Read(s, work, targets, ScadForceReadKind.LoadCases, null, CancellationToken.None);
                foreach (var f in lc.Forces)
                    output.WriteLine($"КЭ {f.ElemId} ({f.Kind}): коды {string.Join(",", f.Types)}, точек {f.Points}, " +
                        $"з1 т1: {string.Join("; ", f.LoadCase(0, 0).ToArray().Select(x => x.ToString("0.###")))}");
                output.WriteLine($"Без результатов {lc.NoResultElements}, нет {lc.MissingElements}, другой вид {lc.WrongKindElements}");
                Assert.Equal(2, lc.Forces.Count);
                Assert.Equal(-10.98, lc.Forces.Single(f => f.ElemId == 1114).LoadCase(0, 0)[0], 2);
            }
            catch (ScadApiException ex) { output.WriteLine($"Усилия: {ex.ResourceKey}"); }
        }

        // Расчётная модель: опоры, жёсткие тела, нагрузки (срез 1 спеки плиты SCAD → OpenSees).
        var model = r.Data.AnalysisModel!;
        output.WriteLine($"Опоры: {string.Join(", ", model.Bounds.Select(kv => $"{kv.Key}:0x{kv.Value:X}"))}; " +
            $"тел {model.RigidBodies.Count}; сила — {model.ForceUnitN} Н");
        Assert.Equal(4, model.Bounds.Count);
        Assert.All(model.Bounds, kv => Assert.Equal(0x3F, kv.Value));
        var nodeZ = r.Data.Nodes.ToDictionary(nd => nd.Id, nd => nd.Z);
        Assert.All(model.Bounds.Keys, id => Assert.Equal(-4, nodeZ[id], 6));
        Assert.Equal(4, model.RigidBodies.Count);
        var body = model.RigidBodies.Single(b => b.ElemId == 1046);
        Assert.Equal(5, body.MasterNode);
        Assert.Equal(8, body.SlaveNodes.Length);
        Assert.Equal(0x3F, body.Mask);
        Assert.Equal(1.0, model.ForceUnitN, 6);
        var l1 = model.LoadCases[0];
        Assert.All(l1.ElementLoads, x => Assert.Equal(ScadAnalysisModel.SelfWeightQw, x.Qw));
        Assert.Equal(1073 + 12, l1.ElementLoads.Sum(x => x.Ids.Length));
        var q = Assert.Single(model.LoadCases[1].ElementLoads);
        Assert.Equal((ScadAnalysisModel.PlatePressureQw, 3), (q.Qw, q.Qn));
        Assert.Equal(8730.9, q.Data[0], 1);
        Assert.Equal(1073, q.Ids.Length);

        // Перемещения линейного расчёта (пробник 03.10): узел 510 — −3,868 мм (L1), −6,873 мм (L2). Остановленный
        // нелинейный расчёт затирает результаты в SWORK — тогда только сообщение.
        if (work == null) return;
        ScadDisplacementSet disp;
        try { disp = ScadApiDisplacementReader.Read(s, work, r.Data.Nodes.Select(nd => nd.Id).ToList(), CancellationToken.None); }
        catch (ScadApiException ex) { output.WriteLine($"Перемещения: {ex.ResourceKey}"); return; }
        foreach (var row in disp.Rows)
            output.WriteLine($"Перемещения: загр. {row.Load} строка {row.Row} шаг {row.Step} «{row.Name}», узлов {row.Nodes.Length}, " +
                $"узел 510: {row.Of(510)?[2]:0.######} м");
        if (disp.Rows.All(x => x.Step == 0))
        {
            Assert.Equal(-0.003868, disp.Rows.Single(x => x.Load == 1).Of(510)![2], 6);
            Assert.Equal(-0.006873, disp.Rows.Single(x => x.Load == 2).Of(510)![2], 6);
        }
    }
}
