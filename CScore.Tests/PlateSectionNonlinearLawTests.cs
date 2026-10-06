using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace CScore.Tests;

// Закон слоя пластины для нелинейного расчёта схем (CSfea, срез 2): ν бетона до трещины
// (Дарвин — Пекнольд), необратимая память трещин слоёв и ψs арматуры по п. 8.2.32 СП 63.
public class PlateSectionNonlinearLawTests
{
    static readonly Material ConcreteB25 = TestMaterials.Concrete("B25");
    static readonly Material RebarA500 = TestMaterials.Rebar("A500");
    static Diagramm ConcreteN => ConcreteB25.GetDiagramms(DiagrammType.L3)![CalcType.N];
    static Diagramm RebarN => RebarA500.GetDiagramms(DiagrammType.L2)![CalcType.N];

    static PlateSection Slab(bool tension, string softening = "") => new()
    {
        H = 0.2, NLayers = 12, TensionConcrete = tension, SofteningModel = softening,
        SofteningEpsC2 = 0.002, PlateModel = "layered",
        RebarLayers =
        [
            new PlateRebarLayer { Asx = 5.65e-4, Asy = 3.93e-4, Zsx = -0.07, Zsy = -0.058 },
            new PlateRebarLayer { Asx = 3.93e-4, Asy = 5.65e-4, Zsx = 0.07, Zsy = 0.058 },
        ],
    };

    static readonly ShellStrainState[] SnapshotStates =
    [
        new(0, 0, 0, 1e-4, 0, 0),
        new(-2e-4, 1e-4, 5e-5, -8e-3, 3e-3, 2e-3),
        new(3e-4, 2e-4, -1e-4, 2e-3, -1e-3, 4e-3),
        new(-1e-3, -5e-4, 2e-4, 1.5e-2, 1e-2, -6e-3),
        new(5e-5, -3e-5, 1e-3, 0, 0, 1e-2),
        new(-2e-4, 1e-4, 0, 3e-2, -3e-2, 1e-2),   // ε₁ до 3‰ — β Vecchio–Collins < 1
    ];

    // Усилия (Nx, Ny, Nxy, Mx, My, Mxy) слоистой модели до введения ν и памяти трещин
    // (master 0c26fb49): растяжение бетона {вкл, выкл} × softening {нет, Vecchio–Collins} ×
    // SnapshotStates. При ν = 0 новый закон обязан давать их бит в бит.
    static readonly double[] Snapshot =
    [
        2.764126108374382, -7.17557482221837E-32, 1.1718602991841054E-15, 2.826825069147965, 4.750496294339014E-33, -7.75814920293181E-17,
        -1306.967745465893, -183.11791355062897, 90.05057598704839, -71.79212025644689, 21.496507326989374, 7.645672125927576,
        76.69055753648934, 128.68362036513656, 3.8936265534028767, 1.6824081674726408, 4.244600887087167, -1.6328999177123271,
        -2618.4657887188205, -1873.4891573111418, 62.10932602143983, 85.82310647729193, 72.78952926208817, -6.845640755131848,
        -909.3102241509201, -1149.920559697995, 1065.3453274380897, -23.28824637086533, -17.744994055167318, 17.43467983349773,
        -1720.3723819671707, -1496.1929663274723, 30.29531105196285, 115.56234760217791, -102.41772466580322, 14.334519910924909,
        2.764126108374382, -7.17557482221837E-32, 1.1718602991841054E-15, 2.826825069147965, 4.750496294339014E-33, -7.75814920293181E-17,
        -1306.967745465893, -183.11791355062897, 90.05057598704839, -71.79212025644689, 21.496507326989374, 7.645672125927576,
        76.69055753648934, 128.68362036513656, 3.8936265534028767, 1.6824081674726408, 4.244600887087167, -1.6328999177123271,
        -2618.4657887188205, -1873.4891573111418, 62.10932602143983, 85.82310647729193, 72.78952926208817, -6.845640755131848,
        -909.3102241509201, -1149.920559697995, 1065.3453274380897, -23.28824637086533, -17.744994055167318, 17.43467983349773,
        -1557.8582502316951, -1377.0790687888916, 32.398636849237334, 103.18224762862863, -92.94430250972246, 12.523909695775446,
        -19.378731034482758, -7.17557482221837E-32, 1.1718602991841054E-15, 1.3608859157088125, 4.750496294339014E-33, -7.75814920293181E-17,
        -1332.8499353550012, -247.66218835689128, 88.7480537302775, -70.72761212589539, 21.883162070305765, 7.531704050188241,
        33.25630709635465, 27.00426229752175, -13.449540268222785, 2.9343185161674903, 0.6088802227271732, 1.2328745245870887,
        -2634.6475005422367, -1902.1391682455064, 61.160257353723, 84.65005055869543, 71.03076152940663, -7.042608728135591,
        -944.4566020621481, -1162.223681463083, 1045.24636803887, -20.34738428136539, -16.766874229893848, 19.072048691181912,
        -1739.9716733227913, -1496.9769379816971, 26.375452780838753, 115.3990201742144, -102.42425776292177, 14.301854425332209,
        -19.378731034482758, -7.17557482221837E-32, 1.1718602991841054E-15, 1.3608859157088125, 4.750496294339014E-33, -7.75814920293181E-17,
        -1332.8499353550012, -247.66218835689128, 88.7480537302775, -70.72761212589539, 21.883162070305765, 7.531704050188241,
        33.25630709635465, 27.00426229752175, -13.449540268222785, 2.9343185161674903, 0.6088802227271732, 1.2328745245870887,
        -2634.6475005422367, -1902.1391682455064, 61.160257353723, 84.65005055869543, 71.03076152940663, -7.042608728135591,
        -944.4566020621481, -1162.223681463083, 1045.24636803887, -20.34738428136539, -16.766874229893848, 19.072048691181912,
        -1577.4575415873157, -1377.8630404431165, 28.478778578113232, 103.01892020066515, -92.950835606841, 12.491244210182744,
    ];

