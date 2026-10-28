using System.Text.Json;
using CScore.Fem;
using CScore.Fem.Fragment.Masks;
using CScore.Planar;
using CScore.Submodel;
using CSfea.CScoreBridge.Structural;
using OpenCS.Services;
using Xunit;

namespace OpenCS.Tests;

/// <summary>
/// Результат родителя для масок по полям CSfea (срез 3 C/D, задача 1): значения интерфейса по настоящему линейному
/// расчёту против аналитики — перемещения консоли-колонны, силы концов в местных осях с поворотом КонЭ, нагрузка КЭ по
/// замыканию сил концов, усилия пластин стены с осями выдачи (угол КЭ, ось области, обратный обход).
/// </summary>
public sealed class CsfeaFieldsParentResultTests
{
    const double E = 30e9, G = 12.5e9, A = 0.16, I = 2e-3, J = 3e-3;
    static readonly FragmentMaskKey Key = FragmentMaskKey.OfLoadCase(1);

    sealed class Props : IFemElementStiffnessSource
    {
        public FemShellStiffness? Shell(FemElement e) => e.ElemType == "shell" ? new(E, 0.0, 0.2) : null;
        public FemBarStiffness? Bar(FemElement e) => e.ElemType == "beam" ? new(E, G, A, I, I, J) : null;
        public double? UnitWeight(FemElement e) => null;
        public double? BarArea(FemElement e) => e.ElemType == "beam" ? A : null;
    }

    static FemMeshNode N(int tag, double x, double y, double z, string? source = null) =>
        new() { NodeTag = tag.ToString(), X = x, Y = y, Z = z, SourceNodeTag = source };

    static FemElement El(int tag, string type, int[] nodes, string? member = null) => new()
    {
        ElemTag = tag.ToString(), ElemType = type, NodeIdsJson = JsonSerializer.Serialize(nodes), SourceMemberTag = member,
        ThicknessM = type == "shell" ? 0.2 : null,
    };

    static CsfeaFieldsParentResult Solve(FemRcModelInput input)
    {
        var adapted = FemRcModelAdapter.Adapt(input);
        Assert.False(adapted.HasErrors, string.Join("; ", adapted.Report));
        var run = RcLinearAnalysis.Run(adapted.Model, new RcLinearOptions { ShellForceAngles = adapted.ShellForceAngles });
        var nodes = input.MeshNodes.ToDictionary(n => n.NodeTag, StringComparer.Ordinal);
        return new CsfeaFieldsParentResult([(Key, run.Cases[0].Fields)], nodes, input.MeshElements, input.Members,
            input.RegionAxisX);
    }

    static void Near(double expected, double actual, double scale, string what) =>
        Assert.True(Math.Abs(expected - actual) <= 1e-7 * scale, $"{what}: ожидалось {expected:G9}, получено {actual:G9}");

    static void Near(Dof6 expected, Dof6 actual, double forceScale, double momentScale, string what)
    {
        for (int d = 0; d < 6; d++) Near(expected[d], actual[d], d < 3 ? forceScale : momentScale, $"{what}[{d}]");
    }

