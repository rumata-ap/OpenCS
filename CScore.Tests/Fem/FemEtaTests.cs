using System.Text.Json;
using CScore.Fem;
using Xunit;

namespace CScore.Tests.Fem;

/// <summary>Продольный изгиб в проверке по КЭ: длина между раскреплениями, ψ и параметры строк.</summary>
public class FemEtaTests
{
    static FemMeshNode Node(int tag, double x, double y, double z) =>
        new() { NodeTag = tag.ToString(), X = x, Y = y, Z = z };

    static FemElement Bar(int tag, int i, int j) =>
        new() { ElemTag = tag.ToString(), ElemType = "beam", NodeIdsJson = $"[{i},{j}]" };

    static FemElement Shell(int tag, params int[] nodes) =>
        new() { ElemTag = tag.ToString(), ElemType = "shell", NodeIdsJson = JsonSerializer.Serialize(nodes) };

    /// <summary>
    /// Колонна в два этажа по 3 м, каждый этаж — два КЭ по 1,5 м; на отметке 3 м к колонне примыкает
    /// пластина перекрытия, на 6 м — ригель. Длина каждого КЭ — высота своего этажа.
    /// </summary>
    [Fact]
    public void BarLength_IsStoreyHeightBetweenBracingNodes()
    {
        FemMeshNode[] nodes =
        [
            Node(1, 0, 0, 0), Node(2, 0, 0, 1.5), Node(3, 0, 0, 3), Node(4, 0, 0, 4.5), Node(5, 0, 0, 6),
            Node(10, 3, 0, 3), Node(11, 3, 3, 3), Node(12, 0, 3, 3), Node(20, 3, 0, 6),
        ];
        FemElement[] mesh =
        [
            Bar(1, 1, 2), Bar(2, 2, 3), Bar(3, 3, 4), Bar(4, 4, 5),
            Shell(100, 3, 10, 11, 12), Bar(200, 5, 20),
        ];
        var braced = new FemBracedLength(mesh, nodes);

        Assert.Equal(3.0, braced.BarLength(mesh[0])!.Value, 9);
        Assert.Equal(3.0, braced.BarLength(mesh[1])!.Value, 9);
        Assert.Equal(3.0, braced.BarLength(mesh[2])!.Value, 9);
        Assert.Equal(3.0, braced.BarLength(mesh[3])!.Value, 9);
        Assert.Null(braced.BarLength(mesh[4]));   // пластина — не стержень
    }

    /// <summary>Без примыканий колонна — одна цепочка; излом оси обрывает цепочку.</summary>
    [Fact]
    public void BarLength_ChainBreaksOnKinkOnly()
    {
        FemMeshNode[] nodes = [Node(1, 0, 0, 0), Node(2, 0, 0, 2), Node(3, 0, 0, 5), Node(4, 2, 0, 7)];
        FemElement[] mesh = [Bar(1, 1, 2), Bar(2, 3, 2), Bar(3, 3, 4)];   // КЭ 2 направлен навстречу
        var braced = new FemBracedLength(mesh, nodes);

        Assert.Equal(5.0, braced.BarLength(mesh[0])!.Value, 9);
        Assert.Equal(5.0, braced.BarLength(mesh[1])!.Value, 9);
        Assert.Equal(Math.Sqrt(8), braced.BarLength(mesh[2])!.Value, 9);
    }

