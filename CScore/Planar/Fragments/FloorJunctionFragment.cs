using System.Collections.Generic;
using CScore;

namespace CScore.Planar.Fragments
{
    /// <summary>
    /// Доменный агрегат пары «горизонтальная плита + вертикальная стена» с одним явным
    /// пространственным junction. Оба региона заранее выбраны пользователем; автоматического
    /// поиска примыкающих элементов нет.
    /// </summary>
    public class FloorJunctionFragment
    {
        /// <summary>Идентификатор фрагмента junction.</summary>
        public int FragmentId { get; set; }
        /// <summary>Имя фрагмента junction.</summary>
        public string Name { get; set; } = string.Empty;
        /// <summary>Горизонтальная плита (источник геометрии, не мутируется mesh workflow).</summary>
        public PlanarRegion PlateRegion { get; set; } = new PlanarRegion();
        /// <summary>Вертикальная стена.</summary>
        public PlanarRegion WallRegion { get; set; } = new PlanarRegion();
        /// <summary>Секция плиты.</summary>
        public PlateSection PlateSection { get; set; } = new PlateSection();
        /// <summary>Секция стены.</summary>
        public PlateSection WallSection { get; set; } = new PlateSection();
        /// <summary>Явный пространственный junction; MeshMode обязан быть ConformingPartition.</summary>
        public PlanarConnection Connection { get; set; } = new PlanarConnection();
        /// <summary>Конфигурация стадий нелинейного нагружения фрагмента.</summary>
        public FragmentStageConfig StageConfig { get; set; } = FragmentStageConfig.CreateDefault1Stage();
        /// <summary>Внешние boundary interfaces (не junction), каждый с уникальным Id.</summary>
        public List<FloorJunctionBoundary> Boundaries { get; set; } = new List<FloorJunctionBoundary>();
        /// <summary>Template-наборы boundary actions на 100% величины, ключ — FloorJunctionBoundary.Id.</summary>
        public Dictionary<string, PlanarBoundaryActionSet> BoundaryTemplates { get; set; } =
            new Dictionary<string, PlanarBoundaryActionSet>();
    }
}
