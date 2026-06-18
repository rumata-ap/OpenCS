using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace OpenCS.Views.Helpers
{
    /// <summary>Серия/точки/оси для одной эпюры по толщине.</summary>
    public sealed class ThroughThicknessProfile
    {
        public double[] Z { get; init; } = [];
        public (string Name, double[] Values, Brush Color)[] Series { get; init; } = [];
        public (double Z, double Value, Brush Color)[] Points { get; init; } = [];
        public string Title { get; init; } = "";
        public string ValueAxisLabel { get; init; } = "";
    }

    /// <summary>WPF-контрол: эпюра значение↔z (1D-профиль по толщине пластины).</summary>
    public sealed class ThroughThicknessCanvas : Canvas
    {
        public static readonly DependencyProperty ProfileProperty =
            DependencyProperty.Register(nameof(Profile), typeof(ThroughThicknessProfile),
                typeof(ThroughThicknessCanvas),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender,
                    (d, _) => ((ThroughThicknessCanvas)d).Redraw()));

        public ThroughThicknessProfile? Profile
        {
            get => (ThroughThicknessProfile?)GetValue(ProfileProperty);
            set => SetValue(ProfileProperty, value);
        }

        public ThroughThicknessCanvas()
        {
            Background = Brushes.White;
            SizeChanged += (_, _) => Redraw();
        }

        void Redraw()
        {
            Children.Clear();
            var p = Profile;
            if (p == null || p.Z.Length < 2) return;
            double w = ActualWidth, h = ActualHeight;
            if (w < 20 || h < 20) return;

            const double ml = 48, mr = 12, mt = 22, mb = 28;
            double plotW = w - ml - mr, plotH = h - mt - mb;
            if (plotW < 10 || plotH < 10) return;

            double zMin = p.Z.Min(), zMax = p.Z.Max();
            double vMin = 0, vMax = 0;
            foreach (var s in p.Series)
            {
                if (s.Values.Length == 0) continue;
                vMin = Math.Min(vMin, s.Values.Min());
                vMax = Math.Max(vMax, s.Values.Max());
            }
            foreach (var pt in p.Points) { vMin = Math.Min(vMin, pt.Value); vMax = Math.Max(vMax, pt.Value); }
            if (Math.Abs(vMax - vMin) < 1e-12) { vMax = 1; vMin = -1; }
            double vPad = 0.05 * (vMax - vMin);
            vMin -= vPad; vMax += vPad;

            double X(double v) => ml + (v - vMin) / (vMax - vMin) * plotW;
            double Y(double z) => mt + (zMax - z) / (zMax - zMin) * plotH;

            // Рамка
            var frame = new Rectangle
            {
                Width = plotW, Height = plotH,
                Stroke = Brushes.Gray, StrokeThickness = 1,
            };
            SetLeft(frame, ml); SetTop(frame, mt);
            Children.Add(frame);

            // Ось значений v=0
            if (vMin < 0 && vMax > 0)
                AddLine(X(0), mt, X(0), mt + plotH, Brushes.DarkGray, 1, 2);

            // Заголовок и подписи осей
            AddText(p.Title, ml, 2, Brushes.Black, 12, true);
            AddText(p.ValueAxisLabel, ml + plotW - 60, mt + plotH + 8, Brushes.Black, 11, false);
            AddText("z, м", 4, mt - 2, Brushes.Black, 11, false);

            // Серии (линии value(z))
            foreach (var s in p.Series)
            {
                if (s.Values.Length != p.Z.Length) continue;
                var poly = new Polyline { Stroke = s.Color, StrokeThickness = 1.6 };
                for (int i = 0; i < p.Z.Length; i++)
                    poly.Points.Add(new Point(X(s.Values[i]), Y(p.Z[i])));
                Children.Add(poly);
            }
            // Точки арматуры
            foreach (var pt in p.Points)
            {
                var dot = new Ellipse { Width = 7, Height = 7, Fill = pt.Color };
                SetLeft(dot, X(pt.Value) - 3.5); SetTop(dot, Y(pt.Z) - 3.5);
                Children.Add(dot);
            }
        }

        void AddLine(double x1, double y1, double x2, double y2, Brush b, double th, double dash)
        {
            var l = new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = b, StrokeThickness = th };
            if (dash > 0) l.StrokeDashArray = new DoubleCollection { dash, dash };
            Children.Add(l);
        }

        void AddText(string s, double x, double y, Brush b, double size, bool bold)
        {
            var t = new TextBlock { Text = s, Foreground = b, FontSize = size };
            if (bold) t.FontWeight = FontWeights.Bold;
            SetLeft(t, x); SetTop(t, y);
            Children.Add(t);
        }
    }
}
