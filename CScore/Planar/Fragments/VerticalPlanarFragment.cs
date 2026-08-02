using System;
using System.Collections.Generic;
using CScore;

namespace CScore.Planar.Fragments
{
    /// <summary>
    /// Доменный агрегат вертикального фрагмента стены для нелинейного расчёта.
    /// </summary>
    public class VerticalPlanarFragment
    {
        public int FragmentId { get; set; }
        public string Name { get; set; } = string.Empty;
        public PlanarRegion Region { get; set; } = new PlanarRegion();
        public PlateSection Section { get; set; } = new PlateSection();
        public List<PlanarLoad> Loads { get; set; } = new List<PlanarLoad>();
        public PlanarCutInterface? BottomCut { get; set; }
        public PlanarCutInterface? TopCut { get; set; }
        public List<PlanarCutInterface> SideCuts { get; set; } = new List<PlanarCutInterface>();
        public FragmentStageConfig StageConfig { get; set; } = FragmentStageConfig.CreateDefault1Stage();

        /// <summary>Template-набор boundary actions на 100% величины (до FragmentStage
        /// .CutInterfaceScale), ключ — PlanarCutInterface.Id. См. PlanarBoundaryActionSetScaling.</summary>
        public Dictionary<string, PlanarBoundaryActionSet> BoundaryTemplates { get; set; } =
            new Dictionary<string, PlanarBoundaryActionSet>();

        /// <summary>
        /// Возвращает хэш-отпечаток актуальности параметров агрегата.
        /// </summary>
        public string GetFingerprint()
        {
            return $"Fragment_{FragmentId}_{Region?.Id}_{StageConfig?.Stages.Count ?? 0}";
        }
    }
}