    static IEnumerable<(PlateSection Section, ShellStrainState State)> SnapshotCases()
    {
        foreach (bool ten in new[] { true, false })
            foreach (string soft in new[] { "", "vecchio_collins" })
                foreach (var s in SnapshotStates)
                    yield return (Slab(ten, soft), s);
    }

    static double[] Forces(ShellResult r) => [r.Nx, r.Ny, r.Nxy, r.Mx, r.My, r.Mxy];

    [Fact]
    public void ZeroPoisson_NoState_MatchesSnapshotBitwise()
    {
        int k = 0;
        foreach (var (section, s) in SnapshotCases())
            foreach (double v in Forces(section.Compute(s, ConcreteN, RebarN, null, computeStiffness: false)))
                Assert.Equal(Snapshot[k++], v);
        Assert.Equal(Snapshot.Length, k);
    }

    [Fact]
    public void ZeroPoisson_EmptyState_MatchesNoStateBitwise()
    {
        foreach (var (section, s) in SnapshotCases())
        {
            var a = section.Compute(s, ConcreteN, RebarN, null, computeStiffness: false);
            var b = section.Compute(s, ConcreteN, RebarN, null, computeStiffness: false,
                layerState: PlateLayerState.For(section));
            Assert.Equal(Forces(a), Forces(b));
        }
    }

    // Линейно-упругий «бетон» (σ = E·ε в обе стороны до ±4 %), кПа. Двухлинейная диаграмма
    // стали идёт в Ft при Ft/E, поэтому предел задаётся как E·ε_max — без площадки до ε_max.
    static Diagramm LinearDiagram(double eKPa)
    {
        double fMax = eKPa * 0.04;
        MaterialChars Ch(CalcType ct) => new(ct)
        {
            Type = MatType.ReSteelF, E = eKPa, Ry = fMax, Ru = fMax, Ft = fMax, Fc = -fMax,
            Ec2 = -0.05, Et2 = 0.05,
        };
        var m = new Material { Id = 9001, E = eKPa, Type = MatType.ReSteelF, Tag = "lin" };
        m.MaterialChars = [Ch(CalcType.C), Ch(CalcType.CL), Ch(CalcType.N), Ch(CalcType.NL)];
        return m.GetDiagramms(DiagrammType.L2)![CalcType.N];
    }

