using System.Windows;
using System.Windows.Media;
using CScore.Import.Cad;

namespace OpenCS.Views;

/// <summary>Миниатюра распознанного сечения: бетон и сталь с отверстиями, стержни; ц. т. — крестик.</summary>
public sealed class CadSectionThumb : FrameworkElement
{
   public static readonly DependencyProperty CandidateProperty = DependencyProperty.Register(
      nameof(Candidate), typeof(CadSectionCandidate), typeof(CadSectionThumb),
      new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

   public CadSectionCandidate? Candidate
   {
      get => (CadSectionCandidate?)GetValue(CandidateProperty);
      set => SetValue(CandidateProperty, value);
   }

   static readonly Brush ConcreteFill = Freeze(new SolidColorBrush(Color.FromRgb(0xD9, 0xD9, 0xD9)));
   static readonly Brush SteelFill = Freeze(new SolidColorBrush(Color.FromRgb(0x7F, 0xA7, 0xD1)));
   static readonly Brush BarFill = Freeze(new SolidColorBrush(Color.FromRgb(0xB0, 0x30, 0x30)));
   static readonly Pen Outline = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0x40, 0x40, 0x40)), 1));
   static readonly Pen Axis = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0x20, 0x80, 0x20)), 1));

   static T Freeze<T>(T f) where T : Freezable { f.Freeze(); return f; }

   protected override void OnRender(DrawingContext dc)
   {
      dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
      if (Candidate is not { Regions.Count: > 0 } c) return;
      var pts = c.Regions.SelectMany(r => r.Outer).ToList();
      double x0 = pts.Min(p => p.X), x1 = pts.Max(p => p.X), y0 = pts.Min(p => p.Y), y1 = pts.Max(p => p.Y);
      double pad = 6;
      double k = Math.Min((ActualWidth - 2 * pad) / Math.Max(x1 - x0, 1e-9), (ActualHeight - 2 * pad) / Math.Max(y1 - y0, 1e-9));
      double ox = pad + (ActualWidth - 2 * pad - (x1 - x0) * k) / 2, oy = pad + (ActualHeight - 2 * pad - (y1 - y0) * k) / 2;
      Point P(double x, double y) => new(ox + (x - x0) * k, oy + (y1 - y) * k);

      foreach (var r in c.Regions)
      {
         var g = new StreamGeometry { FillRule = FillRule.EvenOdd };
         using (var ctx = g.Open())
         {
            foreach (var ring in r.Holes.Prepend(r.Outer))
            {
               ctx.BeginFigure(P(ring[0].X, ring[0].Y), true, true);
               ctx.PolyLineTo(ring.Skip(1).Select(p => P(p.X, p.Y)).ToList(), true, false);
            }
         }
         g.Freeze();
         dc.DrawGeometry(r.Role == CadLayerRole.Steel ? SteelFill : ConcreteFill, Outline, g);
      }
      foreach (var b in c.Bars)
      {
         double rr = Math.Max(1.5, b.DiameterM / 2 * k);
         dc.DrawEllipse(BarFill, null, P(b.X, b.Y), rr, rr);
      }
      var o = P(0, 0);
      dc.DrawLine(Axis, new Point(o.X - 5, o.Y), new Point(o.X + 5, o.Y));
      dc.DrawLine(Axis, new Point(o.X, o.Y - 5), new Point(o.X, o.Y + 5));
   }
}