    /// <summary>
    /// Стена в плоскости XZ, два этажа по 3 м (по два КЭ 1,5 м по высоте); на отметке 3 м примыкает перекрытие,
    /// у торца x = 0 — перпендикулярная стена (уровней не даёт). Высота полосы каждого КЭ — 3 м; ось X выдачи —
    /// «узел 1 → узел 2» вдоль X, нормаль (узлы 1, 2, 3) — −Y, вертикаль — ось Y выдачи (90°).
    /// </summary>
    [Fact]
    public void WallStrip_IsStoreyHeight_PerpendicularWallDoesNotBrace()
    {
        var nodes = new List<FemMeshNode>();
        // Узлы стены: x = 0, 1; z = 0, 1.5, 3, 4.5, 6 → тег = 10·k + i.
        double[] zs = [0, 1.5, 3, 4.5, 6];
        for (int k = 0; k < zs.Length; k++)
            for (int i = 0; i < 2; i++)
                nodes.Add(Node(10 * k + i, i, 0, zs[k]));
        // Узлы перекрытия (y = 1 на отметке 3) и перпендикулярной стены (x = 0, y = 1 на всех отметках).
        nodes.Add(Node(100, 0, 1, 3)); nodes.Add(Node(101, 1, 1, 3));
        for (int k = 0; k < zs.Length; k++) nodes.Add(Node(200 + k, 0, 1, zs[k]));

        var mesh = new List<FemElement>();
        for (int k = 0; k < 4; k++)   // порядок «1 2 4 3»: низ слева → низ справа → верх справа → верх слева
            mesh.Add(Shell(k + 1, 10 * k, 10 * k + 1, 10 * (k + 1) + 1, 10 * (k + 1)));
        mesh.Add(Shell(50, 20, 21, 101, 100));   // перекрытие
        for (int k = 0; k < 4; k++)
            mesh.Add(Shell(60 + k, 10 * k, 200 + k, 200 + k + 1, 10 * (k + 1)));   // перпендикулярная стена

        var braced = new FemBracedLength(mesh, nodes);

        for (int k = 0; k < 4; k++)
        {
            var strip = braced.WallStrip(mesh[k]);
            Assert.NotNull(strip);
            Assert.Equal(3.0, strip!.HeightM, 9);
            Assert.Equal(90.0, strip.VerticalAngleDeg, 6);
        }
        Assert.Null(braced.WallStrip(mesh[4]));   // перекрытие — не стена
        // Перпендикулярная стена — своя стенка; перекрытие касается её в угловом узле 20 и раскрепляет на отметке 3 м.
        Assert.Equal(3.0, braced.WallStrip(mesh[5])!.HeightM, 9);
    }

    /// <summary>Угол согласования осей выдачи поворачивает вертикаль: ось X выдачи вертикальна — угол 0.</summary>
    [Fact]
    public void WallStrip_LocalAxisAngleRotatesVertical()
    {
        FemMeshNode[] nodes = [Node(1, 0, 0, 0), Node(2, 1, 0, 0), Node(3, 1, 0, 1), Node(4, 0, 0, 1)];
        var wall = Shell(1, 1, 2, 3, 4);
        wall.LocalAxisAngleDeg = 90;
        Assert.Equal(0.0, new FemBracedLength([wall], nodes).WallStrip(wall)!.VerticalAngleDeg, 6);
    }

    static MaterialChars ConcreteChars(CalcType ct) => new(ct)
    {
        Type = MatType.Concrete, E = 30_000_000.0, Fc = -14_500.0, Ft = 1_050.0,
        Ec0 = -0.002, Ec1 = -0.6 * 14_500.0 / 30_000_000.0, Ec2 = -0.0035, Ec1Red = -0.0015,
        Et0 = 0.0001, Et1 = 0.6 * 1_050.0 / 30_000_000.0, Et2 = 0.00015, Et1Red = 0.00008,
    };

    static MaterialChars RebarChars(CalcType ct) => new(ct)
    {
        Type = MatType.ReSteelF, E = 200_000_000.0, Fc = -435_000.0, Ft = 435_000.0, Ec2 = -0.025, Et2 = 0.025,
    };

    static Material WallConcrete()
    {
        var m = new Material { Id = 1, Tag = "B25", Type = MatType.Concrete, E = 30_000_000.0 };
        m.C = ConcreteChars(CalcType.C); m.CL = ConcreteChars(CalcType.CL);
        m.N = ConcreteChars(CalcType.N); m.NL = ConcreteChars(CalcType.NL);
        return m;
    }

    static Material WallRebar()
    {
        var m = new Material { Id = 2, Tag = "A500", Type = MatType.ReSteelF, E = 200_000_000.0 };
        m.C = RebarChars(CalcType.C); m.CL = RebarChars(CalcType.CL);
        m.N = RebarChars(CalcType.N); m.NL = RebarChars(CalcType.NL);
        return m;
    }

