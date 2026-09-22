using System.Linq;
using System.Text.Json;
using System.Windows.Controls;
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
                bool? tensionOverride = task.CalcType is CalcType.C or CalcType.CL
                   ? app.CalcSettings.ConsiderConcreteTensionUls : (bool?)null;

                // ── HLines: центры тяжести из JSON ────────────────────────────
                double zcxM = 0, zcyM = 0;
                try
                {
                    var root = JsonDocument.Parse(result.DataJson).RootElement;
                    if (root.TryGetProperty("zc_x_sec", out var vx)) zcxM = vx.GetDouble() / 1000.0;
                    if (root.TryGetProperty("zc_y_sec", out var vy)) zcyM = vy.GetDouble() / 1000.0;
                }
                catch { }

                ShellProfilePlots.Fill(
                    new ShellProfileCanvases(EpsXCanvas, EpsYCanvas, EpsGammaCanvas,
                        SigXCanvas, SigYCanvas, TauXYCanvas,
                        PrEpsCanvas, PrSigCanvas, BetaCanvas, ThetaCanvas),
                    plate, st, cDiag, rDiag, layerDiags, tensionOverride, zcxM, zcyM);
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
