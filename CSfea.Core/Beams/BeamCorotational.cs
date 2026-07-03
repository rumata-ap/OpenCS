using CSfea.Sparse;

namespace CSfea.Core;

/// <summary>
/// Коротационная нелинейная формулировка балки Эйлера–Бернулли (2D и 3D).
/// Порт элементной части <c>fea/beam_corotational.py</c>.
/// </summary>
public static class BeamCorotational
{
    private static double WrapPi(double angle) => Math.Atan2(Math.Sin(angle), Math.Cos(angle));

    // ==================== 2D ====================

    /// <summary>Ядро CR-элемента 2D: (f_int (6), K_T (6×6)).</summary>
    public static (double[] F, double[,] K) Beam2dKernel(double[][] coordsRef,
                                                         IBeamSectionResponse section, double[] uElem)
    {
        double dx0X = coordsRef[1][0] - coordsRef[0][0];
        double dx0Y = coordsRef[1][1] - coordsRef[0][1];
        double l0 = Math.Sqrt(dx0X * dx0X + dx0Y * dx0Y);
        double beta0 = Math.Atan2(dx0Y, dx0X);

        double x1X = coordsRef[0][0] + uElem[0], x1Y = coordsRef[0][1] + uElem[1];
        double x2X = coordsRef[1][0] + uElem[3], x2Y = coordsRef[1][1] + uElem[4];
        double theta1 = uElem[2], theta2 = uElem[5];
        double dxX = x2X - x1X, dxY = x2Y - x1Y;
        double ln = Math.Sqrt(dxX * dxX + dxY * dxY);
        if (ln < 1e-14) throw new ArgumentException("Балка схлопнулась в точку.");
        double beta = Math.Atan2(dxY, dxX);

        double alpha = WrapPi(beta - beta0);
        double uBar = ln - l0;
        double tb1 = WrapPi(theta1 - alpha);
        double tb2 = WrapPi(theta2 - alpha);
        var dL = new[] { uBar, tb1, tb2 };

        var (ea, _, eIz, _) = BeamElements.SectionStiffness(section);
        var kL = new[,]
        {
            { ea / l0, 0.0, 0.0 },
            { 0.0, 4.0 * eIz / l0, 2.0 * eIz / l0 },
            { 0.0, 2.0 * eIz / l0, 4.0 * eIz / l0 },
        };
        var qL = Dense.MatVec(kL, dL);
        double nAxial = qL[0], m1 = qL[1], m2 = qL[2];

        double c = Math.Cos(beta), s = Math.Sin(beta);
        var r = new[] { -c, -s, 0.0, c, s, 0.0 };
        var z = new[] { s / ln, -c / ln, 0.0, -s / ln, c / ln, 0.0 };
        var b = new double[3, 6];
        for (int j = 0; j < 6; j++)
        {
            b[0, j] = r[j];
            b[1, j] = -z[j];
            b[2, j] = -z[j];
        }
        b[1, 2] += 1.0; // e3
        b[2, 5] += 1.0; // e6

        var f = Dense.MatTVec(b, qL);

        var kMat = Dense.MatMul(Dense.MatTMul(b, kL), b);
        var k = (double[,])kMat.Clone();
        double cg2 = (m1 + m2) / ln;
        for (int i = 0; i < 6; i++)
            for (int j = 0; j < 6; j++)
                k[i, j] += nAxial * ln * z[i] * z[j]
                         + cg2 * (r[i] * z[j] + z[i] * r[j]);
        return (f, k);
    }

    /// <summary>Вектор внутренних сил (6) плоского CR-элемента.</summary>
    public static double[] Beam2dInternalForce(double[][] coordsRef, IBeamSectionResponse section, double[] uElem)
        => Beam2dKernel(coordsRef, section, uElem).F;

    /// <summary>Тангенциальная матрица (6×6) плоского CR-элемента.</summary>
    public static double[,] Beam2dTangent(double[][] coordsRef, IBeamSectionResponse section, double[] uElem)
        => Beam2dKernel(coordsRef, section, uElem).K;

    // ==================== 3D ====================

