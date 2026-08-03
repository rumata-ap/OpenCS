using CScore.Planar;

namespace CScore.Planar.Fragments
{
    /// <summary>Лёгкая обёртка внешней границы фрагмента junction: регион + cut interface.
    /// Не создаёт solver-связь между плитой и стеной — только указывает, на каком snapshot
    /// лежит boundary mapping и какие действия к нему применяются.</summary>
    public class FloorJunctionBoundary
    {
        /// <summary>Уникальный ID boundary; является ключом FloorJunctionFragment.BoundaryTemplates.</summary>
        public string Id { get; set; } = string.Empty;
        /// <summary>RegionId стороны (PlateRegion.Id или WallRegion.Id), на snapshot которой лежит cut.</summary>
        public int RegionId { get; set; }
        /// <summary>Внешний cut interface (не junction).</summary>
        public PlanarCutInterface Cut { get; set; } = new PlanarCutInterface();
    }
}
