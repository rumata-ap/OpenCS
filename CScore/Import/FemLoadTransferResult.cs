using CScore.Fem;

namespace CScore.Import;

/// <summary>Итог переноса нагрузок из программы-источника (SCAD, ЛИРА): полные списки загружений и нагрузок сеточного уровня схемы и журнал.</summary>
/// <param name="LoadCases">Все загружения схемы после переноса (существующие объекты обновлены, новые — с временными Id).</param>
/// <param name="ElementLoads">Все нагрузки на КЭ: не из источника — без изменений, из источника — новые.</param>
/// <param name="MeshNodeLoads">Все узловые нагрузки на узлы сетки, по тому же правилу.</param>
/// <param name="Report">Журнал: что перенесено, что нет и почему.</param>
public sealed record FemLoadTransferResult(
    IReadOnlyList<FemLoadCase> LoadCases,
    IReadOnlyList<FemElementLoad> ElementLoads,
    IReadOnlyList<FemMeshNodeLoad> MeshNodeLoads,
    IReadOnlyList<string> Report,
    int CasesCreated, int ImportedElementLoads, int ImportedNodeLoads, int SkippedRecords);
