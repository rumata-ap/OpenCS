using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using CScore;
using OpenCS.Utilites;

namespace OpenCS.Views.Helpers;

/// <summary>Набор холстов эпюр по толщине пластины: деформации, напряжения, главные оси.</summary>
public sealed record ShellProfileCanvases(
    ThroughThicknessCanvas EpsX, ThroughThicknessCanvas EpsY, ThroughThicknessCanvas EpsGamma,
    ThroughThicknessCanvas SigX, ThroughThicknessCanvas SigY, ThroughThicknessCanvas TauXY,
    ThroughThicknessCanvas PrEps, ThroughThicknessCanvas PrSig,
    ThroughThicknessCanvas Beta, ThroughThicknessCanvas Theta);

/// <summary>
/// Построение эпюр по толщине пластины для заданного НДС. Общее для результатов
/// проверки прочности и ширины раскрытия трещин слоистой модели — диаграммы и
/// учёт растяжения бетона передаются те же, что использовал решатель.
/// </summary>
public static class ShellProfilePlots
{
    const int NPoints = 41;

    /// <param name="zcxM">Центр тяжести по секущей жёсткости вдоль X, м.</param>
    /// <param name="zcyM">Центр тяжести по секущей жёсткости вдоль Y, м.</param>
    public static void Fill(ShellProfileCanvases c, PlateSection plate, ShellStrainState st,
        Diagramm cDiag, Diagramm rDiag, IReadOnlyList<Diagramm?>? layerDiags,
        bool? tensionOverride, double zcxM, double zcyM)
    {
        var zcxLine = new (double Z, Brush Color, string Label)[] { (zcxM, Brushes.DarkRed,  "zc,x") };
        var zcyLine = new (double Z, Brush Color, string Label)[] { (zcyM, Brushes.DarkBlue, "zc,y") };

        // ── Выборка профилей ──────────────────────────────────────────
        var s = plate.SampleThroughThickness(st, cDiag, rDiag, layerDiags, NPoints, tensionOverride);

        // Маркеры арматуры на деформационных эпюрах (деформация в слое)
        var rebarEpsX = s.Rebar.Where(r => r.AlongX)
            .Select(r => (r.Z, st.EpsX(r.Z), (Brush)Brushes.DarkRed)).ToArray();
        var rebarEpsY = s.Rebar.Where(r => !r.AlongX)
            .Select(r => (r.Z, st.EpsY(r.Z), (Brush)Brushes.DarkBlue)).ToArray();
        var rebarEpsGamma = s.Rebar
            .Select(r => (r.Z, st.GammaXY(r.Z),
                r.AlongX ? (Brush)Brushes.DarkRed : (Brush)Brushes.DarkBlue)).ToArray();

        // ── Деформации ────────────────────────────────────────────────
        c.EpsX.Profile = new ThroughThicknessProfile
        {
            Z = s.Z,
            Title = Loc.S("ShellStrainEpsXPlot"),
            ValueAxisLabel = "ε",
            Series = new[] { ("εx", s.EpsX, (Brush)Brushes.Crimson) },
            Points = rebarEpsX,
        };
        c.EpsY.Profile = new ThroughThicknessProfile
        {
            Z = s.Z,
            Title = Loc.S("ShellStrainEpsYPlot"),
            ValueAxisLabel = "ε",
            Series = new[] { ("εy", s.EpsY, (Brush)Brushes.SteelBlue) },
            Points = rebarEpsY,
        };
        c.EpsGamma.Profile = new ThroughThicknessProfile
        {
            Z = s.Z,
            Title = Loc.S("ShellStrainEpsGammaPlot"),
            ValueAxisLabel = "γ",
            Series = new[] { ("γxy", s.GammaXY, (Brush)Brushes.SeaGreen) },
            Points = rebarEpsGamma,
        };

        // ── Напряжения (кПа → МПа) ────────────────────────────────────
        var sigX  = s.SigX.Select(v => v / 1000.0).ToArray();
        var sigY  = s.SigY.Select(v => v / 1000.0).ToArray();
        var tauXY = s.TauXY.Select(v => v / 1000.0).ToArray();

        c.SigX.Profile = new ThroughThicknessProfile
        {
            Z = s.Z,
            Title = Loc.S("ShellStrainSigXPlot"),
            ValueAxisLabel = "МПа",
            Series = new[] { ("σx", sigX, (Brush)Brushes.Crimson) },
            Points = s.Rebar.Where(r => r.AlongX)
                            .Select(r => (r.Z, r.Sigma / 1000.0, (Brush)Brushes.DarkRed))
                            .ToArray(),
            HLines = zcxLine,
            PointsInSeparateZone = true,
        };
        c.SigY.Profile = new ThroughThicknessProfile
        {
            Z = s.Z,
            Title = Loc.S("ShellStrainSigYPlot"),
            ValueAxisLabel = "МПа",
            Series = new[] { ("σy", sigY, (Brush)Brushes.SteelBlue) },
            Points = s.Rebar.Where(r => !r.AlongX)
                            .Select(r => (r.Z, r.Sigma / 1000.0, (Brush)Brushes.DarkBlue))
                            .ToArray(),
            HLines = zcyLine,
            PointsInSeparateZone = true,
        };
        c.TauXY.Profile = new ThroughThicknessProfile
        {
            Z = s.Z,
            Title = Loc.S("ShellStrainSigTauPlot"),
            ValueAxisLabel = "МПа",
            Series = new[] { ("τxy", tauXY, (Brush)Brushes.SeaGreen) },
        };

        // ── Главные оси ───────────────────────────────────────────────
        var pa = plate.SamplePrincipalAxes(st, cDiag, NPoints, tensionOverride);

        c.PrEps.Profile = new ThroughThicknessProfile
        {
            Z = pa.Z,
            Title = Loc.S("ShellStrainPrEpsPlot"),
            ValueAxisLabel = "ε",
            Series = new[]
            {
                ("ε₁", pa.Eps1, (Brush)Brushes.Crimson),
                ("ε₂", pa.Eps2, (Brush)Brushes.SteelBlue),
            },
        };
        c.PrSig.Profile = new ThroughThicknessProfile
        {
            Z = pa.Z,
            Title = Loc.S("ShellStrainPrSigPlot"),
            ValueAxisLabel = "МПа",
            Series = new[]
            {
                ("σ₁", pa.Sig1.Select(v => v / 1000.0).ToArray(), (Brush)Brushes.Crimson),
                ("σ₂", pa.Sig2.Select(v => v / 1000.0).ToArray(), (Brush)Brushes.SteelBlue),
            },
        };
        c.Beta.Profile = new ThroughThicknessProfile
        {
            Z = pa.Z,
            Title = Loc.S("ShellStrainBetaPlot"),
            ValueAxisLabel = "β",
            Series = new[] { ("β", pa.Beta, (Brush)Brushes.DarkOrange) },
        };
        c.Theta.Profile = new ThroughThicknessProfile
        {
            Z = pa.Z,
            Title = Loc.S("ShellStrainThetaPlot"),
            ValueAxisLabel = "°",
            Series = new[] { ("θ", pa.ThetaDeg, (Brush)Brushes.Purple) },
        };
    }
}
