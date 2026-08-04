namespace CScore.Planar.Fragments
{
    /// <summary>Один нелинейный фибровый балочный сегмент многоэтажной колонны между
    /// двумя соседними ColumnFloorLevel (Segments[i] соединяет Levels[i] и Levels[i+1]
    /// позиционно — явных ссылок на уровни нет).</summary>
    public class ColumnSegment
    {
        /// <summary>Уникальный ID сегмента внутри фрагмента.</summary>
        public string Id { get; set; } = string.Empty;
        /// <summary>Фибровое сечение сегмента (может отличаться по этажам); фибры и
        /// арматура подготавливаются так же, как для CrossSectionToOpenSeesAdapter.</summary>
        public CScore.CrossSection Section { get; set; } = new CScore.CrossSection();
        /// <summary>Крутильная жёсткость сегмента, Н·м². Обязательное явное значение:
        /// дефолт CrossSectionToOpenSeesAdapter.Options.GJ=0 физически неверен для
        /// пространственной (6-DOF) модели колонны.</summary>
        public double GJ { get; set; }
        /// <summary>Ориентация локальной оси сечения (geomTransf vecxz).</summary>
        public (double X, double Y, double Z) Vecxz { get; set; } = (1, 0, 0);
        /// <summary>Число точек интегрирования forceBeamColumn/dispBeamColumn.</summary>
        public int NumIntegrationPoints { get; set; } = 5;
    }
}
