using System;
using System.Collections.Generic;
using CScore.PlateRebar;

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
        public PlateRebarField? RebarField { get; set; }
        public List<PlanarLoad> Loads { get; set; } = new List<PlanarLoad>();
        public PlanarCutInterface? BottomCut { get; set; }
        public PlanarCutInterface? TopCut { get; set; }
        public List<PlanarCutInterface> SideCuts { get; set; } = new List<PlanarCutInterface>();
        public FragmentStageConfig StageConfig { get; set; } = FragmentStageConfig.CreateDefault1Stage();

        /// <summary>
        /// Возвращает хэш-отпечаток актуальности параметров агрегата.
        /// </summary>
        public string GetFingerprint()
        {
            return $"Fragment_{FragmentId}_{Region?.Id}_{StageConfig?.Stages.Count ?? 0}";
        }
    }
}