    /// <summary>Стена 200 мм: вертикальная арматура — по y сечения (⌀12 шаг 200, z = ±0,065), горизонтальная — по x (⌀8).</summary>
    static PlateSection WallSection() => new()
    {
        Tag = "Ст200", H = 0.2, NLayers = 40, PlateModel = "layered", ConcreteDiagramType = DiagrammType.L3,
        ConcreteMaterialId = 1, RebarMaterialId = 2,
        RebarLayers =
        [
            new PlateRebarLayer { Name = "низ", InputMode = "direct", Asx = 251e-6, Asy = 565e-6, Zsx = -0.075, Zsy = -0.065 },
            new PlateRebarLayer { Name = "верх", InputMode = "direct", Asx = 251e-6, Asy = 565e-6, Zsx = 0.075, Zsy = 0.065 },
        ],
    };

    /// <summary>
    /// Буквальный режим: вертикаль — ось Y выдачи (90°), η считается по Ny, My и вертикальной арматуре (Asy, Zsy)
    /// и совпадает с формулой нормы для полосы 1 м; усиливается только My.
    /// </summary>
    [Fact]
    public void WallEta_FormulaUsesVerticalForcesAndRebar()
    {
        var shell = new ShellLoadItem { Label = "э.1 с1", Nx = -50, Ny = -800, Mx = 2, My = 10 };
        var strip = new FemWallStrip(3.0, 90.0);
        var eta = new FemEtaParams { Enabled = true };

        var (amp, r) = FemWallEta.Apply(shell, strip, eta, psi: 1.0, WallSection(), WallConcrete(), WallRebar(),
            CalcType.C, rebarAngleDeg: 0);

        double t = 0.2;
        var expected = CScore.Sp63.EccentricityAmplifier.AmplifyFormula(-800, 10, 3.0, t, t / Math.Sqrt(12),
            eiConcrete: 30_000_000.0 * t * t * t / 12, eiRebar: 200_000_000.0 * 2 * 565e-6 * 0.065 * 0.065, psi: 1.0);
        Assert.True(r.Stable);
        Assert.Equal(expected.Eta, r.Eta, 9);
        Assert.True(r.Eta > 1.0);
        Assert.Equal(10 * r.Eta, amp.My, 6);
        Assert.Equal(2.0, amp.Mx, 6);
        Assert.Equal(-800.0, amp.Ny, 6);
        Assert.Equal(-50.0, amp.Nx, 6);
    }

    /// <summary>Уточнённый режим даёт η того же порядка, что и формула (жёсткость из слоистой модели).</summary>
    [Fact]
    public void WallEta_IterativeIsFiniteAndAboveOne()
    {
        var shell = new ShellLoadItem { Label = "э.1 с1", Ny = -800, My = 10 };
        var (_, r) = FemWallEta.Apply(shell, new FemWallStrip(3.0, 90.0), new FemEtaParams { Enabled = true, Iterative = true },
            1.0, WallSection(), WallConcrete(), WallRebar(), CalcType.C, 0);

        Assert.True(r.Stable);
        Assert.InRange(r.Eta, 1.0 + 1e-6, 3.0);
    }

    [Fact]
    public void WallEta_InstabilityWhenNAboveNcr()
    {
        var shell = new ShellLoadItem { Label = "э.1 с1", Ny = -3000, My = 10 };
        var (amp, r) = FemWallEta.Apply(shell, new FemWallStrip(6.0, 90.0), new FemEtaParams { Enabled = true, MuX = 2 },
            1.0, WallSection(), WallConcrete(), WallRebar(), CalcType.C, 0);

        Assert.False(r.Stable);
        Assert.Equal(10.0, amp.My, 9);   // при потере устойчивости строка не усиливается
    }

