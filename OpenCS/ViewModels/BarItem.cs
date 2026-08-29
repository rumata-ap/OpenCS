using OpenCS.Utilites;

namespace OpenCS.ViewModels
{
    /// <summary>Один арматурный стержень в группе.</summary>
    public class BarItem : ViewModelBase
    {
        double _x, _y, _d;
        bool _isSelected;

        public int Index { get; set; }

        /// <summary>Координата X центра стержня в метрах.</summary>
        public double X
        {
            get => _x;
            set { _x = value; OnPropertyChanged(nameof(X)); OnPropertyChanged(nameof(XMm)); }
        }

        /// <summary>Координата X центра стержня в миллиметрах для отображения в UI.</summary>
        public double XMm
        {
            get => _x * 1000;
            set => X = value / 1000;
        }

        /// <summary>Координата Y центра стержня в метрах.</summary>
        public double Y
        {
            get => _y;
            set { _y = value; OnPropertyChanged(nameof(Y)); OnPropertyChanged(nameof(YMm)); }
        }

        /// <summary>Координата Y центра стержня в миллиметрах для отображения в UI.</summary>
        public double YMm
        {
            get => _y * 1000;
            set => Y = value / 1000;
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
