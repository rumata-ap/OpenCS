using System;
using System.Collections.ObjectModel;

namespace CScore.Fem;

/// <summary>МКЭ-расчётная схема. Контейнер для узлов, КЭ, конструктивных элементов и загружений.</summary>
public class FemSchema
{
    public int    Id         { get; set; }
    public string Tag        { get; set; } = "";
    /// <summary>Источник схемы: "lira" | "scad" | "robot" | "rfem" | "opensees" | "internal" | "submodel"</summary>
    public string SourceType { get; set; } = "internal";

    /// <summary>
    /// Файл проекта-источника (для SCAD — .SPR), из которого дозагружаются результаты; null — не задан
    /// (схема импортирована до v71 или не из файла проекта).
    /// </summary>
    public string? SourcePath { get; set; }
    public string Created    { get; set; } = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

    /// <summary>Общий шаг дискретизации стержней, м; null — стержни делятся только узлами. Локальный шаг КонЭ
    /// (<see cref="FemMember.TargetMeshLengthM"/>) важнее.</summary>
    public double? MeshBarStepM { get; set; }

    /// <summary>Общий размер КЭ пластин, м; null — размер из области (<see cref="Planar.PlanarRegion.MeshMaxElementSizeM"/>).
    /// Локальный шаг КонЭ (<see cref="FemMember.TargetMeshLengthM"/>) важнее.</summary>
    public double? MeshPlateStepM { get; set; }

    /// <summary>Создавать АЖТ в примыканиях стержней к плитам при построении сетки (по умолчанию — да).</summary>
    public bool   AutoRigidJunctions    { get; set; } = true;

    /// <summary>Порог угла оси стержня к плоскости плиты, град: не меньше — стержень входит в плиту (случай А).</summary>
    public double RigidJunctionAngleDeg { get; set; } = 10;

    /// <summary>Схема-родитель фрагмента этажа; null — схема не фрагмент. Родитель мог быть удалён (ссылка висячая).</summary>
    public int?    ParentSchemaId          { get; set; }
    /// <summary>Нижняя отметка полосы фрагмента, м.</summary>
    public double? FragmentZ1              { get; set; }
    /// <summary>Верхняя отметка полосы фрагмента, м.</summary>
    public double? FragmentZ2              { get; set; }
    /// <summary>Отпечаток снимка полосы родителя при сборке (<see cref="Fragment.StoreyFragmentFingerprint"/>).</summary>
    public string? ParentFingerprint       { get; set; }
    /// <summary>Отчёт сборки фрагмента — JSON диагностики (<see cref="Fragment.StoreyFragmentBuildReport"/>).</summary>
    public string? FragmentBuildReportJson { get; set; }
    /// <summary>Программа-источник родителя на момент сборки фрагмента (lira, scad …): по ней выбираются источники
    /// армирования копий вложений; null — схема не фрагмент или собрана до v93.</summary>
    public string? ParentSourceType        { get; set; }
    /// <summary>Пресет ГУ разрезов фрагмента (спека масок разд. 5.1); null — «Как в родителе» (Kinematic).</summary>
    public string? FragmentBcPreset        { get; set; }
    /// <summary>Пресет ГУ боковых граней призмы (спека призмы 3.2); null — «Усилия из родителя» (Static).</summary>
    public string? FragmentLateralBcPreset { get; set; }
    /// <summary>Призма фрагмента вокруг стен и колонн (<see cref="Fragment.FragmentPrismCodec"/>); null — этажный фрагмент.</summary>
    public string? FragmentPrismJson       { get; set; }
    /// <summary>Схема — фрагмент этажа другой схемы.</summary>
    public bool IsFragment => ParentSchemaId is not null;

    /// <summary>Группы конструктивных элементов схемы. Заполняются при загрузке из БД.</summary>
    public ObservableCollection<FemMemberGroup> MemberGroups { get; } = [];

    /// <summary>Загружения схемы.</summary>
    public ObservableCollection<FemLoadCase> LoadCases { get; } = [];

    /// <summary>Именованные определения одиночных загружений и сочетаний схемы.</summary>
    public ObservableCollection<FemLoadDefinition> LoadDefinitions { get; } = [];

    /// <summary>Постановки расчёта схемы.</summary>
    public ObservableCollection<FemAnalysis> Analyses { get; } = [];
}
