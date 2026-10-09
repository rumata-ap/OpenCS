using System.Runtime.InteropServices;

using CScore.Import;
using OpenCS.Services.Scad;
using Xunit;
using Xunit.Abstractions;

namespace OpenCS.Tests;

/// <summary>
/// Пробник граничных условий SCAD (подсрез 4б CSfea, 07.10): какие закрепления, связи конечной жёсткости,
/// объединения перемещений, шарниры, жёсткие вставки, жёсткие тела и упругое основание реально встречаются.
/// OPENCS_SCAD_BC_SPR — пути .SPR через «;»; OPENCS_SCAD_BC_OUT — файл для отчёта (необязательно).
/// Без переменной тест сразу выходит.
/// </summary>
public unsafe class ScadBoundaryProbeManualTests(ITestOutputHelper output)
{
    static string Mask(int m)
    {
        if (m == 0) return "—";
        string[] names = ["X", "Y", "Z", "UX", "UY", "UZ"];
        return string.Join("", Enumerable.Range(0, 6).Where(i => (m & (1 << i)) != 0).Select(i => names[i]))
            + ((m & ~0x3F) != 0 ? $"+0x{m & ~0x3F:X}" : "");
    }

    [Fact]
    public void Probe()
    {
        string? list = Environment.GetEnvironmentVariable("OPENCS_SCAD_BC_SPR");
        if (string.IsNullOrWhiteSpace(list)) return;
        string dir = Environment.GetEnvironmentVariable("OPENCS_SCAD_DIR") ?? ScadInstallLocator.FindDllDirectory()!;
        var native = ScadApiNative.Load(dir);
        nint lib = NativeLibrary.Load(native.DllPath);
        nint F(string name) => NativeLibrary.GetExport(lib, name);
        var getJoint = (delegate* unmanaged[Stdcall]<nint, uint, uint, byte*, double**, uint>)F("ApiGetJoint");
        var getRodJoint = (delegate* unmanaged[Stdcall]<nint, uint, ushort*, ushort*, double*, double*, ushort>)F("ApiGetRodJoint");
        var qUnite = (delegate* unmanaged[Stdcall]<nint, uint>)F("ApiGetQuantityBoundUnite");
        var getUnite = (delegate* unmanaged[Stdcall]<nint, uint, ushort*, uint*, uint**, ushort>)F("ApiGetBoundUnite");
        var qInsert = (delegate* unmanaged[Stdcall]<nint, uint>)F("ApiGetQuantityInsert");
        var getNumInsert = (delegate* unmanaged[Stdcall]<nint, uint, byte*, uint*, double**, uint*, uint**, int>)F("ApiGetNumInsert");
        var qBed = (delegate* unmanaged[Stdcall]<nint, uint>)F("ApiGetQuantityBed");
        var getBed = (delegate* unmanaged[Stdcall]<nint, uint, byte*, uint*, double**, uint*, uint**, int>)F("ApiGetBed");

        // Пишем сразу: падение в DLL роняет тестовый хост.
        string? outPath = Environment.GetEnvironmentVariable("OPENCS_SCAD_BC_OUT");
        void W(string s)
        {
            output.WriteLine(s);
            if (!string.IsNullOrEmpty(outPath)) File.AppendAllText(outPath, s + Environment.NewLine);
        }

        foreach (string spr in list.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            W($"=== {Path.GetFileName(spr)}");
            try
            {
                using var session = new ScadApiSession(native);
                session.Open(spr);
                var read = ScadApiReader.Read(session, new ScadReadOptions(OutputAxes: false, ConcreteGroups: false,
                    AssignedRebar: false, SteelGroups: false), null, CancellationToken.None);
                var data = read.Data;
                var model = data.AnalysisModel!;
                var stiff = data.Stiffnesses.ToDictionary(s => s.Id);
                nint h = session.Handle;
                W($"Узлов {data.Nodes.Count}, КЭ прочитано {data.Elements.Count}, ед. длины {data.LengthUnitM} м, силы {model.ForceUnitN} Н");

                // Все типы КЭ, в т. ч. пропущенные импортом; у пропущенных — тексты жёсткостей.
                var byType = new Dictionary<int, (int Count, HashSet<int> Nq, Dictionary<int, int> Stiff)>();
                uint elemCount = native.ApiGetElemQuantity(h);
                var bars = new List<uint>();
                for (uint i = 1; i <= elemCount; i++)
                {
                    if (native.ApiIsElemDeleted(h, i) != 0) continue;
                    uint type, rigid, qn;
                    uint* el;
                    if (native.ApiElemGetData(h, i, &type, &rigid, &qn, &el) != 0) continue;
                    if (!byType.TryGetValue((int)type, out var t)) byType[(int)type] = t = (0, [], []);
                    t.Nq.Add((int)qn);
                    t.Stiff[(int)rigid] = t.Stiff.GetValueOrDefault((int)rigid) + 1;
                    byType[(int)type] = (t.Count + 1, t.Nq, t.Stiff);
                    if (ScadElementKinds.Classify((int)type, (int)qn) == ScadElementKind.Beam) bars.Add(i);
                }
                foreach (var (type, t) in byType.OrderBy(x => x.Key))
                {
                    var kind = ScadElementKinds.Classify(type, t.Nq.First());
                    W($"  тип {type}: {t.Count} КЭ, узлов {string.Join("/", t.Nq.Order())}, {kind}, жёсткостей {t.Stiff.Count}");
                    if (kind != ScadElementKind.Skip) continue;
                    foreach (var (sid, cnt) in t.Stiff.OrderByDescending(x => x.Value).Take(6))
                    {
                        string txt = stiff.GetValueOrDefault(sid)?.Text?.ReplaceLineEndings(" ") ?? "?";
                        if (txt.Length > 160) txt = txt[..160] + "…";
                        W($"      ж.{sid} ×{cnt}: {txt}");
                    }
                }

                // Закрепления.
                W($"  Закреплений {model.Bounds.Count}: " + string.Join(", ", model.Bounds.GroupBy(b => b.Value)
                    .OrderByDescending(g => g.Count()).Select(g => $"{Mask(g.Key)}×{g.Count()}")));

                // Жёсткие тела.
                if (model.RigidBodies.Count > 0)
                    W($"  Жёстких тел {model.RigidBodies.Count}: маски " + string.Join(", ", model.RigidBodies.GroupBy(b => b.Mask)
                        .Select(g => $"{Mask(g.Key)}×{g.Count()}")) + $"; ведомых {model.RigidBodies.Min(b => b.SlaveNodes.Length)}…" +
                        $"{model.RigidBodies.Max(b => b.SlaveNodes.Length)}; ведущий закреплён у " +
                        $"{model.RigidBodies.Count(b => model.Bounds.ContainsKey(b.MasterNode))}, ведомый закреплён у " +
                        $"{model.RigidBodies.Count(b => b.SlaveNodes.Any(model.Bounds.ContainsKey))}");

                W("  [объединения]");
                // Объединения перемещений.
                uint nu = qUnite(h);
                if (nu > 0)
                {
                    var masks = new Dictionary<int, (int Groups, int Nodes, int Max)>();
                    for (uint g = 1; g <= nu; g++)
                    {
                        ushort m; uint qn; uint* ln;
                        if (getUnite(h, g, &m, &qn, &ln) != 0) continue;
                        var v = masks.GetValueOrDefault(m);
                        masks[m] = (v.Groups + 1, v.Nodes + (int)qn, Math.Max(v.Max, (int)qn));
                    }
                    W($"  Объединений перемещений {nu}: " + string.Join(", ", masks.Select(x =>
                        $"{Mask(x.Key)}: групп {x.Value.Groups}, узлов {x.Value.Nodes}, max {x.Value.Max}")));
                }

                W("  [шарниры]");
                // Шарниры стержней: ApiGetJoint по обоим узлам; упругие — ненулевые значения.
                var joints = new Dictionary<string, int>();
                var samples = new List<string>();
                int rodJointDiff = 0;
                foreach (uint e in bars)
                {
                    int combined = 0;
                    for (uint k = 1; k <= 2; k++)
                    {
                        byte place; double* val = null;
                        uint m = getJoint(h, e, k, &place, &val);
                        if (m == 0) continue;
                        combined |= (int)(m & 0x3F) << (6 * ((int)k - 1));
                        bool elastic = false;
                        string vals = "";
                        if (val != null)
                        {
                            var vv = new ReadOnlySpan<double>(val, 6).ToArray();
                            elastic = vv.Any(x => x != 0);
                            vals = string.Join(" ", vv.Select(x => x.ToString("G4")));
                        }
                        string key = $"{Mask((int)m)} {(place == 1 ? "в узле" : "гибк.часть")}{(elastic ? " упругий" : "")}";
                        joints[key] = joints.GetValueOrDefault(key) + 1;
                        if (elastic && samples.Count < 5) samples.Add($"КЭ {e} узел {k}: {vals}");
                    }
                    ushort je, ji;
                    double* de = stackalloc double[12];
                    double* di = stackalloc double[12];
                    if (getRodJoint(h, e, &je, &ji, de, di) == 0 && ((je | ji) & 0xFFF) != combined) rodJointDiff++;
                }
                if (joints.Count > 0)
                    W($"  Шарниры (концов {joints.Values.Sum()}): " + string.Join(", ", joints.OrderByDescending(x => x.Value).Select(x => $"{x.Key}×{x.Value}")));
                foreach (var s in samples) W("      " + s);
                if (rodJointDiff > 0) W($"  ! ApiGetRodJoint расходится с ApiGetJoint у {rodJointDiff} КЭ");

                W("  [вставки]");
                // Жёсткие вставки.
                uint ni = qInsert(h);
                for (uint g = 1; g <= ni; g++)
                {
                    byte type; uint qs, qe; double* size; uint* le;
                    if (getNumInsert(h, g, &type, &qs, &size, &qe, &le) == 0) continue;
                    var sz = size == null ? "" : string.Join(" ", new ReadOnlySpan<double>(size, (int)qs).ToArray().Select(x => x.ToString("G4")));
                    W($"  Вставка {g}: СК {type}, данные [{sz}], КЭ {qe}");
                }

                W("  [основание]");
                // Упругое основание.
                uint nb = qBed(h);
                for (uint g = 1; g <= nb && g <= 10; g++)
                {
                    byte type; uint qs, qe; double* size; uint* le;
                    if (getBed(h, g, &type, &qs, &size, &qe, &le) == 0) continue;
                    var sz = size == null ? "" : string.Join(" ", new ReadOnlySpan<double>(size, (int)qs).ToArray().Select(x => x.ToString("G4")));
                    W($"  Упругое основание {g}: тип {type}, данные [{sz}], КЭ {qe}");
                }
                if (nb > 10) W($"  … всего групп основания {nb}");
            }
            catch (Exception ex)
            {
                W($"  ОШИБКА: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }
}
