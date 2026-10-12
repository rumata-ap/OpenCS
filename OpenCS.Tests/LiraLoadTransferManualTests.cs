using System.Diagnostics;
using CScore.Fem;
using CScore.Fem.Loads;
using CScore.Import;
using CScore.Planar;
using CSfea.CScoreBridge.Structural;
using Xunit;
using Xunit.Abstractions;

namespace OpenCS.Tests;

/// <summary>
/// Ручной прогон переноса нагрузок ЛИРЫ (срез 4 плана 4б ЛИРЫ + 4ж, 12.10): текстовые файлы OPENCS_LIRA_LOADS_TXT (пути
/// через «;»). Сетка — все узлы документа 4 и стержни/пластины документа 1 (номера = номера импорта, проверено в срезе 2).
/// По каждому загружению — ΣF узловых сил сетки после переноса против ΣF, посчитанной прямо по документам 6/7 (сила
/// записи × длина/площадь КЭ, без слияния и разнесения), и время/память чтения и переноса. С OPENCS_LIRA_LOADS_LINEAR=1 —
/// ещё линейный CSfea с перенесёнными ГУ (жёсткости условные: E = 30 ГПа, ν = 0,2, h = 0,2 м, стержни 0,3 × 0,3 м):
/// ΣR = −ΣF по каждому загружению. Без переменной — выход.
/// </summary>
public class LiraLoadTransferManualTests(ITestOutputHelper output)
{
    const double TonToKn = 9.80665;

    [Fact]
    public void TransferFromText()
    {
        string? list = Environment.GetEnvironmentVariable("OPENCS_LIRA_LOADS_TXT");
        if (string.IsNullOrWhiteSpace(list)) return;
        bool linear = Environment.GetEnvironmentVariable("OPENCS_LIRA_LOADS_LINEAR") == "1";

        foreach (string path in list.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            output.WriteLine($"=== {Path.GetFileName(path)}");
            GC.Collect();
            long mem0 = GC.GetTotalMemory(true);
            var sw = Stopwatch.StartNew();
            var s = LiraTextReader.Read(path);
            double tRead = sw.Elapsed.TotalSeconds;
            long memSchema = GC.GetTotalMemory(true) - mem0;
            output.WriteLine($"  узлов {s.Nodes.Count}, КЭ {s.Elements.Count}, нагрузок {s.Loads.Count}, строк значений {s.LoadValues.Count}, " +
                $"единицы {s.Units}; чтение {tRead:0.00} с, схема {memSchema / 1e6:0} МБ");

            var meshNodes = s.Nodes.Select((p, i) => new FemMeshNode
            {
                NodeTag = (i + 1).ToString(), X = p.X * s.Units.LengthUnitM, Y = p.Y * s.Units.LengthUnitM, Z = p.Z * s.Units.LengthUnitM,
            }).ToList();
            var elements = s.Elements.Where(e => e.Nodes.Length is 2 or 3 or 4 && !IsLink(e.Type)).Select(e => new FemElement
            {
                ElemTag = e.Number.ToString(), ElemType = e.Nodes.Length == 2 ? "beam" : "shell",
                NodeIdsJson = System.Text.Json.JsonSerializer.Serialize(e.Nodes), StiffnessNum = 1,
                Origin = FemMember.MeshSourceImported,
                BeamRotationDeg = e.Nodes.Length == 2 ? Rotation(meshNodes[e.Nodes[0] - 1], meshNodes[e.Nodes[1] - 1]) : null,
            }).ToList();
            var nodeTags = meshNodes.Select(n => n.NodeTag).ToHashSet(StringComparer.Ordinal);
            var types = elements.ToDictionary(e => e.ElemTag, e => e.ElemType, StringComparer.Ordinal);

            int next = 0;
            sw.Restart();
            var r = LiraLoadTransfer.Transfer(s, nodeTags, types, TonToKn, [], [], [], () => --next);
            double tTransfer = sw.Elapsed.TotalSeconds;
            long memAll = GC.GetTotalMemory(true) - mem0;
            output.WriteLine($"  перенос {tTransfer:0.00} с; схема + сетка + нагрузки {memAll / 1e6:0} МБ, " +
                $"рабочий набор {Process.GetCurrentProcess().WorkingSet64 / 1e6:0} МБ");
            foreach (var line in r.Report) output.WriteLine("  " + line);
            output.WriteLine("  виды: " + string.Join(", ", r.ElementLoads.GroupBy(l => $"{l.LoadKind}/{l.CoordinateSystem}")
                .Select(g => $"{g.Key} ×{g.Count()} (КЭ {g.Sum(l => l.TargetTags.Count)})")));

            var mesh = new FemLoadMeshContext(meshNodes, elements);
            sw.Restart();
            var transferred = new Dictionary<int, (double, double, double)>();
            output.WriteLine("  | загружение | ΣFx, ΣFy, ΣFz после переноса, кН | прямо по файлу, кН | замечаний |");
            foreach (var lc in r.LoadCases)
            {
                var res = FemLoadCaseNodalForces.Resolve(lc, r.ElementLoads, r.MeshNodeLoads, mesh);
                var direct = DirectSum(s, lc.SourceLoadNum!.Value, mesh);
                var t = res.Total;
                transferred[lc.Id] = t;
                output.WriteLine($"  | {lc.SourceLoadNum} {lc.Tag} | {t.Fx / 1e3:0.###}, {t.Fy / 1e3:0.###}, {t.Fz / 1e3:0.###} | " +
                    $"{direct.X / 1e3:0.###}, {direct.Y / 1e3:0.###}, {direct.Z / 1e3:0.###} | {res.Diagnostics.Count} |");
                foreach (var d in res.Diagnostics.Take(3)) output.WriteLine("      " + d.Message);
            }
            output.WriteLine($"  разнесение по узлам {sw.Elapsed.TotalSeconds:0.00} с");

            if (linear) RunLinear(s, meshNodes, elements, nodeTags, types, r, transferred);
        }
    }

