using OpenCS.Utilites;

namespace OpenCS.ViewModels
{
    /// <summary>Один арматурный стержень в группе.</summary>
    public class BarItem : ViewModelBase
    {
        double _x, _y, _d;
        bool _isSelected;

        public int Index { get; set; }

        public double X
        {
            get => _x;
            set { _x = value; OnPropertyChanged(); }
        }

        public double Y
        {
            get => _y;
            set { _y = value; OnPropertyChanged(); }
        }

        /// <summary>Диаметр в метрах.</summary>
        public double Diameter
        {
            get => _d;
            set { _d = value; OnPropertyChanged(); OnPropertyChanged(nameof(DiameterMm)); }
        }

        /// <summary>Диаметр в мм — для отображения в UI.</summary>
        public double DiameterMm
        {
            get => _d * 1000;
            set { Diameter = value / 1000; }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }
    }
}
