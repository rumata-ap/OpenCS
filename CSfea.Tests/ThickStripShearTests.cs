using CSfea.Core;

namespace CSfea.Tests;

/// <summary>
/// Полоса толстой пластины в цилиндрическом изгибе против балки Тимошенко: D = E·h³/(12(1−ν²)), S = k·G·h.
/// Размеры — стена музея из сверки со SCAD (кладка E = 2354 МПа, h = 0,38 м, пролёт 7,7 м, шаг сетки около 0,5 м).
/// Отличает лишнюю сдвиговую податливость КЭ (прогиб больше балочного при обычном G) от запирания
/// (прогиб меньше балки Бернулли при трансверсальном G × 100).
/// </summary>
[HarnessChecks]
public class ThickStripShearTests
{
    private const double E = 2354e6;
    private const double Nu = 0.25;
    private const double H = 0.38;
    private const double L = 7.7;
    private const double B = 1.0;
    private const double Q = 10e3;
    private const int Ny = 2;

    private enum Split { Quads, Diagonal, Alternating }

    [Fact]
    public static void RunAll()
    {
        foreach (bool cantilever in new[] { false, true })
            foreach (double shearFactor in new[] { 1.0, 100.0 })
            {
                TestHarness.Section($"Полоса h={H} м, L={L} м, {(cantilever ? "консоль" : "защемление по двум концам")}, G попер. × {shearFactor:0}");
                double d = E * H * H * H / (12.0 * (1.0 - Nu * Nu));
                double s = 5.0 / 6.0 * E / (2.0 * (1.0 + Nu)) * shearFactor * H;
                double wBend = cantilever ? Q * Math.Pow(L, 4) / (8.0 * d) : Q * Math.Pow(L, 4) / (384.0 * d);
                double wShear = cantilever ? Q * L * L / (2.0 * s) : Q * L * L / (8.0 * s);
                Console.WriteLine($"  балка: изгиб {wBend * 1e3:f4} мм + сдвиг {wShear * 1e3:f4} мм ({wShear / wBend * 100:f2} % изгиба)");
                // Нагрузка сведена в узловые силы без моментов: у консоли прогиб конца от этого не меняется, у защемлённой
                // по двум концам полосы прогиб занижен примерно на 4/nx² (1,6 % при 16 КЭ, 0,4 % при 32).
                foreach (int nx in new[] { 16, 32 })
                    foreach (var split in new[] { Split.Quads, Split.Diagonal, Split.Alternating })
                    {
                        double w = Deflection(split, cantilever, shearFactor, nx);
                        TestHarness.CheckRel($"{split}, {nx} КЭ: w/w_Тимошенко", w / (wBend + wShear), 1.0, cantilever ? 1e-3 : 5.0 / (nx * nx));
                    }
            }
    }

    /// <summary>Наибольший прогиб полосы (середина пролёта или свободный конец) под равномерной нагрузкой.</summary>
    private static double Deflection(Split split, bool cantilever, double shearFactor, int nx)
    {
        int Ni(int i, int j) => j * (nx + 1) + i;
        var nodes = new double[(nx + 1) * (Ny + 1)][];
        for (int j = 0; j <= Ny; j++)
            for (int i = 0; i <= nx; i++)
                nodes[Ni(i, j)] = [i * L / nx, j * B / Ny, 0.0];

        var elements = new List<int[]>();
        for (int j = 0; j < Ny; j++)
            for (int i = 0; i < nx; i++)
            {
                int a = Ni(i, j), b = Ni(i + 1, j), c = Ni(i + 1, j + 1), dd = Ni(i, j + 1);
                if (split == Split.Quads) elements.Add([a, b, c, dd]);
                else if (split == Split.Diagonal || (i + j) % 2 == 0) { elements.Add([a, b, c]); elements.Add([a, c, dd]); }
                else { elements.Add([a, b, dd]); elements.Add([b, c, dd]); }
            }

        double g = E / (2.0 * (1.0 + Nu));
        var resp = new LinearLaminateResponse(new Laminate([new Ply(
            new OrthotropicMaterial(E, E, Nu, g, g * shearFactor, g * shearFactor), 0.0, H)]));
        var mesh = new StructuralMesh(nodes, elements.Select(el => new StructuralShell(el, resp)).ToArray(), null);

        // Цилиндрический изгиб: свободны только uz и θy; концы защемлены.
        var bc = new BoundaryConditions(mesh).Fix(Enumerable.Range(0, nodes.Length), [0, 1, 3, 5]);
        bc.Fix(Enumerable.Range(0, Ny + 1).Select(j => Ni(0, j)));
        if (!cantilever) bc.Fix(Enumerable.Range(0, Ny + 1).Select(j => Ni(nx, j)));

        var f = new double[mesh.NDof];
        double cell = L / nx * (B / Ny);
        foreach (var el in elements)
            foreach (int node in el)
                f[6 * node + 2] -= Q * (el.Length == 4 ? cell : cell / 2.0) / el.Length;

        var u = mesh.SolveLinear(f, bc);
        int iMax = cantilever ? nx : nx / 2;
        return Enumerable.Range(0, Ny + 1).Average(j => Math.Abs(u[6 * Ni(iMax, j) + 2]));
    }
}
