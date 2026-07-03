using CSfea.Sparse;

namespace CSfea.Core;

/// <summary>
/// Результаты постпроцессинга рамы: деформированная схема и эпюры внутренних
/// усилий по длине каждого элемента. Порт <c>postprocess.py: FrameResults</c>.
/// </summary>
public sealed class FrameResults
{
    public required double[][] NodesRef { get; init; }
    public required double[][] NodesDef { get; init; }
    public required (int I, int J)[] Elements { get; init; }
    public required double[][,] ElemFrames { get; init; }

    public required double[,] XLocal { get; init; }
    public required double[,] N { get; init; }
    public required double[,] Qy { get; init; }
    public required double[,] Mz { get; init; }
    public double[,]? Qz { get; init; }
    public double[,]? My { get; init; }

    public required double[] Ux { get; init; }
    public required double[] Uy { get; init; }
    public double[]? Uz { get; init; }
    public bool Is3d { get; init; }

    /// <summary>Сериализация в plain-словарь (для интеропа/хранения).</summary>
    public Dictionary<string, object?> ToDict() => new()
    {
        ["nodes_ref"] = NodesRef,
        ["nodes_def"] = NodesDef,
        ["elements"] = Elements,
        ["elem_frames"] = ElemFrames,
        ["x_local"] = XLocal,
        ["N"] = N,
        ["Qy"] = Qy,
        ["Mz"] = Mz,
        ["Qz"] = Qz,
        ["My"] = My,
        ["ux"] = Ux,
        ["uy"] = Uy,
        ["uz"] = Uz,
        ["is_3d"] = Is3d,
    };

    /// <summary>Десериализация из plain-словаря.</summary>
    public static FrameResults FromDict(Dictionary<string, object?> d) => new()
    {
        NodesRef = (double[][])d["nodes_ref"]!,
        NodesDef = (double[][])d["nodes_def"]!,
        Elements = ((int I, int J)[])d["elements"]!,
        ElemFrames = (double[][,])d["elem_frames"]!,
        XLocal = (double[,])d["x_local"]!,
        N = (double[,])d["N"]!,
        Qy = (double[,])d["Qy"]!,
        Mz = (double[,])d["Mz"]!,
        Qz = (double[,]?)d["Qz"],
        My = (double[,]?)d["My"],
        Ux = (double[])d["ux"]!,
        Uy = (double[])d["uy"]!,
        Uz = (double[]?)d["uz"],
        Is3d = (bool)d["is_3d"]!,
    };
}

