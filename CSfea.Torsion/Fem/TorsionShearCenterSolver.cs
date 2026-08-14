using CSfea.Sparse;

namespace CSfea.Torsion;

/// <summary>
/// Центр кручения через МКЭ по депланационной формулировке (порт sectionproperties/fea.py):
/// два независимых результата — "elasticity" (по функциям сдвига ψ/φ, зависит от ν, основной
/// результат sectionproperties) и "Трефтц" (только по депланации ω, без ν). Переиспользует
/// глобальную матрицу K, уже собранную для функции Прандтля (тот же лапласиан, не зависит от
/// начала координат) — здесь нужны только новые векторы правой части.
/// </summary>
internal static class TorsionShearCenterSolver
{
    internal readonly struct Result
    {
        public required double ElasticX { get; init; }
        public required double ElasticY { get; init; }
        public required double TrefftzX { get; init; }
        public required double TrefftzY { get; init; }
        public required double[] Warping { get; init; }
        public required double WarpingConstant { get; init; }
        public required double DeltaS { get; init; }
        public required TorsionShearStressPostprocessor.UnitFields ShearUnitFields { get; init; }
    }

    internal static Result Compute(TorsionBoundary boundary, TorsionMesh mesh, CooMatrix k, double nu)
    {
        var (xc, yc, area, ixx, iyy, ixy) = TorsionGeoMoments.Compute(boundary);
        int ndof = mesh.NodesX.Length;

        var fWarp = new double[ndof];
        var fPsi = new double[ndof];
        var fPhi = new double[ndof];
        var mX = new double[ndof];
        var mY = new double[ndof];
        double scXint = 0.0, scYint = 0.0;

        foreach (var el in mesh.Triangles)
        {
            double[] coords = BuildCoords(mesh, el);
            var r = WarpingAssembler.Integrate(coords, el.Length, xc, yc, ixx, iyy, ixy, nu);
            for (int i = 0; i < el.Length; i++)
            {
                fWarp[el[i]] += r.FWarp[i];
                fPsi[el[i]] += r.FPsi[i];
                fPhi[el[i]] += r.FPhi[i];
                mX[el[i]] += r.MX[i];
                mY[el[i]] += r.MY[i];
            }
            scXint += r.ScXint;
            scYint += r.ScYint;
        }

        // Задача Неймана (K вырождена, нуль-пространство = константа) — закрепляем один узел
        // произвольно; f_torsion/f_psi/f_phi по построению ортогональны константе (условие
        // разрешимости), поэтому итоговые формулы не зависят от выбора узла закрепления.
        int[] pin = [0];
        double[] SolveNeumann(double[] f)
        {
            var reduced = DirichletReducer.Reduce(k, f, pin, uFixed: null);
            var solve = TorsionFemSolver.FactorizeSpd(reduced.Kff);
            double[] uFree = solve(reduced.Fmod);
            return DirichletReducer.Expand(ndof, reduced.Free, uFree, pin, uFixed: null);
        }

        double[] psi = SolveNeumann(fPsi);
        double[] phi = SolveNeumann(fPhi);
        double[] omega = SolveNeumann(fWarp);

        double deltaS = 2.0 * (1.0 + nu) * (ixx * iyy - ixy * ixy);
        double xSe = (0.5 * nu * scXint - Dot(fWarp, phi)) / deltaS;
        double ySe = (0.5 * nu * scYint + Dot(fWarp, psi)) / deltaS;

        double denom = ixx * iyy - ixy * ixy;
        double ixOmega = Dot(mX, omega);
        double iyOmega = Dot(mY, omega);
        double xSt = (ixy * ixOmega - iyy * iyOmega) / denom;
        double ySt = (ixx * ixOmega - ixy * iyOmega) / denom;

        // Секториальная жёсткость γ=Iω (порт warping_analysis из sectionproperties/section.py):
        // γ = ∫ω²dA − (∫ω dA)²/A − ySe·I_xω + xSe·I_yω (xSe,ySe — локальные, от центроида).
        double qOmega = TorsionPostprocessor.IntegrateField(mesh, omega);
        double iOmegaSq = TorsionPostprocessor.IntegrateFieldSquared(mesh, omega);
        double gamma = iOmegaSq - qOmega * qOmega / area - ySe * ixOmega + xSe * iyOmega;

        var shearFields = TorsionShearStressPostprocessor.Compute(mesh, psi, phi, xc, yc, ixx, iyy, ixy, nu);

        return new Result
        {
            ElasticX = xc + xSe, ElasticY = yc + ySe,
            TrefftzX = xc + xSt, TrefftzY = yc + ySt,
            Warping = omega,
            WarpingConstant = gamma,
            DeltaS = deltaS,
            ShearUnitFields = shearFields
        };
    }

    static double Dot(double[] a, double[] b)
    {
        double s = 0.0;
        for (int i = 0; i < a.Length; i++) s += a[i] * b[i];
        return s;
    }

    static double[] BuildCoords(TorsionMesh mesh, int[] el)
    {
        var c = new double[el.Length * 2];
        for (int k = 0; k < el.Length; k++)
        {
            c[2 * k] = mesh.NodesX[el[k]];
            c[2 * k + 1] = mesh.NodesY[el[k]];
        }
        return c;
    }
}