    /// <summary>Локальные деформационные DOF p_l (12) пространственного CR-элемента.</summary>
    public static double[] Beam3dPLocal(double[][] coordsRef, double[,] e0, double l0, double[] uElem)
    {
        var x1 = new[] { coordsRef[0][0] + uElem[0], coordsRef[0][1] + uElem[1], coordsRef[0][2] + uElem[2] };
        var x2 = new[] { coordsRef[1][0] + uElem[6], coordsRef[1][1] + uElem[7], coordsRef[1][2] + uElem[8] };
        var dx = Dense.SubV(x2, x1);
        double ln = Dense.Norm(dx);
        if (ln < 1e-14) throw new ArgumentException("Балка схлопнулась в точку.");
        var e1r = Dense.ScaleV(dx, 1.0 / ln);

        var rg1 = So3.Exp(new[] { uElem[3], uElem[4], uElem[5] });
        var rg2 = So3.Exp(new[] { uElem[9], uElem[10], uElem[11] });
        var e0t = Dense.Transpose(e0);
        var t1 = Dense.MatMul(rg1, e0t);
        var t2 = Dense.MatMul(rg2, e0t);

        var y1 = new[] { t1[0, 1], t1[1, 1], t1[2, 1] };
        var y2 = new[] { t2[0, 1], t2[1, 1], t2[2, 1] };
        var q = Dense.ScaleV(Dense.AddV(y1, y2), 0.5);
        var e2r = Dense.SubV(q, Dense.ScaleV(e1r, Dense.Dot(q, e1r)));
        double n2 = Dense.Norm(e2r);
        if (n2 < 1e-10)
        {
            var yf = y1;
            e2r = Dense.SubV(yf, Dense.ScaleV(e1r, Dense.Dot(yf, e1r)));
            n2 = Dense.Norm(e2r);
            if (n2 < 1e-10) throw new InvalidOperationException("Невозможно построить CR-базис (вырождение).");
        }
        e2r = Dense.ScaleV(e2r, 1.0 / n2);
        var e3r = Dense.Cross(e1r, e2r);
        var er = new[,]
        {
            { e1r[0], e1r[1], e1r[2] },
            { e2r[0], e2r[1], e2r[2] },
            { e3r[0], e3r[1], e3r[2] },
        };

        var rDef1 = Dense.MatMul(Dense.MatMul(er, rg1), e0t);
        var rDef2 = Dense.MatMul(Dense.MatMul(er, rg2), e0t);
        var tb1 = So3.Log(rDef1);
        var tb2 = So3.Log(rDef2);

        var pl = new double[12];
        pl[3] = tb1[0]; pl[4] = tb1[1]; pl[5] = tb1[2];
        pl[6] = ln - l0;
        pl[9] = tb2[0]; pl[10] = tb2[1]; pl[11] = tb2[2];
        return pl;
    }

    private static readonly double[] GaussXi =
        { (1.0 - 1.0 / 1.7320508075688772) * 0.5, (1.0 + 1.0 / 1.7320508075688772) * 0.5 };
    private static readonly double[] GaussW = { 0.5, 0.5 };

    /// <summary>Локальные силы (12) через нелинейный отклик сечения (2-точечная квадратура).</summary>
    public static double[] Beam3dForcesFromResponse(IBeamSectionResponse section, double[] pl, double l0)
    {
        double eps0 = pl[6] / l0;
        double ty1 = pl[4], tz1 = pl[5], ty2 = pl[10], tz2 = pl[11];
        var fl = new double[12];
        for (int g = 0; g < 2; g++)
        {
            double xi = GaussXi[g], w = GaussW[g];
            double bb = -4.0 + 6.0 * xi;
            double cc = -2.0 + 6.0 * xi;
            double kappaY = (bb * ty1 + cc * ty2) / l0;
            double kappaZ = (bb * tz1 + cc * tz2) / l0;
            var force = section.Forces(eps0, kappaY, kappaZ);
            fl[0] -= w * force.N;
            fl[6] += w * force.N;
            fl[4] += w * force.My * bb;
            fl[10] += w * force.My * cc;
            fl[5] += w * force.Mz * bb;
            fl[11] += w * force.Mz * cc;
        }
        fl[1] = (fl[5] + fl[11]) / l0;
        fl[7] = -(fl[5] + fl[11]) / l0;
        fl[2] = -(fl[4] + fl[10]) / l0;
        fl[8] = (fl[4] + fl[10]) / l0;

        double twist = (pl[9] - pl[3]) / l0;
        double tq = section.TorsionalStiffness(twist) * twist;
        fl[3] = -tq;
        fl[9] = tq;
        return fl;
    }

    /// <summary>Материальная локальная K (12×12) через касательную сечения (2-точечная квадратура).</summary>
    public static double[,] Beam3dKLocalFromResponse(IBeamSectionResponse section, double[] pl, double l0)
    {
        double eps0 = pl[6] / l0;
        double ty1 = pl[4], tz1 = pl[5], ty2 = pl[10], tz2 = pl[11];
        var kL = new double[12, 12];
        for (int g = 0; g < 2; g++)
        {
            double xi = GaussXi[g], w = GaussW[g];
            double bb = -4.0 + 6.0 * xi;
            double cc = -2.0 + 6.0 * xi;
            double kappaY = (bb * ty1 + cc * ty2) / l0;
            double kappaZ = (bb * tz1 + cc * tz2) / l0;
            var j = section.Tangent(eps0, kappaY, kappaZ);
            var be = new double[3, 12];
            be[0, 6] = 1.0 / l0;
            be[1, 4] = bb / l0;
            be[1, 10] = cc / l0;
            be[2, 5] = bb / l0;
            be[2, 11] = cc / l0;
            var term = Dense.MatMul(Dense.MatTMul(be, j), be);
            Dense.AddScaledInPlace(kL, term, w * l0);
        }
        double twist = (pl[9] - pl[3]) / l0;
        double gjL = section.TorsionalStiffness(twist) / l0;
        kL[3, 3] += gjL; kL[3, 9] -= gjL; kL[9, 3] -= gjL; kL[9, 9] += gjL;
        return kL;
    }

