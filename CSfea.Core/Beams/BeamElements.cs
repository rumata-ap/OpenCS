using CSfea.Sparse;

namespace CSfea.Core;

/// <summary>
/// Линейный балочный элемент Эйлера–Бернулли (2D: 3 DOF/узел; 3D: 6 DOF/узел).
/// Порт <c>fea/beam.py</c>.
/// </summary>
public static class BeamElements
{
    /// <summary>Нормализовать сечение к отклику (BeamSection → LinearBeamResponse).</summary>
    public static IBeamSectionResponse EnsureResponse(BeamSection section)
        => new LinearBeamResponse(section);

    /// <summary>
    /// Извлечь (EA, EI_y, EI_z, GJ) из отклика сечения при нулевых деформациях.
    /// Порт <c>_section_stiffness</c>.
    /// </summary>
    public static (double EA, double EIy, double EIz, double GJ) SectionStiffness(IBeamSectionResponse resp)
    {
        if (resp is LinearBeamResponse lin)
            return (lin.EA, lin.EIy, lin.EIz, lin.GJ);
        var j = resp.Tangent(0.0, 0.0, 0.0);
        return (j[0, 0], j[1, 1], j[2, 2], resp.TorsionalStiffness(0.0));
    }

    // -------------------- 2D --------------------

    /// <summary>Локальная 6×6 плоского элемента. DOF: [u1,v1,θ1, u2,v2,θ2].</summary>
    public static double[,] Beam2dKLocal(IBeamSectionResponse section, double l)
    {
        var (ea, _, eIz, _) = SectionStiffness(section);
        double l2 = l * l, l3 = l2 * l;
        var k = new double[6, 6];

        double eaL = ea / l;
        k[0, 0] = eaL; k[0, 3] = -eaL; k[3, 0] = -eaL; k[3, 3] = eaL;

        double k11 = 12.0 * eIz / l3;
        double k12 = 6.0 * eIz / l2;
        double k22 = 4.0 * eIz / l;
        double k23 = 2.0 * eIz / l;
        var idx = new[] { 1, 2, 4, 5 };
        var kb = new[,]
        {
            { k11, k12, -k11, k12 },
            { k12, k22, -k12, k23 },
            { -k11, -k12, k11, -k12 },
            { k12, k23, -k12, k22 },
        };
        for (int a = 0; a < 4; a++)
            for (int b = 0; b < 4; b++)
                k[idx[a], idx[b]] = kb[a, b];
        return k;
    }

    /// <summary>Матрица преобразования 6×6 (глобальная → локальная) и длина.</summary>
    public static (double[,] T, double L) Beam2dT(double[][] coords)
    {
        double dx0 = coords[1][0] - coords[0][0];
        double dy0 = coords[1][1] - coords[0][1];
        double l = Math.Sqrt(dx0 * dx0 + dy0 * dy0);
        if (l < 1e-14) throw new ArgumentException("Нулевая длина балки.");
        double c = dx0 / l, s = dy0 / l;
        var t = new double[6, 6];
        var r = new[,] { { c, s, 0.0 }, { -s, c, 0.0 }, { 0.0, 0.0, 1.0 } };
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
            {
                t[i, j] = r[i, j];
                t[3 + i, 3 + j] = r[i, j];
            }
        return (t, l);
    }

    /// <summary>Глобальная 6×6 плоского элемента.</summary>
    public static double[,] Beam2dKGlobal(double[][] coords, IBeamSectionResponse section)
    {
        var (t, l) = Beam2dT(coords);
        var kl = Beam2dKLocal(section, l);
        return Dense.MatMul(Dense.MatTMul(t, kl), t);
    }

    // -------------------- 3D --------------------

