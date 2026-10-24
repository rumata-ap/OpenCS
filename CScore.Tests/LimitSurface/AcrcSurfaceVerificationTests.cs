using CScore.LimitSurface;
using Xunit;
using Xunit.Abstractions;

namespace CScore.Tests.LimitSurface;

// Сверка поверхности ширины раскрытия с одиночным расчётом (режим Moment — луч колонны от её оси):
// эталон 1 — ядро с точкой трещинообразования по телу ПС2 (как у построителя): расхождение — только
// допуск сетки; эталон 2 — CrackWidthSolver (трещинообразование — CrackingSolver без (8.54)): в двузначной
// зоне то же, при растяжении одного знака поверхность не дальше решателя. Эталон принимается, только если
// он сошёлся; несошедшийся — отдельный FAIL.
public class AcrcSurfaceVerificationTests(ITestOutputHelper output)
{
    const double GridTol = 0.006;

    public static TheoryData<string> Sections => new() { "symmetric", "asymmetric", "prestressed" };

    static CrossSection Make(string name) => name switch
    {
        "symmetric" => LimitSurfaceTestSections.Symmetric(),
        "asymmetric" => LimitSurfaceTestSections.Asymmetric(),
        "prestressed" => TestSections.RectWithEccentricPrestressedRebar(),
        _ => throw new ArgumentException(name),
    };

    static double MaxMoment(LimitSurfaceMesh mesh) => mesh.Vertices.Max(v => Math.Sqrt(v.Mx * v.Mx + v.My * v.My));

    /// <summary>Параметр луча колонны до сетки (от оси колонны).</summary>
    static double MeshRay(LimitSurfaceMesh mesh, (double Mx, double My) axis, double n, double psi, double mBig)
    {
        var o = new ForcePoint(n, axis.Mx, axis.My);
        var u = mesh.Utilization(o, new ForcePoint(0, mBig * Math.Cos(psi), mBig * Math.Sin(psi)));
        var cap = u.Capacity ?? throw new InvalidOperationException("луч не пересёк сетку");
        return Math.Sqrt((cap.Mx - axis.Mx) * (cap.Mx - axis.Mx) + (cap.My - axis.My) * (cap.My - axis.My));
    }

    /// <summary>
    /// Эталон 2: корень g(m) = 1 по CrackWidthSolver в скобке ±10 % у m* колонны (дальше полная нагрузка
    /// бывает за несущей способностью сечения с трещиной — Ньютон не сходится, это не дефект эталона);
    /// null — эталон не сошёлся в скобке или граница вне её.
    /// </summary>
    static double? SolverRay(CrossSection section, AcrcSurfaceCriterion c, (double Mx, double My) axis,
        double n, double psi, double mHint)
    {
        var solver = new CrackWidthSolver(section, calcCrc: c.CalcCrc, phi2: c.Phi2,
            acrcUltLong: c.AcrcUltLong, acrcUltShort: c.AcrcUltShort, solverTol: 0.01, psiSMethod: c.PsiSMethod);
        double G(double m, out bool ok)
        {
            double mx = axis.Mx + m * Math.Cos(psi), my = axis.My + m * Math.Sin(psi);
            var r = solver.Compute(n, c.LongShare * mx, mx, c.LongShare * my, my);
            ok = r.CrcConverged && r.TotalConverged && (r.Cracked || r.Mcrc > Math.Sqrt(mx * mx + my * my));
            if (!r.Cracked) return 0.0;
            return new[] { r.AcrcLong / c.AcrcUltLong, r.AcrcShort / c.AcrcUltShort, r.UtilSteel, r.UtilConcrete }.Max();
        }
        double lo = 0.9 * mHint, hi = 1.1 * mHint;
        if (G(hi, out bool okHi) < 1.0 || !okHi) return null;
        if (G(lo, out bool okLo) >= 1.0 || !okLo) return null;
        for (int it = 0; it < 50; it++)
        {
            double mid = 0.5 * (lo + hi);
            double g = G(mid, out bool ok);
            if (!ok) return null;
            if (g >= 1.0) hi = mid; else lo = mid;
        }
        return 0.5 * (lo + hi);
    }

