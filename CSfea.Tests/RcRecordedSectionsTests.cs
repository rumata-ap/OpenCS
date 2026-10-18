using CScore;
using CSfea.Core;
using CSfea.CScoreBridge;
using CSfea.CScoreBridge.Structural;

namespace CSfea.Tests;

/// <summary>
/// Линейный расчёт на записанных секущих жёсткостях (<see cref="RecordedRcSectionFactory"/>, инженерная нелинейность):
/// определяющая нагрузка секущего прогона, решённая линейно на матрицах его конечного состояния, воспроизводит его
/// перемещения (ЖБ-балка с трещинами — матрицы КЭ стержней; ЖБ-полоса с осью сечения под углом — ABD); нижний предел
/// e_min сохраняет симметрию и положительную определённость; поля линейного расчёта — с начальными жёсткостями.
/// </summary>
[HarnessChecks]
public class RcRecordedSectionsTests
{
    [Fact]
    public static void RunAll()
    {
        TestHarness.Section("Записанные жёсткости: ЖБ-балка с трещинами, две стадии");
        RunCrackedBeam();

        TestHarness.Section("Записанные жёсткости: ЖБ-полоса, ось сечения под 30°");
        RunStrip();

        TestHarness.Section("Записанные жёсткости: нижний предел e_min");
        RunFloor();
    }

    static RcStructuralModel Beam(out int mid)
    {
        const int n = 12;
        const double span = 6.0, q = 8 * 30.0 / (span * span);
        var rs = new RcBeamSection("ЖБ 1×0,2")
        {
            Cross = RcTestMaterials.Rect(1.0, 0.2, 0.03, 7.85e-4, 2.5e-4, nBars: 5, nx: 4, ny: 40), Calc = CalcType.N, TorsionGJ = 1e7,
        };
        var m = new RcStructuralModel();
        for (int i = 0; i <= n; i++) m.Nodes.Add(new RcNode(i, span * i / n, 0, 0));
        for (int i = 0; i < n; i++) m.Beams.Add(new RcBeam(i, i, i + 1, rs, [0.0, 1.0, 0.0]));
        m.Supports.Add(new RcSupport(0, 0b001111));
        m.Supports.Add(new RcSupport(n, 0b000110));
        var lc = new RcLoadCase(1, "q");
        foreach (var b in m.Beams) lc.Beams.Add(new RcBeamLoad(b.Id, [0, 0, -q * 1e3]));
        m.LoadCases.Add(lc);
        m.Stages.Add(new RcStage("0,6q", [(1, 0.6)], 2));
        m.Stages.Add(new RcStage("+0,4q", [(1, 0.4)], 2));
        mid = n / 2;
        return m;
    }

    /// <summary>Линейный расчёт модели одним загружением «q» с коэффициентом 1 на записанных матрицах.</summary>
    static (RcLinearRun Run, RecordedRcSectionFactory Sections) Linear(RcStructuralModel m, RcSecantStepFields f, double eMin = 0.01)
    {
        m.Stages.Clear();
        m.Stages.Add(new RcStage("q", [(1, 1.0)]));
        var sections = new RecordedRcSectionFactory(f, eMin);
        return (RcLinearAnalysis.Run(m, new RcLinearOptions { Sections = sections }), sections);
    }

    /// <summary>max |u_lin·k − u_src| / max |u_src| по поступательным перемещениям узлов.</summary>
    static double Mismatch(RcLinearRun run, RcSecantStepFields f, double k)
    {
        double diff = 0, max = 0;
        for (int n = 0; n < f.NodeIds.Length; n++)
        {
            int g = run.Build.NodeIndex[f.NodeIds[n]];
            for (int c = 0; c < 3; c++)
            {
                double us = f.Displacements[6 * n + c];
                max = Math.Max(max, Math.Abs(us));
                diff = Math.Max(diff, Math.Abs(k * run.Cases[0].U[6 * g + c] - us));
            }
        }
        return diff / max;
    }

    static void RunCrackedBeam()
    {
        var m = Beam(out int mid);
        var f = RcSecantAnalysis.Run(m, new RcSecantOptions()).LastConvergedFields();
        TestHarness.Check("секущий: конечное состояние с матрицами КЭ", f is { HasElementMatrices: true }, "");
        if (f == null) return;
        TestHarness.Check("секущий: в середине трещины", (f.BeamFlags[mid] & RcSecantStepFields.Cracked) != 0);

        var (run, sections) = Linear(m, f);
        TestHarness.Check("линейный: матрицы точные, предел не сработал", sections.Exact && sections.FlooredBeams.Count == 0,
            $"предел у {sections.FlooredBeams.Count}");
        double mis = Mismatch(run, f, 1.0);
        TestHarness.Check("линейный на секущих: u определяющей = u секущего (1e-9)", mis < 1e-9, $"{mis:e2}");

        // Начальные жёсткости полей — упругие исходного сечения (у сечения CScore — начальные записи), секущие — источника.
        var lf = run.Cases[0].Fields;
        int o = mid * RcSecantStepFields.BeamStiffnessComponents;
        TestHarness.CheckRel("поля: EIy₀ = начальная записи", lf.BeamStiffness[o + 16], f.BeamStiffness[o + 16], 1e-9);
        TestHarness.Check("поля: в середине EIy < 0,6·EIy₀", lf.BeamStiffness[o + 1] < 0.6 * lf.BeamStiffness[o + 16],
            $"{lf.BeamStiffness[o + 1] / lf.BeamStiffness[o + 16]:0.###}");

        // Старая запись (без матриц КЭ) — средняя податливость точек: близко, но не точно.
        var old = WithoutElementMatrices(f);
        var m2 = Beam(out _);
        var (runOld, sectionsOld) = Linear(m2, old);
        double misOld = Mismatch(runOld, old, 1.0);
        TestHarness.Check("старая запись: приближённо (< 5 %), не точно", !sectionsOld.Exact && misOld < 5e-2, $"{misOld:e2}");
    }

