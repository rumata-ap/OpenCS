using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CScore;
using OpenCS.Tasks;
using OpenCS.ViewModels;
using OpenCS.Views.Helpers;

namespace OpenCS.Views
{
    public partial class ShellStrainResultView : UserControl
    {
        public ShellStrainResultView(CalcResult result, AppViewModel app, CalcTask task)
        {
            InitializeComponent();
            DataContext = new ShellStrainSummaryVM(result);

            var plate = app.PlateSections.FirstOrDefault(s => s.Id == task.SectionId);
            if (plate == null) return;
            try
            {
                var (cDiag, rDiag, layerDiags, _) =
                    PlateMaterialResolver.Resolve(plate, app.db.Materials, task.CalcType);
                var st = ParseState(result.DataJson);

                // ──── HLines: центры тяжести (zc из секущих жёсткостей) ───────
                double zcx = 0, zcy = 0;
                try
                {
                    var root = JsonDocument.Parse(result.DataJson).RootElement;
                    if (root.TryGetProperty("zc_x_sec", out var vx)) zcx = vx.GetDouble() / 1000.0;
                    if (root.TryGetProperty("zc_y_sec", out var vy)) zcy = vy.GetDouble() / 1000.0;
                }
                catch { }

                var zcxLine = new (double Z, Brush Color, string Label)[] { (zcx, Brushes.DarkRed,  "zc,x") };
                var zcyLine = new (double Z, Brush Color, string Label)[] { (zcy, Brushes.DarkBlue, "zc,y") };

                // ──── Вкладка «Деформации»: εx(z) и εy(z) ───────────────────
                var s = plate.SampleThroughThickness(st, cDiag, rDiag, layerDiags, 41);

                EpsXCanvas.Profile = new ThroughThicknessProfile
                {
                    Z = s.Z,
                    Title = Res("ShellStrainEpsXPlot"),
                    ValueAxisLabel = "ε",
                    Series = new[]
                    {
                        ("εx",  s.EpsX,    (Brush)Brushes.Crimson),
                        ("γxy", s.GammaXY, (Brush)Brushes.SeaGreen),
                    },
                };
                EpsYCanvas.Profile = new ThroughThicknessProfile
                {
                    Z = s.Z,
                    Title = Res("ShellStrainEpsYPlot"),
                    ValueAxisLabel = "ε",
                    Series = new[]
                    {
                        ("εy",  s.EpsY,    (Brush)Brushes.SteelBlue),
                        ("γxy", s.GammaXY, (Brush)Brushes.SeaGreen),
                    },
                };

                // ──── Вкладка «Напряжения»: σx(z) и σy(z) в МПа ─────────────
                var sigX  = s.SigX.Select(v => v / 1000.0).ToArray();
                var sigY  = s.SigY.Select(v => v / 1000.0).ToArray();
                var tauXY = s.TauXY.Select(v => v / 1000.0).ToArray();

                SigXCanvas.Profile = new ThroughThicknessProfile
                {
                    Z = s.Z,
                    Title = Res("ShellStrainSigXPlot"),
                    ValueAxisLabel = "МПа",
                    Series = new[]
                    {
                        ("σx",  sigX,  (Brush)Brushes.Crimson),
                        ("τxy", tauXY, (Brush)Brushes.SeaGreen),
                    },
                    Points = s.Rebar.Where(p => p.AlongX)
                                    .Select(p => (p.Z, p.Sigma / 1000.0, (Brush)Brushes.DarkRed))
                                    .ToArray(),
                    HLines = zcxLine,
                };
                SigYCanvas.Profile = new ThroughThicknessProfile
                {
                    Z = s.Z,
                    Title = Res("ShellStrainSigYPlot"),
                    ValueAxisLabel = "МПа",
                    Series = new[]
                    {
                        ("σy",  sigY,  (Brush)Brushes.SteelBlue),
                        ("τxy", tauXY, (Brush)Brushes.SeaGreen),
                    },
                    Points = s.Rebar.Where(p => !p.AlongX)
                                    .Select(p => (p.Z, p.Sigma / 1000.0, (Brush)Brushes.DarkBlue))
                                    .ToArray(),
                    HLines = zcyLine,
                };

                // ──── Вкладка «Главные оси» ───────────────────────────────────
                var pa = plate.SamplePrincipalAxes(st, cDiag, 41);

                PrEpsCanvas.Profile = new ThroughThicknessProfile
                {
                    Z = pa.Z,
                    Title = Res("ShellStrainPrEpsPlot"),
                    ValueAxisLabel = "ε",
                    Series = new[]
                    {
                        ("ε₁", pa.Eps1, (Brush)Brushes.Crimson),
                        ("ε₂", pa.Eps2, (Brush)Brushes.SteelBlue),
                    },
                };
                PrSigCanvas.Profile = new ThroughThicknessProfile
                {
                    Z = pa.Z,
                    Title = Res("ShellStrainPrSigPlot"),
                    ValueAxisLabel = "МПа",
                    Series = new[]
                    {
                        ("σ₁", pa.Sig1.Select(v => v / 1000.0).ToArray(), (Brush)Brushes.Crimson),
                        ("σ₂", pa.Sig2.Select(v => v / 1000.0).ToArray(), (Brush)Brushes.SteelBlue),
                    },
                };
                BetaCanvas.Profile = new ThroughThicknessProfile
                {
                    Z = pa.Z,
                    Title = Res("ShellStrainBetaPlot"),
                    ValueAxisLabel = "β",
                    Series = new[]
                    {
                        ("β", pa.Beta, (Brush)Brushes.DarkOrange),
                    },
                };
                ThetaCanvas.Profile = new ThroughThicknessProfile
                {
                    Z = pa.Z,
                    Title = Res("ShellStrainThetaPlot"),
                    ValueAxisLabel = "°",
                    Series = new[]
                    {
                        ("θ", pa.ThetaDeg, (Brush)Brushes.Purple),
                    },
                };
            }
            catch { /* нет материалов/диаграмм — показываем только сводку */ }
        }

        static string Res(string key)
            => Application.Current.Resources.Contains(key)
                ? (string)Application.Current.Resources[key]
                : key;

        static ShellStrainState ParseState(string json)
        {
            try
            {
                var r = JsonDocument.Parse(json).RootElement;
                double G(string k) => r.TryGetProperty(k, out var v) ? v.GetDouble() : 0;
                return new ShellStrainState(G("eps0x"), G("eps0y"), G("gamma0xy"),
                    G("kx"), G("ky"), G("kxy"));
            }
            catch { return ShellStrainState.Zero; }
        }
    }
}
