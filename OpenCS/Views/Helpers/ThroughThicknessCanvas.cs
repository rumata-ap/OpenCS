using System;
using System.Collections.Generic;
using System.Globalization;
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
        /// <summary>Точки арматуры: (z, value, цвет).</summary>
        public (double Z, double Value, Brush Color)[] Points { get; init; } = [];
        /// <summary>Горизонтальные опорные линии (ц.т., нейтральная ось).</summary>
        public (double Z, Brush Color, string Label)[] HLines { get; init; } = [];
        public string Title { get; init; } = "";
        public string ValueAxisLabel { get; init; } = "";
        /// <summary>
        /// Если true, Points отрисовываются в своём масштабе (вторичная ось),
        /// центрированном в точке X(0) первичной шкалы.
        /// </summary>
        public bool PointsUseSecondaryScale { get; init; } = false;
    }

    /// <summary>WPF-контрол: эпюра значение↔z (1D-профиль по толщине пластины).</summary>
    public sealed class ThroughThicknessCanvas : Canvas
    {
        // Фиксированные цвета заливки: «+» — тёплый, «−» — холодный
        private static readonly SolidColorBrush s_fillPos;
        private static readonly SolidColorBrush s_fillNeg;

        static ThroughThicknessCanvas()
        {
            s_fillPos = new SolidColorBrush(Color.FromArgb(55, 235, 100, 90));
            s_fillPos.Freeze();
            s_fillNeg = new SolidColorBrush(Color.FromArgb(55, 85, 130, 225));
            s_fillNeg.Freeze();
        }

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

            bool hasSecondary = p.PointsUseSecondaryScale && p.Points.Length > 0;
            const double ml = 50, mr = 12, mt = 22;
            double mb = hasSecondary ? 46 : 38;
            double plotW = w - ml - mr, plotH = h - mt - mb;
            if (plotW < 10 || plotH < 10) return;

            double zMin = p.Z.Min(), zMax = p.Z.Max();

            // ── Первичная шкала (бетон / серии) ─────────────────────────────
            double vMin = 0, vMax = 0;
            foreach (var s in p.Series)
            {
                if (s.Values.Length == 0) continue;
                vMin = Math.Min(vMin, s.Values.Min());
                vMax = Math.Max(vMax, s.Values.Max());
            }
            if (!hasSecondary)
                foreach (var pt in p.Points)
                { vMin = Math.Min(vMin, pt.Value); vMax = Math.Max(vMax, pt.Value); }
            if (Math.Abs(vMax - vMin) < 1e-12) { vMax = 1; vMin = -1; }
            double vPad = 0.05 * (vMax - vMin);
            vMin -= vPad; vMax += vPad;

            double X(double v) => ml + (v - vMin) / (vMax - vMin) * plotW;
            double Y(double z) => mt + (zMax - z) / (zMax - zMin) * plotH;
            double x0 = X(0);

            // ── Вторичная шкала (арматура): ноль совпадает с X(0) первичной ─
            double vMinP = 0, vMaxP = 0, rebarHalfSpan = 1;
            Func<double, double> Xp = X;
            if (hasSecondary)
            {
                double maxAbs = p.Points.Max(pt => Math.Abs(pt.Value));
                rebarHalfSpan = maxAbs < 1e-12 ? 1 : maxAbs * 1.1;
                vMinP = -rebarHalfSpan; vMaxP = rebarHalfSpan;
                double scale = plotW * 0.5 / rebarHalfSpan;
                Xp = v => x0 + v * scale;
            }

            // ── 1. Заливка полигонов ──────────────────────────────────────────
            void DrawFill(double[] zArr, double[] vArr)
            {
                int n = zArr.Length;
                if (n < 2 || vArr.Length != n) return;

                // Вставляем точки пересечения нуля
                var zs = new List<double>(n + 8); var vs = new List<double>(n + 8);
                zs.Add(zArr[0]); vs.Add(vArr[0]);
                for (int i = 1; i < n; i++)
                {
                    double z0 = zArr[i - 1], z1 = zArr[i], v0 = vArr[i - 1], v1 = vArr[i];
                    if (v0 != 0 && v1 != 0 && ((v0 > 0) != (v1 > 0)))
                    {
                        double t = v0 / (v0 - v1);
                        zs.Add(z0 + t * (z1 - z0)); vs.Add(0.0);
                    }
                    zs.Add(z1); vs.Add(v1);
                }

                int total = zs.Count, k = 0;
                while (k < total - 1)
                {
                    // Пропускаем нулевые граничные точки
                    while (k < total - 1 && vs[k] == 0.0) k++;
                    if (k >= total - 1) break;

                    bool pos = vs[k] > 0;
                    int end = k + 1;
                    while (end < total && vs[end] != 0.0 && (vs[end] > 0) == pos) end++;

                    bool endIsZero = end < total && vs[end] == 0.0;
                    int last = endIsZero ? end : end - 1;

                    var poly = new Polygon { Fill = pos ? s_fillPos : s_fillNeg, Stroke = null };
                    poly.Points.Add(new Point(x0, Y(zs[k])));
                    for (int j = k; j <= last; j++)
                        poly.Points.Add(new Point(X(vs[j]), Y(zs[j])));
                    poly.Points.Add(new Point(x0, Y(zs[last])));
                    Children.Add(poly);

                    k = endIsZero ? end : end;
                }
            }

            foreach (var s in p.Series)
                if (s.Values.Length == p.Z.Length)
                    DrawFill(p.Z, s.Values);

            // ── 2. Рамка ─────────────────────────────────────────────────────
            var frame = new Rectangle
            {
                Width = plotW, Height = plotH,
                Stroke = Brushes.Gray, StrokeThickness = 1,
            };
            SetLeft(frame, ml); SetTop(frame, mt);
            Children.Add(frame);

            // ── 3. Ось нулей (v = 0) ─────────────────────────────────────────
            if (vMin < 0 && vMax > 0)
                AddLine(x0, mt, x0, mt + plotH, Brushes.DarkGray, 1, 3);

            // ── 4. Заголовок и подписи осей ──────────────────────────────────
            AddText(p.Title, ml, 2, Brushes.Black, 12, true);
            // Z-ось
            AddText("z", 2, mt - 2, Brushes.Gray, 9, false);
            AddText(Fv(zMax), 2, mt,              Brushes.DimGray, 8, false);
            AddText(Fv(zMin), 2, mt + plotH - 10, Brushes.DimGray, 8, false);
            // Первичная шкала (нижний ряд)
            AddText(Fv(vMin), ml,             mt + plotH + 2, Brushes.DimGray, 8, false);
            AddText(Fv(vMax), ml + plotW - 26, mt + plotH + 2, Brushes.DimGray, 8, false);
            if (vMin < 0 && vMax > 0)
                AddText("0", x0 - 4, mt + plotH + 2, Brushes.DimGray, 8, false);
            // Единица первичной шкалы (второй ряд под значениями)
            AddText(p.ValueAxisLabel, ml + plotW / 2 - 8, mt + plotH + 14, Brushes.Black, 10, false);
            // Вторичная шкала (третий ряд, цвет арматуры)
            if (hasSecondary)
            {
                var pBrush = p.Points[0].Color;
                AddText(Fv(-rebarHalfSpan), ml,              mt + plotH + 28, pBrush, 8, false);
                AddText(Fv(+rebarHalfSpan), ml + plotW - 26, mt + plotH + 28, pBrush, 8, false);
                AddText("0", x0 - 4,                         mt + plotH + 28, pBrush, 8, false);
            }

            // ── 5. Горизонтальные опорные линии ──────────────────────────────
            foreach (var hl in p.HLines)
            {
                double yh = Y(hl.Z);
                AddLine(ml, yh, ml + plotW, yh, hl.Color, 0.9, 4);
                var lbl = new TextBlock { Text = hl.Label, Foreground = hl.Color, FontSize = 9 };
                SetLeft(lbl, ml + 2); SetTop(lbl, yh - 11);
                Children.Add(lbl);
            }

            // ── 6. Серии (линии) ─────────────────────────────────────────────
            foreach (var s in p.Series)
            {
                if (s.Values.Length != p.Z.Length) continue;
                var poly = new Polyline { Stroke = s.Color, StrokeThickness = 1.6 };
                for (int i = 0; i < p.Z.Length; i++)
                    poly.Points.Add(new Point(X(s.Values[i]), Y(p.Z[i])));
                Children.Add(poly);
            }

            // ── 7. Легенда (при нескольких сериях) ───────────────────────────
            if (p.Series.Length > 1)
            {
                double lx = ml + plotW - 48, ly = mt + 4;
                foreach (var s in p.Series)
                {
                    AddLine(lx, ly + 5, lx + 14, ly + 5, s.Color, 2, 0);
                    AddText(s.Name, lx + 16, ly, s.Color, 9, false);
                    ly += 13;
                }
            }

            // ── 8. Точки арматуры ─────────────────────────────────────────────
            foreach (var pt in p.Points)
            {
                double xPt = Xp(pt.Value), yPt = Y(pt.Z);
                AddLine(Xp(0), yPt, xPt, yPt, pt.Color, 1.5, 0);
                var dot = new Ellipse { Width = 7, Height = 7, Fill = pt.Color };
                SetLeft(dot, xPt - 3.5); SetTop(dot, yPt - 3.5);
                Children.Add(dot);
            }
        }

        static string Fv(double v)
        {
            if (v == 0) return "0";
            return v.ToString("G3", CultureInfo.InvariantCulture);
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