    /// <summary>
    /// Проверка стены по КЭ (слоистая модель, прочность): с η коэффициент использования выше, в описании — η;
    /// у КЭ без полосы (перекрытие) η нет; потеря устойчивости — строка не пройдена по п. 8.1.15.
    /// </summary>
    [Fact]
    public void RunPerElement_WallEtaAmplifiesPlateRows()
    {
        PlateCheckParams Params(FemEtaParams? eta) => new() { Kind = "shell_layered", CheckGroup = "uls", Eta = eta };
        FemCheckScopeElement Elem(int num) => new(num, new FemElement { ElemTag = num.ToString(), ElemType = "shell" }, null);
        ShellLoadItem Row(int elem, double ny) => new() { Label = $"э.{elem} с1", Ny = ny, My = 15, SourceElementNum = elem };
        var fs = new ForceSet { Id = 1, Kind = "shell", Tag = "Стены (C)", ShellItems = [Row(1, -800), Row(2, -800), Row(3, -3000)] };
        FemCheckScopeElement[] elements = [Elem(1), Elem(2), Elem(3)];

        CalcResult Run(FemEtaParams? eta) => FemCheckRunner.RunPerElement(
            new FemCheck { NormCode = "rc_plate_check", Tag = "стены", ParamsJson = Params(eta).ToJson() },
            new FemMemberGroup { Tag = "Стены" }, new FemCheckScope([], elements, RefersToMeshElements: true), [fs],
            new FemPerElementInputs
            {
                PlateTemplate = WallSection(), ConcreteMat = WallConcrete(), RebarMat = WallRebar(),
                // КЭ 2 — не стена (перекрытие), η не учитывается.
                WallStrip = e => e.ElemNum == 2 ? null : new FemWallStrip(e.ElemNum == 3 ? 6.0 : 3.0, 90.0),
            },
            (_, _, _) => throw new InvalidOperationException("стержневой исполнитель не нужен"));

        var plain = Run(null).FemCheckRows!.ToDictionary(r => r.Label);
        var withEta = Run(new FemEtaParams { Enabled = true, MuX = 1.0 }).FemCheckRows!.ToDictionary(r => r.Label);

        Assert.True(withEta["э.1 с1"].Utilization > plain["э.1 с1"].Utilization);
        Assert.Contains("η =", withEta["э.1 с1"].WorstDescription);
        Assert.Equal(plain["э.2 с1"].Utilization, withEta["э.2 с1"].Utilization, 9);
        Assert.DoesNotContain("η =", withEta["э.2 с1"].WorstDescription);
        Assert.False(withEta["э.3 с1"].Passed);
        Assert.Equal("п. 8.1.15", withEta["э.3 с1"].WorstFormula);
    }

    [Fact]
    public void BarLength_NullWithoutNodeCoordinates()
    {
        FemElement[] mesh = [Bar(1, 1, 2)];
        Assert.Null(new FemBracedLength(mesh, [Node(1, 0, 0, 0)]).BarLength(mesh[0]));
    }

    /// <summary>
    /// ψ относительно растянутого стержня: Mx > 0 растягивает грань y > 0 (стержень ys = 0,25).
    /// M1 = 50 + 1000·0,25 = 300, M1l = 20 + 600·0,25 = 170 → ψ = 0,5667.
    /// </summary>
    [Fact]
    public void Psi_AboutLeastCompressedBar()
    {
        double psi = FemEtaPsi.Compute(nTotal: -1000, mTotal: 50, nLong: -600, mLong: 20, rebarMin: -0.25, rebarMax: 0.25);
        Assert.Equal(170.0 / 300.0, psi, 9);

        // Момент другого знака — растянута грань y < 0.
        Assert.Equal(170.0 / 300.0, FemEtaPsi.Compute(-1000, -50, -600, -20, -0.25, 0.25), 9);
        // Длительная больше полной (разные знаки, перераспределение) — не больше 1.
        Assert.Equal(1.0, FemEtaPsi.Compute(-100, 10, -400, 10, -0.25, 0.25), 9);
        // Нет ни момента, ни силы — запас.
        Assert.Equal(1.0, FemEtaPsi.Compute(0, 0, 0, 0, -0.25, 0.25), 9);
    }

