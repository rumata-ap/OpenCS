using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
        public string Title          { get; init; } = "";
        public string ValueAxisLabel { get; init; } = "";
        public bool PointsUseSecondaryScale { get; init; } = false;
        /// <summary>
        /// Если true и Points.Length > 0: канвас делится на левую зону (Series, свой масштаб)
        /// и правую зону (Points, свой масштаб).
        /// </summary>
        public bool PointsInSeparateZone { get; init; } = false;
    }

    /// <summary>WPF-контрол: эпюра значение↔z (1D-профиль по толщине пластины).</summary>
    public sealed class ThroughThicknessCanvas : Canvas
    {
        // ── Глобальная настройка шрифта осей ─────────────────────────────────
        /// <summary>
        /// Размер шрифта числовых меток и подписей осей на всех эпюрах ThroughThicknessCanvas.
        /// Изменяется один раз при старте приложения; перерисовка происходит по SizeChanged.
        /// </summary>
        public static double AxisFontSize { get; set; } = 10.0;

        // ── Цвета заливки ────────────────────────────────────────────────────
        private static readonly SolidColorBrush s_fillPos;
        private static readonly SolidColorBrush s_fillNeg;
        private static readonly SolidColorBrush s_divider;

        static ThroughThicknessCanvas()
        {
            s_fillPos = new SolidColorBrush(Color.FromArgb(55, 235, 100, 90));  s_fillPos.Freeze();
            s_fillNeg = new SolidColorBrush(Color.FromArgb(55, 85, 130, 225));  s_fillNeg.Freeze();
            s_divider = new SolidColorBrush(Color.FromRgb(180, 180, 180));      s_divider.Freeze();
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

        // ── Кеш геометрии ────────────────────────────────────────────────────
        private ThroughThicknessProfile? _p;
        private double _ml, _mt, _plotH;
        private double _zMin, _zMax;
        private double _plotW, _vMin, _vMax, _x0;
        private bool   _hasSecondary;
        private double _rebarHalfSpan;
        private bool   _splitZone;
        private double _splitX, _plotWR, _vMinR, _vMaxR;
        private double _plotWFull;

        // ── Hover-overlay ─────────────────────────────────────────────────────
        private readonly Line      _hoverLine;
        private readonly TextBlock _hoverZLabel;
        private readonly TextBlock _hoverVLabel;

        public ThroughThicknessCanvas()
        {
            Background = Brushes.White;
            SizeChanged += (_, _) => Redraw();

            _hoverLine = new Line
            {
                Stroke = Brushes.DimGray, StrokeThickness = 0.8,
                StrokeDashArray = new DoubleCollection { 4, 3 },
                Visibility = Visibility.Collapsed,
            };
            _hoverZLabel = new TextBlock { FontSize = 9, Foreground = Brushes.DimGray, Visibility = Visibility.Collapsed,
                Width = 46, TextAlignment = TextAlignment.Right };
            _hoverVLabel = new TextBlock { FontSize = 9, Foreground = Brushes.DimGray, Visibility = Visibility.Collapsed };

            Children.Add(_hoverLine);
            Children.Add(_hoverZLabel);
            Children.Add(_hoverVLabel);

            MouseMove  += OnMouseMove;
            MouseLeave += OnMouseLeave;
        }

        // ═════════════════════════════════════════════════════════════════════
        //  Redraw
        // ═════════════════════════════════════════════════════════════════════

        void Redraw()
        {
            for (int i = Children.Count - 1; i >= 0; i--)
            {
                var c = Children[i];
                if (c == _hoverLine || c == _hoverZLabel || c == _hoverVLabel) continue;
                Children.RemoveAt(i);
            }
            HideHover();
            _p = null; _splitZone = false;

            var p = Profile;
            if (p == null || p.Z.Length < 2) return;
            double w = ActualWidth, h = ActualHeight;
            if (w < 20 || h < 20) return;

            bool splitZone = p.PointsInSeparateZone && p.Points.Length > 0;

            if (splitZone)
                RedrawSplit(p, w, h);
            else
                RedrawNormal(p, w, h);

            foreach (UIElement ov in new UIElement[] { _hoverLine, _hoverZLabel, _hoverVLabel })
            { Children.Remove(ov); Children.Add(ov); }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Нормальный режим
        // ─────────────────────────────────────────────────────────────────────

        void RedrawNormal(ThroughThicknessProfile p, double w, double h)
        {
            double fs = AxisFontSize;
            // Строки подписей снизу: row1=+3 (числа), row2=+row2Off (единица), row3=+row3Off (вторич.)
            const double row1Off = 3, row2Off = 22, row3Off = 42;

            bool hasSecondary = p.PointsUseSecondaryScale && p.Points.Length > 0;
            bool hasLegend    = p.Series.Length > 1;
            const double ml = 50, mr = 12, mt = 22;
            double mb = hasSecondary ? 58 : 42;
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
            if (!hasSecondary)
                foreach (var pt in p.Points)
                { vMin = Math.Min(vMin, pt.Value); vMax = Math.Max(vMax, pt.Value); }
            if (Math.Abs(vMax - vMin) < 1e-12) { vMax = 1; vMin = -1; }
            double vPad = 0.05 * (vMax - vMin); vMin -= vPad; vMax += vPad;

            double X(double v) => ml + (v - vMin) / (vMax - vMin) * plotW;
            double Y(double z) => mt + (zMax - z) / (zMax - zMin) * plotH;
            double x0 = X(0);

            double rebarHalfSpan = 1;
            Func<double, double> Xp = X;
            if (hasSecondary)
            {
                double maxAbs = p.Points.Max(pt => Math.Abs(pt.Value));
                rebarHalfSpan = maxAbs < 1e-12 ? 1 : maxAbs * 1.1;
                double scale = plotW * 0.5 / rebarHalfSpan;
                Xp = v => x0 + v * scale;
            }

            // 1. Заливка
            foreach (var s in p.Series)
                if (s.Values.Length == p.Z.Length)
                    DrawFill(p.Z, s.Values, X, x0, Y);

            // 2. Рамка
            var frame = new Rectangle { Width = plotW, Height = plotH, Stroke = Brushes.Gray, StrokeThickness = 1 };
            SetLeft(frame, ml); SetTop(frame, mt); Children.Add(frame);

            // 3. Ноль
            if (vMin < 0 && vMax > 0) AddLine(x0, mt, x0, mt + plotH, Brushes.DarkGray, 1, 3);

            // 4. Заголовок
            AddText(p.Title, ml, 2, Brushes.Black, 12, true);
            if (hasLegend)
            {
                double spacing = plotW * 0.55 / (p.Series.Length + 1);
                double startX  = ml + plotW * 0.45;
                for (int si = 0; si < p.Series.Length; si++)
                {
                    var s = p.Series[si];
                    double lx = startX + spacing * (si + 1) - 12;
                    AddLine(lx, 9, lx + 12, 9, s.Color, 2, 0);
                    AddText(s.Name, lx + 14, 1, s.Color, 11, true);
                }
            }

            // 5. Ось z (слева)
            // Правое выравнивание: правый край текста всегда в 4 px от рамки
            double zW = ml - 4;
            AddText("z",      0, 3,     Brushes.Gray,    fs, true, zW, TextAlignment.Right);
            AddText(Fv(zMax), 0, mt,    Brushes.DimGray, fs, true, zW, TextAlignment.Right);
            double zMinY = mt + plotH - fs - 3;
            if (zMinY > mt + fs + 4)
                AddText(Fv(zMin), 0, zMinY, Brushes.DimGray, fs, true, zW, TextAlignment.Right);

            // 6. Ось значений (снизу): числа в row1, единица в row2
            double row1Y = mt + plotH + row1Off;
            double row2Y = mt + plotH + row2Off;
            AddText(Fv(vMin), ml,              row1Y, Brushes.DimGray, fs, true);
            AddText(Fv(vMax), ml + plotW - 30, row1Y, Brushes.DimGray, fs, true);
            if (vMin < 0 && vMax > 0)
            {
                // Показываем "0" только если не перекрывается с vMin и vMax
                double x0label = x0 - 4;
                bool clearLeft  = x0label - ml > 30;
                bool clearRight = (ml + plotW - 30) - x0label > 30;
                if (clearLeft && clearRight)
                    AddText("0", x0label, row1Y, Brushes.DimGray, fs, true);
            }
            AddText(p.ValueAxisLabel, ml + plotW / 2 - 8, row2Y, Brushes.Black, fs, true);

            // Вторичная шкала (row3)
            if (hasSecondary)
            {
                double row3Y = mt + plotH + row3Off;
                var pBrush = p.Points[0].Color;
                AddText(Fv(-rebarHalfSpan), ml,              row3Y, pBrush, fs, true);
                AddText(Fv(+rebarHalfSpan), ml + plotW - 30, row3Y, pBrush, fs, true);
                AddText("0", x0 - 4,                         row3Y, pBrush, fs, true);
            }

            // 7. HLines
            foreach (var hl in p.HLines)
            {
                double yh = Y(hl.Z);
                AddLine(ml, yh, ml + plotW, yh, hl.Color, 0.9, 4);
                var lbl = new TextBlock { Text = hl.Label, Foreground = hl.Color, FontSize = fs, FontWeight = FontWeights.Bold };
                SetLeft(lbl, ml + 2); SetTop(lbl, yh - fs - 2); Children.Add(lbl);
            }

            // 8. Серии
            foreach (var s in p.Series)
            {
                if (s.Values.Length != p.Z.Length) continue;
                var poly = new Polyline { Stroke = s.Color, StrokeThickness = 1.6 };
                for (int i = 0; i < p.Z.Length; i++)
                    poly.Points.Add(new Point(X(s.Values[i]), Y(p.Z[i])));
                Children.Add(poly);
            }

            // 9. Точки арматуры
            foreach (var pt in p.Points)
            {
                double xPt = Xp(pt.Value), yPt = Y(pt.Z);
                AddLine(Xp(0), yPt, xPt, yPt, pt.Color, 1.5, 0);
                var dot = new Ellipse { Width = 7, Height = 7, Fill = pt.Color };
                SetLeft(dot, xPt - 3.5); SetTop(dot, yPt - 3.5); Children.Add(dot);
            }

            // Кеш
            _p = p; _ml = ml; _mt = mt; _plotH = plotH;
            _plotW = plotW; _vMin = vMin; _vMax = vMax; _zMin = zMin; _zMax = zMax; _x0 = x0;
            _hasSecondary = hasSecondary; _rebarHalfSpan = rebarHalfSpan;
            _splitZone = false; _plotWFull = plotW;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Сплит-режим: левая зона (серии) + правая зона (точки арматуры)
        // ─────────────────────────────────────────────────────────────────────

        void RedrawSplit(ThroughThicknessProfile p, double w, double h)
        {
            double fs = AxisFontSize;
            const double row1Off = 3, row2Off = 22;

            const double ml = 50, mr = 12, mt = 22, mb = 42;
            double plotW = w - ml - mr, plotH = h - mt - mb;
            if (plotW < 10 || plotH < 10) return;

            double zMin = p.Z.Min(), zMax = p.Z.Max();
            double Y(double z) => mt + (zMax - z) / (zMax - zMin) * plotH;

            const double splitFrac = 0.60;
            double plotWL = plotW * splitFrac;
            double plotWR = plotW * (1 - splitFrac);
            double splitX = ml + plotWL;

            // Масштаб левой зоны (бетон)
            double vMinL = 0, vMaxL = 0;
            foreach (var s in p.Series)
            {
                if (s.Values.Length == 0) continue;
                vMinL = Math.Min(vMinL, s.Values.Min());
                vMaxL = Math.Max(vMaxL, s.Values.Max());
            }
            if (Math.Abs(vMaxL - vMinL) < 1e-12) { vMaxL = 1; vMinL = -1; }
            double vPadL = 0.05 * (vMaxL - vMinL); vMinL -= vPadL; vMaxL += vPadL;
            double XL(double v) => ml + (v - vMinL) / (vMaxL - vMinL) * plotWL;
            double x0L = XL(0);

            // Масштаб правой зоны (арматура)
            double vMinR = 0, vMaxR = 0;
            foreach (var pt in p.Points)
            { vMinR = Math.Min(vMinR, pt.Value); vMaxR = Math.Max(vMaxR, pt.Value); }
            if (Math.Abs(vMaxR - vMinR) < 1e-12) { vMaxR = 1; vMinR = -1; }
            double vPadR = 0.05 * (vMaxR - vMinR); vMinR -= vPadR; vMaxR += vPadR;
            double XR(double v) => splitX + (v - vMinR) / (vMaxR - vMinR) * plotWR;
            double x0R = XR(0);

            // 1. Заливка (левая зона)
            foreach (var s in p.Series)
                if (s.Values.Length == p.Z.Length)
                    DrawFill(p.Z, s.Values, XL, x0L, Y);

            // 2. Рамка + разделитель
            var frame = new Rectangle { Width = plotW, Height = plotH, Stroke = Brushes.Gray, StrokeThickness = 1 };
            SetLeft(frame, ml); SetTop(frame, mt); Children.Add(frame);
            AddLine(splitX, mt, splitX, mt + plotH, s_divider, 0.8, 0);

            // 3. Оси нулей
            if (vMinL < 0 && vMaxL > 0) AddLine(x0L, mt, x0L, mt + plotH, Brushes.DarkGray, 1, 3);
            if (vMinR < 0 && vMaxR > 0) AddLine(x0R, mt, x0R, mt + plotH, Brushes.DarkGray, 1, 3);

            // 4. Заголовок
            AddText(p.Title, ml, 2, Brushes.Black, 12, true);

            // 5. Ось z (слева)
            // Правое выравнивание: правый край текста всегда в 4 px от рамки
            double zW = ml - 4;
            AddText("z",      0, 3,     Brushes.Gray,    fs, true, zW, TextAlignment.Right);
            AddText(Fv(zMax), 0, mt,    Brushes.DimGray, fs, true, zW, TextAlignment.Right);
            double zMinY = mt + plotH - fs - 3;
            if (zMinY > mt + fs + 4)
                AddText(Fv(zMin), 0, zMinY, Brushes.DimGray, fs, true, zW, TextAlignment.Right);

            // 6. Числа осей значений (row1) и единица (row2)
            double row1Y = mt + plotH + row1Off;
            double row2Y = mt + plotH + row2Off;

            // Левая зона: vMinL, 0, vMaxL
            AddText(Fv(vMinL), ml,              row1Y, Brushes.DimGray, fs, true);
            AddText(Fv(vMaxL), ml + plotWL - 30, row1Y, Brushes.DimGray, fs, true);
            if (vMinL < 0 && vMaxL > 0)
            {
                bool clearLeft  = x0L - 4 - ml > 30;
                bool clearRight = (ml + plotWL - 30) - (x0L - 4) > 30;
                if (clearLeft && clearRight)
                    AddText("0", x0L - 4, row1Y, Brushes.DimGray, fs, true);
            }

            // Правая зона: vMinR, 0, vMaxR
            var armBrush = p.Points[0].Color;
            AddText(Fv(vMinR), splitX + 1,           row1Y, armBrush, fs, true);
            AddText(Fv(vMaxR), splitX + plotWR - 30,  row1Y, armBrush, fs, true);
            if (vMinR < 0 && vMaxR > 0)
            {
                bool clearLeft  = x0R - 4 - splitX > 30;
                bool clearRight = (splitX + plotWR - 30) - (x0R - 4) > 30;
                if (clearLeft && clearRight)
                    AddText("0", x0R - 4, row1Y, armBrush, fs, true);
            }

            // Единица — по центру всей ширины
            AddText(p.ValueAxisLabel, ml + plotW / 2 - 8, row2Y, Brushes.Black, fs, true);

            // 7. HLines (левая зона)
            foreach (var hl in p.HLines)
            {
                double yh = Y(hl.Z);
                AddLine(ml, yh, splitX, yh, hl.Color, 0.9, 4);
                var lbl = new TextBlock { Text = hl.Label, Foreground = hl.Color, FontSize = fs, FontWeight = FontWeights.Bold };
                SetLeft(lbl, ml + 2); SetTop(lbl, yh - fs - 2); Children.Add(lbl);
            }

            // 8. Серии (левая зона)
            foreach (var s in p.Series)
            {
                if (s.Values.Length != p.Z.Length) continue;
                var poly = new Polyline { Stroke = s.Color, StrokeThickness = 1.6 };
                for (int i = 0; i < p.Z.Length; i++)
                    poly.Points.Add(new Point(XL(s.Values[i]), Y(p.Z[i])));
                Children.Add(poly);
            }

            // 9. Точки арматуры (правая зона)
            foreach (var pt in p.Points)
            {
                double xPt = XR(pt.Value), yPt = Y(pt.Z);
                AddLine(x0R, yPt, xPt, yPt, pt.Color, 1.5, 0);
                var dot = new Ellipse { Width = 7, Height = 7, Fill = pt.Color };
                SetLeft(dot, xPt - 3.5); SetTop(dot, yPt - 3.5); Children.Add(dot);
            }

            // Кеш
            _p = p; _ml = ml; _mt = mt; _plotH = plotH; _zMin = zMin; _zMax = zMax;
            _plotW = plotWL; _vMin = vMinL; _vMax = vMaxL; _x0 = x0L;
            _hasSecondary = false; _rebarHalfSpan = 1;
            _splitZone = true; _splitX = splitX; _plotWR = plotWR; _vMinR = vMinR; _vMaxR = vMaxR;
            _plotWFull = plotW;
        }

        // ── DrawFill ──────────────────────────────────────────────────────────

        void DrawFill(double[] zArr, double[] vArr, Func<double, double> X, double x0, Func<double, double> Y)
        {
            int n = zArr.Length;
            if (n < 2 || vArr.Length != n) return;

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

        // ═════════════════════════════════════════════════════════════════════
        //  Hover
        // ═════════════════════════════════════════════════════════════════════

        void OnMouseMove(object sender, MouseEventArgs e)
        {
            var p = _p;
            if (p == null || p.Z.Length < 2) { HideHover(); return; }

            var pos = e.GetPosition(this);
            if (pos.X < _ml || pos.X > _ml + _plotWFull || pos.Y < _mt || pos.Y > _mt + _plotH)
            { HideHover(); return; }

            double z = ZFromY(pos.Y);
            z = Math.Max(_zMin, Math.Min(_zMax, z));

            double hoverZ = z, hoverV = double.NaN;

            if (_splitZone && pos.X >= _splitX)
            {
                double bestDist = double.MaxValue;
                foreach (var pt in p.Points)
                {
                    double xPt = _splitX + (pt.Value - _vMinR) / (_vMaxR - _vMinR) * _plotWR;
                    double yPt = PrimaryY(pt.Z);
                    double d = Math.Sqrt((pos.X - xPt) * (pos.X - xPt) + (pos.Y - yPt) * (pos.Y - yPt));
                    if (d < bestDist) { bestDist = d; hoverZ = pt.Z; hoverV = pt.Value; }
                }
                if (bestDist > 30) hoverV = double.NaN;
            }
            else
            {
                bool hitRebar = false;
                if (!_splitZone)
                {
                    const double hitPx = 10.0;
                    foreach (var pt in p.Points)
                    {
                        double xPt = _hasSecondary
                            ? (_x0 + pt.Value * (_plotW * 0.5 / _rebarHalfSpan))
                            : PrimaryX(pt.Value);
                        double yPt = PrimaryY(pt.Z);
                        double dx = pos.X - xPt, dy = pos.Y - yPt;
                        if (Math.Sqrt(dx * dx + dy * dy) < hitPx)
                        { hoverZ = pt.Z; hoverV = pt.Value; hitRebar = true; break; }
                    }
                }
                if (!hitRebar && p.Series.Length > 0)
                {
                    double bestDist = double.MaxValue;
                    foreach (var s in p.Series)
                    {
                        if (s.Values.Length != p.Z.Length) continue;
                        double v = Interp(s.Values, p.Z, hoverZ);
                        double dist = Math.Abs(pos.X - PrimaryX(v));
                        if (dist < bestDist) { bestDist = dist; hoverV = v; }
                    }
                }
            }

            double yLine = PrimaryY(hoverZ);
            _hoverLine.X1 = _ml; _hoverLine.X2 = _ml + _plotWFull;
            _hoverLine.Y1 = yLine; _hoverLine.Y2 = yLine;
            _hoverLine.Visibility = Visibility.Visible;

            _hoverZLabel.Text = Fv(hoverZ);
            SetLeft(_hoverZLabel, 0); SetTop(_hoverZLabel, yLine - 16);
            _hoverZLabel.Visibility = Visibility.Visible;

            if (!double.IsNaN(hoverV))
            {
                _hoverVLabel.Text = Fv(hoverV);
                double xV;
                if (_splitZone && pos.X >= _splitX)
                    xV = _splitX + (hoverV - _vMinR) / (_vMaxR - _vMinR) * _plotWR;
                else
                    xV = PrimaryX(hoverV);
                xV = Math.Max(_ml + 2, Math.Min(xV, _ml + _plotWFull - 24));
                SetLeft(_hoverVLabel, xV + 6); SetTop(_hoverVLabel, yLine - 16);
                _hoverVLabel.Visibility = Visibility.Visible;
            }
            else
            {
                _hoverVLabel.Visibility = Visibility.Collapsed;
            }
        }

        void OnMouseLeave(object sender, MouseEventArgs e) => HideHover();

        void HideHover()
        {
            _hoverLine.Visibility = _hoverZLabel.Visibility = _hoverVLabel.Visibility = Visibility.Collapsed;
        }

        double PrimaryX(double v) => _ml + (v - _vMin) / (_vMax - _vMin) * _plotW;
        double PrimaryY(double z) => _mt + (_zMax - z) / (_zMax - _zMin) * _plotH;
        double ZFromY(double y)   => _zMax - (y - _mt) / _plotH * (_zMax - _zMin);

        static double Interp(double[] vals, double[] zArr, double z)
        {
            int n = zArr.Length;
            if (n == 0) return 0;
            if (z <= zArr[0])     return vals[0];
            if (z >= zArr[n - 1]) return vals[n - 1];
            int lo = 0, hi = n - 1;
            while (hi - lo > 1) { int mid = (lo + hi) / 2; if (zArr[mid] <= z) lo = mid; else hi = mid; }
            return vals[lo] + (z - zArr[lo]) / (zArr[hi] - zArr[lo]) * (vals[hi] - vals[lo]);
        }

        static string Fv(double v) => v == 0 ? "0" : v.ToString("G3", CultureInfo.InvariantCulture);

        void AddLine(double x1, double y1, double x2, double y2, Brush b, double th, double dash)
        {
            var l = new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = b, StrokeThickness = th };
            if (dash > 0) l.StrokeDashArray = new DoubleCollection { dash, dash };
            Children.Add(l);
        }

        void AddText(string s, double x, double y, Brush b, double size, bool bold,
                     double fixedWidth = 0, TextAlignment align = TextAlignment.Left)
        {
            var t = new TextBlock { Text = s, Foreground = b, FontSize = size, TextAlignment = align };
            if (bold) t.FontWeight = FontWeights.Bold;
            if (fixedWidth > 0) t.Width = fixedWidth;
            SetLeft(t, x); SetTop(t, y); Children.Add(t);
        }
    }
}