    /// <summary>Локальная 12×12 пространственного элемента. Сечение с замороженной связанной
    /// матрицей (<see cref="SecantBeamResponse"/>) идёт через связанную перегрузку.</summary>
    public static double[,] Beam3dKLocal(IBeamSectionResponse section, double l)
    {
        if (section is SecantBeamResponse secant)
            return Beam3dKLocal(secant.Matrix, secant.GJ, l, secant.Shear);
        var (ea, eIy, eIz, gj) = SectionStiffness(section);
        double l2 = l * l, l3 = l2 * l;
        var k = new double[12, 12];

        double eaL = ea / l;
        k[0, 0] += eaL; k[0, 6] += -eaL; k[6, 0] += -eaL; k[6, 6] += eaL;

        double gjL = gj / l;
        k[3, 3] += gjL; k[3, 9] += -gjL; k[9, 3] += -gjL; k[9, 9] += gjL;

        // Изгиб x·y (момент вокруг z, EI_z): DOF v, θz.
        {
            double k11 = 12.0 * eIz / l3, k12 = 6.0 * eIz / l2, k22 = 4.0 * eIz / l, k23 = 2.0 * eIz / l;
            var kb = new[,]
            {
                { k11, k12, -k11, k12 },
                { k12, k22, -k12, k23 },
                { -k11, -k12, k11, -k12 },
                { k12, k23, -k12, k22 },
            };
            var idx = new[] { 1, 5, 7, 11 };
            for (int a = 0; a < 4; a++)
                for (int b = 0; b < 4; b++)
                    k[idx[a], idx[b]] += kb[a, b];
        }
        // Изгиб x·z (момент вокруг y, EI_y): DOF w, θy; w' = −θy.
        {
            double k11 = 12.0 * eIy / l3, k12 = 6.0 * eIy / l2, k22 = 4.0 * eIy / l, k23 = 2.0 * eIy / l;
            var kb = new[,]
            {
                { k11, -k12, -k11, -k12 },
                { -k12, k22, k12, k23 },
                { -k11, k12, k11, k12 },
                { -k12, k23, k12, k22 },
            };
            var idx = new[] { 2, 4, 8, 10 };
            for (int a = 0; a < 4; a++)
                for (int b = 0; b < 4; b++)
                    k[idx[a], idx[b]] += kb[a, b];
        }
        return k;
    }

    /// <summary>
    /// Локальная 12×12 пространственного элемента со связанной матрицей сечения
    /// <paramref name="s"/>: (N, M_y, M_z) = S·(ε₀, κ_y, κ_z), постоянной по длине, плюс GJ.
    /// Кинематика — та же, что в <see cref="BeamCorotational.Beam3dKLocalFromResponse"/>:
    /// ε₀ = u′, κ_z = v″, κ_y = −w″ (w′ = −θ_y), прогибы — кубические функции Эрмита.
    ///
    /// Продольное перемещение дополнено внутренним квадратичным «пузырём» 4ξ(1 − ξ) со статической
    /// конденсацией: при связанной S (смещённая после трещин нейтральная ось, S₀₁ ≠ 0) и линейной
    /// эпюре моментов условие N = 0 требует ε₀ = −e·κ(x), линейной по длине; при линейном u ε₀
    /// постоянна, и КЭ «запирается» — завышает жёсткость тем сильнее, чем крупнее КЭ. С пузырём
    /// один КЭ точен для линейной эпюры M. При диагональной S пузырь отделяется (∫u′_пузыря dx = 0) и
    /// матрица совпадает с <see cref="Beam3dKLocal(IBeamSectionResponse, double)"/>.
    /// K = ∫ Bᵀ·S·B dx — подынтегральное выражение не выше квадратичного, 2 точки Гаусса точны.
    /// </summary>
    public static double[,] Beam3dKLocal(double[,] s, double gj, double l)
    {
        var k13 = Beam3dKCoupled13(s, l);
        var k = new double[12, 12];
        double kbb = k13[12, 12];
        for (int i = 0; i < 12; i++)
            for (int j = 0; j < 12; j++)
                k[i, j] = k13[i, j] - (kbb > 0.0 ? k13[i, 12] * k13[12, j] / kbb : 0.0);

        double gjL = gj / l;
        k[3, 3] += gjL; k[3, 9] += -gjL; k[9, 3] += -gjL; k[9, 9] += gjL;
        return k;
    }

