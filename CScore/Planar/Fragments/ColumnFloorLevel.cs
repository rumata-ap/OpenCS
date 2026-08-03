namespace CScore.Planar.Fragments
{
    /// <summary>Один уровень (перекрытие) многоэтажной колонны: горизонтальная
    /// PlanarRegion + точка примыкания оси колонны (embedded_point).</summary>
    public class ColumnFloorLevel
    {
        /// <summary>Уникальный ID уровня внутри фрагмента.</summary>
        public string Id { get; set; } = string.Empty;
        /// <summary>Имя уровня (для диагностики/UI).</summary>
        public string Name { get; set; } = string.Empty;
        /// <summary>Горизонтальная плита (источник геометрии, не мутируется mesh workflow).</summary>
        public PlanarRegion PlateRegion { get; set; } = new PlanarRegion();
        /// <summary>Секция плиты этого уровня.</summary>
        public PlateSection PlateSection { get; set; } = new PlateSection();
        /// <summary>Точка оси колонны в локальных координатах PlateRegion.Frame —
        /// становится embedded_point constraint при построении сетки уровня.</summary>
        public (double U, double V) ColumnAnchorLocalXY { get; set; }
        /// <summary>Нагрузки уровня (поверхностные/краевые/точечные — включая осевую
        /// нагрузку на колонну через PlanarLoad.Point в координатах anchor-точки).</summary>
        public List<PlanarLoad> Loads { get; set; } = new List<PlanarLoad>();
        /// <summary>Внешние границы уровня (опирание вне фрагмента и т. п.), может быть
        /// пустым.</summary>
        public List<FloorJunctionBoundary> Boundaries { get; set; } = new List<FloorJunctionBoundary>();
    }
}