    static void RunStrip()
    {
        const double span = 6.0;
        var m = SecantPicardTests.StripModel(8 * 30.0 / (span * span), 30, out _);
        var f = RcSecantAnalysis.Run(m, new RcSecantOptions()).LastConvergedFields();
        TestHarness.Check("полоса: конечное состояние с матрицами", f is { HasMatrices: true });
        if (f == null) return;
        TestHarness.Check("полоса: трещины есть", f.ShellFlags.Any(x => (x & RcSecantStepFields.Cracked) != 0));
        var (run, sections) = Linear(m, f);
        double mis = Mismatch(run, f, 1.0);
        TestHarness.Check("полоса: u определяющей = u секущего (1e-9)", mis < 1e-9, $"{mis:e2}");
        TestHarness.Check("полоса: начальные жёсткости — упругие (ABD бетона)", sections.InitialShell(m.Shells[0].Id) != null);
    }

    static void RunFloor()
    {
        var k = new[,] { { 1.0, 0.5, 0.1 }, { 0.5, 4.0, 0.2 }, { 0.1, 0.2, 9.0 } };
        bool any = StiffnessFloor.Apply(k, [10.0, 10.0, 10.0], 0.2);
        TestHarness.Check("предел сработал", any);
        TestHarness.CheckRel("k11 = 0,2·10", k[0, 0], 2.0, 1e-12);
        TestHarness.CheckRel("k22 не тронут (4 ≥ 2)", k[1, 1], 4.0, 1e-12);
        TestHarness.CheckRel("k33 не тронут", k[2, 2], 9.0, 1e-12);
        TestHarness.Check("симметрия", k[0, 1] == k[1, 0] && k[0, 2] == k[2, 0] && k[1, 2] == k[2, 1]);
        TestHarness.CheckRel("k12 масштабирован на √2", k[0, 1], 0.5 * Math.Sqrt(2.0), 1e-12);
        TestHarness.Check("положительно определена", PositiveDefinite(k));

        var z = new[,] { { -1.0, 0.3 }, { 0.3, 5.0 } };
        StiffnessFloor.Apply(z, [10.0, 10.0], 0.1);
        TestHarness.Check("неположительная диагональ → предел, связи обнулены", z[0, 0] == 1.0 && z[0, 1] == 0 && z[1, 0] == 0 && z[1, 1] == 5.0);

        var m = Beam(out _);
        var f = RcSecantAnalysis.Run(m, new RcSecantOptions()).LastConvergedFields()!;
        var (run, sections) = Linear(m, f, eMin: 0.9);
        TestHarness.Check("e_min = 0,9: предел сработал у треснувших КЭ", sections.FlooredBeams.Count > 0, $"{sections.FlooredBeams.Count}");
        TestHarness.Check("e_min = 0,9: u меньше секущего", Mismatch(run, f, 1.0) > 1e-3);
    }

    static bool PositiveDefinite(double[,] a)
    {
        int n = a.GetLength(0);
        var l = new double[n, n];
        for (int i = 0; i < n; i++)
            for (int j = 0; j <= i; j++)
            {
                double s = a[i, j];
                for (int k = 0; k < j; k++) s -= l[i, k] * l[j, k];
                if (i == j)
                {
                    if (s <= 0) return false;
                    l[i, i] = Math.Sqrt(s);
                }
                else l[i, j] = s / l[j, j];
            }
        return true;
    }

    static RcSecantStepFields WithoutElementMatrices(RcSecantStepFields f) => new()
    {
        Stage = f.Stage, Step = f.Step, LoadFactor = f.LoadFactor, IsRefinement = f.IsRefinement,
        NodeIds = f.NodeIds, Displacements = f.Displacements,
        ShellIds = f.ShellIds, ShellForces = f.ShellForces, ShellStates = f.ShellStates, ShellFlags = f.ShellFlags,
        BeamIds = f.BeamIds, BeamForces = f.BeamForces, BeamFlags = f.BeamFlags,
        HasStiffness = f.HasStiffness, ShellStiffness = f.ShellStiffness, BeamStiffness = f.BeamStiffness,
        HasMatrices = true, ShellMatrices = f.ShellMatrices, BeamMatrices = f.BeamMatrices,
    };
}