    /// <summary>
    /// Связанная секущая КЭ (<see cref="Beam3dKLocal(double[,], double, double)"/>) с податливостью сдвига (Тимошенко):
    /// у консоли, закреплённой в узле i, к податливости F = K_jj⁻¹ добавляется L/GA_v по поперечным перемещениям конца
    /// (поперечная сила по КЭ постоянна, сечение от сдвига не поворачивается), K_jj' = F⁻¹, остальные блоки — из
    /// равновесия: K = [Γᵀ·K_jj'·Γ, −Γᵀ·K_jj'; −K_jj'·Γ, K_jj'], Γ — перенос жёсткого тела из i в j. Для постоянной EI —
    /// точный КЭ Тимошенко; при <see cref="BeamShearStiffness.Rigid"/> — прежний КЭ Бернулли.
    /// </summary>
    public static double[,] Beam3dKLocal(double[,] s, double gj, double l, BeamShearStiffness shear)
    {
        var k = Beam3dKLocal(s, gj, l);
        if (shear.IsRigid) return k;
        var kc = ShearCantilever(k, l, shear);
        var g = RigidTransfer(l);
        var kcg = Dense.MatMul(kc, g);
        var r = new double[12, 12];
        var gtkcg = Dense.MatTMul(g, kcg);
        for (int i = 0; i < 6; i++)
            for (int j = 0; j < 6; j++)
            {
                r[i, j] = gtkcg[i, j];
                r[i, 6 + j] = -kcg[j, i];
                r[6 + i, j] = -kcg[i, j];
                r[6 + i, 6 + j] = kc[i, j];
            }
        return r;
    }

    /// <summary>
    /// Разделение локальных перемещений КЭ Тимошенко (<see cref="Beam3dKLocal(double[,], double, double, BeamShearStiffness)"/>)
    /// на изгибные и сдвиговые: из перемещения конца j относительно жёсткого тела узла i вычитается сдвиговая часть
    /// Q·L/GA_v. Изгибные перемещения подаются в <see cref="Beam3dCoupledStrains"/>; γ — углы сдвига КЭ по осям y и z
    /// (знак — как у поперечной силы конца j).
    /// </summary>
    public static (double[] Bending, double GammaY, double GammaZ) Beam3dShearSplit(double[,] s, double gj, double l,
        BeamShearStiffness shear, double[] dLocal)
    {
        if (shear.IsRigid) return (dLocal, 0.0, 0.0);
        var kc = ShearCantilever(Beam3dKLocal(s, gj, l), l, shear);
        var g = RigidTransfer(l);
        var di = dLocal[..6];
        var rel = Dense.SubV(dLocal[6..], Dense.MatVec(g, di));
        var p = Dense.MatVec(kc, rel);
        double gy = double.IsPositiveInfinity(shear.GAvY) ? 0.0 : p[1] / shear.GAvY;
        double gz = double.IsPositiveInfinity(shear.GAvZ) ? 0.0 : p[2] / shear.GAvZ;
        var bend = (double[])dLocal.Clone();
        bend[7] -= gy * l;
        bend[8] -= gz * l;
        return (bend, gy, gz);
    }

    // Жёсткость консоли (узел i закреплён) с податливостью сдвига; кручение (DOF 3) не связано с остальными и не меняется.
    private static double[,] ShearCantilever(double[,] k, double l, BeamShearStiffness shear)
    {
        int[] idx = [0, 1, 2, 4, 5];
        var kc5 = new double[5, 5];
        for (int a = 0; a < 5; a++)
            for (int b = 0; b < 5; b++)
                kc5[a, b] = k[6 + idx[a], 6 + idx[b]];
        var f = Inverse(kc5);
        if (!double.IsPositiveInfinity(shear.GAvY)) f[1, 1] += l / shear.GAvY;
        if (!double.IsPositiveInfinity(shear.GAvZ)) f[2, 2] += l / shear.GAvZ;
        var kc5s = Inverse(f);
        var kc = new double[6, 6];
        for (int a = 0; a < 5; a++)
            for (int b = 0; b < 5; b++)
                kc[idx[a], idx[b]] = 0.5 * (kc5s[a, b] + kc5s[b, a]);
        kc[3, 3] = k[9, 9];
        return kc;
    }

    // Перемещения конца j при жёстком смещении узла i: v_j = v_i + L·θz_i, w_j = w_i − L·θy_i (w′ = −θy).
    private static double[,] RigidTransfer(double l)
    {
        var g = new double[6, 6];
        for (int i = 0; i < 6; i++) g[i, i] = 1.0;
        g[1, 5] = l;
        g[2, 4] = -l;
        return g;
    }