    /// <summary>Линейный CSfea с перенесёнными ГУ: ΣR = −ΣF по каждому загружению.</summary>
    void RunLinear(LiraTextSchema s, List<FemMeshNode> meshNodes, List<FemElement> elements, HashSet<string> nodeTags,
        Dictionary<string, string> types, FemLoadTransferResult loads, Dictionary<int, (double Fx, double Fy, double Fz)> totals)
    {
        var bc = LiraBoundaryTransfer.Transfer(s, nodeTags, types, TonToKn);
        foreach (var (tag, p) in bc.ElementProps)
        {
            var e = elements.First(x => x.ElemTag == tag);
            e.ReleaseI = p.ReleaseI; e.ReleaseJ = p.ReleaseJ;
        }
        var adapted = FemRcModelAdapter.Adapt(new FemRcModelInput
        {
            MeshNodes = meshNodes, MeshElements = elements, Properties = new ConventionalProps(),
            Supports = bc.Supports, Springs = bc.Springs, RigidBodies = bc.RigidBodies,
            LoadCases = loads.LoadCases, ElementLoads = loads.ElementLoads, MeshNodeLoads = loads.MeshNodeLoads,
            Stages = loads.LoadCases.Select(c => new FemRcStage(c.Tag, [(c.Id, 1.0)])).ToList(),
        });
        foreach (var line in adapted.Report.Take(10)) output.WriteLine("  адаптер: " + line);
        if (adapted.HasErrors) return;
        var build = RcStructuralMeshBuilder.Build(adapted.Model, new LinearRcSectionFactory());
        for (int k = 0; k < loads.LoadCases.Count; k++)
        {
            var lc = loads.LoadCases[k];
            var f = build.Combination(adapted.Model.Stages[k].Loads);
            var u = build.Mesh.SolveLinear(f, build.Bc);
            var react = build.Mesh.ComputeReactions(u, build.Bc, fExternal: f);
            double rx = 0, ry = 0, rz = 0;
            foreach (int id in build.NodeIndex.Keys)
            {
                rx += react[build.Dof(id, 0)]; ry += react[build.Dof(id, 1)]; rz += react[build.Dof(id, 2)];
            }
            var t = totals[lc.Id];
            output.WriteLine($"  CSfea «{lc.Tag}»: ΣR = {rx / 1e3:0.###}, {ry / 1e3:0.###}, {rz / 1e3:0.###} кН; " +
                $"ΣR + ΣF = {(rx + t.Fx) / 1e3:0.######}, {(ry + t.Fy) / 1e3:0.######}, {(rz + t.Fz) / 1e3:0.######} кН; " +
                $"max |u| = {u.Max(Math.Abs) * 1e3:0.###} мм");
        }
    }