    /// <summary>
    /// Консоль-колонна H = 3 м (КЭ 11: 0…1,5; 12: 1,5…3), заделка внизу, сила P по +X наверху, КонЭ повёрнут на 30°.
    /// Перемещения: u_x(z) = P·z²(3H − z)/(6EI), θ_y = du_x/dz. Силы узлов на КЭ (общие оси): у КЭ 11 f_I = реакция
    /// (−P, 0, 0 | 0, −PH, 0), M_J = (0, P·H/2, 0); у КЭ 12 f_I = (−P, 0, 0 | 0, −P·H/2, 0), f_J = (P, 0, 0 | 0). Местные оси
    /// при 30°: x = Z, y = (cos, sin, 0), z = (−sin, cos, 0) → N = f·x, Qy = f·y, Qz = f·z (моменты так же).
    /// </summary>
    [Fact]
    public void Column_DisplacementsAndEndForces_InRotatedLocalAxes()
    {
        const double h = 3, p = 10e3, ei = E * I;
        var fem = new[] { new FemNode { Id = 1, NodeTag = "1", DofMask = 0x3F }, new FemNode { Id = 2, NodeTag = "2", Z = h } };
        var member = new FemMember { Id = 5, ElemTag = "C", ElemType = "beam", NodeIdsJson = "[1,2]", RotationDeg = 30 };
        var input = new FemRcModelInput
        {
            MeshNodes = [N(1, 0, 0, 0, "1"), N(2, 0, 0, 1.5), N(3, 0, 0, h, "2")],
            MeshElements = [El(11, "beam", [1, 2], "C"), El(12, "beam", [2, 3], "C")],
            Nodes = fem, Members = [member], Properties = new Props(),
            LoadCases = [new FemLoadCase { Id = 1, Tag = "P" }],
            MeshNodeLoads = [new FemMeshNodeLoad { LoadCaseId = 1, MeshNodeTag = "3", Fx = p }],
            Stages = [new FemRcStage("S", [(1, 1.0)])],
        };
        var r = Solve(input);
        Assert.Equal([Key], r.Keys);

        double Ux(double z) => p * z * z * (3 * h - z) / (6 * ei);
        double Ry(double z) => p * (2 * h * z - z * z) / (2 * ei);
        Assert.True(r.TryNodeDisplacement(Key, "2", out var u2));
        Assert.True(r.TryNodeDisplacement(Key, "3", out var u3));
        Near(new Dof6(Ux(1.5), 0, 0, 0, Ry(1.5), 0), u2, Ux(h), Ry(h), "u узла 2");
        Near(new Dof6(Ux(h), 0, 0, 0, Ry(h), 0), u3, Ux(h), Ry(h), "u узла 3");

        double c = Math.Cos(Math.PI / 6), s = Math.Sin(Math.PI / 6);
        var x = new PlanarVector3(0, 0, 1);
        var y = new PlanarVector3(c, s, 0);
        var z = new PlanarVector3(-s, c, 0);
        Dof6 Local(PlanarVector3 f, PlanarVector3 m) => new(f.Dot(x), f.Dot(y), f.Dot(z), m.Dot(x), m.Dot(y), m.Dot(z));
        var fP = new PlanarVector3(p, 0, 0);
        Assert.True(r.TryBeamEndForces(Key, "11", out var e11));
        Near(Local(fP * -1, new(0, -p * h, 0)), e11.I, p, p * h, "КЭ 11, конец I");
        Near(Local(fP, new(0, p * h / 2, 0)), e11.J, p, p * h, "КЭ 11, конец J");
        Assert.True(r.TryBeamEndForces(Key, "12", out var e12));
        Near(Local(fP * -1, new(0, -p * h / 2, 0)), e12.I, p, p * h, "КЭ 12, конец I");
        Near(Local(fP, PlanarVector3.Zero), e12.J, p, p * h, "КЭ 12, конец J");

        Assert.True(r.KnowsBeamLoads);
        Assert.Empty(r.BeamLoads(Key, "11"));
        Assert.False(r.TryNodeDisplacement(Key, "99", out _));
        Assert.False(r.TryBeamEndForces(Key, "77", out _));
        Assert.False(r.TryShellForces(Key, "11", out _));
        Assert.False(r.TryNodeDisplacement(FragmentMaskKey.OfLoadCase(2), "2", out _));
    }

    /// <summary>
    /// Горизонтальная консоль L = 4 м (один КЭ), равномерная q по −Z: нагрузка КЭ по замыканию сил концов
    /// q = −(f_I + f_J)/L = (0, 0, −q) в общих осях; действие части за серединой на часть до неё —
    /// F = (0, 0, −qL/2), M = (0, qL²/8, 0) — не зависит от поворота сечения.
    /// </summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(37.0)]
    public void Cantilever_SpanLoadFromEndForces_GivesSectionAction(double rotationDeg)
    {
        const double l = 4, q = 10e3;
        var fem = new[] { new FemNode { Id = 1, NodeTag = "1", DofMask = 0x3F }, new FemNode { Id = 2, NodeTag = "2", X = l } };
        var member = new FemMember { Id = 7, ElemTag = "B", ElemType = "beam", NodeIdsJson = "[1,2]", RotationDeg = rotationDeg };
        var input = new FemRcModelInput
        {
            MeshNodes = [N(1, 0, 0, 0, "1"), N(2, l, 0, 0, "2")],
            MeshElements = [El(1, "beam", [1, 2], "B")],
            Nodes = fem, Members = [member], Properties = new Props(),
            LoadCases = [new FemLoadCase { Id = 1, Tag = "q" }],
            MemberLoads =
            [
                new FemMemberLoad
                {
                    LoadCaseId = 1, MemberId = 7, CoordinateSystem = "global", DistributionType = "uniform", QzStart = -q, QzEnd = -q,
                },
            ],
            Stages = [new FemRcStage("S", [(1, 1.0)])],
        };
        var r = Solve(input);

        var load = Assert.Single(r.BeamLoads(Key, "1"));
        Assert.False(load.IsPoint);
        Assert.Equal((0.0, 1.0), (load.A, load.B));
        Near(-q, load.QA.Z, q, "q_z");
        Near(0, load.QA.X, q, "q_x");
        Near(0, load.QA.Y, q, "q_y");
        Assert.Equal(load.QA, load.QB);

        Assert.True(r.TryBeamEndForces(Key, "1", out var ends));
        double rot = FemRcModelAdapter.BeamRotationDeg(input.MeshElements[0], new Dictionary<string, FemMember> { ["B"] = member });
        Assert.Equal(rotationDeg, rot);
        var action = FragmentBarCutSampler.SectionAction(new(0, 0, 0), new(l, 0, 0), rot, ends, r.BeamLoads(Key, "1"), 0.5);
        Near(new Dof6(0, 0, -q * l / 2, 0, q * l * l / 8, 0), action, q * l, q * l * l, "действие в середине");
    }