    // Обращение малой симметричной положительно определённой матрицы (Гаусс — Жордан с выбором ведущего).
    private static double[,] Inverse(double[,] m)
    {
        int n = m.GetLength(0);
        var a = (double[,])m.Clone();
        var r = new double[n, n];
        for (int i = 0; i < n; i++) r[i, i] = 1.0;
        for (int c = 0; c < n; c++)
        {
            int piv = c;
            for (int i = c + 1; i < n; i++) if (Math.Abs(a[i, c]) > Math.Abs(a[piv, c])) piv = i;
            if (!(Math.Abs(a[piv, c]) > 0.0) || !double.IsFinite(a[piv, c]))
                throw new InvalidOperationException("Вырожденная жёсткость консоли КЭ: податливость сдвига не добавить.");
            if (piv != c)
                for (int j = 0; j < n; j++)
                {
                    (a[c, j], a[piv, j]) = (a[piv, j], a[c, j]);
                    (r[c, j], r[piv, j]) = (r[piv, j], r[c, j]);
                }
            double inv = 1.0 / a[c, c];
            for (int j = 0; j < n; j++) { a[c, j] *= inv; r[c, j] *= inv; }
            for (int i = 0; i < n; i++)
            {
                if (i == c || a[i, c] == 0.0) continue;
                double f = a[i, c];
                for (int j = 0; j < n; j++) { a[i, j] -= f * a[c, j]; r[i, j] -= f * r[c, j]; }
            }
        }
        return r;
    }

    /// <summary>
    /// Обобщённые деформации (ε₀, κ_y, κ_z) в точке ξ ∈ [0, 1] КЭ со связанной матрицей сечения по
    /// локальным перемещениям узлов <paramref name="dLocal"/> (12) — с восстановленной амплитудой
    /// пузыря продольного перемещения (см. <see cref="Beam3dKLocal(double[,], double, double)"/>).
    /// </summary>
    public static (double Eps0, double KappaY, double KappaZ) Beam3dCoupledStrains(
        double[,] s, double l, double[] dLocal, double xi)
    {
        var k13 = Beam3dKCoupled13(s, l);
        double kbb = k13[12, 12], alpha = 0.0;
        if (kbb > 0.0)
        {
            double r = 0.0;
            for (int j = 0; j < 12; j++) r += k13[12, j] * dLocal[j];
            alpha = -r / kbb;
        }
        var b = CoupledB(l, xi);
        double e0 = 0.0, ky = 0.0, kz = 0.0;
        for (int j = 0; j < 12; j++)
        {
            e0 += b[0, j] * dLocal[j];
            ky += b[1, j] * dLocal[j];
            kz += b[2, j] * dLocal[j];
        }
        return (e0 + b[0, 12] * alpha, ky, kz);
    }

    // 13×13: 12 узловых степеней свободы + амплитуда пузыря продольного перемещения.
    private static double[,] Beam3dKCoupled13(double[,] s, double l)
    {
        if (s.GetLength(0) != 3 || s.GetLength(1) != 3)
            throw new ArgumentException("Матрица сечения должна быть 3×3", nameof(s));
        var k = new double[13, 13];
        foreach (double x in new[] { 0.5 - 0.5 / Math.Sqrt(3.0), 0.5 + 0.5 / Math.Sqrt(3.0) })
        {
            var b = CoupledB(l, x);
            Dense.AddScaledInPlace(k, Dense.MatMul(Dense.MatTMul(b, s), b), 0.5 * l);
        }
        return k;
    }

    // B (3×13) в точке ξ: строки ε₀, κ_y = −w″, κ_z = v″; столбец 12 — пузырь u = 4ξ(1 − ξ)·α.
    private static double[,] CoupledB(double l, double x)
    {
        double l2 = l * l;
        // Вторые производные функций Эрмита по x на отрезке длины l.
        double n1 = (-6.0 + 12.0 * x) / l2, n2 = (-4.0 + 6.0 * x) / l;
        double n3 = (6.0 - 12.0 * x) / l2, n4 = (-2.0 + 6.0 * x) / l;
        var b = new double[3, 13];
        b[0, 0] = -1.0 / l; b[0, 6] = 1.0 / l; b[0, 12] = 4.0 * (1.0 - 2.0 * x) / l;
        b[1, 2] = -n1; b[1, 4] = n2; b[1, 8] = -n3; b[1, 10] = n4;
        b[2, 1] = n1; b[2, 5] = n2; b[2, 7] = n3; b[2, 11] = n4;
        return b;
    }

