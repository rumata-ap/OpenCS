using CSfea.Core;
using Xunit;

namespace CSfea.Tests;

/// <summary>
/// Жёсткость MITC4 не зависит от нумерации узлов: циклический сдвиг и обратный обход искажённого четырёхугольника дают
/// ту же матрицу в общих осях (с перестановкой узлов). До 26.11.2026 сдвиговые деформации тай-точек переводились через
/// транспонированный якобиан — сдвиг узлов на один менял K на ~8 %.
/// </summary>
public class Shell4NodeOrderTests
{
    static readonly Laminate Lam = new([new Ply(new OrthotropicMaterial(30e9, 30e9, 0.2, 12.5e9), 0, 0.2)]);

    [Theory]
    [InlineData(new[] { 1, 2, 3, 0 })]
    [InlineData(new[] { 2, 3, 0, 1 })]
    [InlineData(new[] { 0, 3, 2, 1 })]
    [InlineData(new[] { 2, 1, 0, 3 })]
    public void DistortedQuad_SameStiffnessForAnyNodeOrder(int[] order)
    {
        double[][] c = [[0, 0, 0], [1.1, 0.1, 0], [0.9, 0.8, 0], [-0.1, 1.2, 0]];
        var k = ShellElementMatrices.ElementKLinearGlobal(c, Lam);
        var kp = ShellElementMatrices.ElementKLinearGlobal(order.Select(i => c[i]).ToArray(), Lam);
        double max = 0, diff = 0;
        for (int a = 0; a < 4; a++)
            for (int b = 0; b < 4; b++)
                for (int i = 0; i < 6; i++)
                    for (int j = 0; j < 6; j++)
                    {
                        double x = k[6 * order[a] + i, 6 * order[b] + j];
                        max = Math.Max(max, Math.Abs(x));
                        diff = Math.Max(diff, Math.Abs(x - kp[6 * a + i, 6 * b + j]));
                    }
        Assert.True(diff < 1e-6 * max, $"max |ΔK| = {diff:E3} при max |K| = {max:E3}");
    }
}
