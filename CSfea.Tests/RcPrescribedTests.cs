using CSfea.CScoreBridge.Structural;
using CSfea.Core;

namespace CSfea.Tests;

/// <summary>Заданные перемещения загружений <see cref="RcPrescribed"/>: сборка, линейный и секущий расчёт модели.</summary>
[HarnessChecks]
public class RcPrescribedTests
{
    private const double E = 30e9;
    private const double I = 2.13e-3;

    [Fact]
    public static void RunAll()
    {
        RunBuildDofsAndCombination();
        RunBuildErrors();
    }

    private static readonly RcBeamSection Sq = new("sq 0.4") { Elastic = new BeamSection(E, 0.16, I, I, 3.6e-3) };

    /// <summary>Балка вдоль X: узлы 10…10+n, стержни 100…; refVec = Z.</summary>
    private static RcStructuralModel Line(int n, double l)
    {
        var m = new RcStructuralModel();
        for (int i = 0; i <= n; i++) m.Nodes.Add(new RcNode(10 + i, l * i / n, 0.0, 0.0));
        for (int i = 0; i < n; i++) m.Beams.Add(new RcBeam(100 + i, 10 + i, 11 + i, Sq, new[] { 0.0, 0.0, 1.0 }));
        return m;
    }

    private static bool Throws<T>(Action a, string? contains = null) where T : Exception
    {
        try { a(); return false; }
        catch (T ex) { return contains == null || ex.Message.Contains(contains); }
    }

    /// <summary>
    /// Два загружения задают разные DOF одного свободного узла: оба DOF входят в закрепления сборки, у загружения без
    /// значения на DOF — 0; сочетание — сумма с коэффициентами; загружение без заданных перемещений — null.
    /// </summary>
    private static void RunBuildDofsAndCombination()
    {
        TestHarness.Section("RcPrescribed: закрепления сборки и сочетания");
        var m = Line(3, 6.0);
        m.Supports.Add(new RcSupport(10, 0x3F));
        m.Supports.Add(new RcSupport(13, 0x3F));
        var lc1 = new RcLoadCase(1, "осадка");
        lc1.Prescribed.Add(new RcPrescribed(12, 2, -0.01));
        var lc2 = new RcLoadCase(2, "сдвиг");
        lc2.Prescribed.Add(new RcPrescribed(12, 0, 0.003));
        lc2.Prescribed.Add(new RcPrescribed(13, 4, 0.002));   // поворот заделки — DOF уже закреплён опорой
        var lc3 = new RcLoadCase(3, "сила");
        lc3.Nodal.Add(new RcNodalLoad(11, new[] { 0.0, 0.0, -1e4, 0, 0, 0 }));
        m.LoadCases.AddRange(new[] { lc1, lc2, lc3 });
        var build = RcStructuralMeshBuilder.Build(m, new LinearRcSectionFactory());

        var fixedDofs = build.Bc.FixedDofs;
        int uz12 = build.Dof(12, 2), ux12 = build.Dof(12, 0), ry13 = build.Dof(13, 4);
        TestHarness.Check("uz и ux узла 12 закреплены", fixedDofs.Contains(uz12) && fixedDofs.Contains(ux12));
        TestHarness.Check("прочие DOF узла 12 свободны", !fixedDofs.Contains(build.Dof(12, 1)) && !fixedDofs.Contains(build.Dof(12, 4)));
        TestHarness.Check("PrescribedDofs = {uz12, ux12, θy13}",
            build.PrescribedDofs.SetEquals(new[] { uz12, ux12, ry13 }), string.Join(",", build.PrescribedDofs));
        TestHarness.Check("реакции заданных DOF — как опора (SupportDofs)",
            build.SupportDofs.Contains(uz12) && build.SupportDofs.Contains(ux12));

        double At(double[] v, int dof) => v[Array.IndexOf(fixedDofs, dof)];
        var p1 = build.PrescribedCombination([(1, 1.0)]);
        TestHarness.Check("загружение 1: значения есть", p1 != null && p1.Length == fixedDofs.Length);
        if (p1 != null)
        {
            TestHarness.CheckRel("загружение 1: uz12 = −0,01", At(p1, uz12), -0.01, 1e-15);
            TestHarness.Check("загружение 1: ux12 = 0, θy13 = 0", At(p1, ux12) == 0 && At(p1, ry13) == 0);
        }
        var p12 = build.PrescribedCombination([(1, 1.5), (2, 0.5), (3, 2.0)]);
        if (p12 != null)
        {
            TestHarness.CheckRel("1,5·(1) + 0,5·(2): uz12", At(p12, uz12), -0.015, 1e-15);
            TestHarness.CheckRel("1,5·(1) + 0,5·(2): ux12", At(p12, ux12), 0.0015, 1e-15);
            TestHarness.CheckRel("1,5·(1) + 0,5·(2): θy13", At(p12, ry13), 0.001, 1e-15);
        }
        else TestHarness.Check("сочетание 1 + 2: значения есть", false);
        TestHarness.Check("загружение без заданных перемещений → null", build.PrescribedCombination([(3, 1.0)]) == null);
        TestHarness.Check("нулевой коэффициент → null", build.PrescribedCombination([(1, 0.0)]) == null);
    }