    [Fact]
    public void LongTermSet_PairsByTagSuffix()
    {
        var c = new ForceSet { Id = 1, Tag = "Колонна — РСУ (C)" };
        var cl = new ForceSet { Id = 2, Tag = "Колонна — РСУ (CL)" };
        var n = new ForceSet { Id = 3, Tag = "Плита (N)" };
        var nl = new ForceSet { Id = 4, Tag = "Плита(NL)" };
        ForceSet[] all = [c, cl, n, nl];

        Assert.Same(cl, FemEtaPsi.LongTermSet(c, all));
        Assert.Same(nl, FemEtaPsi.LongTermSet(n, all));
        Assert.Null(FemEtaPsi.LongTermSet(cl, all));
        Assert.Null(FemEtaPsi.LongTermSet(new ForceSet { Tag = "Ручной" }, all));
    }

    [Fact]
    public void BarCheckParams_RoundTripsEta()
    {
        var p = new BarCheckParams
        {
            RebarSources = ["section"],
            Eta = new FemEtaParams { Enabled = true, MuX = 0.7, LengthM = 3.3, PsiMode = FemEtaParams.PsiManual, PsiX = 0.4 },
        };
        var back = BarCheckParams.Parse(p.ToJson());

        Assert.True(back.EtaEnabled);
        Assert.Equal(0.7, back.Eta!.MuX);
        Assert.Equal(3.3, back.ResolveLengthM());
        Assert.False(back.Eta.IsPsiAuto);
        Assert.DoesNotContain("ElementLengthM", p.ToJson());
        // Без η параметры прежние: старые проверки читаются как раньше.
        Assert.False(BarCheckParams.Parse("{\"RebarSources\":[]}").EtaEnabled);
    }

    /// <summary>
    /// Проверка по КЭ: в задание строки уходят длина КЭ по сетке и ψ из строки длительного набора с той же
    /// меткой; строка без пары — без ψ (возьмётся из параметров); длительный набор — ψ = 1.
    /// </summary>
    [Fact]
    public void RunPerElement_PassesLengthAndRowPsiToExecutor()
    {
        var check = new FemCheck
        {
            NormCode = "rc_check", Tag = "колонны",
            ParamsJson = new BarCheckParams { Eta = new FemEtaParams { Enabled = true } }.ToJson(),
        };
        var scope = new FemCheckScope([],
        [
            new FemCheckScopeElement(1, Bar(1, 1, 2), null),
            new FemCheckScopeElement(2, Bar(2, 2, 3), null),
        ], RefersToMeshElements: true);
        LoadItem Row(int elem, double n, double mx) =>
            new() { Label = $"э.{elem} с1", N = n, Mx = mx, SourceElementNum = elem, SourceSectionNum = 1 };
        var c = new ForceSet { Id = 1, Tag = "РСУ (C)", Items = [Row(1, -1000, 50), Row(2, -1000, 50)] };
        var cl = new ForceSet { Id = 2, Tag = "РСУ (CL)", Items = [Row(1, -600, 20)] };

        var seen = new Dictionary<string, BarCheckParams>();
        var result = FemCheckRunner.RunPerElement(check, new FemMemberGroup { Tag = "К" }, scope, [c, cl],
            new FemPerElementInputs
            {
                TargetBarSection = new CrossSection { Tag = "цель" },
                BarSectionById = _ => null,
                BarBracedLength = e => e.ElemNum == 1 ? 3.0 : 4.5,
                LookupForceSets = [c, cl],
            },
            (task, _, item) =>
            {
                lock (seen) seen[$"{task.CalcType}/{item.Label}"] = BarCheckParams.Parse(task.ParamsJson);
                return new CalcResult { Status = "ok", DataJson = "{\"utilization\":0.5}" };
            });

        Assert.Equal("ok", result.Status);
        var c1 = seen["C/э.1 с1"];
        Assert.Equal(3.0, c1.ElementLengthM);
        // Сечение без геометрии: ys = 0, ψ = Mlong/Mtotal.
        Assert.Equal(20.0 / 50.0, c1.RowPsiX!.Value, 9);
        Assert.Equal(1.0, c1.RowPsiY!.Value, 9);   // моментов My нет — запас

        var c2 = seen["C/э.2 с1"];
        Assert.Equal(4.5, c2.ElementLengthM);
        Assert.Null(c2.RowPsiX);

        var cl1 = seen["CL/э.1 с1"];
        Assert.Equal(1.0, cl1.RowPsiX);
        Assert.Equal(1.0, cl1.RowPsiY);
    }
}
