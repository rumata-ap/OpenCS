using CScore.Planar;

namespace CScore.Fem.Loads;

/// <summary>Вид величины глифа — для единиц подписи.</summary>
public enum FemLoadGlyphUnit
{
    /// <summary>Па (пластина: равномерная, по узлам, с. в.).</summary>
    Pressure,
    /// <summary>Н/м (стержень: равномерная, с. в.).</summary>
    LineLoad,
    /// <summary>Н (сосредоточенная).</summary>
    Force,
}

/// <summary>Стрелка нагрузки на КЭ: точка приложения (центр КЭ или точка силы), единичное направление действия,
/// модуль (в единицах <see cref="Unit"/>) и характерный размер КЭ для длины стрелки.</summary>
public sealed record FemElementLoadGlyph(string ElementTag, PlanarVector3 Point, PlanarVector3 Direction, double Magnitude,
    FemLoadGlyphUnit Unit, double SizeM);

/// <summary>Подпись нагрузки: одна на нагрузку, у первой её стрелки.</summary>
public sealed record FemElementLoadLabel(PlanarVector3 Point, double Magnitude, FemLoadGlyphUnit Unit, string Kind);

/// <summary>Глифы нагрузок на КЭ для 3D-вида: стрелки (с прореживанием), подписи, нагруженные КЭ.</summary>
public sealed record FemElementLoadGlyphSet(IReadOnlyList<FemElementLoadGlyph> Arrows, IReadOnlyList<FemElementLoadLabel> Labels,
    IReadOnlyList<string> LoadedElementTags, IReadOnlyList<FemValidationDiagnostic> Diagnostics);

/// <summary>
/// Глифы нагрузок на КЭ загружения или суммы загружений с коэффициентами. Направление и модуль — по виду нагрузки:
/// равномерная — в центре КЭ, по узлам — среднее значение в центре, сосредоточенная — в точке, с. в. — вниз. Стрелок
/// одной нагрузки не больше <paramref name="maxArrowsPerLoad"/> (КЭ берутся с равным шагом по списку цели).
/// </summary>
public static class FemElementLoadGlyphs
{
    public static FemElementLoadGlyphSet Build(IReadOnlyList<(FemLoadCase LoadCase, double Factor)> terms,
        IReadOnlyList<FemElementLoad> elementLoads, FemLoadMeshContext mesh, int maxArrowsPerLoad = 2000)
    {
        var arrows = new List<FemElementLoadGlyph>();
        var labels = new List<FemElementLoadLabel>();
        var loaded = new HashSet<string>(StringComparer.Ordinal);
        var diagnostics = new List<FemValidationDiagnostic>();
        foreach (var (lc, factor) in terms)
        {
            if (factor == 0) continue;
            if (lc.SelfWeightFactor is { } k && k != 0)
                AddLoad(SelfWeightLoad(k * factor), mesh.Elements, 1);
            foreach (var load in elementLoads.Where(l => l.LoadCaseId == lc.Id))
                AddLoad(load, FemLoadTargets.Resolve(load, mesh, diagnostics), factor);
        }
        return new FemElementLoadGlyphSet(arrows, labels, loaded.ToArray(), diagnostics);

        void AddLoad(FemElementLoad load, IReadOnlyList<FemElement> elements, double factor)
        {
            if (elements.Count == 0) return;
            int step = Math.Max(1, (int)Math.Ceiling(elements.Count / (double)Math.Max(1, maxArrowsPerLoad)));
            bool labelled = false;
            for (int i = 0; i < elements.Count; i++)
            {
                var e = elements[i];
                if (mesh.Geometry(e) is not { } g) continue;
                if (Glyph(load, e, g, mesh, factor) is not { } glyph) continue;
                loaded.Add(e.ElemTag);
                if (!labelled)
                {
                    labels.Add(new FemElementLoadLabel(glyph.Point, glyph.Magnitude, glyph.Unit, load.LoadKind));
                    labelled = true;
                }
                if (i % step == 0) arrows.Add(glyph);
            }
        }
    }

    static FemElementLoad SelfWeightLoad(double k)
    {
        var load = new FemElementLoad { LoadKind = FemElementLoadKinds.SelfWeight };
        load.SetValues([k]);
        return load;
    }

    static FemElementLoadGlyph? Glyph(FemElementLoad load, FemElement e, FemElementGeometry g, FemLoadMeshContext mesh, double factor)
    {
        var v = load.Values;
        double size = g.IsShell ? Math.Sqrt(Math.Max(g.Area, 0)) : g.Length;
        var unit = g.IsShell ? FemLoadGlyphUnit.Pressure : FemLoadGlyphUnit.LineLoad;
        if (load.LoadKind == FemElementLoadKinds.SelfWeight)
        {
            if (v.Count < 1 || FemElementLoadNodalizer.SelfWeightIntensity(e, g, mesh.SelfWeight) is not { } q) return null;
            return Make(g.Centroid, new PlanarVector3(0, 0, -1), q * v[0] * factor, unit);
        }
        if (FemElementLoadNodalizer.Direction(load, g) is not { } dir || v.Count < 1) return null;
        switch (load.LoadKind)
        {
            case FemElementLoadKinds.Uniform:
                return Make(g.Centroid, dir, v[0] * factor, unit);
            case FemElementLoadKinds.Nodal when g.IsShell && v.Count == g.Points.Count:
                return Make(g.Centroid, dir, v.Average() * factor, FemLoadGlyphUnit.Pressure);
            case FemElementLoadKinds.Point when g.IsShell && v.Count >= 3:
            {
                var (ex, ey, _) = g.ShellFrame();
                return Make(g.Points[g.Contour[0]] + ex * v[1] + ey * v[2], dir, v[0] * factor, FemLoadGlyphUnit.Force);
            }
            case FemElementLoadKinds.Point when g.IsBar && v.Count >= 2:
            {
                var axis = (g.Points[1] - g.Points[0]).Normalize();
                return Make(g.Points[0] + axis * v[1], dir, v[0] * factor, FemLoadGlyphUnit.Force);
            }
            default:
                return null;
        }

        FemElementLoadGlyph? Make(PlanarVector3 point, PlanarVector3 direction, double value, FemLoadGlyphUnit u) =>
            value == 0 || !double.IsFinite(value) ? null
                : new FemElementLoadGlyph(e.ElemTag, point, direction * Math.Sign(value), Math.Abs(value), u, size);
    }
}
