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
                var s = plate.SampleThroughThickness(st, cDiag, rDiag, layerDiags, 41);

                EpsCanvas.Profile = new ThroughThicknessProfile
                {
                    Z = s.Z,
                    Title = (string)Application.Current.Resources["ShellStrainEpsPlot"],
                    ValueAxisLabel = "ε",
                    Series = new[]
                    {
                        ("εx", s.EpsX, (Brush)Brushes.Crimson),
                        ("εy", s.EpsY, (Brush)Brushes.SteelBlue),
                        ("γxy", s.GammaXY, (Brush)Brushes.SeaGreen),
                    },
                };
                SigCanvas.Profile = new ThroughThicknessProfile
                {
                    Z = s.Z,
                    Title = (string)Application.Current.Resources["ShellStrainSigPlot"],
                    ValueAxisLabel = "σ, МПа",
                    Series = new[]
                    {
                        ("σx", s.SigX, (Brush)Brushes.Crimson),
                        ("σy", s.SigY, (Brush)Brushes.SteelBlue),
                        ("τxy", s.TauXY, (Brush)Brushes.SeaGreen),
                    },
                    Points = s.Rebar.Select(p =>
                        (p.Z, p.Sigma, p.AlongX ? (Brush)Brushes.DarkRed : (Brush)Brushes.DarkBlue)).ToArray(),
                };
            }
            catch { /* нет материалов/диаграмм — показываем только сводку */ }
        }

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