/// <summary>Постпроцессор рам. Порт <c>postprocess.py: compute_frame_results</c>.</summary>
public static class Postprocess
{
    /// <summary>Внутренние усилия и деформированная схема рамы.</summary>
    public static FrameResults ComputeFrameResults(FrameMeshBase mesh, double[] u,
                                                   int nPoints = 10, double scaleDef = 1.0)
    {
        bool is3d = mesh is FrameMesh3D;
        int dpn = mesh.DofsPerNode;
        int nElem = mesh.Elements.Length;
        int dim = is3d ? 3 : 2;

        int nNodes = mesh.NNodes;
        var ux = new double[nNodes];
        var uy = new double[nNodes];
        var uz = is3d ? new double[nNodes] : null;
        for (int i = 0; i < nNodes; i++)
        {
            ux[i] = u[dpn * i + 0];
            uy[i] = u[dpn * i + 1];
            if (is3d) uz![i] = u[dpn * i + 2];
        }

        var nodesDef = new double[nNodes][];
        for (int i = 0; i < nNodes; i++)
        {
            if (is3d)
                nodesDef[i] = new[]
                {
                    mesh.Nodes[i][0] + scaleDef * ux[i],
                    mesh.Nodes[i][1] + scaleDef * uy[i],
                    mesh.Nodes[i][2] + scaleDef * uz![i],
                };
            else
                nodesDef[i] = new[]
                {
                    mesh.Nodes[i][0] + scaleDef * ux[i],
                    mesh.Nodes[i][1] + scaleDef * uy[i],
                };
        }

        var xLocal = new double[nElem, nPoints];
        var nArr = new double[nElem, nPoints];
        var qyArr = new double[nElem, nPoints];
        var mzArr = new double[nElem, nPoints];
        var qzArr = is3d ? new double[nElem, nPoints] : null;
        var myArr = is3d ? new double[nElem, nPoints] : null;
        var elemFrames = new double[nElem][,];

        var t = new double[nPoints];
        for (int p = 0; p < nPoints; p++) t[p] = nPoints == 1 ? 0.0 : (double)p / (nPoints - 1);

        for (int e = 0; e < nElem; e++)
        {
            var elem = mesh.Elements[e];
            var coords = new[] { mesh.Nodes[elem.I], mesh.Nodes[elem.J] };
            var section = mesh.Section(e);
            var dofs = mesh.ElementDofs(elem);
            var uElem = new double[dofs.Length];
            for (int i = 0; i < dofs.Length; i++) uElem[i] = u[dofs[i]];

            if (is3d)
            {
                var refVec = ((FrameMesh3D)mesh).RefVec(e);
                var (e0, l0) = BeamElements.Beam3dFrame(coords, refVec);
                var fg = BeamCorotational.Beam3dInternalForce(coords, section, uElem, refVec);

                var mLeft = Dense.MatVec(e0, new[] { fg[3], fg[4], fg[5] });
                var mRight = Dense.ScaleV(Dense.MatVec(e0, new[] { fg[9], fg[10], fg[11] }), -1.0);
                var fLoc2 = Dense.MatVec(e0, new[] { fg[6], fg[7], fg[8] });
                double nVal = fLoc2[0];
                double mz1 = mLeft[2], mz2 = mRight[2];
                double my1 = mLeft[1], my2 = mRight[1];
                double qy = (mz2 - mz1) / l0;
                double qz = -(my2 - my1) / l0;

                elemFrames[e] = e0;
                for (int p = 0; p < nPoints; p++)
                {
                    xLocal[e, p] = l0 * t[p];
                    nArr[e, p] = nVal;
                    qyArr[e, p] = qy;
                    qzArr![e, p] = qz;
                    mzArr[e, p] = mz1 + (mz2 - mz1) * t[p];
                    myArr![e, p] = my1 + (my2 - my1) * t[p];
                }
            }
            else
            {
                double le = Dense.Norm(Dense.SubV(coords[1], coords[0]));
                double beta = Math.Atan2(coords[1][1] - coords[0][1], coords[1][0] - coords[0][0]);
                double c = Math.Cos(beta), s = Math.Sin(beta);
                var (ea, _, eIz, _) = BeamElements.SectionStiffness(section);
                elemFrames[e] = new[,] { { c, s }, { -s, c } };

                double duAxial = c * (uElem[3] - uElem[0]) + s * (uElem[4] - uElem[1]);
                double nVal = ea * duAxial / le;

                double vi = -s * uElem[0] + c * uElem[1];
                double thi = uElem[2];
                double vj = -s * uElem[3] + c * uElem[4];
                double thj = uElem[5];

                double d3 = 12.0 * vi + 6.0 * le * thi - 12.0 * vj + 6.0 * le * thj;
                double qy = -eIz * d3 / (le * le * le);

                for (int p = 0; p < nPoints; p++)
                {
                    double tp = t[p];
                    double d2 = vi * (-6.0 + 12.0 * tp)
                              + thi * le * (-4.0 + 6.0 * tp)
                              + vj * (6.0 - 12.0 * tp)
                              + thj * le * (-2.0 + 6.0 * tp);
                    mzArr[e, p] = eIz * d2 / (le * le);
                    qyArr[e, p] = qy;
                    nArr[e, p] = nVal;
                    xLocal[e, p] = tp * le;
                }
            }
        }

        var nodesRef = new double[nNodes][];
        for (int i = 0; i < nNodes; i++) nodesRef[i] = (double[])mesh.Nodes[i].Clone();

        return new FrameResults
        {
            NodesRef = nodesRef,
            NodesDef = nodesDef,
            Elements = (( int I, int J)[])mesh.Elements.Clone(),
            ElemFrames = elemFrames,
            XLocal = xLocal,
            N = nArr,
            Qy = qyArr,
            Mz = mzArr,
            Qz = qzArr,
            My = myArr,
            Ux = ux,
            Uy = uy,
            Uz = uz,
            Is3d = is3d,
        };
    }
}