    /// <summary>
    /// Стена 2 × 2 м в плоскости XZ (КЭ 1 × 1 м), ν = 0, низ заделан, верх нагружен узловыми силами равномерного
    /// сжатия N = −50 кН/м по Z: поле одноосное, N_zz = N во всех КЭ. Оси выдачи: КЭ 1 — повёрнуты на 30° вокруг нормали
    /// −Y (x = (cos, 0, sin)); КЭ 2 — X1 = +X, Y1 = +Z; КЭ 3 — ось области КонЭ «W» = +Z; КЭ 4 — обратный обход (нормаль +Y,
    /// X1 = −X). Во всех КЭ: тензор Nx·x⊗x + Ny·y⊗y + Nxy·(x⊗y + y⊗x) по осям интерфейса = N·ẑ⊗ẑ.
    /// </summary>
    [Fact]
    public void Wall_ShellForces_WithOutputAxes()
    {
        const double n = -50e3;
        var nodes = new List<FemMeshNode>();
        for (int j = 0; j < 3; j++)
            for (int i = 0; i < 3; i++)
                nodes.Add(N(1 + i + 3 * j, i, 0, j));
        var e1 = El(1, "shell", [1, 2, 4, 5]);
        e1.LocalAxisAngleDeg = 30;
        var elements = new[] { e1, El(2, "shell", [2, 3, 5, 6]), El(3, "shell", [4, 5, 7, 8], "W"), El(4, "shell", [6, 5, 9, 8]) };
        var input = new FemRcModelInput
        {
            MeshNodes = nodes, MeshElements = elements, Properties = new Props(),
            Members = [new FemMember { Id = 3, ElemTag = "W", ElemType = "shell", NodeIdsJson = "[]" }],
            RegionAxisX = new Dictionary<string, PlanarVector3> { ["W"] = new(0, 0, 1) },
            Supports = new[] { 1, 2, 3 }.Select(t => new FemMeshNodeSupport { NodeTag = t.ToString(), Mask = 0x3F }).ToList(),
            LoadCases = [new FemLoadCase { Id = 1, Tag = "N" }],
            MeshNodeLoads =
            [
                new FemMeshNodeLoad { LoadCaseId = 1, MeshNodeTag = "7", Fz = n / 2 },
                new FemMeshNodeLoad { LoadCaseId = 1, MeshNodeTag = "8", Fz = n },
                new FemMeshNodeLoad { LoadCaseId = 1, MeshNodeTag = "9", Fz = n / 2 },
            ],
            Stages = [new FemRcStage("S", [(1, 1.0)])],
        };
        var r = Solve(input);

        double c = Math.Cos(Math.PI / 6), s = Math.Sin(Math.PI / 6);
        var expected = new Dictionary<string, (PlanarVector3 X, PlanarVector3 Y, double Nx, double Ny, double Nxy)>
        {
            ["1"] = (new(c, 0, s), new(-s, 0, c), n * s * s, n * c * c, n * s * c),
            ["2"] = (new(1, 0, 0), new(0, 0, 1), 0, n, 0),
            ["3"] = (new(0, 0, 1), new(-1, 0, 0), n, 0, 0),
            ["4"] = (new(-1, 0, 0), new(0, 0, 1), 0, n, 0),
        };
        foreach (var (tag, ex) in expected)
        {
            Assert.True(r.TryShellForces(Key, tag, out var f), tag);
            Assert.True((f.AxisX - ex.X).Length < 1e-12 && (f.AxisY - ex.Y).Length < 1e-12, $"оси КЭ {tag}: {f.AxisX}, {f.AxisY}");
            Assert.Equal(FragmentShellForces.Components, f.Values.Length);
            Near(ex.Nx, f.Values[0], -n, $"Nx КЭ {tag}");
            Near(ex.Ny, f.Values[1], -n, $"Ny КЭ {tag}");
            Near(ex.Nxy, f.Values[2], -n, $"Nxy КЭ {tag}");
            for (int k = 3; k < 8; k++) Near(0, f.Values[k], -n, $"компонента {k} КЭ {tag}");
            // Тензор в общих осях: сила на площадке с нормалью ẑ.
            var t = f.AxisX * (f.Values[0] * f.AxisX.Z + f.Values[2] * f.AxisY.Z) + f.AxisY * (f.Values[2] * f.AxisX.Z + f.Values[1] * f.AxisY.Z);
            Assert.True((t - new PlanarVector3(0, 0, n)).Length < 1e-7 * -n, $"N·ẑ КЭ {tag}: {t}");
        }
        Assert.False(r.TryShellForces(Key, "9", out _));
        Assert.False(r.TryBeamEndForces(Key, "1", out _));
    }
}
