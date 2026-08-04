namespace CScore.Planar.Fragments
{
    /// <summary>Нерасчётный по умолчанию результат расчёта многоэтажной колонны. Формируется
    /// и при раннем выходе (blocking diagnostics), и при успешном прогоне.</summary>
    public class MultiStoryColumnResult
    {
        public int FragmentId { get; set; }
        public bool IsConverged { get; set; }

        /// <summary>Блокирующие диагностики построения Gmsh-сеток по уровням (пусто = все
        /// сетки расчётны).</summary>
        public List<string> MeshDiagnostics { get; set; } = new List<string>();
        /// <summary>Блокирующие диагностики сборки составной модели.</summary>
        public List<string> AssemblyDiagnostics { get; set; } = new List<string>();
        /// <summary>Блокирующие диагностики boundary pipeline.</summary>
        public List<string> BoundaryDiagnostics { get; set; } = new List<string>();
        /// <summary>Блокирующие диагностики расчёта (неполная нагрузка и т. п.).</summary>
        public List<string> AnalysisDiagnostics { get; set; } = new List<string>();

        /// <summary>Уровень достоверности energy audit (зеркалит OpenCS.OpenSees.Audit.ShellEnergyConfidence
        /// строкой).</summary>
        public string EnergyConfidence { get; set; } = "Unavailable";
        /// <summary>Силовой баланс всей составной модели на последнем сошедшемся шаге.</summary>
        public FloorJunctionForceBalance? ForceBalance { get; set; }

        /// <summary>Максимальная деформация сжатия бетона (отрицательная величина; СП 63.13330
        /// предел -0.0035) — экстремум по shell-слоям всех уровней и фибрам всех сегментов вместе.</summary>
        public double MaxConcreteCompressionStrain { get; set; }
        /// <summary>Максимальная деформация растяжения арматуры (положительная величина; СП 63.13330
        /// предел 0.025) — экстремум по shell-слоям всех уровней и фибрам всех сегментов вместе.</summary>
        public double MaxRebarTensileStrain { get; set; }

        /// <summary>Состояния слоёв плит всех уровней.</summary>
        public List<LayerMaterialState> LayerStates { get; set; } = new List<LayerMaterialState>();
        /// <summary>Состояния фибр всех сегментов колонны.</summary>
        public List<ColumnFiberState> FiberStates { get; set; } = new List<ColumnFiberState>();

        public string? GmshArtifactDirectory { get; set; }
        public string? OpenSeesArtifactDirectory { get; set; }

        /// <summary>Provenance: OpenSees tag → строка «источник|source:...|fp:...».</summary>
        public Dictionary<int, string> ProvenanceMap { get; set; } = new Dictionary<int, string>();
        /// <summary>Level.Id → (snapshot node index → OpenSees node tag).</summary>
        public Dictionary<string, Dictionary<int, int>> NodeIndexToTagByLevel { get; set; } =
            new Dictionary<string, Dictionary<int, int>>();
        /// <summary>Level.Id → OpenSees tag anchor-узла этого уровня.</summary>
        public Dictionary<string, int> AnchorNodeTagByLevel { get; set; } = new Dictionary<string, int>();

        /// <summary>Итоговый отчёт аудита. По умолчанию Verdict=Invalid; Valid выставляется только
        /// методом Audit для чистого и сошедшегося результата.</summary>
        public MultiStoryColumnAuditReport AuditReport { get; set; } =
            new MultiStoryColumnAuditReport { Verdict = FragmentAuditVerdict.Invalid };
    }
}
