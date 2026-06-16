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

    /// <summary>Локальная 12×12 пространственного элемента.</summary>
    public static double[,] Beam3dKLocal(IBeamSectionResponse section, double l)
    {
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
