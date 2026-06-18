using System.Globalization;
using System.Text.Json;
using System.Windows.Media;
using CScore;

namespace OpenCS.ViewModels;

/// <summary>Сводка результата поиска плоскости деформаций пластины.</summary>
public sealed class ShellStrainSummaryVM
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public string TaskTag { get; }
    public string CreatedText { get; }
    public string StatusText { get; }
    public Brush StatusBrush { get; }
    public string IterationsText { get; }
    public string ResidualText { get; }
    public string Eps0xText { get; } public string Eps0yText { get; } public string Gamma0xyText { get; }
    public string KxText { get; } public string KyText { get; } public string KxyText { get; }
    public string NxRow { get; } public string NyRow { get; } public string NxyRow { get; }
    public string MxRow { get; } public string MyRow { get; } public string MxyRow { get; }
    public string EAxText { get; } public string EAyText { get; }
    public string EIxText { get; } public string EIyText { get; }
    public string ZcText { get; }

    public ShellStrainSummaryVM(CalcResult r)
    {
        TaskTag = r.TaskTag; CreatedText = r.Created; StatusText = r.Status;
        StatusBrush = r.Status switch
        {
            "ok" => Brushes.Green,
            "not_converged" => Brushes.DarkOrange,
            _ => Brushes.Red,
        };

        double G(JsonElement e, string k) => e.TryGetProperty(k, out var v) ? v.GetDouble() : 0;
        int Gi(JsonElement e, string k) => e.TryGetProperty(k, out var v) ? v.GetInt32() : 0;
        string F(double x, int d) => x.ToString("F" + d, Inv);
        string E(double x) => x.ToString("E3", Inv);

        IterationsText = "—"; ResidualText = "—";
        Eps0xText = Eps0yText = Gamma0xyText = KxText = KyText = KxyText = "—";
        NxRow = NyRow = NxyRow = MxRow = MyRow = MxyRow = "—";
        EAxText = EAyText = EIxText = EIyText = ZcText = "—";

        try
        {
            var root = JsonDocument.Parse(r.DataJson).RootElement;
            if (root.TryGetProperty("error", out _)) return;

            IterationsText = Gi(root, "iterations").ToString(Inv);
            ResidualText = E(G(root, "residual"));
            Eps0xText = E(G(root, "eps0x")); Eps0yText = E(G(root, "eps0y"));
            Gamma0xyText = E(G(root, "gamma0xy"));
            KxText = E(G(root, "kx")); KyText = E(G(root, "ky")); KxyText = E(G(root, "kxy"));
            NxRow = $"{F(G(root, "Nx_result"), 2)} / {F(G(root, "Nx_target"), 2)}";
            NyRow = $"{F(G(root, "Ny_result"), 2)} / {F(G(root, "Ny_target"), 2)}";
            NxyRow = $"{F(G(root, "Nxy_result"), 2)} / {F(G(root, "Nxy_target"), 2)}";
            MxRow = $"{F(G(root, "Mx_result"), 2)} / {F(G(root, "Mx_target"), 2)}";
            MyRow = $"{F(G(root, "My_result"), 2)} / {F(G(root, "My_target"), 2)}";
            MxyRow = $"{F(G(root, "Mxy_result"), 2)} / {F(G(root, "Mxy_target"), 2)}";
            EAxText = F(G(root, "EAx"), 1); EAyText = F(G(root, "EAy"), 1);
            EIxText = F(G(root, "EIx"), 3); EIyText = F(G(root, "EIy"), 3);
            ZcText = F(G(root, "Zc"), 4);
        }
        catch { /* повреждённый JSON — поля остаются "—" */ }
    }
}
