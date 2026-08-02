using System;
using System.Collections.Generic;

namespace CScore.Planar.Fragments
{
    /// <summary>
    /// Состояние материала в слое элемента (бетон или физический слой арматуры).
    /// </summary>
    public class LayerMaterialState
    {
        public int ElementId { get; set; }
        public int LayerIndex { get; set; }
        public string LayerKind { get; set; } = string.Empty; // "ConcreteTop", "ConcreteBottom", "Rebar"
        public double Stress { get; set; }
        public double Strain { get; set; }
    }

    /// <summary>
    /// Профилированный результат нелинейного расчёта вертикального фрагмента.
    /// </summary>
    public class VerticalPlanarFragmentResult
    {
        public int FragmentId { get; set; }
        public bool IsConverged { get; set; }

        /// <summary>Блокирующие диагностики построения Gmsh-сетки (пусто = сетка расчётна).</summary>
        public List<string> MeshDiagnostics { get; set; } = new List<string>();

        /// <summary>Блокирующие диагностики резолва/маппинга граничных воздействий (пусто = ок).</summary>
        public List<string> BoundaryDiagnostics { get; set; } = new List<string>();

        /// <summary>Уровень достоверности energy audit: "NativeResponse"/"StateIntegral"/
        /// "ExternalWorkOnly"/"Unavailable" (зеркалит OpenCS.OpenSees.Audit.ShellEnergyConfidence
        /// строкой — CScore не ссылается на OpenCS.OpenSees).</summary>
        public string EnergyConfidence { get; set; } = "Unavailable";

        /// <summary>
        /// Максимальная деформация сжатия бетона (отрицательная величина, по СП 63 предельное значение eps_b >= -0.0035).
        /// </summary>
        public double MaxConcreteCompressionStrain { get; set; }
        /// <summary>
        /// Максимальная деформация растяжения арматуры (положительная величина, по СП 63 предельное значение eps_s <= 0.025).
        /// </summary>
        public double MaxRebarTensileStrain { get; set; }
        public double ForceUnbalanceRatio { get; set; }
        public List<LayerMaterialState> LayerStates { get; set; } = new List<LayerMaterialState>();
        public FragmentAuditReport AuditReport { get; set; } = new FragmentAuditReport();
        public Dictionary<int, string> ProvenanceMap { get; set; } = new Dictionary<int, string>();
    }
}