    /// <summary>
    /// Секущая матрица КЭ по сечениям в трёх точках Лобатто (начало, середина, конец) — средняя
    /// податливость S_эл⁻¹ = ∫S⁻¹ dx / L (Симпсон, веса 1/6, 4/6, 1/6): при линейной эпюре моментов
    /// жёсткость не «усредняется в середину», а складывается как у последовательно работающих
    /// участков.
    /// </summary>
    public static double[,] MeanCompliance(double[,] sStart, double[,] sMid, double[,] sEnd)
    {
        var f = Dense.Add(Dense.Scale(Inverse3(sStart), 1.0 / 6.0),
                Dense.Add(Dense.Scale(Inverse3(sMid), 4.0 / 6.0), Dense.Scale(Inverse3(sEnd), 1.0 / 6.0)));
        var r = Inverse3(f);
        for (int i = 0; i < 3; i++)
            for (int j = i + 1; j < 3; j++)
                r[i, j] = r[j, i] = 0.5 * (r[i, j] + r[j, i]);
        return r;
    }

    private static double[,] Inverse3(double[,] m)
    {
        double c00 = m[1, 1] * m[2, 2] - m[1, 2] * m[2, 1];
        double c01 = m[1, 2] * m[2, 0] - m[1, 0] * m[2, 2];
        double c02 = m[1, 0] * m[2, 1] - m[1, 1] * m[2, 0];
        double det = m[0, 0] * c00 + m[0, 1] * c01 + m[0, 2] * c02;
        double scale = Math.Abs(m[0, 0] * m[1, 1] * m[2, 2]);
        if (!(Math.Abs(det) > 1e-14 * scale) || !double.IsFinite(det))
            throw new InvalidOperationException("Вырожденная матрица сечения: податливость не определена.");
        double inv = 1.0 / det;
        return new[,]
        {
            { c00 * inv, (m[0, 2] * m[2, 1] - m[0, 1] * m[2, 2]) * inv, (m[0, 1] * m[1, 2] - m[0, 2] * m[1, 1]) * inv },
            { c01 * inv, (m[0, 0] * m[2, 2] - m[0, 2] * m[2, 0]) * inv, (m[0, 2] * m[1, 0] - m[0, 0] * m[1, 2]) * inv },
            { c02 * inv, (m[0, 1] * m[2, 0] - m[0, 0] * m[2, 1]) * inv, (m[0, 0] * m[1, 1] - m[0, 1] * m[1, 0]) * inv },
        };
    }

    /// <summary>Базис элемента (строки — оси) и длина. Порт <c>beam3d_frame</c>.</summary>
    public static (double[,] R, double L) Beam3dFrame(double[][] coords, double[]? refVec = null)
    {
        var dx = Dense.SubV(coords[1], coords[0]);
        double l = Dense.Norm(dx);
        if (l < 1e-14) throw new ArgumentException("Нулевая длина балки.");
        var e1 = Dense.ScaleV(dx, 1.0 / l);

        double[] rv;
        if (refVec == null)
            rv = Math.Abs(e1[2]) > 0.9999 ? new[] { 1.0, 0.0, 0.0 } : new[] { 0.0, 0.0, 1.0 };
        else
            rv = refVec;

        double proj = Dense.Dot(rv, e1);
        var e2 = Dense.SubV(rv, Dense.ScaleV(e1, proj));
        double n2 = Dense.Norm(e2);
        if (n2 < 1e-10)
            throw new ArgumentException("ref_vec коллинеарен оси балки — задайте другой.");
        e2 = Dense.ScaleV(e2, 1.0 / n2);
        var e3 = Dense.Cross(e1, e2);
        var r = new[,]
        {
            { e1[0], e1[1], e1[2] },
            { e2[0], e2[1], e2[2] },
            { e3[0], e3[1], e3[2] },
        };
        return (r, l);
    }

    /// <summary>Матрица преобразования 12×12 и длина.</summary>
    public static (double[,] T, double L) Beam3dT(double[][] coords, double[]? refVec = null)
    {
        var (r, l) = Beam3dFrame(coords, refVec);
        var t = new double[12, 12];
        for (int k = 0; k < 4; k++)
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                    t[3 * k + i, 3 * k + j] = r[i, j];
        return (t, l);
    }

    /// <summary>Глобальная 12×12 пространственного элемента.</summary>
    public static double[,] Beam3dKGlobal(double[][] coords, IBeamSectionResponse section, double[]? refVec = null)
    {
        var (t, l) = Beam3dT(coords, refVec);
        var kl = Beam3dKLocal(section, l);
        return Dense.MatMul(Dense.MatTMul(t, kl), t);
    }
}