    [Fact]
    public void Poisson_ElasticStage_GivesIsotropicPlateStiffness()
    {
        const double e = 30e6, nu = 0.2, h = 0.2;
        var d = LinearDiagram(e);
        var section = new PlateSection
        {
            H = h, NLayers = 20, TensionConcrete = true, PlateModel = "layered", PoissonUncracked = nu,
        };
        var s = new ShellStrainState(1e-4, -5e-5, 3e-5, 1e-3, 5e-4, -2e-4);
        var t = section.ComputeTangent(s, d, d);

        // A = Eh/(1 − ν²)·[[1, ν, 0], [ν, 1, 0], [0, 0, (1 − ν)/2]], D — то же с Σz²·dz слоёв.
        double k = e / (1 - nu * nu), g = e / (2 * (1 + nu));
        double dz = h / section.NLayers, iz = 0;
        for (int i = 0; i < section.NLayers; i++)
        {
            double z = -h / 2 + dz / 2 + i * dz;
            iz += z * z * dz;
        }
        double[,] aExp = { { k * h, k * nu * h, 0 }, { k * nu * h, k * h, 0 }, { 0, 0, g * h } };
        double[,] dExp = { { k * iz, k * nu * iz, 0 }, { k * nu * iz, k * iz, 0 }, { 0, 0, g * iz } };
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
            {
                Assert.True(Math.Abs(t.A[i, j] - aExp[i, j]) <= 1e-6 * k * h, $"A[{i},{j}] = {t.A[i, j]}, ожидалось {aExp[i, j]}");
                Assert.True(Math.Abs(t.D[i, j] - dExp[i, j]) <= 1e-6 * k * iz, $"D[{i},{j}] = {t.D[i, j]}, ожидалось {dExp[i, j]}");
                Assert.True(Math.Abs(t.B[i, j]) <= 1e-6 * k * h * h, $"B[{i},{j}] = {t.B[i, j]}");
            }
    }

    [Fact]
    public void Poisson_LayerLaw_IsSymmetricAndReproducesStresses()
    {
        var section = Slab(tension: true);
        section.PoissonUncracked = 0.2;
        var s = new ShellStrainState(-2e-4, 1e-4, 5e-5, -8e-3, 3e-3, 2e-3);
        foreach (int i in Enumerable.Range(0, section.NLayers))
        {
            var p = section.EvaluateConcreteLayer(i, s, ConcreteN);
            Assert.Equal(0.2, p.Nu);
            Assert.Equal(p.Q11 * p.Eps1 + p.Q12 * p.Eps2, p.Sig1, 9);
            Assert.Equal(p.Q12 * p.Eps1 + p.Q22 * p.Eps2, p.Sig2, 9);
            Assert.True(p.Q12 * p.Q12 <= p.Q11 * p.Q22 + 1e-6, "Q не положительно полуопределена");
            Assert.True(p.G12 >= 0.0);
        }
    }

    [Fact]
    public void Cracks_AreIrreversible_AndSwitchOffLayerTensionAndPoisson()
    {
        var section = Slab(tension: true);
        section.PoissonUncracked = 0.2;
        var st = PlateLayerState.For(section);

        // Изгиб, растягивающий низ далеко за ε_bt,ult = 0,00015.
        var big = new ShellStrainState(0, 0, 0, -1e-2, 0, 0);
        var upd = section.UpdateCracks(st, big, ConcreteN);
        Assert.True(upd.NewCracks > 0);
        Assert.True(st.IsCracked(0));
        Assert.False(st.IsCracked(section.NLayers - 1));
        // Нижняя арматура x и y — в треснувших слоях, верхняя — нет.
        Assert.Contains((0, true), upd.PendingPsi);
        Assert.Contains((0, false), upd.PendingPsi);
        Assert.DoesNotContain(upd.PendingPsi, p => p.RebarLayer == 1);

        // Разгрузка до малого растяжения низа: без памяти слой несёт растяжение, с памятью — нет.
        var small = new ShellStrainState(0, 0, 0, -5e-4, 0, 0);
        var free = section.EvaluateConcreteLayer(0, small, ConcreteN);
        var mem = section.EvaluateConcreteLayer(0, small, ConcreteN, layerState: st);
        Assert.True(free.Sig1 > 0.0);
        Assert.Equal(0.0, mem.Sig1);
        Assert.Equal(0.0, mem.Nu);
        Assert.True(mem.Cracked);
        Assert.Equal(0.2, section.EvaluateConcreteLayer(section.NLayers - 1, small, ConcreteN, layerState: st).Nu);

        var rFree = section.Compute(small, ConcreteN, RebarN, computeStiffness: false);
        var rMem = section.Compute(small, ConcreteN, RebarN, computeStiffness: false, layerState: st);
        Assert.True(Math.Abs(rMem.MxConcrete) < Math.Abs(rFree.MxConcrete));

        // Повторный вызов новых трещин не добавляет.
        Assert.Equal(0, section.UpdateCracks(st, big, ConcreteN).NewCracks);
    }