    /// <summary>
    /// ΣF загружения прямо по документам 6/7: каждая запись отдельно (без слияния), сила = величина × длина/площадь КЭ,
    /// знак ЛИРЫ обратный, единицы документа 0. Местные Y1/Z1 стержня и записи, которые перенос пропускает, не входят.
    /// </summary>
    static PlanarVector3 DirectSum(LiraTextSchema s, int loadCase, FemLoadMeshContext mesh)
    {
        double fu = s.Units.ForceUnitN(TonToKn), lu = s.Units.LengthUnitM;
        var sum = PlanarVector3.Zero;
        foreach (var l in s.Loads)
        {
            if (l.LoadCase != loadCase || l.Direction is < 1 or > 3 || !s.LoadValues.TryGetValue(l.ValueRow, out var v)) continue;
            var global = l.Direction switch { 1 => new PlanarVector3(1, 0, 0), 2 => new PlanarVector3(0, 1, 0), _ => new PlanarVector3(0, 0, 1) };
            if (l.Kind == 0)
            {
                if (v.Skip(1).All(x => x == 0) && mesh.NodesByTag.ContainsKey(l.Target.ToString())) sum += global * (-v[0] * fu);
                continue;
            }
            if (!mesh.ElementsByTag.TryGetValue(l.Target.ToString(), out var e) || mesh.Geometry(e) is not { } g) continue;
            PlanarVector3 dir;
            bool local = l.Kind is 5 or 6 or 7;
            if (!local) dir = global;
            else if (g.IsShell) { var (x, y, z) = g.ShellFrame(); dir = l.Direction switch { 1 => x, 2 => y, _ => z }; }
            else if (l.Direction == 1) dir = (g.Points[1] - g.Points[0]).Normalize();
            else continue;
            double force = (l.Kind, g.IsShell) switch
            {
                (6 or 16, true) when v.Skip(1).All(x => x == 0) => v[0] * g.Area / (lu * lu),
                (6 or 16, false) when v.Skip(1).All(x => x == 0) => v[0] * g.Length / lu,
                (7 or 17, true) => g.ShellNodeWeights().Select((w, i) => w * v[i]).Sum() / (lu * lu),
                (7 or 17, false) => TrapezoidForce(v[0], v[1] * lu, v[2], v[3] * lu, g.Length) / lu,
                (5 or 15, _) => v[0],
                (19, true) when FemElementLoadNodalizer.ShellEdge(g, v[0], v[1]) is var (i, j) =>
                    (v[2] + v[3]) / 2 * (g.Points[j] - g.Points[i]).Length / lu,
                _ => 0,
            };
            sum += dir * (-force * fu);
        }
        return sum;
    }

    static double TrapezoidForce(double q1, double a1, double q2, double a2, double length) =>
        FemElementLoadNodalizer.TrapezoidOnBar(q1, a1, q2, a2, length) is var (a, qa, b, qb) ? (qa + qb) / 2 * (b - a) : 0;

    static bool IsLink(int type) => type is (>= 50 and <= 59) or (>= 250 and <= 259);

    static double? Rotation(FemMeshNode a, FemMeshNode b) =>
        LiraSchemaConverter.DefaultBarRotationDeg(new LiraNodeRecord(0, a.X, a.Y, a.Z, 0), new LiraNodeRecord(0, b.X, b.Y, b.Z, 0));

    /// <summary>Условные упругие свойства: для ΣR = ΣF жёсткости не важны.</summary>
    sealed class ConventionalProps : IFemElementStiffnessSource
    {
        public FemShellStiffness? Shell(FemElement e) => e.ElemType == "shell" ? new(30e9, 0.2, 0.2) : null;
        public FemBarStiffness? Bar(FemElement e) => e.ElemType == "beam" ? new(30e9, 12.5e9, 0.09, 6.75e-4, 6.75e-4, 1.14e-3) : null;
        public double? UnitWeight(FemElement e) => null;
        public double? BarArea(FemElement e) => null;
    }
}
