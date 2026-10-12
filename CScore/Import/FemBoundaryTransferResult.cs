using CScore.Fem;

namespace CScore.Import;

/// <summary>Итог переноса ГУ программы-источника (SCAD, ЛИРА): объекты сеточного уровня происхождения «import:&lt;источник&gt;» и журнал.</summary>
/// <param name="Supports">Закрепления узлов сетки.</param>
/// <param name="Springs">Пружины на узлах сетки (одна запись на узел, КЭ 51 одного узла суммируются).</param>
/// <param name="RigidBodies">Жёсткие тела.</param>
/// <param name="ElementProps">ГУ КЭ по тегу: освобождения концов стержней и C1 пластин (только КЭ, где они есть).</param>
/// <param name="Report">Журнал: первая строка — сводка, далее — что не перенесено и почему.</param>
/// <param name="Summary">Сводка по видам ГУ для окна импорта.</param>
public sealed record FemBoundaryTransferResult(
    IReadOnlyList<FemMeshNodeSupport> Supports,
    IReadOnlyList<FemSpring> Springs,
    IReadOnlyList<FemRigidBody> RigidBodies,
    IReadOnlyDictionary<string, FemElementBoundaryProps> ElementProps,
    IReadOnlyList<string> Report,
    IReadOnlyList<FemBoundaryTransferRow> Summary);

/// <summary>Строка сводки переноса ГУ: вид, единица счёта, перенесено, не перенесено, примечание.</summary>
public sealed record FemBoundaryTransferRow(string Kind, string Unit, int Transferred, int NotTransferred, string Note = "");
