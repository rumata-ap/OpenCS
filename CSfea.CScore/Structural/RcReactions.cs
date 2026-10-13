using CSfea.Core;

namespace CSfea.CScoreBridge.Structural;

/// <summary>
/// Опорные реакции шага по узлам: на узел — сумма всех опорных вкладов (силы и моменты, действующие на схему со стороны
/// опор, глобальные оси, Н и Н·м) — связей, пружин и упругого основания C1, — и маска типов опор узла. Узлы с одними
/// автоматическими закреплениями (<see cref="RcStructuralMeshBuild.Report"/>) входят, только если их реакция заметна:
/// по такому DOF жёсткости нет, и реакция там — признак нагрузки на «пустой» DOF. Суммы сил по типам опор —
/// <see cref="Sums"/>, всегда по всем вкладам.
/// </summary>
public sealed class RcReactionSet
{
    /// <summary>Типы опор узла (флаги).</summary>
    public const byte Support = 1, Spring = 2, Foundation = 4, Auto = 8;

    /// <summary>Число типов опор в <see cref="Sums"/>: связи, пружины, C1, автозакрепления.</summary>
    public const int KindCount = 4;

    public required int[] NodeIds { get; init; }

    /// <summary>6 компонент на узел: Rx, Ry, Rz (Н), Rmx, Rmy, Rmz (Н·м).</summary>
    public required double[] Values { get; init; }

    public required byte[] Kinds { get; init; }

    /// <summary>ΣRx, ΣRy, ΣRz (Н) по типам: связи, пружины, C1, автозакрепления — подряд по 3.</summary>
    public required double[] Sums { get; init; }
}

/// <summary>Сборка <see cref="RcReactionSet"/> по перемещениям шага и реакциям закреплённых DOF.</summary>
public static class RcReactions
{
    /// <summary>Доля наибольшей реакции, ниже которой реакция автозакрепления не выводится по узлу.</summary>
    public const double AutoThreshold = 1e-9;

    /// <summary>
    /// <paramref name="fixedReactions"/> — реакции закреплённых DOF (<see cref="StructuralMesh.ComputeReactions"/> или
    /// <see cref="SecantStepResult.Reactions"/>, длина — NDof); null — связей нет в расчёте (только пружины).
    /// </summary>
    public static RcReactionSet Compute(RcStructuralMeshBuild build, double[] u, double[]? fixedReactions)
    {
        int nn = build.Mesh.NNodes;
        var r = new double[6 * nn];
        var kinds = new byte[nn];
        var sums = new double[RcReactionSet.KindCount * 3];
        var autoNode = new bool[nn];
        void Add(int dof, double v, int kind)
        {
            r[dof] += v;
            if (dof % 6 < 3) sums[3 * kind + dof % 6] += v;
        }
        if (fixedReactions != null)
            foreach (int d in build.Bc.FixedDofs)
            {
                bool support = build.SupportDofs.Contains(d);
                Add(d, fixedReactions[d], support ? 0 : 3);
                if (support) kinds[d / 6] |= RcReactionSet.Support;
                else autoNode[d / 6] = true;
            }
        // Пружина и C1 действуют на схему силой −K·u.
        foreach (var (n, c, k) in build.NodeSprings)
        {
            Add(6 * n + c, -k * u[6 * n + c], 1);
            kinds[n] |= RcReactionSet.Spring;
        }
        foreach (var (n, m) in build.FoundationSprings)
        {
            for (int a = 0; a < 6; a++)
            {
                double v = 0;
                for (int b = 0; b < 6; b++) v -= m[a, b] * u[6 * n + b];
                if (v != 0) Add(6 * n + a, v, 2);
            }
            kinds[n] |= RcReactionSet.Foundation;
        }

        // Автозакрепления — по узлу, только заметные (силы и моменты — каждые от своего наибольшего).
        double maxF = 0, maxM = 0;
        for (int i = 0; i < r.Length; i++)
            if (i % 6 < 3) maxF = Math.Max(maxF, Math.Abs(r[i])); else maxM = Math.Max(maxM, Math.Abs(r[i]));
        for (int n = 0; n < nn; n++)
        {
            if (!autoNode[n]) continue;
            for (int c = 0; c < 6; c++)
                if (Math.Abs(r[6 * n + c]) > AutoThreshold * (c < 3 ? maxF : maxM))
                {
                    kinds[n] |= RcReactionSet.Auto;
                    break;
                }
        }

        var nodes = Enumerable.Range(0, nn).Where(n => kinds[n] != 0).ToArray();
        var values = new double[6 * nodes.Length];
        for (int i = 0; i < nodes.Length; i++) Array.Copy(r, 6 * nodes[i], values, 6 * i, 6);
        return new RcReactionSet
        {
            NodeIds = nodes.Select(n => build.NodeIds[n]).ToArray(), Values = values,
            Kinds = nodes.Select(n => kinds[n]).ToArray(), Sums = sums,
        };
    }

    /// <summary>ΣFx, ΣFy, ΣFz (Н) узлового вектора нагрузки.</summary>
    public static double[] LoadSum(double[] f)
    {
        var s = new double[3];
        for (int i = 0; i < f.Length; i++)
            if (i % 6 < 3) s[i % 6] += f[i];
        return s;
    }
}
