using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Windows.Media;
using CScore;
using OpenCS.Utilites;
using OpenCS.Views.Helpers;

namespace OpenCS.Views;

/// <summary>Результат подбора сеток Капри-Мори по ПС1 или ПС2 (<c>shell_simpl_capri_design_uls/sls</c>).</summary>
public partial class CapraMauryDesignResultView : System.Windows.Controls.UserControl
{
    public CapraMauryDesignResultView(CalcResult result, CalcTask task)
    {
        InitializeComponent();
        var vm = new CapraMauryDesignResultVM(result, task);
        DataContext = vm;
        if (vm.IsSls)
        {
            // ПС2: вместо η полосы — a_crc при требуемых площадях, плюс отправная As_c из ПС1.
            ValueColumn.Header = Loc.S("CapriDesignColAcrc");
            AsCUlsColumn.Visibility = System.Windows.Visibility.Visible;
        }
        TopCanvas.SetData(vm.TopPoints, vm.TopAsxValue, vm.TopAsyValue);
        BotCanvas.SetData(vm.BotPoints, vm.BotAsxValue, vm.BotAsyValue);
    }
}

public sealed class CapraMauryDesignFacetRow
{
    public string Theta { get; init; } = "";
    public string N { get; init; } = "";
    public string M { get; init; } = "";
    public string ReqTop { get; init; } = "";
    public string ProvTop { get; init; } = "";
    public string ReqBot { get; init; } = "";
    public string ProvBot { get; init; } = "";
    public string Status { get; init; } = "";
    public string Eta { get; init; } = "";
    public string AsCUls { get; init; } = "";
    public bool IsActive { get; init; }
}

/// <summary>Разбор JSON подбора; площади в JSON — м²/м, в виде — см²/м.</summary>
public sealed class CapraMauryDesignResultVM : ViewModelBase
{
    const double Cm2 = 1e4;

    public string Title { get; }
    /// <summary>Подбор по ПС2 (трещины).</summary>
    public bool IsSls { get; }
    public string ModelNote { get; } = Loc.S("CapriDesignModelNote");
    public string StatusText { get; }
    public Brush StatusBrush { get; }
    public string ForcesText { get; } = "";
    public string TopAsx { get; } = "";
    public string TopAsy { get; } = "";
    public string BotAsx { get; } = "";
    public string BotAsy { get; } = "";
    public string TopActive { get; } = "";
    public string BotActive { get; } = "";
    public double TopAsxValue { get; }
    public double TopAsyValue { get; }
    public double BotAsxValue { get; }
    public double BotAsyValue { get; }
    public CapraMauryDemandPoint[] TopPoints { get; } = [];
    public CapraMauryDemandPoint[] BotPoints { get; } = [];
    public ObservableCollection<CapraMauryDesignFacetRow> Facets { get; } = [];
    public ObservableCollection<string> Diagnostics { get; } = [];
    public bool HasDiagnostics => Diagnostics.Count > 0;
    /// <summary>Поперечная сила при подобранных сетках (ПС1); null — Qx = Qy = 0 или подбор по ПС2.</summary>
    public PlateShearBlock? Shear { get; }
    public bool HasShear => Shear != null;

