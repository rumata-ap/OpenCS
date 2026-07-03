using CSfea.Sparse;

namespace CSfea.Core;

/// <summary>
/// Общий контракт сетки для граничных условий и постпроцессинга
/// (ShellMesh: 6 DOF/узел; FrameMesh2D: 3; FrameMesh3D: 6).
/// </summary>
public interface IFeaMesh
{
    /// <summary>DOF на узел.</summary>
    int DofsPerNode { get; }

    /// <summary>Число узлов.</summary>
    int NNodes { get; }

    /// <summary>Полное число DOF.</summary>
    int NDof { get; }

    /// <summary>Линейная глобальная K (COO) — для вычисления реакций.</summary>
    CooMatrix AssembleK();

    /// <summary>Зафиксировать состояние сечений после шага нагружения.</summary>
    void CommitStep(double[] u);
}