    /// <summary>Ошибки сборки: узел без КЭ, ведомый DOF жёсткого тела, DOF вне 0…5, повтор; пружина — предупреждение.</summary>
    private static void RunBuildErrors()
    {
        TestHarness.Section("RcPrescribed: ошибки и предупреждения сборки");
        RcStructuralModel Model(Action<RcStructuralModel, RcLoadCase> setup)
        {
            var m = Line(2, 4.0);
            m.Supports.Add(new RcSupport(10, 0x3F));
            var lc = new RcLoadCase(1, "u");
            m.LoadCases.Add(lc);
            setup(m, lc);
            return m;
        }
        RcStructuralMeshBuild Build(RcStructuralModel m) => RcStructuralMeshBuilder.Build(m, new LinearRcSectionFactory());

        TestHarness.Check("узел без КЭ → ArgumentException с номером узла", Throws<ArgumentException>(() => Build(Model((m, lc) =>
        {
            m.Nodes.Add(new RcNode(20, 9.0, 0.0, 0.0));
            lc.Prescribed.Add(new RcPrescribed(20, 2, 0.01));
        })), "20"));
        TestHarness.Check("ведомый DOF жёсткого тела → ArgumentException с номером узла", Throws<ArgumentException>(() => Build(Model((m, lc) =>
        {
            m.Nodes.Add(new RcNode(21, 4.5, 0.0, 0.0));
            m.RigidBodies.Add(new RcRigidBody(1, 12, new[] { 21 }));
            lc.Prescribed.Add(new RcPrescribed(21, 2, 0.01));
        })), "21"));
        TestHarness.Check("DOF 6 → ArgumentException", Throws<ArgumentException>(() => Build(Model((_, lc) =>
            lc.Prescribed.Add(new RcPrescribed(12, 6, 0.01))))));
        TestHarness.Check("повтор DOF в загружении → ArgumentException", Throws<ArgumentException>(() => Build(Model((_, lc) =>
        {
            lc.Prescribed.Add(new RcPrescribed(12, 2, 0.01));
            lc.Prescribed.Add(new RcPrescribed(12, 2, 0.02));
        }))));
        var withSpring = Build(Model((m, lc) =>
        {
            m.Springs.Add(new RcSpring(12, 2, 1e7));
            lc.Prescribed.Add(new RcPrescribed(12, 2, -0.01));
        }));
        TestHarness.Check("пружина на заданном DOF — предупреждение в отчёте",
            withSpring.Report.Any(r => r.Contains("12") && r.Contains("пружин")), string.Join(" | ", withSpring.Report));
    }
}
