using System.Collections.Generic;

namespace CScore.Planar.Fragments
{
    /// <summary>Показатель непрерывности перемещений/вращений одной junction-пары.</summary>
    public sealed record InterfaceContinuityItem(
        int PlateNodeTag,
        int WallNodeTag,
        double MaxDisplacementDeltaM,
        double MaxRotationDeltaRad);

    /// <summary>Силовой баланс последнего сошедшегося шага полной нагрузки.</summary>
    public sealed record FloorJunctionForceBalance(
        double AppliedLoadMagnitude,
        double ReactionMagnitude,
        double RelativeUnbalance);

    /// <summary>
    /// Нерасчётный по умолчанию результат расчёта floor junction. Формируется и при раннем
    /// выходе (blocking diagnostics), и при успешном прогоне.
    /// </summary>
    public class FloorJunctionResult
    {
        /// <summary>Идентификатор фрагмента junction, которому принадлежит результат.</summary>
        public int FragmentId { get; set; }
        /// <summary>Признак достижения полной нагрузки последним шагом нелинейного расчёта.</summary>
        public bool IsConverged { get; set; }

        /// <summary>Блокирующие диагностики двухстороннего Gmsh workflow (пусто = сетки расчётны).</summary>
        public List<string> MeshDiagnostics { get; set; } = new List<string>();
        /// <summary>Блокирующие диагностики сборки модели (домен/remap/коллизии).</summary>
        public List<string> AssemblyDiagnostics { get; set; } = new List<string>();
        /// <summary>Блокирующие диагностики boundary pipeline.</summary>
        public List<string> BoundaryDiagnostics { get; set; } = new List<string>();
        /// <summary>Блокирующие диагностики расчёта (неполная нагрузка, continuity, balance).</summary>
        public List<string> AnalysisDiagnostics { get; set; } = new List<string>();

        /// <summary>Относительная невязка boundary mapping (applied vs mapped), по модулю накопленная.</summary>
        public double BoundaryForceUnbalanceRatio { get; set; }
        /// <summary>Силовой баланс реакций vs нагрузок последнего шага.</summary>
        public FloorJunctionForceBalance? ForceBalance { get; set; }
        /// <summary>Непрерывность интерфейса по каждой junction-паре.</summary>
        public List<InterfaceContinuityItem> InterfaceContinuity { get; set; } = new List<InterfaceContinuityItem>();

        /// <summary>Каталог артефактов Gmsh (сторона plate).</summary>
        public string? GmshArtifactDirectory { get; set; }
        /// <summary>Каталог артефактов OpenSees.</summary>
        public string? OpenSeesArtifactDirectory { get; set; }

        /// <summary>Provenance: OpenSees tag → строка «сторона|источник|fingerprint» (sections/materials).</summary>
        public Dictionary<int, string> ProvenanceMap { get; set; } = new Dictionary<int, string>();
        /// <summary>Snapshot node index → OpenSees node tag по стороне plate.</summary>
        public Dictionary<int, int> PlateNodeIndexToTag { get; set; } = new Dictionary<int, int>();
        /// <summary>Snapshot node index → OpenSees node tag по стороне wall.</summary>
        public Dictionary<int, int> WallNodeIndexToTag { get; set; } = new Dictionary<int, int>();
        /// <summary>Junction-пары в assembly tags (plate master, wall slave).</summary>
        public List<(int PlateNodeTag, int WallNodeTag)> JunctionPairs { get; set; } = new List<(int, int)>();

        /// <summary>Итоговый отчёт аудита результата (по умолчанию нерасчётный Valid).</summary>
        public FloorJunctionAuditReport AuditReport { get; set; } = new FloorJunctionAuditReport();
    }
}