    [Theory]
    [MemberData(nameof(Sections))]
    public void Moment_MatchesColumnAndSolver(string name)
    {
        var section = Make(name);
        var criterion = new AcrcSurfaceCriterion();
        var mesher = new AcrcColumnMesher(section, criterion);
        var mesh = mesher.Build();
        double mMax = MaxMoment(mesh);
        double nMin = mesh.Vertices.Min(v => v.N), nMax = mesh.Vertices.Max(v => v.N);

        var rnd = new Random(7);
        double worst1 = 0, worst2 = 0, worstSingle = 0;
        int refFailed = 0, columnFailed = 0, checkedSolver = 0;
        for (int i = 0; i < 24; i++)
        {
            double n = nMin * 0.95 + (nMax - nMin) * 0.95 * rnd.NextDouble();
            if (Math.Abs(n) < 1e-3 * (nMax - nMin)) continue;   // ступень N = 0 — отдельный тест
            double psi = 2 * Math.PI * rnd.NextDouble();
            var axis = mesher.Axis(n);
            double mMesh = MeshRay(mesh, axis, n, psi, 10 * mMax);

            var col = mesher.Column(n, psi);
            if (!col.Converged) { columnFailed++; continue; }
            worst1 = Math.Max(worst1, Math.Abs(mMesh - col.M) / mMax);

            // Эталон 2 — только где точка трещинообразования не от одной N и тело ПС2 двузначно/однозначно известно.
            if (col.ByAxialForce || col.Active.Kind == ConstraintKind.CrackBody) continue;
            var crack = mesher.CrackMesh.Utilization(new ForcePoint(n, axis.Mx, axis.My),
                new ForcePoint(0, 10 * mMax * Math.Cos(psi), 10 * mMax * Math.Sin(psi)));
            bool single = crack.Active?.SingleSign ?? false;
            var mSolver = SolverRay(section, criterion, axis, n, psi, col.M);
            if (mSolver is null) { refFailed++; continue; }
            checkedSolver++;
            double d = (mMesh - mSolver.Value) / mMax;
            if (single) worstSingle = Math.Max(worstSingle, d);
            else worst2 = Math.Max(worst2, Math.Abs(d));
        }

        output.WriteLine($"{name}: колонна {worst1:P3}, решатель {worst2:P3}, однозначная {worstSingle:P3}, " +
            $"сверено с решателем {checkedSolver}, эталон не сошёлся {refFailed}, колонна не сошлась {columnFailed}");
        Assert.True(worst1 <= GridTol, $"{name}: сетка против колонны {worst1:P3}");
        Assert.True(worst2 <= GridTol, $"{name}: сетка против CrackWidthSolver {worst2:P3}");
        Assert.True(worstSingle <= GridTol, $"{name}: в однозначной зоне поверхность дальше решателя на {worstSingle:P3}");
        Assert.True(columnFailed == 0, $"{name}: колонна не сошлась в {columnFailed} точках");
        Assert.True(checkedSolver > 0, $"{name}: ни одной сверки с решателем");
    }

    [Fact]
    public void ReferenceFailures_AreReportedSeparately()
    {
        // Отдельный FAIL для несошедшегося эталона: на симметричном сечении эталон обязан сойтись везде.
        var section = Make("symmetric");
        var criterion = new AcrcSurfaceCriterion();
        var mesher = new AcrcColumnMesher(section, criterion, new LimitSurfaceOptions { InitialMeridians = 8, ThetaIntervals = 8 });
        foreach (var (n, psi) in new[] { (-200.0, 0.3), (-50.0, 2.0), (50.0, 4.0) })
        {
            var col = mesher.Column(n, psi);
            if (col.ByAxialForce || col.Active.Kind == ConstraintKind.CrackBody) continue;
            Assert.NotNull(SolverRay(section, criterion, mesher.Axis(n), n, psi, col.M));
        }
    }

    [Fact]
    public void Deviation_DecreasesWithRefineTol()
    {
        var section = Make("asymmetric");
        var criterion = new AcrcSurfaceCriterion();
        var crack = new StrainDomainMesher(section, criterion.CrackCriterion).Build();
        double Worst(double tol)
        {
            var mesher = new AcrcColumnMesher(section, criterion, new LimitSurfaceOptions { RefineTol = tol }, crack);
            var mesh = mesher.Build();
            double mMax = MaxMoment(mesh), w = 0;
            var rnd = new Random(11);
            double nMin = mesh.Vertices.Min(v => v.N), nMax = mesh.Vertices.Max(v => v.N);
            for (int i = 0; i < 16; i++)
            {
                double n = nMin * 0.95 + (nMax - nMin) * 0.95 * rnd.NextDouble();
                double psi = 2 * Math.PI * rnd.NextDouble();
                var col = mesher.Column(n, psi);
                if (!col.Converged) continue;
                w = Math.Max(w, Math.Abs(MeshRay(mesh, mesher.Axis(n), n, psi, 10 * mMax) - col.M) / mMax);
            }
            return w;
        }
        double coarse = Worst(0.004), fine = Worst(0.001);
        output.WriteLine($"RefineTol 0,004: {coarse:P3}; 0,001: {fine:P3}");
        Assert.True(fine < coarse, $"расхождение не убывает: {coarse:P3} → {fine:P3}");
    }
}
