using System.Diagnostics;
using System.Globalization;
using CSfea.CScoreBridge.Structural;
using CSfea.Sparse;
using OpenCS.OpenSees.CScore;
using OpenCS.Services.Scad;
using Xunit;
using Xunit.Abstractions;

namespace OpenCS.Tests;

/// <summary>
/// Пробник размера (срез 4 плана 4б): схема SCAD → RcStructuralModel → сборка K → редукция (жёсткие тела, закрепления)
/// → портрет Kff в файл OPENCS_SIZE_PROBE_OUT (int32: n, colPtr[n+1], rowIdx[nnz]) для символического анализа
/// Холецкого вне теста. OPENCS_SIZE_PROBE_SPR — модель SCAD; без неё тест сразу выходит. Факторизации нет.
/// </summary>
public class CsfeaSizeProbeManualTests(ITestOutputHelper output)
{
    [Fact]
    public void DumpReducedPattern()
    {
        string? spr = Environment.GetEnvironmentVariable("OPENCS_SIZE_PROBE_SPR");
        if (string.IsNullOrWhiteSpace(spr)) return;
        string outPath = Environment.GetEnvironmentVariable("OPENCS_SIZE_PROBE_OUT") ?? Path.ChangeExtension(spr, ".kff.bin");
        string dir = Environment.GetEnvironmentVariable("OPENCS_SCAD_DIR") ?? ScadInstallLocator.FindDllDirectory()!;
        var sw = Stopwatch.StartNew();
        void Log(string s) => output.WriteLine($"[{sw.Elapsed.TotalSeconds,7:0.0} с, {GC.GetTotalMemory(false) / 1048576.0,7:0} МБ] {s}");

        var native = ScadApiNative.Load(dir);
        using var s = new ScadApiSession(native);
        s.Open(spr);
        var data = ScadApiReader.Read(s, new ScadReadOptions(), null, CancellationToken.None).Data;
        var am = data.AnalysisModel!;
        Log($"Прочитано: узлов {data.Nodes.Count}, КЭ {data.Elements.Count}, закреплений {am.Bounds.Count}, " +
            $"пружин {am.Springs.Count}, тел {am.RigidBodies.Count}, загружений {am.LoadCases.Count}");

        var stiff = data.Stiffnesses.ToDictionary(x => x.Id);
        var elems = data.Elements.ToDictionary(e => e.Id);
        foreach (var g in data.Elements.GroupBy(e => (e.TypeCode, e.StiffnessId)).OrderBy(g => g.Key))
        {
            var st = stiff.GetValueOrDefault(g.Key.StiffnessId);
            string text = st?.Text ?? "—";
            output.WriteLine($"  тип {g.Key.TypeCode}, жёсткость {g.Key.StiffnessId} «{st?.Name}» {st?.Kind}: {g.Count()} КЭ; " +
                $"{(text.Length > 160 ? text[..160] + "…" : text)}");
        }
        output.WriteLine($"  шарниров {am.Joints.Count}");
        var adapted = ScadRcModelAdapter.Adapt(new ScadRcModelInput
        {
            Data = data,
            PlateSection = id =>
            {
                int sid = elems[id].StiffnessId;
                double h = stiff.GetValueOrDefault(sid)?.ThicknessM ?? 0.2;
                return new ScadShellElementSection(new CScore.PlateSection { Tag = $"ж{sid}", H = h }, $"elastic|{h}");
            },
            ShellSection = ScadRcModelAdapter.ElasticShells(3e10, 0.2),
            Stages = am.LoadCases.Take(1).Select(l => new ScadShellStage($"L{l.Num}", [(l.Num, 1.0)], 1.0)).ToList(),
        });
        var m = adapted.Model;
        Log($"Модель: узлов {m.Nodes.Count}, пластин {m.Shells.Count} (Q4 {m.Shells.Count(x => x.NodeIds.Length == 4)}), " +
            $"стержней {m.Beams.Count}, тел {m.RigidBodies.Count}, закреплений {m.Supports.Count}");

        var build = RcStructuralMeshBuilder.Build(m, new LinearRcSectionFactory());
        foreach (var line in adapted.Report.Concat(build.Report).Distinct().Take(40)) output.WriteLine("  " + line);
        var mesh = build.Mesh;
        Log($"Сетка: NDof {mesh.NDof}");

        var k = mesh.AssembleK();
        Log($"K собрана (COO): {k.Count} триплетов");
        var kSys = mesh.Links?.ReduceMatrix(k) ?? k;
        var fixedSys = mesh.Links?.ReduceFixedDofs(build.Bc.FixedDofs) ?? build.Bc.FixedDofs;
        Log($"После жёстких тел: {kSys.Rows} DOF, закреплено {fixedSys.Length}");
        var red = DirichletReducer.Reduce(kSys, new double[kSys.Rows], fixedSys, null);
        var a = red.Kff;
        Log($"Kff: n = {a.Cols}, nnz = {a.Nnz} (полная симм.), в среднем {a.Nnz / (double)a.Cols:0.#} в столбце");

        using (var bw = new BinaryWriter(File.Create(outPath)))
        {
            bw.Write(a.Cols);
            foreach (int v in a.ColPtr) bw.Write(v);
            for (int i = 0; i < a.Nnz; i++) bw.Write(a.RowIdx[i]);
        }
        Log($"Портрет записан: {outPath}");
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Пик рабочего набора процесса {Process.GetCurrentProcess().PeakWorkingSet64 / 1048576.0:0} МБ"));
    }
}
