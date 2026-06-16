using CSfea.Sparse;
using CSfea.Thermal.Elements;
using CSfea.Thermal.Materials;

namespace CSfea.Thermal;

/// <summary>
/// Сетка теплопроводности: узлы (X, Y), треугольные элементы CST (3 узла).
/// ndof = N (1 DOF на узел — температура). Разрежённая COO-сборка K и C.
/// </summary>
public sealed class HeatMesh
{
    /// <summary>Координата X узлов, длина <see cref="NNodes"/>.</summary>
    public double[] X { get; }

    /// <summary>Координата Y узлов, длина <see cref="NNodes"/>.</summary>
    public double[] Y { get; }

    /// <summary>Элементы — массивы из 3 индексов узлов.</summary>
    public int[][] Elements { get; }

    /// <summary>Число узлов.</summary>
    public int NNodes => X.Length;

    /// <summary>Число элементов.</summary>
    public int NElements => Elements.Length;

    /// <summary>Полное число степеней свободы (1 на узел).</summary>
    public int NDof => NNodes;

    /// <summary>Создать сетку из координат узлов и списка треугольников.</summary>
    public HeatMesh(double[] x, double[] y, int[][] elements)
    {
        if (x.Length != y.Length)
            throw new ArgumentException("Длины массивов X и Y должны совпадать.");
        X = x;
        Y = y;
        Elements = elements;
    }

    /// <summary>Собрать глобальную матрицу теплопроводности K.</summary>
    /// <param name="mat">Материал.</param>
    /// <param name="nodalT">Температура в узлах для оценки λ(T); если null — T=20°C.</param>
    public CooMatrix AssembleConductivity(IHeatMaterial mat, double[]? nodalT = null)
    {
        var coo = new CooMatrix(NDof, NDof, Elements.Length * 9);
        for (int e = 0; e < Elements.Length; e++)
        {
            var el = Elements[e];
            double T = ElementCentroidT(el, nodalT);
            double lambda = mat.Conductivity(T);
            var ke = HeatTri3.ElementK(lambda, ElementCoords(el));
            coo.AddBlock(el, ke);
        }
        return coo;
    }

    /// <summary>Собрать глобальную матрицу теплоёмкости C (consistent CST mass).</summary>
    /// <param name="mat">Материал.</param>
    /// <param name="nodalT">Температура в узлах для оценки ρc(T).</param>
    public CooMatrix AssembleCapacity(IHeatMaterial mat, double[] nodalT)
    {
        if (nodalT.Length != NNodes)
            throw new ArgumentException(
                $"Длина nodalT ({nodalT.Length}) не совпадает с числом узлов ({NNodes}).");

        var coo = new CooMatrix(NDof, NDof, Elements.Length * 9);
        for (int e = 0; e < Elements.Length; e++)
        {
            var el = Elements[e];
            double T = ElementCentroidT(el, nodalT);
            double rhocp = mat.VolumetricHeatCapacity(T);
            var me = HeatTri3.ElementM(rhocp, ElementCoords(el));
            coo.AddBlock(el, me);
        }
        return coo;
    }

    private double ElementCentroidT(int[] el, double[]? nodalT)
    {
        if (nodalT == null)
            return 20.0;
        return (nodalT[el[0]] + nodalT[el[1]] + nodalT[el[2]]) / 3.0;
    }

    private double[] ElementCoords(int[] el)
        => [X[el[0]], Y[el[0]], X[el[1]], Y[el[1]], X[el[2]], Y[el[2]]];
}
