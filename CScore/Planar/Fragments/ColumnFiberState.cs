namespace CScore.Planar.Fragments
{
    /// <summary>Состояние одного фибрового волокна балочного сегмента колонны (бетон или
    /// арматура/сталь).</summary>
    public class ColumnFiberState
    {
        public string SegmentId { get; set; } = string.Empty;
        public int ElementTag { get; set; }
        public int FiberIndex { get; set; }
        /// <summary>"Concrete", "ReSteelF", "ReSteelU" или "Steel" (SourceType материала фибры).</summary>
        public string Kind { get; set; } = string.Empty;
        public double Stress { get; set; }
        public double Strain { get; set; }
    }
}