    private static double[,] ComputeBMatrix(double[][] coordsRef, double[,] e0, double l0, double[] uElem, double eps)
    {
        var b = new double[12, 12];
        for (int j = 0; j < 12; j++)
        {
            double scale = Math.Max(Math.Abs(uElem[j]), 1.0);
            double h = eps * scale;
            var up = (double[])uElem.Clone(); up[j] += h;
            var um = (double[])uElem.Clone(); um[j] -= h;
            var pp = Beam3dPLocal(coordsRef, e0, l0, up);
            var pm = Beam3dPLocal(coordsRef, e0, l0, um);
            for (int i = 0; i < 12; i++) b[i, j] = (pp[i] - pm[i]) / (2.0 * h);
        }
        return b;
    }

    private static (double[] Fg, double[,] B, double[] Pl0, double[] Fl) InternalAndB(
        double[][] coordsRef, double[,] e0, double l0, double[,] kL, double[] uElem,
        IBeamSectionResponse section, double eps = 1e-7)
    {
        var pl0 = Beam3dPLocal(coordsRef, e0, l0, uElem);
        var b = ComputeBMatrix(coordsRef, e0, l0, uElem, eps);
        double[] fl = section is not LinearBeamResponse
            ? Beam3dForcesFromResponse(section, pl0, l0)
            : Dense.MatVec(kL, pl0);
        var fg = Dense.MatTVec(b, fl);
        return (fg, b, pl0, fl);
    }

    /// <summary>Вектор внутренних сил (12) пространственного CR-элемента.</summary>
    public static double[] Beam3dInternalForce(double[][] coordsRef, IBeamSectionResponse section,
                                               double[] uElem, double[]? refVec = null)
    {
        var (e0, l0) = BeamElements.Beam3dFrame(coordsRef, refVec);
        var kL = BeamElements.Beam3dKLocal(section, l0);
        return InternalAndB(coordsRef, e0, l0, kL, uElem, section).Fg;
    }

    /// <summary>Тангенциальная матрица (12×12) пространственного CR-элемента.</summary>
    public static double[,] Beam3dTangent(double[][] coordsRef, IBeamSectionResponse section,
                                          double[] uElem, double[]? refVec = null,
                                          bool numerical = true, double eps = 1e-6)
    {
        var (e0, l0) = BeamElements.Beam3dFrame(coordsRef, refVec);
        var kL = BeamElements.Beam3dKLocal(section, l0);

        if (!numerical)
        {
            var (_, b0, _, _) = InternalAndB(coordsRef, e0, l0, kL, uElem, section);
            return Dense.MatMul(Dense.MatMul(Dense.Transpose(b0), kL), b0);
        }

        if (section is not LinearBeamResponse)
        {
            var pl0 = Beam3dPLocal(coordsRef, e0, l0, uElem);
            var kLMat = Beam3dKLocalFromResponse(section, pl0, l0);
            var b = ComputeBMatrix(coordsRef, e0, l0, uElem, eps);
            var k = Dense.MatMul(Dense.MatMul(Dense.Transpose(b), kLMat), b);
            return Symmetrize(k);
        }

        var kNum = new double[12, 12];
        for (int j = 0; j < 12; j++)
        {
            double scale = Math.Max(Math.Abs(uElem[j]), 1.0);
            double h = eps * scale;
            var up = (double[])uElem.Clone(); up[j] += h;
            var um = (double[])uElem.Clone(); um[j] -= h;
            var fp = InternalAndB(coordsRef, e0, l0, kL, up, section).Fg;
            var fm = InternalAndB(coordsRef, e0, l0, kL, um, section).Fg;
            for (int i = 0; i < 12; i++) kNum[i, j] = (fp[i] - fm[i]) / (2.0 * h);
        }
        return Symmetrize(kNum);
    }

    private static double[,] Symmetrize(double[,] k)
    {
        int n = k.GetLength(0);
        var s = new double[n, n];
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
                s[i, j] = 0.5 * (k[i, j] + k[j, i]);
        return s;
    }
}
