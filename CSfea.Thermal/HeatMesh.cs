using CSfea.Sparse;
using CSfea.Thermal.Elements;
using CSfea.Thermal.Materials;

namespace CSfea.Thermal;

/// <summary>
/// Сетка теплопроводности: узлы (X, Y), треугольные элементы T3 (3 узла) или T6 (6 узлов).
/// ndof = N (1 DOF на узел — температура). Разрежённая COO-сборка K и C.
/// </summary>
public sealed class HeatMesh
{
    /// <summary>Координата X узлов, длина <see cref="NNodes"/>.</summary>
    public double[] X { get; }

    /// <summary>Координата Y узлов, длина <see cref="NNodes"/>.</summary>
    public double[] Y { get; }

    /// <summary>Элементы — массивы из 3 (T3) или 6 (T6) индексов узлов.</summary>
    public int[][] Elements { get; }

    /// <summary>Число узлов.</summary>
    public int NNodes => X.Length;

    /// <summary>Число элементов.</summary>
    public int NElements => Elements.Length;

    /// <summary>Полное число степеней свободы (1 на узел).</summary>
    public int NDof => NNodes;

    /// <summary>Квадратичная T6-сетка (6 узлов на элемент).</summary>
    public bool IsQuadratic => Elements.Length > 0 && Elements[0].Length == 6;

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
    public CooMatrix AssembleConductivity(IHeatMaterial mat, double[]? nodalT = null)
    {
        int block = IsQuadratic ? 36 : 9;
        var coo = new CooMatrix(NDof, NDof, Elements.Length * block);
        for (int e = 0; e < Elements.Length; e++)
        {
            var el = Elements[e];
            double T = ElementMeanTemperature(el, nodalT);
            double lambda = mat.Conductivity(T);
            if (el.Length == 6)
                coo.AddBlock(el, HeatTri6.ElementK(lambda, ElementCoords(el)));
            else if (el.Length == 3)
                coo.AddBlock(el, HeatTri3.ElementK(lambda, ElementCoords(el)));
            else
                throw new InvalidOperationException($"Элемент #{e}: ожидается 3 или 6 узлов, получено {el.Length}.");
        }
        return coo;
    }

    /// <summary>Собрать глобальную матрицу теплоёмкости C.</summary>
    public CooMatrix AssembleCapacity(IHeatMaterial mat, double[] nodalT)
    {
        if (nodalT.Length != NNodes)
            throw new ArgumentException(
                $"Длина nodalT ({nodalT.Length}) не совпадает с числом узлов ({NNodes}).");

        int block = IsQuadratic ? 36 : 9;
        var coo = new CooMatrix(NDof, NDof, Elements.Length * block);
        for (int e = 0; e < Elements.Length; e++)
        {
            var el = Elements[e];
            double T = ElementMeanTemperature(el, nodalT);
            double rhocp = mat.VolumetricHeatCapacity(T);
            if (el.Length == 6)
                coo.AddBlock(el, HeatTri6.ElementM(rhocp, ElementCoords(el)));
            else if (el.Length == 3)
                coo.AddBlock(el, HeatTri3.ElementM(rhocp, ElementCoords(el)));
            else
                throw new InvalidOperationException($"Элемент #{e}: ожидается 3 или 6 узлов, получено {el.Length}.");
        }
        return coo;
    }

    double ElementMeanTemperature(int[] el, double[]? nodalT)
    {
        if (nodalT == null)
            return 20.0;
        double sum = 0;
        for (int i = 0; i < el.Length; i++)
            sum += nodalT[el[i]];
        return sum / el.Length;
    }

    double[] ElementCoords(int[] el)
    {
        if (el.Length == 3)
            return [X[el[0]], Y[el[0]], X[el[1]], Y[el[1]], X[el[2]], Y[el[2]]];

        var coords = new double[12];
        for (int i = 0; i < 6; i++)
        {
            coords[2 * i] = X[el[i]];
            coords[2 * i + 1] = Y[el[i]];
        }
        return coords;
    }
}