    [Fact]
    public void LayerState_CommitRevertReset()
    {
        var st = new PlateLayerState(4, 2);
        st.MarkCracked(0);
        st.SetEpsCrc(1, true, 2e-4);
        st.Commit();
        st.MarkCracked(1);
        st.SetEpsCrc(0, false, 1e-4);
        Assert.Equal(2, st.CrackedCount);

        var copy = st.Clone();
        st.Revert();
        Assert.True(st.IsCracked(0));
        Assert.False(st.IsCracked(1));
        Assert.Equal(2e-4, st.EpsCrc(1, true));
        Assert.False(st.HasEpsCrc(0, false));
        Assert.True(copy.IsCracked(1));
        Assert.True(copy.HasEpsCrc(0, false));

        st.Reset();
        Assert.Equal(0, st.CrackedCount);
        Assert.False(st.HasEpsCrc(1, true));
        st.Revert();
        Assert.Equal(0, st.CrackedCount);
    }

    [Fact]
    public void LayerState_SizeMismatchOrNonLayeredModel_Throws()
    {
        var section = Slab(tension: true);
        Assert.Throws<ArgumentException>(() =>
            section.Compute(new ShellStrainState(), ConcreteN, RebarN, layerState: new PlateLayerState(5, 2)));
        section.PlateModel = "char1d_axial";
        Assert.Throws<InvalidOperationException>(() =>
            section.Compute(new ShellStrainState(), ConcreteN, RebarN, layerState: PlateLayerState.For(section)));
    }

    [Fact]
    public void Psi_BeforeCrackIsOne_CompressedUncorrected_AfterYieldBounded()
    {
        var section = Slab(tension: false);
        var st = PlateLayerState.For(section);
        var tens = new ShellStrainState(0, 0, 0, -1e-2, 0, 0);
        var comp = new ShellStrainState(0, 0, 0, 1e-2, 0, 0);

        var before = section.EvaluateRebar(0, true, tens, RebarN, layerState: st);
        Assert.Equal(1.0, before.Psi);
        Assert.Equal(section.EvaluateRebar(0, true, tens, RebarN).Sig, before.Sig);

        st.SetEpsCrc(0, true, 1e-3);
        var after = section.EvaluateRebar(0, true, tens, RebarN, layerState: st);
        Assert.True(after.Psi < 1.0);
        Assert.True(after.Sig > before.Sig);
        // За текучестью σ не выше Rs,ser = 500 МПа.
        var yielded = section.EvaluateRebar(0, true, new ShellStrainState(0, 0, 0, -0.2, 0, 0), RebarN, layerState: st);
        Assert.True(yielded.Sig <= 500_000.0 + 1e-6, $"σs = {yielded.Sig}");
        // Сжатый стержень — без поправки.
        Assert.Equal(section.EvaluateRebar(0, true, comp, RebarN).Sig,
                     section.EvaluateRebar(0, true, comp, RebarN, layerState: st).Sig);
    }

    // Полоса 1 м — та же геометрия и материалы, что у пластины ниже, но стержневым НДМ.
    static CrossSection StripBar(double h, int ny, double asBot, double zBot, double asTop, double zTop)
    {
        var x = new[] { -0.5, 0.5, 0.5, -0.5, -0.5 };
        var y = new[] { -h / 2, -h / 2, h / 2, h / 2, -h / 2 };
        var concrete = new MaterialArea
        {
            Id = 1, Tag = "бетон", Category = AreaCategory.Region,
            Material = ConcreteB25, MaterialId = ConcreteB25.Id,
            DiagrammType = DiagrammType.L3, Hull = new Contour(x, y, "hull"),
        };
        concrete.SetWKT();
        concrete.SliceXY(nx: 4, ny: ny);

        MaterialArea Bars(int id, double area, double yBar)
        {
            var a = new MaterialArea
            {
                Id = id, Tag = $"арматура {id}", Category = AreaCategory.RebarGroup,
                Material = RebarA500, MaterialId = RebarA500.Id,
                DiagrammType = DiagrammType.L2, HostArea = concrete, HostAreaId = concrete.Id,
            };
            for (int i = 0; i < 5; i++)
            {
                var bar = Fiber.CreatePoint(0.012, -0.4 + i * 0.2, yBar);
                bar.Area = area / 5;
                a.Fibers.Add(bar);
            }
            return a;
        }

        var section = new CrossSection
        {
            Id = 1, Tag = "полоса", Areas = [concrete, Bars(2, asBot, zBot), Bars(3, asTop, zTop)],
        };
        section.ResolveAndBuildDiagramms(rebarDifferentialDiagram: false);
        return section;
    }

