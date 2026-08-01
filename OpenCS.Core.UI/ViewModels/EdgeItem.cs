using OpenCS.Utilites;

namespace OpenCS.ViewModels
{
    /// <summary>Ребро линии защитного слоя бетона.</summary>
    public class EdgeItem : ViewModelBase
    {
        double _offset;

        public int Index { get; set; }

        /// <summary>Отступ от опорного ребра в метрах (≥ 0).</summary>
        public double Offset
        {
            get => _offset;
            set { _offset = value < 0 ? 0 : value; OnPropertyChanged(); }
        }

        // Геометрия опорного ребра (задаётся при инициализации, не меняется)
        public double StartX { get; init; }
        public double StartY { get; init; }
        public double EndX   { get; init; }
        public double EndY   { get; init; }

        /// <summary>Единичная внутренняя нормаль.</summary>
        public double NormalX { get; init; }
        public double NormalY { get; init; }

        /// <summary>Позиция ручки: середина ребра + Offset*Normal.</summary>
        public (double X, double Y) HandlePoint =>
        (
            (StartX + EndX) / 2 + Offset * NormalX,
            (StartY + EndY) / 2 + Offset * NormalY
        );
    }
}