    public CapraMauryDesignResultVM(CalcResult result, CalcTask task)
    {
        Title = task.Tag;
        var doc = JsonSerializer.Deserialize<JsonElement>(result.DataJson);
        if (doc.TryGetProperty("error", out var err))
        {
            StatusText = err.GetString() ?? "";
            StatusBrush = FailBrush;
            return;
        }

        var f = doc.GetProperty("forces");
        var ci = CultureInfo.CurrentCulture;
        ForcesText = string.Format(ci, "Nx = {0:F2}, Ny = {1:F2}, Nxy = {2:F2};   Mx = {3:F2}, My = {4:F2}, Mxy = {5:F2}",
            f.GetProperty("nx").GetDouble(), f.GetProperty("ny").GetDouble(), f.GetProperty("nxy").GetDouble(),
            f.GetProperty("mx").GetDouble(), f.GetProperty("my").GetDouble(), f.GetProperty("mxy").GetDouble());
        if (f.TryGetProperty("qx", out var qx) && f.TryGetProperty("qy", out var qy)
            && (qx.GetDouble() != 0.0 || qy.GetDouble() != 0.0))
            ForcesText += string.Format(ci, ";   Qx = {0:F2}, Qy = {1:F2}", qx.GetDouble(), qy.GetDouble());

        var d = doc.GetProperty("design");
        IsSls = d.TryGetProperty("Limit", out var lim) && lim.GetString() == "sls";
        if (IsSls)
            ModelNote = string.Format(ci, Loc.S("CapriDesignModelNoteSls"), d.GetProperty("AcrcLimMm").GetDouble());
        var top = d.GetProperty("Top");
        var bot = d.GetProperty("Bottom");
        TopAsxValue = top.GetProperty("Asx").GetDouble() * Cm2;
        TopAsyValue = top.GetProperty("Asy").GetDouble() * Cm2;
        BotAsxValue = bot.GetProperty("Asx").GetDouble() * Cm2;
        BotAsyValue = bot.GetProperty("Asy").GetDouble() * Cm2;
        TopAsx = TopAsxValue.ToString("F2", ci); TopAsy = TopAsyValue.ToString("F2", ci);
        BotAsx = BotAsxValue.ToString("F2", ci); BotAsy = BotAsyValue.ToString("F2", ci);
        var topActive = Thetas(top);
        var botActive = Thetas(bot);
        TopActive = ActiveText(topActive);
        BotActive = ActiveText(botActive);

        var facets = d.GetProperty("Facets").EnumerateArray().ToList();
        TopPoints = new CapraMauryDemandPoint[facets.Count];
        BotPoints = new CapraMauryDemandPoint[facets.Count];
        for (int i = 0; i < facets.Count; i++)
        {
            var x = facets[i];
            double theta = x.GetProperty("ThetaDeg").GetDouble();
            double reqTop = x.GetProperty("AsTop").GetDouble() * Cm2, reqBot = x.GetProperty("AsBot").GetDouble() * Cm2;
            double provTop = x.GetProperty("AsNTop").GetDouble() * Cm2, provBot = x.GetProperty("AsNBot").GetDouble() * Cm2;
            bool aTop = topActive.Contains(theta), aBot = botActive.Contains(theta);
            TopPoints[i] = new CapraMauryDemandPoint(theta, reqTop, provTop, aTop);
            BotPoints[i] = new CapraMauryDemandPoint(theta, reqBot, provBot, aBot);
            double eta = x.GetProperty(IsSls ? "AcrcMm" : "Eta").GetDouble();
            double asCUls = x.TryGetProperty("AsCUls", out var acu) ? acu.GetDouble() * Cm2 : 0.0;
            Facets.Add(new CapraMauryDesignFacetRow
            {
                Theta = theta.ToString("0.##", ci),
                N = x.GetProperty("N").GetDouble().ToString("F2", ci),
                M = x.GetProperty("M").GetDouble().ToString("F2", ci),
                ReqTop = reqTop.ToString("F2", ci), ProvTop = provTop.ToString("F2", ci),
                ReqBot = reqBot.ToString("F2", ci), ProvBot = provBot.ToString("F2", ci),
                Status = StatusName(x.GetProperty("Status")),
                Eta = eta >= 1e9 ? "∞" : eta.ToString("F3", ci),
                AsCUls = asCUls.ToString("F2", ci),
                IsActive = aTop || aBot,
            });
        }

        foreach (var s in d.GetProperty("Diagnostics").EnumerateArray())
            Diagnostics.Add(s.GetString() ?? "");

        // (8.55) не выполняется — сетки подобраны, но толщины по поперечной силе не хватает.
        Shear = PlateShearBlock.From(doc, "shear", design: true);
        bool ok = Diagnostics.Count == 0 && Shear is not { Ok: false };
        StatusText = Loc.S(ok ? "CapriDesignOk" : Diagnostics.Count == 0 ? "CapriDesignShearFails"
            : IsSls ? "CapriDesignImpossibleSls" : "CapriDesignImpossible");
        StatusBrush = ok ? OkBrush : FailBrush;

        static double[] Thetas(JsonElement mesh) =>
            mesh.GetProperty("ActiveThetas").EnumerateArray().Select(t => t.GetDouble()).ToArray();
        static string ActiveText(double[] t) =>
            t.Length == 0 ? "—" : string.Join(", ", t.Select(v => v.ToString("0.##", CultureInfo.CurrentCulture) + "°"));
    }

    internal static readonly Brush OkBrush = new SolidColorBrush(Color.FromArgb(70, 80, 180, 80));
    internal static readonly Brush FailBrush = new SolidColorBrush(Color.FromArgb(60, 192, 57, 43));

    /// <summary>Исход подбора полосы: <see cref="CScore.ShellSimpl.StripNdmDesignStatus"/> числом или строкой.</summary>
    internal static string StatusName(JsonElement s)
    {
        var status = s.ValueKind == JsonValueKind.Number
            ? (CScore.ShellSimpl.StripNdmDesignStatus)s.GetInt32()
            : Enum.TryParse<CScore.ShellSimpl.StripNdmDesignStatus>(s.GetString(), out var v) ? v : CScore.ShellSimpl.StripNdmDesignStatus.Impossible;
        return Loc.S("CapriDesignStatus" + status);
    }
}
