using CScore.Planar;

namespace CScore.PlateStrip;

/// <summary>Производный 1D-фрагмент — стержневая аналогия полосы плиты между двумя явно
/// заданными опорами (Срез 1: только геометрия, WidthPolicy = ExplicitWidth, request-local —
/// без SQLite-персистентности). См.
/// docs/superpowers/specs/2026-07-26-plate-strip-beam-analogy-design.md.</summary>
public sealed class PlateStripBeamAnalogy
{
    public string Id { get; set; } = "";
    public int SourceRegionId { get; set; }
    public SupportLocus StartSupportLocus { get; set; } = new();
    public SupportLocus EndSupportLocus { get; set; } = new();
    public Frame3D StripFrame { get; set; } = Frame3D.Identity;
    public PlateStripGeometry Geometry { get; set; } = new();
    public double ExplicitWidthM { get; set; }
    public string Fingerprint { get; set; } = "";
}