    // Чистый изгиб полосы пластины по x: Ньютон по (ε₀x, κx) при Nx = 0, Mx = m (εy = κy = 0).
    static ShellStrainState SolveStrip(PlateSection section, double m, PlateLayerState? st)
    {
        double e0 = 0, kx = m / (30e6 * Math.Pow(section.H, 3) / 12);
        (double n, double mm) F(double a, double b)
        {
            var r = section.Compute(new ShellStrainState(a, 0, 0, b, 0, 0), ConcreteN, RebarN,
                computeStiffness: false, layerState: st);
            return (r.Nx, r.Mx - m);
        }
        for (int it = 0; it < 200; it++)
        {
            var (f1, f2) = F(e0, kx);
            if (Math.Abs(f1) < 1e-7 && Math.Abs(f2) < 1e-7)
                return new ShellStrainState(e0, 0, 0, kx, 0, 0);
            const double d = 1e-9;
            var (a1, a2) = F(e0 + d, kx);
            var (b1, b2) = F(e0, kx + d);
            double j11 = (a1 - f1) / d, j21 = (a2 - f2) / d, j12 = (b1 - f1) / d, j22 = (b2 - f2) / d;
            double det = j11 * j22 - j12 * j21;
            e0 -= (j22 * f1 - j12 * f2) / det;
            kx -= (-j21 * f1 + j11 * f2) / det;
        }
        throw new InvalidOperationException("Полоса пластины: Ньютон не сошёлся");
    }

    [Fact]
    public void Psi_StripBending_MatchesBarNdmWithPsi()
    {
        const double h = 0.2, asBot = 5.65e-4, zBot = -0.07, asTop = 3.93e-4, zTop = 0.07;
        const double m = -30.0;   // кН·м/м, растянут низ
        var section = new PlateSection
        {
            H = h, NLayers = 80, TensionConcrete = false, PlateModel = "layered",
            RebarLayers =
            [
                new PlateRebarLayer { Asx = asBot, Zsx = zBot },
                new PlateRebarLayer { Asx = asTop, Zsx = zTop },
            ],
        };
        var st = PlateLayerState.For(section);

        // Рабочее НДС без ψs → трещины → εs,crc по ShellCrackingSolver → НДС с ψs.
        var s0 = SolveStrip(section, m, st);
        var upd = section.UpdateCracks(st, s0, ConcreteN);
        Assert.Equal(new[] { (0, true) }, upd.PendingPsi);
        var act = section.ActivatePsi(st, upd.PendingPsi, [0, 0, 0, m, 0, 0], ConcreteN, RebarN);
        Assert.Equal(new PlatePsiActivation(1, 0, 0), act);
        Assert.True(st.EpsCrc(0, true) > 0);
        var s1 = SolveStrip(section, m, st);
        Assert.True(Math.Abs(s1.Kx) < Math.Abs(s0.Kx), "ψs должен уменьшать кривизну");

        var bar = StripBar(h, 80, asBot, zBot, asTop, zTop);
        var res = new TotalCurvatureSolver(bar, CalcType.N, CalcType.N, CalcType.N).Compute(0, m, 0, m, 0);
        Assert.True(res.Cracked && res.AllConverged);

        double rel = Math.Abs(Math.Abs(s1.Kx) - res.KFull) / res.KFull;
        Assert.True(rel <= 0.02,
            $"κ пластины {Math.Abs(s1.Kx):E5}, стержня {res.KFull:E5}, без ψs {Math.Abs(s0.Kx):E5}; расхождение {rel:P2}");
    }
}
